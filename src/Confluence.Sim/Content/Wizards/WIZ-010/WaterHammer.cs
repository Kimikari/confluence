using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Time;
using Confluence.Sim.Casting;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Spells;
using Confluence.Sim.World;

namespace Confluence.Sim.Content.Wizards.HalenTide
{
    /// <summary>
    /// SPL-074 Water Hammer (WIZ-010, close burst): "A 0.8 m-wide thrust; hit pushes 2 m; a miss leaves 0.3 s recovery."
    /// Damage "48", physical; "Water 1 sprays only the impact spot". Mana 20, cooldown 8 s, 0.4 s windup and the 3 m reach
    /// (the record's range) come from the record (spec L525).
    /// <list type="bullet">
    /// <item>A straight swept thrust (spec L102: no snap or tracking), stopped by solid geometry; an offensive melee spell,
    /// so a frontal guard or parry reduces it and its push (spec L104, L535; counterplay "Parry").</item>
    /// <item>"a miss leaves 0.3 s recovery": the 0.4 s windup makes it a heavy cast (0.35 s recovery, D-047); a thrust
    /// that strikes nobody adds 0.3 s to that recovery (spec L92: a record may only lengthen recovery), the punishable
    /// window of its counterplay "punish recovery".</item>
    /// <item>"Water 1 sprays only the impact spot": one cell of water under the struck target, or at the solid the thrust
    /// strikes, never an area.</item>
    /// </list>
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class WaterHammer : ISpellBehavior
    {
        public const string Id = "SPL-074";

        public static readonly ProseParameter Width = ProseParameter.Of("width", 0.8, "A 0.8 m-wide thrust");
        public static readonly ProseParameter Push = ProseParameter.Of("push", 2, "hit pushes 2 m");
        public static readonly ProseParameter MissRecovery = ProseParameter.Of("miss_recovery", 0.3, "a miss leaves 0.3 s recovery");
        public static readonly ProseParameter Direct = ProseParameter.Of("direct", 48, "48");
        public static readonly ProseParameter SprayWater = ProseParameter.Of("spray_water", 1, "Water 1 sprays only the impact spot");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Width, Push, MissRecovery, Direct, SprayWater };

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Build(SpellFormBuilder form)
        {
            form.Delivery = new ArcDelivery { ArcHalfDeg = 0f, Width = Width.Float, Reach = form.Range };

            PayloadSpec payload = form.Payload;
            payload.Direct = Direct.Float;
            payload.Push = Push.Float;
            payload.PushDirection = PushDirection.AlongDelivery;
            form.Script = new WaterHammerScript { MissRecoverySec = MissRecovery.Float, SprayStrength = (int)SprayWater.Value };
        }
    }

    /// <summary>
    /// SPL-074's record-specific rules: the impact-spot spray (or AUG-435's puddle) and the miss recovery. One instance per
    /// resolved form (transformations edit it while the form resolves).
    /// </summary>
    public sealed class WaterHammerScript : SpellScript
    {
        public float MissRecoverySec { get; set; }

        /// <summary>Water at the impact spot (SPL-074 "Water 1 sprays only the impact spot").</summary>
        public int SprayStrength { get; set; } = 1;

        /// <summary>AUG-435: a hit leaves a puddle of this radius instead of the one-cell spray; 0 = the spot only.</summary>
        public float PuddleRadius { get; set; }

        public float PuddleLifetimeSec { get; set; }

        public int PuddleStrength { get; set; } = 1;

        public override void OnPlayerHit(SpellContext ctx, CastRecord cast, int target, DamageEvent hit)
        {
            if (hit.Kind != DamageKind.Direct) return;
            Vec3 spot = ctx.Player(target).Position;
            if (PuddleRadius > 0f)
            {
                HalenRules.DepositWater(ctx, cast, spot, PuddleStrength, PuddleRadius, PuddleLifetimeSec);
            }
            else
            {
                HalenRules.DepositWater(ctx, cast, spot, SprayStrength, 0f, HalenRules.ImpactWaterLifetimeSec);
            }
        }

        public override void OnReleased(SpellContext ctx, CastRecord cast)
        {
            int opponent = 1 - cast.Caster;
            if (cast.HitKindsOn(opponent) != CastHitKinds.None) return;

            // The thrust struck a solid: its spray lands at that impact spot.
            if (cast.Form.Delivery is ArcDelivery arc)
            {
                Vec3 origin = ctx.FocusOrigin(cast.Caster);
                Vec3 dir = cast.Form.AimLock == AimLock.AtCommit ? cast.CommitDirection : ctx.AimDirection(cast.Caster);
                float reach = arc.Reach > 0f ? arc.Reach : cast.Form.Range;
                if (ctx.Collision.SegmentCast(origin, origin + dir * reach, SolidMask.Movement, out WorldHit wall))
                {
                    HalenRules.DepositWater(ctx, cast, wall.Point - dir * 0.05f, SprayStrength, 0f, HalenRules.ImpactWaterLifetimeSec);
                }
            }

            // "a miss leaves 0.3 s recovery" (spec L92: recovery can only grow).
            PlayerCaster caster = ctx.Player(cast.Caster).Caster;
            if (MissRecoverySec > 0f && caster != null && caster.Machine.ExtendRecovery(cast.CastId, Ticks.FromSeconds(MissRecoverySec), ctx.Tick))
            {
                HalenRules.Emit(ctx, TideEventKind.MissRecovery, cast, ctx.Player(cast.Caster).Position, MissRecoverySec);
            }
        }
    }
}
