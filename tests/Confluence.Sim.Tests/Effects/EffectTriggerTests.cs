using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Effects;
using Confluence.Sim.Fields;
using Confluence.Sim.Players;
using Confluence.Sim.Status;
using Confluence.Sim.Tests.World;
using Confluence.Sim.World;

namespace Confluence.Sim.Tests.Effects
{
    /// <summary>Typed triggers raised by Sim systems reach the effects of the right player (spec L433 "trigger").</summary>
    public class EffectTriggerTests
    {
        /// <summary>A content source with test behaviors keyed by id.</summary>
        internal sealed class TestContent : IEffectContentSource
        {
            public readonly Dictionary<string, IAugmentBehavior> Augments = new Dictionary<string, IAugmentBehavior>();

            public bool TryGetAugment(string id, out IAugmentBehavior behavior) => Augments.TryGetValue(id, out behavior);

            public bool TryGetEquipment(string id, out IEquipmentBehavior behavior)
            {
                behavior = null;
                return false;
            }

            public bool TryGetBiomeRule(string id, out IBiomeRuleBehavior behavior)
            {
                behavior = null;
                return false;
            }
        }

        internal sealed class AttachRecorder : IAugmentBehavior
        {
            public readonly List<RecordAction> Records = new List<RecordAction>();
            private readonly EffectTrigger[] _triggers;

            public AttachRecorder(params EffectTrigger[] triggers)
            {
                _triggers = triggers;
            }

            public void Attach(IEffectHost host, int rank)
            {
                foreach (EffectTrigger t in _triggers)
                {
                    var record = new RecordAction();
                    Records.Add(record);
                    host.Attach(new Effect("AUG-TEST", t).With(record));
                }
            }
        }

        [Fact]
        public void Round_Start_Fires_Once_At_Placement_And_Combat_Start_When_The_Alcoves_Open()
        {
            var content = new TestContent();
            var recorder = new AttachRecorder(EffectTrigger.RoundStart, EffectTrigger.CombatStart);
            content.Augments["AUG-TEST"] = recorder;
            DuelPlayerSetup a = TestWorlds.Setup("A");
            a.Augments.Add(new ActiveAugment("AUG-TEST", 1));
            DuelWorld world = WorldDuels.Duel(a: a, alcoveTicks: 60, content: content);

            Assert.Single(recorder.Records[0].Fired);
            Assert.Equal(0, recorder.Records[0].Fired[0].Tick);
            Assert.Empty(recorder.Records[1].Fired);
            WorldDuels.Idle(world, 61);
            Assert.Single(recorder.Records[0].Fired);
            Assert.Single(recorder.Records[1].Fired);
            Assert.Equal(61, recorder.Records[1].Fired[0].Tick);
        }

        [Fact]
        public void Hits_Raise_Dealt_Taken_Damage_And_Shield_Triggers_With_Channel_And_Range()
        {
            DuelWorld world = WorldDuels.Duel();
            RecordAction dealt = EffectTests.Record(world, 0, EffectTrigger.DirectHitDealt);
            RecordAction thermalOnly = EffectTests.Record(world, 0, EffectTrigger.DirectHitDealt, e => e.Filter.Channel = DamageChannel.Thermal);
            RecordAction close = EffectTests.Record(world, 0, EffectTrigger.DirectHitDealt, e => e.Filter.MaxRange = 5f);
            RecordAction taken = EffectTests.Record(world, 1, EffectTrigger.DirectHitTaken);
            RecordAction anyDamage = EffectTests.Record(world, 1, EffectTrigger.DamageTaken);
            RecordAction absorbed = EffectTests.Record(world, 1, EffectTrigger.ShieldAbsorbed);
            WorldDuels.Idle(world, 1);
            world.GrantShield(1, "SPL-TEST-WARD", 10f, 120);
            world.ApplyDamage(DamageRequest.Split(0, 1, DamageKind.Direct, "SPL-TEST", new DamagePart(DamageChannel.Physical, 15f), new DamagePart(DamageChannel.Thermal, 10f))
                .From(world.Player(0).Position));
            WorldDuels.Idle(world, 1);

            TriggerEvent hit = Assert.Single(dealt.Fired);
            Assert.Equal(1, hit.Other);
            Assert.Equal(DamageChannel.Physical, hit.Channel);
            Assert.True(hit.HasChannel(DamageChannel.Thermal));
            Assert.True(hit.IsSpellHit);
            Assert.Equal(12f, hit.Range, 2);
            Assert.Single(thermalOnly.Fired); // the split hit carries both channels and triggers once (spec L112)
            Assert.Empty(close.Fired);       // 12 m away
            Assert.Equal(0, Assert.Single(taken.Fired).Other);
            Assert.Single(anyDamage.Fired);
            Assert.Equal(10f, Assert.Single(absorbed.Fired).Amount, 3);
        }

        [Fact]
        public void Guards_Raise_Perfect_And_Ordinary_Block_Triggers()
        {
            DuelWorld world = WorldDuels.Duel();
            RecordAction perfect = EffectTests.Record(world, 1, EffectTrigger.PerfectGuard);
            RecordAction blocked = EffectTests.Record(world, 1, EffectTrigger.GuardBlocked);
            RecordAction started = EffectTests.Record(world, 1, EffectTrigger.GuardStarted);
            WorldDuels.Idle(world, 1);
            world.Step(TestWorlds.A, WorldDuels.Guard(180f));
            world.ApplyDamage(TestWorlds.Hit(world, 0, 1, 20f));
            world.Step(TestWorlds.A, WorldDuels.Guard(180f));

            Assert.Single(started.Fired);
            Assert.Equal(0, Assert.Single(perfect.Fired).Other);
            Assert.Single(blocked.Fired);
        }

        [Fact]
        public void Movement_Raises_Jump_Dodge_Sprint_And_Selection_Triggers()
        {
            DuelWorld world = WorldDuels.Duel();
            RecordAction jump = EffectTests.Record(world, 0, EffectTrigger.Jump);
            RecordAction dodge = EffectTests.Record(world, 0, EffectTrigger.Dodge);
            RecordAction sprintStart = EffectTests.Record(world, 0, EffectTrigger.SprintStarted);
            RecordAction sprintStop = EffectTests.Record(world, 0, EffectTrigger.SprintStopped);
            RecordAction select = EffectTests.Record(world, 0, EffectTrigger.SpellSelectionChanged);
            WorldDuels.Idle(world, 2);

            world.Step(WorldDuels.Jump(0f), TestWorlds.B);
            WorldDuels.Idle(world, 60);
            world.Step(WorldDuels.Dodge(0f), TestWorlds.B);
            WorldDuels.Idle(world, 60);
            WorldDuels.Step(world, 30, WorldDuels.Sprint(0f), TestWorlds.B);
            WorldDuels.Idle(world, 5);
            world.Step(WorldDuels.Select(0f, 2), TestWorlds.B);
            world.Step(WorldDuels.Select(0f, 2), TestWorlds.B);

            Assert.Single(jump.Fired);
            Assert.Single(dodge.Fired);
            Assert.Single(sprintStart.Fired);
            Assert.Single(sprintStop.Fired);
            Assert.Single(select.Fired);
        }

        [Fact]
        public void Landing_After_A_Drop_Raises_The_Land_Trigger_With_The_Drop_Height()
        {
            ArenaLayout layout = TestWorlds.Flat();
            layout.Solids.Add(TestWorlds.Box(50, new Vec3(0f, 0f, -2f), new Vec3(4f, 2f, 4f), SolidKind.Platform));
            DuelWorld world = WorldDuels.Duel(layout);
            RecordAction land = EffectTests.Record(world, 0, EffectTrigger.Land);
            TestWorlds.Place(world, 0, new Vec3(0f, 2f, -2f), 0f);
            WorldDuels.Step(world, 90, WorldDuels.Walk(0f), TestWorlds.B);

            TriggerEvent t = Assert.Single(land.Fired);
            Assert.Equal(2f, t.Amount, 1);
        }

        [Fact]
        public void Melee_Raises_Hit_And_Miss_Triggers()
        {
            DuelWorld world = WorldDuels.Duel();
            RecordAction hit = EffectTests.Record(world, 0, EffectTrigger.MeleeHit);
            RecordAction miss = EffectTests.Record(world, 0, EffectTrigger.MeleeMiss);
            RecordAction weaponHit = EffectTests.Record(world, 0, EffectTrigger.DirectHitDealt, e => e.Filter.MeleeOnly = true);
            WorldDuels.Idle(world, 1);

            world.Step(WorldDuels.Melee(0f), TestWorlds.B); // 12 m apart: a miss
            WorldDuels.Idle(world, 60);
            Assert.Empty(hit.Fired);
            Assert.Single(miss.Fired);

            TestWorlds.Place(world, 0, new Vec3(0f, 0f, 4.8f), 0f);
            world.Step(WorldDuels.Melee(0f), TestWorlds.B);
            WorldDuels.Idle(world, 60);
            Assert.Equal(1, Assert.Single(hit.Fired).Other);
            Assert.Single(miss.Fired);
            Assert.Single(weaponHit.Fired);
        }

        [Fact]
        public void Statuses_Fields_Cover_And_Props_Raise_Their_Triggers()
        {
            ArenaLayout layout = TestWorlds.Flat();
            layout.Props.Add(WorldDuels.Prop(7, "PROP-013", new Vec3(5f, 0f, 0f), new Vec3(0.6f, 0.6f, 0.6f)));
            DuelWorld world = WorldDuels.Duel(layout);
            RecordAction applied = EffectTests.Record(world, 1, EffectTrigger.StatusApplied, e => e.Filter.Status = StatusKind.Burn);
            RecordAction removed = EffectTests.Record(world, 1, EffectTrigger.StatusRemoved);
            RecordAction field = EffectTests.Record(world, 0, EffectTrigger.FieldCreated);
            RecordAction cover = EffectTests.Record(world, 0, EffectTrigger.CoverDestroyed);
            RecordAction prop = EffectTests.Record(world, 0, EffectTrigger.PropDestroyed);
            WorldDuels.Idle(world, 1);

            world.ApplyStatus(1, StatusApplication.Of(StatusKind.Burn, "SPL-TEST", 0, 30, 4f));
            world.CreateOwnedEntity(new OwnedEntitySpec { Kind = OwnedEntityKind.Field, Owner = 0, DefinitionId = "SPL-TEST-FIELD", LifetimeTicks = 120 });
            int wall = world.AddTemporarySolid(new TemporarySolidSpec { Box = YawBox.OnFloor(new Vec3(-5f, 0f, 0f), new Vec3(2f, 2f, 0.4f), 0f), Kind = SolidKind.Wall, Durability = 10f, OwnerPlayer = 1 });
            world.DamageSolid(wall, 20f, 0);
            world.Props.Damage(7, 50f, 0);
            WorldDuels.Idle(world, 40);

            Assert.Equal(0, Assert.Single(applied.Fired).Other);
            Assert.Equal(StatusKind.Burn, Assert.Single(removed.Fired).Status);
            Assert.Equal("SPL-TEST-FIELD", Assert.Single(field.Fired).SourceId);
            Assert.Single(cover.Fired);
            Assert.Equal("PROP-013", Assert.Single(prop.Fired).SourceId); // a destroyed prop is not cover
        }

        [Fact]
        public void Casts_Raise_Commit_And_Release_Triggers()
        {
            DuelWorld world = WorldDuels.Duel();
            RecordAction commit = EffectTests.Record(world, 0, EffectTrigger.CastCommitted);
            RecordAction release = EffectTests.Record(world, 0, EffectTrigger.CastReleased, e => e.Filter.SourceId = "SPL-009");
            WorldDuels.Idle(world, 1);

            Assert.True(world.TryCommitMana(0, 12f, "SPL-009"));
            world.Effects.RaiseCastReleased(0, "SPL-009", 4242);
            WorldDuels.Idle(world, 1);

            TriggerEvent c = Assert.Single(commit.Fired);
            Assert.Equal("SPL-009", c.SourceId);
            Assert.Equal(12f, c.Amount, 3);
            Assert.Equal(4242, Assert.Single(release.Fired).EventId);
        }

        [Fact]
        public void Periodic_Effects_Fire_On_Their_Combat_Period()
        {
            DuelWorld world = WorldDuels.Duel();
            RecordAction periodic = EffectTests.Record(world, 0, EffectTrigger.Periodic, e => e.PeriodTicks = 60);
            WorldDuels.IdleSeconds(world, 3.5f);
            Assert.Equal(new[] { 60, 120, 180 }, periodic.Fired.Select(t => t.Tick).ToArray());
        }

        [Fact]
        public void Entering_And_Leaving_Surfaces_And_Volumes_Raise_Triggers()
        {
            ArenaLayout layout = TestWorlds.Flat();
            layout.Materials.Add(WorldDuels.IcePatch(new Vec2(0f, -2f), 1f, 1f));
            DuelWorld world = WorldDuels.Duel(layout);
            RecordAction onIce = EffectTests.Record(world, 0, EffectTrigger.EnteredSurface, e => e.Filter.Surface = SurfaceKind.Ice);
            RecordAction offIce = EffectTests.Record(world, 0, EffectTrigger.LeftSurface, e => e.Filter.Surface = SurfaceKind.Ice);
            RecordAction intoSteam = EffectTests.Record(world, 0, EffectTrigger.EnteredVolume, e => e.Filter.Volume = VolumeKind.Steam);
            RecordAction outOfSteam = EffectTests.Record(world, 0, EffectTrigger.LeftVolume);
            WorldDuels.Materials(world).CreateVolume(new VolumeSpec
            {
                Kind = VolumeKind.Steam,
                Footprint = Footprint.Rectangle(new Vec2(0f, 2f), new Vec2(1f, 1f), 0f),
                LifetimeTicks = Ticks.FromSeconds(8f),
                Owner = 1,
            });
            WorldDuels.Step(world, 150, WorldDuels.Walk(0f), TestWorlds.B); // z -6 -> about 7.5: across the ice, then the steam

            Assert.Single(onIce.Fired);
            Assert.Single(offIce.Fired);
            Assert.Single(intoSteam.Fired);
            Assert.Single(outOfSteam.Fired);
            Assert.True(intoSteam.Fired[0].Tick > onIce.Fired[0].Tick);
        }
    }
}
