using System;
using Confluence.Core.Rules;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Stats;

namespace Confluence.Sim.Effects
{
    /// <summary>
    /// Mastery-linked bonuses of one cast, summed over every hook of the caster and clamped once with the cast's own
    /// grammar reward in its category (spec L480 "All mastery-linked direct damage, projectile-speed, or windup benefits
    /// clamp in their relevant category at the shared 15% improvement limit"). Hooks report a fraction through
    /// <see cref="EffectHook.LinkedProjectileSpeed"/> and <see cref="EffectHook.LinkedDirectDamage"/>; the host applies
    /// the clamp a single time, so two linked records never multiply or add past the limit.
    /// </summary>
    public static class MasteryLinked
    {
        /// <summary>
        /// The total mastery improvement of a category: the cast's own reward (<paramref name="own"/> − 1) plus the linked
        /// bonuses, at most 15% (spec L480). Never below the cast's own reward.
        /// </summary>
        public static float Total(float own, float linkedSum)
        {
            float reward = Math.Max(0f, own - 1f);
            return Math.Max(reward, Math.Min(GlobalRules.Modifiers.MasteryAttributeCap, reward + Math.Max(0f, linkedSum)));
        }

        /// <summary>The factor a launch speed (already including the cast's own speed reward) gains from linked bonuses.</summary>
        public static float SpeedFactor(CastRecord cast, float linkedSum)
        {
            if (!(linkedSum > 0f)) return 1f;
            float own = cast != null ? cast.ProjectileSpeedMultiplier : 1f;
            return (1f + Total(own, linkedSum)) / own;
        }

        /// <summary>
        /// The extra damage fraction linked bonuses add on top of the cast's own damage reward multiplier, so the cast's
        /// total mastery damage improvement stays at most 15% (spec L480).
        /// </summary>
        public static float DamageFraction(CastRecord cast, float linkedSum)
        {
            if (!(linkedSum > 0f)) return 0f;
            float own = cast != null ? cast.DirectDamageMultiplier : 1f;
            return Math.Max(0f, (1f + Total(own, linkedSum)) / own - 1f);
        }
    }

    /// <summary>
    /// Gait changes the player's hooks request for one motor step (spec L80-L82, L124): signed extra walk and sprint
    /// bonuses (fractions). The host sums every hook's request and recomputes each gait multiplier once: the stat block's
    /// gait bonus plus all positive requests stay under the shared +20% movement cap, then all penalties (drawbacks) apply
    /// on top, so a drawback is never absorbed by the cap and attach order never matters.
    /// </summary>
    public struct GaitAdjust
    {
        /// <summary>Extra walking (and crouching) bonus; negative is a penalty.</summary>
        public float Walk;

        /// <summary>Extra sprinting bonus; negative is a penalty.</summary>
        public float Sprint;

        public bool IsZero => Walk == 0f && Sprint == 0f;
    }

    /// <summary>Accumulated gait requests of all hooks of one player for one motor step.</summary>
    internal struct GaitTotals
    {
        public float WalkBonus;
        public float WalkPenalty;
        public float SprintBonus;
        public float SprintPenalty;
        public bool Any;

        public void Add(GaitAdjust g)
        {
            if (g.IsZero) return;
            Any = true;
            if (g.Walk > 0f) WalkBonus += g.Walk; else WalkPenalty += g.Walk;
            if (g.Sprint > 0f) SprintBonus += g.Sprint; else SprintPenalty += g.Sprint;
        }

        /// <summary>1 + min(stat bonus + hook bonuses, +20%) + hook penalties, never negative (spec L124).</summary>
        public static float Multiplier(float statRaw, float bonus, float penalty) =>
            Math.Max(0f, 1f + Math.Min(statRaw + bonus, GlobalRules.Modifiers.MovementBonusCap) + penalty);

        public float WalkMultiplier(StatBlock stats) =>
            Multiplier(stats.Raw(StatKind.MovementBonus) + stats.Raw(StatKind.WalkSpeedBonus), WalkBonus, WalkPenalty);

        public float SprintMultiplier(StatBlock stats) =>
            Multiplier(stats.Raw(StatKind.MovementBonus) + stats.Raw(StatKind.SprintSpeedBonus), SprintBonus, SprintPenalty);
    }
}
