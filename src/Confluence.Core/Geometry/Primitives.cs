using System;

namespace Confluence.Core.Geometry
{
    /// <summary>Result of a ray, segment, or sweep query against a solid.</summary>
    public struct RayHit
    {
        /// <summary>Distance along the query direction in meters (0 when the query started inside the solid).</summary>
        public float Distance;

        /// <summary>World-space contact point (for sweeps, the swept shape's center at contact).</summary>
        public Vec3 Point;

        /// <summary>Unit world-space surface normal facing the query. When starting inside, the reversed query direction.</summary>
        public Vec3 Normal;

        public RayHit(float distance, Vec3 point, Vec3 normal)
        {
            Distance = distance;
            Point = point;
            Normal = normal;
        }
    }

    /// <summary>
    /// Minimum translation for separating an overlapping shape from a solid: moving the shape by
    /// Normal * Depth resolves the overlap. Normal points out of the solid.
    /// </summary>
    public struct Penetration
    {
        public Vec3 Normal;
        public float Depth;

        public Penetration(Vec3 normal, float depth)
        {
            Normal = normal;
            Depth = depth;
        }

        public Vec3 Correction => Normal * Depth;
    }

    /// <summary>A half-line with a unit direction. A zero direction never hits anything.</summary>
    public readonly struct Ray
    {
        public readonly Vec3 Origin;
        public readonly Vec3 Direction;

        public Ray(Vec3 origin, Vec3 direction)
        {
            Origin = origin;
            Direction = direction.Normalized();
        }

        public static Ray FromTo(Vec3 from, Vec3 to) => new Ray(from, to - from);

        public Vec3 PointAt(float distance) => Origin + Direction * distance;

        public override string ToString() => "Ray(" + Origin + " -> " + Direction + ")";
    }

    public readonly struct Segment
    {
        public readonly Vec3 A;
        public readonly Vec3 B;

        public Segment(Vec3 a, Vec3 b)
        {
            A = a;
            B = b;
        }

        public Vec3 Delta => B - A;
        public float Length => (B - A).Length();
        public Vec3 Midpoint => (A + B) * 0.5f;

        public Vec3 PointAt(float t) => Vec3.LerpUnclamped(A, B, t);

        public Vec3 ClosestPoint(Vec3 point) => Intersections.ClosestPointOnSegment(point, A, B);

        public float DistanceTo(Vec3 point) => Vec3.Distance(point, ClosestPoint(point));

        public override string ToString() => "Segment(" + A + ", " + B + ")";
    }

    public readonly struct Sphere
    {
        public readonly Vec3 Center;
        public readonly float Radius;

        public Sphere(Vec3 center, float radius)
        {
            Center = center;
            Radius = MathF.Max(0f, radius);
        }

        public bool Contains(Vec3 point) => Vec3.DistanceSquared(point, Center) <= Radius * Radius;

        public bool Intersects(Sphere other)
        {
            float r = Radius + other.Radius;
            return Vec3.DistanceSquared(Center, other.Center) <= r * r;
        }

        public Aabb Bounds() => Aabb.FromCenterExtents(Center, new Vec3(Radius, Radius, Radius));

        public override string ToString() => "Sphere(" + Center + ", r=" + Radius + ")";
    }

    /// <summary>Swept sphere between two endpoint centers. Character hit volumes are vertical capsules.</summary>
    public readonly struct Capsule
    {
        public readonly Vec3 A;
        public readonly Vec3 B;
        public readonly float Radius;

        public Capsule(Vec3 a, Vec3 b, float radius)
        {
            A = a;
            B = b;
            Radius = MathF.Max(0f, radius);
        }

        /// <summary>
        /// Upright capsule standing on <paramref name="feet"/> with total height including the hemispherical caps.
        /// A height below the diameter collapses to a sphere centered at half height.
        /// </summary>
        public static Capsule Vertical(Vec3 feet, float height, float radius)
        {
            radius = MathF.Max(0f, radius);
            height = MathF.Max(0f, height);
            if (height <= 2f * radius)
            {
                Vec3 mid = feet + new Vec3(0f, height * 0.5f, 0f);
                return new Capsule(mid, mid, height * 0.5f);
            }

            return new Capsule(feet + new Vec3(0f, radius, 0f), feet + new Vec3(0f, height - radius, 0f), radius);
        }

        public Vec3 Center => (A + B) * 0.5f;

        public Capsule Translated(Vec3 delta) => new Capsule(A + delta, B + delta, Radius);

        public Vec3 ClosestPointOnAxis(Vec3 point) => Intersections.ClosestPointOnSegment(point, A, B);

        public bool Contains(Vec3 point) => Vec3.DistanceSquared(point, ClosestPointOnAxis(point)) <= Radius * Radius;

        public Aabb Bounds()
        {
            Vec3 r = new Vec3(Radius, Radius, Radius);
            return new Aabb(Vec3.Min(A, B) - r, Vec3.Max(A, B) + r);
        }

        public override string ToString() => "Capsule(" + A + ", " + B + ", r=" + Radius + ")";
    }
}
