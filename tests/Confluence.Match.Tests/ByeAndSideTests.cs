using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Random;

namespace Confluence.Match.Tests
{
    public class ByeAndSideTests
    {
        [Fact]
        public void Bye_Goes_To_Fewest_Previous_Byes()
        {
            var candidates = new[] { new ByeCandidate(0, 1, 2), new ByeCandidate(1, 0, 0), new ByeCandidate(2, 1, 1) };
            Assert.Equal(1, ByeSelector.Choose(candidates, new Rng(1)));
        }

        [Fact]
        public void Equal_Byes_Prefer_The_Longest_Wait_Since_A_Bye()
        {
            // L61: among equal counts, the player whose last bye is oldest has waited longest.
            var candidates = new[] { new ByeCandidate(0, 1, 5), new ByeCandidate(1, 1, 2), new ByeCandidate(2, 1, 4) };
            Assert.Equal(1, ByeSelector.Choose(candidates, new Rng(1)));
        }

        [Fact]
        public void Exact_Bye_Ties_Are_Seeded()
        {
            var candidates = Enumerable.Range(0, 5).Select(i => new ByeCandidate(i, 0, 0)).ToList();
            Assert.Equal(ByeSelector.Choose(candidates, new Rng(11)), ByeSelector.Choose(candidates, new Rng(11)));
            var chosen = new HashSet<int>();
            for (ulong s = 0; s < 50; s++) chosen.Add(ByeSelector.Choose(candidates, new Rng(s)));
            Assert.Equal(5, chosen.Count);
        }

        [Fact]
        public void Sides_Alternate_When_Possible()
        {
            var seat = SideAssigner.Assign(new PlayerPair(0, 1), new[] { 0 }, new[] { 1 }, new Rng(1));
            Assert.Equal(new SeatedPair(1, 0), seat);
            seat = SideAssigner.Assign(new PlayerPair(0, 1), new[] { 1, 0, 1 }, new[] { 1, 1, 0 }, new Rng(1));
            Assert.Equal(new SeatedPair(0, 1), seat);
        }

        [Fact]
        public void A_Third_Consecutive_Side_Is_Avoided_When_The_Other_Seating_Allows_It()
        {
            // Both last spawned on side 0. Player 1 already has two in a row, so player 0 repeats instead.
            for (ulong s = 0; s < 20; s++)
            {
                var seat = SideAssigner.Assign(new PlayerPair(0, 1), new[] { 1, 0 }, new[] { 0, 0 }, new Rng(s));
                Assert.Equal(new SeatedPair(0, 1), seat);
            }

            // Both have two on side 0: no legal alternative, one of them must take a third.
            var forced = SideAssigner.Assign(new PlayerPair(0, 1), new[] { 0, 0 }, new[] { 0, 0 }, new Rng(4));
            Assert.True(forced.Side0Player == 0 || forced.Side0Player == 1);
        }

        [Fact]
        public void Equal_Side_Claims_Are_Seeded()
        {
            var seats = new HashSet<SeatedPair>();
            for (ulong s = 0; s < 30; s++) seats.Add(SideAssigner.Assign(new PlayerPair(2, 5), new int[0], new int[0], new Rng(s)));
            Assert.Equal(2, seats.Count);
            Assert.Equal(SideAssigner.Assign(new PlayerPair(2, 5), new int[0], new int[0], new Rng(8)),
                SideAssigner.Assign(new PlayerPair(2, 5), new int[0], new int[0], new Rng(8)));
        }

        [Fact]
        public void Trailing_Streak()
        {
            Assert.Equal(0, SideAssigner.TrailingStreak(new int[0], out int none));
            Assert.Equal(-1, none);
            Assert.Equal(3, SideAssigner.TrailingStreak(new[] { 0, 1, 1, 1 }, out int last));
            Assert.Equal(1, last);
        }
    }
}
