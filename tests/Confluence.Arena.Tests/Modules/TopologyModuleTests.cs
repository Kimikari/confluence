using System.Linq;
using Confluence.Arena.Modules;
using Confluence.Arena.Navigation;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;

namespace Confluence.Arena.Tests.Modules
{
    /// <summary>Topology modules MOD-001..MOD-012: anchored terrain, attached cover 80 and center cover 120.</summary>
    public class TopologyModuleTests
    {
        [Fact]
        [Verifies("MOD-001")]
        public void SplitGateHasTwoDoorsBesideAnAnchoredPillar()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-001");
            ModulePiece pillar = ModuleAssert.Role(g, "center-pillar").Single();
            ModuleAssert.Anchored(pillar);
            Assert.Equal(0.4f, pillar.Box.Size.X, 3);
            Assert.All(ModuleAssert.On(g, ModuleEdge.North).Concat(ModuleAssert.On(g, ModuleEdge.South)), s => Assert.Equal(2.8f, s.Width, 3));
            Assert.Equal(2, ModuleAssert.On(g, ModuleEdge.North).Count);
            Assert.True(ModuleAssert.Role(g, "lintel").Single().Box.Bottom >= 2.7f - 1e-3f); // walk under the gate
        }

        [Fact]
        [Verifies("MOD-002")]
        public void DoglegLaneTurnsOnceWithTwoPointEightMetersOfRoute()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-002");
            Assert.Equal(2, g.Sockets.Count);
            Assert.Contains(g.Sockets, s => s.Edge == ModuleEdge.West);
            Assert.Contains(g.Sockets, s => s.Edge == ModuleEdge.North);
            Assert.All(g.Sockets, s => Assert.Equal(2.8f, s.Width, 3));
            Assert.All(g.Pieces, ModuleAssert.Anchored);
        }

        [Fact]
        [Verifies("MOD-003")]
        public void ForkedApproachSplitsAroundFourMetersOfCenterCover()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-003");
            ModulePiece cover = ModuleAssert.Role(g, "center-cover").Single();
            ModuleAssert.Destructible(cover, 120f);
            Assert.Equal(4f, cover.Box.Size.Z, 3);
            // Both branches beside the cover stay wider than a 2.5 m route.
            float branch = g.Width * 0.5f - 0.4f - cover.Box.HalfExtents.X;
            Assert.True(branch >= 2.5f);
        }

        [Fact]
        [Verifies("MOD-004")]
        public void CrossingCourtOpensFourWaysWithOneOffsetLowCover()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-004");
            Assert.Equal(4, g.Sockets.Select(s => s.Edge).Distinct().Count());
            ModulePiece cover = ModuleAssert.Role(g, "low-cover").Single();
            ModuleAssert.Destructible(cover, 80f);
            Assert.Equal(1f, cover.Box.Top, 3);
            Assert.NotEqual(0f, cover.Box.Center.X); // offset from the crossing's center
        }

        [Fact]
        [Verifies("MOD-005")]
        public void OffsetAlleyHasThreeStaggeredScreensBesideASideRoute()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-005");
            var screens = ModuleAssert.Role(g, "screen");
            Assert.Equal(3, screens.Count);
            Assert.All(screens, s => ModuleAssert.Destructible(s, 80f));
            Assert.Equal(3, screens.Select(s => s.Box.Center.X).Distinct().Count()); // staggered along the alley
            Assert.All(screens, s => Assert.True(s.Box.Center.Z > 0.6f)); // all on one side of the continuous route
            Assert.All(g.Sockets, s => Assert.True(s.Width >= 2.5f));
        }

        [Fact]
        [Verifies("MOD-006")]
        public void RingWalkCirclesAFourMeterAnchoredPlanter()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-006");
            ModulePiece planter = ModuleAssert.Role(g, "planter").Single();
            ModuleAssert.Anchored(planter);
            Assert.Equal(4f, planter.Box.Size.X, 3);
            Assert.Equal(4f, planter.Box.Size.Z, 3);
            Assert.Equal(4, g.Sockets.Count);

            // The walkway around the planter is a closed loop.
            ArenaNav nav = ArenaFixture.ModuleNav(g);
            int start = nav.FindNode(new Vec3(0f, 0f, 3.5f), 0.3f, 0.5f);
            bool[] reach = nav.Reachable(new[] { start }, NavQuery.Capsule.WithAllowed(ArenaFixture.FootprintMask(nav, g)));
            foreach (Vec2 p in new[] { new Vec2(3.5f, 0f), new Vec2(0f, -3.5f), new Vec2(-3.5f, 0f) })
            {
                Assert.True(reach[nav.FindNode(new Vec3(p.X, 0f, p.Y), 0.3f, 0.5f)]);
            }
        }

        [Fact]
        [Verifies("MOD-007")]
        public void TJunctionJoinsThreeLanes()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-007");
            Assert.Equal(3, g.Sockets.Count);
            Assert.Equal(new[] { ModuleEdge.East, ModuleEdge.South, ModuleEdge.West }, g.Sockets.Select(s => s.Edge).OrderBy(e => e).ToArray());
            Assert.Equal(2, ModuleAssert.Role(g, "peek-block").Count);
        }

        [Fact]
        [Verifies("MOD-008")]
        public void DoubleBendHasTwoOpposingBafflesAroundAPocket()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-008");
            var baffles = ModuleAssert.Role(g, "baffle");
            Assert.Equal(2, baffles.Count);
            Assert.True(baffles[0].Box.Center.Z * baffles[1].Box.Center.Z < 0f); // from opposite walls
            Assert.True(baffles.Max(b => b.Box.Center.X) - baffles.Min(b => b.Box.Center.X) >= 3f); // 3 m pocket between
        }

        [Fact]
        [Verifies("MOD-009")]
        public void DiagonalCutOpensADiagonalLaneBetweenTwoScreens()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-009");
            var walls = ModuleAssert.Role(g, "perimeter-wall");
            Assert.Equal(2, walls.Count);
            Assert.All(walls, w => Assert.Equal(315f, CMath.WrapAngle360(w.Box.YawDeg), 1));
            // The lane between the walls runs corner to corner with a clear sightline.
            ArenaNav nav = ArenaFixture.ModuleNav(g);
            Assert.True(nav.HasLineOfSight(new Vec3(-3.4f, 1.4f, -3.4f), new Vec3(3.4f, 1.4f, 3.4f)));
        }

        [Fact]
        [Verifies("MOD-010")]
        public void BridgePairRaisesTwoThreeMeterBridgesOverAGroundCrossover()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-010");
            var decks = ModuleAssert.Role(g, "deck");
            Assert.Equal(2, decks.Count);
            Assert.All(decks, d => Assert.Equal(3f, d.Box.Size.X, 3));
            Assert.All(decks, d => Assert.Equal(1f, d.Box.Top, 3));
            Assert.Equal(4, ModuleAssert.Role(g, "ramp").Count);
            ModuleAssert.ReachableTop(g, new Vec2(-3f, 0f), 1f);
            Assert.Contains(g.Sockets, s => s.Edge == ModuleEdge.North && s.Offset == 0f); // ground crossover
        }

        [Fact]
        [Verifies("MOD-011")]
        public void BraidedPathsRunThreeRoutesThatReconnectTwice()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-011");
            Assert.Equal(3, ModuleAssert.On(g, ModuleEdge.North).Count);
            Assert.Equal(3, ModuleAssert.On(g, ModuleEdge.South).Count);
            Assert.Equal(4, ModuleAssert.Role(g, "divider").Count);
        }

        [Fact]
        [Verifies("MOD-012")]
        public void OuterArcadeOpensThreeArchesOntoAWalkway()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-012");
            Assert.Equal(3, ModuleAssert.On(g, ModuleEdge.South).Count);
            Assert.Equal(3, ModuleAssert.Role(g, "arch").Count);
            Assert.All(ModuleAssert.Role(g, "arch"), a => Assert.True(a.Box.Bottom >= 2.5f));
            Assert.Single(ModuleAssert.On(g, ModuleEdge.West));
        }
    }
}
