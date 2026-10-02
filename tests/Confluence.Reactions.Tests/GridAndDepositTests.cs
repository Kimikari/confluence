using System.Collections.Generic;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;

namespace Confluence.Reactions.Tests
{
    public class GridAndDepositTests
    {
        [Theory]
        [InlineData(24, 48)]
        [InlineData(32, 64)]
        [InlineData(40, 80)]
        [InlineData(48, 96)]
        public void Grid_Covers_The_Arena_Square_In_Half_Meter_Cells(int diameter, int cells)
        {
            var layout = new ArenaLayout { Diameter = diameter, Center = new Vec3(3f, 0f, -2f) };
            CellGrid grid = CellGrid.ForArena(layout);
            Assert.Equal(cells, grid.Width);
            Assert.Equal(cells, grid.Depth);
            Assert.Equal(0.5f, CellGrid.CellSize);
            Assert.Equal(3f - diameter / 2f, grid.Origin.X, 4);
            Assert.Equal(-2f - diameter / 2f, grid.Origin.Y, 4);
            Assert.True(grid.TryGetIndex(new Vec2(3f, -2f), out _));
            Assert.False(grid.TryGetIndex(new Vec2(3f + diameter, -2f), out _));
        }

        [Fact]
        public void Rasterize_Uses_Cell_Centers_And_Index_Order()
        {
            var grid = CellGrid.ForDiameter(Vec2.Zero, 24f);
            var cells = new List<int>();
            Vec2 center = grid.CellCenter(24, 24);
            grid.Rasterize(Footprint.Circle(center, 1f), cells);
            int expected = 0;
            for (int i = 0; i < grid.CellCount; i++)
            {
                if (Vec2.Distance(grid.CellCenter(i), center) <= 1f) expected++;
            }

            Assert.Equal(expected, cells.Count);
            for (int i = 1; i < cells.Count; i++)
            {
                Assert.True(cells[i] > cells[i - 1]);
            }

            cells.Clear();
            grid.Rasterize(Footprint.Point(center + new Vec2(0.2f, -0.2f)), cells);
            Assert.Equal(new[] { grid.IndexOf(24, 24) }, cells);

            cells.Clear();
            grid.Rasterize(Footprint.Circle(center + new Vec2(0.2f, 0.2f), 0.05f), cells);
            Assert.Equal(new[] { grid.IndexOf(24, 24) }, cells); // tiny shape keeps its anchor cell

            cells.Clear();
            grid.Rasterize(Footprint.Lane(grid.CellCenter(10, 20), grid.CellCenter(29, 20), 0.5f), cells);
            Assert.Equal(20, cells.Count); // 10 m lane one cell wide

            cells.Clear();
            grid.Rasterize(Footprint.Cone(grid.CellCenter(24, 24), Vec2.UnitX, 2f, 10f), cells);
            Assert.Contains(grid.IndexOf(27, 24), cells);
            Assert.DoesNotContain(grid.IndexOf(24, 27), cells);
        }

        [Fact]
        public void Quick_Projectile_Deposits_One_Local_Unit()
        {
            var h = new Harness();
            Vec3 p = h.CellPoint(24, 24);
            h.World.Deposit(MaterialDeposit.QuickProjectile(Material.Water, p, Harness.PlayerA, 1, Ticks.FromSeconds(3f)));
            h.Step();
            Assert.Equal(1, h.World.UnitsAt(p).Water);
            Assert.Equal(1, h.World.ActiveCellCount);
            Assert.Equal(GlobalRules.World.QuickProjectileDeposit, h.World.Ledger.Deposited(Material.Water));
        }

        [Fact]
        public void Field_Deposits_One_Unit_Per_Covered_Cell_On_Creation()
        {
            var h = new Harness();
            Vec3 c = h.CellPoint(24, 24);
            h.World.Deposit(MaterialDeposit.Field(Material.Frost, Footprint.Circle(c.XZ(), 1.5f), 0f, 4f, Harness.PlayerA, 1));
            h.Step();
            var cells = new List<int>();
            h.Grid.Rasterize(Footprint.Circle(c.XZ(), 1.5f), cells);
            foreach (int i in cells)
            {
                Assert.Equal(GlobalRules.World.FieldDepositPerCell, h.World.UnitsAtCell(i).Frost);
            }

            Assert.Equal(cells.Count, h.World.ActiveCellCount);
        }

        [Fact]
        public void Heavy_Projectile_Deposits_Two_At_Impact_And_One_Within_One_Meter()
        {
            var h = new Harness();
            Vec3 p = h.CellPoint(24, 24);
            h.World.Deposit(MaterialDeposit.HeavyProjectile(Material.Frost, p, Harness.PlayerA, 1, lifetimeTicks: 60));
            h.Step();
            Assert.Equal(2, h.World.UnitsAt(p).Frost);
            for (int i = 0; i < h.Grid.CellCount; i++)
            {
                if (i == h.Index(p)) continue;
                float d = Vec2.Distance(h.Grid.CellCenter(i), p.XZ());
                Assert.Equal(d <= 1f ? 1 : 0, h.World.UnitsAtCell(i).Frost);
            }
        }

        [Fact]
        public void Adding_Strength_Never_Exceeds_Four_Units()
        {
            var h = new Harness();
            Vec3 p = h.CellPoint(5, 5);
            h.Point(Material.Dust, p, 3, 120);
            h.Point(Material.Dust, p, 3, 120, Harness.PlayerB, 2);
            h.Step();
            Assert.Equal(GlobalRules.World.MaxUnitsPerCell, h.World.UnitsAt(p).Dust);
            Assert.Equal(4, h.World.Ledger.Deposited(Material.Dust));
            Assert.Equal(2, h.World.Ledger.Clipped(Material.Dust));
        }

        [Fact]
        public void Instant_Residue_Decays_In_The_Same_Step_And_Lifetimes_Are_Exact()
        {
            var h = new Harness();
            Vec3 a = h.CellPoint(5, 5);
            Vec3 b = h.CellPoint(10, 10);
            h.Point(Material.Heat, a, 2); // instant
            h.Field(Material.Water, Footprint.Point(b.XZ()), 2, 6f);
            h.Step(); // tick 0
            Assert.Equal(0, h.World.UnitsAt(a).Heat);
            Assert.Equal(2, h.World.Ledger.Expired(Material.Heat));
            h.StepFor(359 - 0); // through tick 359
            Assert.Equal(2, h.World.UnitsAt(b).Water);
            h.Step(); // tick 360: 6 s after creation
            Assert.Equal(0, h.World.UnitsAt(b).Water);
            Assert.Equal(0, h.World.ActiveCellCount);
        }

        [Fact]
        public void Player_Material_Lasts_At_Most_The_Eight_Second_Field_Lifetime()
        {
            var h = new Harness();
            Vec3 p = h.CellPoint(5, 5);
            h.Field(Material.Growth, Footprint.Point(p.XZ()), 1, 30f);
            h.StepFor(Ticks.FromSeconds(8f));
            Assert.Equal(1, h.World.UnitsAt(p).Growth); // ticks 0..479
            h.Step();
            Assert.Equal(0, h.World.UnitsAt(p).Growth); // gone at 8 s, not 30 s
        }

        [Fact]
        public void Deposits_Outside_The_Grid_Are_Clipped_Without_State()
        {
            var h = new Harness();
            h.Point(Material.Water, new Vec3(40f, 0f, 40f), 2, 60);
            h.Step();
            Assert.Equal(0, h.World.ActiveCellCount);
        }
    }
}
