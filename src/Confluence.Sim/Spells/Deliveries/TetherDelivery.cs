using System;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Hashing;

namespace Confluence.Sim.Spells
{
    public enum TetherMode
    {
        /// <summary>Pulls the struck opponent toward the caster (SPL-185 "tether pulls target up to 2 m toward caster").</summary>
        PullTarget,

        /// <summary>Attaches to solid geometry and reels the caster toward it (SPL-061 "reel caster up to 8 m over 0.5 s").</summary>
        ReelToAnchor,
    }

    /// <summary>
    /// A tether or pull line. It travels like a thin projectile until it touches the opponent or solid geometry.
    /// A pull is hostile displacement: it stops at geometry (the motor collides), is capped at 4 m per effect
    /// (spec L173), lasts at most 0.75 s and is refused during hard-control immunity, where any damage still lands
    /// (spec L116). A reel is a self dash toward the anchor, capped at 8 m (spec L124) and stopped by geometry.
    /// </summary>
    public sealed class TetherDelivery : DeliverySpec
    {
        private TetherMode _mode;
        private float _speed;
        private float _pullDistance;
        private float _reelDurationSec = 0.5f;
        private float _radius = 0.1f;

        public override DeliveryKind Kind => DeliveryKind.Tether;

        public TetherMode Mode { get => _mode; set => _mode = Set(value); }

        /// <summary>Line travel speed in m/s; 0 = instant (hitscan, lag-compensated).</summary>
        public float Speed { get => _speed; set => _speed = Set(value); }

        /// <summary>How far the target is pulled, or the caster reeled, in meters.</summary>
        public float PullDistance { get => _pullDistance; set => _pullDistance = Set(value); }

        /// <summary>Reel travel time in seconds.</summary>
        public float ReelDurationSec { get => _reelDurationSec; set => _reelDurationSec = Set(value); }

        public float Radius { get => _radius; set => _radius = Set(value); }

        internal override void ApplyGlobalCaps()
        {
            float cap = _mode == TetherMode.PullTarget ? GlobalRules.Terrain.PlayerKnockbackCap : GlobalRules.Modifiers.MaxDashDistance;
            _pullDistance = Math.Min(Math.Max(0f, _pullDistance), cap); // spec L124, L173
        }

        public override void Release(SpellContext ctx, CastRecord cast)
        {
            Vec3 origin = ctx.AimOrigin(cast.Caster);
            Vec3 dir = AimDirection(ctx, cast);
            ctx.Emit(SpellEventKind.TetherLaunched, cast, origin, dir);
            if (Speed > 0f)
            {
                ctx.Spawn(new TetherEntity(cast, this, origin, dir));
                return;
            }

            SweepContact hit = SweepQuery.First(ctx, cast.Caster, origin, dir, cast.Form.Range, 0f, Radius, true, false);
            Attach(ctx, cast, hit, dir);
        }

        /// <summary>Resolves a contact: pull the struck player, or reel the caster to the touched solid.</summary>
        internal bool Attach(SpellContext ctx, CastRecord cast, in SweepContact hit, Vec3 dir)
        {
            if (!hit.Found) return false;
            if (Mode == TetherMode.PullTarget && hit.IsPlayer)
            {
                ctx.Emit(SpellEventKind.TetherAttached, cast, hit.Point, dir, 0, PullDistance, hit.Player);
                PayloadSpec payload = cast.Form.Payload;
                ctx.ApplyToPlayer(cast, payload, hit.Player, PayloadHit.Direct(ctx.AimOrigin(cast.Caster), dir));
                if (!(payload.Push > 0f) && PullDistance > 0f)
                {
                    Vec3 toward = ctx.Player(cast.Caster).Position - ctx.Player(hit.Player).Position;
                    float distance = Math.Min(PullDistance, Math.Max(0f, toward.LengthXZ() - 1f));
                    ctx.World.ApplyKnockback(hit.Player, cast.Caster, toward, distance, cast.SpellId);
                }

                return true;
            }

            if (Mode == TetherMode.ReelToAnchor && hit.IsSolid)
            {
                Vec3 from = ctx.Player(cast.Caster).Position;
                Vec3 toward = hit.Point - from;
                float distance = Math.Min(PullDistance, Math.Max(0f, toward.LengthXZ() - 0.5f));
                int ticks = Math.Max(1, Ticks.FromSeconds(ReelDurationSec));
                ctx.Emit(SpellEventKind.TetherAttached, cast, hit.Point, dir, 0, distance, solidId: hit.SolidId);
                if (distance > 0f) ctx.World.TryDash(cast.Caster, toward, distance, ticks, cast.SpellId);
                return true;
            }

            return false;
        }
    }

    /// <summary>A travelling tether line (see <see cref="TetherDelivery"/>).</summary>
    public sealed class TetherEntity : SpellEntity
    {
        private readonly TetherDelivery _spec;
        private readonly Vec3 _dir;
        private float _travelled;

        internal TetherEntity(CastRecord cast, TetherDelivery spec, Vec3 origin, Vec3 dir)
            : base(cast)
        {
            _spec = spec;
            _dir = dir;
            Position = origin;
        }

        public Vec3 Position { get; private set; }

        protected internal override void Step(SpellContext ctx)
        {
            float step = _spec.Speed * Ticks.Dt;
            float range = Cast.Form.Range;
            bool last = _travelled + step >= range;
            if (last) step = Math.Max(0f, range - _travelled);
            SweepContact hit = SweepQuery.First(ctx, Owner, Position, _dir, step, _spec.Radius, _spec.Radius, false, false);
            if (hit.Found)
            {
                Position = hit.Point;
                _spec.Attach(ctx, Cast, hit, _dir);
                ctx.Emit(SpellEventKind.TetherEnded, Cast, Position, _dir, Id);
                End(ctx);
                return;
            }

            Position += _dir * step;
            _travelled += step;
            if (last)
            {
                ctx.Emit(SpellEventKind.TetherEnded, Cast, Position, _dir, Id);
                End(ctx);
            }
        }

        protected override void HashState(ref StateHasher h)
        {
            h.Add(Position);
            h.Add(_travelled);
        }
    }
}
