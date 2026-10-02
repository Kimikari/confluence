using System;
using System.Collections.Generic;
using Confluence.Arena.Generation;
using Confluence.Core.Rules;
using Confluence.Match;

namespace Confluence.Arena.Tests
{
    /// <summary>
    /// The match owns the round diameter (spec L306, D-028, D-095): Match draws one balanced sequence per match and
    /// passes each round's diameter to the arena provider. ArenaSchedule must agree with it for every round of both
    /// modes, so previews, tools, and unforced requests describe the arena the match will ask for.
    /// </summary>
    public class RoundDiameterTests
    {
        [Fact]
        public void ScheduleMatchesTheConclaveSequence()
        {
            for (ulong seed = 1; seed <= 200; seed++)
            {
                ulong matchSeed = seed * 0x9E3779B97F4A7C15UL;
                var match = new ConclaveMatch(ConclaveSettings.Unranked, matchSeed, Seams.Instance, Seams.Instance, Seams.Instance);
                AssertScheduleAgrees(matchSeed, match.ArenaDiameters, MatchRules.ConclaveScheduledRoundCapacity);
            }
        }

        [Fact]
        public void ScheduleMatchesTheDuelSequence()
        {
            for (ulong seed = 1; seed <= 200; seed++)
            {
                ulong matchSeed = seed * 0xC2B2AE3D27D4EB4FUL + 7919UL;
                var match = new DuelMatch(matchSeed, Seams.Instance, Seams.Instance, Seams.Instance);
                AssertScheduleAgrees(matchSeed, match.ArenaDiameters, MatchRules.DuelScheduledRoundCapacity);
            }
        }

        [Fact]
        public void UnforcedRequestsUseTheMatchDiameter()
        {
            var match = new DuelMatch(424242UL, Seams.Instance, Seams.Instance, Seams.Instance);
            for (int round = 1; round <= 3; round++)
            {
                GeneratedArena arena = ArenaFixture.Generator.Generate(new Confluence.Arena.Generation.ArenaRequest { MatchSeed = 424242UL, Round = round });
                Assert.Equal(match.ArenaDiameters[round - 1], arena.Layout.Diameter);
            }
        }

        private static void AssertScheduleAgrees(ulong matchSeed, IReadOnlyList<int> fromMatch, int capacity)
        {
            Assert.Equal(capacity, fromMatch.Count);
            Assert.Equal(fromMatch, ArenaSchedule.DiameterSequence(matchSeed, capacity));
            for (int round = 1; round <= capacity; round++)
            {
                Assert.Equal(fromMatch[round - 1], ArenaSchedule.DiameterForRound(matchSeed, round));
            }

            Assert.True(Schedules.IsBalancedDiameterSequence(fromMatch, out string reason), reason);
        }

        /// <summary>Seams for constructing a match; constructing never calls them, and these tests never tick.</summary>
        private sealed class Seams : IDuelRunner, IMatchEconomy, IArenaProvider
        {
            public static readonly Seams Instance = new Seams();

            public void StartDuel(DuelSetup setup) => throw new NotSupportedException();
            public bool Poll(int duelId, int duelTick, out DuelOutcome outcome) => throw new NotSupportedException();
            public void AbortDuel(int duelId) => throw new NotSupportedException();
            public void OpenShop(ShopOpening opening) => throw new NotSupportedException();
            public void CloseShop(int player) => throw new NotSupportedException();
            public void OpenDraft(DraftOpening opening) => throw new NotSupportedException();
            public void CloseDraft(int player) => throw new NotSupportedException();
            public PublicLoadout FinalizeLoadout(int player, int round) => throw new NotSupportedException();
            public bool IsReady(int player) => throw new NotSupportedException();
            public void RecordCompletedDuel(CompletedDuelRecord record) => throw new NotSupportedException();
            public ArenaPreview RequestArena(Confluence.Match.ArenaRequest request) => throw new NotSupportedException();
        }
    }
}
