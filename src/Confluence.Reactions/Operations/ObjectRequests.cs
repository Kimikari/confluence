using Confluence.Core.Geometry;

namespace Confluence.Reactions.Operations
{
    /// <summary>
    /// The standard shard cone of a breaking brittle object that is not a layout solid (spec L147 "emits a 1 meter cone
    /// of shards for 10 physical damage, once per impact"): Sim props such as PROP-010 Ice Cask ("the standard 10-damage
    /// shard cone") and PROP-037 Brittle Crystal. Applied in the direct-impacts phase with the same cone, damage, and
    /// once-per-body rule as a shattered ice tile; the damage is secondary environment damage (L126, L672).
    /// </summary>
    public sealed class ShardBurst
    {
        /// <summary>Apex of the cone (the struck point or the object's center).</summary>
        public Vec3 Apex { get; set; }

        /// <summary>Travel direction of the breaking impact; a vertical or zero direction sprays all around (L147 note).</summary>
        public Vec3 Direction { get; set; }

        public int Owner { get; set; } = ReactionRules.MapOwner;
        public int SourceEventId { get; set; }

        /// <summary>Object (prop placement) that shattered, for the event's target id, or -1.</summary>
        public int ObjectId { get; set; } = -1;
    }

    /// <summary>
    /// A burning object's contact zone (spec L153 "it burns for 2 seconds, deals its declared damage"): a box around a
    /// burning prop that deals thermal damage per second to each body touching it, at most once per second per body,
    /// for its lifetime (PROP-013 "5 thermal damage/s within 1 m", PROP-014 "4 thermal damage/s within 0.5 m"). Applied
    /// in the direct-impacts phase; player-owned zones last at most the 8 s field maximum (L128).
    /// </summary>
    public sealed class BurnZone
    {
        public YawBox Box { get; set; }
        public float DamagePerSecond { get; set; }
        public int LifetimeTicks { get; set; }
        public int Owner { get; set; } = ReactionRules.MapOwner;
        public int SourceEventId { get; set; }

        /// <summary>Object (prop placement) that burns, for presentation, or -1.</summary>
        public int ObjectId { get; set; } = -1;
    }
}
