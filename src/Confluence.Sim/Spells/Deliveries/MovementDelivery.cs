using System;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions.Grid;
using Confluence.Sim.Duel;
using Confluence.Sim.Hashing;
using Confluence.Sim.Movement;
using Confluence.Sim.Players;

namespace Confluence.Sim.Spells
{
    /// <summary>Which way a dash travels relative to the caster's view.</summary>
    public enum DashDirection
    {
        /// <summary>Along the horizontal aim ("Dash up to 8 m along ground").</summary>
        Forward,

        /// <summary>Backward relative to current facing (SPL-093).</summary>
        Backward,

        /// <summary>The movement keys held at release, else forward (lateral dashes, SPL-221).</summary>
        MovementInput,
    }

    /// <summary>
    /// Self movement spells: a dash along the ground, a leap toward a visible point, or a teleport (spec L124: dash at
    /// most 8 m, teleport at most 10 m, after stat modifiers). Dashes and leaps run on the motor, so they stop at solid
    /// geometry and are clamped at the legal boundary without edge damage (spec L51); a teleport never ends outside the
    /// boundary or inside collision and is refused otherwise (the cast stays spent, spec L90). Optional riders: the
    /// first opponent touched takes the payload once (SPL-109, SPL-114, which may end the dash), an endpoint burst
    /// (SPL-013 "1 m endpoint burst"), and a trail deposit along the travelled path. None grants invulnerability (L82).
    /// </summary>
    public sealed class MovementDelivery : DeliverySpec
    {
        private DeliveryKind _kind = DeliveryKind.Dash;
        private float _distance;
        private float _durationSec;
        private DashDirection _direction;
        private float _maxRise;
        private float _contactRadius;
        private bool _stopOnContact;
        private PayloadSpec _endpointBurst;
        private float _trailWidth;

        /// <summary>Dash, Leap, or Teleport.</summary>
        public DeliveryKind Mode
        {
            get => _kind;
            set
            {
                if (value != DeliveryKind.Dash && value != DeliveryKind.Leap && value != DeliveryKind.Teleport) throw new ArgumentOutOfRangeException(nameof(value));
                _kind = Set(value);
            }
        }

        public override DeliveryKind Kind => _kind;

        /// <summary>Maximum travel in meters (horizontal for leaps).</summary>
        public float Distance { get => _distance; set => _distance = Set(value); }

        /// <summary>Travel time in seconds (dashes, leaps); 0 = the default dash speed.</summary>
        public float DurationSec { get => _durationSec; set => _durationSec = Set(value); }

        public DashDirection Direction { get => _direction; set => _direction = Set(value); }

        /// <summary>Highest landing point above the caster for leaps ("up to 6 m away and 3 m higher").</summary>
        public float MaxRise { get => _maxRise; set => _maxRise = Set(value); }

        /// <summary>Opponents within this distance of the moving body take the primary payload once; 0 = none.</summary>
        public float ContactRadius { get => _contactRadius; set => _contactRadius = Set(value); }

        /// <summary>The first contact ends the dash (SPL-114 "first contact ends dash").</summary>
        public bool StopOnContact { get => _stopOnContact; set => _stopOnContact = Set(value); }

        /// <summary>Splash applied where the movement ends (its Splash and SplashRadius), or null.</summary>
        public PayloadSpec EndpointBurst { get => _endpointBurst; set => _endpointBurst = Set(value); }

        /// <summary>Width of the path deposit (the form payload's Path deposits), meters.</summary>
        public float TrailWidth { get => _trailWidth; set => _trailWidth = Set(value); }

        public override bool RequiresCue => Mode == DeliveryKind.Teleport; // spec L96: teleport destinations

        internal override void ApplyGlobalCaps()
        {
            float cap = Mode == DeliveryKind.Teleport ? GlobalRules.Modifiers.MaxTeleportDistance : GlobalRules.Modifiers.MaxDashDistance;
            _distance = Math.Min(Math.Max(0f, _distance), cap); // spec L124
            if (!(_distance > 0f)) throw new InvalidOperationException("A movement spell needs a positive distance (spec L124).");
            CapNested(_endpointBurst);
        }

        protected override void OnFreeze() => _endpointBurst?.Freeze();

        public override Vec3 PredictTarget(SpellContext ctx, SpellForm form, int caster)
        {
            DuelPlayer p = ctx.Player(caster);
            if (Mode == DeliveryKind.Dash) return p.Position + HorizontalDirection(ctx, caster, ctx.AimDirection(caster)) * DistanceFor(p);
            return Destination(ctx, caster, ctx.AimDirection(caster));
        }

        public override void Release(SpellContext ctx, CastRecord cast)
        {
            DuelPlayer p = ctx.Player(cast.Caster);
            Vec3 aim = AimDirection(ctx, cast);
            switch (Mode)
            {
                case DeliveryKind.Teleport:
                    Teleport(ctx, cast, p, aim);
                    break;
                case DeliveryKind.Leap:
                    Leap(ctx, cast, p, aim);
                    break;
                default:
                    Dash(ctx, cast, p, aim);
                    break;
            }
        }

        private float DistanceFor(DuelPlayer p) =>
            Mode == DeliveryKind.Teleport ? Math.Min(Distance, p.Stats.EffectiveTeleportDistance(Distance)) : Math.Min(Distance, p.Stats.EffectiveDashDistance(Distance));

        private void Dash(SpellContext ctx, CastRecord cast, DuelPlayer p, Vec3 aim)
        {
            Vec3 dir = HorizontalDirection(ctx, cast.Caster, aim);
            float distance = DistanceFor(p);
            int ticks = DurationSec > 0f ? Ticks.FromSeconds(DurationSec) : Math.Max(1, (int)Math.Ceiling(distance / SpellRules.DefaultDashSpeed * Ticks.PerSecond));
            if (!ctx.World.TryDash(cast.Caster, dir, distance, ticks, cast.SpellId))
            {
                ctx.Emit(SpellEventKind.MovementRefused, cast, p.Position, dir);
                return;
            }

            ctx.Emit(SpellEventKind.MovementStarted, cast, p.Position, dir, 0, distance, ticks: ticks);
            ctx.Spawn(new MovementEntity(cast, this, p.Position, ticks + 1));
        }

        private void Leap(SpellContext ctx, CastRecord cast, DuelPlayer p, Vec3 aim)
        {
            Vec3 target = Destination(ctx, cast.Caster, aim);
            Vec3 delta = target - p.Position;
            Vec3 flat = new Vec3(delta.X, 0f, delta.Z);
            float horizontal = flat.Length();
            float seconds = DurationSec > 0f ? DurationSec : 0.8f;
            int ticks = Math.Max(1, Ticks.FromSeconds(seconds));
            float rise = Math.Min(delta.Y, MaxRise);
            if (horizontal < 0.05f || !ctx.World.TryDash(cast.Caster, flat, horizontal, ticks, cast.SpellId))
            {
                ctx.Emit(SpellEventKind.MovementRefused, cast, p.Position, flat);
                return;
            }

            // Vertical launch so the arc reaches the landing height after the travel time under normal gravity.
            float t = ticks * Ticks.Dt;
            float vy = (rise + 0.5f * GlobalRules.Movement.Gravity * t * t) / t;
            p.Motor.Velocity = new Vec3(p.Motor.Velocity.X, vy, p.Motor.Velocity.Z);
            p.Motor.Grounded = false;
            ctx.Emit(SpellEventKind.MovementStarted, cast, p.Position, flat.Normalized(), 0, horizontal, ticks: ticks);
            ctx.Spawn(new MovementEntity(cast, this, p.Position, ticks + Ticks.PerSecond));
        }

        private void Teleport(SpellContext ctx, CastRecord cast, DuelPlayer p, Vec3 aim)
        {
            Vec3 from = p.Position;
            Vec3 destination = Destination(ctx, cast.Caster, aim);
            if (!ctx.World.TryTeleport(cast.Caster, destination, cast.SpellId))
            {
                ctx.Emit(SpellEventKind.MovementRefused, cast, destination, aim);
                return;
            }

            ctx.Emit(SpellEventKind.MovementEnded, cast, p.Position, p.Position - from, 0, Vec3.Distance(from, p.Position));
            Finish(ctx, cast, from, p.Position);
        }

        /// <summary>The ground point along the aim within the movement's distance (leaps also cap the rise).</summary>
        private Vec3 Destination(SpellContext ctx, int caster, Vec3 aim)
        {
            DuelPlayer p = ctx.Player(caster);
            float distance = DistanceFor(p);
            Vec3 ground = ctx.GroundPoint(caster, distance + p.Motor.Height);
            Vec3 flat = new Vec3(ground.X - p.Position.X, 0f, ground.Z - p.Position.Z);
            if (flat.Length() > distance) flat = flat.Normalized() * distance;
            float y = ground.Y;
            if (Mode == DeliveryKind.Leap) y = Math.Min(y, p.Position.Y + MaxRise);
            return new Vec3(p.Position.X + flat.X, y, p.Position.Z + flat.Z);
        }

        private Vec3 HorizontalDirection(SpellContext ctx, int caster, Vec3 aim)
        {
            DuelPlayer p = ctx.Player(caster);
            Vec3 forward = new Vec3(aim.X, 0f, aim.Z).NormalizedOr(CMath.ForwardFromYaw(p.Motor.YawDeg));
            switch (Direction)
            {
                case DashDirection.Backward:
                    return -forward;
                case DashDirection.MovementInput:
                    Vec3 right = CMath.RightFromYaw(CMath.YawOf(forward));
                    Vec3 wish = right * p.Input.MoveX + forward * p.Input.MoveY;
                    return wish.LengthSquared() > 0.01f ? wish.Normalized() : forward;
                default:
                    return forward;
            }
        }

        /// <summary>Endpoint burst and trail deposit once the movement is over.</summary>
        internal void Finish(SpellContext ctx, CastRecord cast, Vec3 from, Vec3 to)
        {
            if (EndpointBurst != null) ctx.ApplySplash(cast, EndpointBurst, to + new Vec3(0f, 0.5f, 0f), Vec3.Up);
            Vec3 path = to - from;
            if (TrailWidth > 0f && path.LengthXZ() > 0.01f)
            {
                ctx.EmitDeposits(cast, cast.Form.Payload, DepositPlacement.Path, to, path, Footprint.Segment(from.XZ(), to.XZ(), TrailWidth));
            }

            ctx.EmitDeposits(cast, cast.Form.Payload, DepositPlacement.Impact, to, path);
        }
    }

    /// <summary>A dash or leap in progress: contact riders while moving, then the endpoint effects.</summary>
    public sealed class MovementEntity : SpellEntity
    {
        private readonly MovementDelivery _spec;
        private readonly Vec3 _start;
        private readonly int _maxTicks;
        private readonly bool[] _contacted = new bool[DuelWorld.PlayerCount];
        private int _ticks;

        internal MovementEntity(CastRecord cast, MovementDelivery spec, Vec3 start, int maxTicks)
            : base(cast)
        {
            _spec = spec;
            _start = start;
            _maxTicks = maxTicks;
        }

        protected internal override void Step(SpellContext ctx)
        {
            DuelPlayer p = ctx.Player(Owner);
            _ticks++;
            if (_spec.ContactRadius > 0f) CheckContacts(ctx, p);

            bool moving = p.Motor.IsDisplaced || (_spec.Mode == DeliveryKind.Leap && !p.Motor.Grounded);
            if ((moving && _ticks < _maxTicks && p.Alive) || _ticks <= 1) return;
            Halt(p);
            ctx.Emit(SpellEventKind.MovementEnded, Cast, p.Position, p.Position - _start, Id, Vec3.Distance(_start, p.Position));
            _spec.Finish(ctx, Cast, _start, p.Position);
            End(ctx);
        }

        private void CheckContacts(SpellContext ctx, DuelPlayer self)
        {
            Capsule mine = self.HitVolume;
            for (int i = 0; i < DuelWorld.PlayerCount; i++)
            {
                if (i == Owner || _contacted[i] || !ctx.IsTargetable(i)) continue;
                Capsule other = ctx.Player(i).HitVolume;
                float d2 = Intersections.SegmentSegmentClosest(mine.A, mine.B, other.A, other.B, out _, out _, out _, out _);
                float reach = mine.Radius + other.Radius + _spec.ContactRadius;
                if (d2 > reach * reach) continue;
                _contacted[i] = true; // "first target contacted takes damage once"
                Vec3 travel = self.Motor.IsDisplaced ? self.Motor.DisplaceVelocity : other.Center - mine.Center;
                ctx.ApplyToPlayer(Cast, Cast.Form.Payload, i, PayloadHit.Direct(self.Position, new Vec3(travel.X, 0f, travel.Z)));
                if (_spec.StopOnContact) Halt(self);
            }
        }

        // The movement ends where it stopped: no momentum carries the body past the stated distance (spec L124 caps).
        private static void Halt(DuelPlayer p)
        {
            if (p.Motor.DisplaceKind == DisplacementKind.Knockback) return; // a hostile displacement owns the body now
            CharacterMotor.CancelDisplacement(ref p.Motor);
            p.Motor.Velocity = new Vec3(0f, p.Motor.Velocity.Y, 0f);
        }

        protected override void HashState(ref StateHasher h)
        {
            h.Add(_start);
            h.Add(_ticks);
            for (int i = 0; i < _contacted.Length; i++) h.Add(_contacted[i]);
        }
    }
}
