using System;
using System.Collections.Generic;
using Confluence.Core.Rules;
using Confluence.Sim.Hashing;

namespace Confluence.Sim.Status
{
    /// <summary>A request to apply a status.</summary>
    public struct StatusApplication
    {
        public StatusKind Kind;

        /// <summary>Catalogue ID of the source definition; same-definition applications refresh (spec L118).</summary>
        public string DefinitionId;

        /// <summary>Applying player, or -1 for the world.</summary>
        public int SourcePlayer;

        public int DurationTicks;

        /// <summary>DoT damage per second, or slow fraction; otherwise the source's own magnitude.</summary>
        public float Intensity;

        public DamageChannel Channel;
        public int OriginEventId;

        public static StatusApplication Of(StatusKind kind, string definitionId, int sourcePlayer, int durationTicks, float intensity = 0f)
        {
            return new StatusApplication
            {
                Kind = kind,
                DefinitionId = definitionId,
                SourcePlayer = sourcePlayer,
                DurationTicks = durationTicks,
                Intensity = intensity,
            };
        }
    }

    /// <summary>One active status instance.</summary>
    public sealed class StatusInstance
    {
        internal StatusInstance(int id, in StatusApplication app, int now, int expireTick, float intensity)
        {
            Id = id;
            Kind = app.Kind;
            DefinitionId = app.DefinitionId;
            SourcePlayer = app.SourcePlayer;
            Channel = app.Channel;
            OriginEventId = app.OriginEventId;
            AppliedTick = now;
            ExpireTick = expireTick;
            Intensity = intensity;
            NextPulseTick = now + StatusRules.DotIntervalTicks;
        }

        public int Id { get; }
        public StatusKind Kind { get; }
        public string DefinitionId { get; }
        public int SourcePlayer { get; }
        public DamageChannel Channel { get; }
        public int OriginEventId { get; }
        public int AppliedTick { get; }
        public int ExpireTick { get; internal set; }
        public float Intensity { get; internal set; }
        public int NextPulseTick { get; internal set; }

        public StatusRule Rule => StatusRules.Get(Kind);

        public int RemainingTicks(int now) => Math.Max(0, ExpireTick - now);
    }

    public enum StatusOutcome
    {
        Applied,
        Refreshed,

        /// <summary>Hard control during the 2 s immunity: nothing applied or queued (spec L116).</summary>
        BlockedByImmunity,

        /// <summary>The continuous hard-control window was already spent.</summary>
        BlockedByControlWindow,

        Rejected,
    }

    public struct StatusApplyResult
    {
        public StatusOutcome Outcome;
        public StatusInstance Instance;

        public bool Succeeded => Outcome == StatusOutcome.Applied || Outcome == StatusOutcome.Refreshed;
    }

    /// <summary>A damage-over-time pulse produced during the status step.</summary>
    public struct DotPulse
    {
        public StatusInstance Status;
        public float Amount;
    }

    /// <summary>
    /// Active statuses of one player (spec L116-L120), kept in application order. Hard control: one application
    /// lasts at most 0.75 s (silence 0.6 s); a continuous hard-control window lasts at most 0.75 s; after the last
    /// hard control ends the player is immune for 2 s, and attempts then apply nothing and are not queued.
    /// Slows: only the strongest applies, at most 30% and 3 s. Same-definition DoT refreshes duration and keeps the
    /// strongest intensity; different definitions coexist. Dispel removes the oldest removable hostile status.
    /// Control immunity is not a status and cannot be dispelled.
    /// </summary>
    public sealed class StatusSet
    {
        private readonly List<StatusInstance> _active = new List<StatusInstance>();
        private int _nextId = 1;
        private int _controlWindowStart = int.MinValue;

        /// <summary>Statuses in application order.</summary>
        public IReadOnlyList<StatusInstance> Active => _active;

        /// <summary>Hard control is refused before this tick (spec L116).</summary>
        public int ControlImmuneUntil { get; private set; } = int.MinValue;

        public bool IsControlImmune(int now) => now < ControlImmuneUntil;

        public StatusApplyResult Apply(in StatusApplication app, int now)
        {
            StatusRule rule = StatusRules.Get(app.Kind);
            if (app.DurationTicks <= 0)
            {
                return new StatusApplyResult { Outcome = StatusOutcome.Rejected };
            }

            int duration = Math.Min(app.DurationTicks, rule.MaxTicks);
            int expire = now + duration;
            bool controlActive = rule.HardControl && HasHardControl();
            if (rule.HardControl)
            {
                if (IsControlImmune(now))
                {
                    return new StatusApplyResult { Outcome = StatusOutcome.BlockedByImmunity };
                }

                if (controlActive)
                {
                    expire = Math.Min(expire, _controlWindowStart + StatusRules.HardControlWindowTicks);
                    if (expire <= now)
                    {
                        return new StatusApplyResult { Outcome = StatusOutcome.BlockedByControlWindow };
                    }
                }
            }

            float intensity = Math.Max(0f, Math.Min(app.Intensity, rule.MaxIntensity));
            StatusInstance existing = Find(app.Kind, app.DefinitionId);
            if (existing != null)
            {
                existing.ExpireTick = Math.Max(existing.ExpireTick, expire);
                existing.Intensity = Math.Max(existing.Intensity, intensity);
                return new StatusApplyResult { Outcome = StatusOutcome.Refreshed, Instance = existing };
            }

            if (rule.HardControl && !controlActive)
            {
                _controlWindowStart = now;
            }

            var instance = new StatusInstance(_nextId++, app, now, expire, intensity);
            _active.Add(instance);
            return new StatusApplyResult { Outcome = StatusOutcome.Applied, Instance = instance };
        }

        /// <summary>
        /// DoT pulses due at now (each pulse deals intensity per second times the 0.5 s interval), then expiry of
        /// statuses whose time ended. Appends pulses and expired instances in application order.
        /// </summary>
        public void Tick(int now, List<DotPulse> pulses, List<StatusInstance> expired)
        {
            float interval = StatusRules.DotIntervalTicks / (float)Confluence.Core.Time.Ticks.PerSecond;
            for (int i = 0; i < _active.Count; i++)
            {
                StatusInstance s = _active[i];
                if (!s.Rule.DamageOverTime)
                {
                    continue;
                }

                while (s.NextPulseTick <= now && s.NextPulseTick <= s.ExpireTick)
                {
                    pulses?.Add(new DotPulse { Status = s, Amount = s.Intensity * interval });
                    s.NextPulseTick += StatusRules.DotIntervalTicks;
                }
            }

            RemoveWhere(s => s.ExpireTick <= now, now, expired);
        }

        /// <summary>
        /// Removes the oldest removable hostile status (or the oldest matching the explicit selection). Never touches
        /// control immunity, boundary rules, or terrain (spec L118). Returns the removed status or null.
        /// </summary>
        public StatusInstance Dispel(int now, StatusKind? kind = null, string definitionId = null)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                StatusInstance s = _active[i];
                if (!s.Rule.Removable || !s.Rule.Harmful)
                {
                    continue;
                }

                if (kind.HasValue && s.Kind != kind.Value)
                {
                    continue;
                }

                if (definitionId != null && !string.Equals(definitionId, s.DefinitionId, StringComparison.Ordinal))
                {
                    continue;
                }

                _active.RemoveAt(i);
                OnRemoved(s, now);
                return s;
            }

            return null;
        }

        /// <summary>Removes every instance of a kind (invisibility broken, displacement stopped). Appends them to removed.</summary>
        public int RemoveKind(StatusKind kind, int now, List<StatusInstance> removed)
        {
            return RemoveWhere(s => s.Kind == kind, now, removed);
        }

        public bool Has(StatusKind kind)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                if (_active[i].Kind == kind)
                {
                    return true;
                }
            }

            return false;
        }

        public StatusInstance Find(StatusKind kind, string definitionId)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                StatusInstance s = _active[i];
                if (s.Kind == kind && string.Equals(s.DefinitionId, definitionId, StringComparison.Ordinal))
                {
                    return s;
                }
            }

            return null;
        }

        public bool HasHardControl()
        {
            for (int i = 0; i < _active.Count; i++)
            {
                if (_active[i].Rule.HardControl)
                {
                    return true;
                }
            }

            return false;
        }

        public bool IsStunned => Has(StatusKind.Stun);
        public bool IsRooted => Has(StatusKind.Root);
        public bool IsSilenced => Has(StatusKind.Silence);
        public bool IsDisplaced => Has(StatusKind.Displaced);
        public bool IsWet => Has(StatusKind.Wet);
        public bool IsInvisible => Has(StatusKind.Invisibility);
        public bool IsRevealed => Has(StatusKind.Reveal);

        /// <summary>The strongest active slow (only it applies, at most 30%, spec L116).</summary>
        public float StrongestSlow
        {
            get
            {
                float best = 0f;
                for (int i = 0; i < _active.Count; i++)
                {
                    if (_active[i].Kind == StatusKind.Slow && _active[i].Intensity > best)
                    {
                        best = _active[i].Intensity;
                    }
                }

                return Math.Min(best, GlobalRules.Control.SlowCap);
            }
        }

        public void Clear()
        {
            _active.Clear();
            ControlImmuneUntil = int.MinValue;
            _controlWindowStart = int.MinValue;
        }

        public void Hash(ref StateHasher h)
        {
            h.Add(_active.Count);
            for (int i = 0; i < _active.Count; i++)
            {
                StatusInstance s = _active[i];
                h.Add(s.Id);
                h.Add((int)s.Kind);
                h.Add(s.DefinitionId);
                h.Add(s.SourcePlayer);
                h.Add(s.ExpireTick);
                h.Add(s.Intensity);
                h.Add(s.NextPulseTick);
            }

            h.Add(ControlImmuneUntil);
            h.Add(_controlWindowStart);
        }

        private int RemoveWhere(Predicate<StatusInstance> match, int now, List<StatusInstance> removed)
        {
            int count = 0;
            for (int i = 0; i < _active.Count;)
            {
                StatusInstance s = _active[i];
                if (!match(s))
                {
                    i++;
                    continue;
                }

                _active.RemoveAt(i);
                removed?.Add(s);
                OnRemoved(s, now);
                count++;
            }

            return count;
        }

        // After the last hard control ends, start the 2 s immunity (spec L116).
        private void OnRemoved(StatusInstance s, int now)
        {
            if (s.Rule.HardControl && !HasHardControl())
            {
                int end = Math.Min(now, s.ExpireTick);
                ControlImmuneUntil = Math.Max(ControlImmuneUntil, end + StatusRules.ControlImmunityTicks);
                _controlWindowStart = int.MinValue;
            }
        }
    }
}
