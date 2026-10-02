using System.Collections.Generic;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions.Electricity;
using Confluence.Reactions.Environment;
using Confluence.Reactions.Events;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Volumes;

namespace Confluence.Reactions.Tests
{
    /// <summary>Arena initialization (spec L664, L668) and biome environment hooks (L138, L656).</summary>
    public class EnvironmentTests
    {
        private static ArenaLayout Raincourt()
        {
            ArenaLayout layout = TestArenas.Empty(biomeId: EnvironmentRules.RaincourtId);
            // Two mirrored 2 m gutter beds (0.5 m wide) starting with one water unit.
            layout.Materials.Add(TestArenas.Bed(new Vec2(-6.25f, 0f), 0.25f, 1f, water: 1, refillRule: "BIO-001 rain"));
            layout.Materials.Add(TestArenas.Bed(new Vec2(6.25f, 0f), 0.25f, 1f, water: 1, refillRule: "BIO-001 rain"));
            // A decorative pool without the refill rule is not a gutter bed.
            layout.Materials.Add(TestArenas.Bed(new Vec2(0f, 6f), 1f, 1f, water: 2));
            return layout;
        }

        [Fact]
        public void The_Catalogue_Raincourt_Rule_Matches_The_Encoded_Numbers()
        {
            Catalogue catalogue = Catalogue.Load(Catalogue.FindDefaultPath());
            BiomeDef raincourt = catalogue.BiomesById[EnvironmentRules.RaincourtId];
            Assert.Contains("Every 8 s a 0.5 s visible rainfall pulse adds one water unit", raincourt.EnvironmentRule);
            Assert.Contains("up to four", raincourt.EnvironmentRule);
        }

        [Fact]
        [Verifies("BIO-001")]
        public void Raincourt_Rain_Adds_One_Water_Unit_To_Each_Gutter_Bed_Every_Eight_Seconds_Up_To_Four()
        {
            const int combatStart = 300; // 5 s spawn alcove before combat time 0
            var world = new ReactionWorld(Raincourt(), combatStart);
            Assert.Equal(2, world.ScheduledSources.Count);
            Vec3 gutter = new Vec3(-6.25f, 0f, 0.25f);
            Vec3 pool = new Vec3(0.25f, 0f, 6.25f);
            Assert.Equal(1, world.UnitsAt(gutter).Water);
            Assert.Equal(4, world.ScheduledSources[0].Cells.Count);

            var bodies = new List<ReactionBody> { ReactionBody.Player(1, new Vec3(-6f, 0f, 0f)) };
            int interval = Ticks.FromSeconds(EnvironmentRules.RaincourtPulseIntervalSec);
            int pulses = 0;
            for (int tick = combatStart; tick <= combatStart + 5 * interval; tick++)
            {
                StepResult r = world.Step(tick, bodies);
                Assert.Empty(r.Damage); // rain never wets or harms players by itself
                int fired = r.Count(ReactionKind.RefillPulse);
                if (fired > 0)
                {
                    Assert.Equal(0, (tick - combatStart) % interval);
                    Assert.Equal(2, fired); // both mirrored beds
                    pulses++;
                    int expected = System.Math.Min(EnvironmentRules.RaincourtMaxUnits, 1 + pulses);
                    Assert.Equal(expected, world.UnitsAt(gutter).Water);
                    Assert.Equal(expected, world.UnitsAt(new Vec3(6.25f, 0f, 0.25f)).Water);
                }
            }

            Assert.Equal(5, pulses);
            Assert.Equal(4, world.UnitsAt(gutter).Water);  // never above four
            Assert.Equal(2, world.UnitsAt(pool).Water);    // other water is not rained on
            Assert.Equal(2 * 4 * 3, world.Ledger.Refilled(Material.Water)); // three effective pulses for 4 cells x 2 beds
            Assert.True(world.ScheduledSources[0].VisibleTicks == Ticks.FromSeconds(EnvironmentRules.RaincourtPulseVisibleSec));
        }

        [Fact]
        public void Rain_Refills_A_Bed_That_Spells_Consumed()
        {
            var world = new ReactionWorld(Raincourt());
            Vec3 gutter = new Vec3(-6.25f, 0f, 0.25f);
            world.Deposit(MaterialDeposit.QuickProjectile(Material.Heat, gutter, Harness.PlayerA, 1));
            world.Step(0, null);
            Assert.Equal(0, world.UnitsAt(gutter).Water);
            for (int tick = 1; tick <= Ticks.FromSeconds(8f); tick++)
            {
                world.Step(tick, null);
            }

            Assert.Equal(1, world.UnitsAt(gutter).Water);
        }

        [Fact]
        public void A_Generic_Scheduled_Source_Pulses_Its_Cells_On_Its_Own_Interval()
        {
            var h = new Harness();
            var source = new ScheduledSource { RuleId = "test", Material = Material.Sand, UnitsPerPulse = 2, MaxUnitsPerCell = 3, FirstPulseTick = 10, IntervalTicks = 20 };
            source.Cells.Add(h.Index(h.CellPoint(5, 5)));
            h.World.AddScheduledSource(source);
            StepLog log = h.StepFor(51); // ticks 0..50: pulses at 10, 30, 50
            Assert.Equal(3, log.Count(ReactionKind.RefillPulse));
            Assert.Equal(3, h.World.UnitsAt(h.CellPoint(5, 5)).Sand);
            Assert.Equal(3, h.World.NaturalUnitsAtCell(h.Index(h.CellPoint(5, 5)), Material.Sand)); // announced map material
        }

        [Fact]
        [Verifies("BIO-004")]
        public void Rime_Foundry_Tiles_Begin_Frozen_Thaw_Normally_And_Never_Refreeze()
        {
            ArenaLayout layout = TestArenas.Empty(biomeId: "BIO-004");
            layout.Materials.Add(TestArenas.Bed(new Vec2(-4f, 0f), 0.25f, 0.25f, water: 1, flags: MaterialFlags.Ice | MaterialFlags.Water));
            layout.Materials.Add(TestArenas.Bed(new Vec2(4f, 0f), 0.25f, 0.25f, water: 1, flags: MaterialFlags.Ice | MaterialFlags.Water));
            var h = new Harness(new ReactionWorld(layout));
            Vec3 tile = new Vec3(-3.75f, 0f, 0.25f);
            Assert.True(h.World.HasIceAt(tile));
            Assert.Equal(GlobalRules.World.IceTileDurability, h.World.IceDurabilityAt(tile));

            h.StepSeconds(30f); // map tiles do not expire by time
            Assert.True(h.World.HasIceAt(tile));

            h.Point(Material.Heat, tile, 2, 0, Harness.PlayerA, 1);
            Assert.Equal(1, h.Step().Count(ReactionKind.IceMelted));
            Assert.Equal(1, h.World.UnitsAt(tile).Water); // thawed into its water unit
            Assert.Equal(1, h.World.NaturalUnitsAtCell(h.Index(tile), Material.Water));
            h.StepSeconds(30f);
            Assert.False(h.World.HasIceAt(tile)); // no automatic refreeze
            Assert.True(h.World.IsWetAt(tile));
        }

        [Fact]
        [Verifies("BIO-011")]
        public void Windmill_Yard_Gusts_Move_Clouds_Up_To_Two_Meters_At_Strength_One()
        {
            ArenaLayout layout = TestArenas.Empty(biomeId: EnvironmentRules.WindmillYardId);
            layout.Gusts.Add(new GustEvent { TimeSec = 12f, DurationSec = 2f, Direction = Vec2.UnitX, Strength = 1 });
            var h = new Harness(new ReactionWorld(layout));
            h.World.CreateVolume(new VolumeSpec { Kind = VolumeKind.Steam, Footprint = Footprint.Circle(Vec2.Zero, 1f), LifetimeTicks = Ticks.FromSeconds(8f) });
            h.StepSeconds(11f);
            h.World.CreateVolume(new VolumeSpec { Kind = VolumeKind.Dust, Footprint = Footprint.Circle(new Vec2(-5f, 0f), 1f), LifetimeTicks = Ticks.FromSeconds(8f), SourceEventId = 1 });
            StepLog log = h.StepSeconds(4f);
            Assert.Equal(1, log.Count(ReactionKind.GustStarted));
            Assert.Equal(0, log.Count(ReactionKind.WindDisperse)); // strength 1 never disperses
            ObscurationVolume dust = h.OnlyVolume();
            Assert.Equal(2f, dust.Offset.X, 3);

            // The same strength-1 gust in another biome does not move clouds (D-023).
            ArenaLayout plain = TestArenas.Empty();
            plain.Gusts.Add(new GustEvent { TimeSec = 0f, DurationSec = 2f, Direction = Vec2.UnitX, Strength = 1 });
            var p = new Harness(new ReactionWorld(plain));
            p.World.CreateVolume(new VolumeSpec { Kind = VolumeKind.Dust, Footprint = Footprint.Circle(Vec2.Zero, 1f), LifetimeTicks = Ticks.FromSeconds(3f) });
            p.StepSeconds(2f);
            Assert.Equal(Vec2.Zero, p.OnlyVolume().Offset);
        }

        [Fact]
        public void Arena_Solids_Register_Conductors_Grounds_And_Prisms_And_Light_Shafts_Load()
        {
            ArenaLayout layout = TestArenas.Empty(biomeId: "BIO-012");
            layout.Solids.Add(new Solid { Id = 3, Kind = SolidKind.Pillar, Box = YawBox.OnFloor(new Vec3(3f, 0f, 0f), new Vec3(1f, 2f, 1f), 0f), Flags = MaterialFlags.Metal | MaterialFlags.Grounded, Anchored = true });
            layout.Solids.Add(new Solid { Id = 4, Kind = SolidKind.Cover, Box = YawBox.OnFloor(new Vec3(-3f, 0f, 0f), new Vec3(1f, 2f, 1f), 0f), Flags = MaterialFlags.Metal | MaterialFlags.Insulating });
            layout.Materials.Add(TestArenas.Bed(new Vec2(0f, 5f), 1f, 0.25f, flags: MaterialFlags.Metal));
            layout.LightShafts.Add(new LightShaft { Position = new Vec3(0f, 0f, -5f), Radius = 1f });
            var world = new ReactionWorld(layout);

            Assert.True(world.HasConductor(ReactionWorld.SolidConductorIdBase + 3));
            Assert.True(world.IsConductorGrounded(ReactionWorld.SolidConductorIdBase + 3));
            Assert.False(world.HasConductor(ReactionWorld.SolidConductorIdBase + 4)); // insulated metal does not conduct
            Assert.True(world.HasConductor(ReactionWorld.PatchConductorIdBase + 0));
            Assert.Single(world.LightShafts);
            Assert.True(world.SurfaceAt(new Vec3(0.25f, 0f, 5f)).Has(SurfaceFlags.Conductive));

            // BIO-012: the statue absorbs one branch, then loses its grounding for 4 s.
            world.Discharge(new ElectricDischarge { Point = new Vec3(3f, 1f, 0f), ReactionDamage = 10f, SourceEventId = 1 });
            StepResult r = world.Step(0, null);
            Assert.Equal(1, r.Count(ReactionKind.GroundRedirect));
            Assert.True(world.IsConductorOverloaded(ReactionWorld.SolidConductorIdBase + 3));
            world.RemoveSolid(3);
            Assert.False(world.HasConductor(ReactionWorld.SolidConductorIdBase + 3));
        }
    }
}
