using System;
using Confluence.Core.Rules;

namespace Confluence.Reactions.Grid
{
    /// <summary>
    /// Units of one material in one cell. Natural units are map-owned and permanent (spec L668); the remaining
    /// temporary units vanish together at <see cref="Expiry"/>. When deposits from several sources merge in a cell
    /// (spec L136 "repeated adjacent effects merge"), the temporary units share the latest expiry, and Owner/Source
    /// record the last direct source that committed material here (spec L670).
    /// </summary>
    internal struct MaterialSlot
    {
        public byte Units;
        public byte Natural;

        /// <summary>Tick at which the temporary units (Units - Natural) vanish.</summary>
        public int Expiry;

        public int Owner;
        public int Source;

        /// <summary>Removable field layer the temporary units belong to (0 = none), for dispel (spec L161).</summary>
        public int Field;

        /// <summary>Damage per second declared by the depositing source (growth burn, venom residue).</summary>
        public float Declared;

        public int Temporary => Units - Natural;

        public void Clear()
        {
            this = default;
            Owner = ReactionRules.MapOwner;
        }
    }

    /// <summary>Dynamic per-cell state bits.</summary>
    [Flags]
    internal enum CellState : ushort
    {
        None = 0,

        /// <summary>Part of a previewed map-owned natural source patch (spec L668).</summary>
        NaturalSource = 1 << 0,

        /// <summary>The growth here is combustible (temporary growth, L153).</summary>
        GrowthCombustible = 1 << 1,

        /// <summary>The growth here is sticky (slows, L175).</summary>
        GrowthSticky = 1 << 2,

        /// <summary>The ice tile is a map-owned starting tile that does not expire by time (BIO-004).</summary>
        IceNatural = 1 << 3,

        /// <summary>The cell's surface height has been set by a patch or deposit.</summary>
        HasSurface = 1 << 4,

        /// <summary>
        /// The surface height comes from an arena material patch and never changes. Without it the height is set by
        /// the first deposit on an inactive cell and cleared when the cell goes inactive again, so inactive cells hold
        /// no dynamic state (snapshots and hashes cover active cells only).
        /// </summary>
        StaticSurface = 1 << 5,
    }

    /// <summary>
    /// One 0.5 m interaction cell (spec L136): material units 0-4, static material flags, and the timers of the
    /// reactions that live on the ground (ice, burn, burn suppression, damp, charge, venom).
    /// </summary>
    internal struct Cell
    {
        public MaterialSlot Water;
        public MaterialSlot Heat;
        public MaterialSlot Frost;
        public MaterialSlot Dust;
        public MaterialSlot Sand;
        public MaterialSlot Growth;
        public MaterialSlot Venom;

        /// <summary>Static material flags from the arena (conductive, insulating, porous, metal...).</summary>
        public MaterialFlags Flags;

        public CellState State;

        /// <summary>World height of the surface this cell lies on.</summary>
        public float SurfaceY;

        // Ice tile (L145-L147). Durability > 0 means a tile is present.
        public float IceDurability;
        public int IceExpiry;
        public int IceOwner;
        public int IceSource;

        // Burning growth (L152-L153). BurnEnd > 0 while burning.
        public int BurnEnd;
        public int BurnHazard;
        public int BurnSuppressedUntil;

        // Damp sand (L154): units of sand/dust paired with water, until DampUntil.
        public byte DampUnits;
        public int DampUntil;

        // Electrified wet patch (L148).
        public int ChargeHazard;

        // Venom residue patch (L159).
        public int VenomHazard;

        public bool HasIce => IceDurability > 0f;
        public bool IsBurning => BurnEnd > 0;

        /// <summary>Liquid water present and not frozen into a tile.</summary>
        public bool IsWet => Water.Units > 0 && !HasIce;

        public static Cell Empty()
        {
            var cell = default(Cell);
            cell.Water.Owner = ReactionRules.MapOwner;
            cell.Heat.Owner = ReactionRules.MapOwner;
            cell.Frost.Owner = ReactionRules.MapOwner;
            cell.Dust.Owner = ReactionRules.MapOwner;
            cell.Sand.Owner = ReactionRules.MapOwner;
            cell.Growth.Owner = ReactionRules.MapOwner;
            cell.Venom.Owner = ReactionRules.MapOwner;
            cell.IceOwner = ReactionRules.MapOwner;
            return cell;
        }

        public CellUnits Units()
        {
            return new CellUnits
            {
                Water = Water.Units,
                Heat = Heat.Units,
                Frost = Frost.Units,
                Dust = Dust.Units,
                Sand = Sand.Units,
                Growth = Growth.Units,
                Venom = Venom.Units,
            };
        }

        /// <summary>
        /// True when the cell holds any dynamic state worth simulating, replicating, or counting as part of an
        /// active region (spec L136). Static flags alone do not make a cell active.
        /// </summary>
        public bool IsActive(int tick)
        {
            return Water.Units > 0 || Heat.Units > 0 || Frost.Units > 0 || Dust.Units > 0 || Sand.Units > 0 ||
                   Growth.Units > 0 || Venom.Units > 0 || HasIce || IsBurning || BurnSuppressedUntil > tick ||
                   DampUnits > 0 || ChargeHazard != 0 || VenomHazard != 0;
        }

        public static ref MaterialSlot Slot(ref Cell cell, Material material)
        {
            switch (material)
            {
                case Material.Water: return ref cell.Water;
                case Material.Heat: return ref cell.Heat;
                case Material.Frost: return ref cell.Frost;
                case Material.Dust: return ref cell.Dust;
                case Material.Sand: return ref cell.Sand;
                case Material.Growth: return ref cell.Growth;
                default: return ref cell.Venom;
            }
        }
    }
}
