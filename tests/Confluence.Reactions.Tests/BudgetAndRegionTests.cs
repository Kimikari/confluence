using System;
using System.Collections.Generic;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Random;
using Confluence.Core.Rules;
using Confluence.Reactions.Events;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Volumes;

namespace Confluence.Reactions.Tests
{
    /// <summary>
    /// Source-strength accounting (spec L165 "Reactions cannot generate more energy than their source permits") and
    /// the 128 active-region cap (L136).
    /// </summary>
    public class BudgetAndRegionTests
    {
        private static long GridTotal(ReactionWorld world, Material m)
        {
            long total = 0;
            for (int i = 0; i < world.Grid.CellCount; i++)
            {
                int units = world.UnitsAtCell(i)[m];
                Assert.InRange(units, 0, GlobalRules.World.MaxUnitsPerCell);
                total += units;
            }

            return total;
        }

        [Fact]
        public void Over_Many_Random_Ticks_No_Reaction_Creates_Material_Or_Energy()
        {
            ArenaLayout layout = TestArenas.Empty(biomeId: "BIO-001");
            layout.Materials.Add(TestArenas.Bed(new Vec2(-5f, 0f), 0.5f, 1f, water: 2, refillRule: "rain"));
            layout.Materials.Add(TestArenas.Bed(new Vec2(5f, 0f), 2f, 2f, sand: 2, flags: MaterialFlags.Sand));
            var h = new Harness(new ReactionWorld(layout));
            h.Bodies.Add(ReactionBody.Player(0, new Vec3(-2f, 0f, 1f)));
            var rng = new Rng(4242UL);
            int reactions = 0;
            for (int tick = 0; tick < 2400; tick++)
            {
                foreach (Action<ReactionWorld> op in RandomScenario.Ops(rng, h.Grid, tick))
                {
                    op(h.World);
                }

                StepResult r = h.Step();
                reactions += r.Count(ReactionKind.Steam) + r.Count(ReactionKind.IceFormed) + r.Count(ReactionKind.IceMelted);

                UnitLedger ledger = h.World.Ledger;
                foreach (Material m in Materials.All)
                {
                    Assert.Equal(ledger.Expected(m), GridTotal(h.World, m));
                }

                // Water only reappears as melt of a frozen unit or as condensate of steamed water.
                Assert.True(ledger.Produced(Material.Water) <= ledger.IceTilesMelted + ledger.CondensedWater);
                Assert.True(ledger.IceTilesMelted <= ledger.IceTilesFormed + ledger.IceTilesInitial);
                Assert.True(ledger.CondensedWater <= ledger.SteamWater);
                foreach (Material m in new[] { Material.Heat, Material.Frost, Material.Dust, Material.Sand, Material.Growth, Material.Venom })
                {
                    Assert.Equal(0, ledger.Produced(m)); // nothing else is ever produced by a reaction
                }

                long cloudWater = 0;
                foreach (ObscurationVolume v in h.World.Volumes) cloudWater += v.SourceWater;
                Assert.True(cloudWater <= ledger.SteamWater - ledger.CondensedWater);
            }

            Assert.True(reactions > 20, "the scenario should exercise the consumption reactions");
        }

        [Fact]
        public void A_Burn_Water_Steam_Condense_Loop_Runs_Out_Of_Purchased_Strength()
        {
            var h = new Harness();
            Vec3 p = h.CellPoint(24, 24);
            h.Field(Material.Water, Footprint.Point(p.XZ()), 4, 8f, Harness.PlayerB, 1);
            h.Step();
            int source = 10;
            int water = h.World.UnitsAt(p).Water;
            for (int cycle = 0; cycle < 6; cycle++)
            {
                // Each cycle buys heat to boil whatever water is left, then condenses the cloud back.
                h.Point(Material.Heat, p, 4, 0, Harness.PlayerA, source++);
                h.Step();
                Assert.Equal(0, h.World.UnitsAt(p).Water);
                if (h.World.Volumes.Count > 0)
                {
                    h.World.Condense(new CondenseRequest { VolumeId = h.World.Volumes[0].Id, LifetimeTicks = 300, Owner = Harness.PlayerB, SourceEventId = source++ });
                }

                h.Step();
                int now = h.World.UnitsAt(p).Water;
                Assert.True(now <= water, "a loop never returns more water than it boiled");
                Assert.True(now <= 1, "condensing returns at most one unit per column");
                water = now;
            }

            // Without new purchased heat nothing more happens, and the condensate expires with its stated lifetime.
            StepLog quiet = h.StepSeconds(6f);
            Assert.Equal(0, quiet.Count(ReactionKind.Steam));
            Assert.Equal(0, h.World.UnitsAt(p).Water);
            Assert.Equal(0, h.World.ActiveCellCount);
            Assert.True(h.World.Ledger.Consumed(Material.Heat) <= h.World.Ledger.Deposited(Material.Heat));
        }

        [Fact]
        public void A_Freeze_Melt_Loop_Never_Creates_Water()
        {
            var h = new Harness();
            Vec3 p = h.CellPoint(24, 24);
            h.Field(Material.Water, Footprint.Point(p.XZ()), 1, 8f, Harness.PlayerB, 1);
            h.Step();
            for (int cycle = 0; cycle < 10; cycle++)
            {
                h.Point(Material.Frost, p, 2, 0, Harness.PlayerA, 100 + cycle * 2);
                h.Step();
                Assert.True(h.World.HasIceAt(p));
                Assert.Equal(0, h.World.UnitsAt(p).Water);
                h.Point(Material.Heat, p, 2, 0, Harness.PlayerA, 101 + cycle * 2);
                h.Step();
                Assert.False(h.World.HasIceAt(p));
                Assert.Equal(1, h.World.UnitsAt(p).Water); // the same unit back, never more
            }

            Assert.Equal(10, h.World.Ledger.IceTilesMelted);
            Assert.Equal(20, h.World.Ledger.Consumed(Material.Heat));
        }

        [Fact]
        public void Adjacent_Effects_Merge_Into_One_Contiguous_Region()
        {
            var h = new Harness();
            h.Field(Material.Water, Footprint.Circle(h.CellPoint(20, 20).XZ(), 1f), 1, 8f);
            h.Field(Material.Dust, Footprint.Circle(h.CellPoint(23, 20).XZ(), 1f), 1, 8f, source: 2);
            h.Field(Material.Frost, Footprint.Circle(h.CellPoint(40, 40).XZ(), 0.5f), 1, 8f, source: 3);
            h.Step();
            Assert.Equal(2, h.World.Regions.Count);
            Assert.Equal(2, h.World.RegionComponentCount);
            Assert.All(h.World.Regions, r => Assert.Equal(1, r.Parts));
            ActiveRegion big = h.World.Regions[0];
            Assert.Contains(h.Index(h.CellPoint(20, 20)), big.Cells);
            Assert.Contains(h.Index(h.CellPoint(23, 20)), big.Cells);
        }

        [Fact]
        public void More_Than_128_Separate_Effects_Share_Representations_Without_Losing_State()
        {
            var h = new Harness();
            var points = new List<Vec3>();
            for (int z = 0; z < 48 && points.Count < 140; z += 3)
            {
                for (int x = 0; x < 48 && points.Count < 140; x += 3)
                {
                    points.Add(h.CellPoint(x, z));
                }
            }

            // One larger patch, so the merge order (smallest components first) is visible.
            h.Field(Material.Water, Footprint.Rectangle(h.CellPoint(46, 46).XZ(), new Vec2(0.5f, 0.5f), 0f), 1, 8f, source: 999);
            for (int i = 0; i < points.Count; i++)
            {
                h.Point(Material.Water, points[i], 1 + i % 3, 300, Harness.PlayerA, i + 1);
            }

            h.Step();
            Assert.True(h.World.RegionComponentCount > GlobalRules.World.MaxActiveCellRegions);
            Assert.Equal(GlobalRules.World.MaxActiveCellRegions, h.World.Regions.Count);

            int cells = 0;
            int parts = 0;
            foreach (ActiveRegion r in h.World.Regions)
            {
                cells += r.CellCount;
                parts += r.Parts;
            }

            Assert.Equal(h.World.ActiveCellCount, cells);
            Assert.Equal(h.World.RegionComponentCount, parts);
            for (int i = 0; i < points.Count; i++)
            {
                Assert.Equal(1 + i % 3, h.World.UnitsAt(points[i]).Water); // no state deleted to meet the cap
            }

            // The cap only merges representations; every cell keeps reacting.
            h.Point(Material.Heat, points[points.Count - 1], 1, 0, Harness.PlayerB, 5000);
            Assert.Equal(1, h.Step().Count(ReactionKind.Steam));
        }
    }
}
