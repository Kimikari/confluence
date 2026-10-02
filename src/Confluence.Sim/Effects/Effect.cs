using System;
using System.Collections.Generic;
using Confluence.Sim.Players;

namespace Confluence.Sim.Effects
{
    /// <summary>Whom an effect's actions affect (spec L433 "target filter").</summary>
    public enum EffectTarget
    {
        Self,
        Opponent,

        /// <summary>The trigger's counterpart (the attacker of a hit taken, the target of a hit dealt).</summary>
        TriggerCounterpart,
    }

    /// <summary>What a repeated activation does to the effect's timed outputs (spec L433 "stacking rule").</summary>
    public enum StackingRule
    {
        /// <summary>One instance; a new activation restarts its duration.</summary>
        Refresh,

        /// <summary>Independent instances up to <see cref="Effect.MaxStacks"/>; at the maximum the oldest is refreshed.</summary>
        Stack,

        /// <summary>A new activation removes the old instance and applies fresh values.</summary>
        Replace,

        /// <summary>While an instance is active a new activation does nothing.</summary>
        KeepExisting,
    }

    /// <summary>
    /// A triggered effect (spec §14 data model L433): trigger, filter (target filter), condition, actions (magnitude,
    /// channel), target, budget group, stacking rule, internal cooldown, activation limit, and permitted secondary
    /// events. Every activation also passes the global proc budget: once per 0.5 s and once per original target-hit
    /// event (spec L126), keyed by the budget group. Effects fire only on original (generation 0) triggers; what their
    /// actions produce is the one permitted generation of secondary effects and triggers nothing further.
    /// </summary>
    public sealed class Effect : ISimEffect
    {
        private SecondaryEvents _permitted = SecondaryEvents.None;
        private bool _permittedSet;

        public Effect(string sourceId, EffectTrigger trigger)
        {
            if (string.IsNullOrEmpty(sourceId))
            {
                throw new ArgumentException("An effect needs its catalogue source id.", nameof(sourceId));
            }

            if (trigger == EffectTrigger.None)
            {
                throw new ArgumentException("A triggered effect needs a trigger; use ConditionalModifier for passive conditions.", nameof(trigger));
            }

            SourceId = sourceId;
            Trigger = trigger;
        }

        public string SourceId { get; }

        /// <summary>Readable part name for the inspector ("drawback", "rank 2 shield").</summary>
        public string Label { get; set; }

        public EffectTrigger Trigger { get; }
        public EffectFilter Filter { get; } = new EffectFilter();
        public IEffectCondition Condition { get; set; } = Conditions.Always;
        public List<IEffectAction> Actions { get; } = new List<IEffectAction>();
        public EffectTarget Target { get; set; } = EffectTarget.Self;

        /// <summary>Proc budget group (spec L126); effects in one group share the budget. Null: the effect's own key.</summary>
        public string BudgetGroup { get; set; }

        public StackingRule Stacking { get; set; } = StackingRule.Refresh;
        public int MaxStacks { get; set; } = 1;

        /// <summary>The record's internal cooldown ("4 s internal cooldown"); the 0.5 s budget applies regardless.</summary>
        public int InternalCooldownTicks { get; set; }

        /// <summary>Activations allowed per duel ("once per duel", "first paid cast each duel"); 0 = unlimited.</summary>
        public int MaxActivations { get; set; }

        /// <summary>Interval of a <see cref="EffectTrigger.Periodic"/> effect in combat ticks.</summary>
        public int PeriodTicks { get; set; }

        /// <summary>Secondary events the record permits; defaults to exactly what the actions produce.</summary>
        public SecondaryEvents PermittedSecondary
        {
            get => _permittedSet ? _permitted : Produced;
            set
            {
                _permitted = value;
                _permittedSet = true;
            }
        }

        /// <summary>Union of what the actions produce.</summary>
        public SecondaryEvents Produced
        {
            get
            {
                SecondaryEvents all = SecondaryEvents.None;
                for (int i = 0; i < Actions.Count; i++)
                {
                    all |= Actions[i] == null ? SecondaryEvents.None : Actions[i].Produces;
                }

                return all;
            }
        }

        /// <summary>Host-assigned unique key (source id and attach order), set when attached.</summary>
        public string Key { get; internal set; }

        /// <summary>The budget group actually used.</summary>
        public string EffectiveBudgetGroup => BudgetGroup ?? Key;

        public Effect With(IEffectAction action)
        {
            Actions.Add(action ?? throw new ArgumentNullException(nameof(action)));
            return this;
        }

        internal void Validate()
        {
            if (Actions.Count == 0)
            {
                throw new InvalidOperationException(SourceId + ": a triggered effect needs at least one action.");
            }

            for (int i = 0; i < Actions.Count; i++)
            {
                if (Actions[i] == null)
                {
                    throw new InvalidOperationException(SourceId + ": null action.");
                }

                if (Actions[i] is GrantShieldAction shield && shield.Reserve && Trigger != EffectTrigger.RoundStart)
                {
                    throw new InvalidOperationException(SourceId + ": reserve shields are issued only at round start (spec L114, L579).");
                }
            }

            SecondaryEvents extra = Produced & ~PermittedSecondary;
            if (extra != SecondaryEvents.None)
            {
                throw new InvalidOperationException(SourceId + ": actions produce " + extra + " that the effect does not permit.");
            }

            if (Trigger == EffectTrigger.Periodic && PeriodTicks <= 0)
            {
                throw new InvalidOperationException(SourceId + ": a periodic effect needs a positive period.");
            }

            if (MaxStacks < 1)
            {
                throw new InvalidOperationException(SourceId + ": MaxStacks must be at least 1.");
            }

            if (Condition == null)
            {
                throw new InvalidOperationException(SourceId + ": null condition.");
            }
        }

        public override string ToString() => SourceId + " on " + Trigger + (Label == null ? "" : " (" + Label + ")");
    }

    /// <summary>
    /// A stat modifier present only while its condition holds ("Airborne incoming damage increases 2%"). Conditions
    /// are evaluated at the effect dispatch point once per tick (tick-order step 7), so a change applies from the
    /// next tick's resolution.
    /// </summary>
    public sealed class ConditionalModifier : ISimEffect
    {
        public ConditionalModifier(IEffectCondition condition, StatModifier modifier)
        {
            Condition = condition ?? throw new ArgumentNullException(nameof(condition));
            if (string.IsNullOrEmpty(modifier.SourceId))
            {
                throw new ArgumentException("The modifier needs its catalogue source id.", nameof(modifier));
            }

            Modifier = modifier;
        }

        public string SourceId => Modifier.SourceId;
        public IEffectCondition Condition { get; }
        public StatModifier Modifier { get; }

        /// <summary>Host-assigned key, used as the stat-block source while the modifier is present.</summary>
        public string Key { get; internal set; }

        /// <summary>Whether the modifier is currently in the stat block.</summary>
        public bool Present { get; internal set; }
    }
}
