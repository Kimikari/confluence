using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions.Grid;
using Confluence.Sim.Hashing;
using Confluence.Sim.Movement;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;

namespace Confluence.Sim.Content.Wizards.Wiz002
{
    /// <summary>
    /// SPL-013 Phoenix Rush (WIZ-002, gap closer): "Dash up to 8 m along ground; stops at solid geometry; 1 m endpoint
    /// burst; no invulnerability." Damage "30", thermal. World interaction: "A 1 m-wide Heat 1 trail persists 3 s and
    /// deals 6/s to any entrant." Mana 28, cooldown 13 s, 0.35 s windup, range 8 from the record (spec L525).
    /// <list type="bullet">
    /// <item>The dash runs on the character motor (spec L124 dash cap, boundary clamp), so it stops at solid geometry and
    /// grants no invulnerability (spec L82). Its direction is fixed at commitment and shown as a path marker for the whole
    /// windup ("Track the telegraphed path", spec L96, L549).</item>
    /// <item>The endpoint burst is the record's damage: thermal area damage within 1 m of the arrival (the caster is not
    /// hit), never guarded (a burst is neither weapon nor projectile, spec L104), carrying the cast's one mastery
    /// result because it is the spell's primary payload (spec L480).</item>
    /// <item>The trail is one neutral field of the caster (tags field; spec L128, L496) along the travelled path behind
    /// the body, Heat 1 per covered cell for its 3 s, 6 thermal per second to anyone standing on a hot cell.</item>
    /// </list>
    /// </summary>
    [ContentBehavior("SPL-013")]
    public sealed class PhoenixRush : ISpellBehavior
    {
        public static readonly ProseParameter Distance = ProseParameter.Of("dash_distance", 8, "Dash up to 8 m along ground");
        public static readonly ProseParameter BurstRadius = ProseParameter.Of("endpoint_burst_radius", 1, "1 m endpoint burst");
        public static readonly ProseParameter Damage = ProseParameter.Of("endpoint_burst", 30, "30");
        public static readonly ProseParameter TrailWidth = ProseParameter.Of("trail_width", 1, "A 1 m-wide Heat 1 trail");
        public static readonly ProseParameter TrailHeat = ProseParameter.Of("trail_heat", 1, "Heat 1 trail");
        public static readonly ProseParameter TrailLifetime = ProseParameter.Of("trail_lifetime", 3, "persists 3 s");
        public static readonly ProseParameter TrailDps = ProseParameter.Of("trail_dps", 6, "deals 6/s to any entrant");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Distance, BurstRadius, Damage, TrailWidth, TrailHeat, TrailLifetime, TrailDps };

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Build(SpellFormBuilder form)
        {
            form.AimLock = AimLock.AtCommit; // the telegraphed path is fixed when the cast commits
            PayloadSpec payload = form.Payload;
            payload.Splash = Damage.Float;
            payload.SplashRadius = BurstRadius.Float;
            payload.GuardEligible = false;

            var trail = new PayloadSpec { Channel = form.Channel, Direct = TrailDps.Float, GuardEligible = false };
            form.Delivery = new RushDelivery
            {
                Distance = Math.Min(Distance.Float, form.Range),
                BurstRadius = BurstRadius.Float,
                TrailWidth = TrailWidth.Float,
                TrailHeat = (int)TrailHeat.Value,
                TrailLifetimeSec = TrailLifetime.Float,
                TrailContact = trail,
            };
            form.Script = RushScript.Instance;
        }
    }

    /// <summary>Phoenix Rush's commitment marker: the fixed dash path (start, direction, length) for the windup.</summary>
    internal sealed class RushScript : SpellScript
    {
        public static readonly RushScript Instance = new RushScript();

        public override void OnCommitted(SpellContext ctx, CastRecord cast)
        {
            if (!(cast.Form.Delivery is RushDelivery rush)) return;
            DuelPlayer p = ctx.Player(cast.Caster);
            Vec3 dir = KitSupport.Flat(cast.CommitDirection, CMath.ForwardFromYaw(p.Motor.YawDeg));
            ctx.Emit(SpellEventKind.Marker, cast, p.Position, dir, 0, Math.Min(rush.Distance, p.Stats.EffectiveDashDistance(rush.Distance)),
                ticks: cast.Form.Castable.WindupTicks);
        }
    }

    /// <summary>
    /// The Phoenix Rush dash and its transformations: the outbound dash with its endpoint burst and heated trail, the
    /// AUG-314 brake (a recast during the dash stops it early with its own burst) and the AUG-313 return (a recast after
    /// the dash runs back toward the intact departure ember along the ground).
    /// </summary>
    public sealed class RushDelivery : DeliverySpec
    {
        private float _distance;
        private float _burstRadius;
        private float _trailWidth;
        private int _trailHeat;
        private float _trailLifetimeSec;
        private PayloadSpec _trailContact;
        private float _returnDistance;
        private float _emberLifetimeSec;
        private float _emberHealth;
        private bool _brake;
        private float _brakeRadius;
        private PayloadSpec _brakeBurst;

        public override DeliveryKind Kind => DeliveryKind.Dash;

        /// <summary>Outbound dash distance, at most 8 m (spec L124).</summary>
        public float Distance { get => _distance; set => _distance = Set(value); }

        public float BurstRadius { get => _burstRadius; set => _burstRadius = Set(value); }
        public float TrailWidth { get => _trailWidth; set => _trailWidth = Set(value); }
        public int TrailHeat { get => _trailHeat; set => _trailHeat = Set(value); }
        public float TrailLifetimeSec { get => _trailLifetimeSec; set => _trailLifetimeSec = Set(value); }

        /// <summary>Trail contact: its Direct amount once per second to anyone on a hot trail cell.</summary>
        public PayloadSpec TrailContact { get => _trailContact; set => _trailContact = Set(value); }

        /// <summary>AUG-313: return distance after a recast (0 = no return).</summary>
        public float ReturnDistance { get => _returnDistance; set => _returnDistance = Set(value); }

        public float EmberLifetimeSec { get => _emberLifetimeSec; set => _emberLifetimeSec = Set(value); }
        public float EmberHealth { get => _emberHealth; set => _emberHealth = Set(value); }

        /// <summary>AUG-314: a recast during the dash brakes it.</summary>
        public bool Brake { get => _brake; set => _brake = Set(value); }

        public float BrakeRadius { get => _brakeRadius; set => _brakeRadius = Set(value); }

        /// <summary>The brake burst (its Splash amount), a separate payload of the same cast.</summary>
        public PayloadSpec BrakeBurst { get => _brakeBurst; set => _brakeBurst = Set(value); }

        public bool Returns => ReturnDistance > 0f;

        internal override void ApplyGlobalCaps()
        {
            _distance = Math.Min(Math.Max(0f, _distance), GlobalRules.Modifiers.MaxDashDistance);
            _returnDistance = Math.Min(Math.Max(0f, _returnDistance), GlobalRules.Modifiers.MaxDashDistance);
            _trailLifetimeSec = Math.Min(_trailLifetimeSec, GlobalRules.FieldsAndSummons.MaxLifetimeSec);
            if (!(_distance > 0f)) throw new InvalidOperationException("Phoenix Rush needs a positive dash distance.");
            CapNested(_trailContact);
            CapNested(_brakeBurst);
        }

        protected override void OnFreeze()
        {
            _trailContact?.Freeze();
            _brakeBurst?.Freeze();
        }

        public override Vec3 PredictTarget(SpellContext ctx, SpellForm form, int caster)
        {
            DuelPlayer p = ctx.Player(caster);
            return p.Position + KitSupport.Flat(ctx.AimDirection(caster), CMath.ForwardFromYaw(p.Motor.YawDeg)) * DistanceFor(p);
        }

        internal float DistanceFor(DuelPlayer p) => Math.Min(Distance, p.Stats.EffectiveDashDistance(Distance));

        internal static int DashTicks(float distance) => Math.Max(1, (int)Math.Ceiling(distance / SpellRules.DefaultDashSpeed * Ticks.PerSecond));

        public override void Release(SpellContext ctx, CastRecord cast)
        {
            DuelPlayer p = ctx.Player(cast.Caster);
            Vec3 dir = KitSupport.Flat(AimDirection(ctx, cast), CMath.ForwardFromYaw(p.Motor.YawDeg));
            float distance = DistanceFor(p);
            int ticks = DashTicks(distance);
            Vec3 start = p.Position;
            if (!ctx.World.TryDash(cast.Caster, dir, distance, ticks, cast.SpellId))
            {
                ctx.Emit(SpellEventKind.MovementRefused, cast, start, dir);
                return;
            }

            ctx.Emit(SpellEventKind.MovementStarted, cast, start, dir, 0, distance, ticks: ticks);
            EmberEntity ember = null;
            if (Returns)
            {
                // AUG-313: the departure ember stays where the dash began (spec L512 anchor).
                ember = ctx.Spawn(new EmberEntity(cast, start + new Vec3(0f, EmberEntity.Radius, 0f), EmberHealth, Ticks.FromSeconds(EmberLifetimeSec)));
                ctx.Emit(SpellEventKind.DeployablePlaced, cast, ember.Position, default, ember.Id, EmberHealth, ticks: Ticks.FromSeconds(EmberLifetimeSec));
            }

            ctx.Spawn(new RushEntity(cast, this, start, dir, ticks, ember));
        }
    }

    /// <summary>The live Phoenix Rush: outbound dash, endpoint or brake burst, trail, and the optional return.</summary>
    public sealed class RushEntity : SpellEntity
    {
        private readonly RushDelivery _spec;
        private readonly Vec3 _start;
        private readonly Vec3 _dir;
        private readonly int _maxTicks;
        private readonly EmberEntity _ember;
        private RushPhase _phase = RushPhase.Outbound;
        private int _ticks;
        private bool _braked;
        private bool _returnQueued;
        private bool _returned;
        private Vec3 _returnFrom;

        internal RushEntity(CastRecord cast, RushDelivery spec, Vec3 start, Vec3 dir, int dashTicks, EmberEntity ember)
            : base(cast)
        {
            _spec = spec;
            _start = start;
            _dir = dir;
            _maxTicks = dashTicks + 1;
            _ember = ember;
        }

        public RushPhase Phase => _phase;
        public bool Braked => _braked;
        public bool Returned => _returned;
        public Vec3 Start => _start;

        public override bool AcceptsRecast => _spec.Brake || _spec.Returns;

        protected internal override void Step(SpellContext ctx)
        {
            DuelPlayer p = ctx.Player(Owner);
            switch (_phase)
            {
                case RushPhase.Outbound:
                    _ticks++;
                    if ((p.Motor.IsDisplaced && _ticks < _maxTicks && p.Alive) || _ticks <= 1) return;
                    FinishOutbound(ctx, p, false);
                    return;
                case RushPhase.Waiting:
                    if (_returnQueued)
                    {
                        _returnQueued = false;
                        StartReturn(ctx, p);
                        return;
                    }

                    if (_ember == null || !_ember.IsIntact)
                    {
                        End(ctx); // the anchor is gone: nothing left to return to (spec L512)
                    }

                    return;
                case RushPhase.Returning:
                    _ticks++;
                    if (_ember == null || !_ember.IsIntact)
                    {
                        // Destroying the ember cancels the dependent return (spec L512).
                        Halt(p);
                        ctx.Emit(SpellEventKind.MovementEnded, Cast, p.Position, p.Position - _returnFrom, Id, Vec3.Distance(_returnFrom, p.Position));
                        End(ctx);
                        return;
                    }

                    if ((p.Motor.IsDisplaced && _ticks < _maxTicks && p.Alive) || _ticks <= 1) return;
                    Halt(p);
                    _returned = true;
                    ctx.Emit(SpellEventKind.MovementEnded, Cast, p.Position, p.Position - _returnFrom, Id, Vec3.Distance(_returnFrom, p.Position));
                    _ember.Consume(ctx);
                    End(ctx);
                    return;
            }
        }

        protected internal override void OnRecast(SpellContext ctx)
        {
            DuelPlayer p = ctx.Player(Owner);
            if (_phase == RushPhase.Outbound && _spec.Brake && !_braked)
            {
                // AUG-314: "Recast during dash stops early and emits 1.2 m burst ... once"; never another dash or refund.
                _braked = true;
                Halt(p);
                FinishOutbound(ctx, p, true);
                return;
            }

            if (!_spec.Returns || _returned) return;
            if (_phase == RushPhase.Outbound) _returnQueued = true; // runs as soon as the outbound dash has ended
            else if (_phase == RushPhase.Waiting) StartReturn(ctx, p);
        }

        private void FinishOutbound(SpellContext ctx, DuelPlayer p, bool brake)
        {
            Halt(p);
            Vec3 end = p.Position;
            ctx.Emit(SpellEventKind.MovementEnded, Cast, end, end - _start, Id, Vec3.Distance(_start, end));
            Vec3 center = end + new Vec3(0f, 0.5f, 0f);
            if (brake)
            {
                // The brake burst replaces the endpoint burst as the cast's primary damage, so it carries the cast's one
                // mastery result (spec L94, L480: an augment that changes delivery inherits it).
                KitSupport.Burst(ctx, Cast, _spec.BrakeBurst, center, _spec.BrakeRadius, CastHitKinds.Splash, Cast.DirectDamageMultiplier, center);
            }
            else
            {
                // The record's damage is the endpoint burst: the primary payload with the cast's one mastery result (L480).
                KitSupport.Burst(ctx, Cast, Cast.Form.Payload, center, _spec.BurstRadius, CastHitKinds.Splash, Cast.DirectDamageMultiplier, center);
            }

            LayTrail(ctx, end);
            if (_spec.Returns && _ember != null && _ember.IsIntact && !_returned)
            {
                _phase = RushPhase.Waiting;
                if (_returnQueued)
                {
                    _returnQueued = false;
                    StartReturn(ctx, p);
                }

                return;
            }

            End(ctx);
        }

        // The trail lies behind the travelling body: from the start to half a meter short of the arrival point.
        private void LayTrail(SpellContext ctx, Vec3 end)
        {
            Vec3 path = new Vec3(end.X - _start.X, 0f, end.Z - _start.Z);
            float length = path.Length();
            float trailLength = length - GlobalRules.World.CellSize;
            if (!(trailLength > 0.25f) || !(_spec.TrailWidth > 0f)) return;
            Vec3 dir = path / length;
            Vec2 from = _start.XZ();
            Vec2 to = (_start + dir * trailLength).XZ();
            Footprint footprint = Footprint.Lane(from, to, _spec.TrailWidth);
            Vec2 mid = (from + to) * 0.5f;
            var center = new Vec3(mid.X, _start.Y, mid.Y);
            float bound = (float)Math.Sqrt(trailLength * trailLength * 0.25f + _spec.TrailWidth * _spec.TrailWidth * 0.25f);
            HeatStripEntity.Create(ctx, Cast, footprint, center, bound, _spec.TrailHeat, _spec.TrailLifetimeSec, _spec.TrailContact);
        }

        private void StartReturn(SpellContext ctx, DuelPlayer p)
        {
            if (_ember == null || !_ember.IsIntact || _returned) return;
            Vec3 toEmber = new Vec3(_ember.Position.X - p.Position.X, 0f, _ember.Position.Z - p.Position.Z);
            float distance = Math.Min(Math.Min(_spec.ReturnDistance, toEmber.Length()), p.Stats.EffectiveDashDistance(_spec.ReturnDistance));
            if (!(distance > 0.05f))
            {
                _returned = true;
                _ember.Consume(ctx);
                End(ctx);
                return;
            }

            Vec3 dir = toEmber.Normalized();
            int ticks = RushDelivery.DashTicks(distance);
            _returnFrom = p.Position;
            if (!ctx.World.TryDash(Owner, dir, distance, ticks, Cast.SpellId))
            {
                ctx.Emit(SpellEventKind.MovementRefused, Cast, p.Position, dir);
                return;
            }

            _phase = RushPhase.Returning;
            _ticks = 0;
            ctx.Emit(SpellEventKind.MovementStarted, Cast, p.Position, dir, Id, distance, ticks: ticks);
        }

        // A spell dash ends where it stopped: no carried momentum (D-141).
        private static void Halt(DuelPlayer p)
        {
            if (p.Motor.DisplaceKind == DisplacementKind.Knockback) return;
            CharacterMotor.CancelDisplacement(ref p.Motor);
            p.Motor.Velocity = new Vec3(0f, p.Motor.Velocity.Y, 0f);
        }

        protected override void HashState(ref StateHasher h)
        {
            h.Add(_start);
            h.Add((int)_phase);
            h.Add(_ticks);
            h.Add(_braked);
            h.Add(_returnQueued);
            h.Add(_returned);
            h.Add(_returnFrom);
        }
    }

    public enum RushPhase
    {
        Outbound,
        Waiting,
        Returning,
    }

    /// <summary>
    /// AUG-313 Rush Return's "25-health departure ember": a visible destructible anchor (spec L512, L527 spheres) the
    /// return dash runs toward. Destroying it cancels the return.
    /// </summary>
    public sealed class EmberEntity : SpellEntity, ISpellBody
    {
        public const float Radius = 0.3f;

        private readonly int _lifetimeTicks;
        private float _health;
        private int _endTick = -1;

        internal EmberEntity(CastRecord cast, Vec3 position, float health, int lifetimeTicks)
            : base(cast)
        {
            Position = position;
            _health = health;
            _lifetimeTicks = lifetimeTicks;
        }

        public Vec3 Position { get; }
        public float Health => _health;

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

        internal void Consume(SpellContext ctx)
        {
            if (!Alive) return;
            ctx.Emit(SpellEventKind.DeployableExpired, Cast, Position, default, Id);
            End(ctx);
        }

        protected internal override void Step(SpellContext ctx)
        {
            if (_endTick < 0) _endTick = SpawnTick + _lifetimeTicks;
            if (ctx.Tick >= _endTick) Consume(ctx);
        }

        protected override void HashState(ref StateHasher h)
        {
            h.Add(Position);
            h.Add(_health);
            h.Add(_endTick);
        }
    }
}
