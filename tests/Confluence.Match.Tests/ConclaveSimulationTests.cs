using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Rules;
using Confluence.Match.Tests.Fakes;

namespace Confluence.Match.Tests
{
    /// <summary>Full Conclave matches against scripted duel outcomes: Resolve, eliminations, placements, byes, pairing, sides (spec L36-L61).</summary>
    public class ConclaveSimulationTests
    {
        private static readonly ulong[] Seeds = { 1, 2, 3, 17, 99, 4242 };

        /// <summary>Strict strengths with a little seeded noise so different seeds produce different matches.</summary>
        private static Func<DuelSetup, DuelScript> Noisy(ulong seed) => setup =>
        {
            int a = setup.Side0Player, b = setup.Side1Player;
            ulong h = Confluence.Core.Random.SeedHash.Combine(seed, (ulong)(setup.Round * 64 + Math.Min(a, b) * 8 + Math.Max(a, b)));
            if (h % 7 == 0) return DuelScript.Draw();
            bool upset = h % 5 == 0;
            int stronger = Math.Min(a, b), weaker = Math.Max(a, b);
            return DuelScript.Win(upset ? weaker : stronger);
        };

        [Fact]
        public void Resolve_Loss_Follows_The_Round_Schedule()
        {
            foreach (ulong seed in Seeds)
            {
                var h = Harness.ForConclave(8, seed, Noisy(seed));
                h.RunToEnd();
                var changes = h.Match.Events.OfType<ResolveChangedEvent>();
                Assert.NotEmpty(changes);
                Assert.All(changes, e =>
                {
                    // L36: 1 in rounds 1-3, 2 in 4-6, 3 in 7-9, 4 in 10-12 (clamped at zero); bracket rounds never change Resolve.
                    Assert.InRange(e.Round, 1, GlobalRules.Match.RegularRounds);
                    Assert.Equal(Math.Min(e.Before, Schedules.ResolveLoss(e.Round)), e.Before - e.After);
                });

                // Every regular loser and both players of every regular draw lose Resolve exactly once per pass.
                foreach (var duel in h.Match.Events.OfType<DuelResolvedEvent>().Where(d => d.Round <= GlobalRules.Match.RegularRounds))
                {
                    var losers = duel.Outcome.IsDraw ? new[] { duel.Setup.Side0Player, duel.Setup.Side1Player } : new[] { duel.Outcome.LoserOrNoPlayer };
                    foreach (int p in losers) Assert.Single(changes, c => c.Round == duel.Round && c.Player == p);
                    if (!duel.Outcome.IsDraw) Assert.DoesNotContain(changes, c => c.Round == duel.Round && c.Player == duel.Outcome.Winner);
                }
            }
        }

        [Fact]
        public void Elimination_Waits_For_The_Simultaneous_Round_To_Finish()
        {
            // Player 7 always loses quickly; other duels take much longer. L36: elimination happens after the round finishes.
            var h = Harness.ForConclave(8, 5, s =>
            {
                int a = s.Side0Player, b = s.Side1Player;
                bool involvesSeven = a == 7 || b == 7;
                return DuelScript.Win(involvesSeven ? (a == 7 ? b : a) : Math.Min(a, b), involvesSeven ? 10 : 2000);
            });
            h.RunToEnd();
            var eliminated = h.Match.Events.OfType<PlayerEliminatedEvent>().First(e => e.Placement.Player == 7);
            var roundDuels = h.Match.Events.OfType<DuelResolvedEvent>().Where(d => d.Round == eliminated.Round).ToList();
            var ownDuel = roundDuels.Single(d => d.Setup.Involves(7));
            Assert.True(ownDuel.Tick < roundDuels.Max(d => d.Tick));
            Assert.Equal(roundDuels.Max(d => d.Tick), eliminated.Tick);
            Assert.Equal(0, h.Match.Player(7).Resolve);

            // 1+1+1+2+2+2: the player who loses every round reaches zero in round 6.
            Assert.Equal(6, eliminated.Round);
        }

        [Fact]
        public void Placements_Partition_The_Lobby_And_Same_Round_Eliminations_Share_The_Mean()
        {
            foreach (ulong seed in Seeds)
            {
                var h = Harness.ForConclave(8, seed, Noisy(seed));
                h.RunToEnd();
                var placements = h.Match.Result.Placements;
                Assert.Equal(8, placements.Count);

                // Groups leaving together occupy contiguous blocks that exactly cover positions 1-8 (L40).
                var blocks = placements.GroupBy(p => (p.FirstPosition, p.LastPosition)).OrderBy(g => g.Key.FirstPosition).ToList();
                int next = 1;
                foreach (var block in blocks)
                {
                    Assert.Equal(next, block.Key.FirstPosition);
                    Assert.Equal(block.Key.LastPosition - block.Key.FirstPosition + 1, block.Count());
                    Assert.All(block, p => Assert.Equal((block.Key.FirstPosition + block.Key.LastPosition) / 2.0, p.Position));
                    next = block.Key.LastPosition + 1;
                }

                Assert.Equal(9, next);

                // Everyone eliminated in the same regular round shares one block; later eliminations place higher.
                foreach (var round in h.Match.Events.OfType<PlayerEliminatedEvent>().Where(e => e.BracketRound == 0).GroupBy(e => e.Round))
                {
                    Assert.Single(round.Select(e => e.Placement.FirstPosition).Distinct());
                    var later = h.Match.Players.Where(p => p.Placement.Round > round.Key || p.Placement.BracketRound > 0 || !h.Match.Events.OfType<PlayerEliminatedEvent>().Any(e => e.Placement.Player == p.Id));
                    Assert.All(later, p => Assert.True(p.Placement.LastPosition < round.First().Placement.FirstPosition));
                }
            }
        }

        [Fact]
        public void A_Lobby_That_Draws_Every_Duel_Is_Eliminated_Together_With_No_Champion()
        {
            var h = Harness.ForConclave(8, 12, _ => DuelScript.Draw());
            h.RunToEnd();

            // Draws remove the scheduled loss from both (L36): 1+1+1+2+2 leaves 1 Resolve, round 6 removes it from everyone.
            Assert.Equal(6, h.Match.Round);
            Assert.All(h.Match.Players, p =>
            {
                Assert.Equal(0, p.Resolve);
                Assert.Equal(1, p.Placement.FirstPosition);
                Assert.Equal(8, p.Placement.LastPosition);
                Assert.Equal(4.5, p.Placement.Position);
            });
            Assert.True(h.Match.Result.SharedFirstPlace);
            Assert.Equal(MatchRules.NoPlayer, h.Match.Result.Champion);
            Assert.Empty(h.Match.Events.OfType<BracketSeededEvent>());
        }

        [Fact]
        public void The_Regular_Schedule_Ends_Early_When_One_Contender_Remains()
        {
            // Player 0 beats everyone; every other duel is a draw, so players 1-7 lose Resolve every round (L36, L38).
            var h = Harness.ForConclave(8, 31, s => s.Involves(0) ? DuelScript.Win(0) : DuelScript.Draw());
            h.RunToEnd();

            Assert.Equal(6, h.Match.Round);
            Assert.Empty(h.Match.Events.OfType<BracketSeededEvent>());
            Assert.Equal(0, h.Match.Result.Champion);
            Assert.Equal(1.0, h.Match.Result.For(0).Position);
            Assert.All(Enumerable.Range(1, 7), p =>
            {
                Assert.Equal(2, h.Match.Result.For(p).FirstPosition);
                Assert.Equal(8, h.Match.Result.For(p).LastPosition);
                Assert.Equal(5.0, h.Match.Result.For(p).Position);
            });
            Assert.DoesNotContain(h.Economy.Shops, s => s.Round > 6);
            Assert.Equal(8, h.Match.Player(0).Resolve);
        }

        [Fact]
        public void Odd_Counts_Give_One_Bye_To_The_Fewest_Byes_Then_The_Longest_Wait()
        {
            foreach (int players in new[] { 3, 5, 7 })
            foreach (ulong seed in Seeds)
            {
                var h = Harness.ForConclave(players, seed, Noisy(seed));
                var byes = new int[players];
                var lastBye = new int[players];
                int byeRounds = 0;
                h.Match.Events.Appended += e =>
                {
                    if (!(e is PairingsAnnouncedEvent p) || p.Attempt != 0 || p.BracketRound != 0) return;
                    var contenders = h.Conclave.RemainingContenders;
                    Assert.Equal(contenders.Count % 2, p.Byes.Count);
                    Assert.Equal(contenders.OrderBy(x => x), p.Pairs.SelectMany(s => new[] { s.Side0Player, s.Side1Player }).Concat(p.Byes).OrderBy(x => x));
                    if (p.Byes.Count == 0) return;
                    int bye = p.Byes[0];

                    // L61: fewest previous regular byes, then the longest wait since the last bye.
                    Assert.Equal(contenders.Min(c => byes[c]), byes[bye]);
                    Assert.Equal(contenders.Where(c => byes[c] == byes[bye]).Min(c => lastBye[c]), lastBye[bye]);
                    byes[bye]++;
                    lastBye[bye] = p.Round;
                    byeRounds++;
                };
                h.RunToEnd();
                Assert.True(byeRounds > 0);

                // L61: the bye preserves Resolve, pays baseline income, and gives no combat win or winner bonus.
                foreach (var result in h.Match.RoundResults.Where(r => r.BracketRound == 0))
                {
                    foreach (var entry in result.Entries.Where(e => e.Outcome == RoundOutcome.Bye))
                    {
                        Assert.Equal(0, entry.ResolveDelta);
                        Assert.Equal(result.Round == 1 ? 600 : 500, entry.BaselineIncome);
                        Assert.Equal(0, entry.NextWinnerBonus);
                        Assert.Equal(MatchRules.NoPlayer, entry.Opponent);
                    }
                }

                Assert.All(h.Match.Players, p => Assert.Equal(byes[p.Id], p.RegularByes));
            }
        }

        [Fact]
        public void Every_Regular_Pairing_Is_The_Complete_Enumeration_Optimum()
        {
            foreach (ulong seed in Seeds)
            {
                var h = Harness.ForConclave(8, seed, Noisy(seed));
                int checkedRounds = 0;
                h.Match.Events.Appended += e =>
                {
                    if (!(e is PairingsAnnouncedEvent p) || p.Attempt != 0 || p.BracketRound != 0) return;
                    var paired = p.Pairs.SelectMany(s => new[] { s.Side0Player, s.Side1Player }).OrderBy(x => x).ToList();
                    var candidates = paired.Select(id =>
                    {
                        var mp = h.Match.Player(id);
                        return new PairingCandidate(id, mp.Resolve, mp.CombatWins, mp.RecentOpponents(2));
                    }).ToList();
                    var chosen = PairingSolver.Cost(p.Pairs.Select(s => s.Pair).ToList(), candidates);
                    var all = PairingSolver.AllPairings(paired).Select(x => PairingSolver.Cost(x, candidates)).ToList();
                    Assert.Equal(all.Min(), chosen);
                    Assert.Equal(h.Match.CurrentPairing.EvaluatedCount, all.Count);

                    // L59: an immediate rematch only when every complete pairing has one.
                    if (all.Any(c => c.ImmediateRematches == 0)) Assert.Equal(0, chosen.ImmediateRematches);
                    if (paired.Count >= 4) Assert.Equal(0, chosen.ImmediateRematches);
                    checkedRounds++;
                };
                h.RunToEnd();
                Assert.True(checkedRounds >= 6);
            }
        }

        [Fact]
        public void Spawn_Sides_Alternate_And_Avoid_A_Third_Consecutive_Side()
        {
            foreach (ulong seed in Seeds)
            {
                var h = Harness.ForConclave(seed % 2 == 0 ? 8 : 7, seed, Noisy(seed));
                int seats = 0;
                h.Match.Events.Appended += e =>
                {
                    if (!(e is PairingsAnnouncedEvent p)) return;
                    foreach (var seat in p.Pairs)
                    {
                        var a = h.Match.Player(seat.Side0Player).Sides;
                        var b = h.Match.Player(seat.Side1Player).Sides;
                        var (chosenViolations, chosenRepeats) = SeatScore(a, 0, b, 1);
                        var (otherViolations, otherRepeats) = SeatScore(a, 1, b, 0);

                        // L59: no third consecutive side when the other seating avoids it; otherwise alternate when possible.
                        Assert.True(chosenViolations <= otherViolations);
                        if (chosenViolations == otherViolations) Assert.True(chosenRepeats <= otherRepeats);
                        seats++;
                    }
                };
                h.RunToEnd();
                Assert.True(seats > 20);

                // With alternation available every time, the side histories themselves never hold three in a row here.
                int longest = h.Match.Players.Max(p => LongestRun(p.Sides));
                Assert.InRange(longest, 1, 3);
            }
        }

        [Fact]
        public void Absent_Players_Keep_Playing_And_Nobody_Pauses()
        {
            // L55: a disconnected player's duels continue and are reported normally; other players are not held up.
            var h = Harness.ForConclave(8, 44, FakeDuelRunner.LowerIdWins);
            h.Match.Start();
            Assert.True(h.Match.SetConnected(3, false));
            h.RunToEnd();
            Assert.False(h.Match.Player(3).IsConnected);
            Assert.Contains(h.Runner.Started, s => s.Involves(3) && s.Round > 1);
            Assert.True(h.Match.Player(3).CompletedDuels > 1);
            Assert.Single(h.Match.Events.OfType<ConnectionChangedEvent>());
            Assert.False(h.Match.SetConnected(3, true)); // the 90 s reconnect window has long passed
        }

        private static (int Violations, int Repeats) SeatScore(IReadOnlyList<int> a, int sideA, IReadOnlyList<int> b, int sideB)
        {
            var (va, ra) = Score(a, sideA);
            var (vb, rb) = Score(b, sideB);
            return (va + vb, ra + rb);
        }

        private static (int, int) Score(IReadOnlyList<int> history, int side)
        {
            int streak = SideAssigner.TrailingStreak(history, out int last);
            bool repeat = streak > 0 && last == side;
            return (repeat && streak >= GlobalRules.Match.MaxConsecutiveSameSpawnSide ? 1 : 0, repeat ? 1 : 0);
        }

        private static int LongestRun(IReadOnlyList<int> sides)
        {
            int best = 0, run = 0;
            for (int i = 0; i < sides.Count; i++)
            {
                run = i > 0 && sides[i] == sides[i - 1] ? run + 1 : 1;
                best = Math.Max(best, run);
            }

            return best;
        }
    }
}
