using System;

namespace Confluence.Core.Geometry
{
    /// <summary>Axis-aligned bounding box given by its minimum and maximum corners.</summary>
    public readonly struct Aabb
    {
        public readonly Vec3 Min;
        public readonly Vec3 Max;

        /// <summary>Creates a box from two corners in any order.</summary>
        public Aabb(Vec3 a, Vec3 b)
        {
            Min = Vec3.Min(a, b);
            Max = Vec3.Max(a, b);
        }

        public static Aabb FromCenterExtents(Vec3 center, Vec3 halfExtents)
        {
            Vec3 h = Vec3.Abs(halfExtents);
            return new Aabb(center - h, center + h);
        }

        public Vec3 Center => (Min + Max) * 0.5f;
        public Vec3 Extents => (Max - Min) * 0.5f;
        public Vec3 Size => Max - Min;

        public bool Contains(Vec3 p)
        {
            return p.X >= Min.X && p.X <= Max.X
                && p.Y >= Min.Y && p.Y <= Max.Y
                && p.Z >= Min.Z && p.Z <= Max.Z;
        }

        public bool Intersects(Aabb other)
        {
            return Min.X <= other.Max.X && Max.X >= other.Min.X
                && Min.Y <= other.Max.Y && Max.Y >= other.Min.Y
                && Min.Z <= other.Max.Z && Max.Z >= other.Min.Z;
        }

        public bool IntersectsSphere(Vec3 center, float radius) => DistanceSquared(center) <= radius * radius;

        public Vec3 ClosestPoint(Vec3 p)
        {
            return new Vec3(
                CMath.Clamp(p.X, Min.X, Max.X),
                CMath.Clamp(p.Y, Min.Y, Max.Y),
                CMath.Clamp(p.Z, Min.Z, Max.Z));
        }

        public float DistanceSquared(Vec3 p) => (p - ClosestPoint(p)).LengthSquared();

        public Aabb Encapsulate(Vec3 p) => new Aabb(Vec3.Min(Min, p), Vec3.Max(Max, p));

        public Aabb Encapsulate(Aabb other) => new Aabb(Vec3.Min(Min, other.Min), Vec3.Max(Max, other.Max));

        /// <summary>Grows (or shrinks, for negative amounts) every face by amount, never inverting the box.</summary>
        public Aabb Expanded(float amount)
        {
            Vec3 h = Extents + new Vec3(amount, amount, amount);
            return FromCenterExtents(Center, Vec3.Max(h, Vec3.Zero));
        }

        /// <summary>Slab raycast. A ray starting inside reports distance 0 and the reversed ray direction.</summary>
        public bool Raycast(Ray ray, float maxDistance, out RayHit hit)
        {
            Vec3 center = Center;
            if (!BoxMath.Raycast(ray.Origin - center, ray.Direction, Extents, maxDistance, out float t, out Vec3 n, out _))
            {
                hit = default;
                return false;
            }

            hit = new RayHit(t, ray.PointAt(t), n);
            return true;
        }

        public override string ToString() => "Aabb(" + Min + ", " + Max + ")";
    }
}
