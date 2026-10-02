using System.Collections.Generic;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions.Events;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Volumes;

namespace Confluence.Reactions.Tests
{
    /// <summary>Heat and water (spec L144) and steam volumes (L167).</summary>
    public class SteamTests
    {
        [Fact]
        public void Heat_And_Water_Consume_One_Unit_Each_In_Overlapping_Cells()
        {
            var h = new Harness();
            // Water 2 on cells x = 10..12, heat 1 on x = 11..13 (x = 13 is dry).
            h.Field(Material.Water, Footprint.Lane(h.CellPoint(10, 20).XZ(), h.CellPoint(12, 20).XZ(), 0.5f), 2, 6f, source: 1);
            h.Step();
            h.Field(Material.Heat, Footprint.Lane(h.CellPoint(11, 20).XZ(), h.CellPoint(13, 20).XZ(), 0.5f), 1, 0f, Harness.PlayerB, 2);
            StepResult r = h.Step();

            Assert.Equal(2, h.World.UnitsAt(h.CellPoint(10, 20)).Water);
            Assert.Equal(1, h.World.UnitsAt(h.CellPoint(11, 20)).Water);
            Assert.Equal(1, h.World.UnitsAt(h.CellPoint(12, 20)).Water);
            Assert.Equal(0, h.World.UnitsAt(h.CellPoint(13, 20)).Heat); // dry heat makes no steam and decays

            ObscurationVolume steam = h.OnlyVolume();
            Assert.Equal(VolumeKind.Steam, steam.Kind);
            Assert.Equal(2, steam.ColumnCount);
            Assert.True(steam.ContainsPoint(h.CellPoint(11, 20) + new Vec3(0f, 1f, 0f)));
            Assert.False(steam.ContainsPoint(h.CellPoint(13, 20) + new Vec3(0f, 1f, 0f)));
            Assert.Equal(2, steam.SourceWater);
            Assert.Equal(2, h.World.Ledger.Consumed(Material.Heat));
            Assert.Equal(2, h.World.Ledger.Consumed(Material.Water));
            Assert.Equal(1, r.Count(ReactionKind.Steam));
        }

        [Fact]
        public void Steam_Lasts_Three_Seconds_Rises_Two_Meters_And_Deals_No_Damage()
        {
            var h = new Harness();
            Vec3 p = h.CellPoint(24, 24);
            h.Bodies.Add(ReactionBody.Player(7, p));
            h.Point(Material.Water, p, 1, 300);
            h.Point(Material.Heat, p, 1, 0, Harness.PlayerB, 2);
            StepLog log = h.StepFor(1);
            ObscurationVolume steam = h.OnlyVolume();
            Assert.Equal(0f, steam.BaseY);
            Assert.Equal(GlobalRules.World.SteamRise, steam.Height);
            Assert.True(steam.ContainsPoint(p + new Vec3(0f, 1.99f, 0f)));
            Assert.False(steam.ContainsPoint(p + new Vec3(0f, 2.01f, 0f)));
            Assert.Equal(Ticks.FromSeconds(GlobalRules.World.SteamLifetimeSec), steam.ExpiresTick - steam.CreatedTick);

            log.Add(h.Step());
            for (int i = 0; i < Ticks.FromSeconds(3f) - 2; i++)
            {
                log.Add(h.Step());
            }

            Assert.Single(h.World.Volumes); // tick 179
            log.Add(h.Step());              // tick 180 = 3 s
            Assert.Empty(h.World.Volumes);
            Assert.Empty(log.Damage);       // L144: no steam damage by itself
            Assert.Contains(log.VolumeEvents, e => e.Kind == VolumeEventKind.Retired && e.Reason == VolumeRetireReason.Expired);
        }

        [Fact]
        public void The_Last_Direct_Source_Owns_The_Cloud_And_Map_Steam_Counts_For_Nobody()
        {
            var h = new Harness();
            Vec3 a = h.CellPoint(10, 10);
            h.Point(Material.Water, a, 1, 300, Harness.PlayerA, 5);
            h.Point(Material.Heat, a, 1, 0, Harness.PlayerB, 9);
            StepResult r = h.Step();
            ObscurationVolume steam = h.OnlyVolume();
            Assert.Equal(Harness.PlayerB, steam.Owner);
            Assert.Equal(9, steam.SourceEventId);
            Assert.True(steam.CountsAsField);
            VolumeEvent created = Assert.Single(r.VolumeEvents);
            Assert.Equal(VolumeEventKind.Created, created.Kind);
            Assert.Equal(Harness.PlayerB, created.Owner);
            Assert.True(created.CountsAsField);

            // Natural water and natural heat (BIO-002 style bed meeting a rain bed) make map-owned steam.
            var layout = new ArenaLayout { Diameter = 24 };
            layout.Materials.Add(new MaterialPatch { Center = new Vec2(0.25f, 0.25f), Radius = 0.2f, Water = 1, Heat = 1, NaturalSource = true });
            var world = new ReactionWorld(layout);
            world.Step(0, null);
            ObscurationVolume mapSteam = Assert.Single(world.Volumes);
            Assert.Equal(ReactionRules.MapOwner, mapSteam.Owner);
            Assert.False(mapSteam.CountsAsField);
        }

        [Fact]
        public void A_Lingering_Heat_Trail_Extends_One_Cloud_And_Runs_Out_Of_Water()
        {
            var h = new Harness();
            Vec2 a = h.CellPoint(10, 10).XZ();
            Vec2 b = h.CellPoint(13, 10).XZ();
            h.Field(Material.Water, Footprint.Lane(a, b, 0.5f), 4, 8f, source: 1);
            h.Step();
            h.Field(Material.Heat, Footprint.Lane(a, b, 0.5f), 1, 3f, Harness.PlayerB, 2); // Phoenix Rush style 3 s trail
            StepLog log = h.StepFor(Ticks.FromSeconds(3f));

            ObscurationVolume steam = h.OnlyVolume(); // one field allocation for the whole trail
            Assert.Equal(4, steam.ColumnCount);
            Assert.Single(log.VolumeEvents, e => e.Kind == VolumeEventKind.Created);
            Assert.Equal(3, h.World.UnitsAt(h.CellPoint(11, 10)).Water); // one heat unit per cell, consumed once
            Assert.Equal(4, h.World.Ledger.Consumed(Material.Heat));
        }

        /// <summary>
        /// Spec L179 worked duel: Fireball (Heat 2 at impact, heavy projectile pattern L163) on a Water Pool's
        /// (Water 2, 3 m radius) near edge consumes local water and makes a bounded steam patch, leaving the rest of
        /// the pool intact.
        /// </summary>
        [Fact]
        public void Fireball_Into_Water_Pool_Edge_Makes_A_Bounded_Steam_Patch()
        {
            var h = new Harness();
            Vec3 center = h.CellPoint(24, 24);
            h.World.Deposit(MaterialDeposit.Field(Material.Water, Footprint.Circle(center.XZ(), 3f), 0f, 6f, Harness.PlayerB, 10, 2, fieldId: 76));
            h.Step();
            Vec3 impact = center + new Vec3(2.5f, 0f, 0f);
            h.World.Deposit(MaterialDeposit.HeavyProjectile(Material.Heat, impact, Harness.PlayerA, 20));
            StepResult r = h.Step();

            ObscurationVolume steam = h.OnlyVolume();
            Assert.Equal(Harness.PlayerA, steam.Owner);
            Assert.Equal(20, steam.SourceEventId);
            Assert.Single(r.VolumeEvents);

            int expectedColumns = 0;
            for (int i = 0; i < h.Grid.CellCount; i++)
            {
                Vec2 c = h.Grid.CellCenter(i);
                bool inPool = Vec2.Distance(c, center.XZ()) <= 3f;
                bool inBlast = Vec2.Distance(c, impact.XZ()) <= 1f;
                Vec3 p = new Vec3(c.X, 1f, c.Y);
                if (inPool && inBlast)
                {
                    expectedColumns++;
                    Assert.True(steam.ContainsPoint(p));
                    Assert.Equal(i == h.Index(impact) ? 0 : 1, h.World.UnitsAtCell(i).Water);
                }
                else
                {
                    Assert.False(steam.ContainsPoint(p));
                    Assert.Equal(inPool ? 2 : 0, h.World.UnitsAtCell(i).Water);
                }

                Assert.Equal(0, h.World.UnitsAtCell(i).Heat); // all heat consumed or decayed
            }

            Assert.Equal(expectedColumns, steam.ColumnCount);
            Assert.Equal(expectedColumns + 1, steam.SourceWater); // impact cell consumed two water units
        }
    }
}
