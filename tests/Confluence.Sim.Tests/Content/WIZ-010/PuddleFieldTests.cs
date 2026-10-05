using System.Linq;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions;
using Confluence.Sim.Content.Wizards.HalenTide;
using Confluence.Sim.Duel;
using Confluence.Sim.Fields;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;

namespace Confluence.Sim.Tests.Content.Halen
{
    /// <summary>
    /// The authored transformation puddles (AUG-435 Hammer Wake, AUG-441 Cascade Landing, AUG-446 Prison Pool) are gameplay
    /// fields: they consume the caster's normal allocation (spec L128 "Neutral reaction fields created by a player count
    /// against that player's field limit"; L510 "consumes the normal allocation ... never grants an extra hidden field"),
    /// so at the three-field limit the oldest owned field retires, and their water carries the field id.
    /// </summary>
    public class PuddleFieldTests
    {
        /// <summary>Gives Halen three owned fields (the limit), oldest first; returns the oldest.</summary>
        private static OwnedEntity FillFieldLimit(HalenDuel d)
        {
            OwnedEntity oldest = null;
            for (int i = 0; i < GlobalRules.FieldsAndSummons.MaxFieldsPerPlayer; i++)
            {
                OwnedEntity e = d.World.CreateOwnedEntity(new OwnedEntitySpec
                {
                    Kind = OwnedEntityKind.Field,
                    Owner = 0,
                    DefinitionId = "SPL-TEST-" + i,
                    LifetimeTicks = Ticks.FromSeconds(7.5),
                    Position = new Vec3(-8f + i, 0f, 8f),
                    Radius = 0.5f,
                });
                oldest = oldest ?? e;
                d.Step();
            }

            Assert.Equal(GlobalRules.FieldsAndSummons.MaxFieldsPerPlayer, d.World.Owned.Count(0, OwnedEntityKind.Field));
            return oldest;
        }

        private static void AssertPuddleIsAnOwnedField(HalenDuel d, string spellId, OwnedEntity oldest, Vec3 center)
        {
            TidePuddleEntity puddle = d.Runtime.Entities.OfType<TidePuddleEntity>().Single();
            Assert.NotNull(puddle.Owned);
            Assert.True(puddle.Owned.Active);
            Assert.Equal(OwnedEntityKind.Field, puddle.Owned.Kind);
            Assert.Equal(spellId, puddle.Owned.DefinitionId);
            Assert.Equal(0, puddle.Owned.Owner);
            Assert.True(puddle.Owned.Neutral);

            // The puddle took a slot: the field count stays at the limit and the oldest field retired.
            Assert.Equal(GlobalRules.FieldsAndSummons.MaxFieldsPerPlayer, d.World.Owned.Count(0, OwnedEntityKind.Field));
            Assert.False(oldest.Active);
            FieldEvent retired = d.Events<FieldEvent>().Single(e => e.Change == FieldChange.Retired);
            Assert.Equal(oldest.Id, retired.EntityId);
            Assert.Contains(d.Events<FieldEvent>(), e => e.Change == FieldChange.Created && e.EntityId == puddle.Owned.Id && e.DefinitionId == spellId);
            Assert.Equal(1, d.Units(center).Water);

            // Ending the owned field (here by a dispel of the field, spec L161) ends the puddle entity with it.
            Assert.True(d.World.DispelOwnedEntity(puddle.Owned.Id, 1));
            d.Run(2);
            Assert.Empty(d.Runtime.Entities.OfType<TidePuddleEntity>());
        }

        [Fact]
        [Verifies("AUG-435")]
        public void The_Hammer_Wake_Puddle_Consumes_A_Field_Slot()
        {
            HalenDuel d = HalenTestKit.Duel(HalenTestKit.Halen(null, HalenTestKit.Aug(HammerWake.Id, 1)));
            TestWorlds.Place(d.World, 1, new Vec3(0f, 0f, -3.5f), 180f);
            d.Halen = TestWorlds.A;
            OwnedEntity oldest = FillFieldLimit(d);
            d.Cast(1);
            d.RunUntil(() => d.DamageTo(1, WaterHammer.Id).Any(), 40);
            d.Step();
            AssertPuddleIsAnOwnedField(d, WaterHammer.Id, oldest, new Vec3(0f, 0f, -3.5f));
        }

        [Fact]
        [Verifies("AUG-441")]
        public void The_Cascade_Landing_Puddle_Consumes_A_Field_Slot()
        {
            HalenDuel d = HalenTestKit.Duel(HalenTestKit.Halen(null, HalenTestKit.Aug(CascadeLanding.Id, 1)));
            OwnedEntity oldest = FillFieldLimit(d);
            CascadeVaultTests.Vault(new Vec3(0f, 0f, -1f), d: d);
            d.RunUntil(() => d.SpellEvents(SpellEventKind.MovementStarted).Any(), 40);
            d.Step();
            Assert.True(oldest.Active); // airborne: the puddle (and its retirement) waits for the landing
            Assert.Empty(d.Runtime.Entities.OfType<TidePuddleEntity>());
            SpellEvent landed = CascadeVaultTests.Landed(d);
            Assert.NotNull(landed);
            Assert.Equal(landed.Tick, d.Runtime.Entities.OfType<TidePuddleEntity>().Single().Owned.CreatedTick); // the landing tick
            d.Step();
            AssertPuddleIsAnOwnedField(d, CascadeVault.Id, oldest, d.P0.Position);
        }

        [Fact]
        [Verifies("AUG-446")]
        public void The_Prison_Pool_Puddle_Consumes_A_Field_Slot()
        {
            HalenDuel d = HalenTestKit.Duel(HalenTestKit.Halen(new[] { DeepglassPrison.Id }, HalenTestKit.Aug(PrisonPool.Id, 1)));
            OwnedEntity oldest = FillFieldLimit(d);
            DeepglassPrisonTests.Fire(d);
            d.RunUntil(() => DeepglassPrisonTests.HitOn(d) != null, 90);
            Vec3 impact = d.P1.Position;
            d.RunUntil(() => d.Tide(TideEventKind.PrisonReleased).Any(), 60);
            d.Step();
            AssertPuddleIsAnOwnedField(d, DeepglassPrison.Id, oldest, impact);
        }

        [Fact]
        [Verifies("AUG-435", "AUG-441", "AUG-446")]
        public void A_Puddle_Lifetime_Is_Its_Owned_Fields_And_Capped_At_8_Seconds()
        {
            HalenDuel d = HalenTestKit.Duel(HalenTestKit.Halen(null, HalenTestKit.Aug(HammerWake.Id, 2)));
            TestWorlds.Place(d.World, 1, new Vec3(0f, 0f, -3.5f), 180f);
            d.Halen = TestWorlds.A;
            d.Cast(1);
            d.RunUntil(() => d.DamageTo(1, WaterHammer.Id).Any(), 40);
            d.Step();
            TidePuddleEntity puddle = d.Runtime.Entities.OfType<TidePuddleEntity>().Single();
            Assert.Equal(Ticks.FromSeconds(4.0), puddle.Owned.ExpireTick - puddle.Owned.CreatedTick); // AUG-435 rank 2 "for 4 s"
            Assert.True(Ticks.FromSeconds(4.0) <= Ticks.FromSeconds(GlobalRules.FieldsAndSummons.MaxLifetimeSec));
            int expire = puddle.Owned.ExpireTick;
            d.RunUntil(() => d.World.Tick > expire + 1, Ticks.FromSeconds(5.0));
            Assert.Empty(d.Runtime.Entities.OfType<TidePuddleEntity>());
            Assert.Contains(d.Events<FieldEvent>(), e => e.Change == FieldChange.Expired && e.DefinitionId == WaterHammer.Id);
            Assert.Equal(0, d.World.Owned.Count(0, OwnedEntityKind.Field));
        }
    }
}
