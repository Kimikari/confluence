using System;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Random;

namespace Confluence.Core.Tests.Arena
{
    public class BoundaryContourTests
    {
        private static readonly Vec3 Center = new Vec3(2f, 0f, -3f);

        // Irregular contour for a 24 m arena: radii between 10 and 12 m.
        private static BoundaryContour Irregular(ulong seed = 11)
        {
            var rng = new Rng(seed);
            var contour = new BoundaryContour { Center = Center, InitialRadii = new float[BoundaryContour.DefaultSampleCount] };
            for (int i = 0; i < contour.InitialRadii.Length; i++)
            {
                contour.InitialRadii[i] = rng.Range(10f, 12f);
            }

            return contour;
        }

        private static Vec3 At(float yawDeg, float radius, float y = 0f)
        {
            return Center + CMath.ForwardFromYaw(yawDeg) * radius + new Vec3(0f, y, 0f);
        }

        [Fact]
        public void Defaults_Follow_The_Round_Timeline()
        {
            var contour = new BoundaryContour();
            Assert.Equal(4f, contour.FinalRadius);
            Assert.Equal(55f, contour.ContractStartSec);
            Assert.Equal(85f, contour.ContractEndSec);
            Assert.Equal(64, contour.SampleCount);
            Assert.True(contour.IsValid(out _));
        }

        [Fact]
        public void Circle_Contains_Its_Interior_Only()
        {
            BoundaryContour circle = BoundaryContour.Circle(Center, 12f);
            Assert.True(circle.Contains(Center, 0f));
            Assert.True(circle.Contains(At(37f, 11.9f), 10f));
            Assert.False(circle.Contains(At(37f, 12.1f), 10f));
            Assert.True(circle.Contains(At(200f, 11.9f, 50f), 10f)); // the boundary is a vertical column
            Assert.Equal(12f, circle.RadiusAt(123f, 0f));
        }

        [Fact]
        public void Samples_Run_Clockwise_From_Positive_Z()
        {
            var contour = BoundaryContour.Circle(Vec3.Zero, 10f);
            contour.InitialRadii[16] = 20f; // sample 16 of 64 sits at yaw 90, world +X
            Assert.Equal(90f, contour.SampleAngleDeg(16));
            Vec3 p = contour.PointOnContour(90f, 0f);
            Assert.Equal(20f, p.X, 4);
            Assert.Equal(0f, p.Z, 4);
            Assert.Equal(10f, contour.PointOnContour(0f, 0f).Z, 4);

            // Linear interpolation between samples in angle.
            Assert.Equal(15f, contour.InitialRadiusAt(90f - 360f / 128f), 4);
            Assert.Equal(15f, contour.InitialRadiusAt(90f + 360f / 128f), 4);
            Assert.Equal(15f, contour.InitialRadiusAt(90f + 360f / 128f - 360f), 4);
        }

        [Fact]
        public void Radius_Is_Unchanged_Before_Contraction()
        {
            BoundaryContour contour = Irregular();
            for (float angle = -180f; angle < 180f; angle += 3.7f)
            {
                float initial = contour.InitialRadiusAt(angle);
                Assert.Equal(initial, contour.RadiusAt(angle, 0f));
                Assert.Equal(initial, contour.RadiusAt(angle, 54.99f));
                Assert.Equal(initial, contour.RadiusAt(angle, 55f));
            }
        }

        [Fact]
        public void Contraction_Is_Monotonic()
        {
            BoundaryContour contour = Irregular();
            for (float angle = 0f; angle < 360f; angle += 2.3f)
            {
                float previous = float.PositiveInfinity;
                for (float t = 0f; t <= 100f; t += 0.25f)
                {
                    float r = contour.RadiusAt(angle, t);
                    Assert.True(r <= previous + 1e-5f, $"radius grew at angle {angle}, t {t}");
                    previous = r;
                }
            }

            float lastArea = float.PositiveInfinity;
            for (float t = 50f; t <= 90f; t += 1f)
            {
                float area = contour.AreaAt(t);
                Assert.True(area <= lastArea + 1e-3f);
                lastArea = area;
            }

            // A point that is inside stays inside only until the boundary passes it, never re-entering.
            Vec3 p = At(45f, 8f);
            bool wasOutside = false;
            for (float t = 0f; t <= 90f; t += 0.5f)
            {
                bool inside = contour.Contains(p, t);
                Assert.False(wasOutside && inside, "point re-entered a contracting boundary");
                wasOutside |= !inside;
            }

            Assert.True(wasOutside);
        }

        [Fact]
        public void Final_Circle_Is_Exactly_The_Final_Radius()
        {
            BoundaryContour contour = Irregular(23);
            foreach (float t in new[] { 85f, 85.01f, 90f, 1000f })
            {
                for (float angle = -180f; angle <= 180f; angle += 1.3f)
                {
                    Assert.Equal(4f, contour.RadiusAt(angle, t));
                    Assert.True(contour.Contains(At(angle, 3.99f), t));
                    Assert.False(contour.Contains(At(angle, 4.01f), t));
                }
            }

            Assert.Equal(MathF.PI * 16f, contour.AreaAt(90f), 3);
            Assert.Equal(4f, contour.MinRadiusAt(85f));
            Assert.Equal(4f, contour.MaxRadiusAt(85f));
        }

        [Fact]
        public void Contraction_Follows_Smoothstep()
        {
            BoundaryContour circle = BoundaryContour.Circle(Center, 12f);
            Assert.Equal(0f, circle.ContractionProgress(55f));
            Assert.Equal(0.5f, circle.ContractionProgress(70f), 5);
            Assert.Equal(1f, circle.ContractionProgress(85f));
            Assert.Equal(8f, circle.RadiusAt(0f, 70f), 4); // halfway between 12 and 4
            float early = 12f - circle.RadiusAt(0f, 58f);
            float middle = circle.RadiusAt(0f, 69f) - circle.RadiusAt(0f, 72f);
            Assert.True(early < middle, "smoothstep starts slowly"); // equal 3 s windows
        }

        [Fact]
        public void Signed_Distance_Sign_Agrees_With_Contains()
        {
            BoundaryContour contour = Irregular(5);
            var rng = new Rng(17);
            for (int i = 0; i < 2000; i++)
            {
                Vec3 p = At(rng.Range(-180f, 180f), rng.Range(0f, 14f), rng.Range(-2f, 5f));
                float t = rng.Range(0f, 95f);
                float sd = contour.SignedDistance(p, t);
                bool inside = contour.Contains(p, t);
                Assert.Equal(inside, sd >= 0f);
                Assert.True(MathF.Abs(sd) <= MathF.Abs(contour.RadialMargin(p, t)) + 0.01f, "distance exceeds the radial gap");
            }
        }

        [Fact]
        public void Signed_Distance_On_A_Circle_Is_Radial()
        {
            BoundaryContour circle = BoundaryContour.Circle(Center, 12f);
            Assert.Equal(2f, circle.SignedDistance(At(10f, 10f), 0f), 2);
            Assert.Equal(-3f, circle.SignedDistance(At(250f, 15f), 0f), 2);
            Assert.Equal(12f, circle.SignedDistance(Center, 0f), 1);
            Assert.Equal(1f, circle.SignedDistance(At(10f, 3f), 90f), 2);
        }

        [Fact]
        public void ClampInside_Moves_Outside_Points_In()
        {
            BoundaryContour contour = Irregular(9);
            var rng = new Rng(3);
            for (int i = 0; i < 500; i++)
            {
                Vec3 p = At(rng.Range(-180f, 180f), rng.Range(0f, 20f), 1.5f);
                float t = rng.Range(0f, 95f);
                float margin = rng.Range(0f, 1f);
                Vec3 q = contour.ClampInside(p, t, margin);
                Assert.Equal(1.5f, q.Y);
                Assert.True(contour.Contains(q, t));
                Assert.True(contour.SignedDistance(q, t) >= margin - 1e-3f, $"margin {margin} not kept: {contour.SignedDistance(q, t)}");
                if (contour.SignedDistance(p, t) >= margin)
                {
                    Assert.Equal(p, q);
                }
                else
                {
                    // Displacement is radial toward the center.
                    Vec2 before = (p - Center).XZ();
                    Vec2 after = (q - Center).XZ();
                    Assert.True(after.Length() <= before.Length() + 1e-4f);
                    if (after.Length() > 1e-3f)
                    {
                        Assert.True(CMath.AngleBetweenDeg(before, after) < 0.1f);
                    }
                }
            }
        }

        private static BoundaryContour FromRadii(Func<int, float> radius)
        {
            var contour = new BoundaryContour { Center = Center, InitialRadii = new float[BoundaryContour.DefaultSampleCount] };
            for (int i = 0; i < contour.InitialRadii.Length; i++)
            {
                contour.InitialRadii[i] = radius(i);
            }

            return contour;
        }

        public static TheoryData<string> SlantedContours() => new TheoryData<string> { "irregular", "wavy", "stepped" };

        private static BoundaryContour Slanted(string name)
        {
            switch (name)
            {
                case "irregular": return Irregular(9);
                case "wavy": return FromRadii(i => 20f + 3f * MathF.Sin(1.7f * i));
                default: return FromRadii(i => (i / 4) % 2 == 0 ? 12f : 14.5f); // 2.5 m square-wave steps
            }
        }

        [Theory]
        [MemberData(nameof(SlantedContours))]
        public void ClampInside_Stops_At_The_Farthest_Legal_Point_On_The_Ray(string name)
        {
            // Regression: a fixed-step walk used to give up on slanted contour segments and return the center,
            // 10-20 m from the destination, although a legal point existed within a meter or two.
            BoundaryContour contour = Slanted(name);
            const float margin = 0.3f;
            const float t = 0f;
            for (float yaw = -180f; yaw < 180f; yaw += 2.5f)
            {
                Vec3 p = At(yaw, contour.RadiusAt(yaw, t) + 0.2f, 1f);
                Vec3 q = contour.ClampInside(p, t, margin);
                Assert.True(contour.SignedDistance(q, t) >= margin - 1e-3f);
                float reached = Vec3.DistanceXZ(q, Center);

                // Brute force: the farthest radius on the ray that keeps the margin, scanning inward in 5 mm steps.
                Vec2 dir = (p - Center).XZ().Normalized();
                float best = 0f;
                for (float s = MathF.Min(Vec3.DistanceXZ(p, Center), contour.RadiusAt(yaw, t) - margin); s > 0f; s -= 0.005f)
                {
                    Vec3 probe = Center + new Vec3(dir.X * s, 1f, dir.Y * s);
                    if (contour.SignedDistance(probe, t) >= margin)
                    {
                        best = s;
                        break;
                    }
                }

                Assert.True(reached >= best - 0.006f, $"{name} yaw {yaw}: clamped to {reached} m, but {best} m keeps the margin");
                Assert.True(reached > 5f, $"{name} yaw {yaw}: snapped toward the center ({reached} m)");
            }
        }

        [Fact]
        public void ClampInside_Final_Circle_And_Oversized_Margin()
        {
            BoundaryContour contour = Irregular(2);
            Vec3 q = contour.ClampInside(At(30f, 9f), 90f, 0.5f);
            Assert.Equal(3.5f, Vec3.DistanceXZ(q, Center), 2);
            Vec3 centered = contour.ClampInside(At(30f, 9f), 90f, 6f);
            Assert.Equal(Center.X, centered.X, 4);
            Assert.Equal(Center.Z, centered.Z, 4);
            Vec3 inside = At(100f, 1f);
            Assert.Equal(inside, contour.ClampInside(inside, 90f, 0.5f));
        }

        [Fact]
        public void Area_Of_Circle_And_Polygon_Agree()
        {
            BoundaryContour circle = BoundaryContour.Circle(Center, 12f);
            Assert.Equal(MathF.PI * 144f, circle.AreaAt(0f), 2);
            BoundaryContour contour = Irregular(4);
            float area = contour.AreaAt(10f);
            float polygonArea = contour.ToPolygon(10f, 8).Area();
            Assert.True(MathF.Abs(area - polygonArea) / area < 0.002f, $"{area} vs {polygonArea}");
        }

        [Fact]
        public void Validation_Rejects_Broken_Contours()
        {
            Assert.True(Irregular().IsValid(out string reason));
            Assert.Null(reason);

            BoundaryContour inside = Irregular();
            inside.InitialRadii[7] = 3f;
            Assert.False(inside.IsValid(out reason));
            Assert.Contains("7", reason);

            Assert.False(new BoundaryContour { InitialRadii = new[] { 10f, 10f } }.IsValid(out _));
            Assert.False(new BoundaryContour { InitialRadii = null }.IsValid(out _));
            Assert.False(new BoundaryContour { ContractStartSec = 90f }.IsValid(out _));
            BoundaryContour nan = Irregular();
            nan.InitialRadii[0] = float.NaN;
            Assert.False(nan.IsValid(out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => BoundaryContour.Circle(Vec3.Zero, 5f, 0));
        }

        [Fact]
        public void Degenerate_Sample_Counts_Are_Safe()
        {
            var empty = new BoundaryContour { InitialRadii = new float[0] };
            Assert.Equal(4f, empty.RadiusAt(10f, 0f));
            Assert.True(empty.Contains(new Vec3(3f, 0f, 0f), 0f));
            var single = new BoundaryContour { InitialRadii = new[] { 9f } };
            Assert.Equal(9f, single.RadiusAt(200f, 0f));
        }

        [Fact]
        public void Clone_Is_Deep()
        {
            BoundaryContour contour = Irregular();
            BoundaryContour copy = contour.Clone();
            Assert.NotSame(contour.InitialRadii, copy.InitialRadii);
            Assert.Equal(contour.InitialRadii, copy.InitialRadii);
            copy.InitialRadii[0] = 99f;
            copy.FinalRadius = 2f;
            Assert.NotEqual(99f, contour.InitialRadii[0]);
            Assert.Equal(4f, contour.FinalRadius);
            Assert.Equal(contour.Center, copy.Center);
        }
    }
}
