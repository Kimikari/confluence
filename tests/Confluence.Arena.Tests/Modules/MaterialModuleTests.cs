using System;
using System.Linq;
using Confluence.Arena.Modules;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Arena.Tests.Modules
{
    /// <summary>
    /// Material modules MOD-037..MOD-048. Durability text: "Ice 45 per tile; prism 75; grounding statue 80; metal plate 90;
    /// growth 50; ordinary floor anchored." Each keeps a dry bypass and finite, visible sources (spec L668).
    /// </summary>
    public class MaterialModuleTests
    {
        /// <summary>
        /// Widest band across the module (along X) that no material patch covers and no floor-standing solid blocks:
        /// the rough dry bypass beside the material (module fairness text).
        /// </summary>
        private static float DryBypass(ModuleGeometry g)
        {
            float best = 0f;
            float run = 0f;
            for (float x = -g.Width * 0.5f + 0.125f; x < g.Width * 0.5f; x += 0.25f)
            {
                bool wet = g.Patches.Any(p => (p.Flags != MaterialFlags.None || p.NaturalSource) && Math.Abs(x - p.Center.X) <= p.HalfSize.X);
                bool blocked = g.Pieces.Any(p => !p.Walkable && p.Box.Bottom < 0.1f && Math.Abs(x - p.Box.Center.X) <= p.Box.HalfExtents.X + 0.4f);
                run = wet || blocked ? 0f : run + 0.25f;
                best = Math.Max(best, run);
            }

            return best;
        }

        [Fact]
        [Verifies("MOD-037")]
        public void WaterChannelIsATwoMeterWetStripBesideAThreeMeterBypass()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-037");
            ModulePatch strip = ModuleAssert.Rule(g, "MOD-037:wet-strip").Single();
            Assert.Equal(2f, strip.HalfSize.X * 2f, 3);
            Assert.Equal(1, strip.Water);
            Assert.True(strip.NaturalSource);
            Assert.True(DryBypass(g) >= 3f);
        }

        [Fact]
        [Verifies("MOD-038")]
        public void IceCrossingIsATwoMeterIcePatchBetweenRoughStops()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-038");
            ModulePatch ice = ModuleAssert.Rule(g, "MOD-038:ice").Single();
            Assert.Equal(2f, ice.HalfSize.X * 2f, 3);
            Assert.True((ice.Flags & MaterialFlags.Ice) != 0 && (ice.Flags & MaterialFlags.Brittle) != 0);
            Assert.Equal(45f, ice.Durability);
            Assert.Equal(2, ModuleAssert.Rule(g, "MOD-038:rough-stop").Count);
            Assert.True(DryBypass(g) >= 2.5f);
        }

        [Fact]
        [Verifies("MOD-039")]
        public void CopperContactIsAThreeMeterPlateInsideInsulation()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-039");
            ModulePatch plate = ModuleAssert.Rule(g, "MOD-039:conductor-plate").Single();
            Assert.Equal(3f, plate.HalfSize.X * 2f, 3);
            Assert.Equal(3f, plate.HalfSize.Y * 2f, 3);
            Assert.True((plate.Flags & MaterialFlags.Conductive) != 0);
            Assert.Equal(90f, plate.Durability);
            Assert.Equal(4, ModuleAssert.Rule(g, "MOD-039:insulation").Count);
            Assert.All(ModuleAssert.Rule(g, "MOD-039:insulation"), p => Assert.Equal(MaterialFlags.Insulating, p.Flags));
        }

        [Fact]
        [Verifies("MOD-040")]
        public void FuelBedIsAFiniteGrowthBedBesideAnchoredCover()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-040");
            ModulePatch bed = ModuleAssert.Rule(g, "MOD-040:growth-bed").Single();
            Assert.Equal(1, bed.Growth);
            Assert.Equal(50f, bed.Durability);
            Assert.True((bed.Flags & MaterialFlags.Combustible) != 0);
            ModuleAssert.Anchored(ModuleAssert.Role(g, "anchored-cover").Single());
            Assert.True(DryBypass(g) >= 2.5f);
        }

        [Fact]
        [Verifies("MOD-041")]
        public void DustPocketIsATwoMeterDustBedBesideAWidePath()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-041");
            ModulePatch bed = ModuleAssert.Rule(g, "MOD-041:dust-bed").Single();
            Assert.Equal(1, bed.Dust);
            Assert.Equal(2f, bed.HalfSize.X * 2f, 3);
            Assert.True(bed.NaturalSource);
            Assert.True(DryBypass(g) >= 3f);
        }

        [Fact]
        [Verifies("MOD-042")]
        public void PrismCornerHoldsAMarkedPrismInARightAngleCorner()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-042");
            ModulePiece prism = ModuleAssert.Role(g, "prism").Single();
            ModuleAssert.Destructible(prism, 75f);
            Assert.True((prism.Flags & MaterialFlags.Reflective) != 0);
            Assert.Equal(2, ModuleAssert.Role(g, "corner-wall").Count);
            Assert.All(ModuleAssert.Role(g, "corner-wall"), ModuleAssert.Anchored);
        }

        [Fact]
        [Verifies("MOD-043")]
        public void GroundingNicheHasOneStatueBesideAShortWetBed()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-043");
            ModulePiece statue = ModuleAssert.Role(g, "grounding-statue").Single();
            ModuleAssert.Destructible(statue, 80f);
            Assert.True((statue.Flags & MaterialFlags.Grounded) != 0);
            ModulePatch bed = ModuleAssert.Rule(g, "MOD-043:wet-bed").Single();
            Assert.Equal(1, bed.Water);
            Assert.True(DryBypass(g) >= 2.5f);
        }

        [Fact]
        [Verifies("MOD-044")]
        public void AcousticArchHangsAPublicBellAboveTheWalkway()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-044");
            ModulePiece bell = ModuleAssert.Role(g, "bell").Single();
            Assert.True((bell.Flags & MaterialFlags.Acoustic) != 0);
            Assert.True(bell.Box.Bottom >= GlobalRules.Movement.StandingHeight); // above a standing player
            ModuleAssert.Anchored(bell);
        }

        [Fact]
        [Verifies("MOD-045")]
        public void HeatSinkLaneHasTwoCoolMetalFaces()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-045");
            var sinks = ModuleAssert.Role(g, "heat-sink");
            Assert.Equal(2, sinks.Count);
            Assert.All(sinks, s => ModuleAssert.Destructible(s, 90f));
            Assert.All(sinks, s => Assert.Equal(ArenaMaterials.HeatSink, s.Material));
            float lane = sinks.Max(s => s.Box.Center.Z - s.Box.HalfExtents.Z) - sinks.Min(s => s.Box.Center.Z + s.Box.HalfExtents.Z);
            Assert.True(lane >= 2.5f);
        }

        [Fact]
        [Verifies("MOD-046")]
        public void PorousStepIsAOneMeterAbsorbentPlatformWithATrough()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-046");
            ModulePiece deck = ModuleAssert.Role(g, "deck").Single();
            Assert.Equal(1f, deck.Box.Top, 3);
            Assert.True((deck.Flags & MaterialFlags.Porous) != 0);
            Assert.Single(ModuleAssert.Rule(g, "MOD-046:release-trough"));
            ModuleAssert.ReachableTop(g, new Vec2(-1.5f, 0.5f), 1f);
        }

        [Fact]
        [Verifies("MOD-047")]
        public void BrittleFringeLinesTheRouteWithThreeIceCovers()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-047");
            var ice = ModuleAssert.Role(g, "brittle-ice");
            Assert.Equal(3, ice.Count);
            Assert.All(ice, c => ModuleAssert.Destructible(c, 45f));
            Assert.All(ice, c => Assert.True((c.Flags & MaterialFlags.Brittle) != 0));
            Assert.All(ice, c => Assert.True(c.Box.Center.Z > 1f)); // along one edge of the route
        }

        [Fact]
        [Verifies("MOD-048")]
        public void DampSandShelfHasTwoSandTilesWithOneWaterUnitEach()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-048");
            var tiles = ModuleAssert.Rule(g, "MOD-048:damp-sand");
            Assert.Equal(2, tiles.Count);
            Assert.All(tiles, t => Assert.True(t.Sand == 1 && t.Water == 1));
            Assert.All(tiles, t => Assert.True(t.Center.Y + t.HalfSize.Y <= g.Depth * 0.5f));
            float bypass = tiles.Min(t => t.Center.Y - t.HalfSize.Y) + g.Depth * 0.5f;
            Assert.True(bypass >= 2.5f - 1e-3f);
        }
    }
}
