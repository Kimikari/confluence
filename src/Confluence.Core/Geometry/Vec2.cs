using System;
using System.Globalization;

namespace Confluence.Core.Geometry
{
    /// <summary>
    /// Single-precision 2D vector. When it represents a ground-plane position, X is world X and
    /// Y is world Z (see <see cref="Vec3.XZ"/> and <see cref="ToXZ"/>).
    /// </summary>
    public struct Vec2 : IEquatable<Vec2>
    {
        public float X;
        public float Y;

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static Vec2 Zero => default;
        public static Vec2 One => new Vec2(1f, 1f);
        public static Vec2 UnitX => new Vec2(1f, 0f);
        public static Vec2 UnitY => new Vec2(0f, 1f);

        /// <summary>World +X on the ground plane.</summary>
        public static Vec2 Right => new Vec2(1f, 0f);

        /// <summary>World +Z on the ground plane.</summary>
        public static Vec2 Forward => new Vec2(0f, 1f);

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator *(float s, Vec2 a) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator /(Vec2 a, float s) => new Vec2(a.X / s, a.Y / s);
        public static bool operator ==(Vec2 a, Vec2 b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(Vec2 a, Vec2 b) => !(a == b);

        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

        /// <summary>Scalar 2D cross product (z component of the 3D cross of (a,0) and (b,0)).</summary>
        public static float Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;

        public static float Distance(Vec2 a, Vec2 b) => (a - b).Length();
        public static float DistanceSquared(Vec2 a, Vec2 b) => (a - b).LengthSquared();

        public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => LerpUnclamped(a, b, CMath.Clamp01(t));

        public static Vec2 LerpUnclamped(Vec2 a, Vec2 b, float t) => new Vec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

        public static Vec2 Min(Vec2 a, Vec2 b) => new Vec2(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y));
        public static Vec2 Max(Vec2 a, Vec2 b) => new Vec2(MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y));
        public static Vec2 Scale(Vec2 a, Vec2 b) => new Vec2(a.X * b.X, a.Y * b.Y);

        /// <summary>Ground-plane unit direction for a yaw angle: yaw 0 is +Z (Y here), positive yaw turns right.</summary>
        public static Vec2 FromYaw(float yawDeg)
        {
            CMath.SinCosDeg(yawDeg, out float s, out float c);
            return new Vec2(s, c);
        }

        public float Length() => MathF.Sqrt(X * X + Y * Y);
        public float LengthSquared() => X * X + Y * Y;

        /// <summary>Unit vector in the same direction, or zero for a (near) zero vector.</summary>
        public Vec2 Normalized()
        {
            float lenSq = X * X + Y * Y;
            if (lenSq < CMath.NormalizeEpsilonSq)
            {
                return Zero;
            }

            float inv = 1f / MathF.Sqrt(lenSq);
            return new Vec2(X * inv, Y * inv);
        }

        /// <summary>Counter-clockwise perpendicular in standard math orientation: (-Y, X).</summary>
        public Vec2 Perp() => new Vec2(-Y, X);

        /// <summary>Rotates a ground-plane vector by a yaw delta (positive turns right / clockwise seen from above).</summary>
        public Vec2 RotatedByYaw(float yawDeg)
        {
            CMath.SinCosDeg(yawDeg, out float s, out float c);
            return new Vec2(X * c + Y * s, -X * s + Y * c);
        }

        /// <summary>Lifts a ground-plane vector into 3D: (X, y, Y).</summary>
        public Vec3 ToXZ(float y = 0f) => new Vec3(X, y, Y);

        public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);

        public override bool Equals(object obj) => obj is Vec2 other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (X.GetHashCode() * 397) ^ Y.GetHashCode();
            }
        }

        public override string ToString() => ToString("0.####");

        public string ToString(string format)
        {
            return "(" + X.ToString(format, CultureInfo.InvariantCulture) + ", "
                + Y.ToString(format, CultureInfo.InvariantCulture) + ")";
        }
    }
}
