using System;

namespace Confluence.Core.Geometry
{
    /// <summary>
    /// Allocation-free intersection and proximity queries. Fractions named t01 run from 0 at the start of a
    /// segment or sweep to 1 at its end; distances are in meters.
    /// </summary>
    public static class Intersections
    {
        private const float DegenerateLengthSq = 1e-12f;

        public static Vec3 ClosestPointOnSegment(Vec3 p, Vec3 a, Vec3 b) => ClosestPointOnSegment(p, a, b, out _);

        public static Vec3 ClosestPointOnSegment(Vec3 p, Vec3 a, Vec3 b, out float t01)
        {
            Vec3 ab = b - a;
            float lenSq = ab.LengthSquared();
            if (lenSq < DegenerateLengthSq)
            {
                t01 = 0f;
                return a;
            }

            t01 = CMath.Clamp01(Vec3.Dot(p - a, ab) / lenSq);
            return a + ab * t01;
        }

        public static Vec2 ClosestPointOnSegment(Vec2 p, Vec2 a, Vec2 b)
        {
            Vec2 ab = b - a;
            float lenSq = ab.LengthSquared();
            if (lenSq < DegenerateLengthSq)
            {
                return a;
            }

            float t = CMath.Clamp01(Vec2.Dot(p - a, ab) / lenSq);
            return a + ab * t;
        }

        /// <summary>
        /// Closest points between segments p1-q1 and p2-q2 (Ericson, Real-Time Collision Detection 5.1.9).
        /// Handles degenerate (point) and parallel segments. Returns the squared distance.
        /// </summary>
        public static float SegmentSegmentClosest(Vec3 p1, Vec3 q1, Vec3 p2, Vec3 q2, out float s, out float t, out Vec3 c1, out Vec3 c2)
        {
            Vec3 d1 = q1 - p1;
            Vec3 d2 = q2 - p2;
            Vec3 r = p1 - p2;
            float a = d1.LengthSquared();
            float e = d2.LengthSquared();
            float f = Vec3.Dot(d2, r);

            if (a < DegenerateLengthSq && e < DegenerateLengthSq)
            {
                s = 0f;
                t = 0f;
                c1 = p1;
                c2 = p2;
                return (c1 - c2).LengthSquared();
            }

            if (a < DegenerateLengthSq)
            {
                s = 0f;
                t = CMath.Clamp01(f / e);
            }
            else
            {
                float c = Vec3.Dot(d1, r);
                if (e < DegenerateLengthSq)
                {
                    t = 0f;
                    s = CMath.Clamp01(-c / a);
                }
                else
                {
                    float b = Vec3.Dot(d1, d2);
                    float denom = a * e - b * b;
                    s = denom > 1e-7f * a * e ? CMath.Clamp01((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f)
                    {
                        t = 0f;
                        s = CMath.Clamp01(-c / a);
                    }
                    else if (t > 1f)
                    {
                        t = 1f;
                        s = CMath.Clamp01((b - c) / a);
                    }
                }
            }

            c1 = p1 + d1 * s;
            c2 = p2 + d2 * t;
            return (c1 - c2).LengthSquared();
        }

        public static bool PointInCircle(Vec2 p, Vec2 center, float radius) => Vec2.DistanceSquared(p, center) <= radius * radius;

        public static bool CircleIntersectsCircle(Vec2 a, float radiusA, Vec2 b, float radiusB)
        {
            float r = radiusA + radiusB;
            return Vec2.DistanceSquared(a, b) <= r * r;
        }

        /// <summary>True if a ground-plane circle touches the box footprint.</summary>
        public static bool CircleIntersectsYawBoxXZ(Vec2 center, float radius, YawBox box)
        {
            Vec2 q = box.ClosestPointXZ(center);
            return Vec2.DistanceSquared(q, center) <= radius * radius;
        }

        /// <summary>Ray vs sphere. A ray starting inside reports distance 0 and the reversed ray direction.</summary>
        public static bool RaySphere(Ray ray, Vec3 center, float radius, float maxDistance, out RayHit hit)
        {
            if (!RaySphere(ray.Origin, ray.Direction, center, radius, maxDistance, out float t))
            {
                hit = default;
                return false;
            }

            Vec3 point = ray.PointAt(t);
            Vec3 normal = t == 0f ? -ray.Direction : (point - center).NormalizedOr(-ray.Direction);
            hit = new RayHit(t, point, normal);
            return true;
        }

        /// <summary>Ray vs sphere with a unit direction. Starting inside returns t = 0.</summary>
        public static bool RaySphere(Vec3 origin, Vec3 unitDirection, Vec3 center, float radius, float maxT, out float t)
        {
            t = 0f;
            Vec3 oc = origin - center;
            float c = oc.LengthSquared() - radius * radius;
            if (c <= 0f)
            {
                return radius >= 0f && maxT >= 0f;
            }

            if (unitDirection.LengthSquared() < DegenerateLengthSq)
            {
                return false;
            }

            float b = Vec3.Dot(oc, unitDirection);
            if (b > 0f)
            {
                return false; // outside and pointing away
            }

            // Discriminant from the perpendicular offset (more precise than b * b - c far from the sphere).
            Vec3 perp = oc - unitDirection * b;
            float disc = radius * radius - perp.LengthSquared();
            if (disc < 0f)
            {
                return false;
            }

            t = -b - MathF.Sqrt(disc);
            if (t < 0f)
            {
                t = 0f;
            }

            return t <= maxT;
        }

        /// <summary>Ray vs capsule (segment a-b, radius). Unit direction; starting inside returns t = 0.</summary>
        public static bool RayCapsule(Vec3 origin, Vec3 unitDirection, Vec3 a, Vec3 b, float radius, float maxT, out float t)
        {
            t = 0f;
            if (Vec3.DistanceSquared(origin, ClosestPointOnSegment(origin, a, b)) <= radius * radius)
            {
                return maxT >= 0f;
            }

            if (unitDirection.LengthSquared() < DegenerateLengthSq)
            {
                return false;
            }

            float best = float.PositiveInfinity;
            Vec3 ba = b - a;
            Vec3 oa = origin - a;
            float babaF = ba.LengthSquared();
            if (babaF >= DegenerateLengthSq)
            {
                // Cylinder body (Quilez). Double precision avoids cancellation for distant rays.
                double baba = babaF;
                double bard = Vec3.Dot(ba, unitDirection);
                double baoa = (double)ba.X * oa.X + (double)ba.Y * oa.Y + (double)ba.Z * oa.Z;
                double rdoa = (double)unitDirection.X * oa.X + (double)unitDirection.Y * oa.Y + (double)unitDirection.Z * oa.Z;
                double oaoa = (double)oa.X * oa.X + (double)oa.Y * oa.Y + (double)oa.Z * oa.Z;
                double qa = baba - bard * bard;
                double qb = baba * rdoa - baoa * bard;
                double qc = baba * oaoa - baoa * baoa - (double)radius * radius * baba;
                double h = qb * qb - qa * qc;
                if (qa > 1e-9 * baba && h >= 0.0)
                {
                    double tb = (-qb - Math.Sqrt(h)) / qa;
                    double y = baoa + tb * bard;
                    if (tb >= 0.0 && y > 0.0 && y < baba)
                    {
                        best = (float)tb;
                    }
                }
            }

            if (RaySphere(origin, unitDirection, a, radius, maxT, out float ta) && ta < best)
            {
                best = ta;
            }

            if (RaySphere(origin, unitDirection, b, radius, maxT, out float tbCap) && tbCap < best)
            {
                best = tbCap;
            }

            if (best > maxT)
            {
                return false;
            }

            t = best;
            return true;
        }

        public static bool RayCapsule(Ray ray, Capsule capsule, float maxDistance, out RayHit hit)
        {
            if (!RayCapsule(ray.Origin, ray.Direction, capsule.A, capsule.B, capsule.Radius, maxDistance, out float t))
            {
                hit = default;
                return false;
            }

            Vec3 point = ray.PointAt(t);
            Vec3 normal = t == 0f
                ? -ray.Direction
                : (point - ClosestPointOnSegment(point, capsule.A, capsule.B)).NormalizedOr(-ray.Direction);
            hit = new RayHit(t, point, normal);
            return true;
        }

        /// <summary>First intersection of segment a-b with a sphere; t01 = 0 when a starts inside.</summary>
        public static bool SegmentSphere(Vec3 a, Vec3 b, Vec3 center, float radius, out float t01)
        {
            Vec3 d = b - a;
            float len = d.Length();
            t01 = 0f;
            if (len < CMath.Epsilon)
            {
                return Vec3.DistanceSquared(a, center) <= radius * radius;
            }

            if (!RaySphere(a, d / len, center, radius, len, out float t))
            {
                return false;
            }

            t01 = t / len;
            return true;
        }

        /// <summary>
        /// Moving sphere (radius, center from start to end) vs a static sphere. Returns the fraction of the
        /// movement at first contact and the contact normal pointing from the static sphere to the mover.
        /// Already-overlapping spheres report t01 = 0.
        /// </summary>
        public static bool SphereSweepVsSphere(Vec3 start, Vec3 end, float radius, Vec3 otherCenter, float otherRadius, out float t01, out Vec3 normal)
        {
            float combined = radius + otherRadius;
            if (!SegmentSphere(start, end, otherCenter, combined, out t01))
            {
                normal = Vec3.Zero;
                return false;
            }

            Vec3 at = Vec3.LerpUnclamped(start, end, t01);
            normal = (at - otherCenter).NormalizedOr((start - end).NormalizedOr(Vec3.Up));
            return true;
        }

        /// <summary>First hit of segment a-b against a box. Distance is measured from a in meters.</summary>
        public static bool SegmentVsYawBox(Vec3 a, Vec3 b, YawBox box, out RayHit hit)
        {
            Vec3 d = b - a;
            float len = d.Length();
            if (len < CMath.Epsilon)
            {
                if (box.Contains(a))
                {
                    hit = new RayHit(0f, a, Vec3.Up);
                    return true;
                }

                hit = default;
                return false;
            }

            return box.Raycast(new Ray(a, d), len, out hit);
        }

        /// <summary>First hit of segment a-b against a ramp. Distance is measured from a in meters.</summary>
        public static bool SegmentVsRamp(Vec3 a, Vec3 b, RampShape ramp, out RayHit hit)
        {
            Vec3 d = b - a;
            float len = d.Length();
            if (len < CMath.Epsilon)
            {
                if (ramp.Contains(a))
                {
                    hit = new RayHit(0f, a, Vec3.Up);
                    return true;
                }

                hit = default;
                return false;
            }

            return ramp.Raycast(new Ray(a, d), len, out hit);
        }

        /// <summary>
        /// Moving sphere vs box: exact first contact of a sphere whose center travels from start to end.
        /// Returns t01 = 0 with the separation normal when the sphere already touches the box.
        /// hit.Point is the sphere center at contact; hit.Normal points out of the box.
        /// </summary>
        public static bool SphereSweepVsYawBox(Vec3 start, Vec3 end, float radius, YawBox box, out float t01, out RayHit hit)
        {
            t01 = 0f;
            if (box.SphereOverlap(start, radius, out Penetration pen) || box.DistanceSquared(start) <= radius * radius)
            {
                Vec3 n = pen.Depth > 0f ? pen.Normal : (start - box.ClosestPoint(start)).NormalizedOr(Vec3.Up);
                hit = new RayHit(0f, start, n);
                return true;
            }

            Vec3 delta = end - start;
            float len = delta.Length();
            if (len < CMath.Epsilon)
            {
                hit = default;
                return false;
            }

            Vec3 dir = delta / len;
            Vec3 o = box.ToLocal(start);
            Vec3 d = box.DirectionToLocal(dir);
            if (!BoxMath.SweepSphere(o, d, len, radius, box.HalfExtents, out float t))
            {
                hit = default;
                return false;
            }

            t01 = t / len;
            Vec3 center = start + dir * t;
            Vec3 normal = (center - box.ClosestPoint(center)).NormalizedOr(-dir);
            hit = new RayHit(t, center, normal);
            return true;
        }

        /// <summary>Sphere overlap with a box (normal points out of the box).</summary>
        public static bool SphereVsYawBox(Vec3 center, float radius, YawBox box, out Penetration penetration)
        {
            return box.SphereOverlap(center, radius, out penetration);
        }

        /// <summary>
        /// Capsule penetration against a box for character collision. Moving the capsule by
        /// penetration.Normal * penetration.Depth separates it.
        /// </summary>
        public static bool CapsuleVsYawBox(Capsule capsule, YawBox box, out Penetration penetration)
        {
            Vec3 a = box.ToLocal(capsule.A);
            Vec3 b = box.ToLocal(capsule.B);
            if (!BoxMath.CapsulePenetration(a, b, capsule.Radius, box.HalfExtents, out Vec3 n, out float depth))
            {
                penetration = default;
                return false;
            }

            penetration = new Penetration(box.DirectionToWorld(n), depth);
            return true;
        }

        /// <summary>
        /// Capsule penetration against a ramp wedge. Shallow contacts use exact closest points (endpoints and
        /// the wedge's 12 edges); when the axis enters the solid, the minimum translation over the face normals
        /// (sides, bottom, ends, slope) is used.
        /// </summary>
        public static bool CapsuleVsRamp(Capsule capsule, RampShape ramp, out Penetration penetration)
        {
            if (ramp.Rise <= 0f)
            {
                return CapsuleVsYawBox(capsule, ramp.Box, out penetration);
            }

            Vec3 a = ramp.Box.ToLocal(capsule.A);
            Vec3 b = ramp.Box.ToLocal(capsule.B);
            float r = capsule.Radius;
            Vec3 normal;
            float depth;

            bool axisInside = ramp.ContainsLocal(a) || ramp.ContainsLocal(b)
                || ramp.LocalRaycast(a, b - a, 1f, out _, out _, out _);
            if (!axisInside)
            {
                Vec3 qa = ramp.ClosestLocal(a);
                float best = (a - qa).LengthSquared();
                Vec3 onSeg = a;
                Vec3 onRamp = qa;

                Vec3 qb = ramp.ClosestLocal(b);
                float db = (b - qb).LengthSquared();
                if (db < best)
                {
                    best = db;
                    onSeg = b;
                    onRamp = qb;
                }

                for (int i = 0; i < RampEdgeVertices.Length; i += 2)
                {
                    Vec3 e0 = ramp.LocalVertex(RampEdgeVertices[i]);
                    Vec3 e1 = ramp.LocalVertex(RampEdgeVertices[i + 1]);
                    float d2 = SegmentSegmentClosest(a, b, e0, e1, out _, out _, out Vec3 c1, out Vec3 c2);
                    if (d2 < best)
                    {
                        best = d2;
                        onSeg = c1;
                        onRamp = c2;
                    }
                }

                if (best >= r * r)
                {
                    penetration = default;
                    return false;
                }

                float dist = MathF.Sqrt(best);
                if (dist > CMath.Epsilon)
                {
                    normal = (onSeg - onRamp) / dist;
                    penetration = new Penetration(ramp.Box.DirectionToWorld(normal), r - dist);
                    return true;
                }
            }

            // Deep contact: separating-axis minimum over the wedge face normals.
            depth = float.PositiveInfinity;
            normal = Vec3.Up;
            ConsiderRampAxis(ramp, a, b, r, ramp.LocalSlopeNormal(), ref normal, ref depth);
            ConsiderRampAxis(ramp, a, b, r, Vec3.Right, ref normal, ref depth);
            ConsiderRampAxis(ramp, a, b, r, Vec3.Up, ref normal, ref depth);
            ConsiderRampAxis(ramp, a, b, r, Vec3.Forward, ref normal, ref depth);
            penetration = new Penetration(ramp.Box.DirectionToWorld(normal), depth);
            return true;
        }

        // Vertex index pairs (see RampShape.LocalVertex) for the wedge's 12 edges.
        private static readonly int[] RampEdgeVertices =
        {
            0, 2, 2, 4, 4, 6, 6, 0, // cross-section at -X
            1, 3, 3, 5, 5, 7, 7, 1, // cross-section at +X
            0, 1, 2, 3, 4, 5, 6, 7, // lengthwise along X
        };

        private static void ConsiderRampAxis(RampShape ramp, Vec3 a, Vec3 b, float r, Vec3 axis, ref Vec3 normal, ref float depth)
        {
            float wedgeMin = float.PositiveInfinity;
            float wedgeMax = float.NegativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                float p = Vec3.Dot(axis, ramp.LocalVertex(i));
                wedgeMin = MathF.Min(wedgeMin, p);
                wedgeMax = MathF.Max(wedgeMax, p);
            }

            float pa = Vec3.Dot(axis, a);
            float pb = Vec3.Dot(axis, b);
            float capMin = MathF.Min(pa, pb) - r;
            float capMax = MathF.Max(pa, pb) + r;
            float pushPositive = wedgeMax - capMin;
            float pushNegative = capMax - wedgeMin;
            if (pushPositive <= pushNegative)
            {
                if (pushPositive < depth)
                {
                    depth = pushPositive;
                    normal = axis;
                }
            }
            else if (pushNegative < depth)
            {
                depth = pushNegative;
                normal = -axis;
            }
        }

        /// <summary>Capsule vs capsule; the normal pushes b away from a.</summary>
        public static bool CapsuleVsCapsule(Capsule a, Capsule b, out Penetration penetration)
        {
            float d2 = SegmentSegmentClosest(a.A, a.B, b.A, b.B, out _, out _, out Vec3 ca, out Vec3 cb);
            float r = a.Radius + b.Radius;
            if (d2 >= r * r)
            {
                penetration = default;
                return false;
            }

            float dist = MathF.Sqrt(d2);
            Vec3 n = dist > CMath.Epsilon ? (cb - ca) / dist : Vec3.Right;
            penetration = new Penetration(n, r - dist);
            return true;
        }
    }
}
