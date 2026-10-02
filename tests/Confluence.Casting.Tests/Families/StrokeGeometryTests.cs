using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Casting.Grammars;
using Confluence.Core.Geometry;

namespace Confluence.Casting.Tests.Families
{
    public class StrokeGeometryTests
    {
        private static readonly Vec2[] Triangle = { new Vec2(0f, 0f), new Vec2(1f, 0f), new Vec2(0.5f, 0.8f), new Vec2(0f, 0f) };

        private static Vec2[] Transform(IReadOnlyList<Vec2> points, float scale, Vec2 offset, float rotationDeg = 0f)
        {
            CMath.SinCosDeg(rotationDeg, out float s, out float c);
            var result = new Vec2[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                Vec2 p = points[i] * scale;
                result[i] = new Vec2(p.X * c - p.Y * s, p.X * s + p.Y * c) + offset;
            }

            return result;
        }

        /// <summary>Splits each edge into <paramref name="parts"/> samples, like a pointer polled at a given rate.</summary>
        private static List<Vec2> Densify(IReadOnlyList<Vec2> points, params int[] parts)
        {
            var result = new List<Vec2> { points[0] };
            for (int e = 1; e < points.Count; e++)
                for (int k = 1; k <= parts[e - 1]; k++)
                    result.Add(Vec2.LerpUnclamped(points[e - 1], points[e], k / (float)parts[e - 1]));
            return result;
        }

        [Fact]
        public void Scale_And_Position_On_The_Pane_Do_Not_Change_The_Score()
        {
            var small = Transform(Triangle, 0.3f, new Vec2(0.4f, 0.2f));
            Assert.True(StrokeGeometry.Accuracy(Triangle, small, normalizeRotation: false) > 0.99f);
        }

        [Fact]
        public void Polling_Rate_Does_Not_Change_The_Score()
        {
            // Spec L490: device polling rate never changes the score. Very uneven sampling still reads the same shape.
            var dense = Densify(Triangle, 40, 3, 1);
            Assert.True(StrokeGeometry.Accuracy(Triangle, dense, normalizeRotation: false) > 0.99f);
            Assert.Equal(StrokeGeometry.Segments(Triangle, 0.05f), StrokeGeometry.Segments(dense, 0.05f));
        }

        [Fact]
        public void Orientation_Is_Forgiven_Only_Where_The_Rune_Permits()
        {
            // Spec L369: normalization for orientation "where the rune definition permits it".
            var tilted = Transform(Triangle, 0.5f, new Vec2(0.3f, 0.3f), 40f);
            Assert.True(StrokeGeometry.Accuracy(Triangle, tilted, normalizeRotation: true) > 0.98f);
            Assert.True(StrokeGeometry.Accuracy(Triangle, tilted, normalizeRotation: false) < 0.5f);
        }

        [Fact]
        public void A_Different_Shape_Scores_Low()
        {
            var line = new[] { new Vec2(0f, 0f), new Vec2(1f, 0f) };
            Assert.True(StrokeGeometry.Accuracy(Triangle, line, normalizeRotation: false) < 0.5f);
        }

        [Fact]
        public void Resample_Spaces_Points_Evenly_Along_The_Path()
        {
            var r = StrokeGeometry.Resample(new[] { new Vec2(0f, 0f), new Vec2(0.25f, 0f), new Vec2(1f, 0f) }, 5);
            for (int i = 0; i < 5; i++) Assert.Equal(i * 0.25f, r[i].X, 5);
        }

        [Fact]
        public void Direction_Keys_Trace_The_Same_Segments_As_The_Mouse()
        {
            // Spec L369: "A directional-key equivalent uses the same segment count".
            var mouse = Densify(new[] { new Vec2(0.2f, 0.8f), new Vec2(0.2f, 0.2f), new Vec2(0.8f, 0.2f), new Vec2(0.5f, 0.5f) }, 12, 9, 7);
            var keys = new[] { TraceDirection.Down, TraceDirection.Right, TraceDirection.UpLeft };
            var mouseSegments = StrokeGeometry.Segments(mouse, 0.05f);
            Assert.Equal(keys, mouseSegments);
            Assert.Equal(keys, StrokeGeometry.Segments(StrokeGeometry.FromDirections(keys), 0.05f));
        }

        [Fact]
        public void Tiny_Jitter_Is_Dropped_And_Equal_Directions_Merge()
        {
            var points = new[] { new Vec2(0f, 0f), new Vec2(0.3f, 0f), new Vec2(0.3f, 0.004f), new Vec2(0.6f, 0.004f) };
            Assert.Equal(new[] { TraceDirection.Right }, StrokeGeometry.Segments(points, 0.05f));
        }

        [Theory]
        [InlineData(1f, 0.4f, TraceDirection.Right)]
        [InlineData(1f, 0.5f, TraceDirection.UpRight)]
        [InlineData(0f, -1f, TraceDirection.Down)]
        [InlineData(-1f, 1f, TraceDirection.UpLeft)]
        [InlineData(-0.3f, -1f, TraceDirection.Down)]
        [InlineData(-1f, -0.9f, TraceDirection.DownLeft)]
        public void Quantize_Uses_Eight_Sectors(float x, float y, TraceDirection expected)
        {
            Assert.Equal(expected, StrokeGeometry.Quantize(new Vec2(x, y)));
        }
    }
}
