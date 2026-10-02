using System;

namespace Confluence.Core.Geometry
{
    /// <summary>
    /// Scalar and angle helpers. Angles are degrees unless a name says otherwise.
    /// Yaw 0 faces +Z and positive yaw turns right (clockwise seen from above): forward = (sin yaw, 0, cos yaw).
    /// Pitch is elevation: positive pitch looks up (Unity's euler X angle is its negation).
    /// </summary>
    public static class CMath
    {
        public const float Pi = 3.14159265358979323846f;
        public const float TwoPi = 2f * Pi;
        public const float Deg2Rad = Pi / 180f;
        public const float Rad2Deg = 180f / Pi;

        /// <summary>General geometric tolerance in meters.</summary>
        public const float Epsilon = 1e-6f;

        /// <summary>Squared length below which a vector is treated as zero when normalizing.</summary>
        public const float NormalizeEpsilonSq = 1e-12f;

        public static float Clamp(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        public static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        public static float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }

            return value > 1f ? 1f : value;
        }

        /// <summary>Linear interpolation with t clamped to [0, 1]. Returns exactly b at t = 1.</summary>
        public static float Lerp(float a, float b, float t) => LerpUnclamped(a, b, Clamp01(t));

        /// <summary>Linear interpolation; exact at both endpoints.</summary>
        public static float LerpUnclamped(float a, float b, float t) => a * (1f - t) + b * t;

        /// <summary>Fraction of value between a and b, clamped to [0, 1]. Returns 0 when a == b.</summary>
        public static float InverseLerp(float a, float b, float value)
        {
            if (a == b)
            {
                return 0f;
            }

            return Clamp01((value - a) / (b - a));
        }

        public static float MoveTowards(float current, float target, float maxDelta)
        {
            if (MathF.Abs(target - current) <= maxDelta)
            {
                return target;
            }

            return current + MathF.Sign(target - current) * maxDelta;
        }

        /// <summary>True when a and b differ by at most tolerance, scaled up for magnitudes above 1.</summary>
        public static bool Approximately(float a, float b, float tolerance = 1e-5f)
        {
            float scale = MathF.Max(1f, MathF.Max(MathF.Abs(a), MathF.Abs(b)));
            return MathF.Abs(a - b) <= tolerance * scale;
        }

        public static float DegToRad(float degrees) => degrees * Deg2Rad;
        public static float RadToDeg(float radians) => radians * Rad2Deg;

        /// <summary>Wraps an angle into (-180, 180].</summary>
        public static float WrapAngle(float degrees)
        {
            float a = degrees % 360f;
            if (a <= -180f)
            {
                a += 360f;
            }
            else if (a > 180f)
            {
                a -= 360f;
            }

            return a;
        }

        /// <summary>Wraps an angle into [0, 360).</summary>
        public static float WrapAngle360(float degrees)
        {
            float a = degrees % 360f;
            if (a < 0f)
            {
                a += 360f;
            }

            return a >= 360f ? 0f : a;
        }

        /// <summary>Shortest signed rotation from current to target, in (-180, 180].</summary>
        public static float DeltaAngle(float currentDeg, float targetDeg) => WrapAngle(targetDeg - currentDeg);

        public static float MoveTowardsAngle(float currentDeg, float targetDeg, float maxDeltaDeg)
        {
            float delta = DeltaAngle(currentDeg, targetDeg);
            if (MathF.Abs(delta) <= maxDeltaDeg)
            {
                return targetDeg;
            }

            return currentDeg + MathF.Sign(delta) * maxDeltaDeg;
        }

        /// <summary>
        /// Sine and cosine of an angle in degrees. Exact for multiples of 90 so mirrored and axis-aligned
        /// layouts stay exactly symmetric.
        /// </summary>
        public static void SinCosDeg(float degrees, out float sin, out float cos)
        {
            double a = degrees % 360.0;
            if (a < 0.0)
            {
                a += 360.0;
            }

            if (a == 0.0)
            {
                sin = 0f;
                cos = 1f;
            }
            else if (a == 90.0)
            {
                sin = 1f;
                cos = 0f;
            }
            else if (a == 180.0)
            {
                sin = 0f;
                cos = -1f;
            }
            else if (a == 270.0)
            {
                sin = -1f;
                cos = 0f;
            }
            else
            {
                double r = a * (Math.PI / 180.0);
                sin = (float)Math.Sin(r);
                cos = (float)Math.Cos(r);
            }
        }

        /// <summary>Unit horizontal forward for a yaw: (sin yaw, 0, cos yaw).</summary>
        public static Vec3 ForwardFromYaw(float yawDeg)
        {
            SinCosDeg(yawDeg, out float s, out float c);
            return new Vec3(s, 0f, c);
        }

        /// <summary>Unit horizontal right for a yaw: (cos yaw, 0, -sin yaw).</summary>
        public static Vec3 RightFromYaw(float yawDeg)
        {
            SinCosDeg(yawDeg, out float s, out float c);
            return new Vec3(c, 0f, -s);
        }

        /// <summary>Unit view direction for a yaw and an elevation pitch (positive looks up).</summary>
        public static Vec3 ForwardFromYawPitch(float yawDeg, float pitchDeg)
        {
            SinCosDeg(yawDeg, out float sy, out float cy);
            SinCosDeg(pitchDeg, out float sp, out float cp);
            return new Vec3(sy * cp, sp, cy * cp);
        }

        /// <summary>Yaw of a direction's horizontal component in (-180, 180]. Returns 0 for a vertical or zero vector.</summary>
        public static float YawOf(Vec3 direction)
        {
            if (direction.X * direction.X + direction.Z * direction.Z < NormalizeEpsilonSq)
            {
                return 0f;
            }

            return WrapAngle(MathF.Atan2(direction.X, direction.Z) * Rad2Deg);
        }

        /// <summary>Yaw of a ground-plane direction (X, Y = world Z) in (-180, 180].</summary>
        public static float YawOf(Vec2 directionXZ) => YawOf(new Vec3(directionXZ.X, 0f, directionXZ.Y));

        /// <summary>Elevation of a direction in [-90, 90]. Returns 0 for a zero vector.</summary>
        public static float PitchOf(Vec3 direction)
        {
            float horizontal = MathF.Sqrt(direction.X * direction.X + direction.Z * direction.Z);
            if (horizontal == 0f && direction.Y == 0f)
            {
                return 0f;
            }

            return MathF.Atan2(direction.Y, horizontal) * Rad2Deg;
        }

        /// <summary>Unsigned angle between two vectors in [0, 180]. Returns 0 if either is (near) zero.</summary>
        public static float AngleBetweenDeg(Vec3 a, Vec3 b)
        {
            float denomSq = a.LengthSquared() * b.LengthSquared();
            if (denomSq < NormalizeEpsilonSq * NormalizeEpsilonSq)
            {
                return 0f;
            }

            float cos = Clamp(Vec3.Dot(a, b) / MathF.Sqrt(denomSq), -1f, 1f);
            return MathF.Acos(cos) * Rad2Deg;
        }

        /// <summary>Unsigned angle between two 2D vectors in [0, 180]. Returns 0 if either is (near) zero.</summary>
        public static float AngleBetweenDeg(Vec2 a, Vec2 b) => AngleBetweenDeg(a.ToXZ(), b.ToXZ());

        /// <summary>Hermite smoothstep of t clamped to [0, 1]; exactly 0 and 1 at the ends.</summary>
        public static float SmoothStep01(float t)
        {
            t = Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Smoothstep of x between edge0 and edge1 (a step at edge1 when the edges coincide).</summary>
        public static float SmoothStep(float edge0, float edge1, float x)
        {
            if (edge1 <= edge0)
            {
                return x >= edge1 ? 1f : 0f;
            }

            return SmoothStep01((x - edge0) / (edge1 - edge0));
        }

        public static float Square(float x) => x * x;
    }
}
