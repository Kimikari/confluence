using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Confluence.Arena.Generation;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Rules;

namespace Confluence.Arena.Tests
{
    /// <summary>
    /// Prevalidated fallback layouts (spec L323 "then use a prevalidated fallback layout for that size and biome.
    /// Never release an invalid arena"; L455 fallback seeds).
    /// </summary>
    public class FallbackTests
    {
        [Fact]
        public void EverySizeAndBiomeHasAFairFallback()
        {
            var cases = new List<KeyValuePair<int, BiomeDef>>();
            foreach (int d in ArenaFixture.Diameters)
            {
                foreach (BiomeDef biome in ArenaFixture.Catalogue.Biomes)
                {
                    cases.Add(new KeyValuePair<int, BiomeDef>(d, biome));
                }
            }

            var problems = new string[cases.Count];
            Parallel.For(0, cases.Count, i =>
            {
                int d = cases[i].Key;
                BiomeDef biome = cases[i].Value;
                ArenaLayout layout = ArenaFixture.Generator.Fallback(d, biome);
                var found = new List<string>();
                if (!layout.IsFallback || !layout.FallbackId.StartsWith("fallback:" + d + ":" + biome.Id + ":", System.StringComparison.Ordinal))
                {
                    found.Add("fallback id " + layout.FallbackId);
                }

                if (layout.Diameter != d || layout.BiomeId != biome.Id || layout.EnvironmentRule != biome.EnvironmentRule)
                {
                    found.Add("records " + layout.Diameter + " m " + layout.BiomeId);
                }

                found.AddRange(FairnessProbe.Check(layout, contraction: true).Problems);
                problems[i] = found.Count == 0 ? null : d + " m " + biome.Id + ": " + string.Join("; ", found.Take(3));
            });

            List<string> failures = problems.Where(p => p != null).ToList();
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }

        [Fact]
        public void FallbacksAreFixedPerSizeAndBiomeAndReleasedAsCopies()
        {
            BiomeDef biome = ArenaFixture.Catalogue.Biome("BIO-007");
            ArenaLayout a = ArenaFixture.Generator.Fallback(32, biome);
            ArenaLayout b = ArenaFixture.Generator.Fallback(32, biome);
            Assert.NotSame(a, b);
            Assert.Equal(ArenaLayoutHash.Compute(a), ArenaLayoutHash.Compute(b));
            Assert.Equal(GlobalRules.Arena.MaxRetrySeeds, a.Report.AttemptsUsed);

            // A caller changing its copy (for example the round number) never reaches the cache.
            a.Round = 9;
            a.Solids.RemoveAt(a.Solids.Count - 1);
            ArenaLayout c = ArenaFixture.Generator.Fallback(32, biome);
            Assert.Equal(ArenaLayoutHash.Compute(b), ArenaLayoutHash.Compute(c));

            // A fresh generator finds the same fallback: its seeds depend only on size, biome, and index.
            var other = new ArenaGenerator(ArenaFixture.Catalogue);
            Assert.Equal(ArenaLayoutHash.Compute(b), ArenaLayoutHash.Compute(other.Fallback(32, biome)));
            Assert.NotEqual(ArenaGenerator.FallbackSeed(32, "BIO-007", 0), ArenaGenerator.FallbackSeed(40, "BIO-007", 0));
        }
    }
}
