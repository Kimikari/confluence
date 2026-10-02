using System;
using Confluence.Core.Rules;

namespace Confluence.Casting
{
    /// <summary>
    /// Modifiers the simulation supplies for the cast being committed (from equipment and augments). The cast
    /// machine combines them with the grammar result under the shared caps in <see cref="MasteryClamp"/>.
    /// </summary>
    public struct CastModifiers
    {
        /// <summary>Ordinary cost reduction from all non-mastery sources (fraction; negative = surcharge).</summary>
        public float CostReduction;

        /// <summary>Equipment/augment efficiency that modifies a cast rewarded with mana efficiency (spec L480).</summary>
        public float MasteryLinkedEfficiency;

        /// <summary>Equipment/augment bonus that modifies a cast rewarded with the same attribute category (spec L480).</summary>
        public float MasteryLinkedAttribute;

        public static CastModifiers None => default;
    }

    /// <summary>
    /// Mastery normalization and cost/refund ceilings (spec L124, L478-L480, L90).
    /// <list type="bullet">
    /// <item>One reward per cast: efficiency up to 20%, or one attribute (damage, projectile speed, windup) up to 15%, never both.</item>
    /// <item>Mastery-linked equipment and augment effects clamp together with the grammar reward in the same category.</item>
    /// <item>Cost reduction from all sources leaves at least 70% of the listed cost; refunds cap at 25% of the payment.</item>
    /// </list>
    /// </summary>
    public static class MasteryClamp
    {
        public static bool IsEfficiency(MasteryReward reward) =>
            reward == MasteryReward.ManaEfficiency || reward == MasteryReward.StaminaEfficiency;

        public static bool IsAttribute(MasteryReward reward) =>
            reward == MasteryReward.DirectDamage || reward == MasteryReward.ProjectileSpeed || reward == MasteryReward.WindupReduction;

        /// <summary>Category ceiling for one cast (spec L124, L480).</summary>
        public static float CategoryCap(MasteryReward reward)
        {
            if (IsEfficiency(reward)) return GlobalRules.Modifiers.MasteryEfficiencyCap;
            if (IsAttribute(reward)) return GlobalRules.Modifiers.MasteryAttributeCap;
            return 0f;
        }

        public static float ClampReward(MasteryReward reward, float magnitude)
        {
            if (!(magnitude > 0f)) return 0f;
            return Math.Min(magnitude, CategoryCap(reward));
        }

        /// <summary>
        /// The applied mastery fraction for a cast: the grammar reward plus same-category mastery-linked effects,
        /// clamped together (spec L480). Linked effects never apply to an unrewarded cast or another category.
        /// </summary>
        public static float Combined(MasteryResult mastery, float linkedSameCategory)
        {
            if (!mastery.IsMastered) return 0f;
            return ClampReward(mastery.Reward, mastery.Magnitude + Math.Max(0f, linkedSameCategory));
        }

        /// <summary>Applied mana-efficiency fraction for a cast (0 unless rewarded with mana efficiency).</summary>
        public static float ManaEfficiency(MasteryResult mastery, CastModifiers modifiers) =>
            mastery.Reward == MasteryReward.ManaEfficiency ? Combined(mastery, modifiers.MasteryLinkedEfficiency) : 0f;

        /// <summary>
        /// Resource actually paid. Reductions from all sources add and are capped so at least 70% of the listed
        /// cost is paid (spec L124). Surcharges (negative reductions) are not floored.
        /// </summary>
        public static float PaidCost(float listedCost, float otherReduction, float masteryEfficiency)
        {
            if (listedCost <= 0f) return 0f;
            float reduction = otherReduction + Math.Max(0f, masteryEfficiency);
            float maxReduction = 1f - GlobalRules.Modifiers.MinPaidCostFraction;
            if (reduction > maxReduction) reduction = maxReduction;
            return listedCost * (1f - reduction);
        }

        /// <summary>The most any refund may return for one cast: 25% of the payment (spec L90, L124).</summary>
        public static float MaxRefund(float paid) => Math.Max(0f, paid) * GlobalRules.Casting.RefundCapFraction;

        /// <summary>
        /// Intrinsic windup after a windup-reduction reward. The reduction rounds down so it never exceeds the stated
        /// fraction, honours an absolute cap, and leaves at least one tick of a nonzero windup (spec L86).
        /// </summary>
        public static int ReducedWindupTicks(int windupTicks, float fraction, int capTicks)
        {
            if (windupTicks <= 0 || !(fraction > 0f)) return windupTicks;
            float applied = Math.Min(fraction, GlobalRules.Modifiers.MasteryAttributeCap);
            int reduction = (int)Math.Floor(windupTicks * (double)applied + 1e-9);
            if (capTicks > 0 && reduction > capTicks) reduction = capTicks;
            if (reduction >= windupTicks) reduction = windupTicks - 1;
            return windupTicks - reduction;
        }
    }
}
