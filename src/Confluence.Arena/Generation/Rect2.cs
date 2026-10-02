using System;
using Confluence.Core.Geometry;

namespace Confluence.Arena.Generation
{
    /// <summary>World-aligned ground rectangle (X by Z) used for module footprints and keep-out zones.</summary>
    public struct Rect2
    {
        public float MinX;
        public float MinZ;
        public float MaxX;
        public float MaxZ;

        public Rect2(float x0, float z0, float x1, float z1)
        {
            MinX = Math.Min(x0, x1);
            MinZ = Math.Min(z0, z1);
            MaxX = Math.Max(x0, x1);
            MaxZ = Math.Max(z0, z1);
        }

        public static Rect2 FromCenter(Vec2 center, float sizeX, float sizeZ)
        {
            return new Rect2(center.X - sizeX * 0.5f, center.Y - sizeZ * 0.5f, center.X + sizeX * 0.5f, center.Y + sizeZ * 0.5f);
        }

        /// <summary>World-aligned bounds of a yaw box footprint.</summary>
        public static Rect2 Around(YawBox box)
        {
            Aabb b = box.Bounds();
            return new Rect2(b.Min.X, b.Min.Z, b.Max.X, b.Max.Z);
        }

        public float Width => MaxX - MinX;
        public float Depth => MaxZ - MinZ;
        public Vec2 Center => new Vec2((MinX + MaxX) * 0.5f, (MinZ + MaxZ) * 0.5f);

        public Rect2 Expanded(float amount) => new Rect2(MinX - amount, MinZ - amount, MaxX + amount, MaxZ + amount);

        /// <summary>The rectangle's image under the arena half turn.</summary>
        public Rect2 Mirrored() => new Rect2(-MaxX, -MaxZ, -MinX, -MinZ);

        public bool Contains(Vec2 p) => p.X >= MinX && p.X <= MaxX && p.Y >= MinZ && p.Y <= MaxZ;

        public bool Overlaps(Rect2 other) => MinX < other.MaxX && other.MinX < MaxX && MinZ < other.MaxZ && other.MinZ < MaxZ;

        /// <summary>Euclidean gap between two rectangles (0 when they touch or overlap).</summary>
        public float DistanceTo(Rect2 other)
        {
            float dx = Math.Max(0f, Math.Max(other.MinX - MaxX, MinX - other.MaxX));
            float dz = Math.Max(0f, Math.Max(other.MinZ - MaxZ, MinZ - other.MaxZ));
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        public float DistanceTo(Vec2 p)
        {
            float dx = Math.Max(0f, Math.Max(MinX - p.X, p.X - MaxX));
            float dz = Math.Max(0f, Math.Max(MinZ - p.Y, p.Y - MaxZ));
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        public Vec2 Corner(int i)
        {
            switch (i & 3)
            {
                case 0: return new Vec2(MinX, MinZ);
                case 1: return new Vec2(MaxX, MinZ);
                case 2: return new Vec2(MaxX, MaxZ);
                default: return new Vec2(MinX, MaxZ);
            }
        }
    }
}
