using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions.Events;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;

namespace Confluence.Reactions.Tests
{
    /// <summary>Surface queries for movement and footsteps (spec L175) and contact damage requests.</summary>
    public class ContactAndSurfaceTests
    {
        [Fact]
        public void Surface_Kinds_Follow_The_Cell_State()
        {
            var h = new Harness();
            Vec3 dry = h.CellPoint(5, 5);
            Vec3 wet = h.CellPoint(10, 10);
            Vec3 sticky = h.CellPoint(15, 15);
            Vec3 charged = h.CellPoint(20, 20);
            h.Point(Material.Water, wet, 1, 600);
            MaterialDeposit vines = MaterialDeposit.Field(Material.Growth, Footprint.Point(sticky.XZ()), 0f, 8f, Harness.PlayerA, 2);
            vines.Sticky = true;
            h.World.Deposit(vines);
            h.Point(Material.Water, charged, 1, 600, Harness.PlayerA, 3);
            h.Step();
            h.World.Discharge(new ElectricDischarge { Point = charged, ReactionDamage = 5f, SourceEventId = 4 });
            h.Step();

            Assert.Equal(SurfaceKind.Normal, h.World.SurfaceAt(dry).Kind);
            Assert.Equal(SurfaceKind.WetStone, h.World.SurfaceAt(wet).Kind);
            Assert.True(h.World.IsWetAt(wet));
            Assert.Equal(SurfaceKind.StickyGrowth, h.World.SurfaceAt(sticky).Kind);
            SurfaceInfo info = h.World.SurfaceAt(charged);
            Assert.Equal(SurfaceKind.Charged, info.Kind);
            Assert.True(info.Has(SurfaceFlags.Wet));
            Assert.Equal(1, info.Units.Water);
            Assert.Equal(-1, h.World.SurfaceAt(new Vec3(50f, 0f, 50f)).CellIndex);
        }

        [Fact]
        public void A_Red_Hot_Surface_Burns_Touching_Bodies_Once_Per_Second_For_Its_Lifetime()
        {
            var h = new Harness();
            var box = YawBox.OnFloor(new Vec3(0f, 0f, 0f), new Vec3(1f, 1f, 1f), 0f);
            h.Bodies.Add(ReactionBody.Player(1, new Vec3(0.85f, 0f, 0f)));  // leaning on it
            h.Bodies.Add(ReactionBody.Player(2, new Vec3(3f, 0f, 0f)));     // clear of it
            h.World.AddHotSurface(new HotSurface { Box = box, DamagePerSecond = 8f, LifetimeTicks = Ticks.FromSeconds(4f), Owner = Harness.PlayerA, SourceEventId = 7 });
            StepLog log = h.StepSeconds(6f);
            Assert.Equal(8f * 4f, log.DamageTo(1));
            Assert.Equal(0f, log.DamageTo(2));
            Assert.All(log.Damage, d =>
            {
                Assert.Equal(DamageChannel.Thermal, d.Channel);
                Assert.Equal(ReactionKind.HotSurface, d.Reaction);
                Assert.True(d.IsSecondary);
                Assert.Equal(Harness.PlayerA, d.Owner);
            });
            Assert.Equal(0, h.World.HazardCount);
        }

        [Fact]
        public void Reactions_Hurt_Their_Owner_Too()
        {
            // L670: the owner receives no safety exemption.
            var h = new Harness();
            Vec3 p = h.CellPoint(24, 24);
            h.Point(Material.Water, p, 1, 600, Harness.PlayerA, 1);
            h.Step();
            h.Bodies.Add(ReactionBody.Player(Harness.PlayerA, p));
            h.World.Discharge(new ElectricDischarge { Point = p, ReactionDamage = 9f, Owner = Harness.PlayerA, SourceEventId = 2 });
            ContactDamage hit = Assert.Single(h.Step().Damage);
            Assert.Equal(Harness.PlayerA, hit.BodyId);
            Assert.Equal(Harness.PlayerA, hit.Owner);
        }
    }
}
