using System;
using System.Collections.Generic;
using Confluence.Core.Random;
using Confluence.Core.Rules;

namespace Confluence.Match
{
    /// <summary>
    /// Spawn sides for one pair (spec L59): sides alternate when possible, and no player receives the same side more than
    /// twice consecutively when the other seating avoids it. Histories list each player's sides in fight order.
    /// </summary>
    public static class SideAssigner
    {
        public static SeatedPair Assign(PlayerPair pair, IReadOnlyList<int> sidesOfA, IReadOnlyList<int> sidesOfB, Rng tieBreak)
        {
            if (sidesOfA == null) throw new ArgumentNullException(nameof(sidesOfA));
            if (sidesOfB == null) throw new ArgumentNullException(nameof(sidesOfB));
            if (tieBreak == null) throw new ArgumentNullException(nameof(tieBreak));

            // Option 0 seats A on side 0; option 1 seats B on side 0.
            Score(sidesOfA, 0, out int violationsA0, out int repeatsA0);
            Score(sidesOfB, 1, out int violationsB1, out int repeatsB1);
            Score(sidesOfA, 1, out int violationsA1, out int repeatsA1);
            Score(sidesOfB, 0, out int violationsB0, out int repeatsB0);

            int violations0 = violationsA0 + violationsB1, repeats0 = repeatsA0 + repeatsB1;
            int violations1 = violationsA1 + violationsB0, repeats1 = repeatsA1 + repeatsB0;
            int c = violations0.CompareTo(violations1);
            if (c == 0) c = repeats0.CompareTo(repeats1);
            bool aOnSideZero = c < 0 || (c == 0 && !tieBreak.NextBool());
            return aOnSideZero ? new SeatedPair(pair.A, pair.B) : new SeatedPair(pair.B, pair.A);
        }

        /// <summary>Trailing run of identical sides (0 for an empty history).</summary>
        public static int TrailingStreak(IReadOnlyList<int> sides, out int lastSide)
        {
            if (sides == null) throw new ArgumentNullException(nameof(sides));
            lastSide = -1;
            if (sides.Count == 0) return 0;
            lastSide = sides[sides.Count - 1];
            int streak = 0;
            for (int i = sides.Count - 1; i >= 0 && sides[i] == lastSide; i--) streak++;
            return streak;
        }

        private static void Score(IReadOnlyList<int> history, int side, out int violations, out int repeats)
        {
            int streak = TrailingStreak(history, out int last);
            bool repeat = streak > 0 && last == side;
            repeats = repeat ? 1 : 0;
            violations = repeat && streak + 1 > GlobalRules.Match.MaxConsecutiveSameSpawnSide ? 1 : 0;
        }
    }
}
