using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Rules;

namespace Confluence.Core.Tests.Rules
{
    public class SchedulesTests
    {
        [Fact]
        public void ResolveLoss_Matches_Spec_Schedule()
        {
            // Spec L36: 1 in rounds 1-3, 2 in 4-6, 3 in 7-9, 4 in 10-12.
            int[] expected = { 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4 };
            for (int round = 1; round <= 12; round++)
            {
                Assert.Equal(expected[round - 1], Schedules.ResolveLoss(round));
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => Schedules.ResolveLoss(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Schedules.ResolveLoss(13));
        }

        [Fact]
        public void Full_Loss_Streak_Eliminates_Before_Bracket()
        {
            // Starting Resolve 8 against the schedule: losing every round eliminates at round 6 (1+1+1+2+2+2 = 9).
            int resolve = GlobalRules.Match.StartingResolve;
            int eliminatedAt = 0;
            for (int round = 1; round <= 12 && eliminatedAt == 0; round++)
            {
                resolve -= Schedules.ResolveLoss(round);
                if (resolve <= 0)
                {
                    eliminatedAt = round;
                }
            }

            Assert.Equal(6, eliminatedAt);
        }

        [Fact]
        public void Shop_Income()
        {
            Assert.Equal(600, Schedules.ShopIncome(firstShop: true));   // spec L193
            Assert.Equal(500, Schedules.ShopIncome(firstShop: false));  // spec L193
        }

        [Fact]
        public void Tier_Prices()
        {
            Assert.Equal(200, Schedules.TierPrice(1));
            Assert.Equal(400, Schedules.TierPrice(2));
            Assert.Equal(600, Schedules.TierPrice(3));
            Assert.Equal(800, Schedules.TierPrice(4));
            Assert.Throws<ArgumentOutOfRangeException>(() => Schedules.TierPrice(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Schedules.TierPrice(5));
        }

        [Theory]
        [InlineData(MatchMode.Conclave, 1, 1)]
        [InlineData(MatchMode.Conclave, 2, 3)]
        [InlineData(MatchMode.Conclave, 3, 6)]
        [InlineData(MatchMode.Conclave, 4, 9)]
        [InlineData(MatchMode.Duel, 1, 1)]
        [InlineData(MatchMode.Duel, 2, 2)]
        [InlineData(MatchMode.Duel, 3, 4)]
        [InlineData(MatchMode.Duel, 4, 6)]
        public void Tier_Unlock_Rounds(MatchMode mode, int tier, int round)
        {
            // Spec L195-L200 (Conclave and Duel columns) and L65.
            Assert.Equal(round, Schedules.TierUnlockRound(mode, tier));
            Assert.True(Schedules.IsTierUnlocked(mode, round, tier));
            Assert.False(Schedules.IsTierUnlocked(mode, round - 1, tier));
        }

        [Fact]
        public void Tier_Unlock_Table_For_Every_Round()
        {
            int[] conclaveHighest = { 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 4, 4, 4, 4 }; // rounds 1-15 (13+ are bracket)
            for (int round = 1; round <= conclaveHighest.Length; round++)
            {
                Assert.Equal(conclaveHighest[round - 1], Schedules.HighestUnlockedTier(MatchMode.Conclave, round));
            }

            int[] duelHighest = { 1, 2, 2, 3, 3, 4, 4, 4 };
            for (int round = 1; round <= duelHighest.Length; round++)
            {
                Assert.Equal(duelHighest[round - 1], Schedules.HighestUnlockedTier(MatchMode.Duel, round));
            }

            Assert.Equal(0, Schedules.HighestUnlockedTier(MatchMode.Conclave, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Schedules.TierUnlockRound(MatchMode.Duel, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => Schedules.TierUnlockRound((MatchMode)7, 1));
        }

        [Fact]
        public void Conclave_Draft_Rarity_Schedule()
        {
            // Spec L216-L223: rounds 1, 3 Silver; 5, 7 Gold; 9, 11 Prismatic; even rounds have no draft.
            var expected = new Dictionary<int, Rarity>
            {
                [1] = Rarity.Silver,
                [3] = Rarity.Silver,
                [5] = Rarity.Gold,
                [7] = Rarity.Gold,
                [9] = Rarity.Prismatic,
                [11] = Rarity.Prismatic,
            };

            for (int round = 0; round <= 15; round++)
            {
                bool has = Schedules.DraftRarity(MatchMode.Conclave, round, false, out Rarity rarity);
                Assert.Equal(expected.ContainsKey(round), has);
                if (has)
                {
                    Assert.Equal(expected[round], rarity);
                }
            }

            Assert.Equal(expected.Keys.OrderBy(k => k), Schedules.DraftRounds(MatchMode.Conclave));
        }

        [Fact]
        public void Duel_Draft_Rarity_Schedule()
        {
            // Spec L225: Silver in 1 and 2, Gold in 3 and 4, Prismatic in 5 and 6.
            Rarity[] expected = { Rarity.Silver, Rarity.Silver, Rarity.Gold, Rarity.Gold, Rarity.Prismatic, Rarity.Prismatic };
            for (int round = 1; round <= 6; round++)
            {
                Assert.True(Schedules.DraftRarity(MatchMode.Duel, round, out Rarity rarity));
                Assert.Equal(expected[round - 1], rarity);
            }

            Assert.False(Schedules.DraftRarity(MatchMode.Duel, 7, out _));
            Assert.False(Schedules.DraftRarity(MatchMode.Duel, 0, out _));
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, Schedules.DraftRounds(MatchMode.Duel));
        }

        [Fact]
        public void Bracket_And_Replay_Rounds_Have_No_Draft()
        {
            // Spec L225: bracket rounds and tied-round replays do not add augment drafts.
            for (int round = 1; round <= 12; round++)
            {
                Assert.False(Schedules.DraftRarity(MatchMode.Conclave, round, true, out _));
                Assert.False(Schedules.DraftRarity(MatchMode.Duel, round, true, out _));
            }
        }

        [Fact]
        public void Each_Mode_Has_Six_Drafts_Two_Per_Rarity()
        {
            foreach (MatchMode mode in new[] { MatchMode.Conclave, MatchMode.Duel })
            {
                var rarities = Schedules.DraftRounds(mode)
                    .Select(r => Schedules.DraftRarity(mode, r, out Rarity rarity) ? rarity : (Rarity)(-1))
                    .ToList();
                Assert.Equal(GlobalRules.Draft.DraftsPerMatch, rarities.Count);
                Assert.Equal(2, rarities.Count(r => r == Rarity.Silver));
                Assert.Equal(2, rarities.Count(r => r == Rarity.Gold));
                Assert.Equal(2, rarities.Count(r => r == Rarity.Prismatic));
            }
        }

        [Fact]
        public void Rank_Two_Offers_Need_At_Least_The_Original_Rarity()
        {
            Assert.True(Schedules.AllowsRankTwoOffer(Rarity.Silver, Rarity.Silver));
            Assert.True(Schedules.AllowsRankTwoOffer(Rarity.Silver, Rarity.Prismatic));
            Assert.False(Schedules.AllowsRankTwoOffer(Rarity.Gold, Rarity.Silver));
        }

        [Fact]
        public void Arena_Diameters_Are_Fixed_And_Read_Only()
        {
            Assert.Equal(new[] { 24, 32, 40, 48 }, Schedules.ArenaDiameters);
            Assert.True(Schedules.IsValidArenaDiameter(40));
            Assert.False(Schedules.IsValidArenaDiameter(36));
            var asList = (IList<int>)Schedules.ArenaDiameters;
            Assert.Throws<NotSupportedException>(() => asList[0] = 99);
            Assert.Throws<NotSupportedException>(() => ((IList<int>)Schedules.ConclaveDraftRounds).Add(13));
        }
    }
}
