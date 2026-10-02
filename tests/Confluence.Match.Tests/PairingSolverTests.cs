using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Random;

namespace Confluence.Match.Tests
{
    public class PairingSolverTests
    {
        private static PairingCandidate C(int id, int resolve, int wins, params int[] recent) => new PairingCandidate(id, resolve, wins, recent);

        [Theory]
        [InlineData(2, 1)]
        [InlineData(4, 3)]
        [InlineData(6, 15)]
        [InlineData(8, 105)]
        public void Enumerates_Every_Complete_Pairing(int players, int expected)
        {
            // (n-1)!! perfect pairings; the solver evaluates all of them (spec L59 "complete small pairing set").
            var all = PairingSolver.AllPairings(Enumerable.Range(0, players).ToList());
            Assert.Equal(expected, all.Count);
            Assert.Equal(expected, all.Select(Key).Distinct().Count());

            var candidates = Enumerable.Range(0, players).Select(i => C(i, 8, 0)).ToList();
            Assert.Equal(expected, PairingSolver.Solve(candidates, new Rng(1)).EvaluatedCount);
        }

        [Fact]
        public void Avoids_Immediate_Rematch_Even_When_Resolve_Prefers_It()
        {
            // Last round was 0v1 and 2v3. Resolve alone would repeat both fights; a greedy pass would too.
            var candidates = new[] { C(0, 8, 1, 1), C(1, 8, 1, 0), C(2, 6, 0, 3), C(3, 6, 0, 2) };
            var solution = PairingSolver.Solve(candidates, new Rng(7));
            Assert.Equal(0, solution.Cost.ImmediateRematches);
            Assert.DoesNotContain(new PlayerPair(0, 1), solution.Pairs);
            Assert.DoesNotContain(new PlayerPair(2, 3), solution.Pairs);
            Assert.Equal(4, solution.Cost.ResolveDifference);
        }

        [Fact]
        public void Rematch_Outranks_Recent_Repeat()
        {
            // Histories, most recent first (5 is an eliminated player). Avoiding every immediate rematch forces two repeats:
            // {0v2, 1v3} costs (0 rematches, 2 repeats); both other pairings contain a rematch.
            var candidates = new[] { C(0, 8, 0, 1, 2), C(1, 8, 0, 0, 3), C(2, 8, 0, 5, 0), C(3, 8, 0, 0, 1) };
            var solution = PairingSolver.Solve(candidates, new Rng(3));
            Assert.Equal(new PairingCost(0, 2, 0, 0), solution.Cost);
            Assert.Equal(new[] { new PlayerPair(0, 2), new PlayerPair(1, 3) }, solution.Pairs);
        }

        [Fact]
        public void Recent_Repeat_Outranks_Resolve_Difference()
        {
            // 0 and 1 met two fights ago and share 8 Resolve; the repeat-free pairings cost 13 Resolve difference.
            var candidates = new[] { C(0, 8, 0, 5, 1), C(1, 8, 0, 6, 0), C(2, 1, 0), C(3, 2, 0) };
            var solution = PairingSolver.Solve(candidates, new Rng(9));
            Assert.Equal(new PairingCost(0, 0, 13, 0), solution.Cost);
            Assert.Equal(2, solution.OptimalCount);
            Assert.DoesNotContain(new PlayerPair(0, 1), solution.Pairs);
        }

        [Fact]
        public void Resolve_Difference_Then_Combat_Wins_Order_Remaining_Pairs()
        {
            var candidates = new[] { C(0, 8, 4), C(1, 8, 1), C(2, 8, 4), C(3, 8, 1) };
            var solution = PairingSolver.Solve(candidates, new Rng(5));
            Assert.Equal(new[] { new PlayerPair(0, 2), new PlayerPair(1, 3) }, solution.Pairs);
            Assert.Equal(1, solution.OptimalCount);

            var byResolve = PairingSolver.Solve(new[] { C(0, 8, 0), C(1, 3, 3), C(2, 7, 0), C(3, 3, 0) }, new Rng(5));
            Assert.Equal(new[] { new PlayerPair(0, 2), new PlayerPair(1, 3) }, byResolve.Pairs);
            Assert.Equal(new PairingCost(0, 0, 1, 3), byResolve.Cost);
        }

        [Fact]
        public void Exact_Ties_Use_The_Round_Seed_Deterministically()
        {
            var candidates = Enumerable.Range(0, 8).Select(i => C(i, 8, 0)).ToList();
            var first = PairingSolver.Solve(candidates, new Rng(42));
            var again = PairingSolver.Solve(candidates, new Rng(42));
            Assert.Equal(105, first.OptimalCount);
            Assert.Equal(first.Pairs, again.Pairs);

            var distinct = new HashSet<string>();
            for (ulong seed = 0; seed < 40; seed++) distinct.Add(Key(PairingSolver.Solve(candidates, new Rng(seed)).Pairs.ToArray()));
            Assert.True(distinct.Count > 10, "different round seeds should spread over the tied pairings");
        }

        [Fact]
        public void Complete_Enumeration_Matches_Brute_Force_Over_Permutations()
        {
            var rng = new Rng(2026);
            for (int trial = 0; trial < 30; trial++)
            {
                int n = trial % 3 == 0 ? 6 : 8;
                var candidates = RandomCandidates(rng, n);
                var solution = PairingSolver.Solve(candidates, new Rng((ulong)trial));

                // Brute force: every permutation, read as consecutive pairs, gives every pairing (many times over).
                var costs = new Dictionary<string, PairingCost>();
                foreach (var perm in Permutations(Enumerable.Range(0, n).ToArray()))
                {
                    var pairs = new List<PlayerPair>();
                    for (int i = 0; i < n; i += 2) pairs.Add(new PlayerPair(perm[i], perm[i + 1]));
                    pairs.Sort((x, y) => x.A.CompareTo(y.A));
                    string key = Key(pairs.ToArray());
                    if (!costs.ContainsKey(key)) costs[key] = PairingSolver.Cost(pairs, candidates);
                }

                var min = costs.Values.Min();
                Assert.Equal(min, solution.Cost);
                Assert.Equal(costs.Values.Count(c => c.Equals(min)), solution.OptimalCount);
                Assert.Equal(min, PairingSolver.Cost(solution.Pairs, candidates));
                Assert.True(costs.Count == solution.EvaluatedCount, "brute force and enumeration see the same pairing set");
            }
        }

        [Fact]
        public void Rejects_Odd_Counts_And_Duplicates()
        {
            Assert.Throws<ArgumentException>(() => PairingSolver.Solve(new[] { C(0, 8, 0), C(1, 8, 0), C(2, 8, 0) }, new Rng(1)));
            Assert.Throws<ArgumentException>(() => PairingSolver.Solve(new[] { C(0, 8, 0), C(0, 8, 0) }, new Rng(1)));
        }

        internal static List<PairingCandidate> RandomCandidates(Rng rng, int n)
        {
            var list = new List<PairingCandidate>();
            for (int i = 0; i < n; i++)
            {
                var recent = new List<int>();
                int count = rng.NextInt(3);
                while (recent.Count < count)
                {
                    int o = rng.NextInt(n);
                    if (o != i && !recent.Contains(o)) recent.Add(o);
                }

                list.Add(new PairingCandidate(i, 1 + rng.NextInt(8), rng.NextInt(6), recent));
            }

            return list;
        }

        internal static string Key(PlayerPair[] pairs) => string.Join(",", pairs.OrderBy(p => p.A).Select(p => p.ToString()));

        private static IEnumerable<int[]> Permutations(int[] items)
        {
            var a = (int[])items.Clone();
            var c = new int[a.Length];
            yield return (int[])a.Clone();
            int i = 0;
            while (i < a.Length)
            {
                if (c[i] < i)
                {
                    int j = i % 2 == 0 ? 0 : c[i];
                    (a[j], a[i]) = (a[i], a[j]);
                    yield return (int[])a.Clone();
                    c[i]++;
                    i = 0;
                }
                else
                {
                    c[i] = 0;
                    i++;
                }
            }
        }
    }
}
