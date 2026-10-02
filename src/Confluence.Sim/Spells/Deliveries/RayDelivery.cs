using System;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Sim.World;

namespace Confluence.Sim.Spells
{
    /// <summary>
    /// Bends a ray at a solid (prisms, mirrors, timed reflections). At most one reflection per ray, keeping 70% of the
    /// damage (spec L157, L527); a reflected ray is still stopped by solid geometry (spec L512).
    /// </summary>
    public interface IRayReflector
    {
        bool TryReflect(SpellContext ctx, CastRecord cast, int solidId, Vec3 point, Vec3 normal, Vec3 incoming, out Vec3 reflected);
    }

    /// <summary>Mirror reflection off solids flagged reflective (light and prism props, spec L157).</summary>
    public sealed class ReflectiveSolidReflector : IRayReflector
    {
        public static ReflectiveSolidReflector Instance { get; } = new ReflectiveSolidReflector();

        public bool TryReflect(SpellContext ctx, CastRecord cast, int solidId, Vec3 point, Vec3 normal, Vec3 incoming, out Vec3 reflected)
        {
            reflected = Vec3.Zero;
            CollisionSolid solid = ctx.Collision.Find(solidId);
            if (solid == null || (solid.Flags & MaterialFlags.Reflective) == 0 || normal.LengthSquared() == 0f) return false;
            reflected = (incoming - normal * (2f * Vec3.Dot(incoming, normal))).Normalized();
            return reflected.LengthSquared() > 0f;
        }
    }

    /// <summary>
    /// A hitscan ray or channeled beam (spec L527 "beams and rays stop at declared solid geometry"). It resolves
    /// instantly from the caster's eye along the aim with its declared width; the opponent's hit volume is rewound to
    /// the tick the caster was viewing, at most 150 ms (spec L423). A channeled form fires on each channel pulse and every
    /// pulse shares the cast's original event, so beam ticks never produce independent proc cascades (spec L126).
    /// Parallel rays (SPL-064, SPL-169) share the cast's per-target cap.
    /// </summary>
    public sealed class RayDelivery : DeliverySpec
    {
        private float _width = SpellRules.DefaultRayHalfWidth * 2f;
        private float _length;
        private bool _lagCompensated = true;
        private bool _reflects = true;
        private int _parallel = 1;
        private float _spacing;

        public override DeliveryKind Kind => DeliveryKind.Ray;

        /// <summary>Full width in meters ("A 0.4 m-wide ray").</summary>
        public float Width { get => _width; set => _width = Set(value); }

        /// <summary>Reach in meters; 0 = the form's range.</summary>
        public float Length { get => _length; set => _length = Set(value); }

        public bool LagCompensated { get => _lagCompensated; set => _lagCompensated = Set(value); }

        /// <summary>Reflective solids and reflector hooks may bounce it once (spec L527); false for "never reflect rays".</summary>
        public bool Reflects { get => _reflects; set => _reflects = Set(value); }

        /// <summary>Number of parallel rays and their spacing in meters ("two parallel telegraphed rays 1.5 m apart").</summary>
        public int Parallel { get => _parallel; set => _parallel = Set(value); }

        public float Spacing { get => _spacing; set => _spacing = Set(value); }

        internal override void ApplyGlobalCaps()
        {
            _width = Math.Max(0f, _width);
            _parallel = Math.Max(1, _parallel);
        }

        public override void Release(SpellContext ctx, CastRecord cast)
        {
            if (!cast.Form.Castable.IsChanneled) Fire(ctx, cast);
        }

        public override void OnChannelPulse(SpellContext ctx, CastRecord cast, int pulse) => Fire(ctx, cast);

        /// <summary>Resolves the ray(s) now from the caster's current eye and aim.</summary>
        public void Fire(SpellContext ctx, CastRecord cast)
        {
            Vec3 origin = ctx.AimOrigin(cast.Caster);
            Vec3 dir = AimDirection(ctx, cast);
            Vec3 right = CMath.RightFromYaw(CMath.YawOf(dir));
            float reach = Length > 0f ? Length : cast.Form.Range;
            for (int i = 0; i < Parallel; i++)
            {
                float offset = Parallel > 1 ? -Spacing * 0.5f * (Parallel - 1) + Spacing * i : 0f;
                Trace(ctx, cast, origin + right * offset, dir, reach);
            }
        }

        private void Trace(SpellContext ctx, CastRecord cast, Vec3 origin, Vec3 dir, float reach)
        {
            PayloadSpec payload = cast.Form.Payload;
            float halfWidth = Width * 0.5f;
            float scale = 1f;
            for (int bounce = 0; bounce <= SpellRules.MaxReflections && reach > 0f; bounce++)
            {
                SweepContact hit = SweepQuery.First(ctx, cast.Caster, origin, dir, reach, 0f, halfWidth, LagCompensated, true);
                ctx.Emit(bounce == 0 ? SpellEventKind.RayFired : SpellEventKind.RayReflected, cast, origin, dir, 0, hit.Distance,
                    hit.Player, hit.SolidId);
                Vec3 end = origin + dir * hit.Distance;
                if (hit.IsPlayer)
                {
                    var direct = PayloadHit.Direct(origin, dir);
                    direct.Scale = scale;
                    ctx.ApplyToPlayer(cast, payload, hit.Player, direct);
                    ctx.EmitDeposits(cast, payload, DepositPlacement.Impact, end, dir);
                    return;
                }

                if (hit.IsBody)
                {
                    ctx.ApplyToBody(cast, payload, hit.Body, CastHitKinds.Direct, scale);
                    return;
                }

                if (!hit.IsSolid)
                {
                    return;
                }

                ctx.DamageSolid(cast, payload, hit.SolidId);
                ctx.EmitDeposits(cast, payload, DepositPlacement.Impact, end, dir);
                if (!Reflects || bounce >= SpellRules.MaxReflections || !TryReflect(ctx, cast, hit, dir, out Vec3 reflected)) return;
                origin = end + hit.Normal * 0.01f;
                dir = reflected;
                reach -= hit.Distance;
                scale *= SpellRules.ReflectionRetainedFraction;
            }
        }

        private static bool TryReflect(SpellContext ctx, CastRecord cast, in SweepContact hit, Vec3 dir, out Vec3 reflected)
        {
            var reflectors = ctx.Reflectors;
            for (int i = 0; i < reflectors.Count; i++)
            {
                if (reflectors[i].TryReflect(ctx, cast, hit.SolidId, hit.Point, hit.Normal, dir, out reflected)) return true;
            }

            reflected = Vec3.Zero;
            return false;
        }
    }
}
