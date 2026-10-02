using System;
using System.Linq;

namespace Confluence.Match.Tests
{
    public class BracketTests
    {
        [Fact]
        public void Standard_Slot_Order_Keeps_Top_Seeds_Apart()
        {
            Assert.Equal(new[] { 1, 2 }, Bracket.SlotOrder(2));
            Assert.Equal(new[] { 1, 4, 2, 3 }, Bracket.SlotOrder(4));
            Assert.Equal(new[] { 1, 8, 4, 5, 2, 7, 3, 6 }, Bracket.SlotOrder(8));
        }

        [Fact]
        public void Seeds_Use_Resolve_Then_Wins_Then_Fewer_Byes_Then_Lobby_Seed()
        {
            // Spec L38.
            var entries = new[]
            {
                new BracketSeedEntry(0, 3, 5, 0, 900),
                new BracketSeedEntry(1, 5, 2, 1, 100),
                new BracketSeedEntry(2, 3, 6, 1, 800),
                new BracketSeedEntry(3, 3, 5, 1, 50),
                new BracketSeedEntry(4, 3, 5, 0, 200),
            };
            Assert.Equal(new[] { 1, 2, 4, 0, 3 }, Bracket.SeedOrder(entries));
        }

        [Theory]
        [InlineData(2, 2, 1, new int[0])]
        [InlineData(3, 4, 2, new[] { 1 })]
        [InlineData(5, 8, 3, new[] { 1, 2, 3 })]
        [InlineData(6, 8, 3, new[] { 1, 2 })]
        [InlineData(7, 8, 3, new[] { 1 })]
        [InlineData(8, 8, 3, new int[0])]
        public void Highest_Seeds_Receive_The_Byes_To_A_Power_Of_Two(int entrants, int size, int rounds, int[] byeSeeds)
        {
            var bracket = new Bracket(Enumerable.Range(10, entrants).ToList()); // player 10 + k is seed k + 1
            Assert.Equal(size, bracket.Size);
            Assert.Equal(rounds, bracket.TotalRounds);
            var byes = bracket.CurrentMatches().Where(m => !m.IsContested).Select(m => bracket.SeedOf(m.UncontestedPlayer)).OrderBy(s => s).ToArray();
            Assert.Equal(byeSeeds, byes);
            Assert.All(bracket.CurrentMatches().Where(m => m.IsContested), m => Assert.True(bracket.SeedOf(m.HigherSeedPlayer) < bracket.SeedOf(m.LowerSeedPlayer)));
        }

        [Fact]
        public void At_Most_Three_Bracket_Rounds()
        {
            Assert.Throws<ArgumentException>(() => new Bracket(Enumerable.Range(0, 9).ToList()));
            Assert.Throws<ArgumentException>(() => new Bracket(new[] { 0 }));
        }

        [Fact]
        public void Winners_Advance_And_A_Shared_Finish_Leaves_A_Walkover()
        {
            var bracket = new Bracket(Enumerable.Range(0, 8).ToList()); // player k is seed k + 1
            var quarter = bracket.CurrentMatches();
            Assert.Equal(new[] { (0, 7), (3, 4), (1, 6), (2, 5) }, quarter.Select(m => (m.HigherSeedPlayer, m.LowerSeedPlayer)).ToArray());

            // 3v4 drew twice (nobody advances); seed 6 upsets seed 3.
            bracket.CompleteRound(new[] { 0, MatchRules.NoPlayer, 1, 5 });
            Assert.Equal(2, bracket.CurrentRound);
            Assert.Equal(3, bracket.RemainingPlayers);
            var semi = bracket.CurrentMatches();
            Assert.False(semi[0].IsContested);
            Assert.Equal(0, semi[0].UncontestedPlayer);
            Assert.True(semi[1].IsContested);
            Assert.Equal(1, semi[1].HigherSeedPlayer);

            Assert.Throws<ArgumentException>(() => bracket.CompleteRound(new[] { 0, 7 }));
            bracket.CompleteRound(new[] { 0, 5 });
            Assert.False(bracket.IsComplete);
            bracket.CompleteRound(new[] { 5 });
            Assert.True(bracket.IsComplete);
            Assert.Equal(3, bracket.RoundsPlayed);
        }

        [Fact]
        public void Complete_When_One_Player_Remains()
        {
            var bracket = new Bracket(new[] { 4, 7 });
            bracket.CompleteRound(new[] { 7 });
            Assert.True(bracket.IsComplete);
            Assert.Equal(new[] { 7 }, bracket.Slots.ToArray());
        }
    }
}
