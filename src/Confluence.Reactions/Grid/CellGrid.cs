using System;
using System.Collections.Generic;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Reactions.Grid
{
    /// <summary>
    /// The 0.5 m interaction grid over one arena (spec L136). The grid is the square circumscribing the arena's
    /// diameter, centered on the arena center; cell (x, z) covers [Origin + (x, z) * CellSize, + CellSize) on the
    /// ground plane and has index z * Width + x. Index order is the fixed iteration order of every phase.
    /// </summary>
    public sealed class CellGrid
    {
        public const float CellSize = GlobalRules.World.CellSize;

        private readonly Cell[] _cells;

        public CellGrid(Vec2 origin, int width, int depth)
        {
            if (width <= 0 || depth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Grid dimensions must be positive.");
            }

            Origin = origin;
            Width = width;
            Depth = depth;
            _cells = new Cell[width * depth];
            for (int i = 0; i < _cells.Length; i++)
            {
                _cells[i] = Cell.Empty();
            }
        }

        /// <summary>Grid covering the square around an arena's diameter (24-48 m: 48-96 cells per side).</summary>
        public static CellGrid ForArena(ArenaLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            return ForDiameter(layout.Center.XZ(), layout.Diameter);
        }

        public static CellGrid ForDiameter(Vec2 center, float diameter)
        {
            if (!(diameter > 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(diameter), "Arena diameter must be positive.");
            }

            int cells = (int)MathF.Ceiling(diameter / CellSize - 1e-4f);
            float side = cells * CellSize;
            return new CellGrid(center - new Vec2(side * 0.5f, side * 0.5f), cells, cells);
        }

        /// <summary>Minimum ground-plane corner (X, world Z).</summary>
        public Vec2 Origin { get; }

        public int Width { get; }
        public int Depth { get; }
        public int CellCount => _cells.Length;

        internal Cell[] Cells => _cells;

        internal ref Cell this[int index] => ref _cells[index];

        public int IndexOf(int x, int z) => z * Width + x;
        public int XOf(int index) => index % Width;
        public int ZOf(int index) => index / Width;

        public bool InBounds(int x, int z) => x >= 0 && z >= 0 && x < Width && z < Depth;

        /// <summary>Integer cell coordinates of a ground point (may lie outside the grid).</summary>
        public void CoordOf(Vec2 point, out int x, out int z)
        {
            x = (int)MathF.Floor((point.X - Origin.X) / CellSize);
            z = (int)MathF.Floor((point.Y - Origin.Y) / CellSize);
        }

        public bool TryGetIndex(Vec2 point, out int index)
        {
            CoordOf(point, out int x, out int z);
            if (!InBounds(x, z))
            {
                index = -1;
                return false;
            }

            index = IndexOf(x, z);
            return true;
        }

        public bool TryGetIndex(Vec3 point, out int index) => TryGetIndex(point.XZ(), out index);

        public Vec2 CellCenter(int x, int z) => new Vec2(Origin.X + (x + 0.5f) * CellSize, Origin.Y + (z + 0.5f) * CellSize);

        public Vec2 CellCenter(int index) => CellCenter(XOf(index), ZOf(index));

        /// <summary>Distance in cells between two indices (Chebyshev).</summary>
        public int CellDistance(int a, int b) => Math.Max(Math.Abs(XOf(a) - XOf(b)), Math.Abs(ZOf(a) - ZOf(b)));

        /// <summary>
        /// Appends, in index order, every cell whose center lies inside the footprint. A footprint covering no
        /// cell center still covers the cell holding its anchor point, so a deposit is never silently lost.
        /// Returns the number of cells appended.
        /// </summary>
        public int Rasterize(Footprint footprint, List<int> cells)
        {
            int before = cells.Count;
            if (footprint.Shape != FootprintShape.Point)
            {
                footprint.GetBounds(out Vec2 min, out Vec2 max);
                CoordOf(min, out int x0, out int z0);
                CoordOf(max, out int x1, out int z1);
                x0 = Math.Max(0, x0);
                z0 = Math.Max(0, z0);
                x1 = Math.Min(Width - 1, x1);
                z1 = Math.Min(Depth - 1, z1);
                for (int z = z0; z <= z1; z++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        if (footprint.Contains(CellCenter(x, z)))
                        {
                            cells.Add(IndexOf(x, z));
                        }
                    }
                }
            }

            if (cells.Count == before && TryGetIndex(footprint.Anchor, out int anchor))
            {
                cells.Add(anchor);
            }

            return cells.Count - before;
        }

        /// <summary>Appends, in index order, cells whose centers lie within the radius of the point (excluding none).</summary>
        public int CellsWithin(Vec2 center, float radius, List<int> cells) => Rasterize(Footprint.Circle(center, radius), cells);

        /// <summary>Appends the in-grid 4-neighbors of a cell in the fixed order +X, -X, +Z, -Z.</summary>
        public void Neighbors4(int index, List<int> neighbors)
        {
            int x = XOf(index);
            int z = ZOf(index);
            if (x + 1 < Width) neighbors.Add(index + 1);
            if (x - 1 >= 0) neighbors.Add(index - 1);
            if (z + 1 < Depth) neighbors.Add(index + Width);
            if (z - 1 >= 0) neighbors.Add(index - Width);
        }

        /// <summary>True when the ground-plane disc overlaps the cell square.</summary>
        public bool DiscOverlapsCell(Vec2 center, float radius, int index)
        {
            int x = XOf(index);
            int z = ZOf(index);
            float minX = Origin.X + x * CellSize;
            float minZ = Origin.Y + z * CellSize;
            float cx = CMath.Clamp(center.X, minX, minX + CellSize);
            float cz = CMath.Clamp(center.Y, minZ, minZ + CellSize);
            float dx = center.X - cx;
            float dz = center.Y - cz;
            return dx * dx + dz * dz <= radius * radius;
        }

        /// <summary>Appends, in index order, cells whose squares overlap the disc.</summary>
        public void CellsOverlappingDisc(Vec2 center, float radius, List<int> cells)
        {
            CoordOf(center - new Vec2(radius, radius), out int x0, out int z0);
            CoordOf(center + new Vec2(radius, radius), out int x1, out int z1);
            x0 = Math.Max(0, x0);
            z0 = Math.Max(0, z0);
            x1 = Math.Min(Width - 1, x1);
            z1 = Math.Min(Depth - 1, z1);
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    int index = IndexOf(x, z);
                    if (DiscOverlapsCell(center, radius, index))
                    {
                        cells.Add(index);
                    }
                }
            }
        }
    }
}
