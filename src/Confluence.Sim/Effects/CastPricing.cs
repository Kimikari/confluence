using System;
using System.Collections.Generic;
using Confluence.Core.Rules;
using Confluence.Sim.Combat;

namespace Confluence.Sim.Effects
{
    /// <summary>Which casts a pending cost bonus applies to.</summary>
    public sealed class CastFilter
    {
        public static readonly CastFilter Any = new CastFilter();

        /// <summary>Exact spell id, or null for any spell.</summary>
        public string SpellId { get; set; }

        /// <summary>A catalogue tag the spell must carry ("projectile", "field"), or null.</summary>
        public string RequiredTag { get; set; }

        /// <summary>Only damaging spells (true), only non-damaging ones (false), or either (null).</summary>
        public bool? Damaging { get; set; }

        public bool Matches(in CastQuote quote)
        {
            if (SpellId != null && !string.Equals(SpellId, quote.SpellId, StringComparison.Ordinal))
            {
                return false;
            }

            if (Damaging.HasValue && Damaging.Value != quote.Damaging)
            {
                return false;
            }

            if (RequiredTag == null)
            {
                return true;
            }

            if (quote.Tags != null)
            {
                for (int i = 0; i < quote.Tags.Count; i++)
                {
                    if (string.Equals(quote.Tags[i], RequiredTag, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    /// <summary>Which original hits a pending damage bonus applies to.</summary>
    public sealed class HitFilter
    {
        /// <summary>Spell direct impacts and common melee (direct hits, spec L102, L114).</summary>
        public static readonly HitFilter AnyDirect = new HitFilter();

        public bool SpellHitsOnly { get; set; }
        public bool MeleeOnly { get; set; }

        /// <summary>Also accept splash portions of an original cast (false: direct hits only).</summary>
        public bool IncludeSplash { get; set; }

        /// <summary>Channel the hit must carry (None = any).</summary>
        public DamageChannel Channel { get; set; } = DamageChannel.None;

        /// <summary>Largest attacker-to-target distance (NaN = any).</summary>
        public float MaxRange { get; set; } = float.NaN;

        public bool Matches(DamageRequest hit, float range)
        {
            if (hit == null || hit.Generation != 0)
            {
                return false;
            }

            bool direct = hit.IsDirectHit;
            bool splash = hit.Kind == DamageKind.Splash;
            if (!direct && !(IncludeSplash && splash))
            {
                return false;
            }

            if (SpellHitsOnly && !hit.IsSpellHit)
            {
                return false;
            }

            if (MeleeOnly && hit.Kind != DamageKind.Melee)
            {
                return false;
            }

            if (!float.IsNaN(MaxRange) && !(range <= MaxRange))
            {
                return false;
            }

            if (Channel == DamageChannel.None)
            {
                return true;
            }

            if (hit.Parts != null)
            {
                for (int i = 0; i < hit.Parts.Length; i++)
                {
                    if (hit.Parts[i].Channel == Channel)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    /// <summary>A cast about to be paid, as the spell runtime describes it.</summary>
    public struct CastQuote
    {
        public string SpellId;

        /// <summary>The record's mana cost after the form's own (transformation, mastery) changes.</summary>
        public float BaseCost;

        public bool Damaging;

        /// <summary>The spell's catalogue tags (filters read them), or null.</summary>
        public IReadOnlyList<string> Tags;

        public static CastQuote Of(string spellId, float baseCost, bool damaging = true, IReadOnlyList<string> tags = null)
        {
            return new CastQuote { SpellId = spellId, BaseCost = baseCost, Damaging = damaging, Tags = tags };
        }
    }

    /// <summary>
    /// The price of one cast (spec L124): reductions from the stat block and pending bonuses together keep at least
    /// 70% of the original cost; increases (drawbacks) add on top.
    /// </summary>
    public struct CastPrice
    {
        public float BaseCost;

        /// <summary>Reduction from the permanent cost-reduction stat (at most 30%).</summary>
        public float StatReduction;

        /// <summary>Reduction requested by pending next-cast bonuses.</summary>
        public float BonusReduction;

        /// <summary>Increase from pending next-cast drawbacks.</summary>
        public float Increase;

        /// <summary>Reduction actually applied after the 70% floor.</summary>
        public float AppliedReduction;

        public float Paid;

        /// <summary>The 70% floor clipped the requested reductions.</summary>
        public bool FloorBound;
    }

    /// <summary>A paid cast recorded for refunds (spec L90, L124: at most 25% of the payment, once per cast).</summary>
    public struct CastReceipt
    {
        /// <summary>Id of the payment's resource event; refunds name it.</summary>
        public int PaymentEventId;

        public string SpellId;
        public float Paid;
        public CastPrice Price;
    }

    internal sealed class CastCostBonus
    {
        public string Key;
        public string SourceId;
        public float Flat;
        public float Fraction;
        public int ActiveFromTick;
        public int ExpireTick;
        public CastFilter Filter;
        public int Serial;
    }

    internal sealed class HitBonus
    {
        public string Key;
        public string SourceId;
        public float Fraction;
        public float Flat;
        public int ExpireTick;
        public HitFilter Filter;
        public int Serial;
    }

    internal sealed class PaymentRecord
    {
        public int PaymentEventId;
        public int Player;
        public string SpellId;
        public float Paid;
        public bool Refunded;
        public float RefundedAmount;
    }

    /// <summary>Pricing rule shared by the inspector and commitment (spec L124).</summary>
    public static class CastCostRules
    {
        public static CastPrice Price(float baseCost, float statReductionFraction, float bonusReduction, float increase)
        {
            float cost = Math.Max(0f, baseCost);
            var price = new CastPrice
            {
                BaseCost = cost,
                StatReduction = cost * Math.Max(0f, Math.Min(statReductionFraction, GlobalRules.Modifiers.CostReductionCap)),
                BonusReduction = Math.Max(0f, bonusReduction),
                Increase = Math.Max(0f, increase),
            };
            float requested = price.StatReduction + price.BonusReduction;
            float maxReduction = cost * (1f - GlobalRules.Modifiers.MinPaidCostFraction);
            price.AppliedReduction = Math.Min(requested, maxReduction);
            price.FloorBound = requested > maxReduction + 1e-6f;
            price.Paid = Math.Max(0f, cost - price.AppliedReduction + price.Increase);
            return price;
        }
    }
}
