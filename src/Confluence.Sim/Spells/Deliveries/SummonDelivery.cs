using System;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Duel;
using Confluence.Sim.Fields;
using Confluence.Sim.Hashing;
using Confluence.Sim.Players;

namespace Confluence.Sim.Spells
{
    /// <summary>How a summon's body moves (spec L537: stationary stays where placed; mobile follows an orbit or route).</summary>
    public enum SummonMobility
    {
        Stationary,

        /// <summary>Circles its caster ("orbits caster at 1.5 m").</summary>
        Orbit,

        /// <summary>Walks a fixed straight route along the aim at placement ("follows a fixed 6 m route").</summary>
        Route,
    }

    /// <summary>
    /// A summon (spec L527: "summons need visible targets, destructible bodies, bounded range and lifetime; they cannot
    /// read a hidden enemy or acquire through steam"). It is a player-owned entity (at most two, 8 s, oldest retires,
    /// spec L128) with a body hostile deliveries can destroy, and it fires its attack payload as a dodgeable projectile
    /// at an opponent it can acquire within reach (visible, clear sight from its body, less than 2 m of obscuring
    /// volume; spec L120, L167).
    /// </summary>
    public sealed class SummonDelivery : DeliverySpec
    {
        private float _health;
        private float _lifetimeSec;
        private SummonMobility _mobility;
        private float _orbitRadius;
        private float _routeLength;
        private float _moveSpeed = 2f;
        private float _attackRange;
        private float _attackIntervalSec = 1f;
        private float _projectileSpeed;
        private float _height = 1.2f;
        private PayloadSpec _attack;

        public override DeliveryKind Kind => DeliveryKind.Summon;

        /// <summary>Body durability ("One 40-health paper familiar"); summons are always destructible (spec L527).</summary>
        public float Health { get => _health; set => _health = Set(value); }

        /// <summary>Seconds; at most 8 (spec L128).</summary>
        public float LifetimeSec { get => _lifetimeSec; set => _lifetimeSec = Set(value); }

        public SummonMobility Mobility { get => _mobility; set => _mobility = Set(value); }
        public float OrbitRadius { get => _orbitRadius; set => _orbitRadius = Set(value); }
        public float RouteLength { get => _routeLength; set => _routeLength = Set(value); }
        public float MoveSpeed { get => _moveSpeed; set => _moveSpeed = Set(value); }

        /// <summary>Visible reach of its attacks ("fires 18 m/s shards only within 4 m visible reach").</summary>
        public float AttackRange { get => _attackRange; set => _attackRange = Set(value); }

        public float AttackIntervalSec { get => _attackIntervalSec; set => _attackIntervalSec = Set(value); }

        /// <summary>Speed of its projectiles ("18 m/s shards").</summary>
        public float ProjectileSpeed { get => _projectileSpeed; set => _projectileSpeed = Set(value); }

        /// <summary>Body center height above its ground.</summary>
        public float Height { get => _height; set => _height = Set(value); }

        /// <summary>What each attack applies; null = the form's primary payload.</summary>
        public PayloadSpec AttackPayload { get => _attack; set => _attack = Set(value); }

        internal ProjectileDelivery Shot { get; private set; }

        internal override void ApplyGlobalCaps()
        {
            _lifetimeSec = Math.Min(_lifetimeSec, GlobalRules.FieldsAndSummons.MaxLifetimeSec); // spec L128
            if (!(_lifetimeSec > 0f) || !(_health > 0f)) throw new InvalidOperationException("A summon needs a lifetime and a destructible body (spec L527).");
            _attackIntervalSec = Math.Max(Ticks.Dt, _attackIntervalSec);
            CapNested(_attack);
            if (_projectileSpeed > 0f && _attackRange > 0f)
            {
                Shot = new ProjectileDelivery { Speed = _projectileSpeed, MaxRange = _attackRange + 1f, HitsBodies = false };
                Shot.ApplyGlobalCaps();
            }
        }

        protected override void OnFreeze()
        {
            _attack?.Freeze();
            Shot?.Freeze();
        }

        public override Vec3 PredictTarget(SpellContext ctx, SpellForm form, int caster) =>
            Mobility == SummonMobility.Orbit ? ctx.Player(caster).Position : ctx.GroundPoint(caster, form.Range);

        public override void Release(SpellContext ctx, CastRecord cast)
        {
            Vec3 ground = PredictTarget(ctx, cast.Form, cast.Caster);
            Vec3 aim = AimDirection(ctx, cast);
            var summon = new SummonEntity(cast, this, ground, new Vec3(aim.X, 0f, aim.Z).NormalizedOr(Vec3.Forward));
            ctx.Spawn(summon);
            summon.Create(ctx);
        }
    }

    /// <summary>A live summon (see <see cref="SummonDelivery"/>).</summary>
    public sealed class SummonEntity : SpellEntity, ISpellBody
    {
        private readonly SummonDelivery _spec;
        private readonly Vec3 _origin;
        private readonly Vec3 _routeDir;
        private int _nextAttackTick;
        private float _phase;
        private OwnedEntity _owned;

        internal SummonEntity(CastRecord cast, SummonDelivery spec, Vec3 ground, Vec3 routeDir)
            : base(cast)
        {
            _spec = spec;
            _origin = ground;
            _routeDir = routeDir;
            Ground = ground;
        }

        public SummonDelivery Spec => _spec;

        /// <summary>Feet of the body on the ground.</summary>
        public Vec3 Ground { get; private set; }

        public OwnedEntity Owned => _owned;

        // ----- ISpellBody -----------------------------------------------------------------------------------

        public bool IsIntact => Alive && _owned != null && _owned.Active;
        public Vec3 BodyCenter => Ground + new Vec3(0f, _spec.Height, 0f);
        public float BodyRadius => 0.4f;

        public bool TakeDamage(SpellContext ctx, float amount, int attacker)
        {
            if (!IsIntact) return false;
            bool destroyed = ctx.World.DamageSummon(_owned.Id, amount, attacker);
            if (destroyed)
            {
                ctx.Emit(SpellEventKind.SummonEnded, Cast, BodyCenter, default, Id, targetPlayer: attacker);
                End(ctx);
            }

            return destroyed;
        }

        internal void Create(SpellContext ctx)
        {
            _owned = ctx.World.CreateOwnedEntity(new OwnedEntitySpec
            {
                Kind = OwnedEntityKind.Summon,
                Owner = Owner,
                DefinitionId = SpellId,
                LifetimeTicks = Ticks.FromSeconds(_spec.LifetimeSec),
                Position = Ground,
                Radius = BodyRadius,
                Health = _spec.Health,
                OriginEventId = Cast.OriginEventId,
            });
            if (_owned == null)
            {
                End(ctx);
                return;
            }

            _nextAttackTick = ctx.Tick + Ticks.FromSeconds(_spec.AttackIntervalSec);
            ctx.Emit(SpellEventKind.SummonCreated, Cast, BodyCenter, _routeDir, Id, _spec.Health, ticks: _owned.ExpireTick - ctx.Tick);
        }

        protected internal override void Step(SpellContext ctx)
        {
            if (!IsIntact)
            {
                ctx.Emit(SpellEventKind.SummonEnded, Cast, BodyCenter, default, Id);
                End(ctx);
                return;
            }

            Move(ctx);
            _owned.Position = Ground;
            if (_spec.Shot != null && ctx.Tick >= _nextAttackTick) TryAttack(ctx);
        }

        private void Move(SpellContext ctx)
        {
            float dt = Ticks.Dt;
            switch (_spec.Mobility)
            {
                case SummonMobility.Orbit:
                {
                    DuelPlayer owner = ctx.Player(Owner);
                    float r = Math.Max(0.1f, _spec.OrbitRadius);
                    _phase += _spec.MoveSpeed / r * dt;
                    Ground = owner.Position + new Vec3((float)Math.Sin(_phase) * r, 0f, (float)Math.Cos(_phase) * r);
                    break;
                }

                case SummonMobility.Route:
                {
                    // Back and forth along the fixed route; the body stops at solid geometry (no passing walls).
                    _phase += _spec.MoveSpeed * dt;
                    float length = Math.Max(0.1f, _spec.RouteLength);
                    float along = _phase % (2f * length);
                    if (along > length) along = 2f * length - along;
                    Vec3 next = _origin + _routeDir * along;
                    Vec3 from = BodyCenter;
                    Vec3 to = next + new Vec3(0f, _spec.Height, 0f);
                    if (ctx.HasClearPath(from, to)) Ground = next;
                    break;
                }
            }
        }

        private void TryAttack(SpellContext ctx)
        {
            Vec3 eye = BodyCenter;
            for (int i = 0; i < DuelWorld.PlayerCount; i++)
            {
                if (i == Owner || !ctx.IsTargetable(i)) continue;
                DuelPlayer target = ctx.Player(i);
                Vec3 chest = target.Position + new Vec3(0f, target.Motor.Height * 0.6f, 0f);
                if (Vec3.Distance(eye, chest) > _spec.AttackRange) continue;

                // Visible targets only, never through cover or steam (spec L120, L167, L527).
                if (!ctx.World.IsVisibleTo(Owner, i) || !ctx.World.HasClearSight(eye, chest)) continue;
                Vec3 dir = (chest - eye).Normalized();
                var shot = new ProjectileEntity(Cast, _spec.Shot, eye, dir * _spec.Shot.Speed, _spec.AttackPayload ?? Cast.Form.Payload);
                ctx.Spawn(shot);
                ctx.Emit(SpellEventKind.SummonAttack, Cast, eye, dir, Id, targetPlayer: i);
                _nextAttackTick = ctx.Tick + Ticks.FromSeconds(_spec.AttackIntervalSec);
                return;
            }
        }

        protected override void HashState(ref StateHasher h)
        {
            h.Add(Ground);
            h.Add(_phase);
            h.Add(_nextAttackTick);
            h.Add(_owned == null ? 0 : _owned.Id);
        }
    }
}
