using System;
using Confluence.Core.Geometry;
using Confluence.Core.Random;

namespace Confluence.Core.Tests.Geometry
{
    public class IntersectionTests
    {
        private const float Tol = 1e-4f;

        private static void AssertVec(Vec3 expected, Vec3 actual, float tol = Tol)
        {
            Assert.True(Vec3.Distance(expected, actual) <= tol, $"expected {expected} but was {actual}");
        }

        private static Vec3 RandomPoint(Rng rng, float extent)
        {
            return new Vec3(rng.Range(-extent, extent), rng.Range(-extent, extent), rng.Range(-extent, extent));
        }

        private static YawBox RandomBox(Rng rng)
        {
            var half = new Vec3(rng.Range(0.2f, 2f), rng.Range(0.2f, 2f), rng.Range(0.2f, 2f));
            return new YawBox(RandomPoint(rng, 1f), half, rng.Range(-180f, 180f));
        }

        // Minimum distance from a segment to a box by dense sampling (reference for exact queries).
        private static float SampledSegmentBoxDistance(Vec3 a, Vec3 b, YawBox box, int samples = 4000)
        {
            float best = float.PositiveInfinity;
            for (int i = 0; i <= samples; i++)
            {
                Vec3 p = Vec3.LerpUnclamped(a, b, i / (float)samples);
                best = MathF.Min(best, MathF.Sqrt(box.DistanceSquared(p)));
            }

            return best;
        }

        [Fact]
        public void ClosestPointOnSegment_Clamps_And_Handles_Degenerate()
        {
            var a = new Vec3(0f, 0f, 0f);
            var b = new Vec3(10f, 0f, 0f);
            Assert.Equal(new Vec3(4f, 0f, 0f), Intersections.ClosestPointOnSegment(new Vec3(4f, 3f, 0f), a, b, out float t));
            Assert.Equal(0.4f, t, 5);
            Assert.Equal(a, Intersections.ClosestPointOnSegment(new Vec3(-4f, 3f, 0f), a, b));
            Assert.Equal(b, Intersections.ClosestPointOnSegment(new Vec3(14f, 3f, 0f), a, b));
            Assert.Equal(a, Intersections.ClosestPointOnSegment(new Vec3(5f, 5f, 5f), a, a));
            Assert.Equal(new Vec2(1f, 0f), Intersections.ClosestPointOnSegment(new Vec2(1f, 5f), new Vec2(-2f, 0f), new Vec2(2f, 0f)));
        }

        [Fact]
        public void SegmentSegmentClosest_Crossing_Parallel_And_Degenerate()
        {
            float d2 = Intersections.SegmentSegmentClosest(
                new Vec3(-1f, 0f, 0f), new Vec3(1f, 0f, 0f),
                new Vec3(0f, 2f, -1f), new Vec3(0f, 2f, 1f),
                out float s, out float t, out Vec3 c1, out Vec3 c2);
            Assert.Equal(4f, d2, 5);
            Assert.Equal(0.5f, s, 5);
            Assert.Equal(0.5f, t, 5);
            Assert.Equal(new Vec3(0f, 0f, 0f), c1);
            Assert.Equal(new Vec3(0f, 2f, 0f), c2);

            // Intersecting segments.
            d2 = Intersections.SegmentSegmentClosest(
                new Vec3(-1f, 0f, 0f), new Vec3(1f, 0f, 0f),
                new Vec3(0f, -1f, 0f), new Vec3(0f, 1f, 0f),
                out _, out _, out _, out _);
            Assert.Equal(0f, d2, 6);

            // Parallel, overlapping in projection: distance is the gap.
            d2 = Intersections.SegmentSegmentClosest(
                new Vec3(0f, 0f, 0f), new Vec3(4f, 0f, 0f),
                new Vec3(1f, 3f, 0f), new Vec3(6f, 3f, 0f),
                out _, out _, out c1, out c2);
            Assert.Equal(9f, d2, 4);
            Assert.Equal(3f, Vec3.Distance(c1, c2), 4);

            // Parallel and disjoint end to end.
            d2 = Intersections.SegmentSegmentClosest(
                new Vec3(0f, 0f, 0f), new Vec3(1f, 0f, 0f),
                new Vec3(3f, 0f, 0f), new Vec3(5f, 0f, 0f),
                out _, out _, out _, out _);
            Assert.Equal(4f, d2, 4);

            // Both degenerate.
            d2 = Intersections.SegmentSegmentClosest(Vec3.Zero, Vec3.Zero, Vec3.Up, Vec3.Up, out s, out t, out _, out _);
            Assert.Equal(1f, d2);
            Assert.Equal(0f, s);
            Assert.Equal(0f, t);

            // One degenerate.
            d2 = Intersections.SegmentSegmentClosest(new Vec3(2f, 1f, 0f), new Vec3(2f, 1f, 0f), Vec3.Zero, new Vec3(4f, 0f, 0f), out _, out t, out _, out _);
            Assert.Equal(1f, d2, 5);
            Assert.Equal(0.5f, t, 5);
        }

        [Fact]
        public void SegmentSegmentClosest_Matches_Sampling()
        {
            var rng = new Rng(7);
            for (int trial = 0; trial < 200; trial++)
            {
                Vec3 p1 = RandomPoint(rng, 3f), q1 = RandomPoint(rng, 3f), p2 = RandomPoint(rng, 3f), q2 = RandomPoint(rng, 3f);
                float d = MathF.Sqrt(Intersections.SegmentSegmentClosest(p1, q1, p2, q2, out _, out _, out _, out _));
                float sampled = float.PositiveInfinity;
                for (int i = 0; i <= 200; i++)
                {
                    Vec3 a = Vec3.LerpUnclamped(p1, q1, i / 200f);
                    float da = MathF.Sqrt(Intersections.SegmentSegmentClosest(a, a, p2, q2, out _, out _, out _, out _));
                    sampled = MathF.Min(sampled, da);
                }

                Assert.True(d <= sampled + 1e-4f, $"trial {trial}: exact {d} above sampled {sampled}");
                Assert.True(d >= sampled - 0.05f, $"trial {trial}: exact {d} far below sampled {sampled}");
            }
        }

        [Fact]
        public void Circle_Tests()
        {
            Assert.True(Intersections.PointInCircle(new Vec2(3f, 4f), Vec2.Zero, 5f));
            Assert.False(Intersections.PointInCircle(new Vec2(3f, 4.01f), Vec2.Zero, 5f));
            Assert.True(Intersections.CircleIntersectsCircle(Vec2.Zero, 1f, new Vec2(3f, 0f), 2f));
            Assert.False(Intersections.CircleIntersectsCircle(Vec2.Zero, 1f, new Vec2(3.1f, 0f), 2f));

            // Diamond footprint: half 1 rotated 45 degrees; its corner points along +X at sqrt 2.
            var diamond = new YawBox(Vec3.Zero, new Vec3(1f, 1f, 1f), 45f);
            Assert.True(Intersections.CircleIntersectsYawBoxXZ(new Vec2(1.9f, 0f), 0.5f, diamond));
            Assert.False(Intersections.CircleIntersectsYawBoxXZ(new Vec2(2f, 0f), 0.5f, diamond));
            // Along the face normal (1,1)/sqrt2 the face is at distance 1.
            Assert.True(Intersections.CircleIntersectsYawBoxXZ(new Vec2(1.0f, 1.0f), 0.42f, diamond));
            Assert.False(Intersections.CircleIntersectsYawBoxXZ(new Vec2(1.0f, 1.0f), 0.40f, diamond));
            Assert.True(Intersections.CircleIntersectsYawBoxXZ(Vec2.Zero, 0.01f, diamond));
        }

        [Fact]
        public void RaySphere_Hit_Inside_Miss_Behind()
        {
            var ray = new Ray(new Vec3(-10f, 0f, 0f), Vec3.Right);
            Assert.True(Intersections.RaySphere(ray, Vec3.Zero, 2f, 100f, out RayHit hit));
            Assert.Equal(8f, hit.Distance, 4);
            AssertVec(Vec3.Left, hit.Normal);
            Assert.False(Intersections.RaySphere(ray, Vec3.Zero, 2f, 7.9f, out _));
            Assert.False(Intersections.RaySphere(ray, new Vec3(0f, 2.1f, 0f), 2f, 100f, out _));
            Assert.False(Intersections.RaySphere(new Ray(new Vec3(10f, 0f, 0f), Vec3.Right), Vec3.Zero, 2f, 100f, out _));
            Assert.True(Intersections.RaySphere(new Ray(new Vec3(0.5f, 0f, 0f), Vec3.Right), Vec3.Zero, 2f, 100f, out hit));
            Assert.Equal(0f, hit.Distance);
            Assert.False(Intersections.RaySphere(new Ray(new Vec3(-10f, 0f, 0f), Vec3.Zero), Vec3.Zero, 2f, 100f, out _));
        }

        [Fact]
        public void RaySphere_Is_Precise_For_Distant_Small_Spheres()
        {
            var ray = new Ray(new Vec3(0f, 1.5f, -45f), Vec3.Forward);
            Assert.True(Intersections.RaySphere(ray, new Vec3(0.1f, 1.5f, 40f), 0.2f, 200f, out RayHit hit));
            float expected = 85f - MathF.Sqrt(0.2f * 0.2f - 0.1f * 0.1f);
            Assert.Equal(expected, hit.Distance, 3);
        }

        [Fact]
        public void RayCapsule_Body_Caps_And_Inside()
        {
            var capsule = new Capsule(new Vec3(0f, 0f, 0f), new Vec3(0f, 4f, 0f), 0.5f);
            Assert.True(Intersections.RayCapsule(new Ray(new Vec3(-5f, 2f, 0f), Vec3.Right), capsule, 100f, out RayHit hit));
            Assert.Equal(4.5f, hit.Distance, 4);
            AssertVec(Vec3.Left, hit.Normal);

            Assert.True(Intersections.RayCapsule(new Ray(new Vec3(0f, 10f, 0f), Vec3.Down), capsule, 100f, out hit));
            Assert.Equal(5.5f, hit.Distance, 4);
            AssertVec(Vec3.Up, hit.Normal);

            Assert.True(Intersections.RayCapsule(new Ray(new Vec3(-5f, -0.3f, 0f), Vec3.Right), capsule, 100f, out hit));
            Assert.Equal(5f - MathF.Sqrt(0.25f - 0.09f), hit.Distance, 4);

            Assert.False(Intersections.RayCapsule(new Ray(new Vec3(-5f, 4.6f, 0f), Vec3.Right), capsule, 100f, out _));
            Assert.True(Intersections.RayCapsule(new Ray(new Vec3(0.2f, 1f, 0f), Vec3.Right), capsule, 100f, out hit));
            Assert.Equal(0f, hit.Distance);
        }

        [Fact]
        public void SphereSweepVsSphere_First_Contact()
        {
            Assert.True(Intersections.SphereSweepVsSphere(new Vec3(-10f, 0f, 0f), new Vec3(10f, 0f, 0f), 0.5f, Vec3.Zero, 1.5f, out float t01, out Vec3 n));
            Assert.Equal(8f / 20f, t01, 4);
            AssertVec(Vec3.Left, n);
            Assert.False(Intersections.SphereSweepVsSphere(new Vec3(-10f, 2.1f, 0f), new Vec3(10f, 2.1f, 0f), 0.5f, Vec3.Zero, 1.5f, out _, out _));
            Assert.False(Intersections.SphereSweepVsSphere(new Vec3(-10f, 0f, 0f), new Vec3(-5f, 0f, 0f), 0.5f, Vec3.Zero, 1.5f, out _, out _));
            Assert.True(Intersections.SphereSweepVsSphere(new Vec3(1f, 0f, 0f), new Vec3(5f, 0f, 0f), 0.5f, Vec3.Zero, 1.5f, out t01, out _));
            Assert.Equal(0f, t01);
        }

        [Fact]
        public void SegmentVsYawBox_First_Hit_And_Degenerate()
        {
            var box = new YawBox(new Vec3(0f, 1f, 0f), new Vec3(1f, 1f, 1f), 30f);
            Assert.True(Intersections.SegmentVsYawBox(new Vec3(0f, 1f, -10f), new Vec3(0f, 1f, 10f), box, out RayHit hit));
            Assert.True(hit.Distance > 8f && hit.Distance < 9f);
            Assert.True(box.ClosestPoint(hit.Point) == hit.Point || Vec3.Distance(box.ClosestPoint(hit.Point), hit.Point) < 1e-4f);
            Assert.False(Intersections.SegmentVsYawBox(new Vec3(0f, 1f, -10f), new Vec3(0f, 1f, -5f), box, out _));
            Assert.True(Intersections.SegmentVsYawBox(new Vec3(0f, 1f, 0f), new Vec3(0f, 1f, 0f), box, out hit));
            Assert.Equal(0f, hit.Distance);
            Assert.False(Intersections.SegmentVsYawBox(new Vec3(5f, 1f, 0f), new Vec3(5f, 1f, 0f), box, out _));
        }

        [Fact]
        public void SegmentVsRamp_Hits_Slope()
        {
            RampShape ramp = RampShape.Wedge(Vec3.Zero, 1f, 2f, 0f, 2f);
            Assert.True(Intersections.SegmentVsRamp(new Vec3(0f, 5f, 0f), new Vec3(0f, -5f, 0f), ramp, out RayHit hit));
            Assert.Equal(4f, hit.Distance, 4);
            Assert.False(Intersections.SegmentVsRamp(new Vec3(0f, 5f, 0f), new Vec3(0f, 1.1f, 0f), ramp, out _));
        }

        [Fact]
        public void SphereSweepVsYawBox_Face_Edge_And_Corner()
        {
            var box = new YawBox(Vec3.Zero, new Vec3(1f, 1f, 1f), 0f);

            // Face: center reaches x = -1.5.
            Assert.True(Intersections.SphereSweepVsYawBox(new Vec3(-10f, 0f, 0f), new Vec3(10f, 0f, 0f), 0.5f, box, out float t01, out RayHit hit));
            Assert.Equal(8.5f / 20f, t01, 4);
            AssertVec(new Vec3(-1.5f, 0f, 0f), hit.Point);
            AssertVec(Vec3.Left, hit.Normal);

            // Edge region: passing diagonally above the top-left edge touches its rounded edge.
            Assert.True(Intersections.SphereSweepVsYawBox(new Vec3(-10f, 1.3f, 0f), new Vec3(10f, 1.3f, 0f), 0.5f, box, out t01, out hit));
            float expectedX = -1f - MathF.Sqrt(0.25f - 0.09f);
            Assert.Equal(expectedX, hit.Point.X, 3);
            Assert.True(hit.Normal.X < 0f && hit.Normal.Y > 0f);

            // Corner region: approaching along the diagonal touches the rounded corner.
            Assert.True(Intersections.SphereSweepVsYawBox(new Vec3(5f, 5f, 5f), Vec3.Zero, 0.5f, box, out t01, out hit));
            float cornerOffset = 0.5f / MathF.Sqrt(3f);
            AssertVec(new Vec3(1f + cornerOffset, 1f + cornerOffset, 1f + cornerOffset), hit.Point, 1e-3f);
            AssertVec(new Vec3(1f, 1f, 1f).Normalized(), hit.Normal, 1e-3f);

            // Passing the +X face 1 cm beyond the radius misses.
            Assert.False(Intersections.SphereSweepVsYawBox(new Vec3(1.51f, 0f, -10f), new Vec3(1.51f, 0f, 10f), 0.5f, box, out _, out _));

            // Starting in contact.
            Assert.True(Intersections.SphereSweepVsYawBox(new Vec3(1.2f, 0f, 0f), new Vec3(5f, 0f, 0f), 0.5f, box, out t01, out hit));
            Assert.Equal(0f, t01);
            AssertVec(Vec3.Right, hit.Normal);
        }

        [Fact]
        public void SphereSweepVsYawBox_Matches_Sampling_On_Random_Cases()
        {
            var rng = new Rng(20261001);
            int hits = 0;
            for (int trial = 0; trial < 300; trial++)
            {
                YawBox box = RandomBox(rng);
                float radius = rng.Range(0.05f, 0.8f);
                Vec3 start = RandomPoint(rng, 6f);
                Vec3 end = RandomPoint(rng, 6f);
                if (MathF.Sqrt(box.DistanceSquared(start)) <= radius)
                {
                    continue;
                }

                const int samples = 20000;
                float sampledT = -1f;
                float minDistance = float.PositiveInfinity;
                for (int i = 0; i <= samples; i++)
                {
                    float t = i / (float)samples;
                    float distance = MathF.Sqrt(box.DistanceSquared(Vec3.LerpUnclamped(start, end, t)));
                    minDistance = MathF.Min(minDistance, distance);
                    if (distance <= radius && sampledT < 0f)
                    {
                        sampledT = t;
                    }
                }

                bool hit = Intersections.SphereSweepVsYawBox(start, end, radius, box, out float t01, out RayHit info);
                float step = 1f / samples;
                float stepMeters = Vec3.Distance(start, end) * step;
                if (sampledT < 0f)
                {
                    // The sampler can step over a grazing contact; an exact hit then must be within a step of grazing.
                    Assert.True(!hit || minDistance <= radius + stepMeters, $"trial {trial}: unexpected hit, path stays {minDistance - radius} m clear");
                    continue;
                }

                hits++;
                Assert.True(hit, $"trial {trial}: missed a contact found by sampling at {sampledT}");
                Assert.True(t01 <= sampledT + 1e-5f, $"trial {trial}: exact {t01} after sampled {sampledT}");
                Assert.True(t01 >= sampledT - step - 2e-4f, $"trial {trial}: exact {t01} well before sampled {sampledT}");
                Vec3 contact = info.Point;
                Assert.True(MathF.Abs(radius - MathF.Sqrt(box.DistanceSquared(contact))) < 1e-3f, $"trial {trial}: contact not at radius");
                Assert.Equal(1f, info.Normal.Length(), 4);
            }

            Assert.True(hits > 30, $"only {hits} contacts exercised");
        }

        [Fact]
        public void SphereSweepVsYawBox_Point_Sweep_Reports_The_Face_Normal()
        {
            // A zero radius puts the contact center on the surface, where center - closest point is zero.
            var box = new YawBox(Vec3.Zero, Vec3.One, 0f);
            Vec3 start = new Vec3(-5f, 3f, 0.2f);
            Vec3 end = new Vec3(5f, -3f, 0.2f);
            Assert.True(Intersections.SphereSweepVsYawBox(start, end, 0f, box, out float t01, out RayHit hit));
            Assert.True(box.Raycast(Ray.FromTo(start, end), Vec3.Distance(start, end), out RayHit ray));
            AssertVec(Vec3.Left, hit.Normal);
            AssertVec(ray.Point, hit.Point, 1e-3f);
            Assert.Equal(ray.Distance / Vec3.Distance(start, end), t01, 4);

            // Rotated box and a tiny radius: still the face normal, not the reversed sweep direction.
            var rotated = new YawBox(new Vec3(1f, 0f, 2f), new Vec3(1f, 0.5f, 2f), 35f);
            Vec3 face = rotated.ToWorld(new Vec3(-1f, 0.1f, 0.3f));
            Vec3 from = face - rotated.Right * 3f + new Vec3(0f, 1f, 0f);
            Assert.True(Intersections.SphereSweepVsYawBox(from, from + (face - from) * 2f, 1e-7f, rotated, out _, out hit));
            AssertVec(-rotated.Right, hit.Normal, 1e-3f);
        }

        private static RampShape RandomRamp(Rng rng)
        {
            var half = new Vec3(rng.Range(0.3f, 2f), rng.Range(0.3f, 1.5f), rng.Range(0.5f, 3f));
            var box = new YawBox(RandomPoint(rng, 1f), half, rng.Range(-180f, 180f));
            // Half the cases are full wedges (Rise equals the box height), half have a raised low end.
            float rise = rng.NextBool() ? half.Y * 2f : rng.Range(0.2f, half.Y * 2f);
            return new RampShape(box, rise);
        }

        [Fact]
        public void SphereSweepVsRamp_Slope_Face_Edge_And_Start_In_Contact()
        {
            RampShape ramp = RampShape.Wedge(Vec3.Zero, 1f, 2f, 0f, 2f); // rises 2 m over 4 m toward +Z
            Vec3 slopeNormal = ramp.SurfaceNormal;

            // Dropping onto the middle of the slope: the center stops one radius above the surface along its normal.
            Assert.True(Intersections.SphereSweepVsRamp(new Vec3(0f, 5f, 0f), new Vec3(0f, -5f, 0f), 0.5f, ramp, out float t01, out RayHit hit));
            AssertVec(slopeNormal, hit.Normal, 1e-4f);
            Assert.Equal(0.5f, Vec3.Distance(hit.Point, ramp.ClosestPoint(hit.Point)), 3);

            // Hitting the vertical high end from +Z.
            Assert.True(Intersections.SphereSweepVsRamp(new Vec3(0f, 1f, 6f), new Vec3(0f, 1f, -6f), 0.25f, ramp, out t01, out hit));
            AssertVec(Vec3.Forward, hit.Normal);
            Assert.Equal(2.25f, hit.Point.Z, 4);

            // Grazing over the top edge from the high side lands on the rounded edge.
            Assert.True(Intersections.SphereSweepVsRamp(new Vec3(0f, 2.3f, 6f), new Vec3(0f, 2.3f, -6f), 0.5f, ramp, out t01, out hit));
            Assert.True(hit.Normal.Y > 0f && hit.Normal.Z > 0f, $"edge normal {hit.Normal}");
            Assert.Equal(0.5f, Vec3.Distance(hit.Point, ramp.ClosestPoint(hit.Point)), 3);

            // Passing beside the ramp misses; starting in contact reports t = 0.
            Assert.False(Intersections.SphereSweepVsRamp(new Vec3(1.6f, 0.5f, -6f), new Vec3(1.6f, 0.5f, 6f), 0.5f, ramp, out _, out _));
            Assert.True(Intersections.SphereSweepVsRamp(new Vec3(1.3f, 0.5f, 0f), new Vec3(6f, 0.5f, 0f), 0.5f, ramp, out t01, out hit));
            Assert.Equal(0f, t01);
            AssertVec(Vec3.Right, hit.Normal);

            // A zero radius matches the ramp raycast, normal included.
            Assert.True(Intersections.SphereSweepVsRamp(new Vec3(0f, 5f, 0f), new Vec3(0f, -5f, 0f), 0f, ramp, out t01, out hit));
            Assert.True(ramp.Raycast(new Ray(new Vec3(0f, 5f, 0f), Vec3.Down), 10f, out RayHit ray));
            Assert.Equal(ray.Distance, hit.Distance, 4);
            AssertVec(ray.Normal, hit.Normal);

            // A ramp without rise is its box.
            var flat = new RampShape(new YawBox(Vec3.Zero, Vec3.One, 0f), 0f);
            Assert.True(Intersections.SphereSweepVsRamp(new Vec3(-10f, 0f, 0f), new Vec3(10f, 0f, 0f), 0.5f, flat, out t01, out hit));
            Assert.Equal(8.5f / 20f, t01, 4);
        }

        [Fact]
        public void SphereSweepVsRamp_Matches_Sampling_On_Random_Cases()
        {
            var rng = new Rng(20261002);
            int hits = 0;
            for (int trial = 0; trial < 300; trial++)
            {
                RampShape ramp = RandomRamp(rng);
                float radius = rng.Range(0.05f, 0.8f);
                Vec3 start = RandomPoint(rng, 6f);
                Vec3 end = RandomPoint(rng, 6f);
                if (Vec3.Distance(start, ramp.ClosestPoint(start)) <= radius)
                {
                    continue;
                }

                const int samples = 20000;
                float sampledT = -1f;
                float minDistance = float.PositiveInfinity;
                for (int i = 0; i <= samples; i++)
                {
                    float t = i / (float)samples;
                    Vec3 p = Vec3.LerpUnclamped(start, end, t);
                    float distance = Vec3.Distance(p, ramp.ClosestPoint(p));
                    minDistance = MathF.Min(minDistance, distance);
                    if (distance <= radius && sampledT < 0f)
                    {
                        sampledT = t;
                    }
                }

                bool hit = Intersections.SphereSweepVsRamp(start, end, radius, ramp, out float t01, out RayHit info);
                float step = 1f / samples;
                float stepMeters = Vec3.Distance(start, end) * step;
                if (sampledT < 0f)
                {
                    Assert.True(!hit || minDistance <= radius + stepMeters, $"trial {trial}: unexpected hit, path stays {minDistance - radius} m clear");
                    continue;
                }

                hits++;
                Assert.True(hit, $"trial {trial}: missed a contact found by sampling at {sampledT}");
                Assert.True(t01 <= sampledT + 1e-5f, $"trial {trial}: exact {t01} after sampled {sampledT}");
                Assert.True(t01 >= sampledT - step - 2e-4f, $"trial {trial}: exact {t01} well before sampled {sampledT}");
                Vec3 contact = info.Point;
                Vec3 surface = ramp.ClosestPoint(contact);
                Assert.True(MathF.Abs(radius - Vec3.Distance(contact, surface)) < 1e-3f, $"trial {trial}: contact not at radius");
                AssertVec((contact - surface).Normalized(), info.Normal, 2e-3f);
            }

            Assert.True(hits > 30, $"only {hits} contacts exercised");
        }

        [Fact]
        public void CapsuleVsYawBox_Standing_On_Floor_And_Against_Wall()
        {
            var floor = new YawBox(new Vec3(0f, -0.5f, 0f), new Vec3(10f, 0.5f, 10f), 0f);
            Capsule standing = Capsule.Vertical(new Vec3(1f, -0.05f, 2f), 1.8f, 0.35f);
            Assert.True(Intersections.CapsuleVsYawBox(standing, floor, out Penetration pen));
            AssertVec(Vec3.Up, pen.Normal);
            Assert.Equal(0.05f, pen.Depth, 4);
            Assert.False(Intersections.CapsuleVsYawBox(Capsule.Vertical(new Vec3(1f, 0.01f, 2f), 1.8f, 0.35f), floor, out _));

            var wall = new YawBox(new Vec3(5f, 1.5f, 0f), new Vec3(0.25f, 1.5f, 4f), 0f);
            Capsule touching = Capsule.Vertical(new Vec3(4.5f, 0f, 0f), 1.8f, 0.35f);
            Assert.True(Intersections.CapsuleVsYawBox(touching, wall, out pen));
            AssertVec(Vec3.Left, pen.Normal);
            Assert.Equal(0.1f, pen.Depth, 4);

            // Rotated wall: push-out follows the wall's face normal.
            var angled = new YawBox(new Vec3(0f, 1.5f, 5f), new Vec3(4f, 1.5f, 0.25f), 30f);
            Vec3 facePoint = angled.ToWorld(new Vec3(0f, -0.5f, -0.25f));
            Capsule near = Capsule.Vertical(facePoint - angled.Forward * 0.3f - new Vec3(0f, 1f, 0f), 2f, 0.35f);
            Assert.True(Intersections.CapsuleVsYawBox(near, angled, out pen));
            AssertVec(-angled.Forward, pen.Normal, 1e-3f);
            Assert.Equal(0.05f, pen.Depth, 3);
        }

        [Fact]
        public void CapsuleVsYawBox_Deep_Penetration_Uses_Minimum_Axis()
        {
            var block = new YawBox(Vec3.Zero, new Vec3(2f, 1f, 2f), 0f);
            Capsule inside = new Capsule(new Vec3(1.7f, -0.2f, 0f), new Vec3(1.7f, 0.2f, 0f), 0.3f);
            Assert.True(Intersections.CapsuleVsYawBox(inside, block, out Penetration pen));
            AssertVec(Vec3.Right, pen.Normal);
            Assert.Equal(0.3f + 0.3f, pen.Depth, 4);
            Capsule moved = inside.Translated(pen.Correction);
            Assert.False(Intersections.CapsuleVsYawBox(moved.Translated(new Vec3(1e-3f, 0f, 0f)), block, out _));
        }

        [Fact]
        public void CapsuleVsYawBox_Matches_Sampling_And_Resolves()
        {
            var rng = new Rng(99);
            int contacts = 0;
            for (int trial = 0; trial < 800; trial++)
            {
                YawBox box = RandomBox(rng);
                Vec3 a = RandomPoint(rng, 3.5f);
                Vec3 b = a + RandomPoint(rng, 1.5f);
                float radius = rng.Range(0.1f, 0.6f);
                var capsule = new Capsule(a, b, radius);
                float sampled = SampledSegmentBoxDistance(a, b, box);
                bool overlap = Intersections.CapsuleVsYawBox(capsule, box, out Penetration pen);

                if (sampled > radius + 1e-3f)
                {
                    Assert.False(overlap, $"trial {trial}: reported overlap at distance {sampled}");
                    continue;
                }

                if (sampled < radius - 1e-3f)
                {
                    Assert.True(overlap, $"trial {trial}: missed overlap at distance {sampled}");
                }

                if (!overlap)
                {
                    continue;
                }

                contacts++;
                Assert.Equal(1f, pen.Normal.Length(), 4);
                Assert.True(pen.Depth > 0f);
                if (sampled > 1e-3f)
                {
                    Assert.True(MathF.Abs(pen.Depth - (radius - sampled)) < 2e-3f, $"trial {trial}: depth {pen.Depth} vs {radius - sampled}");
                }

                Capsule resolved = capsule.Translated(pen.Normal * (pen.Depth + 2e-3f));
                float after = SampledSegmentBoxDistance(resolved.A, resolved.B, box, 1000);
                Assert.True(after >= radius - 1e-3f, $"trial {trial}: still overlapping by {radius - after}");
            }

            Assert.True(contacts > 60, $"only {contacts} contacts exercised");
        }

        [Fact]
        public void CapsuleVsRamp_On_Slope_And_Beside()
        {
            RampShape ramp = RampShape.Wedge(Vec3.Zero, 1f, 2f, 0f, 2f);
            Vec3 normal = ramp.SurfaceNormal;
            // Feet sphere center just below the slope surface by 0.05 along the normal.
            Vec3 surfacePoint = new Vec3(0f, ramp.SurfaceHeightAt(0f, 0f), 0f);
            Vec3 lowCenter = surfacePoint + normal * (0.35f - 0.05f);
            var capsule = new Capsule(lowCenter, lowCenter + new Vec3(0f, 1.1f, 0f), 0.35f);
            Assert.True(Intersections.CapsuleVsRamp(capsule, ramp, out Penetration pen));
            AssertVec(normal, pen.Normal, 1e-3f);
            Assert.Equal(0.05f, pen.Depth, 3);

            // Hovering clear of the slope.
            var clear = capsule.Translated(normal * 0.1f);
            Assert.False(Intersections.CapsuleVsRamp(clear, ramp, out _));

            // Against the side face.
            var side = Capsule.Vertical(new Vec3(1.3f, 0f, 0f), 1.8f, 0.35f);
            Assert.True(Intersections.CapsuleVsRamp(side, ramp, out pen));
            AssertVec(Vec3.Right, pen.Normal, 1e-3f);
            Assert.Equal(0.05f, pen.Depth, 3);

            // Flat ramps defer to the box test.
            var flat = new RampShape(new YawBox(Vec3.Zero, Vec3.One, 0f), 0f);
            Assert.True(Intersections.CapsuleVsRamp(Capsule.Vertical(new Vec3(0f, 0.9f, 0f), 1.8f, 0.35f), flat, out pen));
            AssertVec(Vec3.Up, pen.Normal);
        }

        [Fact]
        public void CapsuleVsRamp_Deep_Penetration_Resolves()
        {
            RampShape ramp = RampShape.Wedge(Vec3.Zero, 2f, 3f, 20f, 2f);
            var rng = new Rng(5);
            for (int trial = 0; trial < 100; trial++)
            {
                Vec3 a = ramp.Box.ToWorld(new Vec3(rng.Range(-1.5f, 1.5f), rng.Range(-0.9f, -0.5f), rng.Range(-1f, 2.5f)));
                var capsule = new Capsule(a, a + new Vec3(0f, 0.3f, 0f), 0.3f);
                Assert.True(Intersections.CapsuleVsRamp(capsule, ramp, out Penetration pen), $"trial {trial}");
                Capsule resolved = capsule.Translated(pen.Normal * (pen.Depth + 2e-3f));
                Assert.False(Intersections.CapsuleVsRamp(resolved, ramp, out Penetration again) && again.Depth > 1e-3f, $"trial {trial}: still {again.Depth}");
            }
        }

        [Fact]
        public void CapsuleVsCapsule_Pushes_Second_Away()
        {
            Capsule a = Capsule.Vertical(Vec3.Zero, 1.8f, 0.35f);
            Capsule b = Capsule.Vertical(new Vec3(0.5f, 0f, 0f), 1.8f, 0.35f);
            Assert.True(Intersections.CapsuleVsCapsule(a, b, out Penetration pen));
            AssertVec(Vec3.Right, pen.Normal);
            Assert.Equal(0.2f, pen.Depth, 4);
            Assert.False(Intersections.CapsuleVsCapsule(a, Capsule.Vertical(new Vec3(0.71f, 0f, 0f), 1.8f, 0.35f), out _));
            Assert.True(Intersections.CapsuleVsCapsule(a, a, out pen));
            Assert.Equal(0.7f, pen.Depth, 4);
        }
    }
}
