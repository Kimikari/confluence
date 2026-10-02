using System;
using Confluence.Core.Geometry;

namespace Confluence.Core.Tests.Geometry
{
    public class ShapeTests
    {
        private const float Tol = 1e-4f;

        private static void AssertVec(Vec3 expected, Vec3 actual, float tol = Tol)
        {
            Assert.True(Vec3.Distance(expected, actual) <= tol, $"expected {expected} but was {actual}");
        }

        // A 4 x 2 x 6 box (half 2, 1, 3) centered at (10, 1, -5), yawed 90 degrees: local +Z points to world +X.
        private static readonly YawBox Rotated = new YawBox(new Vec3(10f, 1f, -5f), new Vec3(2f, 1f, 3f), 90f);

        [Fact]
        public void YawBox_Default_Is_Unrotated()
        {
            YawBox box = default;
            Assert.Equal(Vec3.Right, box.Right);
            Assert.Equal(Vec3.Forward, box.Forward);
            var p = new Vec3(1f, 2f, 3f);
            Assert.Equal(p, box.ToLocal(p));
        }

        [Fact]
        public void YawBox_Local_World_Round_Trip_And_Axes()
        {
            Assert.Equal(Vec3.Right, Rotated.Forward);
            Assert.Equal(Vec3.Back, Rotated.Right);
            AssertVec(new Vec3(13f, 1f, -5f), Rotated.ToWorld(new Vec3(0f, 0f, 3f)));
            AssertVec(new Vec3(10f, 1f, -7f), Rotated.ToWorld(new Vec3(2f, 0f, 0f)));

            var box = new YawBox(new Vec3(-3f, 2f, 7f), new Vec3(1.5f, 0.5f, 4f), 33f);
            for (int i = 0; i < 20; i++)
            {
                var p = new Vec3(i * 0.7f - 5f, i * 0.3f, 9f - i);
                AssertVec(p, box.ToWorld(box.ToLocal(p)));
                AssertVec(p, box.DirectionToWorld(box.DirectionToLocal(p)));
            }
        }

        [Fact]
        public void YawBox_Contains_Respects_Rotation()
        {
            Assert.True(Rotated.Contains(new Vec3(12.9f, 1.5f, -5f)));   // along local +Z (world +X), half 3
            Assert.False(Rotated.Contains(new Vec3(10f, 1f, -2.5f)));   // along local X (world Z), half 2
            Assert.True(Rotated.Contains(new Vec3(10f, 1f, -3.1f)));
            Assert.False(Rotated.Contains(new Vec3(10f, 2.1f, -5f)));
            Assert.True(Rotated.ContainsXZ(new Vec2(12.5f, -6.5f)));
            Assert.False(Rotated.ContainsXZ(new Vec2(13.5f, -5f)));
        }

        [Fact]
        public void YawBox_ClosestPoint()
        {
            var inside = new Vec3(10.5f, 1.2f, -4.5f);
            Assert.Equal(inside, Rotated.ClosestPoint(inside));
            AssertVec(new Vec3(13f, 1f, -5f), Rotated.ClosestPoint(new Vec3(20f, 1f, -5f)));
            AssertVec(new Vec3(13f, 2f, -3f), Rotated.ClosestPoint(new Vec3(15f, 9f, 0f)));
            Assert.Equal(4f, Rotated.DistanceSquared(new Vec3(15f, 1f, -5f)), 4);
            Vec2 xz = Rotated.ClosestPointXZ(new Vec2(10f, 10f));
            Assert.Equal(10f, xz.X, 4);
            Assert.Equal(-3f, xz.Y, 4);
        }

        [Fact]
        public void YawBox_Raycast_Reports_Distance_And_World_Normal()
        {
            // From the west along +X, hitting the local -Z face (world -X face at x = 7).
            Assert.True(Rotated.Raycast(new Ray(new Vec3(0f, 1f, -5f), Vec3.Right), 100f, out RayHit hit));
            Assert.Equal(7f, hit.Distance, 4);
            AssertVec(new Vec3(7f, 1f, -5f), hit.Point);
            AssertVec(Vec3.Left, hit.Normal);

            // From above: top face at y = 2.
            Assert.True(Rotated.Raycast(new Ray(new Vec3(11f, 10f, -4f), Vec3.Down), 100f, out hit));
            Assert.Equal(8f, hit.Distance, 4);
            AssertVec(Vec3.Up, hit.Normal);

            // From the north along -Z: local +X face is world -Z, so the north face is local -X at z = -3.
            Assert.True(Rotated.Raycast(new Ray(new Vec3(10f, 1f, 5f), Vec3.Back), 100f, out hit));
            Assert.Equal(8f, hit.Distance, 4);
            AssertVec(Vec3.Forward, hit.Normal);
        }

        [Fact]
        public void YawBox_Raycast_Misses_Range_Parallel_And_Behind()
        {
            Assert.False(Rotated.Raycast(new Ray(new Vec3(0f, 1f, -5f), Vec3.Right), 6.9f, out _));
            Assert.False(Rotated.Raycast(new Ray(new Vec3(0f, 5f, -5f), Vec3.Right), 100f, out _));
            Assert.False(Rotated.Raycast(new Ray(new Vec3(20f, 1f, -5f), Vec3.Right), 100f, out _));
            Assert.False(Rotated.Raycast(new Ray(new Vec3(0f, 1f, -5f), Vec3.Zero), 100f, out _));
        }

        [Fact]
        public void YawBox_Raycast_From_Inside_Reports_Zero()
        {
            Assert.True(Rotated.Raycast(new Ray(new Vec3(10f, 1f, -5f), Vec3.Up), 100f, out RayHit hit));
            Assert.Equal(0f, hit.Distance);
            AssertVec(Vec3.Down, hit.Normal);
        }

        [Fact]
        public void YawBox_Raycast_On_Diagonal_Box()
        {
            var diamond = new YawBox(Vec3.Zero, new Vec3(1f, 1f, 1f), 45f);
            // Offset from the corner so the ray strikes the upper-left face (x - z = -sqrt 2).
            Assert.True(diamond.Raycast(new Ray(new Vec3(-5f, 0f, 0.1f), Vec3.Right), 10f, out RayHit hit));
            Assert.Equal(5f - MathF.Sqrt(2f) + 0.1f, hit.Distance, 4);
            Assert.True(hit.Normal.Z > 0.7f);
            Assert.Equal(0f, hit.Normal.Y, 5);
            Assert.True(hit.Normal.X < -0.7f);
        }

        [Fact]
        public void YawBox_SphereOverlap_Outside_And_Inside()
        {
            var box = new YawBox(Vec3.Zero, new Vec3(1f, 1f, 1f), 0f);
            Assert.True(box.SphereOverlap(new Vec3(1.5f, 0f, 0f), 1f, out Penetration pen));
            AssertVec(Vec3.Right, pen.Normal);
            Assert.Equal(0.5f, pen.Depth, 5);

            Assert.False(box.SphereOverlap(new Vec3(2.5f, 0f, 0f), 1f, out _));
            Assert.False(box.SphereOverlap(new Vec3(2f, 0f, 0f), 1f, out _)); // touching is not overlap

            Assert.True(box.SphereOverlap(new Vec3(0f, 0.8f, 0.1f), 0.5f, out pen));
            AssertVec(Vec3.Up, pen.Normal);
            Assert.Equal(0.2f + 0.5f, pen.Depth, 5);

            // Rotated box: pushing out through the world -X face (local -Z).
            Assert.True(Rotated.SphereOverlap(new Vec3(6.8f, 1f, -5f), 0.5f, out pen));
            AssertVec(Vec3.Left, pen.Normal);
            Assert.Equal(0.3f, pen.Depth, 4);
        }

        [Fact]
        public void YawBox_Corners_And_Bounds()
        {
            Span<Vec3> corners = stackalloc Vec3[8];
            Rotated.GetCorners(corners);
            Aabb bounds = Rotated.Bounds();
            AssertVec(new Vec3(7f, 0f, -7f), bounds.Min);
            AssertVec(new Vec3(13f, 2f, -3f), bounds.Max);
            for (int i = 0; i < 8; i++)
            {
                Assert.True(bounds.Expanded(1e-4f).Contains(corners[i]));
                Assert.Equal(corners[i], Rotated.Corner(i));
            }

            var tilted = new YawBox(new Vec3(1f, 0f, 2f), new Vec3(2f, 1f, 0.5f), 30f);
            Aabb tb = tilted.Bounds();
            float expectedX = 2f * MathF.Cos(MathF.PI / 6f) + 0.5f * MathF.Sin(MathF.PI / 6f);
            Assert.Equal(expectedX, tb.Extents.X, 4);
            for (int i = 0; i < 8; i++)
            {
                Assert.True(tb.Expanded(1e-4f).Contains(tilted.Corner(i)));
            }
        }

        [Fact]
        public void YawBox_FootprintCorners_Are_Counter_Clockwise()
        {
            var poly = new Polygon2(Rotated.FootprintCorner(0), Rotated.FootprintCorner(1), Rotated.FootprintCorner(2), Rotated.FootprintCorner(3));
            Assert.Equal(Rotated.FootprintArea, poly.SignedArea(), 3);
        }

        [Fact]
        public void YawBox_Mirror_Rotation_Is_Exact()
        {
            var box = new YawBox(new Vec3(5f, 1f, 2f), new Vec3(1f, 1f, 2f), 30f);
            YawBox mirrored = box.RotatedAbout(Vec3.Zero, 180f);
            Assert.Equal(new Vec3(-5f, 1f, -2f), mirrored.Center);
            Assert.Equal(-150f, mirrored.YawDeg, 4);
            AssertVec(-box.Forward, mirrored.Forward);
            Assert.Equal(new YawBox(Vec3.Zero, Vec3.One, 0f).Translated(Vec3.Up).Center, Vec3.Up);
        }

        [Fact]
        public void YawBox_OnFloor_Puts_Bottom_On_Floor()
        {
            YawBox box = YawBox.OnFloor(new Vec3(3f, 2f, 1f), new Vec3(2f, 3f, 4f), 0f);
            Assert.Equal(2f, box.Bottom);
            Assert.Equal(5f, box.Top);
            Assert.Equal(new Vec3(1f, 1.5f, 2f), box.HalfExtents);
        }

        [Fact]
        public void Aabb_Queries()
        {
            var box = new Aabb(new Vec3(2f, 3f, 4f), new Vec3(-2f, -1f, 0f));
            Assert.Equal(new Vec3(-2f, -1f, 0f), box.Min);
            Assert.Equal(new Vec3(0f, 1f, 2f), box.Center);
            Assert.Equal(new Vec3(2f, 2f, 2f), box.Extents);
            Assert.True(box.Contains(new Vec3(0f, 0f, 1f)));
            Assert.False(box.Contains(new Vec3(0f, 5f, 1f)));
            Assert.Equal(new Vec3(2f, 3f, 2f), box.ClosestPoint(new Vec3(9f, 9f, 2f)));
            Assert.True(box.Intersects(new Aabb(new Vec3(1f, 1f, 1f), new Vec3(5f, 5f, 5f))));
            Assert.False(box.Intersects(new Aabb(new Vec3(3f, 1f, 1f), new Vec3(5f, 5f, 5f))));
            Assert.True(box.IntersectsSphere(new Vec3(3f, 0f, 1f), 1.01f));
            Assert.Equal(new Vec3(-2f, -1f, 0f), box.Encapsulate(Vec3.Zero).Min);
            Assert.Equal(new Vec3(5f, 3f, 4f), box.Encapsulate(new Vec3(5f, 0f, 0f)).Max);
            Assert.Equal(Vec3.Zero, box.Expanded(-10f).Extents);

            Assert.True(box.Raycast(new Ray(new Vec3(0f, 10f, 1f), Vec3.Down), 20f, out RayHit hit));
            Assert.Equal(7f, hit.Distance, 5);
            Assert.Equal(Vec3.Up, hit.Normal);
            Assert.True(box.Raycast(new Ray(new Vec3(0f, 0f, 1f), Vec3.Down), 20f, out hit));
            Assert.Equal(0f, hit.Distance);
        }

        [Fact]
        public void Capsule_Vertical_And_Bounds()
        {
            Capsule c = Capsule.Vertical(new Vec3(1f, 0f, 2f), 1.8f, 0.35f);
            Assert.Equal(new Vec3(1f, 0.35f, 2f), c.A);
            Assert.Equal(1.45f, c.B.Y, 5);
            Assert.True(c.Contains(new Vec3(1.3f, 1f, 2f)));
            Assert.False(c.Contains(new Vec3(1.4f, 1f, 2f)));
            Aabb b = c.Bounds();
            Assert.Equal(0f, b.Min.Y, 5);
            Assert.Equal(1.8f, b.Max.Y, 5);

            Capsule squat = Capsule.Vertical(Vec3.Zero, 0.4f, 0.5f);
            Assert.Equal(squat.A, squat.B);
            Assert.Equal(0.2f, squat.Radius, 5);
        }

        [Fact]
        public void Ramp_Surface_Height_And_Slope()
        {
            // 2 m wide, 4 m long wedge rising 2 m toward +Z, standing on the floor at y = 1.
            RampShape ramp = RampShape.Wedge(new Vec3(0f, 1f, 0f), 1f, 2f, 0f, 2f);
            Assert.Equal(1f, ramp.BaseHeight, 5);
            Assert.Equal(3f, ramp.TopHeight, 5);
            Assert.Equal(1f, ramp.SurfaceHeightAt(0f, -2f), 5);
            Assert.Equal(2f, ramp.SurfaceHeightAt(0.5f, 0f), 5);
            Assert.Equal(3f, ramp.SurfaceHeightAt(-1f, 2f), 5);
            Assert.True(float.IsNaN(ramp.SurfaceHeightAt(1.1f, 0f)));
            Assert.True(float.IsNaN(ramp.SurfaceHeightAt(0f, 2.1f)));
            Assert.Equal(MathF.Atan(0.5f) * 180f / MathF.PI, ramp.SlopeDeg, 3);
            AssertVec(new Vec3(0f, 2f, -1f).Normalized(), ramp.SurfaceNormal);
            AssertVec(new Vec3(0f, 1f, -2f), ramp.LowEdgeCenter);
            AssertVec(new Vec3(0f, 3f, 2f), ramp.HighEdgeCenter);
        }

        [Fact]
        public void Ramp_Rotated_Surface_Height()
        {
            // Yaw 90: the ramp climbs toward world +X.
            RampShape ramp = RampShape.Wedge(new Vec3(10f, 0f, 0f), 1f, 3f, 90f, 3f);
            Assert.Equal(0f, ramp.SurfaceHeightAt(7f, 0f), 4);
            Assert.Equal(1.5f, ramp.SurfaceHeightAt(10f, 0.5f), 4);
            Assert.Equal(3f, ramp.SurfaceHeightAt(13f, 0f), 4);
            Assert.True(float.IsNaN(ramp.SurfaceHeightAt(10f, 1.5f)));
            Assert.Equal(26.5651f, ramp.SlopeDeg, 3);
        }

        [Fact]
        public void Ramp_Contains_And_ClosestPoint()
        {
            RampShape ramp = RampShape.Wedge(Vec3.Zero, 1f, 2f, 0f, 2f);
            Assert.True(ramp.Contains(new Vec3(0f, 0.5f, 0f)));
            Assert.False(ramp.Contains(new Vec3(0f, 1.5f, 0f)));
            Assert.True(ramp.Contains(new Vec3(0f, 1.5f, 1.5f)));

            var inside = new Vec3(0.2f, 0.3f, 0.5f);
            AssertVec(inside, ramp.ClosestPoint(inside), 1e-6f);

            // Above the middle of the slope: the closest point lies on the slope plane along its normal.
            var above = new Vec3(0f, 2f, 0f);
            Vec3 closest = ramp.ClosestPoint(above);
            Vec3 offset = above - closest;
            AssertVec(ramp.SurfaceNormal * offset.Length(), offset);
            Assert.Equal(ramp.SurfaceHeightAt(closest.X, closest.Z), closest.Y, 4);

            // Beside the ramp: clamped onto the side face.
            AssertVec(new Vec3(1f, 0.5f, 0f), ramp.ClosestPoint(new Vec3(4f, 0.5f, 0f)));
        }

        [Fact]
        public void Ramp_Raycast_Hits_Slope_Ends_And_Sides()
        {
            RampShape ramp = RampShape.Wedge(Vec3.Zero, 1f, 2f, 0f, 2f);

            Assert.True(ramp.Raycast(new Ray(new Vec3(0f, 10f, 1f), Vec3.Down), 20f, out RayHit hit));
            Assert.Equal(10f - 1.5f, hit.Distance, 4);
            AssertVec(ramp.SurfaceNormal, hit.Normal);

            // Horizontal ray from the high side hits the vertical +Z end face.
            Assert.True(ramp.Raycast(new Ray(new Vec3(0f, 1f, 10f), Vec3.Back), 20f, out hit));
            Assert.Equal(8f, hit.Distance, 4);
            AssertVec(Vec3.Forward, hit.Normal);

            // Horizontal ray from the low side at y = 1.5 hits the slope (surface y = 1.5 at z = 1), not the zero-height low end.
            Assert.True(ramp.Raycast(new Ray(new Vec3(0f, 1.5f, -10f), Vec3.Forward), 20f, out hit));
            Assert.Equal(11f, hit.Distance, 4);
            AssertVec(ramp.SurfaceNormal, hit.Normal);

            // Near the top (y = 1.9) the slope is reached only at z = 1.8.
            Assert.False(ramp.Raycast(new Ray(new Vec3(0f, 1.9f, -10f), Vec3.Forward), 11.7f, out _));
            Assert.True(ramp.Raycast(new Ray(new Vec3(0f, 1.9f, -10f), Vec3.Forward), 11.9f, out hit));
            Assert.Equal(11.8f, hit.Distance, 3);

            // Above the box top: no hit at any range.
            Assert.False(ramp.Raycast(new Ray(new Vec3(0f, 2.5f, -10f), Vec3.Forward), 100f, out _));

            Assert.True(ramp.Raycast(new Ray(new Vec3(-5f, 0.2f, 0f), Vec3.Right), 20f, out hit));
            Assert.Equal(4f, hit.Distance, 4);
            AssertVec(Vec3.Left, hit.Normal);

            Assert.True(ramp.Raycast(new Ray(new Vec3(0f, 0.1f, 0f), Vec3.Up), 20f, out hit));
            Assert.Equal(0f, hit.Distance);
        }

        [Fact]
        public void Ramp_With_Plinth_Has_Raised_Low_Edge()
        {
            var box = new YawBox(new Vec3(0f, 1.5f, 0f), new Vec3(1f, 1.5f, 2f), 0f);
            var ramp = new RampShape(box, 1f);
            Assert.Equal(2f, ramp.BaseHeight, 5);
            Assert.Equal(3f, ramp.TopHeight, 5);
            Assert.Equal(2.5f, ramp.SurfaceHeightAt(0f, 0f), 5);
            Assert.True(ramp.Raycast(new Ray(new Vec3(0f, 1f, -10f), Vec3.Forward), 20f, out RayHit hit));
            Assert.Equal(8f, hit.Distance, 4);
            AssertVec(Vec3.Back, hit.Normal);

            var flat = new RampShape(box, 0f);
            Assert.Equal(0f, flat.SlopeDeg);
            Assert.Equal(3f, flat.SurfaceHeightAt(0.5f, 1f), 5);

            var clamped = new RampShape(box, 10f);
            Assert.Equal(3f, clamped.Rise, 5);
        }

        [Fact]
        public void Polygon_Area_Orientation_Perimeter()
        {
            var ccw = new Polygon2(new Vec2(0f, 0f), new Vec2(2f, 0f), new Vec2(2f, 2f), new Vec2(0f, 2f));
            Assert.Equal(4f, ccw.SignedArea(), 5);
            Assert.Equal(4f, ccw.Area(), 5);
            Assert.Equal(8f, ccw.Perimeter(), 5);

            var cw = new Polygon2(new Vec2(0f, 0f), new Vec2(0f, 2f), new Vec2(2f, 2f), new Vec2(2f, 0f));
            Assert.Equal(-4f, cw.SignedArea(), 5);
            Assert.Equal(4f, cw.Area(), 5);

            Assert.Equal(0f, new Polygon2(new Vec2(0f, 0f), new Vec2(1f, 1f)).Area());
            Assert.Equal(0f, new Polygon2().Area());
        }

        [Fact]
        public void Polygon_Contains_Concave()
        {
            // L shape: 3 x 3 square missing its top-right 2 x 2 quadrant.
            var l = new Polygon2(new Vec2(0f, 0f), new Vec2(3f, 0f), new Vec2(3f, 1f), new Vec2(1f, 1f), new Vec2(1f, 3f), new Vec2(0f, 3f));
            Assert.Equal(5f, l.Area(), 5);
            Assert.True(l.Contains(new Vec2(0.5f, 2.5f)));
            Assert.True(l.Contains(new Vec2(2.5f, 0.5f)));
            Assert.False(l.Contains(new Vec2(2f, 2f)));
            Assert.False(l.Contains(new Vec2(-1f, 0.5f)));
            Assert.Equal(1f, l.DistanceToEdges(new Vec2(2f, 2f)), 5);
            Assert.Equal(-1f, l.SignedDistance(new Vec2(2f, 2f)), 5);
            Assert.Equal(0.25f, l.SignedDistance(new Vec2(0.25f, 2f)), 5);
            Assert.Equal(new Vec2(2f, 1f), l.ClosestPointOnEdges(new Vec2(2f, 2f))); // first of two equidistant edges

            // Centroid of the L: weighted average of the 3 x 1 bar and the 1 x 2 column.
            Vec2 c = l.Centroid();
            Assert.Equal((3f * 1.5f + 2f * 0.5f) / 5f, c.X, 4);
            Assert.Equal((3f * 0.5f + 2f * 2f) / 5f, c.Y, 4);
        }

        [Fact]
        public void Polygon_Degenerate_Centroid_And_Bounds()
        {
            var line = new Polygon2(new Vec2(0f, 0f), new Vec2(2f, 0f), new Vec2(4f, 0f));
            Assert.Equal(new Vec2(2f, 0f), line.Centroid());
            Assert.False(line.Contains(new Vec2(1f, 0f)));
            line.GetBounds(out Vec2 min, out Vec2 max);
            Assert.Equal(new Vec2(0f, 0f), min);
            Assert.Equal(new Vec2(4f, 0f), max);

            var source = new[] { new Vec2(0f, 0f), new Vec2(1f, 0f), new Vec2(0f, 1f) };
            var tri = new Polygon2(source);
            source[0] = new Vec2(50f, 50f);
            Assert.Equal(new Vec2(0f, 0f), tri[0]); // copied on construction
            Assert.Equal(3, tri.Count);
        }
    }
}
