using System;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Core.Arena
{
    /// <summary>
    /// The arena's legal boundary: an irregular closed contour around Center that contracts to the central
    /// final circle (spec L48, L306, L674-L679). The contour is a star-shaped polar curve: InitialRadii holds N
    /// radii sampled clockwise from +Z (sample i at yaw i * 360 / N, the same yaw convention as everything else),
    /// interpolated linearly in angle. Between ContractStartSec and ContractEndSec every radius moves radially
    /// toward FinalRadius along a smoothstep (DECISIONS D-026), so the final contour is exactly the FinalRadius circle.
    /// Times are seconds since combat started. The boundary is a vertical column; Y is ignored.
    /// </summary>
    public sealed class BoundaryContour
    {
        public const int DefaultSampleCount = 64;

        public Vec3 Center { get; set; }

        /// <summary>N radii, clockwise from +Z. Validated contours keep every radius at least FinalRadius.</summary>
        public float[] InitialRadii { get; set; }

        public float FinalRadius { get; set; } = GlobalRules.Boundary.FinalFloorRadius;           // spec L48
        public float ContractStartSec { get; set; } = GlobalRules.Boundary.ContractionStartSec;   // spec L48
        public float ContractEndSec { get; set; } = GlobalRules.Boundary.ContractionEndSec;       // spec L48

        /// <summary>A trivial contour: the final circle at every time.</summary>
        public BoundaryContour()
        {
            InitialRadii = new float[DefaultSampleCount];
            for (int i = 0; i < InitialRadii.Length; i++)
            {
                InitialRadii[i] = FinalRadius;
            }
        }

        public static BoundaryContour Circle(Vec3 center, float radius, int samples = DefaultSampleCount)
        {
            if (samples < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(samples), samples, "At least one sample is required.");
            }

            var contour = new BoundaryContour { Center = center, InitialRadii = new float[samples] };
            for (int i = 0; i < samples; i++)
            {
                contour.InitialRadii[i] = radius;
            }

            return contour;
        }

        public int SampleCount => InitialRadii == null ? 0 : InitialRadii.Length;

        /// <summary>Yaw of sample i in degrees.</summary>
        public float SampleAngleDeg(int index) => SampleCount == 0 ? 0f : index * 360f / SampleCount;

        /// <summary>Contraction progress: 0 before ContractStartSec, 1 from ContractEndSec, smoothstep between (D-026).</summary>
        public float ContractionProgress(float timeSec) => CMath.SmoothStep(ContractStartSec, ContractEndSec, timeSec);

        /// <summary>Initial (uncontracted) radius at a yaw, interpolated linearly between samples.</summary>
        public float InitialRadiusAt(float angleDeg)
        {
            float[] radii = InitialRadii;
            int n = radii == null ? 0 : radii.Length;
            if (n == 0)
            {
                return FinalRadius;
            }

            if (n == 1)
            {
                return radii[0];
            }

            float position = CMath.WrapAngle360(angleDeg) / 360f * n;
            float floor = MathF.Floor(position);
            int i0 = (int)floor % n;
            int i1 = (i0 + 1) % n;
            float frac = position - floor;
            return frac <= 0f ? radii[i0] : radii[i0] + (radii[i1] - radii[i0]) * frac;
        }

        /// <summary>Boundary radius along a yaw at a time. Exactly FinalRadius once the contraction ends.</summary>
        public float RadiusAt(float angleDeg, float timeSec)
        {
            float s = ContractionProgress(timeSec);
            if (s >= 1f)
            {
                return FinalRadius;
            }

            float initial = InitialRadiusAt(angleDeg);
            return s <= 0f ? initial : initial + (FinalRadius - initial) * s;
        }

        /// <summary>World point on the contour along a yaw (at the center's height).</summary>
        public Vec3 PointOnContour(float angleDeg, float timeSec)
        {
            return Center + CMath.ForwardFromYaw(angleDeg) * RadiusAt(angleDeg, timeSec);
        }

        /// <summary>True if p lies inside or on the boundary.</summary>
        public bool Contains(Vec3 p, float timeSec) => RadialMargin(p, timeSec) >= 0f;

        /// <summary>
        /// Boundary radius along p's direction minus p's distance from the center (positive inside). Cheap and
        /// sign-consistent with Contains; it is at least the Euclidean distance to the contour.
        /// </summary>
        public float RadialMargin(Vec3 p, float timeSec)
        {
            float dx = p.X - Center.X;
            float dz = p.Z - Center.Z;
            float dist = MathF.Sqrt(dx * dx + dz * dz);
            float angle = CMath.YawOf(new Vec3(dx, 0f, dz));
            return RadiusAt(angle, timeSec) - dist;
        }

        /// <summary>
        /// Horizontal distance from p to the contour, positive inside and negative outside. The magnitude is
        /// measured to the contour traced with two vertices per sample interval (within millimeters of the curve);
        /// the sign always agrees with Contains.
        /// </summary>
        public float SignedDistance(Vec3 p, float timeSec)
        {
            bool inside = Contains(p, timeSec);
            float d = MathF.Sqrt(DistanceToContourSquared(p, timeSec));
            return inside ? d : -d;
        }

        /// <summary>
        /// Returns p if it is at least margin inside the boundary; otherwise the farthest point on the segment from
        /// the center to p that is (keeping Y), so forced displacement and teleports stop as close to their intended
        /// destination as the boundary allows (spec L51). Only a margin larger than the inradius along that ray
        /// yields the center.
        /// </summary>
        public Vec3 ClampInside(Vec3 p, float timeSec, float margin)
        {
            margin = MathF.Max(0f, margin);
            if (SignedDistance(p, timeSec) >= margin)
            {
                return p;
            }

            float dx = p.X - Center.X;
            float dz = p.Z - Center.Z;
            float dist = MathF.Sqrt(dx * dx + dz * dz);
            Vec3 center = new Vec3(Center.X, p.Y, Center.Z);
            if (dist < CMath.Epsilon)
            {
                return center;
            }

            float ux = dx / dist;
            float uz = dz / dist;
            float angle = CMath.YawOf(new Vec3(dx, 0f, dz));

            // The Euclidean distance never exceeds the radial margin, so nothing beyond RadiusAt - margin qualifies.
            float hi = MathF.Min(dist, RadiusAt(angle, timeSec) - margin);
            if (hi <= 0f)
            {
                return center;
            }

            Vec3 candidate = new Vec3(Center.X + ux * hi, p.Y, Center.Z + uz * hi);
            if (SignedDistance(candidate, timeSec) >= margin - ClampTolerance)
            {
                return candidate;
            }

            if (SignedDistance(center, timeSec) < margin - ClampTolerance)
            {
                return center; // the margin exceeds the inradius along this ray
            }

            // Bisect for the largest qualifying radius on [0, hi]: lo always qualifies, hi never does.
            float lo = 0f;
            for (int iteration = 0; iteration < ClampBisectionSteps; iteration++)
            {
                float mid = 0.5f * (lo + hi);
                Vec3 q = new Vec3(Center.X + ux * mid, p.Y, Center.Z + uz * mid);
                if (SignedDistance(q, timeSec) >= margin - ClampTolerance)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            return new Vec3(Center.X + ux * lo, p.Y, Center.Z + uz * lo);
        }

        private const float ClampTolerance = 1e-4f;
        private const int ClampBisectionSteps = 24; // about 1e-6 of the initial bracket (sub-millimeter at 48 m)

        /// <summary>Exact enclosed area at a time (the polar curve is piecewise linear in angle).</summary>
        public float AreaAt(float timeSec)
        {
            int n = SampleCount;
            if (n == 0)
            {
                return MathF.PI * FinalRadius * FinalRadius;
            }

            double sum = 0.0;
            double step = 2.0 * Math.PI / n;
            for (int i = 0; i < n; i++)
            {
                double a = RadiusAt(SampleAngleDeg(i), timeSec);
                double b = RadiusAt(SampleAngleDeg((i + 1) % n), timeSec);
                sum += 0.5 * step * (a * a + a * b + b * b) / 3.0;
            }

            return (float)sum;
        }

        public float MinRadiusAt(float timeSec)
        {
            int n = SampleCount;
            if (n == 0)
            {
                return FinalRadius;
            }

            float min = float.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                min = MathF.Min(min, RadiusAt(SampleAngleDeg(i), timeSec));
            }

            return min;
        }

        public float MaxRadiusAt(float timeSec)
        {
            int n = SampleCount;
            if (n == 0)
            {
                return FinalRadius;
            }

            float max = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                max = MathF.Max(max, RadiusAt(SampleAngleDeg(i), timeSec));
            }

            return max;
        }

        /// <summary>Ground-plane polygon tracing the contour (allocates; for previews and validation).</summary>
        public Polygon2 ToPolygon(float timeSec, int verticesPerSample = 2)
        {
            int count = Math.Max(3, Math.Max(1, SampleCount) * Math.Max(1, verticesPerSample));
            var points = new Vec2[count];
            for (int i = 0; i < count; i++)
            {
                points[i] = PointOnContour(i * 360f / count, timeSec).XZ();
            }

            return new Polygon2(points);
        }

        /// <summary>Structural checks: at least 3 finite samples, none inside the final circle, start before end.</summary>
        public bool IsValid(out string reason)
        {
            if (InitialRadii == null || InitialRadii.Length < 3)
            {
                reason = "Boundary contour needs at least 3 radius samples.";
                return false;
            }

            if (!(FinalRadius > 0f) || float.IsInfinity(FinalRadius))
            {
                reason = "Final radius must be positive and finite.";
                return false;
            }

            if (!(ContractStartSec < ContractEndSec))
            {
                reason = "Contraction must start before it ends.";
                return false;
            }

            for (int i = 0; i < InitialRadii.Length; i++)
            {
                float r = InitialRadii[i];
                if (float.IsNaN(r) || float.IsInfinity(r) || r < FinalRadius)
                {
                    reason = "Radius sample " + i + " is not finite or lies inside the final circle.";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        public BoundaryContour Clone()
        {
            return new BoundaryContour
            {
                Center = Center,
                InitialRadii = InitialRadii == null ? null : (float[])InitialRadii.Clone(),
                FinalRadius = FinalRadius,
                ContractStartSec = ContractStartSec,
                ContractEndSec = ContractEndSec,
            };
        }

        private float DistanceToContourSquared(Vec3 p, float timeSec)
        {
            int n = Math.Max(3, Math.Max(1, SampleCount) * 2);
            Vec2 q = p.XZ();
            Vec2 first = PointOnContour(0f, timeSec).XZ();
            Vec2 prev = first;
            float best = float.PositiveInfinity;
            for (int i = 1; i <= n; i++)
            {
                Vec2 next = i == n ? first : PointOnContour(i * 360f / n, timeSec).XZ();
                Vec2 c = Intersections.ClosestPointOnSegment(q, prev, next);
                best = MathF.Min(best, Vec2.DistanceSquared(q, c));
                prev = next;
            }

            return best;
        }
    }
}
