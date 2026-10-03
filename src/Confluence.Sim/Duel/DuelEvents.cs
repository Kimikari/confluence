using Confluence.Casting;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Sim.Combat;
using Confluence.Sim.Status;

namespace Confluence.Sim.Duel
{
    public enum DuelEventType
    {
        Phase,
        Damage,
        Heal,
        Shield,
        Status,
        Guard,
        Melee,
        Movement,
        Field,
        Solid,
        Boundary,
        Resource,
        DuelEnd,

        /// <summary>Cast phase changes: preparation, cue, commitment, release, cancel, interrupt, recast (casting stage).</summary>
        Cast,

        /// <summary>Spell delivery lifecycle: projectiles, rays, fields, deployables, summons, waves (spell runtime).</summary>
        Spell,
    }

    /// <summary>
    /// A reliable authoritative event (spec L419, L427). Ids are assigned in simulation order and are stable for a
    /// given seed and input stream, so replays and the results "decisive sequence" (L359) can reference them.
    /// </summary>
    public abstract class DuelEvent
    {
        public int Id { get; internal set; }
        public int Tick { get; internal set; }

        /// <summary>The player the event is about (actor or subject), or -1.</summary>
        public int Player { get; internal set; } = -1;

        public abstract DuelEventType Type { get; }

        public override string ToString() => "#" + Id + " t" + Tick + " " + Type + " p" + Player;
    }

    public enum DuelPhase
    {
        /// <summary>Protected spawn alcoves: no damage, no casting, movement confined (spec L46).</summary>
        Alcove,

        /// <summary>Combat, at most 90 s (spec L47).</summary>
        Combat,

        Ended,
    }

    public sealed class PhaseEvent : DuelEvent
    {
        public DuelPhase Phase { get; internal set; }
        public override DuelEventType Type => DuelEventType.Phase;
    }

    /// <summary>One resolved hit with its full mitigation breakdown (spec L112, L359, L433 DamageEvent).</summary>
    public sealed class DamageEvent : DuelEvent
    {
        public int Attacker { get; internal set; }
        public int Target { get; internal set; }
        public DamageKind Kind { get; internal set; }
        public string SourceId { get; internal set; }

        /// <summary>Original target-hit or cast event (its own id for an original hit).</summary>
        public int OriginEventId { get; internal set; }

        public int Generation { get; internal set; }
        public ReactionTag Reaction { get; internal set; }
        public DamageResolution Resolution { get; internal set; }
        public Vec3 TargetPosition { get; internal set; }
        public float Range { get; internal set; }

        /// <summary>The committed cast this damage belongs to (spells; recasts and splits share it), or none.</summary>
        public CastEventId CastId { get; internal set; }

        public bool Lethal => Resolution != null && Resolution.Lethal;

        public float Dealt => Resolution == null ? 0f : Resolution.Dealt;

        public override DuelEventType Type => DuelEventType.Damage;
    }

    public sealed class HealEvent : DuelEvent
    {
        public string SourceId { get; internal set; }
        public bool Lifesteal { get; internal set; }
        public float Requested { get; internal set; }
        public float Restored { get; internal set; }

        /// <summary>Remaining duel healing budget after this heal (spec L114, L347).</summary>
        public float BudgetRemaining { get; internal set; }

        public override DuelEventType Type => DuelEventType.Heal;
    }

    public enum ShieldChange
    {
        Gained,
        Expired,
    }

    public sealed class ShieldEvent : DuelEvent
    {
        public ShieldChange Change { get; internal set; }
        public string SourceId { get; internal set; }
        public float Amount { get; internal set; }
        public bool Reserve { get; internal set; }
        public float TotalAfter { get; internal set; }
        public override DuelEventType Type => DuelEventType.Shield;
    }

    public enum StatusChange
    {
        Applied,
        Refreshed,
        BlockedByImmunity,
        BlockedByControlWindow,
        Expired,
        Removed,
        Broken,
    }

    /// <summary>Status lifecycle (spec L433 StatusEvent).</summary>
    public sealed class StatusEvent : DuelEvent
    {
        public StatusChange Change { get; internal set; }
        public StatusKind Kind { get; internal set; }
        public string DefinitionId { get; internal set; }
        public int SourcePlayer { get; internal set; }
        public int StatusId { get; internal set; }
        public int ExpireTick { get; internal set; }
        public float Intensity { get; internal set; }
        public override DuelEventType Type => DuelEventType.Status;
    }

    public enum GuardChange
    {
        Started,
        Released,
        Blocked,
        PerfectBlocked,
    }

    public sealed class GuardEvent : DuelEvent
    {
        public GuardChange Change { get; internal set; }
        public int BlockedEventId { get; internal set; }
        public float ManaGained { get; internal set; }
        public override DuelEventType Type => DuelEventType.Guard;
    }

    public enum MeleeChange
    {
        Started,
        Hit,
        ContactedSolid,
    }

    public sealed class MeleeEvent : DuelEvent
    {
        public MeleeChange Change { get; internal set; }
        public string ProfileName { get; internal set; }
        public string SourceId { get; internal set; }
        public int AttackSerial { get; internal set; }
        public int TargetPlayer { get; internal set; } = -1;
        public int SolidId { get; internal set; } = -1;
        public override DuelEventType Type => DuelEventType.Melee;
    }

    public enum MovementChange
    {
        Jumped,
        Dodged,
        Landed,
        FellOutOfMap,
        RecallStarted,
        RecallEnded,
        KnockbackStarted,
        KnockbackBlockedByImmunity,
        EdgeImpact,
        DashStarted,
        Teleported,
        SwapStarted,
        SwapCompleted,

        /// <summary>A temporary solid formed under the player lifted them onto its top (at most 0.5 m, spec L171).</summary>
        RaisedByTerrain,
    }

    public sealed class MovementEvent : DuelEvent
    {
        public MovementChange Change { get; internal set; }
        public Vec3 Position { get; internal set; }
        public float Amount { get; internal set; }
        public string SourceId { get; internal set; }
        public override DuelEventType Type => DuelEventType.Movement;
    }

    public enum OwnedEntityKind
    {
        Field,
        Summon,
    }

    public enum FieldChange
    {
        Created,

        /// <summary>Retired at the ownership limit with a brief dissolution cue (spec L128).</summary>
        Retired,

        Expired,
        Destroyed,
        Dispelled,
    }

    public sealed class FieldEvent : DuelEvent
    {
        public FieldChange Change { get; internal set; }
        public OwnedEntityKind EntityKind { get; internal set; }
        public int EntityId { get; internal set; }
        public string DefinitionId { get; internal set; }
        public int Destroyer { get; internal set; } = -1;
        public override DuelEventType Type => DuelEventType.Field;
    }

    public enum SolidChange
    {
        Created,
        Damaged,
        Destroyed,
        Expired,
        Retired,
    }

    public sealed class SolidEvent : DuelEvent
    {
        public SolidChange Change { get; internal set; }
        public int SolidId { get; internal set; }
        public float Amount { get; internal set; }
        public float DurabilityAfter { get; internal set; }
        public override DuelEventType Type => DuelEventType.Solid;
    }

    public enum BoundaryChange
    {
        Exited,
        Returned,
        EscalationReset,
    }

    public sealed class BoundaryEvent : DuelEvent
    {
        public BoundaryChange Change { get; internal set; }
        public override DuelEventType Type => DuelEventType.Boundary;
    }

    public enum ResourceChange
    {
        ManaSpent,
        ManaGained,
        ConsumableUsed,
    }

    public sealed class ResourceEvent : DuelEvent
    {
        public ResourceChange Change { get; internal set; }
        public float Amount { get; internal set; }
        public string SourceId { get; internal set; }
        public override DuelEventType Type => DuelEventType.Resource;
    }

    public enum DuelOutcome
    {
        Undecided,
        Win,
        Draw,
    }

    public enum DuelEndReason
    {
        Death,
        SimultaneousDeath,
        Timer,
        Surrender,
    }

    public sealed class DuelEndEvent : DuelEvent
    {
        public DuelOutcome Outcome { get; internal set; }

        /// <summary>Winning player index, or -1 for a draw.</summary>
        public int Winner { get; internal set; } = -1;

        public DuelEndReason Reason { get; internal set; }
        public float HealthFraction0 { get; internal set; }
        public float HealthFraction1 { get; internal set; }
        public override DuelEventType Type => DuelEventType.DuelEnd;
    }
}
