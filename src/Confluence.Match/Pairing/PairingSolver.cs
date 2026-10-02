using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Confluence.Core.Random;
using Confluence.Core.Rules;

namespace Confluence.Match
{
    /// <summary>A contender's pairing inputs for one regular round (spec L59).</summary>
    public sealed class PairingCandidate
    {
        public PairingCandidate(int player, int resolve, int combatWins, IEnumerable<int> recentOpponents)
        {
            Player = player;
            Resolve = resolve;
            CombatWins = combatWins;
            var recent = new List<int>();
            if (recentOpponents != null)
            {
                foreach (int opponent in recentOpponents)
                {
                    if (recent.Count == GlobalRules.Match.PairingRepeatWindow) break;
                    recent.Add(opponent);
                }
            }

            RecentOpponents = new ReadOnlyCollection<int>(recent);
        }

        public int Player { get; }
        public int Resolve { get; }
        public int CombatWins { get; }

        /// <summary>Opponents of the player's last two fights, most recent first (byes skipped).</summary>
        public IReadOnlyList<int> RecentOpponents { get; }

        public int LastOpponent => RecentOpponents.Count > 0 ? RecentOpponents[0] : MatchRules.NoPlayer;
    }

    /// <summary>
    /// Lexicographic pairing cost (spec L59): immediate rematches are the largest cost, repeating either of the previous
    /// two opponents is next, then total Resolve difference, then total combat-win difference.
    /// </summary>
    public readonly struct PairingCost : IComparable<PairingCost>, IEquatable<PairingCost>
    {
        public PairingCost(int immediateRematches, int recentRepeats, int resolveDifference, int combatWinDifference)
        {
            ImmediateRematches = immediateRematches;
            RecentRepeats = recentRepeats;
            ResolveDifference = resolveDifference;
            CombatWinDifference = combatWinDifference;
        }

        /// <summary>Pairs in which either player's most recent opponent is the other.</summary>
        public int ImmediateRematches { get; }

        /// <summary>Pairs in which either player is among the other's previous two opponents.</summary>
        public int RecentRepeats { get; }

        /// <summary>Sum over pairs of the absolute Resolve difference.</summary>
        public int ResolveDifference { get; }

        /// <summary>Sum over pairs of the absolute combat-win difference.</summary>
        public int CombatWinDifference { get; }

        public static PairingCost operator +(PairingCost x, PairingCost y) => new PairingCost(
            x.ImmediateRematches + y.ImmediateRematches, x.RecentRepeats + y.RecentRepeats,
            x.ResolveDifference + y.ResolveDifference, x.CombatWinDifference + y.CombatWinDifference);

        public int CompareTo(PairingCost other)
        {
            int c = ImmediateRematches.CompareTo(other.ImmediateRematches);
            if (c != 0) return c;
            c = RecentRepeats.CompareTo(other.RecentRepeats);
            if (c != 0) return c;
            c = ResolveDifference.CompareTo(other.ResolveDifference);
            return c != 0 ? c : CombatWinDifference.CompareTo(other.CombatWinDifference);
        }

        public bool Equals(PairingCost other) => CompareTo(other) == 0;
        public override bool Equals(object obj) => obj is PairingCost other && Equals(other);
        public override int GetHashCode() => unchecked(((ImmediateRematches * 31 + RecentRepeats) * 31 + ResolveDifference) * 31 + CombatWinDifference);
        public override string ToString() => "rematch=" + ImmediateRematches + " repeat=" + RecentRepeats + " resolve=" + ResolveDifference + " wins=" + CombatWinDifference;
    }

    /// <summary>The chosen pairing of one round plus how it was chosen.</summary>
    public sealed class PairingSolution
    {
        internal PairingSolution(IReadOnlyList<PlayerPair> pairs, PairingCost cost, int evaluated, int optimalCount, int chosenIndex)
        {
            Pairs = pairs;
            Cost = cost;
            EvaluatedCount = evaluated;
            OptimalCount = optimalCount;
            ChosenIndex = chosenIndex;
        }

        /// <summary>Pairs ordered by their lower player id.</summary>
        public IReadOnlyList<PlayerPair> Pairs { get; }

        public PairingCost Cost { get; }

        /// <summary>Number of complete pairings evaluated: (n-1)!! for n players.</summary>
        public int EvaluatedCount { get; }

        /// <summary>Pairings sharing the minimum cost; above one, the round seed broke the tie.</summary>
        public int OptimalCount { get; }

        /// <summary>Index of the chosen pairing among the optimal ones, in enumeration order.</summary>
        public int ChosenIndex { get; }
    }

    /// <summary>
    /// Regular-round pairing (spec L59). Evaluates the complete set of perfect pairings of the contenders, never a
    /// greedy one, and picks a minimum-cost pairing; exact ties are broken with the recorded round seed.
    /// </summary>
    public static class PairingSolver
    {
        /// <summary>Cost of seating two candidates against each other.</summary>
        public static PairingCost PairCost(PairingCandidate a, PairingCandidate b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            bool rematch = a.LastOpponent == b.Player || b.LastOpponent == a.Player;
            bool repeat = Contains(a.RecentOpponents, b.Player) || Contains(b.RecentOpponents, a.Player);
            return new PairingCost(rematch ? 1 : 0, repeat ? 1 : 0, Math.Abs(a.Resolve - b.Resolve), Math.Abs(a.CombatWins - b.CombatWins));
        }

        /// <summary>Total cost of a complete pairing.</summary>
        public static PairingCost Cost(IReadOnlyList<PlayerPair> pairs, IReadOnlyList<PairingCandidate> candidates)
        {
            if (pairs == null) throw new ArgumentNullException(nameof(pairs));
            var byPlayer = Index(candidates);
            var total = new PairingCost();
            foreach (var pair in pairs) total += PairCost(byPlayer[pair.A], byPlayer[pair.B]);
            return total;
        }

        /// <summary>Picks a minimum-cost complete pairing of an even number of contenders.</summary>
        public static PairingSolution Solve(IReadOnlyList<PairingCandidate> candidates, Rng tieBreak)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            if (tieBreak == null) throw new ArgumentNullException(nameof(tieBreak));
            if (candidates.Count == 0 || candidates.Count % 2 != 0)
            {
                throw new ArgumentException("Pairing needs a positive even number of contenders; assign the bye first.", nameof(candidates));
            }

            var sorted = new List<PairingCandidate>(candidates);
            sorted.Sort((x, y) => x.Player.CompareTo(y.Player));
            for (int i = 1; i < sorted.Count; i++)
            {
                if (sorted[i].Player == sorted[i - 1].Player) throw new ArgumentException("Duplicate contender " + sorted[i].Player + ".");
            }

            int n = sorted.Count;
            var pairCost = new PairingCost[n, n];
            for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                pairCost[i, j] = PairCost(sorted[i], sorted[j]);

            var optimal = new List<int[]>();
            var best = new PairingCost();
            bool haveBest = false;
            int evaluated = 0;
            var partner = new int[n];
            for (int i = 0; i < n; i++) partner[i] = -1;

            // Depth-first over every perfect matching: the lowest unpaired index takes each later unpaired index in turn.
            void Visit(PairingCost costSoFar)
            {
                int first = -1;
                for (int i = 0; i < n; i++)
                {
                    if (partner[i] < 0)
                    {
                        first = i;
                        break;
                    }
                }

                if (first < 0)
                {
                    evaluated++;
                    int c = haveBest ? costSoFar.CompareTo(best) : -1;
                    if (c < 0)
                    {
                        best = costSoFar;
                        haveBest = true;
                        optimal.Clear();
                    }

                    if (c <= 0) optimal.Add((int[])partner.Clone());
                    return;
                }

                for (int j = first + 1; j < n; j++)
                {
                    if (partner[j] >= 0) continue;
                    partner[first] = j;
                    partner[j] = first;
                    Visit(costSoFar + pairCost[first, j]);
                    partner[first] = -1;
                    partner[j] = -1;
                }
            }

            Visit(new PairingCost());

            int chosen = optimal.Count > 1 ? tieBreak.NextInt(optimal.Count) : 0;
            var pairs = new List<PlayerPair>(n / 2);
            int[] matching = optimal[chosen];
            for (int i = 0; i < n; i++)
            {
                if (matching[i] > i) pairs.Add(new PlayerPair(sorted[i].Player, sorted[matching[i]].Player));
            }

            return new PairingSolution(new ReadOnlyCollection<PlayerPair>(pairs), best, evaluated, optimal.Count, chosen);
        }

        /// <summary>Every complete pairing of the given players, in the solver's enumeration order.</summary>
        public static IReadOnlyList<PlayerPair[]> AllPairings(IReadOnlyList<int> players)
        {
            if (players == null) throw new ArgumentNullException(nameof(players));
            if (players.Count % 2 != 0) throw new ArgumentException("Pairing needs an even number of players.", nameof(players));
            var sorted = new List<int>(players);
            sorted.Sort();
            var result = new List<PlayerPair[]>();
            var used = new bool[sorted.Count];
            var current = new List<PlayerPair>();

            void Visit()
            {
                int first = Array.IndexOf(used, false);
                if (first < 0)
                {
                    result.Add(current.ToArray());
                    return;
                }

                used[first] = true;
                for (int j = first + 1; j < sorted.Count; j++)
                {
                    if (used[j]) continue;
                    used[j] = true;
                    current.Add(new PlayerPair(sorted[first], sorted[j]));
                    Visit();
                    current.RemoveAt(current.Count - 1);
                    used[j] = false;
                }

                used[first] = false;
            }

            Visit();
            return result;
        }

        private static Dictionary<int, PairingCandidate> Index(IReadOnlyList<PairingCandidate> candidates)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            var byPlayer = new Dictionary<int, PairingCandidate>();
            foreach (var c in candidates) byPlayer[c.Player] = c;
            return byPlayer;
        }

        private static bool Contains(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] == value) return true;
            return false;
        }
    }
}
