using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Confluence.Core.Random;

namespace Confluence.Core.Rules
{
    /// <summary>
    /// Round-indexed tables: Resolve loss, income, equipment tiers, augment draft rarity, the seeded arena diameter
    /// sequence, and the round numbers eligibility predicates read.
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

        // ---- Round numbers seen by eligibility predicates ------------------------------------

        /// <summary>
        /// The round number a round_at_least leaf compares against (DECISIONS D-029). Thresholds are authored on the
        /// Conclave schedule; Duel compresses the same six drafts into rounds 1-6 (spec L225), so Duel round r maps
        /// to the Conclave round that holds the same draft, 2r - 1. Conclave rounds are used unchanged.
        /// </summary>
        public static int PredicateRound(MatchMode mode, int scheduledRound)
        {
            switch (mode)
            {
                case MatchMode.Conclave: return scheduledRound;
                case MatchMode.Duel: return scheduledRound < 1 ? scheduledRound : 2 * scheduledRound - 1;
                default: throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown match mode.");
            }
        }

        /// <summary>
        /// Completed duels guaranteed to exist before the draft of a scheduled round: one per earlier round (spec L44-L49,
        /// L280). Duel replays can add more; feat thresholds are validated against this guaranteed count.
        /// </summary>
        public static int CompletedDuelsBefore(int scheduledRound) => Math.Max(0, scheduledRound - 1);

        // ---- Arena diameter sequence (spec L306) -----------------------------------------------

        private static readonly int[][] DiameterOrders = BuildDiameterOrders();

        /// <summary>
        /// True when a 24 m round is followed immediately by a 48 m round: the "back-to-back extremes of 24 then 48"
        /// the size schedule avoids (spec L306, DECISIONS D-028).
        /// </summary>
        public static bool IsExtremeStep(int previousDiameter, int nextDiameter) =>
            previousDiameter == GlobalRules.Arena.DiameterSmall && nextDiameter == GlobalRules.Arena.DiameterHuge;

        /// <summary>
        /// One balanced set of four consecutive rounds: every diameter once, in a seeded order without a 24 m to
        /// 48 m step, including the step from the previous set's last round (pass 0 when there is none). If no order
        /// avoided the step the set itself would take precedence ("unless needed to preserve the four-round set").
        /// </summary>
        public static int[] ArenaDiameterSet(Rng rng, int previousDiameter)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            var legal = new List<int[]>(DiameterOrders.Length);
            foreach (var order in DiameterOrders)
                if (!IsExtremeStep(previousDiameter, order[0]) && !HasExtremeStep(order)) legal.Add(order);
            if (legal.Count == 0) legal.AddRange(DiameterOrders);
            return (int[])legal[rng.NextInt(legal.Count)].Clone();
        }

        /// <summary>
        /// Diameters for scheduled rounds 1..roundCount (index 0 is round 1), built from consecutive balanced sets of
        /// four (spec L306). A tied-round replay keeps its round's diameter. The caller supplies the match's arena stream.
        /// </summary>
        public static int[] ArenaDiameterSequence(Rng rng, int roundCount)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (roundCount < 0) throw new ArgumentOutOfRangeException(nameof(roundCount), roundCount, "Round count cannot be negative.");
            var result = new int[roundCount];
            int previous = 0;
            int setSize = GlobalRules.Arena.DiameterSetRounds;
            for (int start = 0; start < roundCount; start += setSize)
            {
                int[] set = ArenaDiameterSet(rng, previous);
                for (int i = 0; i < setSize && start + i < roundCount; i++) result[start + i] = set[i];
                previous = set[setSize - 1];
            }

            return result;
        }

        /// <summary>
        /// Checks a diameter sequence against spec L306: every complete set of four rounds uses each diameter once, a
        /// trailing partial set repeats none, every value is a legal diameter, and no 24 m round precedes a 48 m round.
        /// </summary>
        public static bool IsBalancedDiameterSequence(IReadOnlyList<int> diameters, out string reason)
        {
            if (diameters == null) throw new ArgumentNullException(nameof(diameters));
            int setSize = GlobalRules.Arena.DiameterSetRounds;
            for (int start = 0; start < diameters.Count; start += setSize)
            {
                var seen = new HashSet<int>();
                for (int i = start; i < start + setSize && i < diameters.Count; i++)
                {
                    if (!IsValidArenaDiameter(diameters[i]))
                    {
                        reason = "round " + (i + 1) + " has illegal diameter " + diameters[i];
                        return false;
                    }

                    if (!seen.Add(diameters[i]))
                    {
                        reason = "round " + (i + 1) + " repeats diameter " + diameters[i] + " within its four-round set";
                        return false;
                    }
                }
            }

            for (int i = 1; i < diameters.Count; i++)
            {
                if (IsExtremeStep(diameters[i - 1], diameters[i]))
                {
                    reason = "rounds " + i + " and " + (i + 1) + " step from 24 m straight to 48 m";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        private static bool HasExtremeStep(int[] order)
        {
            for (int i = 1; i < order.Length; i++)
                if (IsExtremeStep(order[i - 1], order[i])) return true;
            return false;
        }

        // All 24 orders of the four diameters, in lexicographic order so draws are stable across runtimes.
        private static int[][] BuildDiameterOrders()
        {
            var result = new List<int[]>();
            var current = new int[ArenaDiameters.Count];
            var used = new bool[ArenaDiameters.Count];
            Permute(0, current, used, result);
            return result.ToArray();
        }

        private static void Permute(int depth, int[] current, bool[] used, List<int[]> result)
        {
            if (depth == current.Length)
            {
                result.Add((int[])current.Clone());
                return;
            }

            for (int i = 0; i < ArenaDiameters.Count; i++)
            {
                if (used[i]) continue;
                used[i] = true;
                current[depth] = ArenaDiameters[i];
                Permute(depth + 1, current, used, result);
                used[i] = false;
            }
        }
    }
}
