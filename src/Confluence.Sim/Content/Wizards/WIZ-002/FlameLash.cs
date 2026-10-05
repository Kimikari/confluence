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
    /// <item>The curved whip is a swept strike (spec L102 "a swept strike volume matching their visible movement"): the
    /// 0.5 m-wide lash sweeps from the focus across an arc of <see cref="ArcHalfDeg"/> either side of the aim fixed at
    /// commitment ("its fixed arc"), out to the 6 m reach; its tip traces the visible curved arc. The record states no
    /// angle, so the whip uses the common light-blade sweep (35 degrees either side, spec L100 "weapons use the
    /// light-blade profile" unless a category is specified; <see cref="SimRules.Melee.LightBladeArcHalfDeg"/>). Each
    /// sample line stops at the first solid (no hits through walls); each target is struck once. It is an offensive
    /// melee spell, so a frontal guard reduces it (spec L104, D-135); the direct hit adds Burn 4/s for 3 s.</item>
    /// <item>The ground under the tip's curved arc (0.5 m wide) receives Heat 1 as instant residue, once per cell. Growth
    /// ignites at two heat units (spec L153, a global rule a record cannot loosen), so the lash ignites dry growth —
    /// growth already holding a heat unit, such as sun-dried beds — and dries or steams what it touches by the usual
    /// reactions.</item>
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

        /// <summary>Half of the lash's swept angle (unstated in the record: the light-blade sweep, spec L100, L102).</summary>
        public const float ArcHalfDeg = SimRules.Melee.LightBladeArcHalfDeg;

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Width, Damage, BurnDps, BurnDuration, ArcHeat };

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Build(SpellFormBuilder form)
        {
            form.AimLock = AimLock.AtCommit; // "its fixed arc"
            PayloadSpec payload = form.Payload;
            payload.Direct = Damage.Float;
            payload.AddStatus(StatusKind.Burn, BurnDuration.Float, BurnDps.Float, StatusTrigger.DirectHitOnly);
            form.Delivery = new LashDelivery { Reach = form.Range, Width = Width.Float, ArcHalfDeg = ArcHalfDeg, GroundHeat = (int)ArcHeat.Value };
        }
    }

    /// <summary>The Flame Lash strike and its AUG-317 knot and AUG-318 steam strip.</summary>
    public sealed class LashDelivery : DeliverySpec
    {
        private float _reach;
        private float _width;
        private float _arcHalfDeg;
        private int _groundHeat;
        private float _knotLifetimeSec;
        private float _knotHealth;
        private PayloadSpec _knotSwing;
        private float _steamWidth;
        private float _steamLifetimeSec;

        public override DeliveryKind Kind => DeliveryKind.Arc;

        public float Reach { get => _reach; set => _reach = Set(value); }
        public float Width { get => _width; set => _width = Set(value); }

        /// <summary>Half of the swept angle around the aim (the knot's swing sweeps the same arc).</summary>
        public float ArcHalfDeg { get => _arcHalfDeg; set => _arcHalfDeg = Set(value); }

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
            _arcHalfDeg = CMath.Clamp(_arcHalfDeg, 0f, 180f);
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
            LashSweep sweep = Strike(ctx, cast, cast.Form.Payload, origin, dir, false);

            DuelPlayer p = ctx.Player(caster);
            HeatTrack(ctx, cast, sweep, p.Position.Y);

            if (HasKnot)
            {
                // AUG-317 "Hit surface leaves 20-health knot": where the whip meets a wall (the struck sample nearest the
                // aim), else where its tip lands on the ground at the middle of the arc.
                int wall = sweep.WallNearestAim();
                Vec3 at = wall >= 0 ? sweep.Tips[wall] : sweep.Ground[sweep.Middle] + new Vec3(0f, KnotEntity.Radius, 0f);
                KnotEntity knot = ctx.Spawn(new KnotEntity(cast, this, at, Ticks.FromSeconds(KnotLifetimeSec)));
                ctx.Emit(SpellEventKind.DeployablePlaced, cast, at, dir, knot.Id, KnotHealth, ticks: Ticks.FromSeconds(KnotLifetimeSec));
                ctx.Emit(SpellEventKind.Marker, cast, at, KitSupport.Flat(dir, Vec3.Forward), knot.Id, Reach); // its marked arc
            }
        }

        /// <summary>
        /// One lash: the whip's swept volume of the lash's width from the origin across the arc around the aim (sample
        /// lines from the leftmost to the rightmost edge, each cut at the first solid). Every target whose hit volume a
        /// sample touches is struck once with the payload's direct hit, every hostile body once, every struck solid once.
        /// <paramref name="secondary"/> marks the knot's recast swing as new secondary damage (spec L508).
        /// </summary>
        internal LashSweep Strike(SpellContext ctx, CastRecord cast, PayloadSpec payload, Vec3 origin, Vec3 aim, bool secondary)
        {
            float yaw = CMath.YawOf(aim);
            float pitch = CMath.PitchOf(aim);
            int samples = ArcHalfDeg > 0f ? SpellRules.ArcSamples : 1;
            var sweep = new LashSweep(samples);
            var struck = new bool[DuelWorld.PlayerCount];
            var struckBodies = new List<ISpellBody>();
            var struckSolids = new List<int>();
            float halfWidth = Width * 0.5f;
            IReadOnlyList<ISpellBody> bodies = ctx.HostileBodies(cast.Caster);
            for (int s = 0; s < samples; s++)
            {
                float u = samples > 1 ? s / (float)(samples - 1) : 0.5f;
                float offset = -ArcHalfDeg + 2f * ArcHalfDeg * u;
                Vec3 dir = CMath.ForwardFromYawPitch(yaw + offset, pitch);
                float length = Reach;
                if (ctx.Collision.SegmentCast(origin, origin + dir * Reach, SolidMask.Movement, out WorldHit hit))
                {
                    length = hit.Distance;
                    sweep.Solids[s] = hit.SolidId;
                }

                Vec3 tip = origin + dir * length;
                sweep.Tips[s] = tip;
                sweep.Ground[s] = KitSupport.GroundBelow(ctx, tip - dir * 0.05f);
                sweep.Offsets[s] = offset;

                for (int i = 0; i < DuelWorld.PlayerCount; i++)
                {
                    if (i == cast.Caster || struck[i] || !ctx.IsTargetable(i)) continue;
                    Capsule body = ctx.TargetVolume(cast.Caster, i, true);
                    float d2 = Intersections.SegmentSegmentClosest(origin, tip, body.A, body.B, out _, out _, out _, out _);
                    float r = body.Radius + halfWidth;
                    if (d2 > r * r) continue;
                    struck[i] = true; // one strike damages its target once (spec L102)
                    PayloadHit direct = PayloadHit.Direct(origin, dir);
                    direct.Secondary = secondary;
                    ctx.ApplyToPlayer(cast, payload, i, direct);
                }

                for (int i = 0; i < bodies.Count; i++)
                {
                    ISpellBody b = bodies[i];
                    if (struckBodies.Contains(b) || !b.IsIntact) continue;
                    Vec3 closest = Intersections.ClosestPointOnSegment(b.BodyCenter, origin, tip);
                    if (Vec3.Distance(closest, b.BodyCenter) > b.BodyRadius + halfWidth) continue;
                    struckBodies.Add(b);
                    ctx.ApplyToBody(cast, payload, b, CastHitKinds.Direct);
                }

                if (sweep.Solids[s] >= 0 && !struckSolids.Contains(sweep.Solids[s]))
                {
                    struckSolids.Add(sweep.Solids[s]);
                    ctx.DamageSolid(cast, payload, sweep.Solids[s]);
                }
            }

            return sweep;
        }

        /// <summary>
        /// The ground under the tip's curved arc receives the lash's heat (instant residue, one deposit per covered cell so
        /// overlapping pieces never add a second unit). With AUG-318, each covered cell holding liquid water instead loses
        /// one unit ("consumes it") and each wet run of the arc becomes a steam strip; the strip uses the lash's own heat
        /// ("no extra damage or strength"), so those cells receive no further heat.
        /// </summary>
        private void HeatTrack(SpellContext ctx, CastRecord cast, LashSweep sweep, float y)
        {
            var path = new Vec2[sweep.Count];
            for (int i = 0; i < path.Length; i++) path[i] = sweep.Ground[i].XZ();
            ReactionWorld world = KitSupport.Reactions(ctx);
            if (world == null)
            {
                for (int i = 0; i < path.Length; i++)
                {
                    Footprint piece = Piece(path, i);
                    if (i + 1 < path.Length || path.Length == 1) KitSupport.Deposit(ctx, cast, DepositKind.Heat, GroundHeat, new Vec3(path[i].X, y, path[i].Y), piece);
                }

                return;
            }

            // The arc's covered cells, each once, in index order.
            var cells = new List<int>();
            var scratch = new List<int>();
            for (int i = 0; i < path.Length; i++)
            {
                if (i + 1 >= path.Length && path.Length > 1) break;
                scratch.Clear();
                world.Grid.Rasterize(Piece(path, i), scratch);
                for (int c = 0; c < scratch.Count; c++)
                {
                    if (!cells.Contains(scratch[c])) cells.Add(scratch[c]);
                }
            }

            cells.Sort();
            bool steams = SteamWidth > 0f;
            var wet = new List<int>();
            for (int c = 0; c < cells.Count; c++)
            {
                Vec2 center = world.Grid.CellCenter(cells[c]);
                if (steams && world.IsWetCell(cells[c]))
                {
                    wet.Add(cells[c]);
                    continue;
                }

                KitSupport.Deposit(ctx, cast, DepositKind.Heat, GroundHeat, new Vec3(center.X, y, center.Y), Footprint.Point(center));
            }

            if (wet.Count == 0) return;
            ReactionMaterials materials = KitSupport.Materials(ctx);
            materials.RecordSource(cast.OriginEventId, cast.SpellId);
            for (int c = 0; c < wet.Count; c++)
            {
                materials.RemoveUnits(new UnitRemoval
                {
                    Footprint = Footprint.Point(world.Grid.CellCenter(wet[c])),
                    Materials = new[] { Material.Water },
                    MaxPerCell = 1, // "consumes it": the Water 1 the lash crossed
                    Owner = cast.Caster,
                    SourceEventId = cast.OriginEventId,
                });
            }

            // One steam strip per wet run along the arc (walked in half-cell steps).
            List<KeyValuePair<Vec2, Vec2>> runs = WetRuns(world, path, y);
            for (int i = 0; i < runs.Count; i++)
            {
                Vec2 ra = runs[i].Key;
                Vec2 rb = runs[i].Value;
                materials.CreateVolume(new VolumeSpec
                {
                    Kind = VolumeKind.Steam,
                    Footprint = Footprint.Segment(ra, rb, SteamWidth),
                    BaseY = y,
                    LifetimeTicks = Ticks.FromSeconds(SteamLifetimeSec),
                    Owner = cast.Caster,
                    SourceEventId = cast.OriginEventId,
                    SourceWater = 1,
                });
                ctx.Emit(SpellEventKind.Steamed, cast, new Vec3(ra.X, y, ra.Y), new Vec3(rb.X - ra.X, 0f, rb.Y - ra.Y), 0, SteamWidth,
                    ticks: Ticks.FromSeconds(SteamLifetimeSec));
            }
        }

        // The arc's ground piece from tip i to tip i + 1 (a single sample: a disc of the lash's width).
        private Footprint Piece(Vec2[] path, int i) =>
            i + 1 < path.Length ? Footprint.Segment(path[i], path[i + 1], Width) : Footprint.Circle(path[i], Width * 0.5f);

        // Start and end points of every stretch of the path over liquid water.
        private static List<KeyValuePair<Vec2, Vec2>> WetRuns(ReactionWorld world, Vec2[] path, float y)
        {
            var runs = new List<KeyValuePair<Vec2, Vec2>>();
            bool inRun = false;
            Vec2 runStart = default;
            Vec2 last = path[0];
            float step = Core.Rules.GlobalRules.World.CellSize * 0.5f;
            for (int i = 0; i < path.Length; i++)
            {
                Vec2 a = path[i];
                Vec2 b = i + 1 < path.Length ? path[i + 1] : path[i];
                int steps = i + 1 < path.Length ? Math.Max(1, (int)Math.Ceiling(Vec2.Distance(a, b) / step)) : 0;
                for (int k = 0; k <= steps; k++)
                {
                    if (k == steps && i + 1 < path.Length) break; // the piece's end is the next piece's start
                    Vec2 point = steps > 0 ? Vec2.Lerp(a, b, k / (float)steps) : a;
                    bool isWet = world.IsWetAt(new Vec3(point.X, y, point.Y));
                    if (isWet && !inRun)
                    {
                        inRun = true;
                        runStart = point;
                    }
                    else if (!isWet && inRun)
                    {
                        inRun = false;
                        runs.Add(new KeyValuePair<Vec2, Vec2>(runStart, last));
                    }

                    last = point;
                }
            }

            if (inRun) runs.Add(new KeyValuePair<Vec2, Vec2>(runStart, last));
            return runs;
        }
    }

    /// <summary>One lash's sample lines: tips, the ground under each tip, struck solids (-1 = none), angular offsets.</summary>
    internal sealed class LashSweep
    {
        public LashSweep(int count)
        {
            Tips = new Vec3[count];
            Ground = new Vec3[count];
            Solids = new int[count];
            Offsets = new float[count];
            for (int i = 0; i < count; i++) Solids[i] = -1;
        }

        public Vec3[] Tips { get; }
        public Vec3[] Ground { get; }
        public int[] Solids { get; }
        public float[] Offsets { get; }
        public int Count => Tips.Length;
        public int Middle => Tips.Length / 2;

        /// <summary>The sample stopped by a solid closest to the aim (ties: the earlier sample), or -1.</summary>
        public int WallNearestAim()
        {
            int best = -1;
            for (int i = 0; i < Solids.Length; i++)
            {
                if (Solids[i] < 0) continue;
                if (best < 0 || Math.Abs(Offsets[i]) < Math.Abs(Offsets[best])) best = i;
            }

            return best;
        }
    }

    /// <summary>
    /// AUG-317 Lash Knot's "20-health knot": a destructible anchor (spec L512, L527) that swings the lash once more on
    /// the spell recast, sweeping the lash's arc around the caster's current ("manual") aim. Breaking it cancels the
    /// swing. The swing is new secondary damage (spec L508).
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
            // The knot's swing is new secondary damage of the transformation (spec L508): generation 1, so it cannot
            // trigger direct-hit, resource, lifesteal or proc rewards.
            _spec.Strike(ctx, Cast, _spec.KnotSwing, origin, dir, true);
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
