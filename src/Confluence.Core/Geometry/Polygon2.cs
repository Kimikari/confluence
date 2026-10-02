using System;
using System.Collections.Generic;

namespace Confluence.Core.Geometry
{
    /// <summary>
    /// Closed simple polygon on the ground plane (the last vertex connects back to the first).
    /// Orientation: signed area is positive for counter-clockwise vertices seen from above (+X right, +Z up).
    /// Immutable; the vertex list is copied on construction.
    /// </summary>
    public sealed class Polygon2
    {
        private readonly Vec2[] _points;

        public Polygon2(params Vec2[] points)
        {
            _points = points == null ? new Vec2[0] : (Vec2[])points.Clone();
        }

        public Polygon2(IReadOnlyList<Vec2> points)
        {
            if (points == null)
            {
                _points = new Vec2[0];
                return;
            }

            _points = new Vec2[points.Count];
            for (int i = 0; i < _points.Length; i++)
            {
                _points[i] = points[i];
            }
        }

        public int Count => _points.Length;

        public Vec2 this[int index] => _points[index];

        public IReadOnlyList<Vec2> Points => _points;

        /// <summary>Shoelace area: positive when counter-clockwise seen from above. Zero for fewer than 3 vertices.</summary>
        public float SignedArea()
        {
            int n = _points.Length;
            if (n < 3)
            {
                return 0f;
            }

            // Accumulate in double relative to the first vertex to limit cancellation on large coordinates.
            Vec2 origin = _points[0];
            double sum = 0.0;
            for (int i = 1; i < n - 1; i++)
            {
                Vec2 a = _points[i] - origin;
                Vec2 b = _points[i + 1] - origin;
                sum += (double)a.X * b.Y - (double)a.Y * b.X;
            }

            return (float)(sum * 0.5);
        }

        public float Area() => MathF.Abs(SignedArea());

        public float Perimeter()
        {
            int n = _points.Length;
            if (n < 2)
            {
                return 0f;
            }

            float sum = 0f;
            for (int i = 0; i < n; i++)
            {
                sum += Vec2.Distance(_points[i], _points[(i + 1) % n]);
            }

            return n == 2 ? sum * 0.5f : sum;
        }

        /// <summary>Area centroid; falls back to the vertex average for degenerate (zero-area) polygons.</summary>
        public Vec2 Centroid()
        {
            int n = _points.Length;
            if (n == 0)
            {
                return Vec2.Zero;
            }

            Vec2 origin = _points[0];
            double area2 = 0.0;
            double cx = 0.0;
            double cy = 0.0;
            for (int i = 1; i < n - 1; i++)
            {
                Vec2 a = _points[i] - origin;
                Vec2 b = _points[i + 1] - origin;
                double cross = (double)a.X * b.Y - (double)a.Y * b.X;
                area2 += cross;
                cx += (a.X + b.X) * cross;
                cy += (a.Y + b.Y) * cross;
            }

            if (Math.Abs(area2) < 1e-12)
            {
                double sx = 0.0;
                double sy = 0.0;
                for (int i = 0; i < n; i++)
                {
                    sx += _points[i].X;
                    sy += _points[i].Y;
                }

                return new Vec2((float)(sx / n), (float)(sy / n));
            }

            // Triangles fan from the origin vertex: centroid = sum((a + b) * cross) / (3 * area2).
            return new Vec2((float)(origin.X + cx / (3.0 * area2)), (float)(origin.Y + cy / (3.0 * area2)));
        }

        /// <summary>Even-odd containment. Points exactly on an edge may report either result.</summary>
        public bool Contains(Vec2 p)
        {
            int n = _points.Length;
            if (n < 3)
            {
                return false;
            }

            bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Vec2 a = _points[i];
                Vec2 b = _points[j];
                if ((a.Y > p.Y) != (b.Y > p.Y))
                {
                    float xCross = a.X + (p.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y);
                    if (p.X < xCross)
                    {
                        inside = !inside;
                    }
                }
            }

            return inside;
        }

        /// <summary>Unsigned distance from p to the nearest edge.</summary>
        public float DistanceToEdges(Vec2 p) => MathF.Sqrt(DistanceToEdgesSquared(p, out _));

        /// <summary>Distance to the nearest edge, positive inside and negative outside.</summary>
        public float SignedDistance(Vec2 p)
        {
            float d = DistanceToEdges(p);
            return Contains(p) ? d : -d;
        }

        public Vec2 ClosestPointOnEdges(Vec2 p)
        {
            DistanceToEdgesSquared(p, out Vec2 closest);
            return closest;
        }

        private float DistanceToEdgesSquared(Vec2 p, out Vec2 closest)
        {
            int n = _points.Length;
            if (n == 0)
            {
                closest = p;
                return float.PositiveInfinity;
            }

            closest = _points[0];
            float best = Vec2.DistanceSquared(p, closest);
            for (int i = 0; i < n; i++)
            {
                Vec2 q = Intersections.ClosestPointOnSegment(p, _points[i], _points[(i + 1) % n]);
                float d = Vec2.DistanceSquared(p, q);
                if (d < best)
                {
                    best = d;
                    closest = q;
                }
            }

            return best;
        }

        public void GetBounds(out Vec2 min, out Vec2 max)
        {
            if (_points.Length == 0)
            {
                min = Vec2.Zero;
                max = Vec2.Zero;
                return;
            }

            min = _points[0];
            max = _points[0];
            for (int i = 1; i < _points.Length; i++)
            {
                min = Vec2.Min(min, _points[i]);
                max = Vec2.Max(max, _points[i]);
            }
        }
    }
}
