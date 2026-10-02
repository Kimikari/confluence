using System;
using Confluence.Core.Rules;

namespace Confluence.Reactions
{
    /// <summary>
    /// Unit-counted materials stored per 0.5 m cell (spec L136: water, heat, frost, dust; D-025 adds sand and growth).
    /// Venom residue is counted the same way so dilution (L159) and contact damage read a bounded patch.
    /// </summary>
    public enum Material
    {
        Water = 0,
        Heat = 1,
        Frost = 2,
        Dust = 3,
        Sand = 4,
        Growth = 5,
        Venom = 6,
    }

    public static class Materials
    {
        public const int Count = 7;

        /// <summary>Every material in enum order (the fixed iteration order used by decay and snapshots).</summary>
        public static readonly Material[] All =
        {
            Material.Water, Material.Heat, Material.Frost, Material.Dust, Material.Sand, Material.Growth, Material.Venom,
        };

        /// <summary>Cap on units of any material in one cell (spec L136, L163).</summary>
        public const int MaxUnits = GlobalRules.World.MaxUnitsPerCell;
    }

    /// <summary>Unit counts of one cell.</summary>
    public struct CellUnits : IEquatable<CellUnits>
    {
        public int Water;
        public int Heat;
        public int Frost;
        public int Dust;
        public int Sand;
        public int Growth;
        public int Venom;

        public int this[Material material]
        {
            get
            {
                switch (material)
                {
                    case Material.Water: return Water;
                    case Material.Heat: return Heat;
                    case Material.Frost: return Frost;
                    case Material.Dust: return Dust;
                    case Material.Sand: return Sand;
                    case Material.Growth: return Growth;
                    default: return Venom;
                }
            }
        }

        public int Total => Water + Heat + Frost + Dust + Sand + Growth + Venom;

        public bool Equals(CellUnits other) =>
            Water == other.Water && Heat == other.Heat && Frost == other.Frost && Dust == other.Dust &&
            Sand == other.Sand && Growth == other.Growth && Venom == other.Venom;

        public override bool Equals(object obj) => obj is CellUnits other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = Water;
                h = h * 31 + Heat;
                h = h * 31 + Frost;
                h = h * 31 + Dust;
                h = h * 31 + Sand;
                h = h * 31 + Growth;
                return h * 31 + Venom;
            }
        }

        public override string ToString() =>
            "W" + Water + " H" + Heat + " F" + Frost + " D" + Dust + " S" + Sand + " G" + Growth + " V" + Venom;
    }

    /// <summary>Surface conditions present in a cell; several can apply at once.</summary>
    [Flags]
    public enum SurfaceFlags
    {
        None = 0,

        /// <summary>Liquid water (not frozen): wet stone changes reaction eligibility and footsteps only (L175).</summary>
        Wet = 1 << 0,

        /// <summary>Ice tile: slippery, reduced braking and lateral drift (L175).</summary>
        Ice = 1 << 1,

        /// <summary>Sand or dust bed.</summary>
        Sand = 1 << 2,

        /// <summary>Damp sand: reduced dust production, no immobilization (L154).</summary>
        DampSand = 1 << 3,

        /// <summary>Growth present.</summary>
        Growth = 1 << 4,

        /// <summary>Sticky growth: slows within the global 30 percent limit (L175).</summary>
        StickyGrowth = 1 << 5,

        /// <summary>Burning growth (L153).</summary>
        Burning = 1 << 6,

        /// <summary>Electrified wet patch (L148).</summary>
        Charged = 1 << 7,

        /// <summary>Venom residue (L159).</summary>
        Venom = 1 << 8,

        /// <summary>Heat units present.</summary>
        Hot = 1 << 9,

        /// <summary>Burn suppressed by water (L152).</summary>
        Extinguished = 1 << 10,

        /// <summary>Static conductive material (metal plate, rail bed).</summary>
        Conductive = 1 << 11,
    }

    /// <summary>The dominant surface for footsteps and movement, chosen from <see cref="SurfaceFlags"/> by priority.</summary>
    public enum SurfaceKind
    {
        Normal,
        WetStone,
        Ice,
        Sand,
        DampSand,
        Growth,
        StickyGrowth,
        Burning,
        Charged,
        Venom,
    }

    /// <summary>Result of <see cref="ReactionWorld.SurfaceAt"/>.</summary>
    public readonly struct SurfaceInfo
    {
        public readonly SurfaceKind Kind;
        public readonly SurfaceFlags Flags;
        public readonly CellUnits Units;

        /// <summary>Cell index, or -1 outside the grid.</summary>
        public readonly int CellIndex;

        public SurfaceInfo(SurfaceKind kind, SurfaceFlags flags, CellUnits units, int cellIndex)
        {
            Kind = kind;
            Flags = flags;
            Units = units;
            CellIndex = cellIndex;
        }

        public bool Has(SurfaceFlags flag) => (Flags & flag) == flag;

        /// <summary>
        /// Priority: hazards first (charged, burning), then traction (ice, sticky growth), then texture. Movement
        /// effects never come from wet stone alone (L175).
        /// </summary>
        public static SurfaceKind Classify(SurfaceFlags flags)
        {
            if ((flags & SurfaceFlags.Charged) != 0) return SurfaceKind.Charged;
            if ((flags & SurfaceFlags.Burning) != 0) return SurfaceKind.Burning;
            if ((flags & SurfaceFlags.Ice) != 0) return SurfaceKind.Ice;
            if ((flags & SurfaceFlags.StickyGrowth) != 0) return SurfaceKind.StickyGrowth;
            if ((flags & SurfaceFlags.Venom) != 0) return SurfaceKind.Venom;
            if ((flags & SurfaceFlags.DampSand) != 0) return SurfaceKind.DampSand;
            if ((flags & SurfaceFlags.Sand) != 0) return SurfaceKind.Sand;
            if ((flags & SurfaceFlags.Wet) != 0) return SurfaceKind.WetStone;
            if ((flags & SurfaceFlags.Growth) != 0) return SurfaceKind.Growth;
            return SurfaceKind.Normal;
        }
    }
}
