using System;

namespace Confluence.Core.Geometry
{
    /// <summary>
    /// Walkable ramp: a YawBox footprint whose top surface rises linearly from <see cref="BaseHeight"/> at the
    /// local -Z edge to <see cref="TopHeight"/> (the box top) at the local +Z edge. The solid fills everything
    /// between the box bottom and that surface, so the cross-section along local Z is a quadrilateral (a wedge
    /// triangle when Rise equals the box height). A ramp with Rise 0 is the plain box.
    /// </summary>
    public readonly struct RampShape
    {
        /// <summary>Bounding box of the solid. Its top is the high end of the walkable surface.</summary>
        public readonly YawBox Box;

        /// <summary>Height gained across the ramp's length, clamped to [0, box height].</summary>
        public readonly float Rise;

        public RampShape(YawBox box, float rise)
        {
            Box = box;
            Rise = CMath.Clamp(rise, 0f, box.HalfExtents.Y * 2f);
        }

        /// <summary>
        /// Pure wedge standing on the floor: the surface starts at floorCenter.Y on the low (-Z) edge and climbs
        /// by rise toward +Z (the yaw direction).
        /// </summary>
        public static RampShape Wedge(Vec3 floorCenter, float halfWidth, float halfLength, float yawDeg, float rise)
        {
            float h = MathF.Max(0f, rise) * 0.5f;
            YawBox box = new YawBox(floorCenter + new Vec3(0f, h, 0f), new Vec3(halfWidth, h, halfLength), yawDeg);
            return new RampShape(box, rise);
        }

        public float Bottom => Box.Bottom;
        public float TopHeight => Box.Top;
        public float BaseHeight => Box.Top - Rise;
        public float Length => Box.HalfExtents.Z * 2f;
        public float Width => Box.HalfExtents.X * 2f;

        /// <summary>Surface slope in degrees (90 for a degenerate zero-length ramp with nonzero rise).</summary>
        public float SlopeDeg
        {
            get
            {
                if (Rise <= 0f)
                {
                    return 0f;
                }

                return MathF.Atan2(Rise, Length) * CMath.Rad2Deg;
            }
        }

        /// <summary>Unit world normal of the walkable surface.</summary>
        public Vec3 SurfaceNormal => Box.DirectionToWorld(LocalSlopeNormal());

        /// <summary>Center of the low and high surface edges (route endpoints for path validation).</summary>
        public Vec3 LowEdgeCenter => Box.ToWorld(new Vec3(0f, Box.HalfExtents.Y - Rise, -Box.HalfExtents.Z));

        public Vec3 HighEdgeCenter => Box.ToWorld(new Vec3(0f, Box.HalfExtents.Y, Box.HalfExtents.Z));

        /// <summary>Walkable surface height at a world (x, z), or NaN outside the footprint.</summary>
        public float SurfaceHeightAt(float x, float z)
        {
            Vec3 local = Box.ToLocal(new Vec3(x, Box.Center.Y, z));
            Vec3 h = Box.HalfExtents;
            if (MathF.Abs(local.X) > h.X || MathF.Abs(local.Z) > h.Z)
            {
                return float.NaN;
            }

            return Box.Center.Y + LocalSurfaceY(local.Z);
        }

        public bool Contains(Vec3 point) => ContainsLocal(Box.ToLocal(point));

        internal bool ContainsLocal(Vec3 local)
        {
            Vec3 h = Box.HalfExtents;
            return MathF.Abs(local.X) <= h.X && MathF.Abs(local.Z) <= h.Z
                && local.Y >= -h.Y && local.Y <= LocalSurfaceY(local.Z);
        }

        /// <summary>Closest point of the solid wedge; the point itself when inside.</summary>
        public Vec3 ClosestPoint(Vec3 point) => Box.ToWorld(ClosestLocal(Box.ToLocal(point)));

        public Aabb Bounds() => Box.Bounds();

        /// <summary>
        /// Raycast against the wedge (box slabs plus the slope half-space, clipped in local space). A ray
        /// starting inside reports distance 0 with the reversed ray direction as its normal.
        /// </summary>
        public bool Raycast(Ray ray, float maxDistance, out RayHit hit)
        {
            Vec3 o = Box.ToLocal(ray.Origin);
            Vec3 d = Box.DirectionToLocal(ray.Direction);
            if (!LocalRaycast(o, d, maxDistance, out float t, out Vec3 n, out bool inside))
            {
                hit = default;
                return false;
            }

            Vec3 normal = inside ? -ray.Direction : Box.DirectionToWorld(n);
            hit = new RayHit(t, ray.PointAt(t), normal);
            return true;
        }

        // Local surface y (relative to box center) at local z.
        internal float LocalSurfaceY(float localZ)
        {
            Vec3 h = Box.HalfExtents;
            if (h.Z <= 0f)
            {
                return h.Y;
            }

            float t = CMath.Clamp01((localZ + h.Z) / (2f * h.Z));
            return h.Y - Rise + Rise * t;
        }

        internal Vec3 LocalSlopeNormal()
        {
            float lengthZ = 2f * Box.HalfExtents.Z;
            if (Rise <= 0f)
            {
                return Vec3.Up;
            }

            if (lengthZ <= 0f)
            {
                return Vec3.Back;
            }

            return new Vec3(0f, lengthZ, -Rise).Normalized();
        }

        // Slope plane in local space: dot(n, p) <= offset is inside.
        private float LocalSlopeOffset(Vec3 n)
        {
            // The plane passes through the high edge (z = +hz, y = +hy).
            Vec3 h = Box.HalfExtents;
            return n.Y * h.Y + n.Z * h.Z;
        }

        internal bool LocalRaycast(Vec3 o, Vec3 d, float maxT, out float t, out Vec3 normal, out bool inside)
        {
            t = 0f;
            normal = Vec3.Zero;
            inside = false;
            if (d.LengthSquared() < CMath.NormalizeEpsilonSq)
            {
                return false;
            }

            Vec3 h = Box.HalfExtents;
            float tEnter = float.NegativeInfinity;
            float tExit = float.PositiveInfinity;
            Vec3 enterNormal = Vec3.Zero;

            if (!ClipPlane(o, d, Vec3.Right, h.X, ref tEnter, ref tExit, ref enterNormal)
                || !ClipPlane(o, d, Vec3.Left, h.X, ref tEnter, ref tExit, ref enterNormal)
                || !ClipPlane(o, d, Vec3.Down, h.Y, ref tEnter, ref tExit, ref enterNormal)
                || !ClipPlane(o, d, Vec3.Up, h.Y, ref tEnter, ref tExit, ref enterNormal)
                || !ClipPlane(o, d, Vec3.Forward, h.Z, ref tEnter, ref tExit, ref enterNormal)
                || !ClipPlane(o, d, Vec3.Back, h.Z, ref tEnter, ref tExit, ref enterNormal))
            {
                return false;
            }

            if (Rise > 0f)
            {
                Vec3 n = LocalSlopeNormal();
                if (!ClipPlane(o, d, n, LocalSlopeOffset(n), ref tEnter, ref tExit, ref enterNormal))
                {
                    return false;
                }
            }

            if (tExit < 0f || tEnter > maxT)
            {
                return false;
            }

            if (tEnter < 0f)
            {
                inside = true;
                normal = -d.Normalized();
                return true;
            }

            t = tEnter;
            normal = enterNormal;
            return true;
        }

        // Cyrus-Beck clip against the half-space dot(n, p) <= offset.
        private static bool ClipPlane(Vec3 o, Vec3 d, Vec3 n, float offset, ref float tEnter, ref float tExit, ref Vec3 enterNormal)
        {
            float denom = Vec3.Dot(n, d);
            float dist = offset - Vec3.Dot(n, o);
            if (MathF.Abs(denom) < 1e-12f)
            {
                return dist >= 0f;
            }

            float t = dist / denom;
            if (denom < 0f)
            {
                if (t > tEnter)
                {
                    tEnter = t;
                    enterNormal = n;
                }
            }
            else if (t < tExit)
            {
                tExit = t;
            }

            return tEnter <= tExit;
        }

        // Cross-section vertices in (z, y), counter-clockwise: bottom-low, bottom-high, top-high, top-low.
        internal void CrossSection(out Vec2 p0, out Vec2 p1, out Vec2 p2, out Vec2 p3)
        {
            Vec3 h = Box.HalfExtents;
            p0 = new Vec2(-h.Z, -h.Y);
            p1 = new Vec2(h.Z, -h.Y);
            p2 = new Vec2(h.Z, h.Y);
            p3 = new Vec2(-h.Z, h.Y - Rise);
        }

        internal Vec3 ClosestLocal(Vec3 local)
        {
            Vec3 h = Box.HalfExtents;
            float x = CMath.Clamp(local.X, -h.X, h.X);
            Vec2 zy = new Vec2(local.Z, local.Y);
            CrossSection(out Vec2 p0, out Vec2 p1, out Vec2 p2, out Vec2 p3);

            bool inside = Vec2.Cross(p1 - p0, zy - p0) >= 0f
                && Vec2.Cross(p2 - p1, zy - p1) >= 0f
                && Vec2.Cross(p3 - p2, zy - p2) >= 0f
                && Vec2.Cross(p0 - p3, zy - p3) >= 0f;
            if (inside)
            {
                return new Vec3(x, local.Y, local.Z);
            }

            Vec2 best = Intersections.ClosestPointOnSegment(zy, p0, p1);
            float bestD = Vec2.DistanceSquared(best, zy);
            ConsiderEdge(zy, p1, p2, ref best, ref bestD);
            ConsiderEdge(zy, p2, p3, ref best, ref bestD);
            ConsiderEdge(zy, p3, p0, ref best, ref bestD);
            return new Vec3(x, best.Y, best.X);
        }

        private static void ConsiderEdge(Vec2 p, Vec2 a, Vec2 b, ref Vec2 best, ref float bestD)
        {
            Vec2 q = Intersections.ClosestPointOnSegment(p, a, b);
            float d = Vec2.DistanceSquared(q, p);
            if (d < bestD)
            {
                bestD = d;
                best = q;
            }
        }

        /// <summary>Local vertex by index: bit 0 selects +X; bits 1-2 pick the cross-section vertex.</summary>
        internal Vec3 LocalVertex(int index)
        {
            CrossSection(out Vec2 p0, out Vec2 p1, out Vec2 p2, out Vec2 p3);
            Vec2 zy;
            switch ((index >> 1) & 3)
            {
                case 0: zy = p0; break;
                case 1: zy = p1; break;
                case 2: zy = p2; break;
                default: zy = p3; break;
            }

            float x = (index & 1) != 0 ? Box.HalfExtents.X : -Box.HalfExtents.X;
            return new Vec3(x, zy.Y, zy.X);
        }

        public override string ToString() => "Ramp(" + Box + ", rise=" + Rise.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ")";
    }
}
