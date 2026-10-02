using System;
using System.Collections.Generic;
using Confluence.Core.Geometry;

namespace Confluence.Casting.Grammars
{
    /// <summary>
    /// Shape scoring for the GestureStroke family (WIZ-003 runes, WIZ-009 sigils, WIZ-031 cuts). Samples are pane
    /// coordinates (0-1 across the pane, +Y up), so DPI, resolution, and polling rate never change a score (spec
    /// L490). Normalization for scale and, where the rune permits, orientation rewards the recognizable shape rather
    /// than handwriting (spec L369). Direction-key tracing produces the same segment structure (spec L369, L474).
    /// Uses only arithmetic and square roots, so results are identical on server and client.
    /// </summary>
    public static class StrokeGeometry
    {
        public const int DefaultSamples = 32;

        public static float PathLength(IReadOnlyList<Vec2> points)
        {
            float length = 0f;
            for (int i = 1; i < points.Count; i++) length += Vec2.Distance(points[i - 1], points[i]);
            return length;
        }

        /// <summary>
        /// Translates the stroke to its centroid; with <paramref name="normalizeRotation"/>, rotates it so the
        /// centroid-to-first-point direction points along +X; then scales it so the larger bounding-box side is 1.
        /// Rotating before scaling keeps the scale identical at every orientation. Pass evenly resampled points (as
        /// <see cref="Accuracy"/> does) so the centroid does not depend on the pointer polling rate.
        /// </summary>
        public static Vec2[] Normalize(IReadOnlyList<Vec2> points, bool normalizeRotation)
        {
            if (points == null || points.Count == 0) throw new ArgumentException("A stroke needs points.", nameof(points));
            Vec2 sum = Vec2.Zero;
            for (int i = 0; i < points.Count; i++) sum += points[i];
            Vec2 centroid = sum / points.Count;

            float cos = 1f, sin = 0f;
            if (normalizeRotation)
            {
                Vec2 lead = points[0] - centroid;
                float len = lead.Length();
                if (len > 1e-6f)
                {
                    cos = lead.X / len;
                    sin = lead.Y / len;
                }
            }

            var result = new Vec2[points.Count];
            Vec2 min = default, max = default;
            for (int i = 0; i < points.Count; i++)
            {
                Vec2 p = points[i] - centroid;
                // Rotate by -angle(lead): (x cos + y sin, -x sin + y cos).
                result[i] = new Vec2(p.X * cos + p.Y * sin, -p.X * sin + p.Y * cos);
                min = i == 0 ? result[i] : Vec2.Min(min, result[i]);
                max = i == 0 ? result[i] : Vec2.Max(max, result[i]);
            }

            float size = Math.Max(max.X - min.X, max.Y - min.Y);
            float scale = size > 1e-6f ? 1f / size : 1f;
            for (int i = 0; i < result.Length; i++) result[i] = result[i] * scale;
            return result;
        }

        /// <summary>Resamples a polyline to <paramref name="count"/> points evenly spaced along its length.</summary>
        public static Vec2[] Resample(IReadOnlyList<Vec2> points, int count)
        {
            if (count < 2) throw new ArgumentOutOfRangeException(nameof(count));
            var result = new Vec2[count];
            float total = PathLength(points);
            if (points.Count == 1 || total <= 1e-6f)
            {
                for (int i = 0; i < count; i++) result[i] = points[0];
                return result;
            }

            float step = total / (count - 1);
            int seg = 1;
            float segStart = 0f;
            for (int i = 0; i < count; i++)
            {
                float target = i == count - 1 ? total : i * step;
                while (seg < points.Count - 1 && segStart + Vec2.Distance(points[seg - 1], points[seg]) < target)
                {
                    segStart += Vec2.Distance(points[seg - 1], points[seg]);
                    seg++;
                }

                float segLen = Vec2.Distance(points[seg - 1], points[seg]);
                float t = segLen > 1e-6f ? (target - segStart) / segLen : 0f;
                result[i] = Vec2.Lerp(points[seg - 1], points[seg], t);
            }

            return result;
        }

        /// <summary>
        /// Path accuracy 0-1 between a template and a candidate: both are resampled evenly along their length, then
        /// normalized; the score is 1 minus the mean point distance divided by <paramref name="tolerance"/>
        /// (normalized units), clamped. Resampling first makes the score independent of sample density (spec L490).
        /// </summary>
        public static float Accuracy(IReadOnlyList<Vec2> template, IReadOnlyList<Vec2> candidate, bool normalizeRotation, float tolerance = 0.25f, int samples = DefaultSamples)
        {
            var a = Normalize(Resample(template, samples), normalizeRotation);
            var b = Normalize(Resample(candidate, samples), normalizeRotation);
            float mean = 0f;
            for (int i = 0; i < samples; i++) mean += Vec2.Distance(a[i], b[i]);
            mean /= samples;
            return Math.Max(0f, 1f - mean / tolerance);
        }

        /// <summary>Distance between two raw pane points in pane widths (WIZ-003 "endpoints within 8% of pane width").</summary>
        public static float PaneDistance(Vec2 a, Vec2 b) => Vec2.Distance(a, b);

        /// <summary>
        /// The stroke as eight-way segments: runs of samples heading the same quantized direction, kept when they
        /// cover at least <paramref name="minSegment"/> pane units. Consecutive equal directions merge.
        /// </summary>
        public static List<TraceDirection> Segments(IReadOnlyList<Vec2> points, float minSegment)
        {
            var result = new List<TraceDirection>();
            int i = 0;
            while (i < points.Count - 1)
            {
                Vec2 d = points[i + 1] - points[i];
                if (d.LengthSquared() <= 1e-12f)
                {
                    i++;
                    continue;
                }

                TraceDirection dir = Quantize(d);
                float run = 0f;
                int j = i;
                while (j < points.Count - 1)
                {
                    Vec2 step = points[j + 1] - points[j];
                    if (step.LengthSquared() > 1e-12f && Quantize(step) != dir) break;
                    run += step.Length();
                    j++;
                }

                if (run >= minSegment && (result.Count == 0 || result[result.Count - 1] != dir)) result.Add(dir);
                i = j;
            }

            return result;
        }

        /// <summary>The polyline traced by direction keys: one unit step per key (spec L369 same segment count).</summary>
        public static Vec2[] FromDirections(IReadOnlyList<TraceDirection> directions)
        {
            var result = new Vec2[directions.Count + 1];
            for (int i = 0; i < directions.Count; i++) result[i + 1] = result[i] + Step(directions[i]);
            return result;
        }

        public static Vec2 Step(TraceDirection direction)
        {
            const float d = 0.70710678f;
            switch (direction)
            {
                case TraceDirection.Up: return new Vec2(0f, 1f);
                case TraceDirection.UpRight: return new Vec2(d, d);
                case TraceDirection.Right: return new Vec2(1f, 0f);
                case TraceDirection.DownRight: return new Vec2(d, -d);
                case TraceDirection.Down: return new Vec2(0f, -1f);
                case TraceDirection.DownLeft: return new Vec2(-d, -d);
                case TraceDirection.Left: return new Vec2(-1f, 0f);
                default: return new Vec2(-d, d);
            }
        }

        /// <summary>Eight-way direction of a step using slope comparisons (tan 22.5 deg), no trigonometry.</summary>
        public static TraceDirection Quantize(Vec2 v)
        {
            const float tan225 = 0.41421356f;
            float ax = Math.Abs(v.X), ay = Math.Abs(v.Y);
            if (ay <= ax * tan225) return v.X >= 0f ? TraceDirection.Right : TraceDirection.Left;
            if (ax <= ay * tan225) return v.Y >= 0f ? TraceDirection.Up : TraceDirection.Down;
            if (v.X >= 0f) return v.Y >= 0f ? TraceDirection.UpRight : TraceDirection.DownRight;
            return v.Y >= 0f ? TraceDirection.UpLeft : TraceDirection.DownLeft;
        }
    }
}
