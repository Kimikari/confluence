using System.Linq;
using Confluence.Core.Rules;
using Confluence.Match.Tests.Fakes;

namespace Confluence.Match.Tests
{
    /// <summary>Duel: best of seven with the Conclave round economy and the Duel schedules (spec L65, L193, L195-L200, L225).</summary>
    public class DuelMatchTests
    {
        /// <summary>Round 1 draws three times (half win each); later rounds: player 0 wins even rounds, player 1 odd ones.</summary>
        private static DuelScript ToThreeAndAHalfEach(DuelSetup s) =>
            s.Round == 1 ? DuelScript.Draw() : s.Round <= GlobalRules.Match.DuelBestOf ? DuelScript.Win(s.Round % 2 == 0 ? 0 : 1) : null;

        [Fact]
        public void Four_Wins_Clinch_The_Match()
        {
            var h = Harness.ForDuel(1, _ => DuelScript.Win(0));
            h.RunToEnd();
            Assert.Equal(4, h.Match.Round);
            Assert.Equal(4f, h.Duel.Score(0));
            Assert.Equal(0f, h.Duel.Score(1));
            Assert.Equal(0, h.Match.Result.Champion);
            Assert.Equal(1.0, h.Match.Result.For(0).Position);
            Assert.Equal(2.0, h.Match.Result.For(1).Position);
            Assert.Equal(8, h.Match.Result.DuelHalfWins0);
            Assert.False(h.Duel.IsRecordedDraw);
            Assert.Equal(4, h.Match.Events.OfType<DuelScoreChangedEvent>().Count());
            Assert.All(h.Runner.Started, s => Assert.Equal(MatchMode.Duel, s.Mode));
        }

        [Fact]
        public void Tiers_Unlock_In_Rounds_1_2_4_6_And_Drafts_Compress_Into_Rounds_1_To_6()
        {
            var h = Harness.ForDuel(2, s => DuelScript.Win(s.Round % 2 == 1 ? 0 : 1));
            h.RunToEnd();
            Assert.Equal(7, h.Match.Round); // 4-3 after the full seven
            Assert.Equal(0, h.Match.Result.Champion);

            var tiers = Enumerable.Range(1, 7).Select(r => h.ShopsIn(r).Select(s => s.HighestUnlockedTier).Distinct().Single());
            Assert.Equal(new[] { 1, 2, 2, 3, 3, 4, 4 }, tiers);

            var rarities = Enumerable.Range(1, 7).Select(r => h.Economy.Drafts.Where(d => d.Round == r).Select(d => (Rarity?)d.Rarity).Distinct().SingleOrDefault());
            Assert.Equal(new Rarity?[] { Rarity.Silver, Rarity.Silver, Rarity.Gold, Rarity.Gold, Rarity.Prismatic, Rarity.Prismatic, null }, rarities);
            Assert.All(h.Economy.Drafts, d => Assert.Equal(MatchMode.Duel, d.Mode));

            // Same round economy as Conclave (L65, L193): 600, then 500 plus 100 for the previous round's winner.
            Assert.All(h.ShopsIn(1), s => Assert.Equal(600, s.Income));
            for (int r = 2; r <= 7; r++)
            {
                int previousWinner = (r - 1) % 2 == 1 ? 0 : 1;
                Assert.All(h.ShopsIn(r), s => Assert.Equal(s.Player == previousWinner ? 600 : 500, s.Income));
            }
        }

        [Fact]
        public void A_Tied_Round_Replays_Twice_Then_Scores_A_Half_Win_Each()
        {
            var h = Harness.ForDuel(3, s => s.Round == 1 ? DuelScript.Draw() : DuelScript.Win(0));
            h.RunToEnd();

            var round1 = h.Runner.Started.Where(s => s.Round == 1).ToList();
            Assert.Equal(new[] { 0, 1, 2 }, round1.Select(s => s.Attempt));
            Assert.All(round1.Skip(1), s =>
            {
                Assert.Equal(PassKind.DrawReplay, s.PassKind);
                Assert.True(s.FullResourceRestore);
            });

            // L65: same loadouts and no extra income — one shop and one loadout freeze for the whole round.
            Assert.Equal(2, h.ShopsIn(1).Count);
            Assert.Equal(2, h.Economy.Finalized.Count(f => f.Round == 1));
            Assert.Equal(3, h.Arenas.Requests.Where(r => r.Round == 1).Select(r => r.ArenaSeed).Distinct().Count());

            var results = h.ResultsOf(1);
            Assert.Equal(3, results.Count);
            Assert.All(results.Take(2), r => Assert.All(r.Entries, e => Assert.Equal(RoundOutcome.Draw, e.Outcome)));
            Assert.All(results[2].Entries, e =>
            {
                Assert.Equal(RoundOutcome.HalfWin, e.Outcome);
                Assert.Equal(1, e.DuelHalfWins);
            });

            // Half wins advance the round number; no winner bonus follows a drawn round.
            Assert.All(h.ShopsIn(2), s => Assert.Equal(0, s.WinnerBonus));
            Assert.Equal(5, h.Match.Round); // 0.5 + 4 wins clinches after round 5
            Assert.Equal(4.5f, h.Duel.Score(0));
            Assert.Equal(0.5f, h.Duel.Score(1));
        }

        [Fact]
        public void A_Replay_Win_Scores_A_Full_Win_Without_The_Bonus()
        {
            var h = Harness.ForDuel(4, s => s.Round == 1 && s.Attempt == 0 ? DuelScript.Draw() : DuelScript.Win(1));
            h.RunUntilPhase(MatchPhase.Shop, 2);
            Assert.Equal(1f, h.Duel.Score(1));
            Assert.Equal(0f, h.Duel.Score(0));
            Assert.All(h.ShopsIn(2), s =>
            {
                Assert.Equal(500, s.BaselineIncome);
                Assert.Equal(0, s.WinnerBonus); // L193: replay rounds grant no winner bonus
            });
        }

        [Fact]
        public void Three_And_A_Half_All_Plays_One_Decider()
        {
            var h = Harness.ForDuel(5, s => ToThreeAndAHalfEach(s) ?? DuelScript.Win(1));
            h.RunUntilPhase(MatchPhase.Shop, 8);
            Assert.True(h.Duel.IsDecider);
            Assert.Equal(3.5f, h.Duel.Score(0));
            Assert.Equal(3.5f, h.Duel.Score(1));
            Assert.All(h.ShopsIn(8), s => Assert.Equal(4, s.HighestUnlockedTier));
            h.RunToEnd();

            Assert.Equal(8, h.Match.Round);
            Assert.DoesNotContain(h.Economy.Drafts, d => d.Round > 6);
            Assert.Equal(1, h.Match.Result.Champion);
            Assert.Equal(7, h.Match.Result.DuelHalfWins0);
            Assert.Equal(9, h.Match.Result.DuelHalfWins1);
            Assert.False(h.Duel.IsRecordedDraw);
        }

        [Fact]
        public void Two_Drawn_Deciders_Record_A_Match_Draw()
        {
            var h = Harness.ForDuel(6, s => ToThreeAndAHalfEach(s) ?? DuelScript.Draw());
            h.RunToEnd();

            Assert.Equal(8, h.Match.Round);
            Assert.Equal(2, h.Runner.Started.Count(s => s.Round == 8));
            Assert.True(h.Duel.IsRecordedDraw);
            Assert.Equal(MatchRules.NoPlayer, h.Match.Result.Champion);
            Assert.All(h.Match.Result.Placements, p => Assert.Equal(1.5, p.Position));
            Assert.Equal(7, h.Match.Result.DuelHalfWins0);
            Assert.Equal(7, h.Match.Result.DuelHalfWins1);
        }

        [Fact]
        public void Private_Rematches_Reuse_Or_Reset_Wizard_Choices()
        {
            var h = Harness.ForDuel(77, _ => DuelScript.Win(0), options: new DuelMatchOptions(ranked: true));
            h.RunToEnd();
            Assert.True(h.Match.Result.CountsForRating);

            var reuse = h.Duel.RematchOptions(reuseWizardChoices: true);
            var reset = h.Duel.RematchOptions(reuseWizardChoices: false);
            Assert.True(reuse.ReuseWizardChoices);
            Assert.False(reset.ReuseWizardChoices);
            Assert.True(reuse.IsRematch);
            Assert.Equal(77UL, reuse.RematchOfSeed);
            Assert.True(reuse.Ranked);
            Assert.False(DuelMatchOptions.Standard.IsRematch);

            // The rematch seed is reproducible and differs from the original.
            Assert.Equal(DuelMatch.RematchSeed(77), DuelMatch.RematchSeed(77));
            Assert.NotEqual(77UL, DuelMatch.RematchSeed(77));

            // Ranked and unranked Duel rules are distinguishable by fingerprint (L68).
            Assert.NotEqual(new DuelMatchOptions(ranked: true).Fingerprint, DuelMatchOptions.Standard.Fingerprint);
            Assert.Equal(reuse.Fingerprint, reset.Fingerprint);
        }
    }
}
