using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Combat;
using Confluence.Sim.Content.Wizards.HalenTide;
using Confluence.Sim.Duel;
using Confluence.Sim.Fields;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;

namespace Confluence.Sim.Tests.Content.Halen
{
    /// <summary>AUG-439 Pool Lane and AUG-440 Pool Divide on SPL-076 Water Pool, cast through WIZ-010's grammar.</summary>
    public class PoolTransformTests
    {
        private static HalenDuel Placed(string augment, int rank, Vec3 at)
        {
            HalenDuel d = HalenTestKit.Duel(HalenTestKit.Halen(null, HalenTestKit.Aug(augment, rank)));
            return WaterPoolTests.Placed(at, d);
        }

        private static List<int> Cells(HalenDuel d, Footprint footprint)
        {
            var cells = new List<int>();
            d.Reactions.Grid.Rasterize(footprint, cells);
            return cells;
        }

        private static List<int> Charged(HalenDuel d)
        {
            var charged = new List<int>();
            for (int i = 0; i < d.Reactions.Grid.CellCount; i++)
            {
                if (d.Reactions.IsChargedAt(d.Reactions.Grid.CellCenter(i).ToXZ())) charged.Add(i);
            }

            return charged;
        }

        // ---- AUG-439 Pool Lane ---------------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 8f)]
        [InlineData(2, 10f)]
        [Verifies("AUG-439")]
        public void The_Pool_Becomes_One_Connected_Lane_Of_Water_2_For_6_Seconds(int rank, float length)
        {
            HalenDuel d = Placed(PoolLane.Id, rank, Vec3.Zero); // aimed along +Z from (0, 0, -6)
            OwnedEntity field = d.World.Owned.Entities.Single();  // one field allocation
            Assert.Equal(WaterPool.Id, field.DefinitionId);
            SpellEvent created = d.SpellEvents(SpellEventKind.FieldCreated).Single();
            Assert.Equal(Ticks.FromSeconds(6.0), created.Ticks);
            Footprint lane = Footprint.Rectangle(Vec2.Zero, new Vec2(1f, length * 0.5f), 0f);
            List<int> cells = Cells(d, lane);
            Assert.All(cells, c => Assert.Equal(2, d.Reactions.UnitsAtCell(c).Water)); // "Water 2 total"
            Assert.Equal(2 * cells.Count, d.Total(Material.Water, Footprint.Circle(Vec2.Zero, 8f)));
            Assert.Equal(2, d.Units(new Vec3(0f, 0f, length * 0.5f - 0.3f)).Water);
            Assert.Equal(0, d.Units(new Vec3(0f, 0f, length * 0.5f + 0.4f)).Water);

            // Drawback "Less lateral coverage": 1.5 m to the side is dry where the 3 m pool would have been wet.
            Assert.Equal(0, d.Units(new Vec3(1.6f, 0f, 0f)).Water);
            Assert.Equal(2, WaterPoolTests.Placed(Vec3.Zero).Units(new Vec3(1.6f, 0f, 0f)).Water);

            d.RunUntil(() => d.SpellEvents(SpellEventKind.FieldEnded).Any(), Ticks.FromSeconds(6.5));
            d.Step();
            Assert.Equal(0, d.Total(Material.Water, Footprint.Circle(Vec2.Zero, 8f)));
        }

        [Fact]
        [Verifies("AUG-439")]
        public void Conductivity_Follows_The_Exact_Lane()
        {
            HalenDuel d = Placed(PoolLane.Id, 2, Vec3.Zero);
            d.Reactions.Discharge(new ElectricDischarge { Point = new Vec3(0f, 0f, -4.75f), ReactionDamage = 10f, Owner = 1, SourceEventId = 0 });
            d.Step();
            List<int> charged = Charged(d);
            Footprint lane = Footprint.Rectangle(Vec2.Zero, new Vec2(1f, 5f), 0f);
            Assert.Equal(GlobalRules.World.WetChargeMaxCells, charged.Count);
            Assert.All(charged, c => Assert.True(lane.Contains(d.Reactions.Grid.CellCenter(c))));

            // Counterplay "Leave lane": a body 0.5 m beside the lane's edge is never charged.
            HalenDuel beside = Placed(PoolLane.Id, 1, Vec3.Zero);
            TestWorlds.Place(beside.World, 1, new Vec3(1.6f, 0f, -3.5f), 90f);
            beside.Reactions.Discharge(new ElectricDischarge { Point = new Vec3(0f, 0f, -3.75f), ReactionDamage = 10f, Owner = 0, SourceEventId = 0 });
            beside.Run(3);
            Assert.DoesNotContain(beside.DamageTo(1), e => e.Kind == DamageKind.Environment);
        }

        [Fact]
        [Verifies("AUG-439")]
        public void Heat_Consumes_The_Lane_Water_Into_Steam()
        {
            // Counterplay "consume water": heat on the lane makes steam and leaves the cell drier (spec L144).
            HalenDuel d = Placed(PoolLane.Id, 1, Vec3.Zero);
            d.Deposit(Material.Heat, 2, Footprint.Circle(new Vec2(0f, 2f), 0.6f));
            d.Step();
            Assert.Equal(VolumeKind.Steam, d.Reactions.Volumes.Single().Kind);
            Assert.Equal(0, d.Units(new Vec3(0f, 0f, 2f)).Water);
            Assert.Equal(2, d.Units(new Vec3(0f, 0f, -2f)).Water);
        }

        // ---- AUG-440 Pool Divide -------------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 1.5f)]
        [InlineData(2, 2f)]
        [Verifies("AUG-440")]
        public void Two_Water_1_Pools_Form_Side_By_Side_With_A_Dry_Gap(int rank, float radius)
        {
            HalenDuel d = Placed(PoolDivide.Id, rank, Vec3.Zero);
            List<OwnedEntity> pools = d.World.Owned.Entities.Where(e => e.DefinitionId == WaterPool.Id).ToList();
            Assert.Equal(2, pools.Count);                      // each pool is its own field allocation (spec L510)
            List<SpellEvent> created = d.SpellEvents(SpellEventKind.FieldCreated).ToList();
            Assert.Equal(2, created.Count);
            Assert.All(created, e => Assert.Equal(Ticks.FromSeconds(6.0), e.Ticks));
            Assert.All(created, e => Assert.Equal(radius, e.Amount, 3));
            float offset = radius + HalenRules.PoolDivideGap * 0.5f;
            var centers = created.Select(e => e.Position.XZ()).OrderBy(c => c.X).ToList();
            Assert.Equal(-offset, centers[0].X, 1);
            Assert.Equal(offset, centers[1].X, 1);
            float gap = Vec2.Distance(centers[0], centers[1]) - 2f * radius;
            Assert.InRange(gap, 2f, 4f);                       // "separated 2-4 m"

            foreach (Vec2 c in centers)
            {
                List<int> cells = Cells(d, Footprint.Circle(c, radius));
                Assert.All(cells, i => Assert.Equal(1, d.Reactions.UnitsAtCell(i).Water)); // "Water 1 each"
            }

            Assert.Equal(0, d.Units(Vec3.Zero).Water);         // the dry gap between them
            Assert.Equal(0, d.Units(new Vec3(offset - radius - 0.4f, 0f, 0f)).Water);
        }

        [Fact]
        [Verifies("AUG-440")]
        public void The_Pools_Are_Disconnected_So_A_Charge_Never_Crosses_The_Gap()
        {
            HalenDuel d = Placed(PoolDivide.Id, 2, Vec3.Zero);
            TestWorlds.Place(d.World, 1, new Vec3(3.5f, 0f, 0f), 270f); // in the right pool
            d.Reactions.Discharge(new ElectricDischarge { Point = new Vec3(-3.5f, 0f, 0f), ReactionDamage = 10f, Owner = 0, SourceEventId = 0 });
            d.Step();
            List<int> charged = Charged(d);
            Assert.NotEmpty(charged);
            Assert.All(charged, c => Assert.True(d.Reactions.Grid.CellCenter(c).X < 0f)); // drawback "cannot electrify between them"
            d.Run(3);
            Assert.DoesNotContain(d.DamageTo(1), e => e.Kind == DamageKind.Environment);

            // Counterplay "react separately": a second contact in the right pool charges it on its own.
            d.Reactions.Discharge(new ElectricDischarge { Point = new Vec3(3.5f, 0f, 0.5f), ReactionDamage = 10f, Owner = 0, SourceEventId = 0 });
            d.Run(2);
            Assert.Contains(d.DamageTo(1), e => e.Kind == DamageKind.Environment);
        }

        [Fact]
        [Verifies("AUG-440")]
        public void Standing_In_The_Dry_Gap_Keeps_A_Body_Dry()
        {
            HalenDuel d = Placed(PoolDivide.Id, 1, Vec3.Zero);
            TestWorlds.Place(d.World, 1, new Vec3(0f, 0f, 0f), 180f);
            d.Run(30);
            Assert.False(d.P1.Statuses.IsWet);
            TestWorlds.Place(d.World, 1, new Vec3(3f, 0f, 0f), 180f);
            d.Run(30);
            Assert.True(d.P1.Statuses.IsWet);
        }

        [Fact]
        [Verifies("AUG-440")]
        public void Two_Pools_Use_Two_Field_Allocations_And_The_Oldest_Field_Retires()
        {
            HalenDuel d = HalenTestKit.Duel(HalenTestKit.Halen(null, HalenTestKit.Aug(PoolDivide.Id, 1)));
            d.Signature();                                       // Undertow Mantle: one field
            d.RunUntil(() => d.SpellEvents(SpellEventKind.FieldCreated).Any(), 30);
            Assert.Single(d.World.Owned.Entities);
            d.RunUntil(() => SpellTestKit.CastEvents(d.World, CastEventKind.RecoveryEnded).Any(), 40); // the signature recovery blocks a new press
            d.AimAt(new Vec3(0f, 0f, 3f));
            d.Cast(WaterPoolTests.Slot);
            d.RunUntil(() => d.SpellEvents(SpellEventKind.FieldCreated).Count() >= 3, 40);
            Assert.Equal(3, d.World.Owned.Entities.Count(e => e.Owner == 0)); // eddy + two pools = the three-field limit
            Assert.Equal(2, d.World.Owned.Entities.Count(e => e.DefinitionId == WaterPool.Id));

            // A fourth field retires the oldest (the eddy) under the normal replacement policy; nothing extra is hidden.
            d.RunUntil(() => SpellTestKit.CastEvents(d.World, CastEventKind.RecoveryEnded).Count() >= 2, 40);
            Assert.Contains(d.World.Owned.Entities, e => e.DefinitionId == HalenTestKit.Wizard); // the 3 s eddy is still up
            d.AimAt(new Vec3(0f, 0f, -3f));
            d.Cast(RiptideLaneTests.Slot);
            d.RunUntil(() => d.SpellEvents(SpellEventKind.FieldCreated).Count() >= 4, 50);
            Assert.Equal(GlobalRules.FieldsAndSummons.MaxFieldsPerPlayer, d.World.Owned.Entities.Count(e => e.Owner == 0));
            Assert.DoesNotContain(d.World.Owned.Entities, e => e.DefinitionId == HalenTestKit.Wizard);
            Assert.Equal(2, d.World.Owned.Entities.Count(e => e.DefinitionId == WaterPool.Id));
            Assert.Single(d.World.Owned.Entities, e => e.DefinitionId == RiptideLane.Id);
        }

        // ---- The SPL-076-delivery exclusive group --------------------------------------------------------------

        [Fact]
        [Verifies("AUG-439", "AUG-440")]
        public void Lane_And_Divide_Share_The_Delivery_Group_And_Cannot_Coexist()
        {
            Catalogue cat = HalenTestKit.Catalogue;
            Assert.Equal("SPL-076-delivery", cat.Augment(PoolLane.Id).ExclusiveGroup);
            Assert.Equal("SPL-076-delivery", cat.Augment(PoolDivide.Id).ExclusiveGroup);
            Assert.ThrowsAny<InvalidOperationException>(() => HalenTestKit.Form(WaterPool.Id, HalenTestKit.Aug(PoolLane.Id), HalenTestKit.Aug(PoolDivide.Id)));
        }
    }
}
