using System;

namespace Confluence.Core.Geometry
{
    /// <summary>
    /// Queries against an origin-centered axis-aligned box with the given half extents. Aabb and YawBox
    /// transform into this local frame and call these routines, so both share one implementation.
    /// </summary>
    internal static class BoxMath
    {
        private const float ParallelEpsilon = 1e-12f;

        public static Vec3 Clamp(Vec3 p, Vec3 half)
        {
            return new Vec3(
                CMath.Clamp(p.X, -half.X, half.X),
                CMath.Clamp(p.Y, -half.Y, half.Y),
                CMath.Clamp(p.Z, -half.Z, half.Z));
        }

        public static bool Contains(Vec3 p, Vec3 half)
        {
            return p.X >= -half.X && p.X <= half.X
                && p.Y >= -half.Y && p.Y <= half.Y
                && p.Z >= -half.Z && p.Z <= half.Z;
        }

        public static Vec3 Corner(int index, Vec3 half)
        {
            return new Vec3(
                (index & 1) != 0 ? half.X : -half.X,
                (index & 2) != 0 ? half.Y : -half.Y,
                (index & 4) != 0 ? half.Z : -half.Z);
        }

        /// <summary>Corner indices of the 12 box edges (corners that differ in exactly one axis bit).</summary>
        public static readonly int[] EdgeCorners =
        {
            0, 1, 2, 3, 4, 5, 6, 7, // X-aligned edges
            0, 2, 1, 3, 4, 6, 5, 7, // Y-aligned edges
            0, 4, 1, 5, 2, 6, 3, 7, // Z-aligned edges
        };

        /// <summary>
        /// Slab ray test. Direction need not be unit; distances are in multiples of it. A ray starting inside
        /// reports t = 0, startedInside = true, and the reversed unit direction as its normal.
        /// </summary>
        public static bool Raycast(Vec3 o, Vec3 d, Vec3 half, float maxT, out float t, out Vec3 localNormal, out bool startedInside)
        {
            t = 0f;
            localNormal = Vec3.Zero;
            startedInside = false;
            if (d.LengthSquared() < ParallelEpsilon)
            {
                return false;
            }

            float tEnter = float.NegativeInfinity;
            float tExit = float.PositiveInfinity;
            int enterAxis = -1;
            float enterSign = 0f;

            if (!ClipSlab(o.X, d.X, half.X, 0, ref tEnter, ref tExit, ref enterAxis, ref enterSign)
                || !ClipSlab(o.Y, d.Y, half.Y, 1, ref tEnter, ref tExit, ref enterAxis, ref enterSign)
                || !ClipSlab(o.Z, d.Z, half.Z, 2, ref tEnter, ref tExit, ref enterAxis, ref enterSign))
            {
                return false;
            }

            if (tExit < 0f || tEnter > maxT)
            {
                return false;
            }

            if (tEnter < 0f || enterAxis < 0)
            {
                startedInside = true;
                t = 0f;
                localNormal = -d.Normalized();
                return true;
            }

            t = tEnter;
            localNormal = AxisVector(enterAxis, enterSign);
            return true;
        }

        private static bool ClipSlab(float o, float d, float h, int axis, ref float tEnter, ref float tExit, ref int enterAxis, ref float enterSign)
        {
            if (MathF.Abs(d) < ParallelEpsilon)
            {
                return o >= -h && o <= h;
            }

            float inv = 1f / d;
            float tNear = (-h - o) * inv;
            float tFar = (h - o) * inv;
            float sign = -1f;
            if (tNear > tFar)
            {
                float tmp = tNear;
                tNear = tFar;
                tFar = tmp;
                sign = 1f;
            }

            if (tNear > tEnter)
            {
                tEnter = tNear;
                enterAxis = axis;
                enterSign = sign;
            }

            if (tFar < tExit)
            {
                tExit = tFar;
            }

            return tEnter <= tExit;
        }

        public static Vec3 AxisVector(int axis, float sign)
        {
            switch (axis)
            {
                case 0: return new Vec3(sign, 0f, 0f);
                case 1: return new Vec3(0f, sign, 0f);
                default: return new Vec3(0f, 0f, sign);
            }
        }

        /// <summary>True if the closed segment a-b touches the box.</summary>
        public static bool SegmentIntersects(Vec3 a, Vec3 b, Vec3 half)
        {
            if (Contains(a, half) || Contains(b, half))
            {
                return true;
            }

            Vec3 d = b - a;
            return Raycast(a, d, half, 1f, out _, out _, out _);
        }

        /// <summary>Sphere overlap. Normal points out of the box toward the sphere center.</summary>
        public static bool SpherePenetration(Vec3 c, float r, Vec3 half, out Vec3 normal, out float depth)
        {
            Vec3 q = Clamp(c, half);
            Vec3 diff = c - q;
            float d2 = diff.LengthSquared();
            if (d2 > 0f && d2 >= r * r)
            {
                normal = Vec3.Zero;
                depth = 0f;
                return false;
            }

            if (d2 > ParallelEpsilon)
            {
                float dist = MathF.Sqrt(d2);
                normal = diff / dist;
                depth = r - dist;
                return true;
            }

            // Center inside the box: leave through the nearest face.
            float dx = half.X - MathF.Abs(c.X);
            float dy = half.Y - MathF.Abs(c.Y);
            float dz = half.Z - MathF.Abs(c.Z);
            if (dx <= dy && dx <= dz)
            {
                normal = new Vec3(c.X >= 0f ? 1f : -1f, 0f, 0f);
                depth = dx + r;
            }
            else if (dy <= dz)
            {
                normal = new Vec3(0f, c.Y >= 0f ? 1f : -1f, 0f);
                depth = dy + r;
            }
            else
            {
                normal = new Vec3(0f, 0f, c.Z >= 0f ? 1f : -1f);
                depth = dz + r;
            }

            return true;
        }

        /// <summary>
        /// Capsule overlap (segment a-b, radius r). Separated or shallow contacts use exact closest points
        /// (endpoints and the 12 edges cover every closest-pair configuration); when the axis itself
        /// passes through the box, the minimum face-axis translation is used.
        /// </summary>
        public static bool CapsulePenetration(Vec3 a, Vec3 b, float r, Vec3 half, out Vec3 normal, out float depth)
        {
            if (SegmentIntersects(a, b, half))
            {
                FaceAxisTranslation(a, b, r, half, out normal, out depth);
                return true;
            }

            ClosestSegmentPoints(a, b, half, out Vec3 onSegment, out Vec3 onBox, out float d2);
            if (d2 >= r * r)
            {
                normal = Vec3.Zero;
                depth = 0f;
                return false;
            }

            float dist = MathF.Sqrt(d2);
            if (dist <= CMath.Epsilon)
            {
                FaceAxisTranslation(a, b, r, half, out normal, out depth);
                return true;
            }

            normal = (onSegment - onBox) / dist;
            depth = r - dist;
            return true;
        }

        /// <summary>Closest points between a segment that does not intersect the box and the box surface.</summary>
        public static void ClosestSegmentPoints(Vec3 a, Vec3 b, Vec3 half, out Vec3 onSegment, out Vec3 onBox, out float distSq)
        {
            Vec3 qa = Clamp(a, half);
            distSq = (a - qa).LengthSquared();
            onSegment = a;
            onBox = qa;

            Vec3 qb = Clamp(b, half);
            float db = (b - qb).LengthSquared();
            if (db < distSq)
            {
                distSq = db;
                onSegment = b;
                onBox = qb;
            }

            for (int i = 0; i < EdgeCorners.Length; i += 2)
            {
                Vec3 e0 = Corner(EdgeCorners[i], half);
                Vec3 e1 = Corner(EdgeCorners[i + 1], half);
                float d2 = Intersections.SegmentSegmentClosest(a, b, e0, e1, out _, out _, out Vec3 c1, out Vec3 c2);
                if (d2 < distSq)
                {
                    distSq = d2;
                    onSegment = c1;
                    onBox = c2;
                }
            }
        }

        private static void FaceAxisTranslation(Vec3 a, Vec3 b, float r, Vec3 half, out Vec3 normal, out float depth)
        {
            depth = float.PositiveInfinity;
            normal = Vec3.Up;
            ConsiderAxis(MathF.Min(a.X, b.X), MathF.Max(a.X, b.X), half.X, r, 0, ref normal, ref depth);
            ConsiderAxis(MathF.Min(a.Y, b.Y), MathF.Max(a.Y, b.Y), half.Y, r, 1, ref normal, ref depth);
            ConsiderAxis(MathF.Min(a.Z, b.Z), MathF.Max(a.Z, b.Z), half.Z, r, 2, ref normal, ref depth);
        }

        private static void ConsiderAxis(float segMin, float segMax, float h, float r, int axis, ref Vec3 normal, ref float depth)
        {
            float pushPositive = h - segMin + r;
            float pushNegative = segMax + h + r;
            // Ties prefer +Y (standing on top) and positive directions for determinism.
            if (pushPositive <= pushNegative)
            {
                if (pushPositive < depth || (pushPositive == depth && axis == 1))
                {
                    depth = pushPositive;
                    normal = AxisVector(axis, 1f);
                }
            }
            else if (pushNegative < depth)
            {
                depth = pushNegative;
                normal = AxisVector(axis, -1f);
            }
        }

        /// <summary>
        /// Moving sphere (center from o along unit direction d for up to maxT meters) against the box.
        /// The rounded box equals the union of three axis-expanded boxes and twelve edge capsules, so the
        /// earliest entry among those shapes is the exact first contact. Returns false if starting in contact.
        /// localNormal comes from the primitive that was hit (a face axis or the edge-to-center direction), so it
        /// stays exact for a zero radius, where the contact center lies on the box surface.
        /// </summary>
        public static bool SweepSphere(Vec3 o, Vec3 d, float maxT, float r, Vec3 half, out float t, out Vec3 localNormal)
        {
            t = float.PositiveInfinity;
            localNormal = Vec3.Zero;
            bool hit = false;

            if (Raycast(o, d, new Vec3(half.X + r, half.Y, half.Z), maxT, out float tx, out Vec3 nx, out bool inX) && !inX && tx < t)
            {
                t = tx;
                localNormal = nx;
                hit = true;
            }

            if (Raycast(o, d, new Vec3(half.X, half.Y + r, half.Z), maxT, out float ty, out Vec3 ny, out bool inY) && !inY && ty < t)
            {
                t = ty;
                localNormal = ny;
                hit = true;
            }

            if (Raycast(o, d, new Vec3(half.X, half.Y, half.Z + r), maxT, out float tz, out Vec3 nz, out bool inZ) && !inZ && tz < t)
            {
                t = tz;
                localNormal = nz;
                hit = true;
            }

            if (r > 0f)
            {
                int edge = -1;
                for (int i = 0; i < EdgeCorners.Length; i += 2)
                {
                    Vec3 e0 = Corner(EdgeCorners[i], half);
                    Vec3 e1 = Corner(EdgeCorners[i + 1], half);
                    if (Intersections.RayCapsule(o, d, e0, e1, r, maxT, out float tc) && tc < t)
                    {
                        t = tc;
                        edge = i;
                        hit = true;
                    }
                }

                if (edge >= 0)
                {
                    Vec3 center = o + d * t;
                    Vec3 onEdge = Intersections.ClosestPointOnSegment(center, Corner(EdgeCorners[edge], half), Corner(EdgeCorners[edge + 1], half));
                    localNormal = (center - onEdge).NormalizedOr(-d);
                }
            }

            return hit && t <= maxT;
        }
    }
}
