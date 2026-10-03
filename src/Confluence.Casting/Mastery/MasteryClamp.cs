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

        /// <summary>Flat mana added after the 70% floor by drawbacks ("next cast costs 4 more"; spec L124 floors reductions only).</summary>
        public float CostIncrease;

        /// <summary>Equipment/augment efficiency that modifies a cast rewarded with mana efficiency (spec L480).</summary>
        public float MasteryLinkedEfficiency;

        /// <summary>Equipment/augment bonus that modifies a cast rewarded with the same attribute category (spec L480).</summary>
        public float MasteryLinkedAttribute;

        /// <summary>
        /// Cost reduction (fraction of the listed cost) that applies only when this cast earns its mastery reward ("an
        /// advanced variation costs 8% less"); it joins the other reductions under the 70% floor (spec L124, L480).
        /// </summary>
        public float MasteredCostReduction;

        /// <summary>Cost reduction (fraction) that applies only to a normal, unrewarded cast ("a normal paid spell").</summary>
        public float NormalCostReduction;

        // ----- cast timing from equipment and augments (spec L92, L96, L480) -----------------------------------

        /// <summary>Fraction of the intrinsic windup removed for any cast ("preparation decreases 5%"); negative lengthens.</summary>
        public float WindupReduction;

        /// <summary>
        /// Fraction of the intrinsic windup removed only when the cast earns its mastery reward ("gain 6% preparation
        /// speed for that cast"); it clamps with a windup mastery reward in the shared 15% category (spec L480).
        /// </summary>
        public float MasteredWindupReduction;

        /// <summary>Ticks added to the windup after the fractions ("0.05 s longer preparation"); never negative.</summary>
        public int WindupDeltaTicks;

        /// <summary>Ticks added to (negative: removed from) the cast's recovery ("0.05 s longer recovery").</summary>
        public int RecoveryDeltaTicks;

        /// <summary>The smallest recovery a reduction may leave, in ticks ("minimum 0.1 s"); 0 = none.</summary>
        public int RecoveryFloorTicks;

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
        /// The windup after equipment and augment timing modifiers (spec L92, L480): <paramref name="windupTicks"/> is the
        /// intrinsic windup after any mastery windup reward. The non-mastery fraction and, on a rewarded cast, the
        /// mastered-only fraction remove ticks rounding down (a mastered-only windup bonus on a windup-rewarded cast
        /// shares the 15% category with the reward); a nonzero windup keeps at least one tick; added ticks come last.
        /// The anticipatory cue floor is applied by the machine afterwards (spec L96).
        /// </summary>
        public static int TimedWindupTicks(int intrinsicTicks, int windupTicks, MasteryResult mastery, CastModifiers modifiers)
        {
            float fraction = modifiers.WindupReduction;
            if (mastery.IsMastered && modifiers.MasteredWindupReduction > 0f)
            {
                float linked = modifiers.MasteredWindupReduction;
                if (mastery.Reward == MasteryReward.WindupReduction)
                {
                    linked = Math.Max(0f, Math.Min(linked, GlobalRules.Modifiers.MasteryAttributeCap - Combined(mastery, modifiers.MasteryLinkedAttribute)));
                }
                else
                {
                    linked = Math.Min(linked, GlobalRules.Modifiers.MasteryAttributeCap);
                }

                fraction += linked;
            }

            int result = windupTicks;
            if (fraction > 0f && windupTicks > 0)
            {
                result -= (int)Math.Floor(intrinsicTicks * (double)fraction + 1e-9);
                if (result < 1) result = 1;
            }
            else if (fraction < 0f)
            {
                result += (int)Math.Round(intrinsicTicks * (double)-fraction, MidpointRounding.AwayFromZero);
            }

            return result + Math.Max(0, modifiers.WindupDeltaTicks);
        }

        /// <summary>
        /// A cast's recovery after timing modifiers (spec L92): reductions never go below the stated floor (nor below
        /// zero), and a floor larger than the listed recovery never lengthens it.
        /// </summary>
        public static int TimedRecoveryTicks(int recoveryTicks, CastModifiers modifiers)
        {
            int result = recoveryTicks + modifiers.RecoveryDeltaTicks;
            if (modifiers.RecoveryDeltaTicks < 0)
            {
                result = Math.Max(result, Math.Min(recoveryTicks, Math.Max(0, modifiers.RecoveryFloorTicks)));
            }

            return Math.Max(0, result);
        }

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
