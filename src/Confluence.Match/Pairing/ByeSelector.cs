using System;
using System.Collections.Generic;
using Confluence.Core.Random;

namespace Confluence.Match
{
    /// <summary>A contender's bye history for regular rounds.</summary>
    public sealed class ByeCandidate
    {
        public ByeCandidate(int player, int previousByes, int lastByeRound)
        {
            if (previousByes < 0) throw new ArgumentOutOfRangeException(nameof(previousByes));
            if (lastByeRound < 0) throw new ArgumentOutOfRangeException(nameof(lastByeRound));
            Player = player;
            PreviousByes = previousByes;
            LastByeRound = lastByeRound;
        }

        public int Player { get; }

        /// <summary>Regular-round byes so far.</summary>
        public int PreviousByes { get; }

        /// <summary>Round of the most recent regular bye, 0 when the player never had one.</summary>
        public int LastByeRound { get; }
    }

    /// <summary>
    /// Odd-count regular bye (spec L61): the fewest previous regular byes, then the player who has waited longest since a
    /// bye (earliest last bye; never having had one ties with everyone else who never had one), then a seeded tie.
    /// </summary>
    public static class ByeSelector
    {
        public static int Choose(IReadOnlyList<ByeCandidate> candidates, Rng tieBreak)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            if (tieBreak == null) throw new ArgumentNullException(nameof(tieBreak));
            if (candidates.Count == 0) throw new ArgumentException("A bye needs at least one contender.", nameof(candidates));

            var tied = new List<ByeCandidate>();
            foreach (var c in candidates)
            {
                int cmp = tied.Count == 0 ? -1 : Compare(c, tied[0]);
                if (cmp < 0) tied.Clear();
                if (cmp <= 0) tied.Add(c);
            }

            tied.Sort((x, y) => x.Player.CompareTo(y.Player));
            return tied.Count == 1 ? tied[0].Player : tied[tieBreak.NextInt(tied.Count)].Player;
        }

        // Negative when x has the stronger claim to the bye.
        private static int Compare(ByeCandidate x, ByeCandidate y)
        {
            int c = x.PreviousByes.CompareTo(y.PreviousByes);
            return c != 0 ? c : x.LastByeRound.CompareTo(y.LastByeRound);
        }
    }
}
