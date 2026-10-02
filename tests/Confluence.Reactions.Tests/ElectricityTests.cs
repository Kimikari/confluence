using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions.Electricity;
using Confluence.Reactions.Events;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;

namespace Confluence.Reactions.Tests
{
    /// <summary>Electricity and wet cells (spec L148), metal (L149), grounded conductors (L150).</summary>
    public class ElectricityTests
    {
        private static void WetRow(Harness h, int x0, int x1, int z, int water = 1)
        {
            h.Field(Material.Water, Footprint.Lane(h.CellPoint(x0, z).XZ(), h.CellPoint(x1, z).XZ(), 0.5f), water, 8f, Harness.PlayerB, 1);
        }

        private static ElectricDischarge Bolt(Vec3 at, float damage = 12f, int source = 50) =>
            new ElectricDischarge { Point = at, ReactionDamage = damage, Owner = Harness.PlayerA, SourceEventId = source };

        private static int ChargedCells(Harness h)
        {
            int n = 0;
            for (int i = 0; i < h.Grid.CellCount; i++)
            {
                Vec2 c = h.Grid.CellCenter(i);
                if (h.World.IsChargedAt(new Vec3(c.X, 0f, c.Y))) n++;
            }

            return n;
        }

        [Fact]
        public void A_Long_Wet_Lane_Charges_At_Most_Twelve_Cells_Within_Six_Meters()
        {
            var h = new Harness();
            WetRow(h, 4, 40, 20); // 18.5 m of connected water
            h.Step();
            Vec3 contact = h.CellPoint(4, 20);
            h.World.Discharge(Bolt(contact));
            StepResult r = h.Step();

            Assert.Equal(1, r.Count(ReactionKind.WetCharge));
            Assert.Equal(GlobalRules.World.WetChargeMaxCells, ChargedCells(h));
            for (int x = 4; x <= 40; x++)
            {
                Assert.Equal(x < 4 + GlobalRules.World.WetChargeMaxCells, h.World.IsChargedAt(h.CellPoint(x, 20)));
            }
        }

        [Fact]
        public void A_Wide_Contact_Never_Charges_Cells_Beyond_Six_Meters()
        {
            var h = new Harness();
            Vec3 origin = h.CellPoint(4, 20);
            WetRow(h, 4, 40, 20);
            h.Step();
            // A ground rail of lightning (SPL-063 style) touching the whole lane, released at its near end.
            ElectricDischarge rail = Bolt(origin);
            rail.Contact = Footprint.Segment(origin.XZ(), h.CellPoint(40, 20).XZ(), 0.5f);
            h.World.Discharge(rail);
            h.Step();

            int charged = 0;
            for (int i = 0; i < h.Grid.CellCount; i++)
            {
                Vec2 c = h.Grid.CellCenter(i);
                if (!h.World.IsChargedAt(new Vec3(c.X, 0f, c.Y))) continue;
                charged++;
                Assert.True(Vec2.Distance(c, origin.XZ()) <= GlobalRules.World.WetChargeMaxPropagation);
            }

            Assert.Equal(GlobalRules.World.WetChargeMaxCells, charged);

            // A wide pool: the patch is still 12 cells and inside 6 m.
            var h2 = new Harness();
            Vec3 center = h2.CellPoint(24, 24);
            h2.Field(Material.Water, Footprint.Circle(center.XZ(), 8f), 1, 8f, Harness.PlayerB, 1);
            h2.Step();
            h2.World.Discharge(Bolt(center));
            h2.Step();
            Assert.Equal(GlobalRules.World.WetChargeMaxCells, ChargedCells(h2));
        }

        [Fact]
        public void Disconnected_Pools_Dry_Gaps_Corners_And_Ice_Do_Not_Conduct()
        {
            var h = new Harness();
            WetRow(h, 10, 13, 20);
            WetRow(h, 15, 18, 20);                                                  // dry cell x = 14 between
            h.Field(Material.Water, Footprint.Point(h.CellPoint(14, 21).XZ()), 1, 8f, Harness.PlayerB, 1); // corner-only
            h.Step();
            h.World.Discharge(Bolt(h.CellPoint(10, 20)));
            h.Step();
            Assert.Equal(4, ChargedCells(h));
            Assert.False(h.World.IsChargedAt(h.CellPoint(15, 20)));
            Assert.False(h.World.IsChargedAt(h.CellPoint(14, 21)));

            // Ice: freezing the middle cell of a lane breaks connectivity.
            var h2 = new Harness();
            WetRow(h2, 10, 16, 20);
            h2.Step();
            h2.Point(Material.Frost, h2.CellPoint(13, 20), 2, 0);
            h2.Step();
            h2.World.Discharge(Bolt(h2.CellPoint(10, 20)));
            h2.Step();
            Assert.Equal(3, ChargedCells(h2));

            // A bolt on dry ground charges nothing.
            h2.World.Discharge(Bolt(h2.CellPoint(30, 30), source: 51));
            Assert.Equal(0, h2.Step().Count(ReactionKind.WetCharge));
        }

        [Fact]
        public void The_Charge_Lasts_Six_Tenths_Of_A_Second_And_Damages_Each_Body_On_First_Contact()
        {
            var h = new Harness();
            WetRow(h, 10, 16, 20);
            h.Step();
            h.Bodies.Add(ReactionBody.Player(1, h.CellPoint(12, 20)));                    // standing in the patch
            h.Bodies.Add(ReactionBody.Player(2, h.CellPoint(14, 20) + new Vec3(0f, 0.8f, 0f), false)); // jumping
            h.Bodies.Add(ReactionBody.Player(3, h.CellPoint(30, 20)));                    // far away
            h.World.Discharge(Bolt(h.CellPoint(10, 20), 12f));
            StepResult r = h.Step();

            ContactDamage hit = Assert.Single(r.Damage);
            Assert.Equal(1, hit.BodyId);
            Assert.Equal(12f, hit.Amount);
            Assert.Equal(DamageChannel.Electrical, hit.Channel);
            Assert.Equal(ReactionKind.WetCharge, hit.Reaction);
            Assert.True(hit.IsSecondary);

            // Body 2 lands while the patch is still charged: its first contact deals the damage once.
            h.Bodies[1] = ReactionBody.Player(2, h.CellPoint(14, 20));
            StepLog log = h.StepFor(Ticks.FromSeconds(GlobalRules.World.WetChargeDurationSec) - 1);
            Assert.Equal(12f, log.DamageTo(2));
            Assert.Equal(0f, log.DamageTo(1)); // no repeat for the body already hit
            Assert.True(h.World.IsChargedAt(h.CellPoint(12, 20)));

            h.Step(); // 0.6 s after the discharge
            Assert.False(h.World.IsChargedAt(h.CellPoint(12, 20)));
            h.Bodies.Add(ReactionBody.Player(4, h.CellPoint(15, 20)));
            Assert.Empty(h.StepFor(5).Damage);
        }

        [Fact]
        public void Metal_Glows_Then_Releases_Along_At_Most_Three_Branches_Of_Six_Meters()
        {
            var h = new Harness();
            var def = new ConductorDef { Id = 7 };
            def.Branches.Add(new Segment(new Vec3(0f, 1f, 0f), new Vec3(8f, 1f, 0f)));   // 8 m: only 6 m from the contact energize
            def.Branches.Add(new Segment(new Vec3(0f, 1f, 0f), new Vec3(0f, 1f, 4f)));
            def.Branches.Add(new Segment(new Vec3(0f, 1f, 0f), new Vec3(-4f, 1f, 0f)));
            def.Branches.Add(new Segment(new Vec3(0f, 1f, 0f), new Vec3(0f, 1f, -4f)));  // a fourth branch never conducts
            h.World.RegisterConductor(def);

            h.Bodies.Add(ReactionBody.Player(1, new Vec3(5f, 0f, 0.5f)));   // 5 m along branch 1
            h.Bodies.Add(ReactionBody.Player(2, new Vec3(7.5f, 0f, 0.5f))); // beyond 6 m from the contact
            h.Bodies.Add(ReactionBody.Player(3, new Vec3(0.5f, 0f, 3.5f))); // branch 2
            h.Bodies.Add(ReactionBody.Player(4, new Vec3(0.5f, 0f, -3.5f))); // branch 4 (not conducting)

            h.World.Discharge(Bolt(new Vec3(0f, 1f, 0f), 15f));
            StepResult first = h.Step();
            Assert.Equal(1, first.Count(ReactionKind.ConductorGlow));
            Assert.Empty(first.Damage);

            int glow = Ticks.FromSeconds(ReactionRules.ConductorGlowSec);
            StepLog log = h.StepFor(glow - 1);
            Assert.Empty(log.Damage); // still glowing
            StepResult release = h.Step();
            Assert.Equal(1, release.Count(ReactionKind.ConductorRelease));
            var hit = new List<int>();
            foreach (ContactDamage d in release.Damage)
            {
                Assert.Equal(15f, d.Amount);
                Assert.Equal(DamageChannel.Electrical, d.Channel);
                Assert.True(d.IsSecondary);
                hit.Add(d.BodyId);
            }

            Assert.Equal(new List<int> { 1, 3 }, hit);
            Assert.Empty(h.StepFor(30).Damage); // released once
        }

        [Fact]
        public void A_Grounded_Conductor_Absorbs_A_Wet_Branch_Then_Overloads_And_Resets()
        {
            var h = new Harness();
            WetRow(h, 10, 16, 20);
            h.Step();
            Vec3 statue = h.CellPoint(17, 20);
            h.World.RegisterConductor(ConductorDef.ForBox(9, YawBox.OnFloor(statue, new Vec3(0.5f, 2f, 0.5f), 0f), grounded: true));
            Assert.True(h.World.IsConductorGrounded(9));

            h.World.Discharge(Bolt(h.CellPoint(10, 20)));
            StepResult r = h.Step();
            Assert.Equal(1, r.Count(ReactionKind.GroundRedirect));
            Assert.Equal(1, r.Count(ReactionKind.GroundOverload));
            Assert.Equal(0, r.Count(ReactionKind.WetCharge));
            Assert.Equal(0, ChargedCells(h));
            Assert.True(h.World.IsConductorOverloaded(9));

            // Overloaded: the next branch charges the patch normally.
            h.World.Discharge(Bolt(h.CellPoint(10, 20), source: 51));
            Assert.Equal(1, h.Step().Count(ReactionKind.WetCharge));

            StepLog log = h.StepSeconds(ReactionRules.GroundOverloadSec);
            Assert.Equal(1, log.Count(ReactionKind.GroundReset));
            Assert.True(h.World.IsConductorGrounded(9));
        }

        [Fact]
        public void A_Stated_Ground_Capacity_Absorbs_That_Many_Branches()
        {
            var h = new Harness();
            var def = ConductorDef.ForBox(9, YawBox.OnFloor(new Vec3(0f, 0f, 0f), new Vec3(1f, 2f, 1f), 0f), grounded: true);
            def.GroundCapacity = 2;
            h.World.RegisterConductor(def);
            for (int i = 0; i < 2; i++)
            {
                h.World.Discharge(new ElectricDischarge { Point = new Vec3(0f, 1f, 0f), ConductorId = 9, ReactionDamage = 10f, SourceEventId = 60 + i });
                StepResult r = h.Step();
                Assert.Equal(1, r.Count(ReactionKind.GroundRedirect));
                Assert.Equal(i == 1 ? 1 : 0, r.Count(ReactionKind.GroundOverload));
            }

            h.World.Discharge(new ElectricDischarge { Point = new Vec3(0f, 1f, 0f), ConductorId = 9, ReactionDamage = 10f, SourceEventId = 70 });
            Assert.Equal(1, h.Step().Count(ReactionKind.ConductorGlow)); // overloaded: conducts instead
        }
    }
}
