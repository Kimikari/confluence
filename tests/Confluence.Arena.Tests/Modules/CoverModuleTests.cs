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
    /// Cover modules MOD-013..MOD-024. Durability text: "Low wood 60; thin panels 45; stone slab 120; prism 75; metal or
    /// column 90; porous wall 100; hedge 60; parapet segment 40."
    /// </summary>
    public class CoverModuleTests
    {
        [Fact]
        [Verifies("MOD-013")]
        public void LowBarricadeIsOneMeterWoodWithOpenFlanks()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-013");
            ModulePiece cover = g.Pieces.Single();
            ModuleAssert.Destructible(cover, 60f);
            Assert.Equal(ArenaMaterials.Wood, cover.Material);
            Assert.Equal(1f, cover.Box.Top, 3);
            Assert.True(cover.Box.HalfExtents.X < g.Width * 0.5f - 0.4f); // exposed flanking ends
        }

        [Fact]
        [Verifies("MOD-014")]
        public void TallSlabIsAnOpaqueTwoAndAHalfMeterSlab()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-014");
            ModulePiece slab = g.Pieces.Single();
            ModuleAssert.Destructible(slab, 120f);
            Assert.Equal(2.5f, slab.Box.Top, 3);
            Assert.True(slab.BlocksSight);
        }

        [Fact]
        [Verifies("MOD-015")]
        public void WindowScreenHasOneFixedSeventyCentimeterWindow()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-015");
            Assert.All(g.Pieces, p => ModuleAssert.Destructible(p, 90f));
            ModulePiece sill = ModuleAssert.Role(g, "screen-sill").Single();
            ModulePiece head = ModuleAssert.Role(g, "screen-head").Single();
            Assert.Equal(0.7f, sill.Box.Size.X, 3);
            ArenaNav nav = ArenaFixture.ModuleNav(g);
            float x = sill.Box.Center.X;
            Assert.True(nav.HasLineOfSight(new Vec3(x, 1.4f, -1f), new Vec3(x, 1.4f, 1f))); // through the window
            Assert.False(nav.HasLineOfSight(new Vec3(-1f, 1.4f, -1f), new Vec3(-1f, 1.4f, 1f))); // through the screen
            Assert.True(head.Box.Bottom > sill.Box.Top);
        }

        [Fact]
        [Verifies("MOD-016")]
        public void LCornerCoverIsTwoConnectedWallsAroundAnOpenCorner()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-016");
            var walls = ModuleAssert.Role(g, "corner-wall");
            Assert.Equal(2, walls.Count);
            Assert.All(walls, w => ModuleAssert.Destructible(w, 120f));
            Assert.Equal(new[] { ModuleEdge.South, ModuleEdge.West }, g.Sockets.Select(s => s.Edge).OrderBy(e => e).ToArray());
        }

        [Fact]
        [Verifies("MOD-017")]
        public void BrokenColumnIsAFourteenDecimeterColumn()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-017");
            ModulePiece column = g.Pieces.Single();
            ModuleAssert.Destructible(column, 90f);
            Assert.Equal(1.4f, column.Box.Top, 3);
            Assert.Equal(SolidKind.Pillar, column.Kind);
        }

        [Fact]
        [Verifies("MOD-018")]
        public void TwinScreensAreThinPanelsOneMeterApart()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-018");
            var panels = ModuleAssert.Role(g, "thin-panel");
            Assert.Equal(2, panels.Count);
            Assert.All(panels, p => ModuleAssert.Destructible(p, 45f));
            float gap = (panels.Max(p => p.Box.Center.Z) - panels.Max(p => p.Box.HalfExtents.Z)) - (panels.Min(p => p.Box.Center.Z) + panels.Min(p => p.Box.HalfExtents.Z));
            Assert.Equal(1f, gap, 2);
        }

        [Fact]
        [Verifies("MOD-019")]
        public void PrismShieldPairsAnOpaqueShieldWithAMarkedPrism()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-019");
            ModulePiece prism = ModuleAssert.Role(g, "prism").Single();
            ModuleAssert.Destructible(prism, 75f);
            Assert.True((prism.Flags & MaterialFlags.Reflective) != 0);
            ModulePiece shield = ModuleAssert.Role(g, "shield").Single();
            ModuleAssert.Destructible(shield, 120f);
            Assert.Equal(2f, shield.Box.Top, 3);
        }

        [Fact]
        [Verifies("MOD-020")]
        public void ConductiveGrilleIsASolidPlateUnderASeeThroughGrille()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-020");
            ModulePiece plate = ModuleAssert.Role(g, "lower-plate").Single();
            ModulePiece grille = ModuleAssert.Role(g, "upper-grille").Single();
            Assert.All(new[] { plate, grille }, p => ModuleAssert.Destructible(p, 90f));
            Assert.True(plate.BlocksSight);
            Assert.False(grille.BlocksSight);
            Assert.True((grille.Flags & MaterialFlags.Conductive) != 0);
            Assert.Equal(plate.Box.Top, grille.Box.Bottom, 3);
        }

        [Fact]
        [Verifies("MOD-021")]
        public void PorousWallIsAnAbsorbentStoneFace()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-021");
            ModulePiece wall = g.Pieces.Single();
            ModuleAssert.Destructible(wall, 100f);
            Assert.True((wall.Flags & MaterialFlags.Porous) != 0 && (wall.Flags & MaterialFlags.Absorbent) != 0);
        }

        [Fact]
        [Verifies("MOD-022")]
        public void HedgeBendIsAnLShapedCombustibleHedge()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-022");
            var hedges = ModuleAssert.Role(g, "hedge");
            Assert.Equal(2, hedges.Count);
            Assert.All(hedges, h => ModuleAssert.Destructible(h, 60f));
            Assert.All(hedges, h => Assert.True((h.Flags & MaterialFlags.Combustible) != 0));
        }

        [Fact]
        [Verifies("MOD-023")]
        public void SlidingShieldMovesAlongAMarkedFourMeterTrack()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-023");
            ModulePiece shield = ModuleAssert.Role(g, "sliding-shield").Single();
            ModuleAssert.Destructible(shield, 90f);
            Assert.True(shield.IsMovable);
            Assert.Equal(1.6f, shield.Box.Top, 3);
            float sweep = Vec2.Distance(shield.TrackFrom, shield.TrackTo) + shield.Box.Size.X;
            Assert.Equal(4f, sweep, 3);
            Assert.Equal(4f, ModuleAssert.Rule(g, "MOD-023:track").Single().HalfSize.X * 2f, 3);
        }

        [Fact]
        [Verifies("MOD-024")]
        public void SegmentedParapetBreaksInThreeSeparateSegments()
        {
            ModuleGeometry g = ModuleAssert.Authored("MOD-024");
            var segments = ModuleAssert.Role(g, "parapet-segment");
            Assert.Equal(3, segments.Count);
            Assert.All(segments, s => ModuleAssert.Destructible(s, 40f));
            Assert.All(segments, s => Assert.Equal(1f, s.Box.Top, 3));
        }
    }
}
