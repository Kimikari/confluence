using System;
using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Time;
using Confluence.Reactions.Grid;
using Confluence.Sim.Hashing;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.World;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Content.Wizards.Wiz002
{
    /// <summary>
    /// AUG-305 Skipping Fire (Silver, SPL-009, exclusive group SPL-009-delivery). Rank 1: "Fireball bounces once off
    /// ground at 36 direct or 20 splash." Rank 2: "Choose one bounce or normal impact; bounced damage 40/22." Drawback:
    /// "Projectile speed 24 becomes 20 m/s; bounce visibly marked." Counterplay: "Use cover beyond bounce or destroy ward
    /// line."
    /// <list type="bullet">
    /// <item>The 20 m/s fireball reflects once off walkable ground (spec L527: one bounce) and keeps its single mastery
    /// result (spec L508); before the bounce it hits as a normal Fireball (48/28), after it at 36/20 (rank 2: 40/22),
    /// with the same direct/splash exclusivity and Heat 2 impact.</item>
    /// <item>Its predicted ground bounce point is marked at launch, and every bounce is a visible event.</item>
    /// <item>Rank 2's choice is made by holding the Cast Modifier when the cast commits (a class control read only while
    /// the cast is open, spec L474; before release): held, the fireball impacts normally with the base 48/28.</item>
    /// </list>
    /// No structural delta: it stays a projectile.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class SkippingFire : ISpellTransform
    {
        public const string Id = "AUG-305";

        public static readonly ProseParameter Rank1Direct = ProseParameter.Of("rank1_bounced_direct", 36, "at 36 direct");
        public static readonly ProseParameter Rank1Splash = ProseParameter.Of("rank1_bounced_splash", 20, "or 20 splash");
        public static readonly ProseParameter Rank2Direct = ProseParameter.Of("rank2_bounced_direct", 40, "bounced damage 40/22");
        public static readonly ProseParameter Rank2Splash = ProseParameter.Of("rank2_bounced_splash", 22, "bounced damage 40/22");
        public static readonly ProseParameter Speed = ProseParameter.Of("speed", 20, "Projectile speed 24 becomes 20 m/s");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Rank1Direct, Rank1Splash, Rank2Direct, Rank2Splash, Speed };

        public TransformStage Stage => TransformStage.Delivery;

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Apply(SpellFormBuilder form, int rank)
        {
            ProjectileDelivery source = form.Delivery as ProjectileDelivery;
            if (source == null) throw new InvalidOperationException(Id + " transforms a projectile delivery (SPL-009).");
            PayloadSpec base_ = form.Payload;
            var bounced = new PayloadSpec
            {
                Channel = base_.Channel,
                Direct = rank >= 2 ? Rank2Direct.Float : Rank1Direct.Float,
                Splash = rank >= 2 ? Rank2Splash.Float : Rank1Splash.Float,
                SplashRadius = base_.SplashRadius,
                SplashExcludesDirect = base_.SplashExcludesDirect,
            };
            CopyDeposits(base_, bounced);
            form.Delivery = new HeatProjectileDelivery
            {
                Speed = Speed.Float,
                Radius = source.Radius,
                MaxRange = source.MaxRange,
                Bounces = SpellRules.MaxReflections, // "bounces once off ground" (spec L527: one bounce)
                BouncedPayload = bounced,
                BounceChoice = rank >= 2,
            };
            if (rank >= 2) form.Script = BounceChoiceScript.Instance;
        }

        internal static void CopyDeposits(PayloadSpec from, PayloadSpec to)
        {
            for (int i = 0; i < from.Deposits.Count; i++)
            {
                DepositSpec d = from.Deposits[i];
                to.AddDeposit(d.Kind, d.Strength, d.Placement, d.Radius, d.LifetimeSec).HeavyImpact = d.HeavyImpact;
            }
        }
    }

    /// <summary>AUG-305 rank 2: the Cast Modifier held at commitment chooses the normal impact (recorded on the cast).</summary>
    internal sealed class BounceChoiceScript : SpellScript
    {
        public const int NormalImpact = 1;

        public static readonly BounceChoiceScript Instance = new BounceChoiceScript();

        public override void OnCommitted(SpellContext ctx, CastRecord cast)
        {
            if (ctx.Player(cast.Caster).Input.Has(InputButtons.CastModifier)) cast.Choice = NormalImpact;
        }
    }

    /// <summary>
    /// AUG-306 Cinder Seed (Gold, SPL-009, exclusive group SPL-009-delivery). Rank 1: "Impact plants 25-health cinder for
    /// 3 s; recast bursts 24 damage in 1.5 m." Rank 2: "Seed lasts 4 s and burst 28 in 1.8 m." Drawback: "Initial damage
    /// 48/28 becomes 24/16; Heat 2 total shared." Counterplay: "Shoot cinder or leave marker."
    /// <list type="bullet">
    /// <item>The fireball hits for 24 direct or 16 splash; wherever it impacts it plants a visible 25-health cinder (a
    /// destructible body, spec L527; on the ground below when it struck a body).</item>
    /// <item>The spell recast (spec L94: once, during the cinder's life) bursts the cinder: 24 thermal splash within 1.5 m
    /// (rank 2: 28 within 1.8 m, 4 s). A destroyed or expired cinder never bursts.</item>
    /// <item>"Heat 2 total shared" (spec L529 divided strength): the impact keeps one heat unit in its cell and the burst
    /// releases the other in the cinder's cell, so the cast never deposits more than its Heat 2.</item>
    /// </list>
    /// No structural delta: the projectile remains a projectile and the cinder is an anchor, not a field or trap.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class CinderSeed : ISpellTransform
    {
        public const string Id = "AUG-306";

        public static readonly ProseParameter Health = ProseParameter.Of("cinder_health", 25, "Impact plants 25-health cinder");
        public static readonly ProseParameter Rank1Lifetime = ProseParameter.Of("rank1_cinder_lifetime", 3, "25-health cinder for 3 s");
        public static readonly ProseParameter Rank1Burst = ProseParameter.Of("rank1_burst", 24, "recast bursts 24 damage");
        public static readonly ProseParameter Rank1Radius = ProseParameter.Of("rank1_burst_radius", 1.5, "24 damage in 1.5 m");
        public static readonly ProseParameter Rank2Lifetime = ProseParameter.Of("rank2_cinder_lifetime", 4, "Seed lasts 4 s");
        public static readonly ProseParameter Rank2Burst = ProseParameter.Of("rank2_burst", 28, "burst 28 in 1.8 m");
        public static readonly ProseParameter Rank2Radius = ProseParameter.Of("rank2_burst_radius", 1.8, "burst 28 in 1.8 m");
        public static readonly ProseParameter InitialDirect = ProseParameter.Of("initial_direct", 24, "becomes 24/16");
        public static readonly ProseParameter InitialSplash = ProseParameter.Of("initial_splash", 16, "becomes 24/16");
        public static readonly ProseParameter SharedHeat = ProseParameter.Of("shared_heat", 2, "Heat 2 total shared");

        private static readonly IReadOnlyList<ProseParameter> All = new[]
        {
            Health, Rank1Lifetime, Rank1Burst, Rank1Radius, Rank2Lifetime, Rank2Burst, Rank2Radius, InitialDirect, InitialSplash, SharedHeat,
        };

        public TransformStage Stage => TransformStage.Delivery;

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Apply(SpellFormBuilder form, int rank)
        {
            ProjectileDelivery source = form.Delivery as ProjectileDelivery;
            if (source == null) throw new InvalidOperationException(Id + " transforms a projectile delivery (SPL-009).");
            PayloadSpec payload = form.Payload;
            payload.Direct = InitialDirect.Float;
            payload.Splash = InitialSplash.Float;

            // Heat 2 shared between the impact and the burst: one unit each (spec L529).
            int half = (int)SharedHeat.Value / 2;
            payload.ClearDeposits();
            payload.AddDeposit(DepositKind.Heat, half, DepositPlacement.Impact);
            var burst = new PayloadSpec
            {
                Channel = payload.Channel,
                Splash = rank >= 2 ? Rank2Burst.Float : Rank1Burst.Float,
                SplashRadius = rank >= 2 ? Rank2Radius.Float : Rank1Radius.Float,
                GuardEligible = false,
            };
            burst.AddDeposit(DepositKind.Heat, (int)SharedHeat.Value - half, DepositPlacement.Impact);
            form.Delivery = new HeatProjectileDelivery
            {
                Speed = source.Speed,
                Radius = source.Radius,
                MaxRange = source.MaxRange,
                SeedLifetimeSec = rank >= 2 ? Rank2Lifetime.Float : Rank1Lifetime.Float,
                SeedHealth = Health.Float,
                SeedBurst = burst,
            };
            form.Recast = RecastRule.Default; // once while the cinder lives (spec L94)
        }
    }

    /// <summary>
    /// A Fireball variant in flight for the SPL-009 transformations: straight flight, one optional ground bounce with its
    /// own post-bounce payload (AUG-305), and an optional cinder planted at the impact (AUG-306). Direct hits are guarded
    /// like any projectile (spec L104) and the flight is never lag-compensated (spec L423).
    /// </summary>
    public sealed class HeatProjectileDelivery : DeliverySpec
    {
        private float _speed;
        private float _radius = SpellRules.DefaultProjectileRadius;
        private float _maxRange;
        private int _bounces;
        private PayloadSpec _bouncedPayload;
        private bool _bounceChoice;
        private float _seedLifetimeSec;
        private float _seedHealth;
        private PayloadSpec _seedBurst;

        public override DeliveryKind Kind => DeliveryKind.Projectile;

        public float Speed { get => _speed; set => _speed = Set(value); }
        public float Radius { get => _radius; set => _radius = Set(value); }
        public float MaxRange { get => _maxRange; set => _maxRange = Set(value); }

        /// <summary>Ground bounces before impact (AUG-305: one).</summary>
        public int Bounces { get => _bounces; set => _bounces = Set(value); }

        /// <summary>What an impact after a bounce applies (AUG-305 36/20 or 40/22).</summary>
        public PayloadSpec BouncedPayload { get => _bouncedPayload; set => _bouncedPayload = Set(value); }

        /// <summary>AUG-305 rank 2: the Cast Modifier held at commitment chooses the normal impact (no bounce).</summary>
        public bool BounceChoice { get => _bounceChoice; set => _bounceChoice = Set(value); }

        /// <summary>AUG-306: the cinder planted at the impact lives this long (0 = no cinder).</summary>
        public float SeedLifetimeSec { get => _seedLifetimeSec; set => _seedLifetimeSec = Set(value); }

        public float SeedHealth { get => _seedHealth; set => _seedHealth = Set(value); }

        /// <summary>The cinder's recast burst (its Splash and SplashRadius).</summary>
        public PayloadSpec SeedBurst { get => _seedBurst; set => _seedBurst = Set(value); }

        public bool PlantsSeed => SeedLifetimeSec > 0f;

        internal override void ApplyGlobalCaps()
        {
            _radius = Math.Max(0.01f, _radius);
            _bounces = Math.Max(0, Math.Min(_bounces, SpellRules.MaxReflections)); // spec L527 one bounce
            _seedLifetimeSec = Math.Min(_seedLifetimeSec, Core.Rules.GlobalRules.FieldsAndSummons.MaxLifetimeSec);
            if (!(_speed > 0f) || !(_maxRange > 0f)) throw new InvalidOperationException("A projectile needs a speed and a range (spec L527).");
            CapNested(_bouncedPayload);
            CapNested(_seedBurst);
        }

        protected override void OnFreeze()
        {
            _bouncedPayload?.Freeze();
            _seedBurst?.Freeze();
        }

        public override void Release(SpellContext ctx, CastRecord cast)
        {
            Vec3 origin = ctx.AimOrigin(cast.Caster);
            Vec3 dir = AimDirection(ctx, cast);
            float speed = Speed * cast.ProjectileSpeedMultiplier;
            bool normalImpact = BounceChoice && cast.Choice == BounceChoiceScript.NormalImpact;
            int bounces = normalImpact ? 0 : Bounces;
            var p = ctx.Spawn(new HeatProjectileEntity(cast, this, origin, dir * speed, bounces));
            ctx.Emit(SpellEventKind.ProjectileLaunched, cast, origin, dir, p.Id, speed);
            if (bounces > 0 && ctx.Collision.Raycast(origin, dir, MaxRange, SolidMask.Movement, out WorldHit ground) && ground.Normal.Y >= HeatProjectileEntity.WalkableNormalY)
            {
                ctx.Emit(SpellEventKind.Marker, cast, ground.Point, dir, p.Id); // "bounce visibly marked"
            }
        }
    }

    /// <summary>A Fireball variant in flight (see <see cref="HeatProjectileDelivery"/>).</summary>
    public sealed class HeatProjectileEntity : SpellEntity
    {
        public const float WalkableNormalY = 0.7f;

        private readonly HeatProjectileDelivery _spec;
        private int _bouncesLeft;
        private float _travelled;
        private bool _bounced;

        internal HeatProjectileEntity(CastRecord cast, HeatProjectileDelivery spec, Vec3 position, Vec3 velocity, int bounces)
            : base(cast)
        {
            _spec = spec;
            Position = position;
            Velocity = velocity;
            _bouncesLeft = bounces;
        }

        public Vec3 Position { get; private set; }
        public Vec3 Velocity { get; private set; }
        public bool Bounced => _bounced;

        protected internal override void Step(SpellContext ctx)
        {
            Vec3 dir = Velocity.Normalized();
            float length = Velocity.Length() * Ticks.Dt;
            bool last = false;
            if (_travelled + length >= _spec.MaxRange)
            {
                length = Math.Max(0f, _spec.MaxRange - _travelled);
                last = true;
            }

            SweepContact hit = SweepQuery.First(ctx, Owner, Position, dir, length, _spec.Radius, _spec.Radius, false, true);
            if (hit.Found)
            {
                Position = hit.Point;
                _travelled += hit.Distance;
                if (hit.IsSolid && _bouncesLeft > 0 && hit.Normal.Y >= WalkableNormalY)
                {
                    _bouncesLeft--;
                    _bounced = true;
                    Vec3 n = hit.Normal;
                    Velocity = Velocity - n * (2f * Vec3.Dot(Velocity, n));
                    Position = hit.Point + n * 0.01f;
                    ctx.Emit(SpellEventKind.ProjectileBounced, Cast, Position, Velocity.Normalized(), Id, solidId: hit.SolidId);
                    return;
                }

                Impact(ctx, hit, dir);
                return;
            }

            Position += dir * length;
            _travelled += length;
            if (last)
            {
                ctx.Emit(SpellEventKind.ProjectileExpired, Cast, Position, dir, Id, _travelled);
                End(ctx);
            }
        }

        private void Impact(SpellContext ctx, in SweepContact hit, Vec3 dir)
        {
            // After a bounce the bounced payload applies; it is the same source projectile, so its direct hit keeps the
            // cast's one mastery result (spec L508).
            bool bouncedPayload = _bounced && _spec.BouncedPayload != null;
            PayloadSpec payload = bouncedPayload ? _spec.BouncedPayload : Cast.Form.Payload;
            float directScale = bouncedPayload ? Cast.DirectDamageMultiplier : 1f;
            Vec3 point = hit.Point;
            Vec3 normal = hit.IsSolid ? hit.Normal : -dir;
            if (hit.IsPlayer)
            {
                ctx.Emit(SpellEventKind.ProjectileImpact, Cast, point, dir, Id, targetPlayer: hit.Player);
                PayloadHit direct = PayloadHit.Direct(point - dir * _spec.Radius, dir);
                direct.Scale = directScale;
                ctx.ApplyToPlayer(Cast, payload, hit.Player, direct);
            }
            else if (hit.IsBody)
            {
                ctx.Emit(SpellEventKind.ProjectileImpact, Cast, point, dir, Id);
                ctx.ApplyToBody(Cast, payload, hit.Body, CastHitKinds.Direct, directScale);
            }
            else
            {
                ctx.Emit(SpellEventKind.ProjectileImpact, Cast, point, dir, Id, solidId: hit.SolidId);
                ctx.DamageSolid(Cast, payload, hit.SolidId);
                ctx.ReportImpact(Cast, payload, point, dir, hit.SolidId, directScale);
            }

            ctx.ApplySplash(Cast, payload, point, normal);
            ctx.EmitDeposits(Cast, payload, DepositPlacement.Impact, point, dir);
            if (_spec.PlantsSeed)
            {
                Vec3 at = hit.IsSolid ? point : KitSupport.GroundBelow(ctx, point) + new Vec3(0f, CinderEntity.Radius, 0f);
                CinderEntity cinder = ctx.Spawn(new CinderEntity(Cast, _spec, at));
                ctx.Emit(SpellEventKind.DeployablePlaced, Cast, at, default, cinder.Id, _spec.SeedHealth, ticks: Ticks.FromSeconds(_spec.SeedLifetimeSec));
            }

            End(ctx);
        }

        protected override void HashState(ref StateHasher h)
        {
            h.Add(Position);
            h.Add(Velocity);
            h.Add(_travelled);
            h.Add(_bouncesLeft);
            h.Add(_bounced);
        }
    }

    /// <summary>AUG-306's planted cinder: a destructible marker that bursts on the spell recast (see <see cref="CinderSeed"/>).</summary>
    public sealed class CinderEntity : SpellEntity, ISpellBody
    {
        public const float Radius = 0.25f;

        private readonly HeatProjectileDelivery _spec;
        private readonly int _lifetimeTicks;
        private float _health;
        private bool _burst;

        internal CinderEntity(CastRecord cast, HeatProjectileDelivery spec, Vec3 position)
            : base(cast)
        {
            _spec = spec;
            Position = position;
            _health = spec.SeedHealth;
            _lifetimeTicks = Ticks.FromSeconds(spec.SeedLifetimeSec);
        }

        public Vec3 Position { get; }
        public float Health => _health;
        public bool HasBurst => _burst;

        public override bool AcceptsRecast => true;

        public bool IsIntact => Alive && _health > 0f;
        public Vec3 BodyCenter => Position;
        public float BodyRadius => Radius;

        public bool TakeDamage(SpellContext ctx, float amount, int attacker)
        {
            if (!IsIntact || !(amount > 0f)) return false;
            _health -= amount;
            if (_health > 0f) return false;
            ctx.Emit(SpellEventKind.DeployableDestroyed, Cast, Position, default, Id, targetPlayer: attacker); // shot: no burst
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
            if (!IsIntact || _burst) return;
            _burst = true;
            PayloadSpec burst = _spec.SeedBurst;
            ctx.Emit(SpellEventKind.DeployableDetonated, Cast, Position, default, Id, burst.SplashRadius);
            KitSupport.Burst(ctx, Cast, burst, Position, burst.SplashRadius, CastHitKinds.Splash, 1f, Position);
            ctx.EmitDeposits(Cast, burst, DepositPlacement.Impact, KitSupport.GroundBelow(ctx, Position), default);
            End(ctx);
        }

        protected override void HashState(ref StateHasher h)
        {
            h.Add(Position);
            h.Add(_health);
            h.Add(_burst);
        }
    }
}
