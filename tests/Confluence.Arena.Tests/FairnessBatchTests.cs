using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Confluence.Arena.Generation;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Xunit.Abstractions;

namespace Confluence.Arena.Tests
{
    /// <summary>
    /// Procedural arena fairness (spec L302-L335, L674-L679): large seed batches through the full pipeline. Every
    /// released arena is valid or a prevalidated fallback, and passes the independent <see cref="FairnessProbe"/>.
    /// </summary>
    public class FairnessBatchTests
    {
        private readonly ITestOutputHelper _output;

        public FairnessBatchTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private sealed class Case
        {
            public int Diameter;
            public string BiomeId;
            public ulong MatchSeed;
            public int Round;
        }

        private sealed class Outcome
        {
            public Case Case;
            public bool Fallback;
            public int Attempts;
            public FairnessProbe.Result Probe;
            public string Error;

            /// <summary>Why a fallback release does not match the prevalidated fallback for its size and biome (null when it does).</summary>
            public string FallbackMismatch;

            public List<string> ModuleIds;
        }

        private List<Outcome> Run(List<Case> cases, bool contraction)
        {
            var outcomes = new Outcome[cases.Count];
            Parallel.For(0, cases.Count, i =>
            {
                Case c = cases[i];
                var outcome = new Outcome { Case = c };
                try
                {
                    GeneratedArena arena = ArenaFixture.Generator.Generate(new ArenaRequest
                    {
                        MatchSeed = c.MatchSeed,
                        Round = c.Round,
                        Diameter = c.Diameter,
                        BiomeId = c.BiomeId,
                    });
                    outcome.Fallback = arena.UsedFallback;
                    outcome.Attempts = arena.Attempts;
                    outcome.Probe = FairnessProbe.Check(arena.Layout, contraction);
                    outcome.ModuleIds = arena.Layout.Modules.Select(m => m.ModuleId).Distinct().ToList();
                    if (arena.UsedFallback)
                    {
                        outcome.FallbackMismatch = CheckFallbackRelease(arena, c);
                    }
                }
                catch (Exception e)
                {
                    outcome.Error = e.GetType().Name + ": " + e.Message;
                }

                outcomes[i] = outcome;
            });

            return outcomes.ToList();
        }

        private void Report(string title, List<Outcome> outcomes, Stopwatch watch)
        {
            var text = new StringBuilder();
            text.AppendLine(title + " (" + outcomes.Count + " arenas, " + watch.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s)");
            foreach (IGrouping<int, Outcome> size in outcomes.GroupBy(o => o.Case.Diameter).OrderBy(g => g.Key))
            {
                int n = size.Count();
                int fallbacks = size.Count(o => o.Fallback);
                double attempts = size.Average(o => (double)o.Attempts);
                float worst = size.Where(o => o.Probe != null).Select(o => o.Probe.MaxMirrorDistanceError).DefaultIfEmpty(0f).Max();
                text.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0} m: fallback {1}/{2} ({3:0.0}%), mean seeds {4:0.00}, worst mirrored-distance error {5:0.000} m",
                    size.Key, fallbacks, n, 100.0 * fallbacks / n, attempts, worst));
            }

            _output.WriteLine(text.ToString());
        }

        private static void AssertAllFair(List<Outcome> outcomes)
        {
            var failures = new List<string>();
            foreach (Outcome o in outcomes)
            {
                string label = o.Case.Diameter + " m " + o.Case.BiomeId + " seed " + o.Case.MatchSeed + " round " + o.Case.Round;
                if (o.Error != null)
                {
                    failures.Add(label + ": " + o.Error);
                }
                else if (o.Probe.Problems.Count > 0)
                {
                    failures.Add(label + (o.Fallback ? " (fallback)" : string.Empty) + ": " + string.Join("; ", o.Probe.Problems.Take(4)));
                }
                else if (o.FallbackMismatch != null)
                {
                    failures.Add(label + ": " + o.FallbackMismatch);
                }
            }

            Assert.True(failures.Count == 0, failures.Count + " unfair arena(s):\n" + string.Join("\n", failures.Take(25)));
        }

        /// <summary>
        /// A fallback release used all eight seeds (spec L323), keeps every rejected report, and is exactly the
        /// prevalidated fallback of its size and biome (stamped with the request's round).
        /// </summary>
        private static string CheckFallbackRelease(GeneratedArena arena, Case c)
        {
            if (arena.Attempts != GlobalRules.Arena.MaxRetrySeeds || arena.RejectedAttempts.Count != GlobalRules.Arena.MaxRetrySeeds)
            {
                return "fallback after " + arena.Attempts + " attempts and " + arena.RejectedAttempts.Count + " rejections";
            }

            ArenaLayout expected = ArenaFixture.Generator.Fallback(c.Diameter, ArenaFixture.Catalogue.Biome(arena.Layout.BiomeId));
            expected.Round = c.Round;
            return ArenaLayoutHash.Compute(expected) == arena.LayoutHash ? null : "fallback differs from the prevalidated layout";
        }

        private static List<Case> BiomeBySizeCases(int seedsPerCell, ulong seedBase)
        {
            var cases = new List<Case>();
            foreach (BiomeDef biome in ArenaFixture.Catalogue.Biomes)
            {
                foreach (int d in ArenaFixture.Diameters)
                {
                    for (int s = 0; s < seedsPerCell; s++)
                    {
                        cases.Add(new Case { Diameter = d, BiomeId = biome.Id, MatchSeed = seedBase + (ulong)(s * 7919), Round = 1 + s % 12 });
                    }
                }
            }

            return cases;
        }

        /// <summary>24 biomes x 4 sizes x 6 match seeds through Generate, with the contraction re-check on all of them.</summary>
        [Fact]
        public void EveryBiomeAndSizeReleasesFairArenas()
        {
            List<Case> cases = BiomeBySizeCases(6, 0xA11CEUL);
            Assert.Equal(24 * 4 * 6, cases.Count);
            var watch = Stopwatch.StartNew();
            List<Outcome> outcomes = Run(cases, contraction: true);
            Report("Biome x size batch", outcomes, watch);
            AssertAllFair(outcomes);
        }

        /// <summary>24 biomes x 4 sizes x 10 further match seeds (960 arenas). Run with --filter Category=Slow.</summary>
        [Fact]
        [Trait("Category", "Slow")]
        public void TenSeedsPerBiomeAndSizeReleaseFairArenas()
        {
            List<Case> cases = BiomeBySizeCases(10, 0xB16BA7CUL);
            var watch = Stopwatch.StartNew();
            List<Outcome> outcomes = Run(cases, contraction: true);
            Report("Biome x size x 10 batch", outcomes, watch);
            AssertAllFair(outcomes);
        }

        /// <summary>500 match seeds per size with scheduled biome choice (spec L455 batch). Run with --filter Category=Slow.</summary>
        [Fact]
        [Trait("Category", "Slow")]
        public void FiveHundredSeedsPerSizeReleaseFairArenas()
        {
            var cases = new List<Case>();
            foreach (int d in ArenaFixture.Diameters)
            {
                for (int s = 0; s < 500; s++)
                {
                    cases.Add(new Case { Diameter = d, MatchSeed = 0x5EED0000UL + (ulong)s, Round = 1 + s % 15 });
                }
            }

            // Scheduled biome (BiomeId null) is resolved inside Generate.
            var watch = Stopwatch.StartNew();
            List<Outcome> outcomes = Run(cases, contraction: false);
            Report("500-seed batch", outcomes, watch);
            AssertAllFair(outcomes);

            // Every catalogue module is placed by the generator somewhere in the batch (the [ContentBehavior] claim).
            var placed = new HashSet<string>(outcomes.Where(o => o.ModuleIds != null).SelectMany(o => o.ModuleIds), StringComparer.Ordinal);
            List<string> missing = ArenaFixture.Catalogue.Modules.Select(m => m.Id).Where(id => !placed.Contains(id)).ToList();
            Assert.True(missing.Count == 0, "never placed: " + string.Join(", ", missing));
        }
    }
}
