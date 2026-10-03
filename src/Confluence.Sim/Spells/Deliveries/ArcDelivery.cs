using System;
using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Sim.Duel;
using Confluence.Sim.World;

namespace Confluence.Sim.Spells
{
    /// <summary>
    /// A spell melee strike (slash, thrust, whip, kick): a swept volume of the declared width moving across its arc
    /// around the committed aim, like common melee (spec L102: "attacks use a swept strike volume matching their
    /// visible movement"; no snap or tracking). Each sample line stops at the first solid, so a strike cannot pass
    /// solid geometry (SPL-178); each target is struck once per cast. An arc of 0 is a straight thrust.
    /// </summary>
    public sealed class ArcDelivery : DeliverySpec
    {
        private float _arcHalfDeg;
        private float _reach;
        private float _width = 0.5f;
        private bool _vertical;

        public override DeliveryKind Kind => DeliveryKind.Arc;

        /// <summary>Half of the swept angle ("A 180-degree slash" is 90; a thrust is 0).</summary>
        public float ArcHalfDeg { get => _arcHalfDeg; set => _arcHalfDeg = Set(value); }

        /// <summary>Reach from the focus in meters; 0 = the form's range.</summary>
        public float Reach { get => _reach; set => _reach = Set(value); }

        /// <summary>Strike width in meters ("a 0.5 m-wide arc", "Wide 1 m thrust").</summary>
        public float Width { get => _width; set => _width = Set(value); }

        /// <summary>The sweep runs in pitch instead of yaw (SPL-241 "vertical can hit airborne targets").</summary>
        public bool Vertical { get => _vertical; set => _vertical = Set(value); }

        internal override void ApplyGlobalCaps()
        {
            _arcHalfDeg = CMath.Clamp(_arcHalfDeg, 0f, 180f);
            _width = Math.Max(0f, _width);
        }

        public override void Release(SpellContext ctx, CastRecord cast)
        {
            int caster = cast.Caster;
            Vec3 origin = ctx.FocusOrigin(caster);
            Vec3 aim = AimDirection(ctx, cast);
            float yaw = CMath.YawOf(aim);
            float pitch = CMath.PitchOf(aim);
            float reach = Reach > 0f ? Reach : cast.Form.Range;
            float halfWidth = Width * 0.5f;
            int samples = ArcHalfDeg > 0f ? SpellRules.ArcSamples : 1;
            PayloadSpec payload = cast.Form.Payload;
            var struck = new bool[DuelWorld.PlayerCount];
            var struckBodies = new List<ISpellBody>();
            ctx.Emit(SpellEventKind.ArcReleased, cast, origin, aim, 0, reach);

            for (int s = 0; s < samples; s++)
            {
                float u = samples > 1 ? s / (float)(samples - 1) : 0.5f;
                float offset = -ArcHalfDeg + 2f * ArcHalfDeg * u;
                Vec3 dir = Vertical ? CMath.ForwardFromYawPitch(yaw, CMath.Clamp(pitch + offset, -89f, 89f)) : CMath.ForwardFromYawPitch(yaw + offset, pitch);
                float length = reach;
                if (ctx.Collision.SegmentCast(origin, origin + dir * reach, SolidMask.Movement, out WorldHit wall)) length = wall.Distance;

                for (int i = 0; i < DuelWorld.PlayerCount; i++)
                {
                    if (i == caster || struck[i] || !ctx.IsTargetable(i)) continue;
                    Capsule body = ctx.TargetVolume(caster, i, true);
                    float d2 = Intersections.SegmentSegmentClosest(origin, origin + dir * length, body.A, body.B, out _, out _, out _, out _);
                    float r = body.Radius + halfWidth;
                    if (d2 > r * r) continue;
                    struck[i] = true; // one strike damages its target once (spec L102)
                    ctx.ApplyToPlayer(cast, payload, i, PayloadHit.Direct(origin, dir));
                }

                IReadOnlyList<ISpellBody> bodies = ctx.HostileBodies(caster);
                for (int i = 0; i < bodies.Count; i++)
                {
                    ISpellBody b = bodies[i];
                    if (struckBodies.Contains(b)) continue;
                    Vec3 closest = Intersections.ClosestPointOnSegment(b.BodyCenter, origin, origin + dir * length);
                    if (Vec3.Distance(closest, b.BodyCenter) <= b.BodyRadius + halfWidth)
                    {
                        struckBodies.Add(b);
                        ctx.ApplyToBody(cast, payload, b, CastHitKinds.Direct);
                    }
                }
            }
        }
    }
}
