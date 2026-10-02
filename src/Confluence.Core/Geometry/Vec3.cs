using System;
using System.Globalization;

namespace Confluence.Core.Geometry
{
    /// <summary>
    /// Single-precision 3D vector. Axes follow Unity: Y up, +Z forward, +X right (left-handed).
    /// </summary>
    public struct Vec3 : IEquatable<Vec3>
    {
        public float X;
        public float Y;
        public float Z;

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vec3 Zero => default;
        public static Vec3 One => new Vec3(1f, 1f, 1f);
        public static Vec3 Up => new Vec3(0f, 1f, 0f);
        public static Vec3 Down => new Vec3(0f, -1f, 0f);
        public static Vec3 Forward => new Vec3(0f, 0f, 1f);
        public static Vec3 Back => new Vec3(0f, 0f, -1f);
        public static Vec3 Right => new Vec3(1f, 0f, 0f);
        public static Vec3 Left => new Vec3(-1f, 0f, 0f);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);
        public static Vec3 operator *(Vec3 a, float s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator *(float s, Vec3 a) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator /(Vec3 a, float s) => new Vec3(a.X / s, a.Y / s, a.Z / s);
        public static bool operator ==(Vec3 a, Vec3 b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
        public static bool operator !=(Vec3 a, Vec3 b) => !(a == b);

        public static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        /// <summary>Standard cross product. With Unity axes, Cross(Right, Up) == Forward.</summary>
        public static Vec3 Cross(Vec3 a, Vec3 b)
        {
            return new Vec3(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X);
        }

        public static float Distance(Vec3 a, Vec3 b) => (a - b).Length();
        public static float DistanceSquared(Vec3 a, Vec3 b) => (a - b).LengthSquared();

        /// <summary>Horizontal (ground-plane) distance, ignoring Y.</summary>
        public static float DistanceXZ(Vec3 a, Vec3 b)
        {
            float dx = a.X - b.X;
            float dz = a.Z - b.Z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        public static Vec3 Lerp(Vec3 a, Vec3 b, float t) => LerpUnclamped(a, b, CMath.Clamp01(t));

        public static Vec3 LerpUnclamped(Vec3 a, Vec3 b, float t)
        {
            return new Vec3(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
        }

        public static Vec3 Min(Vec3 a, Vec3 b) => new Vec3(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Min(a.Z, b.Z));
        public static Vec3 Max(Vec3 a, Vec3 b) => new Vec3(MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y), MathF.Max(a.Z, b.Z));
        public static Vec3 Scale(Vec3 a, Vec3 b) => new Vec3(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
        public static Vec3 Abs(Vec3 a) => new Vec3(MathF.Abs(a.X), MathF.Abs(a.Y), MathF.Abs(a.Z));

        /// <summary>Moves current toward target by at most maxDistanceDelta without overshooting.</summary>
        public static Vec3 MoveTowards(Vec3 current, Vec3 target, float maxDistanceDelta)
        {
            Vec3 delta = target - current;
            float distSq = delta.LengthSquared();
            if (distSq == 0f || (maxDistanceDelta >= 0f && distSq <= maxDistanceDelta * maxDistanceDelta))
            {
                return target;
            }

            float dist = MathF.Sqrt(distSq);
            return current + delta * (maxDistanceDelta / dist);
        }

        /// <summary>Removes the component of v along a unit normal.</summary>
        public static Vec3 ProjectOnPlane(Vec3 v, Vec3 unitNormal) => v - unitNormal * Dot(v, unitNormal);

        public float Length() => MathF.Sqrt(X * X + Y * Y + Z * Z);
        public float LengthSquared() => X * X + Y * Y + Z * Z;
        public float LengthXZ() => MathF.Sqrt(X * X + Z * Z);

        /// <summary>Unit vector in the same direction, or zero for a (near) zero vector.</summary>
        public Vec3 Normalized()
        {
            float lenSq = X * X + Y * Y + Z * Z;
            if (lenSq < CMath.NormalizeEpsilonSq)
            {
                return Zero;
            }

            float inv = 1f / MathF.Sqrt(lenSq);
            return new Vec3(X * inv, Y * inv, Z * inv);
        }

        /// <summary>Unit vector in the same direction, or the fallback for a (near) zero vector.</summary>
        public Vec3 NormalizedOr(Vec3 fallback)
        {
            float lenSq = X * X + Y * Y + Z * Z;
            if (lenSq < CMath.NormalizeEpsilonSq)
            {
                return fallback;
            }

            float inv = 1f / MathF.Sqrt(lenSq);
            return new Vec3(X * inv, Y * inv, Z * inv);
        }

        /// <summary>Ground-plane projection (X, Z) as a Vec2.</summary>
        public Vec2 XZ() => new Vec2(X, Z);

        public Vec3 WithX(float x) => new Vec3(x, Y, Z);
        public Vec3 WithY(float y) => new Vec3(X, y, Z);
        public Vec3 WithZ(float z) => new Vec3(X, Y, z);

        public bool Equals(Vec3 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

        public override bool Equals(object obj) => obj is Vec3 other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X.GetHashCode();
                hash = (hash * 397) ^ Y.GetHashCode();
                hash = (hash * 397) ^ Z.GetHashCode();
                return hash;
            }
        }

        public override string ToString() => ToString("0.####");

        public string ToString(string format)
        {
            return "(" + X.ToString(format, CultureInfo.InvariantCulture) + ", "
                + Y.ToString(format, CultureInfo.InvariantCulture) + ", "
                + Z.ToString(format, CultureInfo.InvariantCulture) + ")";
        }
    }
}
