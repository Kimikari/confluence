using System.Linq;
using Confluence.Match.Tests.Fakes;

namespace Confluence.Match.Tests
{
    /// <summary>Disconnection never pauses the lobby (spec L55); failed rounds are voided for everyone (L427).</summary>
    public class ReconnectAndVoidTests
    {
        private const int ReconnectWindow = 90 * 60;

        [Fact]
        public void An_Absent_Player_Holds_The_Shop_Only_Within_The_Reconnect_Window()
        {
            // A 120 s custom shop outlasts the 90 s window. Player 4 disconnects and never readies.
            var h = Harness.ForConclave(ConclaveSettings.Custom(8, shopSec: 120f), 2, FakeDuelRunner.LowerIdWins).Started();
            h.Economy.ReadyAfterTicks[4] = null;
            Assert.True(h.Match.SetConnected(4, false));
            h.RunUntilPhase(MatchPhase.Draft, 1);

            // Within the window the absent player is not ready; past it, they count as ready and the shop closes early.
            Assert.Equal(ReconnectWindow + 1, h.Match.PhaseStartTick);
            Assert.True(h.Match.IsAbandoned(4));
            Assert.False(h.Match.SetConnected(4, true));
        }

        [Fact]
        public void Reconnecting_Inside_The_Window_Restores_The_Player()
        {
            var h = Harness.ForConclave(8, 3, FakeDuelRunner.LowerIdWins).Started();
            h.Match.SetConnected(2, false);
            h.RunUntil(() => h.Match.CurrentTick >= ReconnectWindow - 1);
            Assert.False(h.Match.IsAbandoned(2));
            Assert.True(h.Match.SetConnected(2, true));
            Assert.True(h.Match.Player(2).IsConnected);
            Assert.Equal(2, h.Match.Events.OfType<ConnectionChangedEvent>().Count());
        }

        [Fact]
        public void Voiding_A_Pass_Reruns_The_Same_Pairs_Without_Manufactured_Results()
        {
            var h = Harness.ForConclave(8, 4, s => DuelScript.Win(System.Math.Min(s.Side0Player, s.Side1Player), 600));
            h.RunUntilPhase(MatchPhase.Combat, 2);
            var resolveBefore = h.Match.Players.Select(p => p.Resolve).ToArray();
            var winsBefore = h.Match.Players.Select(p => p.CombatWins).ToArray();
            var seats = h.Match.CurrentPairs.ToList();
            var voidedDuels = h.Runner.Started.Where(s => s.Round == 2).Select(s => s.DuelId).ToList();

            h.Match.VoidCurrentPass("server migration failed");
            Assert.Equal(voidedDuels, h.Runner.Aborted);
            Assert.Equal(PassKind.VoidRerun, h.Match.CurrentPassKind);
            Assert.Equal(seats, h.Match.CurrentPairs);
            Assert.Equal(resolveBefore, h.Match.Players.Select(p => p.Resolve));
            Assert.Equal(winsBefore, h.Match.Players.Select(p => p.CombatWins));

            h.RunUntilPhase(MatchPhase.Results, 2);
            var rerun = h.Runner.Started.Where(s => s.Round == 2 && s.Attempt == 1).ToList();
            Assert.Equal(4, rerun.Count);
            Assert.All(rerun, s => Assert.Equal(PassKind.VoidRerun, s.PassKind));
            Assert.Equal(8, h.ShopsIn(2).Count); // no new shop
            var arenas = h.Arenas.Requests.Where(r => r.Round == 2).ToList();
            Assert.NotEqual(arenas[0].ArenaSeed, arenas[1].ArenaSeed);

            // The rerun's results stand in for the voided pass; voided duels never reach the feat ledger.
            var result = h.ResultsOf(2).Single();
            Assert.Equal(PassKind.VoidRerun, result.PassKind);
            Assert.True(result.RoundComplete);
            Assert.DoesNotContain(h.Economy.CompletedDuels, d => d.Round == 2 && d.Attempt == 0);
            Assert.Equal(8, h.Economy.CompletedDuels.Count(d => d.Round == 2));
            Assert.Single(h.Match.Events.OfType<RoundVoidedEvent>());

            // A win in a rerun is an ordinary combat win and earns the bonus (only draw replays withhold it, L193).
            h.RunUntilPhase(MatchPhase.Shop, 3);
            Assert.Equal(4, h.ShopsIn(3).Count(s => s.WinnerBonus == 100));
        }

        [Fact]
        public void A_Duel_That_Never_Reports_Voids_The_Pass_After_The_Combat_Cap()
        {
            var h = Harness.ForConclave(8, 5, s => s.Round == 1 && s.Attempt == 0 && s.Involves(0) ? DuelScript.Never() : FakeDuelRunner.LowerIdWins(s));
            h.RunUntilPhase(MatchPhase.Results, 1);
            var voided = Assert.Single(h.Match.Events.OfType<RoundVoidedEvent>());
            var combat = h.Phases(1).First(e => e.Phase == MatchPhase.Combat);
            Assert.Equal(combat.Tick + 90 * 60 + 60 + 1, voided.Tick);
            Assert.Equal(1, h.Match.Attempt);
            Assert.Equal(4, h.Runner.Aborted.Count);
            Assert.All(h.ResultsOf(1).Single().Entries, e => Assert.NotEqual(RoundOutcome.Bye, e.Outcome));
        }
    }
}
