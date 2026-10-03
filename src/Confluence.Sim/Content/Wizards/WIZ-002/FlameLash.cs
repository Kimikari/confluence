using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Time;
using Confluence.Reactions;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Duel;
using Confluence.Sim.Hashing;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Status;
using Confluence.Sim.World;
using Confluence.Sim.World.Materials;

namespace Confluence.Sim.Content.Wizards.Wiz002
{
    /// <summary>
    /// SPL-015 Flame Lash (WIZ-002, close disarm pressure): "A visible curved whip strikes along a 0.5 m-wide arc; hit
    /// adds Burn 4/s for 3 s." Damage "42", thermal. World interaction: "Heat 1 ignites dry growth touched by the arc."
    /// Counterplay: "Parry the lash or retreat before its fixed arc resolves." Mana 18, cooldown 7 s, 0.3 s windup, range
    /// 6 (the lash's reach) from the record (spec L525).
    /// <list type="bullet">
    /// <item>The lash is a 0.5 m-wide strike volume from the focus along the aim fixed at commitment ("its fixed arc"),
    /// stopped by solid geometry (spec L102 no tracking, no hits through walls); each target is struck once. It is an
    /// offensive melee spell, so a frontal guard reduces it (spec L104, D-135); the direct hit adds Burn 4/s for 3 s.</item>
    /// <item>Its ground track (the whip's landing line, 0.5 m wide) receives Heat 1 as instant residue. Growth ignites at
    /// two heat units (spec L153, a global rule a record cannot loosen), so the lash ignites dry growth — growth already
    /// holding a heat unit, such as sun-dried beds — and dries or steams what it touches by the usual reactions.</item>
    /// </list>
    /// </summary>
    [ContentBehavior("SPL-015")]
    public sealed class FlameLash : ISpellBehavior
    {
        public static readonly ProseParameter Width = ProseParameter.Of("lash_width", 0.5, "strikes along a 0.5 m-wide arc");
        public static readonly ProseParameter Damage = ProseParameter.Of("direct", 42, "42");
        public static readonly ProseParameter BurnDps = ProseParameter.Of("burn_dps", 4, "Burn 4/s");
        public static readonly ProseParameter BurnDuration = ProseParameter.Of("burn_duration", 3, "Burn 4/s for 3 s");
        public static readonly ProseParameter ArcHeat = ProseParameter.Of("arc_heat", 1, "Heat 1 ignites dry growth touched by the arc");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Width, Damage, BurnDps, BurnDuration, ArcHeat };

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Build(SpellFormBuilder form)
        {
            form.AimLock = AimLock.AtCommit; // "its fixed arc"
            PayloadSpec payload = form.Payload;
            payload.Direct = Damage.Float;
            payload.AddStatus(StatusKind.Burn, BurnDuration.Float, BurnDps.Float, StatusTrigger.DirectHitOnly);
            form.Delivery = new LashDelivery { Reach = form.Range, Width = Width.Float, GroundHeat = (int)ArcHeat.Value };
        }
    }

    /// <summary>The Flame Lash strike and its AUG-317 knot and AUG-318 steam strip.</summary>
    public sealed class LashDelivery : DeliverySpec
    {
        private float _reach;
        private float _width;
        private int _groundHeat;
        private float _knotLifetimeSec;
        private float _knotHealth;
        private PayloadSpec _knotSwing;
        private float _steamWidth;
        private float _steamLifetimeSec;

        public override DeliveryKind Kind => DeliveryKind.Arc;

        public float Reach { get => _reach; set => _reach = Set(value); }
        public float Width { get => _width; set => _width = Set(value); }

        /// <summary>Heat units the lash's ground track receives (instant residue).</summary>
        public int GroundHeat { get => _groundHeat; set => _groundHeat = Set(value); }

        /// <summary>AUG-317: the knot left where the lash hits a surface (0 = none).</summary>
        public float KnotLifetimeSec { get => _knotLifetimeSec; set => _knotLifetimeSec = Set(value); }

        public float KnotHealth { get => _knotHealth; set => _knotHealth = Set(value); }

        /// <summary>The knot's recast swing (its Direct amount), a separate payload of the same cast.</summary>
        public PayloadSpec KnotSwing { get => _knotSwing; set => _knotSwing = Set(value); }

        /// <summary>AUG-318: width of the steam strip where the lash crosses water (0 = none).</summary>
        public float SteamWidth { get => _steamWidth; set => _steamWidth = Set(value); }

        public float SteamLifetimeSec { get => _steamLifetimeSec; set => _steamLifetimeSec = Set(value); }

        public bool HasKnot => KnotLifetimeSec > 0f;

        internal override void ApplyGlobalCaps()
        {
            _width = Math.Max(0f, _width);
            _knotLifetimeSec = Math.Min(_knotLifetimeSec, Core.Rules.GlobalRules.FieldsAndSummons.MaxLifetimeSec);
            CapNested(_knotSwing);
        }

        protected override void OnFreeze() => _knotSwing?.Freeze();

        public override Vec3 PredictTarget(SpellContext ctx, SpellForm form, int caster) =>
            ctx.FocusOrigin(caster) + ctx.AimDirection(caster) * Reach;

        public override void Release(SpellContext ctx, CastRecord cast)
        {
            int caster = cast.Caster;
            Vec3 origin = ctx.FocusOrigin(caster);
            Vec3 dir = AimDirection(ctx, cast);
            ctx.Emit(SpellEventKind.ArcReleased, cast, origin, dir, 0, Reach);
            bool wall = Strike(ctx, cast, cast.Form.Payload, origin, dir, Reach, out Vec3 tip, out int solid);

            DuelPlayer p = ctx.Player(caster);
            Vec3 feet = p.Position;
            Vec3 tipGround = wall ? KitSupport.GroundBelow(ctx, tip) : KitSupport.GroundBelow(ctx, tip);
            HeatTrack(ctx, cast, feet, new Vec3(tipGround.X, feet.Y, tipGround.Z));

            if (HasKnot)
            {
                // AUG-317 "Hit surface leaves 20-health knot": where the whip's tip meets a wall, else where it lands.
                Vec3 at = wall ? tip : tipGround + new Vec3(0f, KnotEntity.Radius, 0f);
                KnotEntity knot = ctx.Spawn(new KnotEntity(cast, this, at, Ticks.FromSeconds(KnotLifetimeSec)));
                ctx.Emit(SpellEventKind.DeployablePlaced, cast, at, dir, knot.Id, KnotHealth, ticks: Ticks.FromSeconds(KnotLifetimeSec));
                ctx.Emit(SpellEventKind.Marker, cast, at, KitSupport.Flat(dir, Vec3.Forward), knot.Id, Reach); // its marked arc
            }
        }

        /// <summary>
        /// One lash line: a strike volume of the lash's width from the origin along the direction, cut at the first solid;
        /// every target whose hit volume it touches is struck once with the payload's direct hit, every hostile body it
        /// touches takes the payload. Returns whether a solid stopped it; <paramref name="tip"/> is the line's end.
        /// </summary>
        internal bool Strike(SpellContext ctx, CastRecord cast, PayloadSpec payload, Vec3 origin, Vec3 dir, float reach, out Vec3 tip, out int solid)
        {
            float length = reach;
            solid = -1;
            bool wall = ctx.Collision.SegmentCast(origin, origin + dir * reach, SolidMask.Movement, out WorldHit hit);
            if (wall)
            {
                length = hit.Distance;
                solid = hit.SolidId;
            }

            tip = origin + dir * length;
            float halfWidth = Width * 0.5f;
            for (int i = 0; i < DuelWorld.PlayerCount; i++)
            {
                if (i == cast.Caster || !ctx.IsTargetable(i)) continue;
                Capsule body = ctx.TargetVolume(cast.Caster, i, true);
                float d2 = Intersections.SegmentSegmentClosest(origin, tip, body.A, body.B, out _, out _, out _, out _);
                float r = body.Radius + halfWidth;
                if (d2 > r * r) continue;
                ctx.ApplyToPlayer(cast, payload, i, PayloadHit.Direct(origin, dir)); // one strike damages its target once
            }

            IReadOnlyList<ISpellBody> bodies = ctx.HostileBodies(cast.Caster);
            for (int i = 0; i < bodies.Count; i++)
            {
                ISpellBody b = bodies[i];
                Vec3 closest = Intersections.ClosestPointOnSegment(b.BodyCenter, origin, tip);
                if (Vec3.Distance(closest, b.BodyCenter) <= b.BodyRadius + halfWidth) ctx.ApplyToBody(cast, payload, b, CastHitKinds.Direct);
            }

            if (wall) ctx.DamageSolid(cast, payload, solid);
            return wall;
        }

        /// <summary>
        /// The lash's ground track receives its heat (instant residue). With AUG-318, each run of the track over liquid
        /// water instead consumes that water (one unit per crossed cell) into a steam strip; the strip uses the lash's
        /// own heat ("no extra damage or strength"), so those cells receive no further heat.
        /// </summary>
        private void HeatTrack(SpellContext ctx, CastRecord cast, Vec3 from, Vec3 to)
        {
            Vec2 a = from.XZ();
            Vec2 b = to.XZ();
            if (Vec2.Distance(a, b) < 0.05f) return;
            ReactionWorld world = KitSupport.Reactions(ctx);
            if (SteamWidth <= 0f || world == null)
            {
                KitSupport.Deposit(ctx, cast, DepositKind.Heat, GroundHeat, from, Footprint.Segment(a, b, Width));
                return;
            }

            // Walk the track in half-cell steps and split it into wet and dry runs.
            float length = Vec2.Distance(a, b);
            int steps = Math.Max(1, (int)Math.Ceiling(length / (Core.Rules.GlobalRules.World.CellSize * 0.5f)));
            var runs = new List<KeyValuePair<float, float>>();
            var dry = new List<KeyValuePair<float, float>>();
            float runStart = -1f;
            float dryStart = 0f;
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)steps;
                Vec2 point = Vec2.Lerp(a, b, t);
                bool wet = world.IsWetAt(new Vec3(point.X, from.Y, point.Y));
                if (wet && runStart < 0f)
                {
                    runStart = t;
                    if (t > dryStart) dry.Add(new KeyValuePair<float, float>(dryStart, t));
                }
                else if (!wet && runStart >= 0f)
                {
                    runs.Add(new KeyValuePair<float, float>(runStart, t));
                    runStart = -1f;
                    dryStart = t;
                }
            }

            if (runStart >= 0f) runs.Add(new KeyValuePair<float, float>(runStart, 1f));
            else if (dryStart < 1f) dry.Add(new KeyValuePair<float, float>(dryStart, 1f));

            for (int i = 0; i < dry.Count; i++)
            {
                Vec2 da = Vec2.Lerp(a, b, dry[i].Key);
                Vec2 db = Vec2.Lerp(a, b, dry[i].Value);
                KitSupport.Deposit(ctx, cast, DepositKind.Heat, GroundHeat, from, Footprint.Segment(da, db, Width));
            }

            ReactionMaterials materials = KitSupport.Materials(ctx);
            materials.RecordSource(cast.OriginEventId, cast.SpellId);
            for (int i = 0; i < runs.Count; i++)
            {
                Vec2 ra = Vec2.Lerp(a, b, runs[i].Key);
                Vec2 rb = Vec2.Lerp(a, b, runs[i].Value);
                Footprint crossed = Footprint.Segment(ra, rb, Width);
                materials.RemoveUnits(new UnitRemoval
                {
                    Footprint = crossed,
                    Materials = new[] { Material.Water },
                    MaxPerCell = 1, // "consumes it": the Water 1 the lash crossed
                    Owner = cast.Caster,
                    SourceEventId = cast.OriginEventId,
                });
                materials.CreateVolume(new VolumeSpec
                {
                    Kind = VolumeKind.Steam,
                    Footprint = Footprint.Segment(ra, rb, SteamWidth),
                    BaseY = from.Y,
                    LifetimeTicks = Ticks.FromSeconds(SteamLifetimeSec),
                    Owner = cast.Caster,
                    SourceEventId = cast.OriginEventId,
                    SourceWater = 1,
                });
                ctx.Emit(SpellEventKind.Steamed, cast, new Vec3(ra.X, from.Y, ra.Y), new Vec3(rb.X - ra.X, 0f, rb.Y - ra.Y), 0, SteamWidth,
                    ticks: Ticks.FromSeconds(SteamLifetimeSec));
            }
        }
    }

    /// <summary>
    /// AUG-317 Lash Knot's "20-health knot": a destructible anchor (spec L512, L527) that swings the lash once more on
    /// the spell recast, along the caster's current ("manual") aim. Breaking it cancels the swing.
    /// </summary>
    public sealed class KnotEntity : SpellEntity, ISpellBody
    {
        public const float Radius = 0.3f;

        private readonly LashDelivery _spec;
        private readonly int _lifetimeTicks;
        private float _health;
        private bool _swung;

        internal KnotEntity(CastRecord cast, LashDelivery spec, Vec3 position, int lifetimeTicks)
            : base(cast)
        {
            _spec = spec;
            Position = position;
            _health = spec.KnotHealth;
            _lifetimeTicks = lifetimeTicks;
        }

        public Vec3 Position { get; }
        public float Health => _health;
        public bool Swung => _swung;

        public override bool AcceptsRecast => true;

        public bool IsIntact => Alive && _health > 0f;
        public Vec3 BodyCenter => Position;
        public float BodyRadius => Radius;

        public bool TakeDamage(SpellContext ctx, float amount, int attacker)
        {
            if (!IsIntact || !(amount > 0f)) return false;
            _health -= amount;
            if (_health > 0f) return false;
            ctx.Emit(SpellEventKind.DeployableDestroyed, Cast, Position, default, Id, targetPlayer: attacker);
            End(ctx);
            return true;
        }

        protected internal override void Step(SpellContext ctx)
        {
            if (ctx.Tick >= SpawnTick + _lifetimeTicks)
            {
                ctx.Emit(SpellEventKind.DeployableExpired, Cast, Position, default, Id);
                End(ctx);
            }
        }

        protected internal override void OnRecast(SpellContext ctx)
        {
            if (!IsIntact || _swung) return;
            _swung = true;
            DuelPlayer p = ctx.Player(Owner);
            Vec3 dir = KitSupport.Flat(ctx.AimDirection(Owner), CMath.ForwardFromYaw(p.Motor.YawDeg));
            Vec3 ground = KitSupport.GroundBelow(ctx, Position);
            // A knot lying on the ground lifts the lash to waist height as it swings; one on a wall swings from there.
            Vec3 origin = Position.Y - ground.Y < 0.5f ? new Vec3(Position.X, ground.Y + 0.9f, Position.Z) : Position;
            ctx.Emit(SpellEventKind.ArcReleased, Cast, origin, dir, Id, _spec.Reach);
            _spec.Strike(ctx, Cast, _spec.KnotSwing, origin, dir, _spec.Reach, out _, out _);
            ctx.Emit(SpellEventKind.DeployableDetonated, Cast, Position, dir, Id);
            End(ctx);
        }

        protected override void HashState(ref StateHasher h)
        {
            h.Add(Position);
            h.Add(_health);
            h.Add(_swung);
        }
    }
}
