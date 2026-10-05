using System;
using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Sim.Spells;

namespace Confluence.Sim.Content.Wizards.Wiz002
{
    /// <summary>
    /// AUG-309 Steam Mantle (Gold, SPL-011, no exclusive group). Rank 1: "Casting while Wet consumes Wet and creates 1.5 m
    /// steam puff for 1 s." Rank 2: "Puff radius 2 m for 1.5 s." Drawback: "Projectile reduction 12% becomes 8%; steam
    /// also hides caster's view." Counterplay: "Use sound or close range."
    /// <list type="bullet">
    /// <item>Released while the caster is Wet, the haze consumes the Wet and raises a neutral steam cloud of 1.5 m radius
    /// for 1 s (rank 2: 2 m, 1.5 s) around the caster's feet. It is an ordinary steam volume: it blocks acquisition along
    /// two meters of path both ways, the caster's own view included (spec L167), never mutes sound, and counts as one of
    /// the caster's fields (spec L670).</item>
    /// <item>The shimmer reduces projectile damage by 8% instead of 12%.</item>
    /// </list>
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class SteamMantle : ISpellTransform
    {
        public const string Id = "AUG-309";

        public static readonly ProseParameter Rank1Radius = ProseParameter.Of("rank1_puff_radius", 1.5, "creates 1.5 m steam puff");
        public static readonly ProseParameter Rank1Lifetime = ProseParameter.Of("rank1_puff_lifetime", 1, "steam puff for 1 s");
        public static readonly ProseParameter Rank2Radius = ProseParameter.Of("rank2_puff_radius", 2, "Puff radius 2 m");
        public static readonly ProseParameter Rank2Lifetime = ProseParameter.Of("rank2_puff_lifetime", 1.5, "2 m for 1.5 s");
        public static readonly ProseParameter Reduction = ProseParameter.Percent("projectile_reduction", 8, "Projectile reduction 12% becomes 8%");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Rank1Radius, Rank1Lifetime, Rank2Radius, Rank2Lifetime, Reduction };

        public TransformStage Stage => TransformStage.Payload;

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Apply(SpellFormBuilder form, int rank)
        {
            if (!(form.Delivery is BuffDelivery buff) || !(form.Script is HeatHazeScript script))
            {
                throw new InvalidOperationException(Id + " transforms Heat Haze (SPL-011).");
            }

            buff.ProjectileDamageReduction = Reduction.Float;
            form.Script = rank >= 2
                ? script.WithMantle(Rank2Radius.Float, Rank2Lifetime.Float)
                : script.WithMantle(Rank1Radius.Float, Rank1Lifetime.Float);
        }
    }

    /// <summary>
    /// AUG-310 Haze Reservoir (Prismatic, SPL-011, no exclusive group). Rank 1: "First 20 absorbed damage readies one Heat
    /// 1 vent; recast applies it to 2 m forward cone, no damage." Rank 2: "Vent cone 3 m; maximum Heat 1 unchanged."
    /// Drawback: "Buff 4 s becomes 3 s; no additional shield/reduction." Counterplay: "Wait out mantle or dodge vent."
    /// <list type="bullet">
    /// <item>While the 3 s shimmer lasts, the first 20 damage it absorbs charges the reservoir: the damage the caster
    /// takes (shield and health) from hostile hits the shimmer's projectile reduction acts on (see
    /// <see cref="HazeReservoirEntity"/>). Unmitigated damage (melee, bursts, terrain, statuses) and self damage never
    /// charge it. The reservoir absorbs nothing itself (no extra shield or reduction).</item>
    /// <item>Once charged, the spell recast (spec L94, during the shimmer) vents Heat 1 over a 2 m forward cone (rank 2:
    /// 3 m) along the caster's current aim; the cone angle is unstated and uses the cone default of 90 degrees. The vent
    /// deals no damage; its heat dries, steams or melts by the usual reactions. One vent per cast (one recast, spec
    /// L94); before the reservoir is charged the cast has no recast target, so a press is refused and spends nothing.</item>
    /// <item>Structural delta: the spell gains the cone tag.</item>
    /// </list>
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class HazeReservoir : ISpellTransform
    {
        public const string Id = "AUG-310";

        public const float VentHalfAngleDeg = 45f;

        public static readonly ProseParameter Threshold = ProseParameter.Of("absorbed_threshold", 20, "First 20 absorbed damage");
        public static readonly ProseParameter VentHeat = ProseParameter.Of("vent_heat", 1, "readies one Heat 1 vent");
        public static readonly ProseParameter Rank1Cone = ProseParameter.Of("rank1_vent_cone", 2, "2 m forward cone");
        public static readonly ProseParameter Rank2Cone = ProseParameter.Of("rank2_vent_cone", 3, "Vent cone 3 m");
        public static readonly ProseParameter Duration = ProseParameter.Of("buff_duration", 3, "Buff 4 s becomes 3 s");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Threshold, VentHeat, Rank1Cone, Rank2Cone, Duration };

        static HazeReservoir() => Wiz002StructuralDeltas.EnsureRegistered();

        public HazeReservoir() => Wiz002StructuralDeltas.EnsureRegistered();

        public TransformStage Stage => TransformStage.Delivery;

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Apply(SpellFormBuilder form, int rank)
        {
            if (!(form.Delivery is BuffDelivery buff) || !(form.Script is HeatHazeScript script))
            {
                throw new InvalidOperationException(Id + " transforms Heat Haze (SPL-011).");
            }

            buff.DurationSec = Duration.Float;
            form.Script = script.WithReservoir(Threshold.Float, rank >= 2 ? Rank2Cone.Float : Rank1Cone.Float, VentHalfAngleDeg, (int)VentHeat.Value);
            form.Recast = RecastRule.Default; // one vent; early presses are refused (IAwaitsRecastTarget)
        }
    }
}
