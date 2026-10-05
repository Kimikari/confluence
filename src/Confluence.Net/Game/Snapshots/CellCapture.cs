using System;
using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Reactions;
using Confluence.Reactions.Grid;

namespace Confluence.Net.Game.Snapshots
{
    /// <summary>
    /// Incremental capture of a duel's replicated reaction cells (research §2.4.5): the cost follows the reacting cells
    /// instead of the whole 48x48-96x96 grid. Exactness rests on one Reactions invariant: a cell with any units, ice,
    /// burning, damp, charge or venom state is active (Cell.IsActive), and after every step the active list holds every
    /// active cell (ReactionWorld.RebuildActive). So every cell that is non-empty now is in
    /// <see cref="ReactionWorld.ActiveCells"/>, and every cell that just became empty was non-empty at the previous
    /// capture (it is in the cache). Refreshing "active now" plus "cached" therefore reproduces the full scan; a full scan
    /// still runs on the first capture and every <see cref="FullScanEvery"/> captures as a safety net, and
    /// <c>CellCaptureTests</c> compares both paths every capture over real duels.
    /// </summary>
    public sealed class CellCapture
    {
        public const int FullScanEvery = 30;

        private readonly SortedDictionary<int, CellEntry> _cells = new SortedDictionary<int, CellEntry>();
        private readonly List<int> _scratch = new List<int>();
        private int _sinceFull = -1;

        public long FullScans { get; private set; }
        public long IncrementalScans { get; private set; }
        public long CellsQueried { get; private set; }

        /// <summary>Fills <paramref name="into"/> with every non-empty cell in key order (at most WireLimits.MaxCells).</summary>
        public void Capture(ReactionWorld reactions, List<CellEntry> into)
        {
            if (reactions == null) return;
            if (_sinceFull < 0 || _sinceFull >= FullScanEvery)
            {
                _cells.Clear();
                int count = reactions.Grid.CellCount;
                for (int i = 0; i < count; i++) Refresh(reactions, i);
                _sinceFull = 0;
                FullScans++;
            }
            else
            {
                _scratch.Clear();
                foreach (int key in _cells.Keys) _scratch.Add(key);
                IReadOnlyList<int> active = reactions.ActiveCells;
                for (int i = 0; i < active.Count; i++) Refresh(reactions, active[i]);
                for (int i = 0; i < _scratch.Count; i++) Refresh(reactions, _scratch[i]);
                _sinceFull++;
                IncrementalScans++;
            }

            foreach (CellEntry e in _cells.Values)
            {
                if (into.Count >= Wire.WireLimits.MaxCells) break;
                into.Add(e);
            }
        }

        /// <summary>The replicated entry of one cell, or null when it is empty (shared by the full scan).</summary>
        public static CellEntry Query(ReactionWorld reactions, int index)
        {
            CellGrid grid = reactions.Grid;
            CellUnits u = reactions.UnitsAtCell(index);
            Vec2 c = grid.CellCenter(index);
            var point = new Vec3(c.X, 0f, c.Y);
            CellBits bits = CellBits.None;
            if (reactions.IsWetCell(index)) bits |= CellBits.Wet;
            if (reactions.HasIceAt(point)) bits |= CellBits.Ice;
            if (reactions.IsBurningAt(point)) bits |= CellBits.Burning;
            if (reactions.IsChargedAt(point)) bits |= CellBits.Charged;
            if (reactions.IsDampAt(point)) bits |= CellBits.Damp;
            if (u.Total == 0 && bits == CellBits.None) return null;
            return new CellEntry(index, B(u.Water), B(u.Heat), B(u.Frost), B(u.Dust), B(u.Sand), B(u.Growth), B(u.Venom), bits);
        }

        private void Refresh(ReactionWorld reactions, int index)
        {
            if (index < 0 || index >= reactions.Grid.CellCount) return;
            CellsQueried++;
            CellEntry e = Query(reactions, index);
            if (e == null) _cells.Remove(index);
            else _cells[index] = e;
        }

        private static byte B(int units) => (byte)Math.Max(0, Math.Min(255, units));
    }
}
