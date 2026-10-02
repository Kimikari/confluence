using System;
using System.Collections.Generic;
using Confluence.Core.Rules;

namespace Confluence.Reactions.Grid
{
    /// <summary>A contiguous representation of active cells (spec L136, L421 budget of 128 per duel).</summary>
    public sealed class ActiveRegion
    {
        internal readonly List<int> CellList = new List<int>();

        /// <summary>Ordinal in the current region list (regions are ordered by their lowest cell index).</summary>
        public int Id { get; internal set; }

        /// <summary>Cells of the region in ascending index order.</summary>
        public IReadOnlyList<int> Cells => CellList;

        public int CellCount => CellList.Count;
        public int MinIndex => CellList.Count > 0 ? CellList[0] : -1;

        public int MinX { get; internal set; }
        public int MinZ { get; internal set; }
        public int MaxX { get; internal set; }
        public int MaxZ { get; internal set; }

        /// <summary>
        /// Number of 8-connected components merged into this representation. Greater than one only when the
        /// region cap forced nearby components to share a representation.
        /// </summary>
        public int Parts { get; internal set; } = 1;
    }

    /// <summary>
    /// Active-region bookkeeping. Regions are the 8-connected components of active cells, so repeated adjacent
    /// effects merge automatically. When more than <see cref="GlobalRules.World.MaxActiveCellRegions"/> components
    /// exist, the smallest component (ties: lowest cell index) is merged into its nearest neighbor (bounding-box gap,
    /// ties: lowest cell index) until the cap holds. Merging changes only the representation; no cell state is ever
    /// deleted to satisfy the cap (spec L136 "rather than deleting meaningful states at random").
    /// </summary>
    public sealed class ActiveRegions
    {
        private readonly List<ActiveRegion> _regions = new List<ActiveRegion>();
        private readonly Stack<int> _stack = new Stack<int>();
        private int[] _mark = new int[0];
        private int _stamp;

        public ActiveRegions(int cap = GlobalRules.World.MaxActiveCellRegions)
        {
            if (cap < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(cap));
            }

            Cap = cap;
        }

        public int Cap { get; }

        public IReadOnlyList<ActiveRegion> Regions => _regions;

        /// <summary>8-connected components found before cap merging in the last rebuild.</summary>
        public int ComponentCount { get; private set; }

        /// <summary>Rebuilds regions from the sorted list of active cell indices.</summary>
        public void Rebuild(CellGrid grid, IReadOnlyList<int> sortedActive, bool[] isActive)
        {
            _regions.Clear();
            if (_mark.Length != grid.CellCount)
            {
                _mark = new int[grid.CellCount];
                _stamp = 0;
            }

            _stamp++;
            for (int i = 0; i < sortedActive.Count; i++)
            {
                int seed = sortedActive[i];
                if (_mark[seed] == _stamp)
                {
                    continue;
                }

                var region = new ActiveRegion();
                FloodFill(grid, seed, isActive, region);
                region.CellList.Sort();
                _regions.Add(region);
            }

            ComponentCount = _regions.Count;
            EnforceCap(grid);
            _regions.Sort((a, b) => a.MinIndex.CompareTo(b.MinIndex));
            for (int i = 0; i < _regions.Count; i++)
            {
                _regions[i].Id = i;
            }
        }

        private void FloodFill(CellGrid grid, int seed, bool[] isActive, ActiveRegion region)
        {
            _stack.Clear();
            _stack.Push(seed);
            _mark[seed] = _stamp;
            region.MinX = region.MaxX = grid.XOf(seed);
            region.MinZ = region.MaxZ = grid.ZOf(seed);
            while (_stack.Count > 0)
            {
                int index = _stack.Pop();
                region.CellList.Add(index);
                int x = grid.XOf(index);
                int z = grid.ZOf(index);
                region.MinX = Math.Min(region.MinX, x);
                region.MaxX = Math.Max(region.MaxX, x);
                region.MinZ = Math.Min(region.MinZ, z);
                region.MaxZ = Math.Max(region.MaxZ, z);
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0)
                        {
                            continue;
                        }

                        int nx = x + dx;
                        int nz = z + dz;
                        if (!grid.InBounds(nx, nz))
                        {
                            continue;
                        }

                        int n = grid.IndexOf(nx, nz);
                        if (isActive[n] && _mark[n] != _stamp)
                        {
                            _mark[n] = _stamp;
                            _stack.Push(n);
                        }
                    }
                }
            }
        }

        private void EnforceCap(CellGrid grid)
        {
            while (_regions.Count > Cap)
            {
                int smallest = 0;
                for (int i = 1; i < _regions.Count; i++)
                {
                    if (IsSmaller(_regions[i], _regions[smallest]))
                    {
                        smallest = i;
                    }
                }

                ActiveRegion source = _regions[smallest];
                int target = -1;
                int bestGap = int.MaxValue;
                for (int i = 0; i < _regions.Count; i++)
                {
                    if (i == smallest)
                    {
                        continue;
                    }

                    int gap = Gap(source, _regions[i]);
                    if (gap < bestGap || (gap == bestGap && _regions[i].MinIndex < _regions[target].MinIndex))
                    {
                        bestGap = gap;
                        target = i;
                    }
                }

                ActiveRegion into = _regions[target];
                into.CellList.AddRange(source.CellList);
                into.CellList.Sort();
                into.MinX = Math.Min(into.MinX, source.MinX);
                into.MinZ = Math.Min(into.MinZ, source.MinZ);
                into.MaxX = Math.Max(into.MaxX, source.MaxX);
                into.MaxZ = Math.Max(into.MaxZ, source.MaxZ);
                into.Parts += source.Parts;
                _regions.RemoveAt(smallest);
            }
        }

        private static bool IsSmaller(ActiveRegion a, ActiveRegion b)
        {
            if (a.CellCount != b.CellCount)
            {
                return a.CellCount < b.CellCount;
            }

            return a.MinIndex < b.MinIndex;
        }

        /// <summary>Chebyshev gap in cells between two bounding boxes (0 when they touch or overlap).</summary>
        private static int Gap(ActiveRegion a, ActiveRegion b)
        {
            int gx = Math.Max(0, Math.Max(a.MinX - b.MaxX, b.MinX - a.MaxX) - 1);
            int gz = Math.Max(0, Math.Max(a.MinZ - b.MaxZ, b.MinZ - a.MaxZ) - 1);
            return Math.Max(gx, gz);
        }
    }
}
