using System;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Duel;
using Confluence.Sim.Fields;
using Confluence.Sim.Hashing;
using Confluence.Sim.World;

namespace Confluence.Sim.Spells
{
    /// <summary>How a deployable reaches its resting place.</summary>
    public enum DeployPlacement
    {
        /// <summary>Thrown like a projectile and waits where it first touches a surface (SPL-014 "waits on first surface").</summary>
        Thrown,

        /// <summary>Placed on the ground along the aim within range ("Place a 25-health rune").</summary>
        Placed,

        /// <summary>Left at the caster's feet (SPL-197 "leave one visible needle at start").</summary>
        AtCaster,
    }

    /// <summary>
    /// A deployable: a rune, trap, orb, or anchor that rests somewhere and bursts later. It may wait for the caster's
    /// recast (manual detonation, spec L512: a visible warning precedes it and hard-control immunity is respected by
    /// the statuses it applies), trigger on a proximity entrant after a visible warning (SPL-053 "first entrant within
    /// 1 m triggers a 0.4 s warning then 1.5 m burst"), and have a destructible body (spec L527). Traps count against
    /// the owner's field limit (spec L128, L510). The burst is a splash from the burst payload (or the form's).
    /// </summary>
    public sealed class DeployableDelivery : DeliverySpec
    {
        private DeployPlacement _placement = DeployPlacement.Placed;
        private float _speed;
        private float _lifetimeSec;
        private float _health;
        private float _armDelaySec;
        private float _triggerRadius;
        private float _warningSec;
        private bool _detonateOnRecast;
        private bool _detonateOnExpire;
        private bool _triggeredByOwner;
        private bool _countsAsField = true;
        private PayloadSpec _burst;

        public override DeliveryKind Kind => DeliveryKind.Deployable;

        public DeployPlacement Placement { get => _placement; set => _placement = Set(value); }

        /// <summary>Throw speed in m/s for <see cref="DeployPlacement.Thrown"/>.</summary>
        public float Speed { get => _speed; set => _speed = Set(value); }

        /// <summary>How long it waits, seconds ("waits on first surface for up to 3 s"); fields at most 8 s (spec L128).</summary>
        public float LifetimeSec { get => _lifetimeSec; set => _lifetimeSec = Set(value); }

        /// <summary>Body durability ("25-health rune"); 0 = not destructible.</summary>
        public float Health { get => _health; set => _health = Set(value); }

        /// <summary>Delay after placement before the proximity trigger is live.</summary>
        public float ArmDelaySec { get => _armDelaySec; set => _armDelaySec = Set(value); }

        /// <summary>Proximity trigger radius in meters ("first entrant within 1 m"); 0 = no proximity trigger.</summary>
        public float TriggerRadius { get => _triggerRadius; set => _triggerRadius = Set(value); }

        /// <summary>Visible warning between the trigger and the burst ("a 0.4 s warning").</summary>
        public float WarningSec { get => _warningSec; set => _warningSec = Set(value); }

        /// <summary>The spell recast detonates it (SPL-014 "recast detonates within 2 m").</summary>
        public bool DetonateOnRecast { get => _detonateOnRecast; set => _detonateOnRecast = Set(value); }

        /// <summary>Bursts when its lifetime ends instead of fading.</summary>
        public bool DetonateOnExpire { get => _detonateOnExpire; set => _detonateOnExpire = Set(value); }

        /// <summary>The owner's own body can trip the trigger (neutral traps).</summary>
        public bool TriggeredByOwner { get => _triggeredByOwner; set => _triggeredByOwner = Set(value); }

        /// <summary>Counts against the owner's three fields (traps and placed constructs, spec L128, L510).</summary>
        public bool CountsAsField { get => _countsAsField; set => _countsAsField = Set(value); }

        /// <summary>What the burst applies (its Splash and SplashRadius); null = the form's primary payload.</summary>
        public PayloadSpec Burst { get => _burst; set => _burst = Set(value); }

        public int LifetimeTicks => Ticks.FromSeconds(LifetimeSec);

        internal override void ApplyGlobalCaps()
        {
            if (_countsAsField) _lifetimeSec = Math.Min(_lifetimeSec, GlobalRules.FieldsAndSummons.MaxLifetimeSec); // spec L128
            if (!(_lifetimeSec > 0f)) throw new InvalidOperationException("A deployable needs a positive lifetime.");
            if (_placement == DeployPlacement.Thrown && !(_speed > 0f)) throw new InvalidOperationException("A thrown deployable needs a speed.");
            CapNested(_burst);
        }

        protected override void OnFreeze() => _burst?.Freeze();

        public override Vec3 PredictTarget(SpellContext ctx, SpellForm form, int caster)
        {
            switch (Placement)
            {
                case DeployPlacement.AtCaster: return ctx.Player(caster).Position;
                case DeployPlacement.Placed: return ctx.GroundPoint(caster, form.Range);
                default: return ctx.AimPoint(caster, form.Range);
            }
        }

        public override void Release(SpellContext ctx, CastRecord cast)
        {
            Vec3 dir = AimDirection(ctx, cast);
            DeployableEntity entity;
            if (Placement == DeployPlacement.Thrown)
            {
                entity = new DeployableEntity(cast, this, ctx.AimOrigin(cast.Caster), dir * Speed);
            }
            else
            {
                Vec3 at = Placement == DeployPlacement.AtCaster ? ctx.Player(cast.Caster).Position : ctx.GroundPoint(cast.Caster, cast.Form.Range);
                entity = new DeployableEntity(cast, this, at + new Vec3(0f, SpellRules.DeployableRadius, 0f), Vec3.Zero);
            }

            ctx.Spawn(entity);
            if (Placement != DeployPlacement.Thrown) entity.Settle(ctx);
        }
    }

    /// <summary>A live deployable (see <see cref="DeployableDelivery"/>).</summary>
    public sealed class DeployableEntity : SpellEntity, ISpellBody
    {
        private readonly DeployableDelivery _spec;
        private float _travelled;
        private int _expireTick = -1;
        private int _armTick;
        private int _burstTick = -1;
        private float _health;
        private OwnedEntity _owned;

        internal DeployableEntity(CastRecord cast, DeployableDelivery spec, Vec3 position, Vec3 velocity)
            : base(cast)
        {
            _spec = spec;
            Position = position;
            Velocity = velocity;
            _health = spec.Health;
        }

        public DeployableDelivery Spec => _spec;
        public Vec3 Position { get; private set; }
        public Vec3 Velocity { get; private set; }

        /// <summary>Resting on a surface (placed, or a thrown one that touched down).</summary>
        public bool Settled => _expireTick >= 0;

        /// <summary>The trigger fired or a detonation was requested; the burst follows after the warning.</summary>
        public bool Warning => _burstTick >= 0;

        public override bool AcceptsRecast => _spec.DetonateOnRecast;

        // ----- ISpellBody -----------------------------------------------------------------------------------

        public bool IsIntact => Alive && _spec.Health > 0f && Settled;
        public Vec3 BodyCenter => Position;
        public float BodyRadius => SpellRules.DeployableRadius;

        public bool TakeDamage(SpellContext ctx, float amount, int attacker)
        {
            if (!IsIntact || !(amount > 0f)) return false;
            _health -= amount;
            if (_health > 0f) return false;
            ctx.Emit(SpellEventKind.DeployableDestroyed, Cast, Position, default, Id, targetPlayer: attacker);
            Remove(ctx);
            return true;
        }

        // ----- lifecycle ------------------------------------------------------------------------------------

        internal void Settle(SpellContext ctx)
        {
            Velocity = Vec3.Zero;
            _expireTick = ctx.Tick + _spec.LifetimeTicks;
            _armTick = ctx.Tick + Ticks.FromSeconds(_spec.ArmDelaySec);
            if (_spec.CountsAsField)
            {
                _owned = ctx.World.CreateOwnedEntity(new OwnedEntitySpec
                {
                    Kind = OwnedEntityKind.Field,
                    Owner = Owner,
                    DefinitionId = SpellId,
                    LifetimeTicks = _spec.LifetimeTicks,
                    Position = Position,
                    Radius = Math.Max(_spec.TriggerRadius, SpellRules.DeployableRadius),
                    OriginEventId = Cast.OriginEventId,
                });
                if (_owned == null)
                {
                    End(ctx);
                    return;
                }
            }

            ctx.Emit(SpellEventKind.DeployablePlaced, Cast, Position, default, Id, _spec.Health, ticks: _spec.LifetimeTicks);
        }

        protected internal override void Step(SpellContext ctx)
        {
            if (!Settled)
            {
                Fly(ctx);
                return;
            }

            if (_owned != null && !_owned.Active)
            {
                // Retired at the field limit, dispelled, or expired by the registry: dissolve without bursting.
                ctx.Emit(SpellEventKind.DeployableExpired, Cast, Position, default, Id);
                End(ctx);
                return;
            }

            int now = ctx.Tick;
            if (Warning)
            {
                if (now >= _burstTick) Detonate(ctx);
                return;
            }

            if (now == _armTick && _spec.TriggerRadius > 0f) ctx.Emit(SpellEventKind.DeployableArmed, Cast, Position, default, Id);
            if (_spec.TriggerRadius > 0f && now >= _armTick && Entrant(ctx) >= 0)
            {
                StartWarning(ctx);
                return;
            }

            if (now >= _expireTick)
            {
                if (_spec.DetonateOnExpire)
                {
                    Detonate(ctx);
                    return;
                }

                ctx.Emit(SpellEventKind.DeployableExpired, Cast, Position, default, Id);
                Remove(ctx);
            }
        }

        protected internal override void OnRecast(SpellContext ctx)
        {
            if (Settled && !Warning) StartWarning(ctx);
        }

        private void Fly(SpellContext ctx)
        {
            Vec3 delta = Velocity * Ticks.Dt;
            float length = delta.Length();
            float range = Cast.Form.Range;
            bool last = range > 0f && _travelled + length >= range;
            if (last) length = Math.Max(0f, range - _travelled);
            Vec3 dir = delta.Normalized();
            if (ctx.Collision.SphereSweep(Position, Position + dir * length, SpellRules.DeployableRadius, SolidMask.Movement, out WorldHit hit))
            {
                Position = hit.Point;
                Settle(ctx);
                return;
            }

            Position += dir * length;
            _travelled += length;
            if (last)
            {
                // No surface within range: it drops where its flight ended.
                Settle(ctx);
            }
        }

        private int Entrant(SpellContext ctx)
        {
            for (int i = 0; i < DuelWorld.PlayerCount; i++)
            {
                if ((i == Owner && !_spec.TriggeredByOwner) || !ctx.IsTargetable(i)) continue;
                if (SpellContext.DistanceToBody(Position, ctx.Player(i).HitVolume) <= _spec.TriggerRadius) return i;
            }

            return -1;
        }

        private void StartWarning(SpellContext ctx)
        {
            int warning = Ticks.FromSeconds(_spec.WarningSec);
            _burstTick = ctx.Tick + warning;
            ctx.Emit(SpellEventKind.DeployableWarning, Cast, Position, default, Id, ticks: warning);
            if (warning == 0) Detonate(ctx);
        }

        private void Detonate(SpellContext ctx)
        {
            PayloadSpec burst = _spec.Burst ?? Cast.Form.Payload;
            ctx.Emit(SpellEventKind.DeployableDetonated, Cast, Position, default, Id, burst.SplashRadius);
            ctx.ApplySplash(Cast, burst, Position, Vec3.Up);
            ctx.EmitDeposits(Cast, burst, DepositPlacement.Impact, Position, default);
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
            h.Add(_expireTick);
            h.Add(_armTick);
            h.Add(_burstTick);
            h.Add(_health);
            h.Add(_owned == null ? 0 : _owned.Id);
        }
    }
}
