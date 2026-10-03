using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions;
using SurfaceInfo = Confluence.Sim.World.SurfaceInfo;
using SurfaceKind = Confluence.Sim.World.SurfaceKind;
using Confluence.Reactions.Events;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Effects;
using Confluence.Sim.Fields;
using Confluence.Sim.Players;
using Confluence.Sim.Status;
using Confluence.Sim.Tests.Effects;
using Confluence.Sim.World;
using Confluence.Sim.World.Materials;

namespace Confluence.Sim.Tests.World.Materials
{
    /// <summary>The duel's reaction world (spec §4 L130-L175, L668-L672) running inside a real DuelWorld.</summary>
    public class ReactionMaterialsTests
    {
        private static Vec3 Spawn1 => new Vec3(0f, 0f, 6f);

        [Fact]
        public void A_Duel_Builds_Its_Reaction_World_From_The_Layout_By_Default()
        {
            ArenaLayout layout = TestWorlds.Flat();
            layout.Materials.Add(WorldDuels.WaterBed(new Vec2(3f, 0f), 1f, 1f, water: 2));
            DuelWorld world = WorldDuels.Duel(layout);

            ReactionMaterials materials = Assert.IsType<ReactionMaterials>(world.Materials);
            Assert.Same(world, materials.Duel);
            Assert.Equal(2, materials.World.UnitsAt(new Vec3(3.25f, 0f, 0.25f)).Water);
            Assert.Equal(world.AlcoveTicks, materials.World.CombatStartTick);
        }

        [Fact]
        public void Reaction_Contact_Damage_Is_Attributed_Environment_Damage_That_Never_Triggers_Hit_Effects_Or_Lifesteal()
        {
            ArenaLayout layout = TestWorlds.Flat();
            layout.Materials.Add(WorldDuels.WaterBed(new Vec2(0f, 6f), 1f, 1f));
            DuelWorld world = WorldDuels.Duel(layout);
            var record = new RecordAction();
            Effect onHit = new Effect("AUG-TEST", EffectTrigger.DirectHitDealt).With(record);
            world.Effects.Host(0).Attach(onHit);
            world.Player(0).Stats.Add(StatKind.Lifesteal, 0.1f, "EQP-TEST");
            WorldDuels.Idle(world, 2);
            world.ApplyDamage(TestWorlds.Hit(world, 1, 0, 30f)); // room to heal, if lifesteal wrongly applied

            ReactionMaterials materials = WorldDuels.Materials(world);
            materials.RecordSource(9001, "SPL-TEST-BOLT");
            materials.Discharge(new ElectricDischarge { Point = Spawn1, ReactionDamage = 20f, Owner = 0, SourceEventId = 9001 });
            WorldDuels.Idle(world, 1);

            DamageEvent shock = world.TickEvents.OfType<DamageEvent>().Single();
            Assert.Equal(DamageKind.Environment, shock.Kind);
            Assert.Equal(0, shock.Attacker);
            Assert.Equal(1, shock.Target);
            Assert.Equal("SPL-TEST-BOLT", shock.SourceId);
            Assert.Equal(ReactionTag.Conductivity, shock.Reaction);
            Assert.Equal(9001, shock.OriginEventId);
            Assert.Equal(20f, shock.Dealt, 3);
            Assert.Empty(world.TickEvents.OfType<HealEvent>());
            Assert.Empty(record.Fired);
            Assert.Equal(20f, world.Player(0).Feats.ConductivityDamage, 3); // FEAT-020 still credits the owner
        }

        [Fact]
        public void The_Owner_Of_A_Reaction_Is_Not_Exempt_From_It()
        {
            ArenaLayout layout = TestWorlds.Flat();
            layout.Materials.Add(WorldDuels.WaterBed(new Vec2(0f, 3f), 0.5f, 5f));
            DuelWorld world = WorldDuels.Duel(layout);
            TestWorlds.Place(world, 0, new Vec3(0.25f, 0f, 0.25f), 0f);
            TestWorlds.Place(world, 1, new Vec3(0.25f, 0f, 2.75f), 180f);
            WorldDuels.Idle(world, 1);

            WorldDuels.Materials(world).Discharge(new ElectricDischarge { Point = new Vec3(0.25f, 0f, 1.25f), ReactionDamage = 12f, Owner = 0, SourceEventId = 50 });
            WorldDuels.Idle(world, 1);

            List<DamageEvent> hits = world.TickEvents.OfType<DamageEvent>().ToList();
            Assert.Contains(hits, d => d.Target == 0 && d.Attacker == 0 && d.Kind == DamageKind.Environment);
            Assert.Contains(hits, d => d.Target == 1 && d.Attacker == 0 && d.Kind == DamageKind.Environment);
            Assert.Equal(12f, world.Player(0).Feats.ConductivityDamage, 3); // only the opponent's hit credits the owner
        }

        [Fact]
        public void A_Players_Clouds_Count_Against_Their_Three_Field_Cap_And_The_Oldest_Retires()
        {
            ArenaLayout layout = TestWorlds.Flat();
            for (int i = 0; i < 4; i++)
            {
                layout.Materials.Add(WorldDuels.WaterBed(new Vec2(-9f + 4f * i, 0f), 0.25f, 0.25f, water: 2));
            }

            DuelWorld world = WorldDuels.Duel(layout);
            WorldDuels.Idle(world, 1);
            for (int i = 0; i < 3; i++)
            {
                world.CreateOwnedEntity(new OwnedEntitySpec { Kind = OwnedEntityKind.Field, Owner = 0, DefinitionId = "SPL-TEST-FIELD", LifetimeTicks = 480 });
            }

            ReactionMaterials materials = WorldDuels.Materials(world);
            var clouds = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                Vec3 bed = new Vec3(-9f + 4f * i + 0.25f, 0f, 0.25f);
                materials.Deposit(MaterialDeposit.QuickProjectile(Material.Heat, bed, 0, 100 + i));
                WorldDuels.Idle(world, 1);
                VolumeEvent created = materials.LastStep.VolumeEvents.Single(v => v.Kind == VolumeEventKind.Created);
                Assert.Equal(VolumeKind.Steam, created.VolumeKind);
                Assert.Equal(0, created.Owner); // the last direct source (the heat) owns the cloud (spec L670)
                clouds.Add(created.VolumeId);
                Assert.Equal(GlobalRules.FieldsAndSummons.MaxFieldsPerPlayer, world.Owned.Count(0, OwnedEntityKind.Field));
            }

            // Three spell fields retired to make room, oldest first; then the oldest cloud itself.
            List<FieldEvent> retired = WorldDuels.Events<FieldEvent>(world).Where(f => f.Change == FieldChange.Retired).ToList();
            Assert.Equal(4, retired.Count);
            Assert.All(retired.Take(3), f => Assert.Equal("SPL-TEST-FIELD", f.DefinitionId));
            Assert.Equal(MaterialRules.CloudFieldPrefix + VolumeKind.Steam, retired[3].DefinitionId);
            Assert.Null(materials.World.FindVolume(clouds[0]));
            Assert.All(clouds.Skip(1), id => Assert.NotNull(materials.World.FindVolume(id)));
            WorldDuels.Idle(world, 1);
            Assert.Contains(materials.LastStep.VolumeEvents, v => v.VolumeId == clouds[0] && v.Reason == VolumeRetireReason.FieldLimit);
            Assert.Equal(3, materials.CountedCloudCount);
        }

        [Fact]
        public void A_Cloud_That_Ends_In_The_Reaction_World_Frees_Its_Field_Slot()
        {
            ArenaLayout layout = TestWorlds.Flat();
            layout.Materials.Add(WorldDuels.WaterBed(new Vec2(-4f, 0f), 0.25f, 0.25f, water: 1));
            DuelWorld world = WorldDuels.Duel(layout);
            ReactionMaterials materials = WorldDuels.Materials(world);
            materials.Deposit(MaterialDeposit.QuickProjectile(Material.Heat, new Vec3(-3.75f, 0f, 0.25f), 0, 7));
            WorldDuels.Idle(world, 1);
            Assert.Equal(1, world.Owned.Count(0, OwnedEntityKind.Field));

            WorldDuels.IdleSeconds(world, GlobalRules.World.SteamLifetimeSec + 0.1f); // 3 s base lifetime (spec L144)
            Assert.Equal(0, world.Owned.Count(0, OwnedEntityKind.Field));
            Assert.Contains(WorldDuels.Events<FieldEvent>(world), f => f.Change == FieldChange.Expired && f.DefinitionId == MaterialRules.CloudFieldPrefix + VolumeKind.Steam);
            Assert.Equal(0, materials.CountedCloudCount);
        }

        [Fact]
        public void Two_Meters_Of_Steam_Block_Acquisition_Both_Ways_But_Never_Projectiles()
        {
            DuelWorld world = WorldDuels.Duel();
            WorldDuels.Idle(world, 1);
            Assert.True(world.CanAcquire(0, 1));

            ReactionMaterials materials = WorldDuels.Materials(world);
            materials.CreateVolume(new VolumeSpec
            {
                Kind = VolumeKind.Steam,
                Footprint = Footprint.Rectangle(Vec2.Zero, new Vec2(3f, 1.5f), 0f),
                LifetimeTicks = Ticks.FromSeconds(3f),
                Owner = ReactionRules.MapOwner,
            });
            WorldDuels.Idle(world, 1);

            Vec3 eye0 = world.Player(0).Eye;
            Vec3 eye1 = world.Player(1).Eye;
            Assert.True(materials.ObscuringPathLength(eye0, eye1) >= GlobalRules.World.SteamSightBlockPathLength);
            Assert.True(materials.BlocksAcquisition(eye0, eye1));
            Assert.False(world.CanAcquire(0, 1));
            Assert.False(world.CanAcquire(1, 0));
            Assert.True(world.RaycastOpponent(0, eye0, world.Player(1).Position + new Vec3(0f, 1f, 0f) - eye0, 30f, out _)); // steam intercepts nothing (L167)
        }

        [Fact]
        public void Less_Than_Two_Meters_Of_Steam_Does_Not_Block_Acquisition()
        {
            DuelWorld world = WorldDuels.Duel();
            ReactionMaterials materials = WorldDuels.Materials(world);
            materials.CreateVolume(new VolumeSpec
            {
                Kind = VolumeKind.Steam,
                Footprint = Footprint.Rectangle(Vec2.Zero, new Vec2(3f, 0.5f), 0f),
                LifetimeTicks = Ticks.FromSeconds(3f),
                Owner = ReactionRules.MapOwner,
            });
            WorldDuels.Idle(world, 1);
            Assert.True(materials.ObscuringPathLength(world.Player(0).Eye, world.Player(1).Eye) > 0.5f);
            Assert.True(world.CanAcquire(0, 1));
        }

        [Fact]
        public void Ice_Reduces_Braking_And_Adds_Drift_But_Never_Takes_Control()
        {
            float normal = SlideAfterRelease(withIce: false, out float normalSide);
            float ice = SlideAfterRelease(withIce: true, out float iceSide);

            Assert.True(ice > 3f * normal, "ice slide " + ice + " vs normal " + normal);
            Assert.True(iceSide > 0.25f * normalSide, "steering still works on ice");
            Assert.True(iceSide < normalSide, "a turn on ice keeps more of the old momentum (lateral drift)");
        }

        // Walks +Z at full speed, releases, and measures the slide; then measures the sideways speed 0.25 s into a turn.
        private static float SlideAfterRelease(bool withIce, out float sideSpeed)
        {
            ArenaLayout layout = TestWorlds.Flat();
            if (withIce)
            {
                layout.Materials.Add(WorldDuels.IcePatch(new Vec2(0f, 0f), 6f, 10f));
            }

            DuelWorld world = WorldDuels.Duel(layout);
            TestWorlds.Place(world, 0, new Vec3(0f, 0f, -8f), 0f);
            TestWorlds.Place(world, 1, new Vec3(12f, 0f, 12f), 180f);
            WorldDuels.Step(world, 60, WorldDuels.Walk(0f), TestWorlds.B);
            Assert.Equal(withIce ? SurfaceKind.Ice : SurfaceKind.Normal, world.SurfaceUnder(world.Player(0)).Kind);
            Vec3 released = world.Player(0).Position;
            WorldDuels.Step(world, 90, WorldDuels.Idle(0f), TestWorlds.B);
            float slide = world.Player(0).Position.Z - released.Z;

            WorldDuels.Step(world, 60, WorldDuels.Walk(0f), TestWorlds.B);
            WorldDuels.Step(world, 15, WorldDuels.Walk(0f, x: 1f, y: 0f), TestWorlds.B);
            sideSpeed = world.Player(0).Motor.Velocity.X;
            return slide;
        }

        [Fact]
        public void Sticky_Growth_Slows_Movement_Within_The_Thirty_Percent_Cap()
        {
            Assert.Equal(WorldDuels.Walkspeed * 0.8f, SpeedOnGrowth(0.2f, 0f), 2);
            Assert.Equal(WorldDuels.Walkspeed * (1f - GlobalRules.Control.SlowCap), SpeedOnGrowth(0.5f, 0f), 2); // 50% declared, 30% applied
            Assert.Equal(WorldDuels.Walkspeed * 0.75f, SpeedOnGrowth(0.2f, 0.25f), 2); // only the strongest slow applies (L116)
        }

        private static float SpeedOnGrowth(float surfaceSlow, float statusSlow)
        {
            DuelWorld world = WorldDuels.Duel();
            TestWorlds.Place(world, 0, new Vec3(0f, 0f, -2f), 0f);
            WorldDuels.Materials(world).Deposit(new MaterialDeposit
            {
                Material = Material.Growth,
                Footprint = Footprint.Circle(new Vec2(0f, 0f), 5f),
                LifetimeTicks = Ticks.FromSeconds(8f),
                Sticky = true,
                SlowFraction = surfaceSlow,
                Owner = 1,
                SourceEventId = 3,
            });
            WorldDuels.Idle(world, 1);
            if (statusSlow > 0f)
            {
                world.ApplyStatus(0, StatusApplication.Of(StatusKind.Slow, "SPL-TEST-SLOW", 1, Ticks.FromSeconds(2f), statusSlow));
            }

            Assert.Equal(SurfaceKind.Sticky, world.SurfaceUnder(world.Player(0)).Kind);
            WorldDuels.Step(world, 30, WorldDuels.Walk(0f), TestWorlds.B);
            return world.Player(0).Motor.Velocity.Length();
        }

        [Fact]
        public void Standing_In_Water_Makes_A_Player_Wet_Without_Slowing_Them_And_Wet_Lingers_About_A_Second()
        {
            ArenaLayout layout = TestWorlds.Flat();
            layout.Materials.Add(WorldDuels.WaterBed(new Vec2(0f, 0f), 1f, 3f));
            DuelWorld world = WorldDuels.Duel(layout);
            TestWorlds.Place(world, 0, new Vec3(0f, 0f, -2.5f), 0f);
            WorldDuels.Idle(world, 1);
            StatusInstance wet = world.Player(0).Statuses.Find(StatusKind.Wet, MaterialRules.WetCellSource);
            Assert.NotNull(wet);
            Assert.False(world.Player(1).Statuses.Has(StatusKind.Wet));
            Assert.Equal(SurfaceKind.Wet, world.SurfaceUnder(world.Player(0)).Kind);

            WorldDuels.Step(world, 30, WorldDuels.Walk(0f), TestWorlds.B);
            Assert.Equal(WorldDuels.Walkspeed, world.Player(0).Motor.Velocity.Length(), 2); // wet stone: no movement penalty (L175)
            Assert.True(world.Player(0).Statuses.Has(StatusKind.Wet));

            ReactionMaterials materials = WorldDuels.Materials(world);
            int lastInWater = world.Tick;
            for (int i = 0; i < 90; i++) // walk off the bed (it ends at z = 3)
            {
                world.Step(WorldDuels.Walk(0f), TestWorlds.B);
                if (materials.StandsInWater(world.Player(0).Position))
                {
                    lastInWater = world.Tick;
                }
            }

            Assert.True(world.Player(0).Position.Z > 4f);
            Assert.True(world.Player(0).Statuses.Has(StatusKind.Wet)); // it lingers after leaving the water
            WorldDuels.Step(world, 120, WorldDuels.Idle(0f), TestWorlds.B);
            StatusEvent expired = WorldDuels.Events<StatusEvent>(world).Last(s => s.Player == 0 && s.Kind == StatusKind.Wet);
            Assert.Equal(StatusChange.Expired, expired.Change);
            float linger = (expired.Tick - lastInWater) / (float)Ticks.PerSecond;
            Assert.InRange(linger, MaterialRules.WetLingerSec - 0.02f, MaterialRules.WetLingerSec + MaterialRules.WetRefreshMarginSec + 0.02f);
        }

        [Fact]
        public void Surface_Mapping_Follows_The_Movement_Rules_Of_Each_Surface()
        {
            Vec3 feet = Vec3.Zero;
            Assert.Equal(SurfaceKind.Ice, Map(SurfaceFlags.Ice | SurfaceFlags.Charged, feet).Kind); // a charged ice tile is still ice
            SurfaceInfo sticky = Map(SurfaceFlags.Growth | SurfaceFlags.StickyGrowth, feet, 0.2f);
            Assert.Equal(SurfaceKind.Sticky, sticky.Kind);
            Assert.Equal(0.2f, sticky.SlowFraction, 5);
            Assert.Equal(SurfaceKind.DampSand, Map(SurfaceFlags.Sand | SurfaceFlags.DampSand, feet).Kind);
            Assert.Equal(SurfaceKind.Wet, Map(SurfaceFlags.Wet, feet).Kind);
            Assert.Equal(SurfaceKind.Normal, Map(SurfaceFlags.Growth | SurfaceFlags.Burning, feet).Kind); // fire is a contact hazard, not footing
            Assert.Equal(SurfaceKind.Normal, Map(SurfaceFlags.Sand, feet).Kind);
            Assert.Equal(SurfaceKind.Normal, Map(SurfaceFlags.Ice, new Vec3(0f, 2f, 0f)).Kind); // a platform above the cell
        }

        private static SurfaceInfo Map(SurfaceFlags flags, Vec3 feet, float slow = 0f)
        {
            var surface = new Confluence.Reactions.SurfaceInfo(Confluence.Reactions.SurfaceInfo.Classify(flags), flags, default, 0, slow, 0f);
            return SurfaceMapping.ToMotor(surface, feet);
        }

        [Fact]
        public void Generic_Deposits_Translate_Into_Reaction_Requests()
        {
            ArenaLayout layout = TestWorlds.Flat();
            layout.Materials.Add(WorldDuels.WaterBed(new Vec2(4f, 0f), 0.25f, 0.25f, water: 1));
            DuelWorld world = WorldDuels.Duel(layout);
            world.QueueDeposit(new WorldDeposit { Kind = DepositKind.Frost, Strength = 2, Position = new Vec3(4.25f, 0f, 0.25f), SourcePlayer = 0, SourceId = "SPL-TEST", OriginEventId = 11 });
            world.QueueDeposit(new WorldDeposit { Kind = DepositKind.Heat, Strength = 1, Position = new Vec3(-4.25f, 0f, 0.25f), SourcePlayer = 1, OriginEventId = 12 });
            WorldDuels.Idle(world, 1);

            ReactionWorld reactions = WorldDuels.Reactions(world);
            Assert.True(reactions.HasIceAt(new Vec3(4.25f, 0f, 0.25f))); // two frost and one water form an ice tile (L145)
            Assert.Contains(WorldDuels.Materials(world).LastStep.Events, e => e.Kind == ReactionKind.IceFormed && e.Owner == 0);
        }

        [Fact]
        public void Reactions_Are_Deterministic_And_Folded_Into_The_Duel_Hash()
        {
            List<ulong> a = RunScripted(extraDeposit: false);
            List<ulong> b = RunScripted(extraDeposit: false);
            List<ulong> c = RunScripted(extraDeposit: true);
            Assert.Equal(a, b);
            Assert.Equal(a.Take(200), c.Take(200));
            Assert.NotEqual(a[200], c[200]); // the reaction world's state is part of the duel hash
        }

        private static List<ulong> RunScripted(bool extraDeposit)
        {
            ArenaLayout layout = TestWorlds.Flat();
            layout.Materials.Add(WorldDuels.WaterBed(new Vec2(0f, 2f), 1f, 1f, water: 2));
            layout.Props.Add(WorldDuels.Prop(1, "PROP-013", new Vec3(3f, 0f, 0f), new Vec3(0.6f, 0.6f, 0.6f)));
            DuelWorld world = WorldDuels.Duel(layout);
            ReactionMaterials materials = WorldDuels.Materials(world);
            var hashes = new List<ulong>();
            for (int t = 0; t < 360; t++)
            {
                if (t == 10)
                {
                    materials.Deposit(MaterialDeposit.QuickProjectile(Material.Heat, new Vec3(0.25f, 0f, 2.25f), 0, 21));
                    materials.Discharge(new ElectricDischarge { Point = new Vec3(-0.25f, 0f, 1.75f), ReactionDamage = 8f, Owner = 1, SourceEventId = 22 });
                    materials.PushProp(1, new Vec3(1f, 0f, 0f), 0, 23);
                }

                if (t == 30)
                {
                    materials.Wind(new WindGust { Direction = new Vec2(1f, 0f), Strength = 2, DurationTicks = 30, Owner = 1, SourceEventId = 24 });
                }

                if (extraDeposit && t == 200)
                {
                    materials.Deposit(MaterialDeposit.QuickProjectile(Material.Dust, new Vec3(-6f, 0f, -6f), 1, 25, Ticks.FromSeconds(2f)));
                }

                world.Step(WorldDuels.Walk(0f, 0.3f, 0.2f), WorldDuels.Walk(180f, -0.2f, 0.1f));
                hashes.Add(world.StateHash());
            }

            return hashes;
        }
    }
}
