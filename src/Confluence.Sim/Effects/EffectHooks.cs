using System;
using Confluence.Casting;
using Confluence.Core.Rules;
using Confluence.Sim.Combat;
using Confluence.Sim.Content;
using Confluence.Sim.Duel;
using Confluence.Sim.Hashing;
using Confluence.Sim.Movement;
using Confluence.Sim.Players;

namespace Confluence.Sim.Effects
{
    /// <summary>
    /// A stateful content hook (spec L433 "condition", "magnitude"): the part of a record's behavior that a single
    /// trigger and a pure condition cannot express, because its condition spans several events ("perfect guard, a
    /// weapon hit, then a different spell within 2 s"), needs per-tick history ("walk without sprinting for 1 s"), or
    /// has to adjust a value at the exact moment a rule computes it (a strike's stamina at strike begin, a dodge's cost
    /// at the motor step, a cast's price and timing at commitment, a hit's damage before the pipeline, a spell's
    /// durability damage to a solid). One instance per player per duel, attached with <see cref="IEffectHost.Attach"/>.
    /// <para>
    /// Rules for hooks: <see cref="OnEvent"/> sees every duel event as it is emitted and may only record facts — it
    /// never changes the world. <see cref="OnTick"/> runs once per tick at the effect dispatch point (end of tick-order
    /// step 7, before the tick's triggers fire) and may act; outputs that are effect activations pass the proc budget
    /// (spec L126) through <see cref="TryActivate"/>. The adjust methods are pure functions of the hook's recorded
    /// state and the arguments; global caps still apply where the rule computes the value (cost floor, stat caps,
    /// shield cap, mastery clamps).
    /// </para>
    /// </summary>
    public abstract class EffectHook : ISimEffect, IDuelEventObserver
    {
        protected EffectHook(string sourceId)
        {
            if (string.IsNullOrEmpty(sourceId))
            {
                throw new ArgumentException("A hook needs its catalogue source id.", nameof(sourceId));
            }

            SourceId = sourceId;
        }

        public string SourceId { get; }

        /// <summary>Host-assigned key (source id and attach order), the proc-budget group of its activations.</summary>
        public string Key { get; internal set; }

        /// <summary>The owner's effect host (set when attached).</summary>
        public EffectHost Host { get; internal set; }

        public DuelWorld World => Host.World;
        public int Player => Host.Player;
        public DuelPlayer Self => Host.World.Player(Host.Player);
        public DuelPlayer Opponent => Host.World.Opponent(Host.Player);
        public int Now => Host.World.Tick;
        public bool InCombat => Host.World.Phase == DuelPhase.Combat;

        /// <summary>Every duel event as it is emitted (any player's); record facts only.</summary>
        public virtual void OnEvent(DuelEvent e)
        {
        }

        /// <summary>Once per tick at the dispatch point (end of step 7), in every phase, before the tick's triggers.</summary>
        public virtual void OnTick()
        {
        }

        /// <summary>The owner's motor command of tick-order step 2 (stamina costs, gait multipliers, slows).</summary>
        public virtual void AdjustMotor(ref MotorCommand command)
        {
        }

        /// <summary>The owner's common melee profile at strike begin (stamina is paid from the returned profile).</summary>
        public virtual MeleeProfile AdjustStrike(MeleeProfile profile) => profile;

        /// <summary>The owner's cast price: requested reduction and increase in mana (the 70% floor applies after).</summary>
        public virtual void AdjustCastPrice(in CastQuote quote, ref float reduction, ref float increase)
        {
        }

        /// <summary>Windup and recovery changes of a cast the owner is committing (see <see cref="CastModifiers"/>).</summary>
        public virtual void AdjustCastTiming(in CastQuote quote, ref CastTimingAdjust timing)
        {
        }

        /// <summary>An original hit the owner deals, before the pipeline: fractional bonus and flat damage on the first part.</summary>
        public virtual void AdjustOutgoing(DamageRequest hit, float range, ref float fraction, ref float flat)
        {
        }

        /// <summary>Durability damage the owner's spell deals to a destructible solid (props are cover solids).</summary>
        public virtual float AdjustSolidDamage(int solidId, float amount, string sourceId, CastEventId castId) => amount;

        public virtual void Hash(ref StateHasher h)
        {
        }

        void IDuelEventObserver.OnEvent(DuelWorld world, DuelEvent e) => OnEvent(e);

        /// <summary>
        /// One activation of this hook's record (spec L126): once per 0.5 s and once per original target-hit event in the
        /// hook's budget group, with an <see cref="EffectEvent"/> for the HUD and replays. Returns false when the budget
        /// refuses it. <paramref name="originEventId"/> 0 uses a synthetic origin (only the interval limits it).
        /// </summary>
        protected bool TryActivate(int originEventId = 0, int target = -1, float amount = 0f)
        {
            int origin = originEventId != 0 ? originEventId : Host.System.NextSyntheticOrigin();
            if (!World.Procs.TryTrigger(Player, Key, origin, target >= 0 ? target : Player, Now))
            {
                return false;
            }

            World.RecordEvent(new EffectEvent { Change = EffectChange.Activated, SourceId = SourceId, EffectKey = Key, TriggerEventId = originEventId, Amount = amount }, Player);
            return true;
        }

        /// <summary>A presentation cue owned by this record (no rule effect).</summary>
        protected void EmitCue(string cue, float amount = 0f)
        {
            World.RecordEvent(new EffectEvent { Change = EffectChange.Cue, SourceId = SourceId, EffectKey = Key, Cue = cue, Amount = amount }, Player);
        }

        public override string ToString() => SourceId + " hook";
    }

    /// <summary>Windup and recovery changes requested for one cast (spec L92, L96, L480).</summary>
    public struct CastTimingAdjust
    {
        /// <summary>Fraction of the intrinsic windup removed for any cast (negative lengthens).</summary>
        public float WindupReduction;

        /// <summary>Fraction removed only when the cast earns its mastery reward (clamps with mastery windup, L480).</summary>
        public float MasteredWindupReduction;

        /// <summary>Ticks added to the windup after the fractions (positive only lengthens).</summary>
        public int WindupDeltaTicks;

        /// <summary>Ticks added to (negative: removed from) the cast's recovery.</summary>
        public int RecoveryDeltaTicks;

        /// <summary>Smallest recovery a reduction may leave, in ticks (0 = none).</summary>
        public int RecoveryFloorTicks;
    }
}
