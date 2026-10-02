using System.Collections.Generic;
using System.Linq;
using Confluence.Arena.Generation;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Random;
using Confluence.Core.Rules;

namespace Confluence.Arena.Tests
{
    public class GeneratorTests
    {
        private static ArenaRequest Request(ulong seed, int round, int diameter = 0, string biome = null)
        {
            return new ArenaRequest { MatchSeed = seed, Round = round, Diameter = diameter, BiomeId = biome };
        }

        [Fact]
        public void EqualRequestsGiveByteIdenticalLayouts()
        {
            // Two independent generators: nothing is shared but the catalogue file.
            var other = new ArenaGenerator(Catalogue.Load(Catalogue.FindDefaultPath()));
            foreach (int d in ArenaFixture.Diameters)
            {
                ArenaRequest request = Request(0xC0FFEEUL + (ulong)d, 3, d);
                GeneratedArena a = ArenaFixture.Generator.Generate(request);
                GeneratedArena b = other.Generate(request.Clone());
                Assert.Equal(a.LayoutHash, b.LayoutHash);
                Assert.Equal(a.Attempts, b.Attempts);
                Assert.Equal(ArenaLayoutHash.Compute(a.Layout), ArenaLayoutHash.Compute(a.Layout.Clone()));
                Assert.Equal(a.Cosmetics.Select(c => c.ArtVariant), b.Cosmetics.Select(c => c.ArtVariant));
            }
        }

        [Fact]
        public void SeedsChangeBetweenRoundsAndMatches()
        {
            var hashes = new HashSet<ulong>();
            for (int round = 1; round <= 4; round++)
            {
                hashes.Add(ArenaFixture.Generator.Generate(Request(77UL, round, 32, "BIO-001")).LayoutHash);
                hashes.Add(ArenaFixture.Generator.Generate(Request(78UL, round, 32, "BIO-001")).LayoutHash);
            }

            Assert.Equal(8, hashes.Count);
        }

        [Fact]
        public void LayoutRecordsSeedRoundSizeBiomeAndRule()
        {
            GeneratedArena arena = ArenaFixture.Generator.Generate(Request(5UL, 2, 40, "BIO-006"));
            ArenaLayout layout = arena.Layout;
            Assert.Equal(2, layout.Round);
            Assert.Equal(40, layout.Diameter);
            Assert.Equal("BIO-006", layout.BiomeId);
            Assert.Equal(ArenaFixture.Catalogue.Biome("BIO-006").EnvironmentRule, layout.EnvironmentRule);
            Assert.True(layout.Report.Valid);
            if (!layout.IsFallback)
            {
                Assert.Equal(ArenaSchedule.AttemptSeed(5UL, 2, arena.Attempts - 1), layout.Seed);
            }

            Assert.NotEmpty(layout.Modules);
            Assert.NotEmpty(layout.LightShafts);
            Assert.Equal(2, layout.Spawns.Length);
        }

        [Fact]
        public void UnforcedDiameterFollowsTheBalancedFourRoundSchedule()
        {
            for (ulong match = 1; match <= 40; match++)
            {
                var diameters = new List<int>();
                for (int round = 1; round <= 16; round++)
                {
                    diameters.Add(ArenaSchedule.DiameterForRound(match, round));
                }

                Assert.True(Schedules.IsBalancedDiameterSequence(diameters, out string reason), reason);
                Assert.Equal(diameters[0], ArenaSchedule.DiameterForRound(match, 1)); // stable per round
            }

            GeneratedArena arena = ArenaFixture.Generator.Generate(Request(9UL, 5));
            Assert.Equal(ArenaSchedule.DiameterForRound(9UL, 5), arena.Layout.Diameter);
        }

        [Fact]
        public void BiomeChoiceRespectsTheAvailabilityFilter()
        {
            var allowed = new[] { "BIO-003", "BIO-019" };
            for (ulong match = 1; match <= 12; match++)
            {
                var request = new ArenaRequest { MatchSeed = match, Round = 1, Diameter = 24, Biomes = ContentAvailability.FromIds(allowed) };
                Assert.Contains(ArenaFixture.Generator.Generate(request).Layout.BiomeId, allowed);
            }

            Assert.Throws<System.InvalidOperationException>(() =>
                ArenaFixture.Generator.Generate(new ArenaRequest { MatchSeed = 1, Round = 1, Diameter = 24, Biomes = ContentAvailability.None }));
        }

        [Fact]
        public void PropsComeOnlyFromTheAvailableDefinitions()
        {
            var allowed = new[] { "PROP-001", "PROP-007", "PROP-013", "PROP-019", "PROP-025" };
            var request = new ArenaRequest { MatchSeed = 31, Round = 1, Diameter = 40, BiomeId = "BIO-012", Props = ContentAvailability.FromIds(allowed) };
            ArenaLayout layout = ArenaFixture.Generator.Generate(request).Layout;
            Assert.True(layout.Report.Valid);
            Assert.All(layout.Props, p => Assert.Contains(p.PropId, allowed));
            Assert.True(layout.Props.Select(p => p.PropId).Distinct().Count() >= GlobalRules.Arena.PropDefinitionsMin);

            var two = new ArenaRequest { MatchSeed = 31, Round = 1, Diameter = 32, BiomeId = "BIO-012", Props = ContentAvailability.FromIds(new[] { "PROP-001", "PROP-007" }) };
            Assert.Equal(2, ArenaFixture.Generator.MinPropDefinitions(two.Props));
            ArenaLayout few = ArenaFixture.Generator.Generate(two).Layout;
            Assert.True(few.Report.Valid);
            Assert.All(few.Props, p => Assert.Contains(p.PropId, new[] { "PROP-001", "PROP-007" }));
        }

        [Fact]
        public void EveryAttemptUsesTheArenaStreamAndAtMostEightSeeds()
        {
            for (int k = 0; k < GlobalRules.Arena.MaxRetrySeeds; k++)
            {
                Assert.Equal(RngStreams.SeedFor(12UL, RngStream.Arena, 4, ArenaSchedule.AttemptSubBase + k), ArenaSchedule.AttemptSeed(12UL, 4, k));
            }

            var arenas = new GeneratedArena[10];
            System.Threading.Tasks.Parallel.For(0, arenas.Length, i =>
            {
                arenas[i] = ArenaFixture.Generator.Generate(Request(100UL + (ulong)i, 1, 48, "BIO-020"));
            });

            foreach (GeneratedArena arena in arenas)
            {
                Assert.InRange(arena.Attempts, 1, GlobalRules.Arena.MaxRetrySeeds);
                Assert.Equal(arena.UsedFallback ? GlobalRules.Arena.MaxRetrySeeds : arena.Attempts - 1, arena.RejectedAttempts.Count);
                Assert.True(arena.Layout.Report.Valid);
            }
        }

        [Fact]
        public void CosmeticDressingNeverChangesTheGameplayLayout()
        {
            ArenaLayout layout = ArenaFixture.Generator.Generate(Request(41UL, 1, 32, "BIO-009")).Layout;
            ulong before = ArenaLayoutHash.Compute(layout);
            List<CosmeticVariant> dressing = ArenaGenerator.Dress(layout);
            Assert.Equal(layout.Modules.Count, dressing.Count);
            Assert.Equal(before, ArenaLayoutHash.Compute(layout));

            // Mirrored modules draw their art independently: visual symmetry is optional (spec L308).
            bool anyDifferent = false;
            foreach (ModuleInstance m in layout.Modules)
            {
                int i = layout.Modules.IndexOf(m);
                if (m.MirrorIndex >= 0 && m.MirrorIndex != i && dressing[i].ArtVariant != dressing[m.MirrorIndex].ArtVariant)
                {
                    anyDifferent = true;
                }
            }

            Assert.True(anyDifferent);
        }

        [Fact]
        public void LayoutHashTracksEveryGameplayField()
        {
            ArenaLayout layout = ArenaFixture.Generator.Generate(Request(3UL, 1, 24, "BIO-001")).Layout;
            ulong hash = ArenaLayoutHash.Compute(layout);

            ArenaLayout moved = layout.Clone();
            Solid s = moved.Solids[moved.Solids.Count - 1];
            s.Box = s.Box.Translated(new Vec3(0.001f, 0f, 0f));
            Assert.NotEqual(hash, ArenaLayoutHash.Compute(moved));

            ArenaLayout wetter = layout.Clone();
            wetter.Materials[0].Water += 1;
            Assert.NotEqual(hash, ArenaLayoutHash.Compute(wetter));

            ArenaLayout rerouted = layout.Clone();
            rerouted.Boundary.InitialRadii[3] += 0.01f;
            Assert.NotEqual(hash, ArenaLayoutHash.Compute(rerouted));

            ArenaLayout reported = layout.Clone();
            reported.Report.SetMetric("anything", 1f);
            Assert.Equal(hash, ArenaLayoutHash.Compute(reported)); // the validation report is not gameplay content
        }
    }
}
