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
    /// Approach modules MOD-049..MOD-060. Durability text: "Separate cover 80; central block 120; low islands 50;
    /// load-bearing terrain anchored."
    /// </summary>
    public class ApproachModuleTests
    {
        [Fact]
        [Verifies("MOD-049")]
        public void ShieldedAdvanceSpacesThreeLowCoversThreeMetersApart()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-049");
            var covers = ModuleAssert.Role(g, "low-cover").OrderBy(c => c.Box.Center.X).ToList();
            Assert.Equal(3, covers.Count);
            Assert.All(covers, c => ModuleAssert.Destructible(c, 80f));
            Assert.Equal(3f, covers[1].Box.Center.X - covers[0].Box.Center.X, 3);
            Assert.Equal(3f, covers[2].Box.Center.X - covers[1].Box.Center.X, 3);
            Assert.All(covers, c => Assert.Equal(1f, c.Box.Top, 3));
        }

        [Fact]
        [Verifies("MOD-050")]
        public void FlankTunnelBendsOnceWithTwoExits()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-050");
            Assert.Equal(2, g.Sockets.Count);
            Assert.NotEqual(g.Sockets[0].Edge, g.Sockets[1].Edge); // one bend between the exits
            Assert.Single(ModuleAssert.Role(g, "roof")); // opaque from above
            Assert.All(g.Pieces, ModuleAssert.Anchored);
        }

        [Fact]
        [Verifies("MOD-051")]
        public void PeekGalleryHasThreeAlternatingWindowsBesideAnOuterPath()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-051");
            var sills = ModuleAssert.Role(g, "window-sill").OrderBy(s => s.Box.Center.X).ToList();
            Assert.Equal(3, sills.Count);
            Assert.True(sills[0].Box.Top > sills[1].Box.Top && sills[2].Box.Top > sills[1].Box.Top); // standing, crouched, standing
            Assert.True(ModuleAssert.On(g, ModuleEdge.West).Single().Width >= 2.5f); // the outer path
        }

        [Fact]
        [Verifies("MOD-052")]
        public void WideSweepCurvesAroundOneLargeOuterCover()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-052");
            var cover = ModuleAssert.Role(g, "outer-cover");
            Assert.Equal(3, cover.Count); // three segments approximate the curve
            Assert.Contains(cover, c => c.Box.YawDeg > 0f);
            Assert.Contains(cover, c => c.Box.YawDeg < 0f);
            Assert.All(cover, c => Assert.True(c.Box.Center.Z > 1f)); // on the outer side
        }

        [Fact]
        [Verifies("MOD-053")]
        public void PressureFunnelNarrowsFromSixToThreeMetersThenOpensASideExit()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-053");
            Assert.Equal(6f, ModuleAssert.On(g, ModuleEdge.West).Single().Width, 3);
            var walls = ModuleAssert.Role(g, "funnel-wall");
            float north = walls.Where(w => w.Box.Center.Z > 0f).Min(w => w.Box.Center.Z - w.Box.HalfExtents.Z);
            float south = walls.Where(w => w.Box.Center.Z < 0f).Max(w => w.Box.Center.Z + w.Box.HalfExtents.Z);
            Assert.Equal(3f, north - south, 3);
            Assert.NotEmpty(ModuleAssert.On(g, ModuleEdge.North)); // the side exit
        }

        [Fact]
        [Verifies("MOD-054")]
        public void CrossoverRampCarriesATwoMeterRouteOverAGroundLane()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-054");
            ModulePiece bridge = ModuleAssert.Role(g, "bridge").Single();
            Assert.Equal(2f, bridge.Box.Top, 3);
            Assert.True(bridge.Box.Bottom >= GlobalRules.Movement.StandingHeight); // the ground lane keeps headroom
            ModuleAssert.ReachableTop(g, Vec2.Zero, 2f);

            // The ground lane under the bridge runs through, north to south.
            ArenaNav nav = ArenaFixture.ModuleNav(g);
            int north = nav.FindNode(new Vec3(0f, 0f, 2.6f), 0.3f, 0.5f);
            int south = nav.FindNode(new Vec3(0f, 0f, -2.6f), 0.3f, 0.5f);
            bool[] reach = nav.Reachable(new[] { north }, NavQuery.Capsule.WithAllowed(ArenaFixture.FootprintMask(nav, g)));
            Assert.True(reach[south]);
        }

        [Fact]
        [Verifies("MOD-055")]
        public void BlindPivotHasOneLargeCentralBlockAndThreeExits()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-055");
            ModulePiece block = ModuleAssert.Role(g, "central-block").Single();
            ModuleAssert.Destructible(block, 120f);
            Assert.Equal(3, g.Sockets.Select(s => s.Edge).Distinct().Count());
            ModuleAssert.Anchored(ModuleAssert.Role(g, "back-wall").Single());
        }

        [Fact]
        [Verifies("MOD-056")]
        public void WindowBypassPairsAFiringWindowWithAFullWidthCoveredRoute()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-056");
            Assert.All(g.Pieces, p => ModuleAssert.Destructible(p, 80f));
            ModulePiece sill = ModuleAssert.Role(g, "window-sill").Single();
            Assert.Equal(0.8f, sill.Box.Size.X, 3);
            Assert.Equal(2, ModuleAssert.On(g, ModuleEdge.West).Count); // firing side and covered bypass
            Assert.All(ModuleAssert.On(g, ModuleEdge.West), s => Assert.True(s.Width >= 2.5f));
        }

        [Fact]
        [Verifies("MOD-057")]
        public void PropEscortMovesTwoHeavyCoversHalfAMeter()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-057");
            var covers = ModuleAssert.Role(g, "escort-cover");
            Assert.Equal(2, covers.Count);
            Assert.All(covers, c => ModuleAssert.Destructible(c, 80f));
            Assert.All(covers, c => Assert.True(c.IsMovable));
            Assert.All(covers, c => Assert.Equal(0.5f, Vec2.Distance(c.TrackFrom, c.TrackTo), 3));
            Assert.Equal(2, ModuleAssert.Rule(g, "MOD-057:track").Count); // visible tracks
        }

        [Fact]
        [Verifies("MOD-058")]
        public void SteppingCoverBordersAZigzagWithFourLowIslands()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-058");
            var islands = ModuleAssert.Role(g, "low-island").OrderBy(i => i.Box.Center.X).ToList();
            Assert.Equal(4, islands.Count);
            Assert.All(islands, i => ModuleAssert.Destructible(i, 50f));
            for (int i = 1; i < islands.Count; i++)
            {
                Assert.True(islands[i].Box.Center.Z * islands[i - 1].Box.Center.Z < 0f); // alternating sides
            }
        }

        [Fact]
        [Verifies("MOD-059")]
        public void OpenChallengeKeepsATwelveMeterLaneWithTwoBentFlanks()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-059");
            ArenaNav nav = ArenaFixture.ModuleNav(g);
            Assert.True(nav.HasLineOfSight(new Vec3(-5.9f, 1.4f, 0f), new Vec3(5.9f, 1.4f, 0f))); // the 12 m lane
            Assert.Equal(2, ModuleAssert.Role(g, "stagger-block").Count);
            Assert.True(g.Width <= GlobalRules.Arena.FiringLaneMax);
        }

        [Fact]
        [Verifies("MOD-060")]
        public void RetreatLoopCirclesTwoWallsWithAMiddleCutoffAndAudibleGround()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-060");
            var walls = ModuleAssert.Role(g, "loop-wall");
            Assert.Equal(2, walls.Count);
            float cutoff = walls.Max(w => w.Box.Center.Z - w.Box.HalfExtents.Z) - walls.Min(w => w.Box.Center.Z + w.Box.HalfExtents.Z);
            Assert.True(cutoff >= 2.5f - 1e-3f);
            Assert.Equal(2, ModuleAssert.Rule(g, "MOD-060:audible-gravel").Count);
        }
    }
}
