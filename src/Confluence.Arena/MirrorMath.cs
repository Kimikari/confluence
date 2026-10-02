using System;
using Confluence.Core.Geometry;

namespace Confluence.Arena
{
    /// <summary>
    /// The arena's functional symmetry is a 180-degree turn about the center (spec L308 "functional symmetry is
    /// mandatory"): side 0 spawns on -Z, side 1 on +Z, and every gameplay element at (x, z) with yaw a has a
    /// counterpart at (-x, -z) with yaw a + 180. A turn (rather than a reflection) gives both players the same
    /// left-hand and right-hand approaches.
    /// </summary>
    public static class MirrorMath
    {
        public static Vec3 Point(Vec3 p) => new Vec3(-p.X, p.Y, -p.Z);

        public static Vec2 Point(Vec2 p) => new Vec2(-p.X, -p.Y);

        public static float Yaw(float yawDeg) => CMath.WrapAngle360(yawDeg + 180f);

        public static YawBox Box(YawBox b) => new YawBox(Point(b.Center), b.HalfExtents, Yaw(b.YawDeg));

        /// <summary>
        /// True if b is a's image under the half turn. Plain boxes compare footprint corners (a box is unchanged by
        /// its own half turn); ramps also compare the rise direction.
        /// </summary>
        public static bool IsPointMirror(YawBox a, YawBox b, float tolerance, bool directional = false)
        {
            if (MathF.Abs(a.Center.Y - b.Center.Y) > tolerance || MathF.Abs(a.HalfExtents.Y - b.HalfExtents.Y) > tolerance)
            {
                return false;
            }

            if (directional)
            {
                return Vec3.Distance(Point(a.Center), b.Center) <= tolerance
                    && Vec3.Distance(a.HalfExtents, b.HalfExtents) <= tolerance
                    && MathF.Abs(CMath.DeltaAngle(Yaw(a.YawDeg), b.YawDeg)) <= 0.01f;
            }

            for (int i = 0; i < 4; i++)
            {
                Vec2 mirrored = Point(a.FootprintCorner(i));
                bool matched = false;
                for (int j = 0; j < 4 && !matched; j++)
                {
                    matched = Vec2.Distance(mirrored, b.FootprintCorner(j)) <= tolerance * 4f;
                }

                if (!matched)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>True for points on the side 0 half (z &lt; 0, or z = 0 with x &lt; 0), the half the generator authors.</summary>
        public static bool IsSideZeroHalf(Vec2 p) => p.Y < 0f || (p.Y == 0f && p.X < 0f);
    }
}
