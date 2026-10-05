using System;
using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Reactions.Grid;
using Confluence.Sim.Duel;
using Confluence.Sim.Fields;
using Confluence.Sim.Spells;
using Confluence.Sim.World;

namespace Confluence.Sim.Content.Augments.AugmentsA
{
    /// <summary>
    /// Ground-plane geometry of gameplay fields for the Fieldcraft records (spec L128): footprints of live fields (spell
    /// fields exactly, other field entities such as player clouds by their registry circle), the footprint a field cast
    /// would create at its predicted placement, edge distances, overlaps, and overhead cover.
    /// </summary>
    internal static class FieldGeometry
    {
        /// <summary>Sample spacing of the overlap test between two non-circular footprints (m).</summary>
        public const float OverlapSampleStep = 0.2f;

        /// <summary>How far above a field's center a solid counts as overhead cover / a ceiling (m).</summary>
        public const float OverheadProbe = 12f;

        /// <summary>The live gameplay fields (owned entities of kind Field) with their footprints, in registry order.</summary>
        public static List<KeyValuePair<OwnedEntity, Footprint>> LiveFields(DuelWorld world, int owner = -1)
        {
            var result = new List<KeyValuePair<OwnedEntity, Footprint>>();
            IReadOnlyList<OwnedEntity> all = world.Owned.Entities;
            for (int i = 0; i < all.Count; i++)
            {
                OwnedEntity e = all[i];
                if (!e.Active || e.Kind != OwnedEntityKind.Field || (owner >= 0 && e.Owner != owner)) continue;
                result.Add(new KeyValuePair<OwnedEntity, Footprint>(e, FootprintOf(e)));
            }

            return result;
        }

        public static Footprint FootprintOf(OwnedEntity e) =>
            e.Behavior is FieldEntity f ? f.Footprint : Footprint.Circle(e.Position.XZ(), e.Radius);

        /// <summary>
        /// The footprint and center a field cast of this form would create now: the delivery's own placement rule
        /// (<see cref="DeliverySpec.PredictTarget"/>) along the caster's current aim, built by the delivery's own footprint rule
        /// (<see cref="IFieldDelivery.FootprintAt"/>; the framework field builds it as <see cref="FieldDelivery.Spawn"/>
        /// does). False when the form is not a field delivery or the spell runtime is absent.
        /// </summary>
        public static bool TryPredict(DuelWorld world, SpellForm form, int caster, out Footprint footprint, out Vec3 center) =>
            TryPredict(world, form, caster, out footprint, out center, out _);

        /// <summary>As above, with the predicted placement point (<paramref name="ground"/>: a circle's center, a lane's start).</summary>
        public static bool TryPredict(DuelWorld world, SpellForm form, int caster, out Footprint footprint, out Vec3 center, out Vec3 ground)
        {
            footprint = default;
            center = Vec3.Zero;
            ground = Vec3.Zero;
            SpellRuntime runtime = AugmentsAKit.Runtime(world);
            if (runtime == null || !(form?.Delivery is IFieldDelivery field)) return false;
            SpellContext ctx = runtime.Context;
            ground = form.Delivery.PredictTarget(ctx, form, caster);
            field.FootprintAt(ground, ctx.AimDirection(caster), out footprint, out center); // the delivery's own footprint rule
            return true;
        }

        /// <summary>Signed distance from a point to a footprint's edge: negative inside, positive outside.</summary>
        public static float SignedEdgeDistance(in Footprint f, Vec2 p)
        {
            switch (f.Shape)
            {
                case FootprintShape.Circle:
                case FootprintShape.Cone:
                    return Vec2.Distance(p, f.A) - f.Radius;
                case FootprintShape.Segment:
                    return Vec2.Distance(p, Intersections.ClosestPointOnSegment(p, f.A, f.B)) - f.Radius;
                case FootprintShape.Rectangle:
                {
                    Vec2 local = (p - f.A).RotatedByYaw(-f.YawDeg);
                    float dx = Math.Abs(local.X) - f.HalfSize.X;
                    float dy = Math.Abs(local.Y) - f.HalfSize.Y;
                    float ox = Math.Max(dx, 0f);
                    float oy = Math.Max(dy, 0f);
                    float outside = (float)Math.Sqrt(ox * ox + oy * oy);
                    return outside > 0f ? outside : Math.Max(dx, dy);
                }
                default:
                    return Vec2.Distance(p, f.A);
            }
        }

        /// <summary>
        /// Whether two footprints share ground. Exact when either is a circle (distance from its center to the other
        /// convex shape); otherwise a deterministic sample of the first shape's bounds at <see cref="OverlapSampleStep"/>.
        /// </summary>
        public static bool Overlaps(in Footprint a, in Footprint b)
        {
            if (a.Shape == FootprintShape.Circle) return SignedEdgeDistance(b, a.A) < a.Radius;
            if (b.Shape == FootprintShape.Circle) return SignedEdgeDistance(a, b.A) < b.Radius;
            a.GetBounds(out Vec2 min, out Vec2 max);
            for (float x = min.X; x <= max.X + 1e-4f; x += OverlapSampleStep)
            {
                for (float y = min.Y; y <= max.Y + 1e-4f; y += OverlapSampleStep)
                {
                    var p = new Vec2(x, y);
                    if (a.Contains(p) && b.Contains(p)) return true;
                }
            }

            return false;
        }

        /// <summary>Whether a solid lies directly above a ground point (overhead cover, a ceiling).</summary>
        public static bool UnderCover(CollisionWorld collision, Vec3 ground) =>
            collision.Raycast(ground + new Vec3(0f, 0.1f, 0f), Vec3.Up, OverheadProbe, SolidMask.Movement, out _);
    }
}
