using System;
using Confluence.Core.Geometry;

namespace Confluence.Reactions.Grid
{
    public enum FootprintShape
    {
        /// <summary>The single cell containing the point (a quick projectile's "one local unit", L163).</summary>
        Point,

        /// <summary>Disc on the ground plane.</summary>
        Circle,

        /// <summary>Yaw-rotated rectangle (lanes, strips, curtains).</summary>
        Rectangle,

        /// <summary>Line from A to B with a width (trails, rails, beams along the ground).</summary>
        Segment,

        /// <summary>Ground-plane cone from an apex along a direction.</summary>
        Cone,
    }

    /// <summary>
    /// A spell's declared ground footprint (spec L136 "An operation modifies a bounded cell set based on a spell's
    /// declared footprint"). Coordinates are ground-plane (X, Y = world Z). A cell is covered when its center lies
    /// inside the shape; a shape that covers no cell center still covers the cell holding its anchor.
    /// </summary>
    public readonly struct Footprint
    {
        public readonly FootprintShape Shape;

        /// <summary>Circle/rectangle center, segment start, cone apex, or the point.</summary>
        public readonly Vec2 A;

        /// <summary>Segment end.</summary>
        public readonly Vec2 B;

        /// <summary>Circle radius, segment half width, or cone length.</summary>
        public readonly float Radius;

        /// <summary>Rectangle half size (X across, Y along the yaw direction).</summary>
        public readonly Vec2 HalfSize;

        /// <summary>Rectangle yaw (degrees, 0 = +Z, positive turns right).</summary>
        public readonly float YawDeg;

        /// <summary>Cone unit direction.</summary>
        public readonly Vec2 Direction;

        /// <summary>Cone half angle in degrees.</summary>
        public readonly float HalfAngleDeg;

        private Footprint(FootprintShape shape, Vec2 a, Vec2 b, float radius, Vec2 halfSize, float yawDeg, Vec2 direction, float halfAngleDeg)
        {
            Shape = shape;
            A = a;
            B = b;
            Radius = radius;
            HalfSize = halfSize;
            YawDeg = yawDeg;
            Direction = direction;
            HalfAngleDeg = halfAngleDeg;
        }

        public static Footprint Point(Vec2 point) =>
            new Footprint(FootprintShape.Point, point, point, 0f, Vec2.Zero, 0f, Vec2.Zero, 0f);

        public static Footprint Circle(Vec2 center, float radius) =>
            new Footprint(FootprintShape.Circle, center, center, MathF.Max(0f, radius), Vec2.Zero, 0f, Vec2.Zero, 0f);

        /// <summary>Rectangle with half size (across, along) rotated by yaw.</summary>
        public static Footprint Rectangle(Vec2 center, Vec2 halfSize, float yawDeg) =>
            new Footprint(FootprintShape.Rectangle, center, center, 0f, new Vec2(MathF.Abs(halfSize.X), MathF.Abs(halfSize.Y)), yawDeg, Vec2.Zero, 0f);

        /// <summary>A lane of the given width from one end point to the other (Riptide Lane, Ash Corridor).</summary>
        public static Footprint Lane(Vec2 from, Vec2 to, float width)
        {
            Vec2 delta = to - from;
            float length = delta.Length();
            float yaw = length > CMath.Epsilon ? CMath.YawOf(delta) : 0f;
            return Rectangle((from + to) * 0.5f, new Vec2(width * 0.5f, length * 0.5f), yaw);
        }

        /// <summary>Capsule-ended line of the given total width (trails, rails).</summary>
        public static Footprint Segment(Vec2 a, Vec2 b, float width) =>
            new Footprint(FootprintShape.Segment, a, b, MathF.Max(0f, width * 0.5f), Vec2.Zero, 0f, Vec2.Zero, 0f);

        public static Footprint Cone(Vec2 apex, Vec2 direction, float length, float halfAngleDeg) =>
            new Footprint(FootprintShape.Cone, apex, apex, MathF.Max(0f, length), Vec2.Zero, 0f, direction.Normalized(), CMath.Clamp(halfAngleDeg, 0f, 180f));

        /// <summary>The anchor point whose cell is covered even by a tiny shape.</summary>
        public Vec2 Anchor => A;

        public bool Contains(Vec2 p)
        {
            switch (Shape)
            {
                case FootprintShape.Point:
                    return false; // resolved by cell, see CellGrid.Rasterize
                case FootprintShape.Circle:
                    return Vec2.DistanceSquared(p, A) <= Radius * Radius;
                case FootprintShape.Rectangle:
                {
                    Vec2 local = (p - A).RotatedByYaw(-YawDeg);
                    return MathF.Abs(local.X) <= HalfSize.X && MathF.Abs(local.Y) <= HalfSize.Y;
                }
                case FootprintShape.Segment:
                {
                    Vec2 closest = Intersections.ClosestPointOnSegment(p, A, B);
                    return Vec2.DistanceSquared(p, closest) <= Radius * Radius;
                }
                case FootprintShape.Cone:
                {
                    Vec2 d = p - A;
                    float distSq = d.LengthSquared();
                    if (distSq > Radius * Radius)
                    {
                        return false;
                    }

                    if (distSq < CMath.Epsilon)
                    {
                        return true;
                    }

                    float cos = Vec2.Dot(d, Direction) / MathF.Sqrt(distSq);
                    return cos >= MathF.Cos(CMath.DegToRad(HalfAngleDeg)) - 1e-6f;
                }
                default:
                    return false;
            }
        }

        /// <summary>Axis-aligned ground-plane bounds.</summary>
        public void GetBounds(out Vec2 min, out Vec2 max)
        {
            switch (Shape)
            {
                case FootprintShape.Circle:
                case FootprintShape.Cone:
                    min = A - new Vec2(Radius, Radius);
                    max = A + new Vec2(Radius, Radius);
                    return;
                case FootprintShape.Rectangle:
                {
                    Vec2 ex = new Vec2(HalfSize.X, 0f).RotatedByYaw(YawDeg);
                    Vec2 ez = new Vec2(0f, HalfSize.Y).RotatedByYaw(YawDeg);
                    var extent = new Vec2(MathF.Abs(ex.X) + MathF.Abs(ez.X), MathF.Abs(ex.Y) + MathF.Abs(ez.Y));
                    min = A - extent;
                    max = A + extent;
                    return;
                }
                case FootprintShape.Segment:
                    min = Vec2.Min(A, B) - new Vec2(Radius, Radius);
                    max = Vec2.Max(A, B) + new Vec2(Radius, Radius);
                    return;
                default:
                    min = A;
                    max = A;
                    return;
            }
        }

        public override string ToString() => Shape + "(" + A + ")";
    }
}
