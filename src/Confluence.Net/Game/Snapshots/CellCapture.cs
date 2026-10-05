using System;
using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Reactions;
using Confluence.Reactions.Grid;

namespace Confluence.Net.Game.Snapshots
{
    /// <summary>
    /// Incremental capture of a duel's replicated reaction cells (research §2.4.5): only cells that can have changed are
    /// queried, so the cost follows the reacting cells instead of the whole 48x48-96x96 grid. A cell's units or state
    /// change only through ReactionWorld's touch, which puts it in the active set until its decay ends; the refresh set
    /// is therefore the active cells now plus the active cells of the previous capture (those that just went quiet). A
    /// full scan runs on the first capture and every <see cref="FullScanEvery"/> captures as a safety net.
    /// </summary>
    public sealed class CellCapture
    {
        public const int FullScanEvery = 30;

        private readonly SortedDictionary<int, CellEntry> _cells = new SortedDictionary<int, CellEntry>();
        private readonly List<int> _previousActive = new List<int>();
        private int _sinceFull = -1;

        public long FullScans { get; private set; }
        public long IncrementalScans { get; private set; }
        public long CellsQueried { get; private set; }

        /// <summary>Fills <paramref name="into"/> with every non-empty cell in key order.</summary>
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
                IReadOnlyList<int> active = reactions.ActiveCells;
                for (int i = 0; i < active.Count; i++) Refresh(reactions, active[i]);
                for (int i = 0; i < _previousActive.Count; i++) Refresh(reactions, _previousActive[i]);
                _sinceFull++;
                IncrementalScans++;
            }

            _previousActive.Clear();
            IReadOnlyList<int> now = reactions.ActiveCells;
            for (int i = 0; i < now.Count; i++) _previousActive.Add(now[i]);
            foreach (CellEntry e in _cells.Values)
            {
                if (into.Count >= Wire.WireLimits.MaxCells) break;
                into.Add(e);
            }
        }

        /// <summary>The full-scan result for one cell, or null when it is empty (the reference the incremental path matches).</summary>
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
