using System;
using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Reactions.Grid;

namespace Confluence.Reactions.Volumes
{
    /// <summary>
    /// A server-owned state volume: a set of 0.5 m columns from <see cref="BaseY"/> to <see cref="TopY"/>, translated
    /// by a continuous wind offset (spec L134, L151, L167). Rendered particles only illustrate it (L134). A volume
    /// never intercepts projectiles, never disables audio, and never applies Wet by itself (L167, SPL-012).
    /// </summary>
    public sealed class ObscurationVolume
    {
        internal ObscurationVolume(int id, VolumeKind kind, Vec2 gridOrigin)
        {
            Id = id;
            Kind = kind;
            GridOrigin = gridOrigin;
            Columns = new ColumnSet();
        }

        public int Id { get; internal set; }
        public VolumeKind Kind { get; internal set; }

        /// <summary>Bookkeeping owner (spec L670: the last direct source that committed the reaction); MapOwner for map volumes.</summary>
        public int Owner { get; internal set; }

        public int SourceEventId { get; internal set; }

        /// <summary>Sim field id this volume belongs to (0 if the volume is its own field).</summary>
        public int FieldId { get; internal set; }

        public int CreatedTick { get; internal set; }
        public int ExpiresTick { get; internal set; }

        public float BaseY { get; internal set; }
        public float Height { get; internal set; }
        public float TopY => BaseY + Height;

        /// <summary>Accumulated wind translation on the ground plane (X, world Z).</summary>
        public Vec2 Offset { get; internal set; }

        /// <summary>Origin of the grid the columns are indexed on.</summary>
        public Vec2 GridOrigin { get; }

        public ColumnSet Columns { get; }
        public int ColumnCount => Columns.Count;

        public bool BlocksSight { get; internal set; }
        public bool WindMovable { get; internal set; }
        public bool DrainedByLight { get; internal set; }

        /// <summary>
        /// True for a player-created cloud or field, which counts against its owner's three-field limit (spec L128,
        /// L670). Map-owned volumes never count.
        /// </summary>
        public bool CountsAsField { get; internal set; }

        /// <summary>Removable by a declared dispel (spec L161).</summary>
        public bool Removable { get; internal set; }

        /// <summary>Formed by a reaction (steam) rather than created directly by a spell.</summary>
        public bool IsReactionProduct { get; internal set; }

        public float DamagePerSecond { get; internal set; }
        public DamageChannel Channel { get; internal set; }

        /// <summary>A declared soluble venom effect dilutes to 50 percent in water (spec L159).</summary>
        public bool Soluble { get; internal set; }

        public bool Diluted { get; internal set; }

        /// <summary>Damage multiplier after dilution.</summary>
        public float DamageFactor => Diluted ? GlobalRules.World.VenomDilutionFactor : 1f;

        /// <summary>
        /// Water units consumed to form this cloud: the most it can return when condensed (spec L165, L167 "condense it
        /// back into bounded water"), so burn-water-steam-condense loops lose material every cycle.
        /// </summary>
        public int SourceWater { get; internal set; }

        public bool IsMapOwned => Owner == ReactionRules.MapOwner;

        public int RemainingTicks(int tick) => Math.Max(0, ExpiresTick - tick);

        /// <summary>World ground-plane center of a column.</summary>
        public Vec2 ColumnCenter(int x, int z) =>
            new Vec2(GridOrigin.X + (x + 0.5f) * CellGrid.CellSize + Offset.X, GridOrigin.Y + (z + 0.5f) * CellGrid.CellSize + Offset.Y);

        /// <summary>Column holding a ground point, after removing the offset.</summary>
        public void ColumnOf(Vec2 point, out int x, out int z)
        {
            x = (int)MathF.Floor((point.X - GridOrigin.X - Offset.X) / CellGrid.CellSize);
            z = (int)MathF.Floor((point.Y - GridOrigin.Y - Offset.Y) / CellGrid.CellSize);
        }

        public bool ContainsPoint(Vec3 point)
        {
            if (point.Y < BaseY || point.Y > TopY)
            {
                return false;
            }

            ColumnOf(point.XZ(), out int x, out int z);
            return Columns.Contains(x, z);
        }

        /// <summary>Ground-plane bounds of the occupied columns (empty volumes return a degenerate box at the origin).</summary>
        public void GetBounds(out Vec2 min, out Vec2 max)
        {
            float cs = CellGrid.CellSize;
            min = new Vec2(GridOrigin.X + Columns.MinX * cs + Offset.X, GridOrigin.Y + Columns.MinZ * cs + Offset.Y);
            max = new Vec2(GridOrigin.X + (Columns.MaxX + 1) * cs + Offset.X, GridOrigin.Y + (Columns.MaxZ + 1) * cs + Offset.Y);
        }

        /// <summary>True when a vertical cylinder (a body) overlaps any column of the volume.</summary>
        public bool OverlapsCylinder(Vec2 center, float radius, float bottom, float top)
        {
            if (Columns.IsEmpty || top < BaseY || bottom > TopY)
            {
                return false;
            }

            float cs = CellGrid.CellSize;
            ColumnOf(center - new Vec2(radius, radius), out int x0, out int z0);
            ColumnOf(center + new Vec2(radius, radius), out int x1, out int z1);
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (!Columns.Contains(x, z))
                    {
                        continue;
                    }

                    float minX = GridOrigin.X + x * cs + Offset.X;
                    float minZ = GridOrigin.Y + z * cs + Offset.Y;
                    float cx = CMath.Clamp(center.X, minX, minX + cs);
                    float cz = CMath.Clamp(center.Y, minZ, minZ + cs);
                    float dx = center.X - cx;
                    float dz = center.Y - cz;
                    if (dx * dx + dz * dz <= radius * radius)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>True when any column center lies inside the ground-plane disc.</summary>
        public bool AnyColumnCenterWithin(Vec2 center, float radius)
        {
            if (Columns.IsEmpty)
            {
                return false;
            }

            ColumnOf(center - new Vec2(radius, radius), out int x0, out int z0);
            ColumnOf(center + new Vec2(radius, radius), out int x1, out int z1);
            float rSq = radius * radius;
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (Columns.Contains(x, z) && Vec2.DistanceSquared(ColumnCenter(x, z), center) <= rSq)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Appends the parameter intervals [t0, t1] (0 = from, 1 = to) during which the segment lies inside the
        /// volume. Exact: a 2D grid traversal over the columns intersected with the vertical extent.
        /// </summary>
        public void AppendSegmentIntervals(Vec3 from, Vec3 to, List<float> starts, List<float> ends)
        {
            if (Columns.IsEmpty)
            {
                return;
            }

            float cs = CellGrid.CellSize;
            float u0 = (from.X - GridOrigin.X - Offset.X) / cs;
            float v0 = (from.Z - GridOrigin.Y - Offset.Y) / cs;
            float du = (to.X - GridOrigin.X - Offset.X) / cs - u0;
            float dv = (to.Z - GridOrigin.Y - Offset.Y) / cs - v0;

            float tA = 0f;
            float tB = 1f;
            if (!ClipSlab(from.Y, to.Y - from.Y, BaseY, TopY, ref tA, ref tB) ||
                !ClipSlab(u0, du, Columns.MinX, Columns.MaxX + 1, ref tA, ref tB) ||
                !ClipSlab(v0, dv, Columns.MinZ, Columns.MaxZ + 1, ref tA, ref tB) ||
                tB <= tA)
            {
                return;
            }

            float tStartProbe = tA + (tB - tA) * 1e-4f;
            int i = (int)MathF.Floor(u0 + du * tStartProbe);
            int j = (int)MathF.Floor(v0 + dv * tStartProbe);
            i = Math.Max(Columns.MinX, Math.Min(Columns.MaxX, i));
            j = Math.Max(Columns.MinZ, Math.Min(Columns.MaxZ, j));
            int stepI = du > 0f ? 1 : (du < 0f ? -1 : 0);
            int stepJ = dv > 0f ? 1 : (dv < 0f ? -1 : 0);

            float t = tA;
            int guard = Columns.SizeX + Columns.SizeZ + 4;
            while (guard-- > 0)
            {
                float tNextI = stepI > 0 ? (i + 1 - u0) / du : (stepI < 0 ? (i - u0) / du : float.PositiveInfinity);
                float tNextJ = stepJ > 0 ? (j + 1 - v0) / dv : (stepJ < 0 ? (j - v0) / dv : float.PositiveInfinity);
                float tExit = MathF.Min(MathF.Min(tNextI, tNextJ), tB);
                if (tExit > t && Columns.Contains(i, j))
                {
                    AppendInterval(starts, ends, t, tExit);
                }

                if (tExit >= tB)
                {
                    break;
                }

                t = MathF.Max(t, tExit);
                if (tNextI < tNextJ)
                {
                    i += stepI;
                }
                else if (tNextJ < tNextI)
                {
                    j += stepJ;
                }
                else
                {
                    i += stepI;
                    j += stepJ;
                }

                if (i < Columns.MinX || i > Columns.MaxX || j < Columns.MinZ || j > Columns.MaxZ)
                {
                    break;
                }
            }
        }

        private static void AppendInterval(List<float> starts, List<float> ends, float t0, float t1)
        {
            int last = starts.Count - 1;
            if (last >= 0 && MathF.Abs(ends[last] - t0) <= 1e-7f)
            {
                ends[last] = t1; // contiguous columns of one volume extend the previous interval
                return;
            }

            starts.Add(t0);
            ends.Add(t1);
        }

        /// <summary>Liang-Barsky slab clip of p(t) = p0 + d t against [min, max].</summary>
        private static bool ClipSlab(float p0, float d, float min, float max, ref float tA, ref float tB)
        {
            if (MathF.Abs(d) < 1e-9f)
            {
                return p0 >= min && p0 <= max;
            }

            float t0 = (min - p0) / d;
            float t1 = (max - p0) / d;
            if (t0 > t1)
            {
                float swap = t0;
                t0 = t1;
                t1 = swap;
            }

            tA = MathF.Max(tA, t0);
            tB = MathF.Min(tB, t1);
            return tB >= tA;
        }

        public override string ToString() => Kind + "#" + Id + " owner " + Owner + " cols " + ColumnCount;
    }
}
