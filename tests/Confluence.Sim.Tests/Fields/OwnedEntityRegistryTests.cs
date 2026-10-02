using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Sim.Duel;
using Confluence.Sim.Fields;

namespace Confluence.Sim.Tests.Fields
{
    /// <summary>Field and summon ownership limits (spec L128).</summary>
    public class OwnedEntityRegistryTests
    {
        private static OwnedEntitySpec Field(int owner, string def, float seconds = 6f, bool neutral = false)
        {
            return new OwnedEntitySpec
            {
                Kind = OwnedEntityKind.Field,
                Owner = owner,
                DefinitionId = def,
                LifetimeTicks = (int)(seconds * 60),
                Radius = 2f,
                Neutral = neutral,
            };
        }

        private static OwnedEntitySpec Summon(int owner, string def)
        {
            return new OwnedEntitySpec { Kind = OwnedEntityKind.Summon, Owner = owner, DefinitionId = def, LifetimeTicks = 480, Health = 40f };
        }

        [Fact]
        public void Fourth_Field_Retires_The_Oldest_And_Neutral_Fields_Count()
        {
            var registry = new OwnedEntityRegistry();
            var retired = new List<OwnedEntity>();
            OwnedEntity first = registry.Create(Field(0, "SPL-A"), 0, retired);
            registry.Create(Field(0, "SPL-B", neutral: true), 1, retired);
            registry.Create(Field(0, "SPL-C"), 2, retired);
            registry.Create(Field(1, "SPL-D"), 2, retired); // the opponent's fields are separate
            Assert.Empty(retired);

            registry.Create(Field(0, "SPL-E"), 3, retired);
            Assert.Single(retired);
            Assert.Same(first, retired[0]);
            Assert.False(first.Active);
            Assert.Equal(3, registry.Count(0, OwnedEntityKind.Field));
            Assert.Equal(1, registry.Count(1, OwnedEntityKind.Field));
        }

        [Fact]
        public void Third_Summon_Retires_The_Oldest_Summon_But_Not_A_Field()
        {
            var registry = new OwnedEntityRegistry();
            var retired = new List<OwnedEntity>();
            registry.Create(Field(0, "SPL-F"), 0, retired);
            OwnedEntity oldest = registry.Create(Summon(0, "SPL-S1"), 1, retired);
            registry.Create(Summon(0, "SPL-S2"), 2, retired);
            registry.Create(Summon(0, "SPL-S3"), 3, retired);
            Assert.Single(retired);
            Assert.Same(oldest, retired[0]);
            Assert.Equal(1, registry.Count(0, OwnedEntityKind.Field));
            Assert.Equal(2, registry.Count(0, OwnedEntityKind.Summon));
        }

        [Fact]
        public void Lifetime_Caps_At_8_Seconds()
        {
            var registry = new OwnedEntityRegistry();
            OwnedEntity e = registry.Create(Field(0, "SPL-A", 20f), 100, null);
            Assert.Equal(100 + 480, e.ExpireTick);
            var expired = new List<OwnedEntity>();
            registry.Expire(579, expired);
            Assert.Empty(expired);
            registry.Expire(580, expired);
            Assert.Single(expired);
        }

        [Fact]
        public void Entities_That_May_Not_Retire_Are_Skipped()
        {
            var registry = new OwnedEntityRegistry();
            OwnedEntitySpec pinned = Field(0, "SPL-PIN");
            pinned.NotRetirable = true;
            registry.Create(pinned, 0, null);
            OwnedEntity second = registry.Create(Field(0, "SPL-B"), 1, null);
            registry.Create(Field(0, "SPL-C"), 2, null);
            var retired = new List<OwnedEntity>();
            registry.Create(Field(0, "SPL-D"), 3, retired);
            Assert.Same(second, retired[0]);
        }

        [Fact]
        public void World_Emits_Dissolution_Events_And_Ownership_Grants_No_Immunity()
        {
            DuelWorld w = TestWorlds.Duel();
            for (int i = 0; i < 4; i++)
            {
                w.CreateOwnedEntity(Field(0, "SPL-" + i));
            }

            Assert.Contains(w.EventLog, e => e is FieldEvent f && f.Change == FieldChange.Retired && f.DefinitionId == "SPL-0");
            OwnedEntity mine = w.Owned.Entities[0];
            mine.Position = w.Player(0).Position;
            Assert.True(mine.Contains(w.Player(0).Position)); // the owner is inside its own field like anyone else

            TestWorlds.RunSeconds(w, 6.1f, TestWorlds.A, TestWorlds.B);
            Assert.Empty(w.Owned.Entities);
            Assert.Contains(w.EventLog, e => e is FieldEvent f && f.Change == FieldChange.Expired);
        }

        [Fact]
        public void Destroying_A_Hostile_Summon_Is_Recorded()
        {
            DuelWorld w = TestWorlds.Duel();
            OwnedEntity summon = w.CreateOwnedEntity(Summon(1, "SPL-WOLF"));
            Assert.False(w.DamageSummon(summon.Id, 25f, 0));
            Assert.True(w.DamageSummon(summon.Id, 25f, 0));
            Assert.Equal(1, w.Player(0).Feats.SummonsDestroyed);
            Assert.Contains(w.TickEvents, e => e is FieldEvent f && f.Change == FieldChange.Destroyed && f.Destroyer == 0);
        }

        [Fact]
        public void Behaviors_Run_In_The_Field_Step()
        {
            DuelWorld w = TestWorlds.Duel();
            var behavior = new CountingBehavior();
            OwnedEntitySpec spec = Field(0, "SPL-A", 1f);
            spec.Behavior = behavior;
            w.CreateOwnedEntity(spec);
            TestWorlds.RunSeconds(w, 2f, TestWorlds.A, TestWorlds.B);
            Assert.Equal(60, behavior.Ticks);
        }

        private sealed class CountingBehavior : IOwnedEntityBehavior
        {
            public int Ticks;

            public void OnTick(DuelWorld world, OwnedEntity entity) => Ticks++;
        }
    }
}
