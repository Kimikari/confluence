using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Confluence.Arena.Generation;
using Confluence.Core.Content;
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
            }

            Assert.True(failures.Count == 0, failures.Count + " unfair arena(s):\n" + string.Join("\n", failures.Take(25)));
        }

        /// <summary>24 biomes x 4 sizes x 10 match seeds through Generate, with the contraction re-check on all of them.</summary>
        [Fact]
        public void EveryBiomeAndSizeReleasesFairArenas()
        {
            var cases = new List<Case>();
            foreach (BiomeDef biome in ArenaFixture.Catalogue.Biomes)
            {
                foreach (int d in ArenaFixture.Diameters)
                {
                    for (int s = 0; s < 10; s++)
                    {
                        cases.Add(new Case { Diameter = d, BiomeId = biome.Id, MatchSeed = 0xA11CEUL + (ulong)(s * 7919), Round = 1 + s % 12 });
                    }
                }
            }

            Assert.Equal(24 * 4 * 10, cases.Count);
            var watch = Stopwatch.StartNew();
            List<Outcome> outcomes = Run(cases, contraction: true);
            Report("Biome x size batch", outcomes, watch);
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

            // Scheduled biome (BiomeId null) is resolved inside Generate; record it for the report.
            var watch = Stopwatch.StartNew();
            List<Outcome> outcomes = Run(cases, contraction: false);
            Report("500-seed batch", outcomes, watch);
            AssertAllFair(outcomes);
        }
    }
}
