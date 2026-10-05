using System;
using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Core.Time;
using Confluence.Sim.Hashing;

namespace Confluence.Sim.Spells
{
    /// <summary>
    /// A visible projectile (spec L527: "a projectile's speed, footprint, lifetime, release cue, and collision behavior
    /// come from its effect text"). It flies from the caster's eye along the aim, optionally under gravity, until it
    /// strikes a solid, the opponent, or a hostile spell body, or reaches its maximum range or lifetime. A direct hit
    /// applies the primary payload's direct damage (guardable when the payload is, spec L104); an impact applies the
    /// splash around it, excluding a directly hit target when the record makes them exclusive (spec L525); impact
    /// deposits go to the materials seam (spec L163). Projectiles are never lag-compensated (spec L423).
    /// </summary>
    public sealed class ProjectileDelivery : DeliverySpec
    {
        private float _speed;
        private float _radius = SpellRules.DefaultProjectileRadius;
        private float _gravity;
        private float _maxRange;
        private float _lifetimeSec;
        private int _groundBounces;
        private float _bounceDamageScale = 1f;
        private float _bounceSpeedRetained = 1f;
        private bool _detonateOnExpire;
        private int _count = 1;
        private float _spreadDeg;
        private bool _hitsBodies = true;

        public override DeliveryKind Kind => DeliveryKind.Projectile;

        /// <summary>Launch speed in m/s ("24 m/s projectile").</summary>
        public float Speed { get => _speed; set => _speed = Set(value); }

        /// <summary>Collision radius in meters; narrow and wide variants state their own (SPL-033 "narrow 0.3 m or wide 0.6 m").</summary>
        public float Radius { get => _radius; set => _radius = Set(value); }

        /// <summary>Downward acceleration in m/s^2 (0 = straight flight; lobs and "drop" fields set it).</summary>
        public float Gravity { get => _gravity; set => _gravity = Set(value); }

        /// <summary>Maximum travel in meters (the record's range, spec L525); 0 = limited by lifetime only.</summary>
        public float MaxRange { get => _maxRange; set => _maxRange = Set(value); }

        /// <summary>Maximum flight time in seconds ("flies straight for up to 2.4 s"); 0 = limited by range only.</summary>
        public float LifetimeSec { get => _lifetimeSec; set => _lifetimeSec = Set(value); }

        /// <summary>Bounces off walkable ground before impacting (SPL-273 "one ground bounce is allowed"); 0 = none.</summary>
        public int GroundBounces { get => _groundBounces; set => _groundBounces = Set(value); }

        /// <summary>Damage kept after a bounce (SPL-065 "loses 25% damage" is 0.75).</summary>
        public float BounceDamageScale { get => _bounceDamageScale; set => _bounceDamageScale = Set(value); }

        /// <summary>Speed kept by a bounce.</summary>
        public float BounceSpeedRetained { get => _bounceSpeedRetained; set => _bounceSpeedRetained = Set(value); }

        /// <summary>Reaching the end of its range or lifetime bursts like an impact (SPL-111 "burst on impact/destruction").</summary>
        public bool DetonateOnExpire { get => _detonateOnExpire; set => _detonateOnExpire = Set(value); }

        /// <summary>Projectiles launched together, sharing the cast's payload caps (SPL-034 two bolts).</summary>
        public int Count { get => _count; set => _count = Set(value); }

        /// <summary>Total horizontal fan angle across several projectiles (diverging patterns).</summary>
        public float SpreadDeg { get => _spreadDeg; set => _spreadDeg = Set(value); }

        /// <summary>Strikes hostile summons and anchors in its path (spec L527 destructible bodies).</summary>
        public bool HitsBodies { get => _hitsBodies; set => _hitsBodies = Set(value); }

        public int LifetimeTicks => Ticks.FromSeconds(LifetimeSec);

        internal override void ApplyGlobalCaps()
        {
            _radius = Math.Max(0.01f, _radius);
            _count = Math.Max(1, _count);
            _groundBounces = Math.Max(0, _groundBounces);
            _bounceDamageScale = CMath.Clamp01(_bounceDamageScale);
            if (!(_speed > 0f)) throw new InvalidOperationException("A projectile needs a positive speed (spec L527).");
            if (!(_maxRange > 0f) && !(_lifetimeSec > 0f)) throw new InvalidOperationException("A projectile needs a range or a lifetime (spec L527).");
        }

        public override void Release(SpellContext ctx, CastRecord cast)
        {
            Vec3 origin = ctx.AimOrigin(cast.Caster);
            Vec3 dir = AimDirection(ctx, cast);
            float speed = Speed * cast.ProjectileSpeedMultiplier; // a projectile-speed mastery reward (spec L480)
            speed = ctx.World.Effects.AdjustProjectileSpeed(cast, speed); // release-time content hooks (Effects/EffectHooks.cs)
            for (int i = 0; i < Count; i++)
            {
                float offset = Count > 1 ? -SpreadDeg * 0.5f + SpreadDeg * i / (Count - 1) : 0f;
                Vec3 d = offset == 0f ? dir : CMath.ForwardFromYawPitch(CMath.YawOf(dir) + offset, CMath.PitchOf(dir));
                ProjectileEntity p = ctx.Spawn(new ProjectileEntity(cast, this, origin, d * speed));
                ctx.Emit(SpellEventKind.ProjectileLaunched, cast, origin, d, p.Id, speed);
            }
        }
    }

    /// <summary>A projectile in flight (see <see cref="ProjectileDelivery"/>).</summary>
    public sealed class ProjectileEntity : SpellEntity
    {
        private readonly ProjectileDelivery _spec;
        private readonly PayloadSpec _payload;
        private float _travelled;
        private int _ticksAlive;
        private int _bounces;
        private float _damageScale = 1f;

        /// <param name="payload">What an impact applies; null = the form's primary payload (summons fire their own).</param>
        public ProjectileEntity(CastRecord cast, ProjectileDelivery spec, Vec3 position, Vec3 velocity, PayloadSpec payload = null)
            : base(cast)
        {
            _spec = spec ?? throw new ArgumentNullException(nameof(spec));
            _payload = payload ?? cast.Form.Payload;
            Position = position;
            Velocity = velocity;
        }

        public ProjectileDelivery Spec => _spec;
        public Vec3 Position { get; private set; }
        public Vec3 Velocity { get; private set; }
        public float Travelled => _travelled;

        /// <summary>Changes the flight speed from the next step on (content that speeds a projectile mid-flight).</summary>
        internal void ScaleSpeed(float factor) => Velocity *= Math.Max(0f, factor);

        protected internal override void Step(SpellContext ctx)
        {
            float dt = Ticks.Dt;
            Vec3 v0 = Velocity;
            Vec3 v1 = _spec.Gravity > 0f ? v0 - new Vec3(0f, _spec.Gravity * dt, 0f) : v0;
            Vec3 delta = (v0 + v1) * (0.5f * dt);
            float length = delta.Length();
            bool lastStep = false;
            if (_spec.MaxRange > 0f && _travelled + length >= _spec.MaxRange)
            {
                length = Math.Max(0f, _spec.MaxRange - _travelled);
                lastStep = true;
            }

            _ticksAlive++;
            if (_spec.LifetimeTicks > 0 && _ticksAlive >= _spec.LifetimeTicks) lastStep = true;

            Vec3 dir = delta.LengthSquared() > 0f ? delta.Normalized() : v0.Normalized();
            SweepContact hit = SweepQuery.First(ctx, Owner, Position, dir, length, _spec.Radius, _spec.Radius, false, _spec.HitsBodies);
            if (hit.Found)
            {
                Position = hit.Point;
                _travelled += hit.Distance;
                if (hit.IsSolid && TryBounce(ctx, hit, v1)) return;
                Impact(ctx, hit, dir);
                return;
            }

            Position += dir * length;
            Velocity = v1;
            _travelled += length;
            if (lastStep) Expire(ctx, dir);
        }

        // Walkable ground turns the projectile upward once per allowed bounce (spec L527 one bounce by default).
        private bool TryBounce(SpellContext ctx, in SweepContact hit, Vec3 velocity)
        {
            if (_bounces >= _spec.GroundBounces || hit.Normal.Y < 0.7f) return false;
            _bounces++;
            _damageScale *= _spec.BounceDamageScale;
            Vec3 n = hit.Normal;
            Vec3 reflected = velocity - n * (2f * Vec3.Dot(velocity, n));
            Velocity = reflected * _spec.BounceSpeedRetained;
            Position = hit.Point + n * 0.01f;
            ctx.Emit(SpellEventKind.ProjectileBounced, Cast, Position, Velocity.Normalized(), Id, solidId: hit.SolidId);
            return true;
        }

        private void Impact(SpellContext ctx, in SweepContact hit, Vec3 dir)
        {
            PayloadSpec payload = _payload;
            Vec3 point = hit.Point;
            Vec3 normal = hit.IsSolid ? hit.Normal : -dir;
            if (hit.IsPlayer)
            {
                ctx.Emit(SpellEventKind.ProjectileImpact, Cast, point, dir, Id, targetPlayer: hit.Player);
                var direct = PayloadHit.Direct(point - dir * _spec.Radius, dir);
                direct.Scale = _damageScale;
                direct.WeakPoint = IsWeakPoint(ctx, hit.Player, point);
                ctx.ApplyToPlayer(Cast, payload, hit.Player, direct);
            }
            else if (hit.IsBody)
            {
                ctx.Emit(SpellEventKind.ProjectileImpact, Cast, point, dir, Id);
                ctx.ApplyToBody(Cast, payload, hit.Body, CastHitKinds.Direct, _damageScale);
            }
            else
            {
                ctx.Emit(SpellEventKind.ProjectileImpact, Cast, point, dir, Id, solidId: hit.SolidId);
                ctx.DamageSolid(Cast, payload, hit.SolidId);
                ctx.ReportImpact(Cast, payload, point, dir, hit.SolidId, _damageScale); // ice, brittle and combustible cover (L145-L147)
            }

            Burst(ctx, point, normal, dir);
            End(ctx);
        }

        private void Expire(SpellContext ctx, Vec3 dir)
        {
            ctx.Emit(SpellEventKind.ProjectileExpired, Cast, Position, dir, Id, _travelled);
            if (_spec.DetonateOnExpire) Burst(ctx, Position, Vec3.Up, dir);
            End(ctx);
        }

        private void Burst(SpellContext ctx, Vec3 point, Vec3 normal, Vec3 dir)
        {
            PayloadSpec payload = _payload;
            ctx.ApplySplash(Cast, payload, point, normal);
            ctx.EmitDeposits(Cast, payload, DepositPlacement.Impact, point, dir);
        }

        // The weak point of a precision spell is the head: the top 0.3 m of the struck capsule (spec L110).
        private static bool IsWeakPoint(SpellContext ctx, int target, Vec3 point)
        {
            Capsule body = ctx.Player(target).HitVolume;
            return point.Y >= body.B.Y + body.Radius - 0.3f;
        }

        protected override void HashState(ref StateHasher h)
        {
            h.Add(Position);
            h.Add(Velocity);
            h.Add(_travelled);
            h.Add(_ticksAlive);
            h.Add(_bounces);
            h.Add(_damageScale);
        }
    }
}
