using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions.Events;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;

namespace Confluence.Reactions.Tests
{
    /// <summary>
    /// Water and burning material (spec L152), heat and combustible growth (L153), water and sand (L154), and the
    /// material-flag gate (L163 "Heat on a painted stone wall does not make that wall combustible").
    /// </summary>
    public class BurnAndSandTests
    {
        private static Harness GrowthCell(out Vec3 p, float dps = 6f)
        {
            var h = new Harness();
            p = h.CellPoint(24, 24);
            MaterialDeposit growth = MaterialDeposit.Field(Material.Growth, Footprint.Point(p.XZ()), 0f, 8f, Harness.PlayerB, 1);
            growth.DeclaredDamagePerSecond = dps;
            h.World.Deposit(growth);
            h.Step();
            return h;
        }

        [Fact]
        public void Two_Heat_Ignite_Growth_For_Two_Seconds_Dealing_Its_Declared_Damage_Then_Consume_It()
        {
            Harness h = GrowthCell(out Vec3 p, dps: 6f);
            h.Bodies.Add(ReactionBody.Player(1, p));
            h.Point(Material.Heat, p, 2, 0, Harness.PlayerA, 2);
            StepResult r = h.Step();
            Assert.Equal(1, r.Count(ReactionKind.Ignition));
            Assert.True(h.World.IsBurningAt(p));
            Assert.Equal(SurfaceKind.Burning, h.World.SurfaceAt(p).Kind);
            Assert.Equal(0, h.World.UnitsAt(p).Heat); // the heat is consumed into the fire
            ContactDamage first = Assert.Single(r.Damage);
            Assert.Equal(DamageChannel.Thermal, first.Channel);
            Assert.Equal(ReactionKind.BurnContact, first.Reaction);
            Assert.Equal(Harness.PlayerA, first.Owner); // the igniting source owns the fire (L670)
            Assert.True(first.IsSecondary);

            var log = new StepLog();
            log.Add(r);
            log.Add(h.StepFor(Ticks.FromSeconds(GlobalRules.World.GrowthBurnSec) - 1));
            Assert.True(h.World.IsBurningAt(p));
            log.Add(h.Step()); // 2 s after ignition
            Assert.False(h.World.IsBurningAt(p));
            Assert.Equal(0, h.World.UnitsAt(p).Growth);
            Assert.Equal(1, log.Count(ReactionKind.BurnComplete));
            Assert.Equal(6f * GlobalRules.World.GrowthBurnSec, log.DamageTo(1)); // 6/s for 2 s, one hit per second
        }

        [Fact]
        public void One_Heat_Unit_Does_Not_Ignite_And_Map_Growth_Ignites_Once()
        {
            Harness h = GrowthCell(out Vec3 p);
            h.Point(Material.Heat, p, 1, 0, Harness.PlayerA, 2);
            Assert.Equal(0, h.Step().Count(ReactionKind.Ignition));
            Assert.Equal(1, h.World.UnitsAt(p).Growth);

            // BIO-002 root beds: Heat 1 and Growth 1 at start; one more heat unit ignites the finite fuel once.
            ArenaLayout layout = TestArenas.Empty(biomeId: "BIO-002");
            layout.Materials.Add(TestArenas.Bed(new Vec2(0f, 0f), 1f, 1f, heat: 1, growth: 1, flags: MaterialFlags.Growth));
            var orchard = new Harness(new ReactionWorld(layout));
            Vec3 bed = new Vec3(0.25f, 0f, 0.25f);
            Assert.Equal(0, orchard.Step().Count(ReactionKind.Ignition));
            orchard.Point(Material.Heat, bed, 1, 0, Harness.PlayerA, 3);
            Assert.Equal(1, orchard.Step().Count(ReactionKind.Ignition));
            orchard.StepSeconds(2f);
            Assert.Equal(0, orchard.World.UnitsAt(bed).Growth); // fuel exhausted
            orchard.Point(Material.Heat, bed, 2, 0, Harness.PlayerA, 4);
            Assert.Equal(0, orchard.Step().Count(ReactionKind.Ignition));
        }

        [Fact]
        public void One_Water_Unit_Removes_One_Heat_And_Suppresses_The_Burn_For_Two_Seconds()
        {
            Harness h = GrowthCell(out Vec3 p);
            h.Point(Material.Heat, p, 3, 60, Harness.PlayerA, 2); // two ignite, one stays on the burning cell
            h.Step();
            Assert.True(h.World.IsBurningAt(p));
            Assert.Equal(1, h.World.UnitsAt(p).Heat);

            h.Point(Material.Water, p, 1, 60, Harness.PlayerB, 3);
            StepResult r = h.Step();
            Assert.Equal(1, r.Count(ReactionKind.Extinguish));
            Assert.Equal(0, r.Count(ReactionKind.Steam)); // the water unit went into extinguishing
            Assert.False(h.World.IsBurningAt(p));
            Assert.Equal(0, h.World.UnitsAt(p).Heat);
            Assert.Equal(0, h.World.UnitsAt(p).Water);
            Assert.Equal(1, h.World.UnitsAt(p).Growth); // not burned away
            Assert.True(h.World.SurfaceAt(p).Has(SurfaceFlags.Extinguished));

            // Reapplied heat cannot ignite during the 2 s suppression ...
            h.Point(Material.Heat, p, 2, 0, Harness.PlayerA, 4);
            Assert.Equal(0, h.Step().Count(ReactionKind.Ignition));
            h.StepFor(Ticks.FromSeconds(GlobalRules.World.BurnSuppressionSec) - 3);
            h.Point(Material.Heat, p, 2, 0, Harness.PlayerA, 5);
            Assert.Equal(0, h.Step().Count(ReactionKind.Ignition));

            // ... and can afterwards.
            h.Point(Material.Heat, p, 2, 0, Harness.PlayerA, 6);
            Assert.Equal(1, h.Step().Count(ReactionKind.Ignition));
        }

        [Fact]
        public void Heat_Ignites_Only_Solids_Whose_Flags_Permit_It()
        {
            ArenaLayout layout = TestArenas.Empty();
            layout.Solids.Add(new Solid { Id = 1, Kind = SolidKind.Wall, Box = YawBox.OnFloor(new Vec3(-4f, 0f, 0f), new Vec3(2f, 3f, 0.5f), 0f), Material = "painted stone", Anchored = true });
            layout.Solids.Add(new Solid { Id = 2, Kind = SolidKind.Cover, Box = YawBox.OnFloor(new Vec3(4f, 0f, 0f), new Vec3(2f, 2f, 0.5f), 0f), Material = "hedge", Flags = MaterialFlags.Combustible, Durability = 60f });
            var h = new Harness(new ReactionWorld(layout));
            h.Bodies.Add(ReactionBody.Player(1, new Vec3(4f, 0f, 0.6f)));

            h.World.Impact(new DirectImpact { Point = new Vec3(-4f, 1f, 0.25f), SolidId = 1, HeatStrength = 4, BurnDamagePerSecond = 8f, Owner = Harness.PlayerA, SourceEventId = 1 });
            Assert.Equal(0, h.Step().Count(ReactionKind.SolidIgnited)); // painted stone never becomes combustible

            h.World.Impact(new DirectImpact { Point = new Vec3(4f, 1f, 0.25f), SolidId = 2, HeatStrength = 1, BurnDamagePerSecond = 8f, Owner = Harness.PlayerA, SourceEventId = 2 });
            Assert.Equal(0, h.Step().Count(ReactionKind.SolidIgnited)); // below two units

            h.World.Impact(new DirectImpact { Point = new Vec3(4f, 1f, 0.25f), SolidId = 2, HeatStrength = 2, BurnDamagePerSecond = 8f, Owner = Harness.PlayerA, SourceEventId = 3 });
            StepResult r = h.Step();
            Assert.Equal(1, r.Count(ReactionKind.SolidIgnited));
            var log = new StepLog();
            log.Add(r); // results are reused by the next step: record before stepping
            log.Add(h.StepSeconds(3f));
            Assert.Equal(8f * GlobalRules.World.GrowthBurnSec, log.DamageTo(1)); // burns 2 s

            // One finite fuel layer (BIO-013): it does not ignite again.
            h.World.Impact(new DirectImpact { Point = new Vec3(4f, 1f, 0.25f), SolidId = 2, HeatStrength = 4, BurnDamagePerSecond = 8f, Owner = Harness.PlayerA, SourceEventId = 4 });
            Assert.Equal(0, h.Step().Count(ReactionKind.SolidIgnited));
        }

        [Fact]
        public void Equal_Water_And_Sand_Make_Damp_Sand_That_Reduces_Dust_Production_For_Three_Seconds()
        {
            ArenaLayout layout = TestArenas.Empty();
            layout.Materials.Add(TestArenas.Bed(new Vec2(0f, 0f), 1f, 1f, sand: 2, flags: MaterialFlags.Sand));
            var h = new Harness(new ReactionWorld(layout));
            Vec3 p = new Vec3(0.25f, 0f, 0.25f);
            Vec3 q = new Vec3(-0.25f, 0f, -0.25f);
            h.Step();
            Assert.Equal(2, h.World.DustProductionAt(p));

            h.Point(Material.Water, p, 1, 600, Harness.PlayerA, 1);
            h.Point(Material.Water, q, 2, 600, Harness.PlayerA, 2);
            StepResult r = h.Step();
            Assert.Equal(2, r.Count(ReactionKind.DampFormed));
            Assert.Equal(1, h.World.DustProductionAt(p));
            Assert.Equal(0, h.World.DustProductionAt(q));
            Assert.Equal(0, h.World.UnitsAt(p).Water); // the water is bound into the damp sand
            Assert.Equal(2, h.World.UnitsAt(p).Sand);  // no sand is lost
            Assert.Equal(SurfaceKind.DampSand, h.World.SurfaceAt(p).Kind);

            h.StepFor(Ticks.FromSeconds(GlobalRules.World.DampSandSec) - 1);
            Assert.True(h.World.IsDampAt(q));
            StepResult dry = h.Step();
            Assert.False(h.World.IsDampAt(q));
            Assert.Equal(2, dry.Count(ReactionKind.DampExpired));
            Assert.Equal(2, h.World.DustProductionAt(q));
            Assert.Equal(SurfaceKind.Sand, h.World.SurfaceAt(q).Kind);
        }

        [Fact]
        public void Bounded_Heat_Dries_Damp_Sand_One_Unit_Per_Heat_Unit()
        {
            ArenaLayout layout = TestArenas.Empty();
            layout.Materials.Add(TestArenas.Bed(new Vec2(0f, 0f), 1f, 1f, sand: 2, flags: MaterialFlags.Sand));
            var h = new Harness(new ReactionWorld(layout));
            Vec3 p = new Vec3(0.25f, 0f, 0.25f);
            h.Point(Material.Water, p, 2, 600, Harness.PlayerA, 1);
            h.Step();
            Assert.Equal(0, h.World.DustProductionAt(p));

            h.Point(Material.Heat, p, 1, 0, Harness.PlayerB, 2);
            StepResult r = h.Step();
            Assert.Equal(1, r.Count(ReactionKind.DampDried));
            Assert.Empty(h.World.Volumes); // the bound water is already spent: no steam
            Assert.Equal(1, h.World.DustProductionAt(p));
            h.Point(Material.Heat, p, 3, 0, Harness.PlayerB, 3);
            h.Step();
            Assert.False(h.World.IsDampAt(p));
            Assert.Equal(2, h.World.DustProductionAt(p));
            Assert.Equal(2, h.World.Ledger.Consumed(Material.Heat)); // one per damp unit; the rest decays
        }

        [Fact]
        [Verifies("BIO-007")]
        public void Silt_Basin_Beds_Start_Damp_For_Three_Seconds_Then_Decay_Without_Refill()
        {
            ArenaLayout layout = TestArenas.Empty(biomeId: "BIO-007");
            layout.Materials.Add(TestArenas.Bed(new Vec2(-4f, 0f), 1f, 1f, water: 1, sand: 2, flags: MaterialFlags.Sand));
            layout.Materials.Add(TestArenas.Bed(new Vec2(4f, 0f), 1f, 1f, water: 1, sand: 2, flags: MaterialFlags.Sand));
            var h = new Harness(new ReactionWorld(layout));
            Vec3 bed = new Vec3(-3.75f, 0f, 0.25f);
            h.Step();
            Assert.True(h.World.IsDampAt(bed));
            Assert.Equal(1, h.World.DustProductionAt(bed));
            h.StepSeconds(3f);
            Assert.False(h.World.IsDampAt(bed));
            Assert.Equal(2, h.World.DustProductionAt(bed));
            h.StepSeconds(20f);
            Assert.Equal(0, h.World.UnitsAt(bed).Water); // no refill
            Assert.Empty(h.World.ScheduledSources);
        }

        [Fact]
        public void Raised_Dust_Comes_Only_From_Unsuppressed_Cells()
        {
            ArenaLayout layout = TestArenas.Empty(biomeId: "BIO-015");
            layout.Materials.Add(TestArenas.Bed(new Vec2(0f, 0f), 1f, 1f, dust: 2));
            var h = new Harness(new ReactionWorld(layout));
            h.Step();
            // Water 2 suppresses both layers of the west half of the bed.
            h.Field(Material.Water, Footprint.Rectangle(new Vec2(-0.5f, 0f), new Vec2(0.5f, 1f), 0f), 2, 8f);
            h.Step();
            h.World.RaiseDust(new DustRaise { Footprint = Footprint.Rectangle(Vec2.Zero, new Vec2(1f, 1f), 0f), LifetimeTicks = Ticks.FromSeconds(3f), Owner = Harness.PlayerA, SourceEventId = 9 });
            StepResult r = h.Step();
            Assert.Equal(1, r.Count(ReactionKind.DustRaised));
            Assert.Equal(8, h.OnlyVolume().ColumnCount); // the 4 x 2 east half only
            Assert.Equal(1, h.World.UnitsAt(new Vec3(0.25f, 0f, 0.25f)).Dust);
            Assert.Equal(2, h.World.UnitsAt(new Vec3(-0.25f, 0f, 0.25f)).Dust);
        }
    }
}
