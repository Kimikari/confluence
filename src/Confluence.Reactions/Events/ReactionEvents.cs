using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Reactions.Volumes;

namespace Confluence.Reactions.Events
{
    /// <summary>Kinds of world reactions, used for events and damage attribution (spec §4 table L142-L161).</summary>
    public enum ReactionKind
    {
        None,
        Steam,             // L144
        IceFormed,         // L145
        IceMelted,         // L146
        IceWorn,           // L145 durability loss below the shatter threshold
        IceShattered,      // L147
        IceExpired,        // L145 5 s lifetime
        ShardCone,         // L147
        WetCharge,         // L148
        ConductorGlow,     // L149
        ConductorRelease,  // L149
        GroundRedirect,    // L150
        GroundOverload,    // L150
        GroundReset,       // L150 (PROP-002 "then visibly resets")
        WindTranslate,     // L151
        WindDisperse,      // L151
        VolumeCleared,     // SPL-061 corridor
        Extinguish,        // L152
        Ignition,          // L153
        BurnComplete,      // L153 growth consumed
        SolidIgnited,      // L153, L163 (combustible solids only)
        DampFormed,        // L154
        DampExpired,       // L154
        DampDried,         // L154 response column: bounded heat dries damp sand
        DustRaised,        // L154 dust production
        PropImpulse,       // L155
        VenomDilution,     // L159
        VenomContact,      // L159 residue damage
        VolumeContact,     // venom cloud contact damage
        HotSurface,        // red-hot surface contact
        BurnContact,       // burning growth contact damage
        Dispel,            // L161: a ground field layer (TargetId = field id; 0 when nothing was removable)
        DispelVolume,      // L161: a removable volume (TargetId = volume id)
        Condense,          // L167
        UnitsRemoved,      // neutralization
        RefillPulse,       // L138, BIO-001
        GustStarted,       // L664 gust schedules
    }

    /// <summary>An auditable reaction outcome (spec L670: one recorded origin per simultaneous reaction).</summary>
    public struct ReactionEvent
    {
        public int Tick;
        public ReactionKind Kind;
        public int Owner;
        public int SourceEventId;
        public Vec3 Position;

        /// <summary>Cell index, or -1.</summary>
        public int CellIndex;

        /// <summary>Kind-specific count (units consumed, cells charged, columns removed...).</summary>
        public int Amount;

        /// <summary>Kind-specific id (volume, conductor, hazard, prop, solid, field).</summary>
        public int TargetId;

        public override string ToString() => Kind + " t" + Tick + " owner " + Owner + " amount " + Amount + " target " + TargetId;
    }

    /// <summary>
    /// A request for Sim to damage a body through the damage pipeline. Reactions never apply damage itself. Every
    /// reaction hit is secondary: it cannot trigger on-hit, on-kill, resource-return, or proc effects (spec L126,
    /// L672), and it never triggers spell-hit lifesteal (L173).
    /// </summary>
    public struct ContactDamage
    {
        public int BodyId;
        public float Amount;
        public DamageChannel Channel;
        public int Owner;
        public int SourceEventId;
        public ReactionKind Reaction;
        public bool IsSecondary;

        /// <summary>Hazard (charge, burn, venom patch, shard cone...) that produced the hit.</summary>
        public int HazardId;

        public override string ToString() => Reaction + " " + Amount + " " + Channel + " -> body " + BodyId;
    }

    /// <summary>
    /// A clamped impulse for Sim to apply to a loose prop (spec L155, L173): distance capped by mass class, speed by
    /// the 8 m/s damaging cap, and collision damage by the class value (at most 25 physical per impact).
    /// </summary>
    public struct PropImpulse
    {
        public int PropId;
        public PropMass Mass;

        /// <summary>Unit ground-plane push direction.</summary>
        public Vec3 Direction;

        public float Distance;
        public float MaxSpeed;
        public float CollisionDamageCap;

        /// <summary>Anchored props only shake or take durability damage (L173).</summary>
        public bool ShakeOnly;

        public int Owner;
        public int SourceEventId;
    }

    public enum VolumeEventKind
    {
        Created,
        Retired,
    }

    public enum VolumeRetireReason
    {
        None,
        Expired,
        Dispersed,
        Condensed,
        Dispelled,

        /// <summary>Sim retired the oldest owned field at the three-field limit (spec L128).</summary>
        FieldLimit,

        /// <summary>Consumed by a spell or reaction (for example water collapsing ash, SPL-012).</summary>
        Consumed,

        Cleared,
        Requested,
    }

    /// <summary>
    /// Volume lifecycle notice carrying the bookkeeping owner, so Sim can count player-created clouds against the
    /// three-field cap and retire the oldest (spec L128, L670).
    /// </summary>
    public struct VolumeEvent
    {
        public VolumeEventKind Kind;
        public int VolumeId;
        public VolumeKind VolumeKind;
        public int Owner;
        public int SourceEventId;
        public bool CountsAsField;
        public int CreatedTick;
        public int Tick;
        public VolumeRetireReason Reason;

        public override string ToString() => Kind + " " + VolumeKind + "#" + VolumeId + " owner " + Owner + " " + Reason;
    }

    /// <summary>Everything one <see cref="ReactionWorld.Step"/> produced. Buffers are reused by the next step.</summary>
    public sealed class StepResult
    {
        internal readonly List<ReactionEvent> EventList = new List<ReactionEvent>();
        internal readonly List<ContactDamage> DamageList = new List<ContactDamage>();
        internal readonly List<PropImpulse> ImpulseList = new List<PropImpulse>();
        internal readonly List<VolumeEvent> VolumeEventList = new List<VolumeEvent>();

        public int Tick { get; internal set; }

        public IReadOnlyList<ReactionEvent> Events => EventList;
        public IReadOnlyList<ContactDamage> Damage => DamageList;
        public IReadOnlyList<PropImpulse> PropImpulses => ImpulseList;
        public IReadOnlyList<VolumeEvent> VolumeEvents => VolumeEventList;

        internal void Clear()
        {
            EventList.Clear();
            DamageList.Clear();
            ImpulseList.Clear();
            VolumeEventList.Clear();
        }

        /// <summary>Number of events of one kind.</summary>
        public int Count(ReactionKind kind)
        {
            int n = 0;
            for (int i = 0; i < EventList.Count; i++)
            {
                if (EventList[i].Kind == kind) n++;
            }

            return n;
        }

        /// <summary>Total damage requested for a body.</summary>
        public float DamageTo(int bodyId)
        {
            float total = 0f;
            for (int i = 0; i < DamageList.Count; i++)
            {
                if (DamageList[i].BodyId == bodyId) total += DamageList[i].Amount;
            }

            return total;
        }
    }
}
