using System.Linq;
using Confluence.Arena.Modules;
using Confluence.Arena.Navigation;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Arena.Tests.Modules
{
    /// <summary>
    /// Elevation modules MOD-025..MOD-036: anchored floors, stairs, and ramps; edge cover 80; ramps at most 30 degrees,
    /// standardized stairs, two access routes and a safe descent (spec L312, L175).
    /// </summary>
    public class ElevationModuleTests
    {
        private static void StairsAreStandard(ModuleGeometry g)
        {
            foreach (ModulePiece stair in g.Pieces.Where(p => p.Kind == SolidKind.Stair))
            {
                var shape = new RampShape(stair.Box, stair.RampRise);
                Assert.True(shape.Length + 1e-3f >= ArenaRules.StairRunFor(stair.RampRise));
                Assert.True(shape.SlopeDeg <= GlobalRules.Arena.RampMaxSlopeDeg + 1e-3f);
                ModuleAssert.Anchored(stair);
            }
        }

        [Fact]
        [Verifies("MOD-025")]
        public void ShortRampReachesAOneMeterDeckWithTwoReturnRoutes()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-025");
            ModulePiece ramp = ModuleAssert.Role(g, "ramp").Single();
            Assert.True(new RampShape(ramp.Box, ramp.RampRise).SlopeDeg < 20f); // gentle
            Assert.Equal(1f, g.MaxWalkableHeight(), 3);
            ModuleAssert.ReachableTop(g, new Vec2(1f, 0f), 1f);
            Assert.Equal(2, g.Sockets.Count); // back down the ramp, or the safe 1 m drop at the far edge
        }

        [Fact]
        [Verifies("MOD-026")]
        public void TwinStairsReachATwoMeterPlatformAndALanding()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-026");
            Assert.Equal(2, g.Pieces.Count(p => p.Kind == SolidKind.Stair));
            Assert.All(g.Pieces.Where(p => p.Kind == SolidKind.Stair), s => Assert.True(s.Box.Size.X >= 2.5f - 1e-3f));
            StairsAreStandard(g);
            ModuleAssert.ReachableTop(g, new Vec2(1.75f, 1.8f), 2f);
            ModuleAssert.ReachableTop(g, new Vec2(-1.75f, 1.8f), 1f);
        }

        [Fact]
        [Verifies("MOD-027")]
        public void BroadSlopeIsATwentyDegreeFiveMeterIncline()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-027");
            ModulePiece ramp = ModuleAssert.Role(g, "ramp").Single();
            var shape = new RampShape(ramp.Box, ramp.RampRise);
            Assert.InRange(shape.SlopeDeg, 19.5f, 20.5f);
            Assert.Equal(5f, shape.Width, 3);
            ModuleAssert.Destructible(ModuleAssert.Role(g, "edge-cover").Single(), 80f);
            ModuleAssert.ReachableTop(g, new Vec2(2.75f, 0f), 2f);
        }

        [Fact]
        [Verifies("MOD-028")]
        public void SunkenCourtSitsOneMeterDownWithTwoBroadRamps()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-028");
            Assert.Equal(-1f, g.MinWalkableHeight(), 3);
            Assert.Single(g.FloorCutouts);
            Assert.Equal(2, ModuleAssert.Role(g, "ramp").Count);
            ModuleAssert.ReachableTop(g, new Vec2(0f, 0f), -1f);
        }

        [Fact]
        [Verifies("MOD-029")]
        public void RaisedSpineHasRampsAtBothEnds()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-029");
            Assert.Equal(2, ModuleAssert.Role(g, "ramp").Count);
            ModuleAssert.ReachableTop(g, new Vec2(0f, 0f), 1f);
        }

        [Fact]
        [Verifies("MOD-030")]
        public void TerracedStepsRiseThreeOneMeterTerracesWithAlternatingCover()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-030");
            var decks = ModuleAssert.Role(g, "deck");
            Assert.Equal(new[] { 1f, 2f, 3f }, decks.Select(d => d.Box.Top).OrderBy(h => h).ToArray());
            var covers = ModuleAssert.Role(g, "edge-cover");
            Assert.Equal(3, covers.Count);
            Assert.All(covers, c => ModuleAssert.Destructible(c, 80f));
            Assert.Contains(covers, c => c.Box.Center.X < 0f);
            Assert.Contains(covers, c => c.Box.Center.X > 0f); // alternating sides
            ModuleAssert.ReachableTop(g, new Vec2(2.5f, 2.4f), 3f);
        }

        [Fact]
        [Verifies("MOD-031")]
        public void OverlookPairHasTwoThreeMeterPlatformsOnSafeStairs()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-031");
            Assert.Equal(2, ModuleAssert.Role(g, "deck").Count(d => d.Box.Top == 3f));
            StairsAreStandard(g);
            ModuleAssert.ReachableTop(g, new Vec2(-3.75f, 2.7f), 3f);
            ModuleAssert.ReachableTop(g, new Vec2(3.75f, 2.7f), 3f);
        }

        [Fact]
        [Verifies("MOD-032")]
        public void RampSwitchbackFoldsTwoRampsAroundLowCenterCover()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-032");
            Assert.Equal(2, ModuleAssert.Role(g, "ramp").Count);
            ModuleAssert.Destructible(ModuleAssert.Role(g, "center-cover").Single(), 80f);
            ModuleAssert.ReachableTop(g, new Vec2(3.5f, 1.6f), 3f);
        }

        [Fact]
        [Verifies("MOD-033")]
        public void HopShortcutLeavesAOneMeterGapBetweenOneMeterPlatforms()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-033");
            var decks = ModuleAssert.Role(g, "deck").OrderBy(d => d.Box.Center.X).ToList();
            Assert.Equal(2, decks.Count);
            float gap = (decks[1].Box.Center.X - decks[1].Box.HalfExtents.X) - (decks[0].Box.Center.X + decks[0].Box.HalfExtents.X);
            Assert.Equal(1f, gap, 3);
            Assert.True(gap <= GlobalRules.Movement.MaxCommonJumpGap);
            ModuleAssert.ReachableTop(g, new Vec2(2f, 1.5f), 1f); // the common-jump ledge
        }

        [Fact]
        [Verifies("MOD-034")]
        public void SteppedGalleryIsAUShapedBalconyWithStairsAtBothEnds()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-034");
            Assert.Equal(2, g.Pieces.Count(p => p.Kind == SolidKind.Stair));
            Assert.Equal(3, ModuleAssert.Role(g, "deck").Count);
            StairsAreStandard(g);
            ModuleAssert.ReachableTop(g, new Vec2(0f, 2.75f), 2f);
        }

        [Fact]
        [Verifies("MOD-035")]
        public void LandingBowlIsTwoMetersDeepWithThreeExitRamps()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-035");
            Assert.Equal(-2f, g.MinWalkableHeight(), 3);
            var ramps = ModuleAssert.Role(g, "ramp");
            Assert.Equal(3, ramps.Count);
            Assert.All(ramps, r => Assert.True(new RampShape(r.Box, r.RampRise).SlopeDeg <= 30f));
            ModuleAssert.ReachableTop(g, new Vec2(0f, -2.5f), -2f);
        }

        [Fact]
        [Verifies("MOD-036")]
        public void WatchDeckIsAFourMeterVantageWithTwoStairsAndASafeLanding()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-036");
            Assert.True(g.IsVantage);
            Assert.Equal(4f, g.MaxWalkableHeight(), 3);
            var stairs = g.Pieces.Where(p => p.Kind == SolidKind.Stair).ToList();
            Assert.Equal(2, stairs.Count);
            StairsAreStandard(g);
            ModuleAssert.ReachableTop(g, new Vec2(0f, 3.75f), 4f);

            // Two access routes: with either stair removed the deck is still reachable.
            foreach (ModulePiece removed in stairs)
            {
                ArenaLayout layout = ArenaFixture.ModuleLayout(g);
                int index = g.Pieces.IndexOf(removed) + 1;
                ArenaNav nav = ArenaNav.Build(layout, new ArenaNavOptions { HalfExtent = 8f, IncludeSolid = s => s.Id != index });
                int top = nav.FindNode(new Vec3(0f, 4f, 3.75f), 0.3f, 1f);
                int floor = nav.FindNode(new Vec3(0f, 0f, -4.5f), 0.3f, 1f);
                Assert.True(nav.Reachable(new[] { floor }, NavQuery.Capsule)[top]);
            }

            // Safe descent without stairs: 4 m deck to the 2 m landing to the floor, each drop at most 3 m.
            ModulePiece landing = ModuleAssert.Role(g, "deck").Single(d => d.Box.Top == 2f);
            Assert.True(4f - landing.Box.Top <= GlobalRules.Terrain.FallDamageThreshold);
        }
    }
}
