using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Confluence.Core.Rules
{
    /// <summary>
    /// Round-indexed tables: Resolve loss, income, equipment tiers, augment draft rarity, and arena diameters.
    /// Round numbers are 1-based scheduled round numbers. Conclave bracket rounds continue after round 12
    /// (tier 4 stays unlocked, spec L38).
    /// </summary>
    public static class Schedules
    {
        /// <summary>Arena diameters in meters (spec L306).</summary>
        public static readonly IReadOnlyList<int> ArenaDiameters = new ReadOnlyCollection<int>(new[]
        {
            GlobalRules.Arena.DiameterSmall,
            GlobalRules.Arena.DiameterMedium,
            GlobalRules.Arena.DiameterLarge,
            GlobalRules.Arena.DiameterHuge,
        });

        /// <summary>Conclave rounds that open an augment draft (spec L216-L223).</summary>
        public static readonly IReadOnlyList<int> ConclaveDraftRounds = new ReadOnlyCollection<int>(new[] { 1, 3, 5, 7, 9, 11 });

        /// <summary>Duel rounds that open an augment draft (spec L225).</summary>
        public static readonly IReadOnlyList<int> DuelDraftRounds = new ReadOnlyCollection<int>(new[] { 1, 2, 3, 4, 5, 6 });

        /// <summary>
        /// Resolve removed from the loser of a regular Conclave round (spec L36): 1 in rounds 1-3, 2 in 4-6,
        /// 3 in 7-9, 4 in 10-12. A draw removes the same amount from both players. Bracket losses eliminate
        /// regardless of Resolve (spec L38), so bracket rounds are not valid input.
        /// </summary>
        public static int ResolveLoss(int regularRound)
        {
            if (regularRound < 1 || regularRound > GlobalRules.Match.RegularRounds)
            {
                throw new ArgumentOutOfRangeException(nameof(regularRound), regularRound, "Regular rounds are 1-12.");
            }

            return (regularRound - 1) / 3 + 1;
        }

        /// <summary>Scheduled income before an equipment shop (spec L193). Bracket shops count as later shops.</summary>
        public static int ShopIncome(bool firstShop) => firstShop ? GlobalRules.Economy.FirstShopIncome : GlobalRules.Economy.LaterShopIncome;

        /// <summary>Crown price of an equipment tier 1-4 (spec L195-L200).</summary>
        public static int TierPrice(int tier)
        {
            switch (tier)
            {
                case 1: return GlobalRules.Economy.Tier1Price;
                case 2: return GlobalRules.Economy.Tier2Price;
                case 3: return GlobalRules.Economy.Tier3Price;
                case 4: return GlobalRules.Economy.Tier4Price;
                default: throw new ArgumentOutOfRangeException(nameof(tier), tier, "Equipment tiers are 1-4.");
            }
        }

        /// <summary>First round in which a tier is purchasable (spec L195-L200, L65): Conclave 1/3/6/9, Duel 1/2/4/6.</summary>
        public static int TierUnlockRound(MatchMode mode, int tier)
        {
            if (tier < 1 || tier > GlobalRules.Economy.TierCount)
            {
                throw new ArgumentOutOfRangeException(nameof(tier), tier, "Equipment tiers are 1-4.");
            }

            switch (mode)
            {
                case MatchMode.Conclave:
                    return ConclaveTierUnlock[tier - 1];
                case MatchMode.Duel:
                    return DuelTierUnlock[tier - 1];
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown match mode.");
            }
        }

        private static readonly int[] ConclaveTierUnlock = { 1, 3, 6, 9 }; // spec L197-L200
        private static readonly int[] DuelTierUnlock = { 1, 2, 4, 6 };     // spec L65, L197-L200

        public static bool IsTierUnlocked(MatchMode mode, int round, int tier) => round >= TierUnlockRound(mode, tier);

        /// <summary>Highest purchasable tier in a round (0 before round 1).</summary>
        public static int HighestUnlockedTier(MatchMode mode, int round)
        {
            int highest = 0;
            for (int tier = 1; tier <= GlobalRules.Economy.TierCount; tier++)
            {
                if (IsTierUnlocked(mode, round, tier))
                {
                    highest = tier;
                }
            }

            return highest;
        }

        /// <summary>
        /// Draft rarity for a scheduled round, or false when the round has no draft (spec L216-L225).
        /// Conclave: rounds 1 and 3 Silver, 5 and 7 Gold, 9 and 11 Prismatic. Duel: rounds 1-2 Silver,
        /// 3-4 Gold, 5-6 Prismatic. Bracket rounds and tied-round replays never add drafts.
        /// </summary>
        public static bool DraftRarity(MatchMode mode, int round, bool isBracketOrReplay, out Rarity rarity)
        {
            rarity = Rarity.Silver;
            if (isBracketOrReplay || round < 1)
            {
                return false;
            }

            switch (mode)
            {
                case MatchMode.Conclave:
                    if (round > 11 || round % 2 == 0)
                    {
                        return false;
                    }

                    rarity = round <= 3 ? Rarity.Silver : round <= 7 ? Rarity.Gold : Rarity.Prismatic;
                    return true;

                case MatchMode.Duel:
                    if (round > 6)
                    {
                        return false;
                    }

                    rarity = round <= 2 ? Rarity.Silver : round <= 4 ? Rarity.Gold : Rarity.Prismatic;
                    return true;

                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown match mode.");
            }
        }

        /// <summary>Regular (non-bracket, non-replay) round overload.</summary>
        public static bool DraftRarity(MatchMode mode, int round, out Rarity rarity) => DraftRarity(mode, round, false, out rarity);

        public static IReadOnlyList<int> DraftRounds(MatchMode mode)
        {
            switch (mode)
            {
                case MatchMode.Conclave: return ConclaveDraftRounds;
                case MatchMode.Duel: return DuelDraftRounds;
                default: throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown match mode.");
            }
        }

        /// <summary>
        /// True if an owned augment of originalRarity may be offered at rank 2 in a draft of draftRarity:
        /// the draft rarity must be at least the original rarity (spec L231, L600).
        /// </summary>
        public static bool AllowsRankTwoOffer(Rarity originalRarity, Rarity draftRarity) => draftRarity >= originalRarity;

        public static bool IsValidArenaDiameter(int diameter)
        {
            for (int i = 0; i < ArenaDiameters.Count; i++)
            {
                if (ArenaDiameters[i] == diameter)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
