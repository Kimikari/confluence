using System;
using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Core.Rules;
using Confluence.Sim.Combat;
using Confluence.Sim.Hashing;
using Confluence.Sim.Movement;

namespace Confluence.Sim.Effects
{
    /// <summary>The player's attached <see cref="EffectHook"/>s (see EffectHooks.cs), in attach order.</summary>
    public sealed partial class EffectHost
    {
        private readonly List<EffectHook> _hooks = new List<EffectHook>();

        public IReadOnlyList<EffectHook> Hooks => _hooks;

        private void AttachHook(EffectHook hook)
        {
            if (hook.Host != null)
            {
                throw new InvalidOperationException(hook + " is already attached.");
            }

            hook.Host = this;
            hook.Key = hook.SourceId + "#" + (++_attachSerial);
            _hooks.Add(hook);
            World.AddEventObserver(hook);
        }

        internal void TickHooks()
        {
            for (int i = 0; i < _hooks.Count; i++)
            {
                _hooks[i].OnTick();
            }
        }

        internal void AdjustMotorHooks(ref MotorCommand command)
        {
            for (int i = 0; i < _hooks.Count; i++)
            {
                _hooks[i].AdjustMotor(ref command);
            }

            // Gait requests add up and the +20% cap applies once, whatever the attach order (HookAggregates.cs).
            var totals = new GaitTotals();
            for (int i = 0; i < _hooks.Count; i++)
            {
                var gait = new GaitAdjust();
                _hooks[i].AdjustGait(command, ref gait);
                totals.Add(gait);
            }

            if (totals.Any)
            {
                // Only the hooks' change is applied on top of the step's multipliers (the stat block's gait, plus any
                // edit made before this point), so nothing else written to the command is discarded.
                Stats.StatBlock stats = Stats;
                command.Tuning.WalkMultiplier = Math.Max(0f, command.Tuning.WalkMultiplier + totals.WalkMultiplier(stats) - (1f + stats.WalkBonus));
                command.Tuning.SprintMultiplier = Math.Max(0f, command.Tuning.SprintMultiplier + totals.SprintMultiplier(stats) - (1f + stats.SprintBonus));
            }
        }

        internal MeleeProfile AdjustStrikeHooks(MeleeProfile profile)
        {
            for (int i = 0; i < _hooks.Count; i++)
            {
                profile = _hooks[i].AdjustStrike(profile);
            }

            return profile;
        }

        internal void AdjustCastPriceHooks(in CastQuote quote, ref float reduction, ref float increase)
        {
            for (int i = 0; i < _hooks.Count; i++)
            {
                float r = 0f;
                float inc = 0f;
                _hooks[i].AdjustCastPrice(quote, ref r, ref inc);
                reduction += Math.Max(0f, r);
                increase += Math.Max(0f, inc);
            }
        }

        internal void AdjustMasteryPriceHooks(in CastQuote quote, ref float mastered, ref float normal)
        {
            for (int i = 0; i < _hooks.Count; i++)
            {
                float m = 0f;
                float n = 0f;
                _hooks[i].AdjustMasteryPrice(quote, ref m, ref n);
                mastered += Math.Max(0f, m);
                normal += Math.Max(0f, n);
            }
        }

        internal float AdjustProjectileSpeedHooks(Spells.CastRecord cast, float speed)
        {
            float linked = 0f;
            for (int i = 0; i < _hooks.Count; i++)
            {
                linked += Math.Max(0f, _hooks[i].LinkedProjectileSpeed(cast));
            }

            speed *= MasteryLinked.SpeedFactor(cast, linked); // one clamp per cast (spec L480, HookAggregates.cs)
            for (int i = 0; i < _hooks.Count; i++)
            {
                speed = Math.Max(0f, _hooks[i].AdjustProjectileSpeed(cast, speed));
            }

            return speed;
        }

        internal void AdjustCastTimingHooks(in CastQuote quote, ref CastTimingAdjust timing)
        {
            for (int i = 0; i < _hooks.Count; i++)
            {
                _hooks[i].AdjustCastTiming(quote, ref timing);
            }
        }

        internal void AdjustOutgoingHooks(DamageRequest hit, float range, ref float fraction, ref float flat)
        {
            if (_hooks.Count == 0) return;
            Spells.CastRecord cast = hit.CastId.IsValid && World.Spells is Spells.SpellRuntime runtime ? runtime.FindCast(hit.CastId) : null;
            float linked = 0f;
            for (int i = 0; i < _hooks.Count; i++)
            {
                linked += Math.Max(0f, _hooks[i].LinkedDirectDamage(hit, cast));
            }

            fraction += MasteryLinked.DamageFraction(cast, linked); // one clamp per cast (spec L480, HookAggregates.cs)
            for (int i = 0; i < _hooks.Count; i++)
            {
                _hooks[i].AdjustOutgoing(hit, range, ref fraction, ref flat);
            }
        }

        internal bool BindFieldPlacementHooks(Spells.CastRecord cast, out Core.Geometry.Vec3 ground, out Core.Geometry.Vec3 direction)
        {
            for (int i = 0; i < _hooks.Count; i++)
            {
                if (_hooks[i].BindFieldPlacement(cast, out ground, out direction))
                {
                    return true;
                }
            }

            ground = default;
            direction = default;
            return false;
        }

        internal float AdjustSolidDamageHooks(int solidId, float amount, string sourceId, CastEventId castId)
        {
            for (int i = 0; i < _hooks.Count; i++)
            {
                amount = Math.Max(0f, _hooks[i].AdjustSolidDamage(solidId, amount, sourceId, castId));
            }

            return amount;
        }

        private void HashHooks(ref StateHasher h)
        {
            h.Add(_hooks.Count);
            for (int i = 0; i < _hooks.Count; i++)
            {
                h.Add(_hooks[i].Key);
                _hooks[i].Hash(ref h);
            }
        }
    }
}
