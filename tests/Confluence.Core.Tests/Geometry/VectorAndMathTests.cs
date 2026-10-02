using System;
using System.Globalization;
using System.Threading;
using Confluence.Core.Geometry;

namespace Confluence.Core.Tests.Geometry
{
    public class VectorAndMathTests
    {
        private const float Tol = 1e-5f;

        private static void AssertVec(Vec3 expected, Vec3 actual, float tol = Tol)
        {
            Assert.True(Vec3.Distance(expected, actual) <= tol, $"expected {expected} but was {actual}");
        }

        [Fact]
        public void Vec3_Operators_And_Products()
        {
            var a = new Vec3(1f, 2f, 3f);
            var b = new Vec3(-4f, 0.5f, 2f);
            Assert.Equal(new Vec3(-3f, 2.5f, 5f), a + b);
            Assert.Equal(new Vec3(5f, 1.5f, 1f), a - b);
            Assert.Equal(new Vec3(2f, 4f, 6f), a * 2f);
            Assert.Equal(new Vec3(2f, 4f, 6f), 2f * a);
            Assert.Equal(new Vec3(0.5f, 1f, 1.5f), a / 2f);
            Assert.Equal(new Vec3(-1f, -2f, -3f), -a);
            Assert.Equal(1f * -4f + 2f * 0.5f + 3f * 2f, Vec3.Dot(a, b));
            Assert.Equal(14f, a.LengthSquared());
            Assert.Equal(MathF.Sqrt(14f), a.Length());
        }

        [Fact]
        public void Vec3_Cross_Follows_Unity_Axes()
        {
            Assert.Equal(Vec3.Forward, Vec3.Cross(Vec3.Right, Vec3.Up));
            Assert.Equal(Vec3.Right, Vec3.Cross(Vec3.Up, Vec3.Forward));
            Assert.Equal(Vec3.Up, Vec3.Cross(Vec3.Forward, Vec3.Right));
        }

        [Fact]
        public void Vec3_Normalized_Is_Safe_For_Zero()
        {
            Assert.Equal(Vec3.Zero, Vec3.Zero.Normalized());
            Assert.Equal(Vec3.Zero, new Vec3(1e-9f, 0f, 0f).Normalized());
            Assert.Equal(Vec3.Up, Vec3.Zero.NormalizedOr(Vec3.Up));
            AssertVec(new Vec3(0.6f, 0f, 0.8f), new Vec3(3f, 0f, 4f).Normalized());
            Assert.Equal(1f, new Vec3(-7f, 2f, 9f).Normalized().Length(), 5);
        }

        [Fact]
        public void Vec3_Distances_Lerp_And_Projections()
        {
            var a = new Vec3(0f, 10f, 0f);
            var b = new Vec3(3f, -5f, 4f);
            Assert.Equal(5f, Vec3.DistanceXZ(a, b));
            Assert.Equal(MathF.Sqrt(9f + 225f + 16f), Vec3.Distance(a, b), 5);
            Assert.Equal(new Vec2(3f, 4f), b.XZ());
            Assert.Equal(new Vec3(3f, 1f, 4f), b.WithY(1f));
            Assert.Equal(b, Vec3.Lerp(a, b, 1f));
            Assert.Equal(b, Vec3.Lerp(a, b, 2f)); // clamped
            Assert.Equal(new Vec3(6f, -20f, 8f), Vec3.LerpUnclamped(a, b, 2f));
            Assert.Equal(new Vec3(1.5f, 2.5f, 2f), Vec3.Lerp(a, b, 0.5f));
            Assert.Equal(b, Vec3.MoveTowards(a, b, 100f));
            AssertVec(new Vec3(0f, 9f, 0f), Vec3.MoveTowards(a, new Vec3(0f, 0f, 0f), 1f));
        }

        [Fact]
        public void Vec3_Equality_And_HashCode()
        {
            var a = new Vec3(1.25f, -3f, 8f);
            var b = new Vec3(1.25f, -3f, 8f);
            Assert.True(a == b);
            Assert.True(a.Equals(b));
            Assert.True(a.Equals((object)b));
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
            Assert.True(a != new Vec3(1.25f, -3f, 8.0001f));
            Assert.False(a.Equals("not a vector"));
        }

        [Fact]
        public void ToString_Is_Culture_Invariant()
        {
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                Assert.Equal("(1.5, -2, 0.25)", new Vec3(1.5f, -2f, 0.25f).ToString());
                Assert.Equal("(1.5, -2)", new Vec2(1.5f, -2f).ToString());
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Fact]
        public void Vec2_Basics_And_Yaw_Rotation()
        {
            var a = new Vec2(3f, 4f);
            Assert.Equal(5f, a.Length());
            Assert.True(Vec2.Distance(new Vec2(0.6f, 0.8f), a.Normalized()) < Tol);
            Assert.Equal(Vec2.Zero, Vec2.Zero.Normalized());
            Assert.Equal(-2f, Vec2.Cross(new Vec2(1f, 0f), new Vec2(0f, -2f)));
            Assert.Equal(new Vec3(3f, 7f, 4f), a.ToXZ(7f));
            // Positive yaw turns right: forward (+Z) rotates to +X.
            Assert.Equal(new Vec2(1f, 0f), Vec2.Forward.RotatedByYaw(90f));
            Assert.Equal(new Vec2(0f, -1f), Vec2.Forward.RotatedByYaw(180f));
            Assert.Equal(new Vec2(1f, 0f), Vec2.FromYaw(90f));
        }

        [Theory]
        [InlineData(0f, 0f)]
        [InlineData(180f, 180f)]
        [InlineData(-180f, 180f)]
        [InlineData(540f, 180f)]
        [InlineData(-540f, 180f)]
        [InlineData(190f, -170f)]
        [InlineData(-190f, 170f)]
        [InlineData(360f, 0f)]
        [InlineData(725f, 5f)]
        [InlineData(-90f, -90f)]
        public void WrapAngle_Maps_Into_Half_Open_Range(float input, float expected)
        {
            float wrapped = CMath.WrapAngle(input);
            Assert.Equal(expected, wrapped, 4);
            Assert.True(wrapped > -180f && wrapped <= 180f);
        }

        [Fact]
        public void DeltaAngle_Takes_Shortest_Path()
        {
            Assert.Equal(20f, CMath.DeltaAngle(170f, -170f), 4);
            Assert.Equal(-20f, CMath.DeltaAngle(-170f, 170f), 4);
            Assert.Equal(180f, CMath.DeltaAngle(0f, 180f), 4);
            Assert.Equal(-90f, CMath.DeltaAngle(45f, -45f), 4);
            Assert.Equal(10f, CMath.MoveTowardsAngle(0f, 90f, 10f), 4);
            Assert.Equal(-170f, CMath.MoveTowardsAngle(170f, -170f, 30f), 4);
        }

        [Fact]
        public void Yaw_Convention_Zero_Faces_Forward_Positive_Turns_Right()
        {
            Assert.Equal(Vec3.Forward, CMath.ForwardFromYaw(0f));
            Assert.Equal(Vec3.Right, CMath.ForwardFromYaw(90f));
            Assert.Equal(Vec3.Back, CMath.ForwardFromYaw(180f));
            Assert.Equal(Vec3.Left, CMath.ForwardFromYaw(-90f));
            Assert.Equal(Vec3.Left, CMath.ForwardFromYaw(270f));
            Assert.Equal(Vec3.Right, CMath.RightFromYaw(0f));
            Assert.Equal(Vec3.Back, CMath.RightFromYaw(90f));
            AssertVec(new Vec3(MathF.Sqrt(0.5f), 0f, MathF.Sqrt(0.5f)), CMath.ForwardFromYaw(45f));
        }

        [Fact]
        public void YawOf_Round_Trips_ForwardFromYaw()
        {
            for (float yaw = -179f; yaw <= 180f; yaw += 7f)
            {
                Assert.Equal(yaw, CMath.YawOf(CMath.ForwardFromYaw(yaw)), 3);
            }

            Assert.Equal(180f, CMath.YawOf(new Vec3(-0f, 0f, -1f)));
            Assert.Equal(0f, CMath.YawOf(Vec3.Up));
            Assert.Equal(0f, CMath.YawOf(Vec3.Zero));
            Assert.Equal(90f, CMath.YawOf(new Vec2(2f, 0f)), 4);
        }

        [Fact]
        public void ForwardFromYawPitch_Positive_Pitch_Looks_Up()
        {
            Assert.Equal(Vec3.Up, CMath.ForwardFromYawPitch(37f, 90f));
            AssertVec(new Vec3(0f, MathF.Sqrt(0.5f), MathF.Sqrt(0.5f)), CMath.ForwardFromYawPitch(0f, 45f));
            AssertVec(new Vec3(MathF.Sqrt(0.5f), -MathF.Sqrt(0.5f), 0f), CMath.ForwardFromYawPitch(90f, -45f));
            Assert.Equal(30f, CMath.PitchOf(CMath.ForwardFromYawPitch(120f, 30f)), 3);
            Assert.Equal(1f, CMath.ForwardFromYawPitch(33f, 21f).Length(), 5);
        }

        [Fact]
        public void AngleBetween_Handles_Degenerate_Input()
        {
            Assert.Equal(90f, CMath.AngleBetweenDeg(Vec3.Right, Vec3.Forward), 4);
            Assert.Equal(180f, CMath.AngleBetweenDeg(Vec3.Up, Vec3.Down), 4);
            Assert.Equal(0f, CMath.AngleBetweenDeg(new Vec3(2f, 0f, 0f), new Vec3(5f, 0f, 0f)), 4);
            Assert.Equal(45f, CMath.AngleBetweenDeg(new Vec3(1f, 0f, 0f), new Vec3(1f, 1f, 0f)), 3);
            Assert.Equal(0f, CMath.AngleBetweenDeg(Vec3.Zero, Vec3.Up));
            Assert.Equal(90f, CMath.AngleBetweenDeg(new Vec2(1f, 0f), new Vec2(0f, 3f)), 4);
        }

        [Fact]
        public void Scalar_Helpers()
        {
            Assert.Equal(2f, CMath.Clamp(5f, -1f, 2f));
            Assert.Equal(-1f, CMath.Clamp(-5f, -1f, 2f));
            Assert.Equal(3, CMath.Clamp(3, 0, 10));
            Assert.Equal(1f, CMath.Clamp01(1.5f));
            Assert.Equal(0f, CMath.Clamp01(-0.5f));
            Assert.Equal(7f, CMath.Lerp(3f, 7f, 1f));
            Assert.Equal(7f, CMath.Lerp(3f, 7f, 3f));
            Assert.Equal(5f, CMath.Lerp(3f, 7f, 0.5f));
            Assert.Equal(0.25f, CMath.InverseLerp(10f, 20f, 12.5f));
            Assert.Equal(0f, CMath.InverseLerp(4f, 4f, 9f));
            Assert.Equal(1f, CMath.InverseLerp(10f, 20f, 99f));
            Assert.Equal(1.5f, CMath.MoveTowards(1f, 3f, 0.5f));
            Assert.Equal(3f, CMath.MoveTowards(1f, 3f, 5f));
            Assert.Equal(0.5f, CMath.MoveTowards(1f, -3f, 0.5f));
            Assert.True(CMath.Approximately(1000f, 1000.005f));
            Assert.False(CMath.Approximately(1f, 1.001f));
            Assert.Equal(MathF.PI, CMath.DegToRad(180f), 5);
            Assert.Equal(90f, CMath.RadToDeg(MathF.PI / 2f), 4);
            Assert.Equal(0f, CMath.SmoothStep01(0f));
            Assert.Equal(1f, CMath.SmoothStep01(1f));
            Assert.Equal(0.5f, CMath.SmoothStep01(0.5f));
            Assert.Equal(1f, CMath.SmoothStep(5f, 5f, 5f));
            Assert.Equal(0f, CMath.SmoothStep(5f, 5f, 4.9f));
            Assert.Equal(10f, CMath.WrapAngle360(370f), 4);
            Assert.Equal(350f, CMath.WrapAngle360(-10f), 4);
        }

        [Fact]
        public void SinCosDeg_Is_Exact_On_Right_Angles()
        {
            foreach (float deg in new[] { 0f, 90f, 180f, 270f, 360f, -90f, -180f, 450f })
            {
                CMath.SinCosDeg(deg, out float s, out float c);
                Assert.True(s == MathF.Round(s) && c == MathF.Round(c), $"{deg}: {s}, {c}");
                Assert.Equal(1f, s * s + c * c);
            }

            CMath.SinCosDeg(30f, out float s30, out float c30);
            Assert.Equal(0.5f, s30, 6);
            Assert.Equal(MathF.Sqrt(3f) / 2f, c30, 6);
        }
    }
}
