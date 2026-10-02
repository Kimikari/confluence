using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Effects;
using Confluence.Sim.Effects.Content;
using Confluence.Sim.Players;
using Confluence.Sim.Tests.World;

namespace Confluence.Sim.Tests.Effects.Content
{
    /// <summary>
    /// Tier-1 equipment and unconditional Silver augments built on the effect framework, end to end in a duel built
    /// from catalogue loadouts: effect, drawback, and the global caps that bound them.
    /// </summary>
    public class ProofContentTests
    {
        private static DuelWorld WithItems(params KeyValuePair<string, EquipmentSlot>[] items) =>
            WorldDuels.Duel(a: WorldDuels.SetupWith("A", equipment: items));

        private static DuelWorld WithAugment(string id, int rank, DuelPlayerSetup setup = null, int alcoveTicks = 0)
        {
            DuelPlayerSetup a = setup ?? WorldDuels.SetupWith("A");
            a.Augments.Add(new ActiveAugment(id, rank));
            return WorldDuels.Duel(a: a, alcoveTicks: alcoveTicks);
        }

        [Fact]
        public void The_Proof_Records_Are_Registered_With_Their_Catalogue_Text()
        {
            Catalogue cat = WorldDuels.Catalogue;
            Assert.Equal("Maximum health increases 15.", cat.Equipment(TrainingCuirass.Id).Effect);
            Assert.Equal("Sprint stamina consumption increases 1/s.", cat.Equipment(TrainingCuirass.Id).Drawback);
            Assert.Equal("Maximum mana increases 8.", cat.Equipment(PocketReservoir.Id).Effect);
            Assert.Equal("Maximum stamina decreases 4.", cat.Equipment(PocketReservoir.Id).Drawback);
            Assert.Equal("Mana regeneration increases 8%, within +40% cap.", cat.Equipment(ApprenticeMantle.Id).Effect);
            Assert.Equal("Maximum health decreases 8.", cat.Equipment(ApprenticeMantle.Id).Drawback);
            Assert.Equal("Maximum mana increases 12.", cat.Augment(LargeVessel.Id).Rank2);
            Assert.Equal("Maximum health increases 20.", cat.Augment(HealthyHabit.Id).Rank2);
            Assert.Equal("Gain 20 shield until broken.", cat.Augment(OpeningWard.Id).Rank2);
            Assert.Equal("Round starts.", cat.Augment(OpeningWard.Id).Condition);
            foreach (string id in new[] { TrainingCuirass.Id, PocketReservoir.Id, ApprenticeMantle.Id })
            {
                Assert.True(ContentBehaviorIndex.Default.TryGetEquipment(id, out _), id);
                Assert.Equal(1, cat.Equipment(id).Tier);
            }

            foreach (string id in new[] { LargeVessel.Id, HealthyHabit.Id, OpeningWard.Id })
            {
                Assert.True(ContentBehaviorIndex.Default.TryGetAugment(id, out _), id);
                Assert.Equal("Silver", cat.Augment(id).Rarity);
            }
        }

        [Fact]
        [Verifies("EQP-051")]
        public void Training_Cuirass_Adds_15_Maximum_Health_And_Costs_1_More_Stamina_Per_Sprint_Second()
        {
            DuelWorld world = WithItems(WorldDuels.Item(TrainingCuirass.Id, EquipmentSlot.Armor));
            DuelPlayer p = world.Player(0);
            Assert.Equal(315f, p.Resources.MaxHealth, 3);
            Assert.Equal(315f, p.Resources.Health, 3);
            WorldDuels.Step(world, Ticks.PerSecond, WorldDuels.Sprint(0f), TestWorlds.B);
            Assert.Equal(100f - 13f, p.Resources.Stamina, 2); // 12/s + 1/s drawback (spec L80)
            Assert.Equal(300f, world.Player(1).Resources.MaxHealth, 3);

            // Maximum health stays inside the 450 cap (spec L114).
            DuelPlayerSetup big = WorldDuels.SetupWith("A", equipment: new[] { WorldDuels.Item(TrainingCuirass.Id, EquipmentSlot.Armor) });
            big.Modifiers.Add(new StatModifier(StatKind.MaxHealthBonus, 140f, "EQP-TEST"));
            DuelWorld capped = WorldDuels.Duel(a: big);
            Assert.Equal(GlobalRules.Resources.MaxHealthCap, capped.Player(0).Resources.MaxHealth, 3);
            StatLine line = capped.Player(0).Stats.InspectAll().Single(l => l.Stat == StatKind.MaxHealthBonus);
            Assert.Equal(455f, line.Raw, 3);
            Assert.True(line.Capped);
        }

        [Fact]
        [Verifies("EQP-096")]
        public void Pocket_Reservoir_Adds_8_Maximum_Mana_And_Removes_4_Maximum_Stamina()
        {
            DuelWorld world = WithItems(WorldDuels.Item(PocketReservoir.Id, EquipmentSlot.TrinketA));
            DuelPlayer p = world.Player(0);
            Assert.Equal(108f, p.Resources.MaxMana, 3);
            Assert.Equal(108f, p.Resources.Mana, 3);      // static capacity is filled at round start (spec L579)
            Assert.Equal(96f, p.Resources.MaxStamina, 3);
            Assert.Equal(96f, p.Resources.Stamina, 3);
            Assert.Equal(GlobalRules.Resources.ManaRegenPerSecond, p.Stats.ManaRegenPerSecond, 3); // capacity, not regeneration

            WorldDuels.Idle(world, 1);
            Assert.True(world.TryCommitMana(0, 108f, "SPL-TEST")); // the extra reserve pays a full commitment (spec L78)
            Assert.False(world.TryCommitMana(1, 101f, "SPL-TEST"));
        }

        [Fact]
        [Verifies("EQP-066")]
        public void Apprentice_Mantle_Adds_8_Percent_Mana_Regeneration_Within_The_14_Per_Second_Cap_And_Removes_8_Health()
        {
            DuelWorld world = WithItems(WorldDuels.Item(ApprenticeMantle.Id, EquipmentSlot.Clothing));
            DuelPlayer p = world.Player(0);
            Assert.Equal(292f, p.Resources.MaxHealth, 3);
            Assert.Equal(10.8f, p.Stats.ManaRegenPerSecond, 3);
            WorldDuels.Idle(world, 1);
            Assert.True(world.TryCommitMana(0, 50f, "SPL-TEST"));
            WorldDuels.Idle(world, Ticks.FromSeconds(GlobalRules.Resources.ManaRegenDelaySec) - 1);
            Assert.Equal(50f, p.Resources.Mana, 3);
            WorldDuels.IdleSeconds(world, 2f);
            Assert.Equal(50f + 2f * 10.8f, p.Resources.Mana, 1);

            // Within the +40% cap: with other regeneration bonuses the total stops at 14/s (spec L78).
            DuelPlayerSetup stacked = WorldDuels.SetupWith("A", equipment: new[] { WorldDuels.Item(ApprenticeMantle.Id, EquipmentSlot.Clothing) });
            stacked.Modifiers.Add(new StatModifier(StatKind.ManaRegenPercent, 0.4f, "AUG-147"));
            DuelWorld capped = WorldDuels.Duel(a: stacked);
            Assert.Equal(GlobalRules.Resources.ManaRegenCapPerSecond, capped.Player(0).Stats.ManaRegenPerSecond, 3);
            StatLine line = capped.Player(0).Stats.InspectAll().Single(l => l.Stat == StatKind.ManaRegenPercent);
            Assert.Equal(14.8f, line.Raw, 3);
            Assert.True(line.Capped);
        }

        [Theory]
        [InlineData(1, 108f)]
        [InlineData(2, 112f)]
        [Verifies("AUG-145")]
        public void Large_Vessel_Raises_Maximum_Mana_By_Rank_And_Lowers_Maximum_Stamina_By_4(int rank, float maxMana)
        {
            DuelWorld world = WithAugment(LargeVessel.Id, rank);
            DuelPlayer p = world.Player(0);
            Assert.Equal(maxMana, p.Resources.MaxMana, 3);
            Assert.Equal(maxMana, p.Resources.Mana, 3);
            Assert.Equal(96f, p.Resources.MaxStamina, 3);
            Assert.Equal(GlobalRules.Resources.ManaRegenPerSecond, p.Stats.ManaRegenPerSecond, 3);
        }

        [Theory]
        [InlineData(1, 312f)]
        [InlineData(2, 320f)]
        [Verifies("AUG-169")]
        public void Healthy_Habit_Raises_Maximum_Health_By_Rank_And_Lowers_Maximum_Mana_By_3(int rank, float maxHealth)
        {
            DuelWorld world = WithAugment(HealthyHabit.Id, rank);
            DuelPlayer p = world.Player(0);
            Assert.Equal(maxHealth, p.Resources.MaxHealth, 3);
            Assert.Equal(maxHealth, p.Resources.Health, 3);
            Assert.Equal(97f, p.Resources.MaxMana, 3);
            WorldDuels.Idle(world, 1);
            Assert.False(world.TryCommitMana(0, 98f, "SPL-TEST")); // the drawback: a smaller reserve
        }

        [Fact]
        [Verifies("AUG-169")]
        public void Healthy_Habit_Stays_Inside_The_450_Maximum_Health_Cap()
        {
            DuelPlayerSetup a = WorldDuels.SetupWith("A");
            a.Modifiers.Add(new StatModifier(StatKind.MaxHealthBonus, 140f, "EQP-TEST"));
            DuelWorld world = WithAugment(HealthyHabit.Id, 2, a);
            Assert.Equal(GlobalRules.Resources.MaxHealthCap, world.Player(0).Resources.MaxHealth, 3);
            Assert.Equal(460f, world.Player(0).Stats.RawMaxHealth, 3);
        }

        [Theory]
        [InlineData(1, 12f)]
        [InlineData(2, 20f)]
        [Verifies("AUG-175")]
        public void Opening_Ward_Issues_A_Reserve_Shield_At_Round_Start_That_Lasts_Until_Broken_And_Removes_5_Health(int rank, float shield)
        {
            DuelWorld world = WithAugment(OpeningWard.Id, rank, alcoveTicks: Ticks.FromSeconds(GlobalRules.Round.AlcoveSec));
            DuelPlayer p = world.Player(0);
            Assert.Equal(295f, p.Resources.MaxHealth, 3);
            ShieldEntry ward = Assert.Single(p.Shields.Entries);
            Assert.True(ward.Reserve);
            Assert.Equal(OpeningWard.Id, ward.SourceId);
            Assert.Equal(shield, ward.Amount, 3);

            WorldDuels.IdleSeconds(world, GlobalRules.Round.AlcoveSec + GlobalRules.Shields.MaxTimedDurationSec + 1f);
            Assert.Equal(shield, p.Shields.Total, 3); // reserve shields do not expire with the 8 s timed rule

            world.ApplyDamage(TestWorlds.Hit(world, 1, 0, shield + 10f));
            Assert.Equal(0f, p.Shields.Total, 3);
            Assert.Equal(285f, p.Resources.Health, 3);
            WorldDuels.IdleSeconds(world, 10f);
            Assert.Equal(0f, p.Shields.Total, 3); // never regenerates for free (spec L114)
            Assert.Single(WorldDuels.Events<EffectEvent>(world), e => e.SourceId == OpeningWard.Id && e.Change == EffectChange.Activated);
        }

        [Fact]
        [Verifies("AUG-175")]
        public void Opening_Ward_Shares_The_100_Shield_Cap_With_Other_Reserve_Shields()
        {
            DuelPlayerSetup a = WorldDuels.SetupWith("A");
            a.ReserveShields.Add(new ReserveShieldGrant("EQP-TEST", 95f));
            DuelWorld world = WithAugment(OpeningWard.Id, 2, a);
            Assert.Equal(GlobalRules.Shields.Cap, world.Player(0).Shields.Total, 3);
            Assert.Equal(5f, world.Player(0).Shields.Entries.Single(e => e.SourceId == OpeningWard.Id).Amount, 3);
        }

        [Fact]
        public void A_Full_Loadout_Of_The_Proof_Records_Combines_Under_The_Caps()
        {
            DuelPlayerSetup a = WorldDuels.SetupWith("A", equipment: new[]
            {
                WorldDuels.Item(TrainingCuirass.Id, EquipmentSlot.Armor),
                WorldDuels.Item(PocketReservoir.Id, EquipmentSlot.TrinketA),
                WorldDuels.Item(ApprenticeMantle.Id, EquipmentSlot.Clothing),
            }, augments: new[] { new ActiveAugment(LargeVessel.Id, 1), new ActiveAugment(HealthyHabit.Id, 2), new ActiveAugment(OpeningWard.Id, 1) });
            DuelWorld world = WorldDuels.Duel(a: a);
            DuelPlayer p = world.Player(0);
            Assert.Equal(300f + 15f - 8f + 20f - 5f, p.Resources.MaxHealth, 3);
            Assert.Equal(100f + 8f + 8f - 3f, p.Resources.MaxMana, 3);
            Assert.Equal(100f - 4f - 4f, p.Resources.MaxStamina, 3);
            Assert.Equal(10.8f, p.Stats.ManaRegenPerSecond, 3);
            Assert.Equal(12f, p.Shields.ReserveTotal, 3);
        }
    }
}
