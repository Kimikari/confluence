using System;
using System.Collections.Generic;
using Confluence.Core.Rules;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Hashing;
using Confluence.Sim.Players;
using Confluence.Sim.Stats;

namespace Confluence.Sim.Effects
{
    /// <summary>
    /// One player's attached effects and their runtime state: activation counts and cooldowns, timed stat buffs with
    /// their stacking instances, conditional modifiers, and pending next-cast and next-hit bonuses. Content behaviors
    /// attach here at duel construction (<see cref="IEffectHost"/>); the <see cref="EffectSystem"/> runs them.
    /// </summary>
    public sealed class EffectHost : IEffectHost
    {
        private readonly List<EffectRuntime> _effects = new List<EffectRuntime>();
        private readonly List<ConditionalModifier> _conditional = new List<ConditionalModifier>();
        private readonly List<BuffInstance> _buffs = new List<BuffInstance>();
        private readonly List<CastCostBonus> _castBonuses = new List<CastCostBonus>();
        private readonly List<HitBonus> _hitBonuses = new List<HitBonus>();
        private int _attachSerial;
        private int _bonusSerial;

        internal EffectHost(EffectSystem system, int player)
        {
            System = system;
            Player = player;
        }

        public EffectSystem System { get; }
        public int Player { get; }
        public DuelWorld World => System.World;
        public StatBlock Stats => World.Player(Player).Stats;

        public int EffectCount => _effects.Count;

        /// <summary>Attached triggered effects in attach order.</summary>
        public IEnumerable<Effect> Effects
        {
            get
            {
                for (int i = 0; i < _effects.Count; i++)
                {
                    yield return _effects[i].Effect;
                }
            }
        }

        public IReadOnlyList<ConditionalModifier> ConditionalModifiers => _conditional;

        /// <summary>Activations of an effect so far.</summary>
        public int ActivationCount(Effect effect)
        {
            EffectRuntime r = Find(effect);
            return r == null ? 0 : r.Activations;
        }

        /// <summary>Live stacking instances of an effect's timed modifiers.</summary>
        public int BuffInstances(Effect effect)
        {
            int n = 0;
            for (int i = 0; i < _buffs.Count; i++)
            {
                if (_buffs[i].Key == effect.Key)
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>Pending next-cast cost bonuses (active or delayed).</summary>
        public int PendingCastBonuses => _castBonuses.Count;

        /// <summary>Pending next-hit damage bonuses.</summary>
        public int PendingHitBonuses => _hitBonuses.Count;

        // ----- attach API (IEffectHost) -----------------------------------------------------------------------

        public void AddModifier(StatModifier modifier)
        {
            if (string.IsNullOrEmpty(modifier.SourceId))
            {
                throw new ArgumentException("A modifier needs its catalogue source id.", nameof(modifier));
            }

            Stats.Add(modifier);
        }

        public void Attach(ISimEffect effect)
        {
            switch (effect)
            {
                case Effect triggered:
                    triggered.Validate();
                    if (triggered.Key != null)
                    {
                        throw new InvalidOperationException(triggered + " is already attached.");
                    }

                    triggered.Key = triggered.SourceId + "#" + (++_attachSerial);
                    _effects.Add(new EffectRuntime(triggered));
                    break;
                case ConditionalModifier conditional:
                    if (conditional.Key != null)
                    {
                        throw new InvalidOperationException(conditional.SourceId + " conditional modifier is already attached.");
                    }

                    conditional.Key = conditional.SourceId + "#" + (++_attachSerial);
                    _conditional.Add(conditional);
                    break;
                case null:
                    throw new ArgumentNullException(nameof(effect));
                default:
                    throw new ArgumentException("Unsupported effect type " + effect.GetType().FullName + ".", nameof(effect));
            }
        }

        // ----- activation -------------------------------------------------------------------------------------

        /// <summary>Runs every effect matching the trigger (attach order). Called by the system for generation 0 triggers.</summary>
        internal void Fire(in TriggerEvent trigger)
        {
            for (int i = 0; i < _effects.Count; i++)
            {
                EffectRuntime r = _effects[i];
                Effect e = r.Effect;
                if (e.Trigger != trigger.Trigger || !e.Filter.Matches(trigger))
                {
                    continue;
                }

                if (e.Trigger == EffectTrigger.Periodic && (e.PeriodTicks <= 0 || World.CombatTick % e.PeriodTicks != 0))
                {
                    continue;
                }

                int now = World.Tick;
                if (e.MaxActivations > 0 && r.Activations >= e.MaxActivations)
                {
                    continue;
                }

                if (e.InternalCooldownTicks > 0 && now - r.LastActivationTick < e.InternalCooldownTicks)
                {
                    continue;
                }

                var context = new EffectContext(System, this, e, trigger);
                if (!e.Condition.Holds(context))
                {
                    continue;
                }

                // spec L126: once per 0.5 s and once per original target-hit event, per budget group.
                int origin = trigger.OriginEventId != 0 ? trigger.OriginEventId : trigger.EventId != 0 ? trigger.EventId : System.NextSyntheticOrigin();
                int target = trigger.Other >= 0 ? trigger.Other : Player;
                if (!World.Procs.TryTrigger(Player, e.EffectiveBudgetGroup, origin, target, now))
                {
                    continue;
                }

                r.Activations++;
                r.LastActivationTick = now;
                System.EmitActivation(Player, e, trigger);
                for (int a = 0; a < e.Actions.Count; a++)
                {
                    e.Actions[a].Apply(context);
                }
            }
        }

        // ----- timed outputs ----------------------------------------------------------------------------------

        /// <summary>Applies a timed stat buff under the effect's stacking rule; 0 duration lasts the duel.</summary>
        internal void ApplyBuff(Effect effect, IReadOnlyList<StatModifier> modifiers, int durationTicks)
        {
            int now = World.Tick;
            int expire = durationTicks > 0 ? now + durationTicks : 0;
            var existing = new List<BuffInstance>();
            for (int i = 0; i < _buffs.Count; i++)
            {
                if (_buffs[i].Key == effect.Key)
                {
                    existing.Add(_buffs[i]);
                }
            }

            switch (effect.Stacking)
            {
                case StackingRule.KeepExisting:
                    if (existing.Count > 0)
                    {
                        return;
                    }

                    break;
                case StackingRule.Refresh:
                    if (existing.Count > 0)
                    {
                        for (int i = 0; i < existing.Count; i++)
                        {
                            Renew(existing[i], expire);
                        }

                        return;
                    }

                    break;
                case StackingRule.Replace:
                    for (int i = 0; i < existing.Count; i++)
                    {
                        Stats.RemoveSource(existing[i].StatSource);
                        _buffs.Remove(existing[i]);
                    }

                    break;
                case StackingRule.Stack:
                    if (existing.Count >= effect.MaxStacks)
                    {
                        Renew(existing[0], expire); // at the maximum the oldest instance is refreshed
                        return;
                    }

                    break;
            }

            var buff = new BuffInstance { Key = effect.Key, Serial = ++_bonusSerial, ExpireTick = expire };
            buff.StatSource = effect.Key + ":" + buff.Serial;
            for (int i = 0; i < modifiers.Count; i++)
            {
                StatModifier m = modifiers[i];
                Stats.Add(new StatModifier(m.Stat, m.Value, buff.StatSource, m.Channel, expire));
            }

            _buffs.Add(buff);
        }

        private void Renew(BuffInstance buff, int expire)
        {
            if (expire == 0 || buff.ExpireTick == 0)
            {
                return; // a duel-long instance has nothing to refresh
            }

            buff.ExpireTick = expire;
            Stats.SetExpiry(buff.StatSource, expire);
        }

        internal void AddCastBonus(Effect effect, float flat, float fraction, int windowTicks, int delayTicks, CastFilter filter)
        {
            int now = World.Tick;
            var bonus = new CastCostBonus
            {
                Key = effect.Key,
                SourceId = effect.SourceId,
                Flat = flat,
                Fraction = fraction,
                ActiveFromTick = now + delayTicks,
                ExpireTick = windowTicks > 0 ? now + delayTicks + windowTicks : int.MaxValue,
                Filter = filter,
                Serial = ++_bonusSerial,
            };
            PlacePending(_castBonuses, effect, bonus, b => b.Key);
        }

        internal void AddHitBonus(Effect effect, float fraction, float flat, int windowTicks, HitFilter filter)
        {
            int now = World.Tick;
            var bonus = new HitBonus
            {
                Key = effect.Key,
                SourceId = effect.SourceId,
                Fraction = fraction,
                Flat = flat,
                ExpireTick = windowTicks > 0 ? now + windowTicks : int.MaxValue,
                Filter = filter,
                Serial = ++_bonusSerial,
            };
            PlacePending(_hitBonuses, effect, bonus, b => b.Key);
        }

        // Pending bonuses: Refresh and Replace keep one per effect (the newest), Stack keeps up to MaxStacks, and
        // KeepExisting ignores a new activation while one is pending.
        private static void PlacePending<T>(List<T> list, Effect effect, T bonus, Func<T, string> keyOf)
        {
            int count = 0;
            int oldest = -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (keyOf(list[i]) == effect.Key)
                {
                    count++;
                    if (oldest < 0)
                    {
                        oldest = i;
                    }
                }
            }

            switch (effect.Stacking)
            {
                case StackingRule.KeepExisting:
                    if (count > 0)
                    {
                        return;
                    }

                    break;
                case StackingRule.Stack:
                    if (count >= effect.MaxStacks)
                    {
                        list.RemoveAt(oldest);
                    }

                    break;
                default:
                    list.RemoveAll(b => keyOf(b) == effect.Key);
                    break;
            }

            list.Add(bonus);
        }

        /// <summary>Requested reduction (positive) and increase (positive) of the pending bonuses matching a cast.</summary>
        internal void SumCastBonuses(in CastQuote quote, int now, out float reduction, out float increase)
        {
            reduction = 0f;
            increase = 0f;
            float cost = Math.Max(0f, quote.BaseCost);
            for (int i = 0; i < _castBonuses.Count; i++)
            {
                CastCostBonus b = _castBonuses[i];
                if (now < b.ActiveFromTick || now >= b.ExpireTick || !b.Filter.Matches(quote))
                {
                    continue;
                }

                float amount = b.Flat + b.Fraction * cost;
                if (amount >= 0f)
                {
                    reduction += amount;
                }
                else
                {
                    increase -= amount;
                }
            }
        }

        /// <summary>Consumes the bonuses that priced a cast ("next cast").</summary>
        internal void ConsumeCastBonuses(in CastQuote quote, int now)
        {
            for (int i = _castBonuses.Count - 1; i >= 0; i--)
            {
                CastCostBonus b = _castBonuses[i];
                if (now >= b.ActiveFromTick && now < b.ExpireTick && b.Filter.Matches(quote))
                {
                    _castBonuses.RemoveAt(i);
                }
            }
        }

        /// <summary>Applies and consumes the pending bonuses matching an original hit. Returns the multiplier and flat add.</summary>
        internal void ConsumeHitBonuses(DamageRequest hit, float range, int now, out float fraction, out float flat)
        {
            fraction = 0f;
            flat = 0f;
            for (int i = 0; i < _hitBonuses.Count; i++)
            {
                HitBonus b = _hitBonuses[i];
                if (now < b.ExpireTick && b.Filter.Matches(hit, range))
                {
                    fraction += b.Fraction;
                    flat += b.Flat;
                    _hitBonuses.RemoveAt(i);
                    i--;
                }
            }
        }

        /// <summary>Removes ended buffs (their modifiers leave the stat block) and lapsed bonuses.</summary>
        internal void ExpireTimed(int now)
        {
            _buffs.RemoveAll(b => b.ExpireTick > 0 && b.ExpireTick <= now);
            Stats.Expire(now);
            _castBonuses.RemoveAll(b => b.ExpireTick <= now);
            _hitBonuses.RemoveAll(b => b.ExpireTick <= now);
        }

        /// <summary>Adds or removes each conditional modifier to match its condition.</summary>
        internal void RefreshConditional()
        {
            for (int i = 0; i < _conditional.Count; i++)
            {
                ConditionalModifier c = _conditional[i];
                bool holds = c.Condition.Holds(new EffectContext(System, this, null, default));
                if (holds == c.Present)
                {
                    continue;
                }

                if (holds)
                {
                    StatModifier m = c.Modifier;
                    Stats.Add(new StatModifier(m.Stat, m.Value, c.Key, m.Channel));
                }
                else
                {
                    Stats.RemoveSource(c.Key);
                }

                c.Present = holds;
            }
        }

        public void Hash(ref StateHasher h)
        {
            h.Add(_effects.Count);
            for (int i = 0; i < _effects.Count; i++)
            {
                h.Add(_effects[i].Effect.Key);
                h.Add(_effects[i].Activations);
                h.Add(_effects[i].LastActivationTick);
            }

            h.Add(_conditional.Count);
            for (int i = 0; i < _conditional.Count; i++)
            {
                h.Add(_conditional[i].Present);
            }

            h.Add(_buffs.Count);
            for (int i = 0; i < _buffs.Count; i++)
            {
                h.Add(_buffs[i].StatSource);
                h.Add(_buffs[i].ExpireTick);
            }

            h.Add(_castBonuses.Count);
            for (int i = 0; i < _castBonuses.Count; i++)
            {
                CastCostBonus b = _castBonuses[i];
                h.Add(b.Key);
                h.Add(b.Flat);
                h.Add(b.Fraction);
                h.Add(b.ActiveFromTick);
                h.Add(b.ExpireTick);
            }

            h.Add(_hitBonuses.Count);
            for (int i = 0; i < _hitBonuses.Count; i++)
            {
                HitBonus b = _hitBonuses[i];
                h.Add(b.Key);
                h.Add(b.Fraction);
                h.Add(b.Flat);
                h.Add(b.ExpireTick);
            }
        }

        private EffectRuntime Find(Effect effect)
        {
            for (int i = 0; i < _effects.Count; i++)
            {
                if (ReferenceEquals(_effects[i].Effect, effect))
                {
                    return _effects[i];
                }
            }

            return null;
        }

        private sealed class EffectRuntime
        {
            public EffectRuntime(Effect effect)
            {
                Effect = effect;
            }

            public Effect Effect { get; }
            public int Activations;
            public int LastActivationTick = int.MinValue / 2;
        }

        private sealed class BuffInstance
        {
            public string Key;
            public int Serial;
            public string StatSource;

            /// <summary>0 for a duel-long instance.</summary>
            public int ExpireTick;
        }
    }
}
