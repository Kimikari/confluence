using System.Linq;
using Confluence.Arena.Modules;
using Confluence.Arena.Validation;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Arena.Tests.Modules
{
    /// <summary>
    /// Center modules MOD-061..MOD-072: a clear, anchored 8 m final floor (spec L678, D-027); soft cover on it retires at
    /// 85 s; durability "Edge cover 60; prisms 75; pillars 80; central floor anchored"; point-symmetric for both sides.
    /// </summary>
    public class CenterModuleTests
    {
        private const float FloorRadius = GlobalRules.Boundary.FinalFloorRadius;

        /// <summary>The anchored final floor, and every solid either retires or stands outside the 4 m radius.</summary>
        private static ModuleGeometry AuthoredCenter(string id)
        {
            ModuleGeometry g = ModuleAssert.Authored(id);
            Assert.True(g.IsPointSymmetric);
            Assert.True(ModuleLibrary.IsPointSymmetric(g));
            ModulePiece floor = ModuleAssert.Role(g, "final-floor").Single();
            ModuleAssert.Anchored(floor);
            Assert.True(floor.Walkable);
            Assert.Equal(2f * FloorRadius, floor.Box.Size.X, 3);
            Assert.Equal(0f, floor.Box.Top, 3);
            foreach (ModulePiece p in g.Pieces.Where(p => p.Role != "final-floor"))
            {
                bool onFloor = ArenaSurvey.FootprintDistance(p.Box, Vec2.Zero) < FloorRadius - 0.05f;
                Assert.True(!onFloor || (p.RetiresAtFinal && !p.Anchored), id + " " + p.Role + " intrudes on the final floor");
                Assert.True(!p.RetiresAtFinal || onFloor, id + " " + p.Role + " retires but stands off the floor");
            }

            return g;
        }

        [Fact]
        [Verifies("MOD-061")]
        public void FinalCircleHasTwoOpposedLowCoversThatRetire()
        {
            ModuleGeometry g = AuthoredCenter("MOD-061");
            var covers = ModuleAssert.Role(g, "opposed-low-cover");
            Assert.Equal(2, covers.Count);
            Assert.All(covers, c => ModuleAssert.Destructible(c, 60f));
            Assert.All(covers, c => Assert.True(c.RetiresAtFinal));
            Assert.True(covers[0].Box.Center.Z * covers[1].Box.Center.Z < 0f);
        }

        [Fact]
        [Verifies("MOD-062")]
        public void FourPillarCenterBordersAClearFloorWithThinPillars()
        {
            ModuleGeometry g = AuthoredCenter("MOD-062");
            var pillars = ModuleAssert.Role(g, "thin-pillar");
            Assert.Equal(4, pillars.Count);
            Assert.All(pillars, p => ModuleAssert.Destructible(p, 80f));
            Assert.All(pillars, p => Assert.True(p.Box.Size.X <= 0.6f + 1e-3f));
        }

        [Fact]
        [Verifies("MOD-063")]
        public void CrossWellCenterRingsFlatWellArtWithFourRoutes()
        {
            ModuleGeometry g = AuthoredCenter("MOD-063");
            Assert.Equal(4, ModuleAssert.Role(g, "corner-block").Count);
            ModulePatch well = ModuleAssert.Rule(g, "MOD-063:sealed-well-art").Single();
            Assert.Equal(MaterialFlags.None, well.Flags); // art only: no pit, no collision
            Assert.Equal(4, g.Sockets.Select(s => s.Edge).Distinct().Count());
        }

        [Fact]
        [Verifies("MOD-064")]
        public void PrismCenterSetsTwoEdgePrismsOutsideTheFloor()
        {
            ModuleGeometry g = AuthoredCenter("MOD-064");
            var prisms = ModuleAssert.Role(g, "edge-prism");
            Assert.Equal(2, prisms.Count);
            Assert.All(prisms, p => ModuleAssert.Destructible(p, 75f));
            Assert.All(prisms, p => Assert.True((p.Flags & MaterialFlags.Reflective) != 0));
        }

        [Fact]
        [Verifies("MOD-065")]
        public void DryIslandCenterHasWetBedsOnlyOutsideTheFloor()
        {
            ModuleGeometry g = AuthoredCenter("MOD-065");
            var beds = ModuleAssert.Rule(g, "MOD-065:wet-bed");
            Assert.Equal(2, beds.Count);
            Assert.All(beds, b => Assert.True(b.Water > 0 && b.NaturalSource));
            Assert.All(beds, b => Assert.True(System.Math.Abs(b.Center.Y) - b.HalfSize.Y >= FloorRadius));
        }

        [Fact]
        [Verifies("MOD-066")]
        public void SplitHeightCenterSetsTwoOneMeterLedgesOutsideALevelFloor()
        {
            ModuleGeometry g = AuthoredCenter("MOD-066");
            var ledges = ModuleAssert.Role(g, "deck");
            Assert.Equal(2, ledges.Count);
            Assert.All(ledges, l => Assert.Equal(1f, l.Box.Top, 3));
            Assert.True(1f <= GlobalRules.Movement.MaxCommonJumpLedge);
        }

        [Fact]
        [Verifies("MOD-067")]
        public void AcousticCenterSetsTwoPlatesOutsideTheFloor()
        {
            ModuleGeometry g = AuthoredCenter("MOD-067");
            var plates = ModuleAssert.Role(g, "acoustic-plate");
            Assert.Equal(2, plates.Count);
            Assert.All(plates, p => Assert.True((p.Flags & MaterialFlags.Acoustic) != 0));
        }

        [Fact]
        [Verifies("MOD-068")]
        public void FractureCenterBordersTheFloorWithFourBreakablePanels()
        {
            ModuleGeometry g = AuthoredCenter("MOD-068");
            var panels = ModuleAssert.Role(g, "breakable-panel");
            Assert.Equal(4, panels.Count);
            Assert.All(panels, p => ModuleAssert.Destructible(p, 60f));
            Assert.All(panels, p => Assert.True(p.RetiresAtFinal));
        }

        [Fact]
        [Verifies("MOD-069")]
        public void ConductorCenterIsolatesTwoMetalStripsOutsideTheDryFloor()
        {
            ModuleGeometry g = AuthoredCenter("MOD-069");
            var strips = ModuleAssert.Rule(g, "MOD-069:metal-strip");
            Assert.Equal(2, strips.Count);
            Assert.All(strips, s => Assert.True((s.Flags & MaterialFlags.Conductive) != 0 && s.NaturalSource));
            Assert.Equal(2, ModuleAssert.Rule(g, "MOD-069:insulation").Count);
            Assert.All(strips, s => Assert.True(System.Math.Abs(s.Center.Y) - s.HalfSize.Y >= FloorRadius));
        }

        [Fact]
        [Verifies("MOD-070")]
        public void GardenCenterGrowsTwoCombustibleCorners()
        {
            ModuleGeometry g = AuthoredCenter("MOD-070");
            var hedges = ModuleAssert.Role(g, "growth-corner");
            Assert.Equal(4, hedges.Count); // two L-shaped corners of two pieces each
            Assert.All(hedges, h => ModuleAssert.Destructible(h, 60f));
            Assert.All(hedges, h => Assert.True((h.Flags & MaterialFlags.Combustible) != 0));
        }

        [Fact]
        [Verifies("MOD-071")]
        public void WindCenterLaysTwoCloudGuttersAlongTheFloorEdges()
        {
            ModuleGeometry g = AuthoredCenter("MOD-071");
            var gutters = ModuleAssert.Rule(g, "MOD-071:wind-gutter");
            Assert.Equal(2, gutters.Count);
            Assert.All(gutters, gt => Assert.True(System.Math.Abs(gt.Center.Y) - gt.HalfSize.Y >= FloorRadius));
            Assert.Single(g.Pieces); // only the floor: gutters have no collision
        }

        [Fact]
        [Verifies("MOD-072")]
        public void BalancedCoverCenterPairsLowCoversWithMatchingPeekPosts()
        {
            ModuleGeometry g = AuthoredCenter("MOD-072");
            var covers = ModuleAssert.Role(g, "opposed-low-cover");
            var posts = ModuleAssert.Role(g, "peek-post");
            Assert.Equal(2, covers.Count);
            Assert.Equal(4, posts.Count);
            Assert.All(covers, c => ModuleAssert.Destructible(c, 60f));
            Assert.All(posts, p => ModuleAssert.Destructible(p, 80f));
        }
    }
}
