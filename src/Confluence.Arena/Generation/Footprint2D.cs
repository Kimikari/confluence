using System;
using Confluence.Core.Geometry;

namespace Confluence.Arena.Generation
{
    /// <summary>Ground-plane distance and overlap between yaw-box footprints (convex quadrilaterals).</summary>
    public static class Footprint2D
    {
        /// <summary>
        /// Horizontal gap between two footprints, 0 when they touch or overlap. For disjoint convex polygons the
        /// closest pair always involves a vertex of one and an edge of the other.
        /// </summary>
        public static float Distance(YawBox a, YawBox b)
        {
            if (Overlap(a, b))
            {
                return 0f;
            }

            float best = float.PositiveInfinity;
            for (int i = 0; i < 4; i++)
            {
                Vec2 va = a.FootprintCorner(i);
                best = Math.Min(best, Vec2.Distance(b.ClosestPointXZ(va), va));
                Vec2 vb = b.FootprintCorner(i);
                best = Math.Min(best, Vec2.Distance(a.ClosestPointXZ(vb), vb));
            }

            return best;
        }

        /// <summary>Separating-axis overlap test of two rotated rectangles.</summary>
        public static bool Overlap(YawBox a, YawBox b)
        {
            return !Separated(a, b, a.Right.XZ()) && !Separated(a, b, a.Forward.XZ())
                && !Separated(a, b, b.Right.XZ()) && !Separated(a, b, b.Forward.XZ());
        }

        private static bool Separated(YawBox a, YawBox b, Vec2 axis)
        {
            Project(a, axis, out float a0, out float a1);
            Project(b, axis, out float b0, out float b1);
            return a1 < b0 || b1 < a0;
        }

        private static void Project(YawBox box, Vec2 axis, out float min, out float max)
        {
            min = float.PositiveInfinity;
            max = float.NegativeInfinity;
            for (int i = 0; i < 4; i++)
            {
                float d = Vec2.Dot(box.FootprintCorner(i), axis);
                min = Math.Min(min, d);
                max = Math.Max(max, d);
            }
        }
    }
}
