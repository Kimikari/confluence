using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Random;
using Confluence.Core.Rules;
using Confluence.Match.Tests.Fakes;

namespace Confluence.Match.Tests
{
    /// <summary>The seeded bracket after the regular schedule (spec L38, L61, L193, L225).</summary>
    public class ConclaveBracketFlowTests
    {
        private static IReadOnlyList<int> ExpectedSeeds(MatchFlow match, IEnumerable<int> contenders) =>
            Bracket.SeedOrder(contenders.Select(p =>
            {
                var mp = match.Player(p);
                return new BracketSeedEntry(p, mp.Resolve, mp.CombatWins, mp.RegularByes, mp.LobbySeed);
            }).ToList());

        private static List<BracketSeededEvent> CaptureSeeding(Harness h, List<IReadOnlyList<int>> expected)
        {
            var seeded = new List<BracketSeededEvent>();
            h.Match.Events.Appended += e =>
            {
                if (e is BracketSeededEvent b)
                {
                    seeded.Add(b);
                    expected.Add(ExpectedSeeds(h.Match, h.Conclave.RemainingContenders));
                }
            };
            return seeded;
        }

        [Fact]
        public void The_Full_Schedule_Enters_The_Bracket_After_Round_12()
        {
            // A scripted lobby in which two contenders survive all twelve regular rounds (found by search; rare by design).
            var h = Harness.ForConclave(8, 7, s =>
            {
                ulong x = SeedHash.Combine(2135UL, (ulong)(s.Round * 64 + Math.Min(s.Side0Player, s.Side1Player) * 8 + Math.Max(s.Side0Player, s.Side1Player)));
                if (x % 11 == 0) return DuelScript.Draw();
                return DuelScript.Win((x >> 8) % 2 == 0 ? s.Side0Player : s.Side1Player);
            });
            var expected = new List<IReadOnlyList<int>>();
            var seeded = CaptureSeeding(h, expected);
            var resolveAtBracket = new Dictionary<int, int>();
            h.Match.Events.Appended += e =>
            {
                if (e is BracketSeededEvent) foreach (var p in h.Match.Players) resolveAtBracket[p.Id] = p.Resolve;
            };
            h.RunToEnd();

            var seeding = Assert.Single(seeded);
            Assert.Equal(13, seeding.Round);
            Assert.Equal(expected[0], seeding.PlayersBySeed);
            Assert.Equal(2, seeding.Size);

            // Round 13 is bracket round 1: 500 income, tier 4 unlocked, no draft, no Resolve change (L38, L193, L225).
            var bracketShops = h.ShopsIn(13);
            Assert.Equal(2, bracketShops.Count);
            Assert.All(bracketShops, s =>
            {
                Assert.Equal(1, s.BracketRound);
                Assert.Equal(500, s.BaselineIncome);
                Assert.Equal(4, s.HighestUnlockedTier);
            });
            Assert.DoesNotContain(h.Economy.Drafts, d => d.Round > 12);
            Assert.DoesNotContain(h.Match.Events.OfType<ResolveChangedEvent>(), e => e.Round > 12);
            Assert.All(h.Match.Players, p => Assert.Equal(resolveAtBracket[p.Id], p.Resolve));

            var final = h.Match.Events.OfType<DuelResolvedEvent>().Single(d => d.Round == 13);
            Assert.Equal(final.Outcome.Winner, h.Match.Result.Champion);
            Assert.Equal(2.0, h.Match.Result.For(final.Outcome.LoserOrNoPlayer).Position);
            Assert.True(h.Match.Player(final.Outcome.LoserOrNoPlayer).Resolve > 0); // L38: a bracket loss eliminates regardless
        }

        [Fact]
        public void Six_Contenders_Give_The_Top_Two_Seeds_Byes_In_A_Three_Round_Bracket()
        {
            var h = Harness.ForShortConclave(6, 3, 1, FakeDuelRunner.LowerIdWins);
            var expected = new List<IReadOnlyList<int>>();
            var seeded = CaptureSeeding(h, expected);
            h.RunToEnd();

            var seeding = Assert.Single(seeded);
            var seeds = seeding.PlayersBySeed;
            Assert.Equal(expected[0], seeds);
            Assert.Equal(8, seeding.Size);
            Assert.Equal(new[] { 8, 8, 8, 7, 7, 7 }, seeds.Select(p => h.Match.Player(p).Resolve));

            // Bracket round 1 (round 2): seeds 1 and 2 take the byes to reach eight slots; 3 plays 6 and 4 plays 5.
            var announced = h.Match.Events.OfType<PairingsAnnouncedEvent>().Single(p => p.Round == 2);
            Assert.Equal(1, announced.BracketRound);
            Assert.Equal(new[] { seeds[0], seeds[1] }.OrderBy(x => x), announced.Byes.OrderBy(x => x));
            Assert.Equal(
                new[] { (seeds[2], seeds[5]), (seeds[3], seeds[4]) }.Select(Norm).OrderBy(x => x),
                announced.Pairs.Select(s => Norm((s.Side0Player, s.Side1Player))).OrderBy(x => x));

            // L61/L193: bracket byes shop with baseline income, and the bye earns no bonus for the next shop.
            Assert.Equal(6, h.ShopsIn(2).Count);
            foreach (int bye in announced.Byes)
            {
                Assert.Equal(100, h.ShopsIn(2).Single(s => s.Player == bye).WinnerBonus); // won regular round 1
                Assert.Equal(0, h.ShopsIn(3).Single(s => s.Player == bye).WinnerBonus);
                Assert.Equal(RoundOutcome.Bye, h.ResultsOf(2).Single().For(bye).Outcome);
            }

            // Three bracket rounds at most, then the match ends.
            Assert.Equal(4, h.Match.Round);
            Assert.Equal(3, h.Match.BracketRound);

            // Every bracket loss eliminates (L38), whatever Resolve remains; same-round losers share a block.
            var bracketLosses = h.Match.Events.OfType<DuelResolvedEvent>().Where(d => d.BracketRound() > 0).ToList();
            Assert.All(bracketLosses, d => Assert.True(h.Match.Player(d.Outcome.LoserOrNoPlayer).IsEliminated));
            Assert.All(bracketLosses, d => Assert.True(h.Match.Player(d.Outcome.LoserOrNoPlayer).Resolve > 0));
            var byRound = bracketLosses.GroupBy(d => d.Round).ToDictionary(g => g.Key, g => g.Select(d => h.Match.Result.For(d.Outcome.LoserOrNoPlayer)).ToList());
            Assert.All(byRound[2], p => Assert.Equal(5.5, p.Position));
            Assert.All(byRound[3], p => Assert.Equal(3.5, p.Position));
            Assert.Equal(2.0, Assert.Single(byRound[4]).Position);
            Assert.Equal(1.0, h.Match.Result.For(h.Match.Result.Champion).Position);
        }

        [Fact]
        public void A_Bracket_Draw_Replays_Once_With_Frozen_Loadouts_Full_Resources_And_A_New_Arena()
        {
            var h = Harness.ForShortConclave(4, 8, 1, s =>
                s.BracketRound == 1 && s.Attempt == 0 ? DuelScript.Draw() : FakeDuelRunner.LowerIdWins(s));
            var revealsDuringReplay = new List<RevealInfo>();
            h.Match.Events.Appended += e =>
            {
                if (e is PhaseChangedEvent p && p.Phase == MatchPhase.Reveal && p.Attempt == 1)
                    foreach (var pair in h.Match.CurrentPairs) revealsDuringReplay.Add(h.Match.GetReveal(pair.Side0Player));
            };
            h.RunToEnd();

            var original = h.Runner.Started.Where(s => s.Round == 2 && s.Attempt == 0).ToList();
            var replay = h.Runner.Started.Where(s => s.Round == 2 && s.Attempt == 1).ToList();
            Assert.Equal(2, original.Count);
            Assert.Equal(original.Select(s => s.SortedPlayers()).OrderBy(x => x), replay.Select(s => s.SortedPlayers()).OrderBy(x => x));
            Assert.All(replay, s =>
            {
                Assert.Equal(PassKind.DrawReplay, s.PassKind);
                Assert.True(s.IsReplay);
                Assert.True(s.FullResourceRestore);
            });
            Assert.False(original[0].IsReplay);

            // A new arena seed with the round's diameter (L38, D-028), shared by every replayed pair (L34).
            var requests = h.Arenas.Requests.Where(r => r.Round == 2).OrderBy(r => r.Attempt).ToList();
            Assert.Equal(2, requests.Count);
            Assert.NotEqual(requests[0].ArenaSeed, requests[1].ArenaSeed);
            Assert.Equal(requests[0].Diameter, requests[1].Diameter);
            Assert.Single(replay.Select(s => s.Arena.ArenaId).Distinct());

            // No shop or draft, and the loadouts frozen at the original alcove stay in force.
            Assert.Equal(4, h.ShopsIn(2).Count);
            Assert.Equal(4, h.Economy.Finalized.Count(f => f.Round == 2));
            var replayPhases = h.Match.Events.OfType<PhaseChangedEvent>().Where(e => e.Round == 2 && e.Attempt == 1).Select(e => e.Phase);
            Assert.Equal(new[] { MatchPhase.Reveal, MatchPhase.Alcove, MatchPhase.Combat, MatchPhase.Results }, replayPhases);
            Assert.All(revealsDuringReplay, r =>
            {
                Assert.Equal(PassKind.DrawReplay, r.PassKind);
                Assert.Equal("EQP-002", r.OpponentLoadout.EquipmentIds.Single());
            });

            var results = h.ResultsOf(2);
            Assert.Equal(2, results.Count);
            Assert.False(results[0].RoundComplete);
            Assert.All(results[0].Entries, e => Assert.True(e.ReplayPending));
            Assert.True(results[1].RoundComplete);

            // L193: winning a replay grants no bonus for the final's shop.
            Assert.All(h.ShopsIn(3), s => Assert.Equal(0, s.WinnerBonus));
            Assert.All(h.ShopsIn(3), s => Assert.Equal(500, s.BaselineIncome));
            Assert.Equal(3, h.Match.Round);
            Assert.NotEqual(MatchRules.NoPlayer, h.Match.Result.Champion);
        }

        [Fact]
        public void A_Second_Draw_In_The_Final_Is_A_Shared_Finish_Without_A_Champion()
        {
            var h = Harness.ForShortConclave(2, 9, 1, s => s.BracketRound > 0 ? DuelScript.Draw() : FakeDuelRunner.LowerIdWins(s));
            h.RunToEnd();

            Assert.Equal(2, h.Runner.Started.Count(s => s.Round == 2)); // one replay only
            Assert.Equal(MatchRules.NoPlayer, h.Match.Result.Champion);
            Assert.True(h.Match.Result.SharedFirstPlace);
            Assert.All(h.Match.Result.Placements, p =>
            {
                Assert.Equal(1, p.FirstPosition);
                Assert.Equal(2, p.LastPosition);
                Assert.Equal(1.5, p.Position);
                Assert.Equal(1, p.BracketRound);
            });
        }

        [Fact]
        public void A_Shared_Semifinal_Finish_Sends_The_Other_Winner_Through_By_Walkover()
        {
            // Seeds 1 and 4 draw twice; seeds 2 and 3 play normally. Nobody advances from the shared match.
            var h = Harness.ForShortConclave(4, 10, 1, FakeDuelRunner.LowerIdWins);
            Bracket bracket = null;
            h.Match.Events.Appended += e =>
            {
                if (!(e is BracketSeededEvent)) return;
                bracket = h.Conclave.Bracket;
                int top = bracket.Seeds[0], bottom = bracket.Seeds[3];
                h.Runner.Decide = s => s.BracketRound > 0 && s.Involves(top) && s.Involves(bottom) ? DuelScript.Draw() : FakeDuelRunner.LowerIdWins(s);
            };
            h.RunToEnd();

            int seed1 = bracket.Seeds[0], seed2 = bracket.Seeds[1], seed3 = bracket.Seeds[2], seed4 = bracket.Seeds[3];
            int semiWinner = Math.Min(seed2, seed3), semiLoser = Math.Max(seed2, seed3);
            Assert.Equal(2, h.Match.Round); // no final is scheduled: the walkover needs no shop or arena
            Assert.DoesNotContain(h.Economy.Shops, s => s.Round == 3);
            Assert.Equal(semiWinner, h.Match.Result.Champion);
            foreach (int p in new[] { seed1, seed4, semiLoser })
            {
                Assert.Equal(2, h.Match.Result.For(p).FirstPosition);
                Assert.Equal(4, h.Match.Result.For(p).LastPosition);
                Assert.Equal(3.0, h.Match.Result.For(p).Position);
            }
        }

        [Fact]
        public void Bracket_Rounds_Never_Draft_And_Keep_Tier_Four()
        {
            // Bracket rounds 3 and 5 carry Conclave draft round numbers here, but bracket rounds never add drafts (L225).
            var h = Harness.ForShortConclave(8, 11, 2, FakeDuelRunner.LowerIdWins);
            h.RunToEnd();
            Assert.Equal(5, h.Match.Round);
            Assert.Equal(new[] { 1 }, h.Economy.Drafts.Select(d => d.Round).Distinct());
            Assert.All(h.Economy.Shops.Where(s => s.BracketRound > 0), s => Assert.Equal(4, s.HighestUnlockedTier));
            Assert.DoesNotContain(h.Match.Events.OfType<PhaseChangedEvent>(), e => e.Round > 2 && e.Phase == MatchPhase.Draft);
        }

        [Fact]
        public void Seeding_Counts_Regular_Byes_With_Odd_Lobbies()
        {
            foreach (ulong seed in new ulong[] { 4, 5, 6 })
            {
                var h = Harness.ForShortConclave(7, seed, 3, s => DuelScript.Win(Math.Max(s.Side0Player, s.Side1Player)));
                var expected = new List<IReadOnlyList<int>>();
                var seeded = CaptureSeeding(h, expected);
                h.RunToEnd();
                var seeding = Assert.Single(seeded);
                Assert.Equal(expected[0], seeding.PlayersBySeed);
                Assert.Equal(8, seeding.Size);
                Assert.Equal(3, h.Match.Players.Sum(p => p.RegularByes));

                // Seed 1 alone takes the bracket bye (L38).
                var first = h.Match.Events.OfType<PairingsAnnouncedEvent>().Single(p => p.BracketRound == 1);
                Assert.Equal(new[] { seeding.PlayersBySeed[0] }, first.Byes);
            }
        }

        private static (int, int) Norm((int, int) pair) => pair.Item1 < pair.Item2 ? pair : (pair.Item2, pair.Item1);
    }

    internal static class DuelEventExtensions
    {
        public static int BracketRound(this DuelResolvedEvent e) => e.Setup.BracketRound;

        public static (int, int) SortedPlayers(this DuelSetup s) =>
            (Math.Min(s.Side0Player, s.Side1Player), Math.Max(s.Side0Player, s.Side1Player));
    }
}
