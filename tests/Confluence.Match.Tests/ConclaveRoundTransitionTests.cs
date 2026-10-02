using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Rules;
using Confluence.Match.Tests.Fakes;

namespace Confluence.Match.Tests
{
    /// <summary>Phase order, timers, early ready, income, drafts, and reveal across Conclave round transitions (spec L44-L49, L193, L216).</summary>
    public class ConclaveRoundTransitionTests
    {
        private const int ShopTicks = 35 * 60, DraftTicks = 30 * 60, AlcoveTicks = 5 * 60, CombatTicks = 90 * 60, ResultsTicks = 8 * 60;
        private const int DuelResolvesAfter = 60; // FakeDuelRunner default combat tick

        private static (MatchPhase, int, int)[] Phases(Harness h, int round) =>
            h.Phases(round).Select(e => (e.Phase, e.Tick, e.EndTick)).ToArray();

        [Fact]
        public void Phases_Run_Their_Full_Timers_When_Nobody_Readies()
        {
            var h = Harness.ForConclave(8, 11, FakeDuelRunner.LowerIdWins, readyAfter: null);
            h.RunUntilPhase(MatchPhase.Results, 2);

            // Round 1 is a Silver draft round (L216): reveal, shop 35 s, draft 30 s, alcove 5 s, combat, results 8 s.
            int draftAt = ShopTicks, alcoveAt = draftAt + DraftTicks, combatAt = alcoveAt + AlcoveTicks;
            int resultsAt = combatAt + DuelResolvesAfter, round2 = resultsAt + ResultsTicks;
            Assert.Equal(new[]
            {
                (MatchPhase.Reveal, 0, 0),
                (MatchPhase.Shop, 0, ShopTicks),
                (MatchPhase.Draft, draftAt, alcoveAt),
                (MatchPhase.Alcove, alcoveAt, combatAt),
                (MatchPhase.Combat, combatAt, combatAt + CombatTicks),
                (MatchPhase.Results, resultsAt, round2),
            }, Phases(h, 1));

            // Round 2 has no draft: the shop leads straight to the alcove.
            int alcove2 = round2 + ShopTicks, combat2 = alcove2 + AlcoveTicks;
            Assert.Equal(new[]
            {
                (MatchPhase.Reveal, round2, round2),
                (MatchPhase.Shop, round2, round2 + ShopTicks),
                (MatchPhase.Alcove, alcove2, combat2),
                (MatchPhase.Combat, combat2, combat2 + CombatTicks),
                (MatchPhase.Results, combat2 + DuelResolvesAfter, combat2 + DuelResolvesAfter + ResultsTicks),
            }, Phases(h, 2));

            // Duels start when the alcoves are entered; the ward lasts the alcove.
            Assert.All(h.Runner.Started.Where(s => s.Round == 1), s => Assert.Equal(AlcoveTicks, s.AlcoveTicks));
            Assert.Equal(4, h.Runner.Started.Count(s => s.Round == 1));
        }

        [Fact]
        public void Combat_Lasts_Until_The_Last_Duel_Resolves()
        {
            int Delay(DuelSetup s) => 100 * (s.Side0Player + s.Side1Player);
            var h = Harness.ForConclave(8, 3, s => DuelScript.Win(System.Math.Min(s.Side0Player, s.Side1Player), Delay(s)));
            h.RunUntilPhase(MatchPhase.Results, 1);
            var combat = h.Phases(1).Single(e => e.Phase == MatchPhase.Combat);
            var results = h.Phases(1).Single(e => e.Phase == MatchPhase.Results);
            int slowest = h.Runner.Started.Where(s => s.Round == 1).Max(Delay);
            Assert.Equal(combat.Tick + slowest, results.Tick);
        }

        [Fact]
        public void Early_Ready_Advances_Only_When_Every_Active_Player_Is_Ready()
        {
            // Everyone ready one tick after opening: shop and draft each close on the next tick (L45).
            var fast = Harness.ForConclave(8, 1, FakeDuelRunner.LowerIdWins, readyAfter: 1);
            fast.RunUntilPhase(MatchPhase.Alcove, 1);
            Assert.Equal(new[] { MatchPhase.Reveal, MatchPhase.Shop, MatchPhase.Draft, MatchPhase.Alcove }, Phases(fast, 1).Select(p => p.Item1));
            Assert.Equal(1, Phases(fast, 1)[2].Item2);
            Assert.Equal(2, Phases(fast, 1)[3].Item2);

            // One player never readies: both phases run their full time.
            var held = Harness.ForConclave(8, 1, FakeDuelRunner.LowerIdWins, readyAfter: 1);
            held.Economy.ReadyAfterTicks[5] = null;
            held.RunUntilPhase(MatchPhase.Alcove, 1);
            Assert.Equal(ShopTicks, Phases(held, 1)[2].Item2);
            Assert.Equal(ShopTicks + DraftTicks, Phases(held, 1)[3].Item2);

            // The last player readies at 600 ticks: the shop ends then, not before.
            var late = Harness.ForConclave(8, 1, FakeDuelRunner.LowerIdWins, readyAfter: 1);
            late.Economy.ReadyAfterTicks[2] = 600;
            late.RunUntilPhase(MatchPhase.Draft, 1);
            Assert.Equal(600, late.Match.PhaseStartTick);
        }

        [Fact]
        public void Bye_Players_Shop_And_Count_For_Early_Ready()
        {
            // L61: bye players may shop; L45: every active player must be ready.
            var h = Harness.ForConclave(7, 2, FakeDuelRunner.LowerIdWins, readyAfter: 1).Started();
            int bye = Assert.Single(h.Match.CurrentByes);
            h.Economy.ReadyAfterTicks[bye] = null;
            h.RunUntilPhase(MatchPhase.Draft, 1);
            Assert.Equal(ShopTicks, h.Match.PhaseStartTick);
            Assert.Contains(h.ShopsIn(1), s => s.Player == bye);
            Assert.Contains(h.Economy.Drafts, d => d.Player == bye && d.Round == 1);
            Assert.DoesNotContain(h.Runner.Started, s => s.Round == 1 && s.Involves(bye));
        }

        [Fact]
        public void Eliminated_Players_No_Longer_Shop_Or_Hold_The_Lobby()
        {
            var h = Harness.ForConclave(8, 4, FakeDuelRunner.ByStrength(8, 7, 6, 5, 4, 3, 2, 1));
            var contendersAtRound = new Dictionary<int, List<int>>();
            h.Match.Events.Appended += e =>
            {
                if (e is PairingsAnnouncedEvent p && p.Attempt == 0) contendersAtRound[p.Round] = h.Conclave.RemainingContenders.ToList();
            };
            h.RunToEnd();
            Assert.True(h.Match.Players.Any(p => p.IsEliminated && p.Placement.Round < h.Match.Round));
            foreach (var kv in contendersAtRound)
            {
                Assert.Equal(kv.Value, h.ShopsIn(kv.Key).Select(s => s.Player).OrderBy(p => p));
                // With everyone remaining ready after one tick, an eliminated player never holds a shop open.
                var shop = h.Phases(kv.Key).Single(e => e.Phase == MatchPhase.Shop);
                var next = h.Phases(kv.Key).First(e => e.Tick > shop.Tick || (e.Phase != MatchPhase.Shop && e.Phase != MatchPhase.Reveal && e.Tick >= shop.Tick));
                Assert.Equal(shop.Tick + 1, next.Tick);
            }
        }

        [Fact]
        public void Income_Is_600_Then_500_With_A_100_Bonus_For_Winning_The_Previous_Round()
        {
            var h = Harness.ForConclave(8, 21, FakeDuelRunner.ByStrength(8, 7, 6, 5, 4, 3, 2, 1));
            h.RunUntilPhase(MatchPhase.Results, 3);

            // L193: 600 before the first shop.
            Assert.All(h.ShopsIn(1), s =>
            {
                Assert.True(s.IsFirstShop);
                Assert.Equal(600, s.BaselineIncome);
                Assert.Equal(0, s.WinnerBonus);
            });

            foreach (int round in new[] { 2, 3 })
            {
                var previous = h.ResultsOf(round - 1).Single();
                Assert.All(h.ShopsIn(round), s =>
                {
                    bool won = previous.For(s.Player).Outcome == RoundOutcome.Win;
                    Assert.False(s.IsFirstShop);
                    Assert.Equal(500, s.BaselineIncome);
                    Assert.Equal(won ? 100 : 0, s.WinnerBonus);
                    Assert.Equal(s.BaselineIncome + s.WinnerBonus, s.Income);
                    Assert.Equal(500, previous.For(s.Player).NextBaselineIncome);
                    Assert.Equal(won ? 100 : 0, previous.For(s.Player).NextWinnerBonus);
                });
            }

            // The results screen repeats what each shop paid (L361).
            var r2 = h.ResultsOf(2).Single();
            Assert.All(h.ShopsIn(2), s => Assert.Equal(s.Income, r2.For(s.Player).BaselineIncome + r2.For(s.Player).WinnerBonus));
            Assert.All(Enumerable.Range(0, 8), p => Assert.Equal(h.Economy.Shops.Where(s => s.Player == p).Sum(s => s.Income), h.Economy.Crowns[p]));
        }

        [Fact]
        public void Draws_And_Byes_Earn_No_Winner_Bonus()
        {
            // Round 1: every duel is a draw. Draws remove the scheduled loss from both (L36) and grant no bonus (L193).
            var h = Harness.ForConclave(7, 8, s => s.Round == 1 ? DuelScript.Draw() : FakeDuelRunner.LowerIdWins(s));
            h.RunUntilPhase(MatchPhase.Results, 2);
            var r1 = h.ResultsOf(1).Single();
            int bye = r1.Entries.Single(e => e.Outcome == RoundOutcome.Bye).Player;
            Assert.All(r1.Entries.Where(e => e.Outcome == RoundOutcome.Draw), e => Assert.Equal(-1, e.ResolveDelta));
            Assert.Equal(6, r1.Entries.Count(e => e.Outcome == RoundOutcome.Draw));

            // L61: the bye keeps Resolve, takes baseline income, and no bonus.
            var byeEntry = r1.For(bye);
            Assert.Equal(0, byeEntry.ResolveDelta);
            Assert.Equal(600, byeEntry.BaselineIncome);
            Assert.Equal(0, byeEntry.NextWinnerBonus);
            Assert.All(h.ShopsIn(2), s =>
            {
                Assert.Equal(500, s.BaselineIncome);
                Assert.Equal(0, s.WinnerBonus);
            });
        }

        [Fact]
        public void Custom_Starting_Crowns_Replace_The_First_Income_Only()
        {
            var settings = ConclaveSettings.Custom(4, startingCrowns: 1000);
            var h = Harness.ForConclave(settings, 5, FakeDuelRunner.LowerIdWins);
            h.RunUntilPhase(MatchPhase.Shop, 2);
            Assert.All(h.ShopsIn(1), s => Assert.Equal(1000, s.BaselineIncome));
            Assert.All(h.ShopsIn(2), s => Assert.Equal(500, s.BaselineIncome));
        }

        [Fact]
        public void Drafts_Open_Only_On_The_Scheduled_Rounds_With_The_Shared_Rarity()
        {
            var h = Harness.ForConclave(8, 6, FakeDuelRunner.ByStrength(8, 7, 6, 5, 4, 3, 2, 1));
            var activeAt = new Dictionary<int, List<int>>();
            h.Match.Events.Appended += e =>
            {
                if (e is PairingsAnnouncedEvent p && p.Attempt == 0) activeAt[p.Round] = h.Conclave.RemainingContenders.ToList();
            };
            h.RunToEnd();
            Assert.True(h.Match.Round >= 9, "the scripted match reaches the Prismatic stage");

            var expected = new Dictionary<int, Rarity> { { 1, Rarity.Silver }, { 3, Rarity.Silver }, { 5, Rarity.Gold }, { 7, Rarity.Gold }, { 9, Rarity.Prismatic }, { 11, Rarity.Prismatic } };
            foreach (var kv in activeAt)
            {
                var drafts = h.Economy.Drafts.Where(d => d.Round == kv.Key).ToList();
                bool hasDraftPhase = h.Phases(kv.Key).Any(e => e.Phase == MatchPhase.Draft);
                if (expected.TryGetValue(kv.Key, out var rarity))
                {
                    Assert.True(hasDraftPhase);
                    Assert.Equal(kv.Value, drafts.Select(d => d.Player).OrderBy(p => p));
                    Assert.All(drafts, d => Assert.Equal(rarity, d.Rarity));
                }
                else
                {
                    Assert.False(hasDraftPhase);
                    Assert.Empty(drafts);
                }
            }
        }

        [Fact]
        public void Reveal_Shows_Opponent_Arena_Resolve_And_The_Completed_Previous_Loadout()
        {
            var h = Harness.ForConclave(8, 9, FakeDuelRunner.LowerIdWins);
            h.RunUntilPhase(MatchPhase.Shop, 1);
            var first = h.Match.GetReveal(0);
            Assert.False(first.IsBye);
            Assert.Null(first.OpponentLoadout); // no previous round yet
            Assert.Equal(h.Match.CurrentArena.ArenaId, first.Arena.ArenaId);
            Assert.Equal(8, first.OwnResolve);

            h.RunUntilPhase(MatchPhase.Shop, 2);
            foreach (var pair in h.Match.CurrentPairs)
            {
                var reveal = h.Match.GetReveal(pair.Side0Player);
                Assert.Equal(pair.Side1Player, reveal.Opponent);
                Assert.Equal(0, reveal.Side);
                Assert.Equal(h.Match.Player(pair.Side1Player).Resolve, reveal.OpponentResolve);
                // L44: the opponent's completed previous-round loadout; live purchases stay hidden.
                Assert.Equal("EQP-001", reveal.OpponentLoadout.EquipmentIds.Single());
                Assert.Equal("EQP-001", h.Match.VisibleOpponentLoadout(pair.Side0Player).EquipmentIds.Single());
            }

            // L46: from the alcove on, the opponent's final loadout for this round is visible.
            h.RunUntilPhase(MatchPhase.Alcove, 2);
            var seat = h.Match.CurrentPairs[0];
            Assert.Equal("EQP-002", h.Match.VisibleOpponentLoadout(seat.Side0Player).EquipmentIds.Single());
            Assert.Equal(8, h.Match.Events.OfType<LoadoutFinalizedEvent>().Count(e => e.Round == 2));
        }

        [Fact]
        public void Every_Pair_Of_A_Pass_Shares_The_Round_Arena_And_Diameters_Stay_Balanced()
        {
            var h = Harness.ForConclave(8, 13, FakeDuelRunner.ByStrength(8, 7, 6, 5, 4, 3, 2, 1));
            h.RunToEnd();
            foreach (var group in h.Runner.Started.GroupBy(s => (s.Round, s.Attempt)))
            {
                Assert.Single(group.Select(s => s.Arena.ArenaId).Distinct()); // L34
                Assert.All(group, s => Assert.Equal(h.Match.ArenaDiameters[s.Round - 1], s.Arena.Diameter));
            }

            Assert.True(Schedules.IsBalancedDiameterSequence(h.Match.ArenaDiameters, out string reason), reason);
            Assert.Equal(h.Arenas.Requests.Count, h.Arenas.Requests.Select(r => r.ArenaSeed).Distinct().Count());
        }
    }
}
