using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions.Grid;

namespace Confluence.Reactions.Operations
{
    /// <summary>How a deposit's strength spreads over its footprint (spec L163).</summary>
    public enum DepositPattern
    {
        /// <summary><see cref="MaterialDeposit.Strength"/> units in every covered cell (fields, trails, quick hits).</summary>
        Uniform,

        /// <summary>
        /// Heavy direct projectile: <see cref="MaterialDeposit.Strength"/> at the impact cell and
        /// <see cref="MaterialDeposit.AdjacentStrength"/> in other cells within <see cref="MaterialDeposit.AdjacentRadius"/>.
        /// </summary>
        HeavyImpact,
    }

    /// <summary>
    /// Material a spell, prop, or biome adds to the ground (spec L163 "Reaction strength values are part of the
    /// spell's world-interaction field"). Applied in the direct-impacts phase; adding strength never exceeds four
    /// units per cell (L163). Lifetime 0 is an instant residue: it reacts this step, then whatever is left decays.
    /// </summary>
    public sealed class MaterialDeposit
    {
        public Material Material { get; set; }

        /// <summary>Units per covered cell (impact cell for <see cref="DepositPattern.HeavyImpact"/>).</summary>
        public int Strength { get; set; } = 1;

        public DepositPattern Pattern { get; set; } = DepositPattern.Uniform;

        public int AdjacentStrength { get; set; } = GlobalRules.World.HeavyProjectileAdjacentDeposit;
        public float AdjacentRadius { get; set; } = GlobalRules.World.HeavyProjectileAdjacentRadius;

        public Footprint Footprint { get; set; }

        /// <summary>Height of the surface the material lands on.</summary>
        public float SurfaceY { get; set; }

        /// <summary>
        /// Ticks the deposited units persist (0: instant residue). Player-owned material lasts at most the 8 s field
        /// lifetime (spec L128); map-owned material may be permanent (<see cref="int.MaxValue"/>).
        /// </summary>
        public int LifetimeTicks { get; set; }

        public int Owner { get; set; } = ReactionRules.MapOwner;
        public int SourceEventId { get; set; }

        /// <summary>Sim field the material belongs to, so a dispel can consume the layer (spec L161); 0 for none.</summary>
        public int FieldId { get; set; }

        /// <summary>False for material a dispel may not remove.</summary>
        public bool Removable { get; set; } = true;

        /// <summary>Declared damage per second: burning growth (L153) or venom residue contact (L159).</summary>
        public float DeclaredDamagePerSecond { get; set; }

        /// <summary>Growth: temporary growth ignites with two heat units (L153). Default true.</summary>
        public bool Combustible { get; set; } = true;

        /// <summary>Growth: sticky growth slows (L175).</summary>
        public bool Sticky { get; set; }

        /// <summary>Venom: a declared soluble venom effect dilutes in water (L159).</summary>
        public bool Soluble { get; set; }

        /// <summary>Quick single projectile: one local unit at the impact cell (L163).</summary>
        public static MaterialDeposit QuickProjectile(Material material, Vec3 impact, int owner, int sourceEventId, int lifetimeTicks = 0)
        {
            return new MaterialDeposit
            {
                Material = material,
                Strength = GlobalRules.World.QuickProjectileDeposit,
                Footprint = Footprint.Point(impact.XZ()),
                SurfaceY = impact.Y,
                LifetimeTicks = lifetimeTicks,
                Owner = owner,
                SourceEventId = sourceEventId,
            };
        }

        /// <summary>Heavy direct projectile: two units at impact, one in adjacent cells within 1 m (L163).</summary>
        public static MaterialDeposit HeavyProjectile(Material material, Vec3 impact, int owner, int sourceEventId,
            int impactStrength = GlobalRules.World.HeavyProjectileImpactDeposit, int lifetimeTicks = 0)
        {
            return new MaterialDeposit
            {
                Material = material,
                Strength = impactStrength,
                Pattern = DepositPattern.HeavyImpact,
                Footprint = Footprint.Circle(impact.XZ(), GlobalRules.World.HeavyProjectileAdjacentRadius),
                SurfaceY = impact.Y,
                LifetimeTicks = lifetimeTicks,
                Owner = owner,
                SourceEventId = sourceEventId,
            };
        }

        /// <summary>A field: its strength (default one unit) in every covered cell on creation (L163).</summary>
        public static MaterialDeposit Field(Material material, Footprint footprint, float surfaceY, float lifetimeSec,
            int owner, int sourceEventId, int strength = GlobalRules.World.FieldDepositPerCell, int fieldId = 0)
        {
            return new MaterialDeposit
            {
                Material = material,
                Strength = strength,
                Footprint = footprint,
                SurfaceY = surfaceY,
                LifetimeTicks = Ticks.FromSeconds(lifetimeSec),
                Owner = owner,
                SourceEventId = sourceEventId,
                FieldId = fieldId,
            };
        }
    }
}
