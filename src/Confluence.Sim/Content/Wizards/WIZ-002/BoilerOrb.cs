using System;
using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Duel;
using Confluence.Sim.Fields;
using Confluence.Sim.Hashing;
using Confluence.Sim.Spells;
using Confluence.Sim.World;
using Confluence.Sim.World.Materials;

namespace Confluence.Sim.Content.Wizards.Wiz002
{
    /// <summary>
    /// SPL-014 Boiler Orb (WIZ-002, reaction setup): "18 m/s orb waits on first surface for up to 3 s; recast detonates
    /// within 2 m." Damage "32 explosion", thermal. World interaction: "Heat 3; water reaction consumes orb heat and
    /// creates steam, reducing explosion to 16." Counterplay: "Shoot the 25-health orb or exploit premature steaming."
    /// Mana 22, cooldown 10 s, 0.45 s windup, range 22 (the throw's maximum travel) from the record (spec L525).
    /// <list type="bullet">
    /// <item>The thrown orb passes players and sticks where it first touches a solid within 22 m. It only ever waits on a
    /// surface ("waits on first surface"): with no solid within its range it falls straight down from the end of its
    /// flight to the first surface below (at its throw speed) and waits there; with none within 30 m it fades without
    /// waiting. Resting, it is a 25-health destructible body (spec L527): destroyed, it never explodes.</item>
    /// <item>The orb carries Heat 3. When it rests on a cell with liquid water (placed there, or water arrives later) the
    /// reaction consumes that heat into steam at once — the reaction world forms the steam from the orb's heat and the
    /// cell's water (spec L144) — and the orb's explosion drops to 16 ("premature steaming").</item>
    /// <item>The spell recast (spec L94, one recast in the orb's lifetime, no listed final warning, L512) detonates it:
    /// thermal area damage within 2 m (the caster is not hit; not guarded), the record's primary damage with the cast's
    /// mastery result (spec L480); a dry orb releases its Heat 3 into the explosion cell. Unrecast, it fades after 3 s.</item>
    /// <item>The orb is a deployable, not a field or trap (record tags), so it uses no field allocation.</item>
    /// </list>
    /// </summary>
    [ContentBehavior("SPL-014")]
    public sealed class BoilerOrb : ISpellBehavior
    {
        public static readonly ProseParameter Speed = ProseParameter.Of("orb_speed", 18, "18 m/s orb");
        public static readonly ProseParameter Wait = ProseParameter.Of("wait", 3, "waits on first surface for up to 3 s");
        public static readonly ProseParameter Radius = ProseParameter.Of("explosion_radius", 2, "recast detonates within 2 m");
        public static readonly ProseParameter Explosion = ProseParameter.Of("explosion", 32, "32 explosion");
        public static readonly ProseParameter Heat = ProseParameter.Of("orb_heat", 3, "Heat 3");
        public static readonly ProseParameter Steamed = ProseParameter.Of("steamed_explosion", 16, "reducing explosion to 16");
        public static readonly ProseParameter Health = ProseParameter.Of("orb_health", 25, "Shoot the 25-health orb");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Speed, Wait, Radius, Explosion, Heat, Steamed, Health };

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Build(SpellFormBuilder form)
        {
            PayloadSpec payload = form.Payload;
            payload.Splash = Explosion.Float;
            payload.SplashRadius = Radius.Float;
            payload.GuardEligible = false;
            form.Delivery = new BoilerOrbDelivery
            {
                Speed = Speed.Float,
                LifetimeSec = Wait.Float,
                Health = Health.Float,
                ExplosionRadius = Radius.Float,
                Heat = (int)Heat.Value,
                SteamedExplosion = Steamed.Float,
                DetonateOnRecast = true,
            };
            form.Recast = RecastRule.Default; // once while the orb waits (spec L94)
        }
    }

    /// <summary>The Boiler Orb throw and its AUG-315 jet and AUG-316 mine variants.</summary>
    public sealed class BoilerOrbDelivery : DeliverySpec
    {
        private float _speed;
        private float _lifetimeSec;
        private float _health;
        private float _explosionRadius;
        private int _heat;
        private float _steamedExplosion;
        private bool _detonateOnRecast;
        private float _jetLength;
        private float _jetWidth;
        private float _jetLifetimeSec;
        private bool _mine;
        private float _triggerRadius;
        private float _warningSec;

        public override DeliveryKind Kind => DeliveryKind.Deployable;

        public float Speed { get => _speed; set => _speed = Set(value); }

        /// <summary>How long the orb waits once it rests ("up to 3 s").</summary>
        public float LifetimeSec { get => _lifetimeSec; set => _lifetimeSec = Set(value); }

        public float Health { get => _health; set => _health = Set(value); }
        public float ExplosionRadius { get => _explosionRadius; set => _explosionRadius = Set(value); }

        /// <summary>The orb's heat strength (consumed by water, released by a dry explosion).</summary>
        public int Heat { get => _heat; set => _heat = Set(value); }

        /// <summary>Explosion after the water reaction consumed the orb's heat ("reducing explosion to 16").</summary>
        public float SteamedExplosion { get => _steamedExplosion; set => _steamedExplosion = Set(value); }

        public bool DetonateOnRecast { get => _detonateOnRecast; set => _detonateOnRecast = Set(value); }

        /// <summary>AUG-315: the steam reaction becomes a jet of this length along the throw direction (0 = a steam puff).</summary>
        public float JetLength { get => _jetLength; set => _jetLength = Set(value); }

        public float JetWidth { get => _jetWidth; set => _jetWidth = Set(value); }
        public float JetLifetimeSec { get => _jetLifetimeSec; set => _jetLifetimeSec = Set(value); }

        /// <summary>AUG-316: the orb is a proximity mine (a trap: one field allocation, spec L510).</summary>
        public bool Mine { get => _mine; set => _mine = Set(value); }

        public float TriggerRadius { get => _triggerRadius; set => _triggerRadius = Set(value); }
        public float WarningSec { get => _warningSec; set => _warningSec = Set(value); }

        public bool CountsAsField => Mine;

        internal override void ApplyGlobalCaps()
        {
            if (Mine) _lifetimeSec = Math.Min(_lifetimeSec, GlobalRules.FieldsAndSummons.MaxLifetimeSec); // spec L128
            _jetLifetimeSec = Math.Min(_jetLifetimeSec, GlobalRules.FieldsAndSummons.MaxLifetimeSec);
            _heat = Math.Max(0, Math.Min(_heat, GlobalRules.World.MaxUnitsPerCell));
            if (!(_speed > 0f) || !(_lifetimeSec > 0f)) throw new InvalidOperationException("Boiler Orb needs a speed and a lifetime.");
        }

        public override Vec3 PredictTarget(SpellContext ctx, SpellForm form, int caster) => ctx.AimPoint(caster, form.Range);

        public override void Release(SpellContext ctx, CastRecord cast)
        {
            Vec3 dir = AimDirection(ctx, cast);
            var orb = ctx.Spawn(new BoilerOrbEntity(cast, this, ctx.AimOrigin(cast.Caster), dir * Speed,
                KitSupport.Flat(dir, CMath.ForwardFromYaw(ctx.Player(cast.Caster).Motor.YawDeg))));
            ctx.Emit(SpellEventKind.ProjectileLaunched, cast, orb.Position, dir, orb.Id, Speed);
        }
    }

    /// <summary>A live Boiler Orb (see <see cref="BoilerOrb"/>).</summary>
    public sealed class BoilerOrbEntity : SpellEntity, ISpellBody, IAwaitsRecastTarget
    {
        public const float OrbRadius = SpellRules.DeployableRadius;

        private readonly BoilerOrbDelivery _spec;
        private readonly Vec3 _throwDir;
        private float _travelled;
        private float _fallen;
        private bool _falling;
        private int _expireTick = -1;
        private int _burstTick = -1;
        private float _health;
        private int _heat;
        private bool _steamed;
        private Vec3 _ground;
        private OwnedEntity _owned;

        internal BoilerOrbEntity(CastRecord cast, BoilerOrbDelivery spec, Vec3 position, Vec3 velocity, Vec3 throwDir)
            : base(cast)
        {
            _spec = spec;
            Position = position;
            Velocity = velocity;
            _throwDir = throwDir;
            _health = spec.Health;
            _heat = spec.Heat;
        }

        public BoilerOrbDelivery Spec => _spec;
        public Vec3 Position { get; private set; }
        public Vec3 Velocity { get; private set; }
        public bool Settled => _expireTick >= 0;

        /// <summary>No surface was within range: the orb drops to the first surface below the end of its flight.</summary>
        public bool Falling => _falling;

        /// <summary>The farthest the orb drops looking for a surface after its range ends.</summary>
        public const float MaxFall = 30f;

        /// <summary>The water reaction consumed the orb's heat (premature steaming).</summary>
        public bool IsSteamed => _steamed;

        public int Heat => _heat;
        public float Health => _health;
        public bool Warning => _burstTick >= 0;

        /// <summary>The explosion it would deal now (before the target's mitigation and the mastery result).</summary>
        public float ExplosionDamage => _steamed ? Math.Min(Cast.Form.Payload.Splash, _spec.SteamedExplosion) : Cast.Form.Payload.Splash;

        /// <summary>The recast detonates a waiting orb only; before it settles there is nothing to detonate (spec L94).</summary>
        public override bool AcceptsRecast => _spec.DetonateOnRecast && Settled && !Warning;

        /// <summary>In flight (or falling to a surface) the orb is the recast's target on its way: an early press does nothing.</summary>
        public bool AwaitingRecastTarget => _spec.DetonateOnRecast && !Settled;

        // ----- ISpellBody -----------------------------------------------------------------------------------

        public bool IsIntact => Alive && Settled && _health > 0f;
        public Vec3 BodyCenter => Position;
        public float BodyRadius => OrbRadius;

        public bool TakeDamage(SpellContext ctx, float amount, int attacker)
        {
            if (!IsIntact || !(amount > 0f)) return false;
            _health -= amount;
            if (_health > 0f) return false;
            ctx.Emit(SpellEventKind.DeployableDestroyed, Cast, Position, default, Id, targetPlayer: attacker); // shot down: no explosion
            Remove(ctx);
            return true;
        }

        // ----- lifecycle ------------------------------------------------------------------------------------

        protected internal override void Step(SpellContext ctx)
        {
            if (!Settled)
            {
                Fly(ctx);
                if (!Settled) return;
            }

            if (_owned != null && !_owned.Active)
            {
                ctx.Emit(SpellEventKind.DeployableExpired, Cast, Position, default, Id); // retired at the field limit
                End(ctx);
                return;
            }

            if (!_steamed && _heat > 0 && TouchesWater(ctx)) Steam(ctx);

            int now = ctx.Tick;
            if (Warning)
            {
                if (now >= _burstTick) Detonate(ctx);
                return;
            }

            if (_spec.Mine && Entrant(ctx) >= 0)
            {
                int warning = Ticks.FromSeconds(_spec.WarningSec);
                _burstTick = now + warning;
                ctx.Emit(SpellEventKind.DeployableWarning, Cast, Position, default, Id, ticks: warning);
                if (warning == 0) Detonate(ctx);
                return;
            }

            if (now >= _expireTick)
            {
                ctx.Emit(SpellEventKind.DeployableExpired, Cast, Position, default, Id);
                Remove(ctx);
            }
        }

        protected internal override void OnRecast(SpellContext ctx)
        {
            // "recast detonates": no final warning is listed (spec L512).
            if (Settled && !Warning && _spec.DetonateOnRecast) Detonate(ctx);
        }

        private void Fly(SpellContext ctx)
        {
            if (_falling)
            {
                Fall(ctx);
                return;
            }

            Vec3 delta = Velocity * Ticks.Dt;
            float length = delta.Length();
            float range = Cast.Form.Range;
            bool last = range > 0f && _travelled + length >= range;
            if (last) length = Math.Max(0f, range - _travelled);
            Vec3 dir = delta.Normalized();
            if (ctx.Collision.SphereSweep(Position, Position + dir * length, OrbRadius, SolidMask.Movement, out WorldHit hit))
            {
                Position = hit.Point;
                Settle(ctx);
                return;
            }

            Position += dir * length;
            _travelled += length;
            if (last)
            {
                // No surface within range: it never waits in mid-air ("waits on first surface"); it drops instead.
                _falling = true;
                Velocity = Vec3.Down * _spec.Speed;
            }
        }

        private void Fall(SpellContext ctx)
        {
            float length = Math.Min(_spec.Speed * Ticks.Dt, MaxFall - _fallen);
            if (ctx.Collision.SphereSweep(Position, Position + Vec3.Down * length, OrbRadius, SolidMask.Movement, out WorldHit hit))
            {
                Position = hit.Point;
                _falling = false;
                Settle(ctx);
                return;
            }

            Position += Vec3.Down * length;
            _fallen += length;
            if (_fallen >= MaxFall - 1e-4f)
            {
                ctx.Emit(SpellEventKind.DeployableExpired, Cast, Position, default, Id); // nothing below: no surface to wait on
                End(ctx);
            }
        }

        private void Settle(SpellContext ctx)
        {
            Velocity = Vec3.Zero;
            _expireTick = ctx.Tick + Ticks.FromSeconds(_spec.LifetimeSec);
            _ground = KitSupport.GroundBelow(ctx, Position);
            if (_spec.CountsAsField)
            {
                _owned = ctx.World.CreateOwnedEntity(new OwnedEntitySpec
                {
                    Kind = OwnedEntityKind.Field,
                    Owner = Owner,
                    DefinitionId = SpellId,
                    LifetimeTicks = Ticks.FromSeconds(_spec.LifetimeSec),
                    Position = Position,
                    Radius = Math.Max(_spec.TriggerRadius, OrbRadius),
                    OriginEventId = Cast.OriginEventId,
                });
                if (_owned == null)
                {
                    End(ctx);
                    return;
                }
            }

            ctx.Emit(SpellEventKind.DeployablePlaced, Cast, Position, default, Id, _health, ticks: Ticks.FromSeconds(_spec.LifetimeSec));
        }

        // The orb rests on (or within a step of) a cell holding liquid water.
        private bool TouchesWater(SpellContext ctx) =>
            Position.Y - _ground.Y <= OrbRadius + KitSupport.StandHeight + 0.05f && KitSupport.IsWet(ctx, _ground);

        /// <summary>"water reaction consumes orb heat and creates steam, reducing explosion to 16".</summary>
        private void Steam(SpellContext ctx)
        {
            ReactionMaterials materials = KitSupport.Materials(ctx);
            if (materials == null) return;
            int heat = _heat;
            _heat = 0;
            _steamed = true;
            materials.RecordSource(Cast.OriginEventId, Cast.SpellId);
            if (_spec.JetLength > 0f)
            {
                // AUG-315: the reaction becomes a steam jet along the throw direction; it still consumes the orb's heat
                // and (one per heat unit) the cell's water.
                int water = Math.Min(heat, KitSupport.UnitsAt(ctx, _ground).Water);
                Footprint cell = Footprint.Point(_ground.XZ());
                materials.RemoveUnits(new UnitRemoval
                {
                    Footprint = cell,
                    Materials = new[] { Material.Water },
                    MaxPerCell = water,
                    MaxTotal = water,
                    Owner = Owner,
                    SourceEventId = Cast.OriginEventId,
                });
                Vec2 from = _ground.XZ();
                Vec2 to = from + _throwDir.XZ().Normalized() * _spec.JetLength;
                materials.CreateVolume(new VolumeSpec
                {
                    Kind = VolumeKind.Steam,
                    Footprint = Footprint.Lane(from, to, _spec.JetWidth),
                    BaseY = _ground.Y,
                    LifetimeTicks = Ticks.FromSeconds(_spec.JetLifetimeSec),
                    Owner = Owner,
                    SourceEventId = Cast.OriginEventId,
                    SourceWater = water,
                });
                ctx.Emit(SpellEventKind.Steamed, Cast, _ground, _throwDir, Id, _spec.JetLength, ticks: Ticks.FromSeconds(_spec.JetLifetimeSec));
                return;
            }

            // The orb's heat enters the wet cell, where the reactions stage turns heat and water into steam (spec L144).
            KitSupport.Deposit(ctx, Cast, DepositKind.Heat, heat, _ground, Footprint.Point(_ground.XZ()));
            ctx.Emit(SpellEventKind.Steamed, Cast, _ground, default, Id, heat);
        }

        private int Entrant(SpellContext ctx)
        {
            for (int i = 0; i < DuelWorld.PlayerCount; i++)
            {
                if (i == Owner || !ctx.IsTargetable(i)) continue;
                if (SpellContext.DistanceToBody(Position, ctx.Player(i).HitVolume) <= _spec.TriggerRadius) return i;
            }

            return -1;
        }

        private void Detonate(SpellContext ctx)
        {
            PayloadSpec payload = Cast.Form.Payload;
            float full = payload.Splash;
            float scale = full > 0f ? ExplosionDamage / full * KitSupport.PrimaryBurstMastery(Cast) : 0f; // the primary payload
            ctx.Emit(SpellEventKind.DeployableDetonated, Cast, Position, default, Id, _spec.ExplosionRadius);
            KitSupport.Burst(ctx, Cast, payload, Position, _spec.ExplosionRadius, CastHitKinds.Splash, scale, Position);
            if (_heat > 0)
            {
                // A dry orb releases its Heat 3 where it explodes (instant residue, spec L163).
                KitSupport.Deposit(ctx, Cast, DepositKind.Heat, _heat, _ground, Footprint.Point(_ground.XZ()));
                _heat = 0;
            }

            Remove(ctx);
        }

        private void Remove(SpellContext ctx)
        {
            if (_owned != null && _owned.Active) ctx.World.Owned.Remove(_owned.Id);
            End(ctx);
        }

        protected override void HashState(ref StateHasher h)
        {
            h.Add(Position);
            h.Add(Velocity);
            h.Add(_travelled);
            h.Add(_fallen);
            h.Add(_falling);
            h.Add(_expireTick);
            h.Add(_burstTick);
            h.Add(_health);
            h.Add(_heat);
            h.Add(_steamed);
            h.Add(_owned == null ? 0 : _owned.Id);
        }
    }
}
