using System;
using Confluence.Core.Geometry;
using Confluence.Reactions.Grid;
using Confluence.Sim.Fields;

namespace Confluence.Sim.Spells
{
    /// <summary>
    /// The shape every delivery that creates one gameplay field (spec L128) at a placement shares, whether it is the
    /// framework <see cref="FieldDelivery"/> or a kit's own field delivery (WIZ-002's ash cloud for SPL-012 Ash Curtain,
    /// WIZ-010's stream for SPL-078 Riptide Lane). The shared Fieldcraft records judge and edit fields through it, so a
    /// kit field receives the same field-family records the Economy offers for its "field" tag (spec L243, L543).
    /// "Radius" is a circle's radius; lanes and rectangles use their full width. Setters are guarded by the delivery's
    /// freeze like any <see cref="DeliverySpec"/> property.
    /// </summary>
    public interface IFieldDelivery
    {
        FieldShape Shape { get; }

        /// <summary>Circle radius (m).</summary>
        float Radius { get; set; }

        /// <summary>Lane or rectangle full width (m).</summary>
        float Width { get; set; }

        /// <summary>Lane or rectangle length (m); a circle's diameter.</summary>
        float Length { get; }

        /// <summary>Field lifetime (s, at most 8 s after the global caps, spec L128).</summary>
        float LifetimeSec { get; set; }

        PlacementMode Placement { get; }

        /// <summary>Whether contact with the field deals damage.</summary>
        bool IsDamaging { get; }

        /// <summary>The first-contact payload ("initial direct damage"), or null when the field deals nothing on entry.</summary>
        PayloadSpec EnterPayload { get; }

        /// <summary>The ground footprint and center a field of this delivery gets at a placement point and aim.</summary>
        void FootprintAt(Vec3 ground, Vec3 aim, out Footprint footprint, out Vec3 center);
    }

    /// <summary>A live field created by an <see cref="IFieldDelivery"/> cast (its spell entity).</summary>
    public interface IFieldBody
    {
        CastRecord Cast { get; }
        bool Alive { get; }

        /// <summary>The field exists in the world (a reaction-world cloud appears in the step after its release).</summary>
        bool HasAppeared { get; }

        Footprint Footprint { get; }
        Vec3 Center { get; }

        /// <summary>Where the field was placed: a circle's or rectangle's center, a lane's starting point.</summary>
        Vec2 PlacementPoint { get; }

        /// <summary>The field's entry in the owned-entity registry (spec L128 allocation), or null.</summary>
        OwnedEntity Owned { get; }
    }

    /// <summary>Footprint construction shared by field deliveries (the rule <see cref="FieldDelivery.Spawn"/> applies).</summary>
    public static class FieldFootprints
    {
        /// <summary>
        /// A circle around the placement point, a rectangle centered on it with its length along the aim, or a lane from it
        /// along the aim; <paramref name="center"/> is the footprint's middle (a lane's midpoint).
        /// </summary>
        public static void Of(FieldShape shape, float radius, float width, float length, Vec3 ground, Vec3 aim, out Footprint footprint, out Vec3 center)
        {
            Vec2 dir = new Vec2(aim.X, aim.Z);
            dir = dir.LengthSquared() > 1e-8f ? dir.Normalized() : new Vec2(0f, 1f);
            switch (shape)
            {
                case FieldShape.Rectangle:
                    footprint = Footprint.Rectangle(ground.XZ(), new Vec2(width * 0.5f, length * 0.5f), CMath.YawOf(dir));
                    center = ground;
                    return;
                case FieldShape.Lane:
                {
                    Vec2 from = ground.XZ();
                    Vec2 to = from + dir * length;
                    footprint = Footprint.Lane(from, to, width);
                    Vec2 mid = (from + to) * 0.5f;
                    center = new Vec3(mid.X, ground.Y, mid.Y);
                    return;
                }
                default:
                    footprint = Footprint.Circle(ground.XZ(), Math.Max(0f, radius));
                    center = ground;
                    return;
            }
        }
    }
}
