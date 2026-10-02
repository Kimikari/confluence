using System.Linq;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Time;
using Confluence.Reactions;
using Confluence.Reactions.Events;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Duel;
using Confluence.Sim.Props;
using Confluence.Sim.Status;
using Confluence.Sim.World.Materials;
using Confluence.Sim.World.Materials.Biomes;

namespace Confluence.Sim.Tests.World.Materials
{
    /// <summary>Biome environment rules running end to end in a duel (spec L138, L656, L664, L668).</summary>
    public class BiomeRuleTests
    {
        private const int Alcove = 300; // the 5 s spawn alcove (spec L46)

        private static ArenaLayout Raincourt()
        {
            ArenaLayout layout = TestWorlds.Flat();
            layout.BiomeId = "BIO-001";
            // Two mirrored 2 m gutter beds (0.5 m wide) starting with one water unit, as the generator places them.
            layout.Materials.Add(WorldDuels.WaterBed(new Vec2(-6.25f, 0f), 0.25f, 1f, water: 1, refillRule: "BIO-001 rain"));
            layout.Materials.Add(WorldDuels.WaterBed(new Vec2(6.25f, 0f), 0.25f, 1f, water: 1, refillRule: "BIO-001 rain"));
            return layout;
        }

        [Fact]
        public void The_Shipped_Behaviors_Claim_Exactly_The_Implemented_Biomes()
        {
            Assert.Equal(typeof(RaincourtRainRule), BehaviorType("BIO-001"));
            Assert.Equal(typeof(WindmillYardGustRule), BehaviorType("BIO-011"));
            Assert.True(Confluence.Sim.Effects.ContentBehaviorIndex.Default.TryGetBiomeRule("BIO-001", out _));
            Assert.False(Confluence.Sim.Effects.ContentBehaviorIndex.Default.TryGetBiomeRule("BIO-002", out _));
        }

        private static System.Type BehaviorType(string id)
        {
            return typeof(RaincourtRainRule).Assembly.GetTypes()
                .Single(t => t.GetCustomAttributes(typeof(ContentBehaviorAttribute), false).Cast<ContentBehaviorAttribute>().Any(a => a.Id == id));
        }

        [Fact]
        [Verifies("BIO-001")]
        public void Raincourt_Rain_Refills_The_Gutter_Beds_Every_Eight_Seconds_Up_To_Four_And_Never_Wets_Players_Itself()
        {
            BiomeDef record = WorldDuels.Catalogue.BiomesById["BIO-001"];
            Assert.Contains("Every 8 s a 0.5 s visible rainfall pulse adds one water unit", record.EnvironmentRule);
            Assert.Contains("No rain wets players automatically", record.EnvironmentRule);

            DuelWorld world = WorldDuels.Duel(Raincourt(), alcoveTicks: Alcove);
            ReactionMaterials materials = WorldDuels.Materials(world);
            Assert.Equal("BIO-001", materials.InstalledBiomeRule);
            Assert.Equal(2, materials.World.ScheduledSources.Count);

            WorldDuels.Idle(world, Alcove + 1); // combat starts
            TestWorlds.Place(world, 0, new Vec3(-6.25f, 0f, 0.25f), 0f); // standing in a gutter bed
            Vec3 gutter = new Vec3(-6.25f, 0f, -0.25f);
            Vec3 otherGutter = new Vec3(6.25f, 0f, 0.75f);
            int pulses = 0;
            for (int t = 1; t <= Ticks.FromSeconds(41f); t++)
            {
                world.Step(TestWorlds.A, TestWorlds.B);
                int fired = materials.LastStep.Count(ReactionKind.RefillPulse);
                if (fired > 0)
                {
                    pulses++;
                    Assert.Equal(0, world.CombatTick % Ticks.FromSeconds(8f)); // pulses at combat time 8 s, 16 s, ...
                    Assert.Equal(System.Math.Min(4, 1 + pulses), materials.World.UnitsAt(gutter).Water);
                    Assert.Equal(System.Math.Min(4, 1 + pulses), materials.World.UnitsAt(otherGutter).Water);
                }

                Assert.True(world.Player(0).Statuses.Has(StatusKind.Wet)); // wet from the gutter cell it stands in
                Assert.False(world.Player(1).Statuses.Has(StatusKind.Wet)); // the rain never wets the dry player
            }

            Assert.Equal(5, pulses);
            Assert.Equal(4, materials.World.UnitsAt(gutter).Water); // up to four (the L136 cell maximum)
            Assert.Empty(WorldDuels.Events<DamageEvent>(world));
            Assert.All(WorldDuels.Events<StatusEvent>(world).Where(s => s.Kind == StatusKind.Wet),
                s => Assert.Equal(MaterialRules.WetCellSource, s.DefinitionId));
        }

        private static ArenaLayout Windyard(string biomeId)
        {
            ArenaLayout layout = TestWorlds.Flat();
            layout.BiomeId = biomeId;
            layout.Gusts.Add(new GustEvent { TimeSec = 2f, DurationSec = 2f, Direction = new Vec2(1f, 0f), Strength = 1 });
            layout.Props.Add(WorldDuels.Prop(1, "PROP-013", new Vec3(-3f, 0f, 2f), new Vec3(0.6f, 0.6f, 0.6f)));
            return layout;
        }

        [Fact]
        [Verifies("BIO-011")]
        public void Windmill_Yard_Gusts_Move_Clouds_Up_To_Two_Meters_But_Never_Players_Or_Props()
        {
            BiomeDef record = WorldDuels.Catalogue.BiomesById["BIO-011"];
            Assert.Contains("gust moves clouds up to 2 m", record.EnvironmentRule);
            Assert.Contains("force strength 1", record.EnvironmentRule);
            Assert.Contains("does not move players or damaging props", record.Fairness);

            float moved = CloudDriftAfterGust("BIO-011", out DuelWorld world, out Vec3 p0, out Vec3 p1, out Vec3 prop);
            Assert.Equal("BIO-011", WorldDuels.Materials(world).InstalledBiomeRule);
            Assert.Equal(2f, moved, 2);
            Assert.Equal(p0, world.Player(0).Position);
            Assert.Equal(p1, world.Player(1).Position);
            Assert.Equal(prop, world.Props.Find(1).Position);
            Assert.Empty(WorldDuels.Events<PropEvent>(world));

            // The same strength-1 schedule outside Windmill Yard follows the global rule: strength 1 moves no cloud (L151).
            float generic = CloudDriftAfterGust("BIO-002", out DuelWorld other, out _, out _, out _);
            Assert.Null(WorldDuels.Materials(other).InstalledBiomeRule);
            Assert.Equal(0f, generic, 3);
        }

        private static float CloudDriftAfterGust(string biomeId, out DuelWorld world, out Vec3 p0, out Vec3 p1, out Vec3 prop)
        {
            world = WorldDuels.Duel(Windyard(biomeId));
            ReactionMaterials materials = WorldDuels.Materials(world);
            WorldDuels.Idle(world, 30);
            materials.CreateVolume(new VolumeSpec
            {
                Kind = VolumeKind.Steam,
                Footprint = Footprint.Rectangle(new Vec2(0f, 0f), new Vec2(1f, 1f), 0f),
                LifetimeTicks = Ticks.FromSeconds(8f),
                Owner = 0,
                SourceEventId = 77,
            });
            WorldDuels.Idle(world, 1);
            ObscurationVolume cloud = materials.World.Volumes.Single();
            cloud.GetBounds(out Vec2 before, out _);
            p0 = world.Player(0).Position;
            p1 = world.Player(1).Position;
            prop = world.Props.Find(1).Position;
            WorldDuels.IdleSeconds(world, 4f); // the gust runs from combat time 2 s to 4 s
            cloud.GetBounds(out Vec2 after, out _);
            return after.X - before.X;
        }
    }
}
