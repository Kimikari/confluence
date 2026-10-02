using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Arena.Biomes;
using Confluence.Arena.Generation;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Arena.Tests
{
    /// <summary>
    /// Biome dressing (spec L335 "one announced bounded environment rule", L668 natural sources): each biome's rule
    /// sources and objects are placed in mirrored pairs and flagged for Reactions and the Sim. The rules themselves
    /// are not executable here, so these tests check geometry only and carry no [Verifies] attribute.
    /// </summary>
    public class BiomeDressingTests
    {
        private static List<MaterialPatch> RulePatches(ArenaLayout layout, string biomeId)
        {
            return layout.Materials.Where(m => m.RefillRule != null && m.RefillRule.StartsWith(biomeId + ":", StringComparison.Ordinal)).ToList();
        }

        private static List<Solid> RuleSolids(ArenaLayout layout, string material)
        {
            return layout.Solids.Where(s => s.Material == material && s.ModuleIndex < 0).ToList();
        }

        private static void AssertPatchPairs(ArenaLayout layout, List<MaterialPatch> patches)
        {
            foreach (MaterialPatch p in patches)
            {
                MaterialPatch image = layout.Materials[p.MirrorId];
                Assert.NotSame(p, image);
                Assert.True(Vec2.Distance(MirrorMath.Point(p.Center), image.Center) < 1e-3f);
                Assert.Equal(p.RefillRule, image.RefillRule);
            }
        }

        private static void AssertSolidPairs(ArenaLayout layout, List<Solid> solids)
        {
            foreach (Solid s in solids)
            {
                Solid image = layout.FindSolid(s.MirrorId);
                Assert.NotNull(image);
                Assert.NotSame(s, image);
                Assert.Equal(s.Durability, image.Durability);
                Assert.True(MirrorMath.IsPointMirror(s.Box, image.Box, ArenaRules.MirrorTolerance));
            }
        }

        [Fact]
        public void EveryCatalogueBiomeHasFeatureGeometry()
        {
            Assert.Equal(24, BiomeFeatures.Count);
            foreach (BiomeDef biome in ArenaFixture.Catalogue.Biomes)
            {
                Assert.True(BiomeFeatures.TryGet(biome.Id, out BiomeFeature f), biome.Id);
                Assert.False(string.IsNullOrEmpty(f.Geometry));
            }
        }

        [Fact]
        public void EveryBiomeSampleCarriesItsRuleAndMirroredSources()
        {
            foreach (GeneratedArena arena in ArenaFixture.BiomeSamples)
            {
                ArenaLayout layout = arena.Layout;
                BiomeDef biome = ArenaFixture.Catalogue.Biome(layout.BiomeId);
                Assert.True(layout.Report.Valid, layout.BiomeId);
                Assert.Equal(biome.EnvironmentRule, layout.EnvironmentRule);
                Assert.True(BiomeFeatures.TryGet(biome.Id, out BiomeFeature f));
                switch (f.Kind)
                {
                    case BiomeFeatureKind.Patch:
                    case BiomeFeatureKind.LightShaft:
                        List<MaterialPatch> patches = RulePatches(layout, biome.Id);
                        Assert.Equal(2 * f.Pairs, patches.Count);
                        AssertPatchPairs(layout, patches);
                        Assert.All(patches, p => Assert.Equal(f.NaturalSource, p.NaturalSource));
                        break;
                    case BiomeFeatureKind.Solid:
                        List<Solid> solids = RuleSolids(layout, f.Material);
                        Assert.Equal(2 * f.Pairs, solids.Count);
                        AssertSolidPairs(layout, solids);
                        Assert.All(solids, s => Assert.Equal(f.Durability, s.Durability));
                        break;
                    default:
                        Assert.NotEmpty(layout.Gusts);
                        break;
                }

                Assert.InRange(layout.NaturalSourceCount(), 0, GlobalRules.Arena.MaxNaturalSourcePatches);
            }
        }

        [Fact]
        public void RaincourtGuttersAreTwoTwoMeterWaterBedsWithTheRainPulse()
        {
            // BIO-001: "adds one water unit to the two mirrored 2 m gutter beds, up to four".
            ArenaLayout layout = ArenaFixture.SampleFor("BIO-001").Layout;
            List<MaterialPatch> beds = RulePatches(layout, "BIO-001");
            Assert.Equal(2, beds.Count);
            Assert.All(beds, b =>
            {
                Assert.Equal(new Vec2(1f, 1f), b.HalfSize);
                Assert.Equal(1, b.Water);
                Assert.True(b.NaturalSource);
                Assert.True((b.Flags & MaterialFlags.Water) != 0);
                Assert.Contains("every=8s", b.RefillRule);
                Assert.Contains("max=4", b.RefillRule);
            });
        }

        [Fact]
        public void EmberOrchardRootBedsStartWithHeatAndGrowth()
        {
            List<MaterialPatch> beds = RulePatches(ArenaFixture.SampleFor("BIO-002").Layout, "BIO-002");
            Assert.Equal(2, beds.Count);
            Assert.All(beds, b =>
            {
                Assert.Equal(1, b.Heat);
                Assert.Equal(1, b.Growth);
                Assert.True((b.Flags & MaterialFlags.Combustible) != 0);
            });
        }

        [Fact]
        public void GlassObservatoryHasTwoAnchoredPrisms()
        {
            List<Solid> prisms = RuleSolids(ArenaFixture.SampleFor("BIO-003").Layout, ArenaMaterials.Prism);
            Assert.Equal(2, prisms.Count);
            Assert.All(prisms, p => Assert.False(p.IsDestructible));
        }

        [Fact]
        public void RimeFoundryTilesBeginFrozenWithFortyFiveDurability()
        {
            List<MaterialPatch> tiles = RulePatches(ArenaFixture.SampleFor("BIO-004").Layout, "BIO-004");
            Assert.Equal(2, tiles.Count);
            Assert.All(tiles, t =>
            {
                Assert.True((t.Flags & MaterialFlags.Ice) != 0);
                Assert.Contains("durability=45", t.RefillRule);
            });
        }

        [Fact]
        public void ThunderCausewayHasFourThreeMeterConductiveRails()
        {
            List<MaterialPatch> rails = RulePatches(ArenaFixture.SampleFor("BIO-006").Layout, "BIO-006");
            Assert.Equal(4, rails.Count);
            Assert.All(rails, r =>
            {
                Assert.Equal(3f, 2f * Math.Max(r.HalfSize.X, r.HalfSize.Y), 3);
                Assert.True((r.Flags & MaterialFlags.Conductive) != 0);
            });
        }

        [Fact]
        public void MoonCloisterMarksTwoLightShafts()
        {
            ArenaLayout layout = ArenaFixture.SampleFor("BIO-008").Layout;
            List<MaterialPatch> shafts = RulePatches(layout, "BIO-008");
            Assert.Equal(2, shafts.Count);
            foreach (MaterialPatch s in shafts)
            {
                Assert.Contains(layout.LightShafts, l => Vec2.Distance(l.Position.XZ(), s.Center) < 1e-3f);
            }
        }

        [Fact]
        public void WindmillYardSchedulesTwoSecondGustsEveryTwelveSeconds()
        {
            ArenaLayout layout = ArenaFixture.SampleFor("BIO-011").Layout;
            Assert.NotEmpty(layout.Gusts);
            for (int i = 0; i < layout.Gusts.Count; i++)
            {
                GustEvent g = layout.Gusts[i];
                Assert.Equal(12f * (i + 1), g.TimeSec);
                Assert.Equal(2f, g.DurationSec);
                Assert.Equal(1, g.Strength);

                // Across the spawn axis (spawns sit on -Z and +Z), so the push favors neither spawn.
                Assert.Equal(0f, g.Direction.Y);
                Assert.Equal(1f, Math.Abs(g.Direction.X));
            }
        }

        [Fact]
        public void WellspringBasinsHoldTwoWaterUnitsOnTop()
        {
            ArenaLayout layout = ArenaFixture.SampleFor("BIO-024").Layout;
            List<Solid> basins = RuleSolids(layout, ArenaMaterials.Basin);
            List<MaterialPatch> water = RulePatches(layout, "BIO-024");
            Assert.Equal(2, basins.Count);
            Assert.Equal(2, water.Count);
            foreach (MaterialPatch w in water)
            {
                Assert.Equal(2, w.Water);
                Assert.Contains(basins, b => b.Box.ContainsXZ(w.Center) && Math.Abs(b.Box.Top - w.SurfaceY) < 1e-3f);
            }
        }
    }
}
