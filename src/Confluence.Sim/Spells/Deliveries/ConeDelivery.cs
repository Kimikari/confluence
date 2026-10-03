using System;
using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Reactions.Grid;
using Confluence.Sim.Duel;

namespace Confluence.Sim.Spells
{
    /// <summary>
    /// A cone from the caster's focus (SPL-010 "90-degree cone; check cover per target"): every body whose hit volume
    /// lies within the reach and inside the half angle around the aim yaw is struck once, unless solid cover blocks
    /// the line from the focus to that body (spec L120: no hits through solid cover). Resolves at release, or on every
    /// channel pulse for channeled cones (SPL-056, SPL-282). Footprint deposits cover the cone on the ground (spec L163).
    /// </summary>
    public sealed class ConeDelivery : DeliverySpec
    {
        private float _halfAngleDeg = 45f;
        private float _reach;
        private bool _coverPerTarget = true;
        private bool _lagCompensated = true;
        private float _maxHeightAbove = 2.5f;

        public override DeliveryKind Kind => DeliveryKind.Cone;

        /// <summary>Half of the written cone angle ("90-degree cone" is 45).</summary>
        public float HalfAngleDeg { get => _halfAngleDeg; set => _halfAngleDeg = Set(value); }

        /// <summary>Reach in meters; 0 = the form's range.</summary>
        public float Reach { get => _reach; set => _reach = Set(value); }

        /// <summary>Solid cover between the focus and a target protects that target (SPL-010, SPL-025, SPL-090).</summary>
        public bool CoverPerTarget { get => _coverPerTarget; set => _coverPerTarget = Set(value); }

        /// <summary>A cone resolves instantly like a strike, so the opponent is rewound to the caster's view (spec L423).</summary>
        public bool LagCompensated { get => _lagCompensated; set => _lagCompensated = Set(value); }

        /// <summary>How far above the caster's feet a target may be and still be inside the cone's volume.</summary>
        public float MaxHeightAbove { get => _maxHeightAbove; set => _maxHeightAbove = Set(value); }

        internal override void ApplyGlobalCaps()
        {
            _halfAngleDeg = CMath.Clamp(_halfAngleDeg, 0f, 180f);
            _reach = Math.Max(0f, _reach);
        }

        public override void Release(SpellContext ctx, CastRecord cast)
        {
            if (!cast.Form.Castable.IsChanneled) Resolve(ctx, cast);
        }

        public override void OnChannelPulse(SpellContext ctx, CastRecord cast, int pulse) => Resolve(ctx, cast);

        public override Vec3 PredictTarget(SpellContext ctx, SpellForm form, int caster) => ctx.Player(caster).Position;

        public float ReachFor(SpellForm form) => Reach > 0f ? Reach : form.Range;

        /// <summary>The cone's ground footprint for the caster's current position and aim.</summary>
        public Footprint FootprintFor(SpellContext ctx, CastRecord cast)
        {
            Vec3 dir = AimDirection(ctx, cast);
            Vec2 flat = new Vec2(dir.X, dir.Z);
            if (flat.LengthSquared() < 1e-8f) flat = CMath.ForwardFromYaw(ctx.Player(cast.Caster).Motor.YawDeg).XZ();
            return Footprint.Cone(ctx.Player(cast.Caster).Position.XZ(), flat.Normalized(), ReachFor(cast.Form), HalfAngleDeg);
        }

        /// <summary>Whether a body lies in the cone from the apex (feet plane, horizontal angle, reach, height band).</summary>
        public bool Contains(Vec3 apexFeet, float facingYawDeg, float reach, Capsule body)
        {
            Vec3 center = body.Center;
            Vec3 flat = new Vec3(center.X - apexFeet.X, 0f, center.Z - apexFeet.Z);
            float distance = flat.Length();
            if (distance - body.Radius > reach) return false;
            float bottom = body.A.Y - body.Radius;
            if (bottom > apexFeet.Y + MaxHeightAbove || body.B.Y + body.Radius < apexFeet.Y - 0.5f) return false;
            if (distance <= body.Radius) return true;

            // The body counts when any part of its width is inside the angle.
            float angular = CMath.RadToDeg((float)Math.Asin(Math.Min(1.0, body.Radius / distance)));
            return Math.Abs(CMath.DeltaAngle(facingYawDeg, CMath.YawOf(flat))) <= HalfAngleDeg + angular;
        }

        private void Resolve(SpellContext ctx, CastRecord cast)
        {
            int caster = cast.Caster;
            Vec3 feet = ctx.Player(caster).Position;
            Vec3 focus = ctx.FocusOrigin(caster);
            Vec3 dir = AimDirection(ctx, cast);
            float yaw = new Vec3(dir.X, 0f, dir.Z).LengthSquared() > 1e-8f ? CMath.YawOf(dir) : ctx.Player(caster).Motor.YawDeg;
            float reach = ReachFor(cast.Form);
            PayloadSpec payload = cast.Form.Payload;
            ctx.Emit(SpellEventKind.ConeReleased, cast, feet, CMath.ForwardFromYaw(yaw), 0, reach);

            for (int i = 0; i < DuelWorld.PlayerCount; i++)
            {
                if (i == caster || !ctx.IsTargetable(i)) continue;
                Capsule body = ctx.TargetVolume(caster, i, LagCompensated);
                if (!Contains(feet, yaw, reach, body)) continue;
                if (CoverPerTarget && ctx.IsCovered(focus, body)) continue;
                Vec3 toTarget = body.Center - focus;
                ctx.ApplyToPlayer(cast, payload, i, PayloadHit.Direct(focus, new Vec3(toTarget.X, 0f, toTarget.Z)));
            }

            IReadOnlyList<ISpellBody> bodies = ctx.HostileBodies(caster);
            for (int i = 0; i < bodies.Count; i++)
            {
                ISpellBody b = bodies[i];
                var sphere = new Capsule(b.BodyCenter, b.BodyCenter, b.BodyRadius);
                if (Contains(feet, yaw, reach, sphere) && (!CoverPerTarget || ctx.HasClearPath(focus, b.BodyCenter)))
                {
                    ctx.ApplyToBody(cast, payload, b, CastHitKinds.Direct);
                }
            }

            ctx.EmitDeposits(cast, payload, DepositPlacement.Footprint, feet, CMath.ForwardFromYaw(yaw), FootprintFor(ctx, cast));
        }
    }
}
