using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Sim.Spells;
using Confluence.Sim.World;

namespace Confluence.Sim.Content.Wizards.HalenTide
{
    /// <summary>
    /// SPL-076 Water Pool (WIZ-010, reaction setup): "Place a connected 3 m-radius shallow pool for 6 s; entrants are Wet; no
    /// automatic slow." World interaction "Water 2; Heat creates steam; Frost freezes it; electricity stays within its
    /// connected footprint." Mana 14, cooldown 8 s, 0.4 s windup and the 18 m placement range from the record (spec L525).
    /// <list type="bullet">
    /// <item>One gameplay field (spec L128): a 3 m circle on the ground at the aimed point, neutral (it affects its owner
    /// too), lasting 6 s; its creation deposits Water 2 in every covered cell for the field's lifetime, owned by the field
    /// so a dispel can consume it (spec L161, L163).</item>
    /// <item>The pool's cells are one 4-connected disc, so the reaction world's own rules give the record's interactions:
    /// heat on it makes steam (L144), two frost freeze it (L145), and a charge spreads only through its connected wet
    /// cells, at most 12 cells within 6 m (L148).</item>
    /// <item>"entrants are Wet": a body standing in the pool's liquid water is Wet (spec L118, D-150); a frozen pool is ice and
    /// wets nobody. The pool applies no slow ("no automatic slow"; wet stone has no movement penalty, L175).</item>
    /// </list>
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class WaterPool : ISpellBehavior
    {
        public const string Id = "SPL-076";

        public static readonly ProseParameter Radius = ProseParameter.Of("radius", 3, "connected 3 m-radius shallow pool");
        public static readonly ProseParameter Lifetime = ProseParameter.Of("lifetime", 6, "pool for 6 s");
        public static readonly ProseParameter Water = ProseParameter.Of("water", 2, "Water 2");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Radius, Lifetime, Water };

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Build(SpellFormBuilder form)
        {
            form.Delivery = new FieldDelivery
            {
                Shape = FieldShape.Circle,
                Radius = Radius.Float,
                Placement = PlacementMode.AtAimPoint,
                LifetimeSec = Lifetime.Float,
                AffectsOwner = true,
            };

            form.Payload.AddDeposit(DepositKind.Water, (int)Water.Value, DepositPlacement.Footprint);
        }
    }

    /// <summary>
    /// AUG-440 Pool Divide: two circular pools side by side across the aim, centered on the aimed point with a dry gap
    /// between their edges, each its own gameplay field (spec L510: a new field consumes the normal allocation), each with
    /// the form's footprint deposit.
    /// </summary>
    public sealed class TwinPoolDelivery : DeliverySpec, IFieldDelivery
    {
        private float _radius;
        private float _gap;
        private float _lifetimeSec;
        private FieldDelivery _pool;

        public override DeliveryKind Kind => DeliveryKind.Field;

        public float Radius { get => _radius; set => _radius = Set(value); }

        /// <summary>Dry gap between the two pools' edges (AUG-440 "separated 2-4 m").</summary>
        public float Gap { get => _gap; set => _gap = Set(value); }

        public float LifetimeSec { get => _lifetimeSec; set => _lifetimeSec = Set(value); }

        /// <summary>The field one pool is (built at resolution).</summary>
        public FieldDelivery Pool => _pool;

        // The shared field shape (Spells/Deliveries/FieldShapes.cs): two non-damaging circles across the aim; the Fieldcraft
        // records see the first (left) pool, the field their settlement finds first.
        FieldShape IFieldDelivery.Shape => FieldShape.Circle;

        float IFieldDelivery.Width { get => Radius * 2f; set => Radius = value * 0.5f; }

        float IFieldDelivery.Length => Radius * 2f;

        PlacementMode IFieldDelivery.Placement => PlacementMode.AtAimPoint;

        bool IFieldDelivery.IsDamaging => false;

        PayloadSpec IFieldDelivery.EnterPayload => null;

        void IFieldDelivery.FootprintAt(Vec3 ground, Vec3 aim, out Confluence.Reactions.Grid.Footprint footprint, out Vec3 center)
        {
            Vec3 flat = new Vec3(aim.X, 0f, aim.Z).NormalizedOr(Vec3.Forward);
            center = ground - CMath.RightFromYaw(CMath.YawOf(flat)) * (Radius + Gap * 0.5f); // the left pool (Centers)
            footprint = Confluence.Reactions.Grid.Footprint.Circle(center.XZ(), Radius);
        }

        internal override void ApplyGlobalCaps()
        {
            _pool = new FieldDelivery
            {
                Shape = FieldShape.Circle,
                Radius = _radius,
                Placement = PlacementMode.AtAimPoint,
                LifetimeSec = _lifetimeSec,
                AffectsOwner = true,
            };
            _pool.ApplyGlobalCaps();
        }

        protected override void OnFreeze() => _pool?.Freeze();

        public override Vec3 PredictTarget(SpellContext ctx, SpellForm form, int caster) => ctx.GroundPoint(caster, form.Range);

        /// <summary>The two pool centers for an aimed ground point and aim direction.</summary>
        public void Centers(SpellContext ctx, Vec3 aimed, Vec3 aim, out Vec3 left, out Vec3 right)
        {
            Vec3 flat = new Vec3(aim.X, 0f, aim.Z).NormalizedOr(Vec3.Forward);
            Vec3 across = CMath.RightFromYaw(CMath.YawOf(flat));
            float offset = Radius + Gap * 0.5f;
            right = HalenRules.Ground(ctx.Collision, aimed + across * offset + new Vec3(0f, 1f, 0f));
            left = HalenRules.Ground(ctx.Collision, aimed - across * offset + new Vec3(0f, 1f, 0f));
        }

        public override void Release(SpellContext ctx, CastRecord cast)
        {
            Vec3 aimed = PredictTarget(ctx, cast.Form, cast.Caster);
            Vec3 aim = AimDirection(ctx, cast);
            ctx.World.Effects.BindFieldPlacement(cast, ref aimed, ref aim); // a placement priced at commitment (Effects/EffectHooks.cs)
            Centers(ctx, aimed, aim, out Vec3 left, out Vec3 right);
            Vec3 flat = new Vec3(aim.X, 0f, aim.Z);
            _pool.Spawn(ctx, cast, left, flat);
            _pool.Spawn(ctx, cast, right, flat);
        }
    }
}
