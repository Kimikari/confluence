using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Volumes;

namespace Confluence.Reactions.Operations
{
    /// <summary>
    /// A direct impact on the world: a projectile, melee strike, or force hit at a point (spec L147, L165 "incoming
    /// direct impacts"). An impact of at least 20 direct damage on an ice tile or a brittle solid breaks one struck
    /// tile and emits the shard cone once (L147); weaker impacts wear the tile's 45 durability (L145).
    /// </summary>
    public sealed class DirectImpact
    {
        public Vec3 Point { get; set; }

        /// <summary>Travel direction of the impact; the shard cone opens along its ground projection.</summary>
        public Vec3 Direction { get; set; }

        public float DirectDamage { get; set; }

        /// <summary>Struck solid (frangible brittle cover such as MOD-047, BIO-021), or -1 for the ground.</summary>
        public int SolidId { get; set; } = -1;

        /// <summary>Heat strength carried into a struck solid: combustible solids ignite at two units (L153, L163).</summary>
        public int HeatStrength { get; set; }

        /// <summary>Declared burn damage per second for an ignited solid.</summary>
        public float BurnDamagePerSecond { get; set; }

        public int Owner { get; set; } = ReactionRules.MapOwner;
        public int SourceEventId { get; set; }
    }

    /// <summary>
    /// An electrical contact with the world (spec L148-L150): a wet-cell charge, a metal conductor, or a grounded
    /// redirect. ReactionDamage is the source's declared reaction damage dealt on first contact (L148).
    /// </summary>
    public sealed class ElectricDischarge
    {
        public Vec3 Point { get; set; }

        /// <summary>Optional contact footprint (for example a rail of lightning along the ground, SPL-063).</summary>
        public Footprint? Contact { get; set; }

        /// <summary>Conductor struck directly, or -1 to find one touching the point.</summary>
        public int ConductorId { get; set; } = -1;

        public float ReactionDamage { get; set; }

        /// <summary>Electrical strength from the world-interaction field (informational; the caps do not scale with it).</summary>
        public int Strength { get; set; } = 1;

        /// <summary>Charge duration; clamped to the 0.6 s rule (L148). Local text may only shorten it.</summary>
        public float ChargeSec { get; set; } = GlobalRules.World.WetChargeDurationSec;

        public int Owner { get; set; } = ReactionRules.MapOwner;
        public int SourceEventId { get; set; }
    }

    /// <summary>
    /// A directional force acting on clouds (spec L151, DECISIONS D-023). Above strength 1 it translates movable
    /// volumes at up to 3 m/s; strength 2 or more also disperses each touched volume's front 2 m slice once.
    /// </summary>
    public sealed class WindGust
    {
        /// <summary>Area the gust acts on; null for the whole arena (biome gusts).</summary>
        public Footprint? Area { get; set; }

        /// <summary>Ground-plane direction the gust pushes toward.</summary>
        public Vec2 Direction { get; set; }

        public int Strength { get; set; }

        public int DurationTicks { get; set; } = 1;

        /// <summary>Requested translation speed; never more than 3 m/s (L151).</summary>
        public float Speed { get; set; } = GlobalRules.World.WindTranslateMaxSpeed;

        /// <summary>Total translation bound per volume stated by the source (SPL-059 "moves vapor 3 m", BIO-011 "up to 2 m").</summary>
        public float MaxDistance { get; set; } = float.PositiveInfinity;

        /// <summary>
        /// The source's own text states that a strength-1 force moves clouds (SPL-105, SPL-110, BIO-011; D-023). Such
        /// a source still obeys the speed cap and its stated distance bound.
        /// </summary>
        public bool LocalCloudOverride { get; set; }

        /// <summary>Restrict the gust to one volume (manual pushes such as AUG-312 Ash Bellows); 0 for any.</summary>
        public int TargetVolumeId { get; set; }

        public int Owner { get; set; } = ReactionRules.MapOwner;
        public int SourceEventId { get; set; }

        public bool Translates => Strength > GlobalRules.World.WindTranslateStrengthAbove || LocalCloudOverride;
        public bool Disperses => GlobalRules.World.WindDispersesClouds(Strength);
    }

    /// <summary>
    /// A force hit on a loose prop (spec L155, L173). Props live in Sim; Reactions clamps the push to the prop's mass
    /// class and the damaging velocity cap, and reports a <see cref="PropImpulse"/>.
    /// </summary>
    public sealed class PropForce
    {
        public int PropId { get; set; }
        public PropMass Mass { get; set; }
        public Vec3 Direction { get; set; }

        /// <summary>Requested push; a normal force attack requests the class maximum (L173).</summary>
        public float RequestedDistance { get; set; } = float.PositiveInfinity;

        public float RequestedSpeed { get; set; } = GlobalRules.Terrain.DamagingPropVelocityCap;

        public int Owner { get; set; } = ReactionRules.MapOwner;
        public int SourceEventId { get; set; }
    }

    /// <summary>A declared dispel: consumes one removable field layer touching its area (spec L161).</summary>
    public sealed class DispelRequest
    {
        public Footprint Area { get; set; }
        public int Owner { get; set; } = ReactionRules.MapOwner;
        public int SourceEventId { get; set; }
    }

    /// <summary>Creation of a state volume by a spell, prop, or biome (spec L134, L167).</summary>
    public sealed class VolumeSpec
    {
        public VolumeKind Kind { get; set; }
        public Footprint Footprint { get; set; }
        public float BaseY { get; set; }
        public float Height { get; set; } = ReactionRules.SteamHeight;

        /// <summary>Lifetime; player-owned volumes are clamped to the 8 s field maximum (L128).</summary>
        public int LifetimeTicks { get; set; }

        public int Owner { get; set; } = ReactionRules.MapOwner;
        public int SourceEventId { get; set; }
        public int FieldId { get; set; }

        /// <summary>Overrides of the kind defaults when the source's text states otherwise.</summary>
        public bool? BlocksSight { get; set; }
        public bool? WindMovable { get; set; }

        /// <summary>Counts against the owner's field limit; default: any player-owned volume (L128, L670).</summary>
        public bool? CountsAsField { get; set; }

        public bool Removable { get; set; } = true;

        public float DamagePerSecond { get; set; }
        public DamageChannel Channel { get; set; } = DamageChannel.None;
        public bool Soluble { get; set; }

        /// <summary>Water units the volume could return if condensed (0 for clouds not made of water).</summary>
        public int SourceWater { get; set; }
    }

    /// <summary>
    /// A spell that explicitly condenses a cloud back into bounded water, consuming it (spec L167). At most the
    /// cloud's <see cref="ObscurationVolume.SourceWater"/> units return, one per column below the cloud.
    /// </summary>
    public sealed class CondenseRequest
    {
        public int VolumeId { get; set; }

        /// <summary>Further bound stated by the spell (int.MaxValue for none).</summary>
        public int MaxUnits { get; set; } = int.MaxValue;

        public int LifetimeTicks { get; set; }
        public int Owner { get; set; } = ReactionRules.MapOwner;
        public int SourceEventId { get; set; }
        public int FieldId { get; set; }
    }

    /// <summary>
    /// Neutralization that removes units from a footprint (SPL-080 Seal the Spring "removes up to 2 reaction units";
    /// AUG-447 "one hot/corrosive unit per cell"). Reports how many units were removed.
    /// </summary>
    public sealed class UnitRemoval
    {
        public Footprint Footprint { get; set; }

        /// <summary>Materials eligible for removal, tried in this order in each cell.</summary>
        public Material[] Materials { get; set; } = { Material.Heat, Material.Venom };

        public int MaxPerCell { get; set; } = 1;
        public int MaxTotal { get; set; } = int.MaxValue;
        public int Owner { get; set; } = ReactionRules.MapOwner;
        public int SourceEventId { get; set; }
    }

    /// <summary>
    /// A force that raises dust from sand and dust beds (BIO-015 "a force impact above 20 raises a 3 s dust field",
    /// PROP-017). Each covered cell with unsuppressed dust production gives one unit to a dust cloud; damp sand
    /// produces less (L154).
    /// </summary>
    public sealed class DustRaise
    {
        public Footprint Footprint { get; set; }
        public float BaseY { get; set; }
        public float Height { get; set; } = ReactionRules.SteamHeight;
        public int LifetimeTicks { get; set; }
        public int Owner { get; set; } = ReactionRules.MapOwner;
        public int SourceEventId { get; set; }
    }

    /// <summary>A red-hot surface dealing contact damage (SPL-016 Flashforge "red-hot for 4 s, dealing 8/s on contact").</summary>
    public sealed class HotSurface
    {
        public YawBox Box { get; set; }
        public float DamagePerSecond { get; set; }
        public int LifetimeTicks { get; set; }
        public int Owner { get; set; } = ReactionRules.MapOwner;
        public int SourceEventId { get; set; }

        /// <summary>Conductor or prop the surface belongs to (presentation link), or -1.</summary>
        public int ObjectId { get; set; } = -1;
    }

    /// <summary>Removes the columns of movable vapor inside a footprint (SPL-061 "clears a 1 m-wide corridor of vapor").</summary>
    public sealed class VolumeClear
    {
        public Footprint Area { get; set; }
        public int Owner { get; set; } = ReactionRules.MapOwner;
        public int SourceEventId { get; set; }

        /// <summary>Only wind-movable volumes by default; set false to clear any kind.</summary>
        public bool MovableOnly { get; set; } = true;
    }
}
