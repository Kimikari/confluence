using System.Linq;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Economy.Augments;
using Confluence.Economy.Eligibility;
using Confluence.Economy.Loadouts;
using static Confluence.Economy.Tests.TestData;

namespace Confluence.Economy.Tests
{
    /// <summary>Content ownership, slots, Unique groups, the active-item choice, and consumable charges (spec L210-L212, L292, L521).</summary>
    public class LoadoutTests
    {
        [Fact]
        public void EveryWizardStartsWithItsFirstSixSpellsAndIssuedGear()
        {
            foreach (var wizard in Cat.Wizards)
            {
                var l = new Loadout(Cat, wizard.Id);
                Assert.Equal(wizard.SpellIds.Take(6), l.EquippedSpellIds);
                Assert.Equal(8, l.KitSpellIds.Count);
                Assert.All(l.KitSpellIds, id => Assert.Equal(wizard.Id, Cat.Spell(id).WizardId));
                Assert.Empty(l.AllItems);
                Assert.True(l.HoldsIssued(EquipmentSlot.MainFocus));
                Assert.True(l.HoldsIssued(EquipmentSlot.Armor));
                Assert.False(l.HoldsIssued(EquipmentSlot.TrinketA)); // no issued trinket (spec L571)
                Assert.Empty(l.Validate());
            }
        }

        [Fact]
        public void OnlyOwnKitSpellsCanBeEquippedAndTheyMustBeSixDistinct()
        {
            var l = Loadout("WIZ-002");
            Assert.Equal(TransactionCode.Success, l.SetEquippedSpells(new[] { "SPL-016", "SPL-015", "SPL-009", "SPL-010", "SPL-011", "SPL-012" }));
            Assert.Equal("SPL-016", l.EquippedSpellIds[0]);

            // Another wizard's spell is never equippable (spec L292 "no cross-wizard spell theft").
            Assert.Equal(TransactionCode.SpellNotInKit, l.SetEquippedSpells(new[] { "SPL-001", "SPL-015", "SPL-009", "SPL-010", "SPL-011", "SPL-012" }));
            Assert.Equal(TransactionCode.DuplicateSpell, l.SetEquippedSpells(new[] { "SPL-009", "SPL-009", "SPL-010", "SPL-011", "SPL-012", "SPL-013" }));
            Assert.Equal(TransactionCode.InvalidSpellSelection, l.SetEquippedSpells(new[] { "SPL-009", "SPL-010", "SPL-011" }));
            Assert.Equal(TransactionCode.InvalidSpellSelection, l.SetEquippedSpells(null));
            Assert.Equal("SPL-016", l.EquippedSpellIds[0]); // failed requests changed nothing
        }

        [Fact]
        public void MainFocusAcceptsWandStaffWeaponAndSidearmOnlyWandOrWeapon()
        {
            var l = Loadout("WIZ-001");
            var staff = Cat.Equipment("EQP-016");
            var wand = Cat.Equipment("EQP-001");
            var weapon = Cat.Equipment("EQP-031");
            Assert.Equal(TransactionCode.SlotRejectsItem, l.CheckAdd(staff, Placement.In(EquipmentSlot.Sidearm), out _));
            Assert.Equal(TransactionCode.Success, l.CheckAdd(staff, Placement.In(EquipmentSlot.MainFocus), out _));
            Assert.Equal(TransactionCode.Success, l.CheckAdd(wand, Placement.In(EquipmentSlot.Sidearm), out _));
            Assert.Equal(TransactionCode.Success, l.CheckAdd(weapon, Placement.In(EquipmentSlot.Sidearm), out _));
            Assert.Equal(TransactionCode.SlotRejectsItem, l.CheckAdd(Cat.Equipment("EQP-051"), Placement.In(EquipmentSlot.Clothing), out _));
            Assert.Equal(TransactionCode.SlotRejectsItem, l.CheckAdd(Cat.Equipment("EQP-096"), Placement.In(EquipmentSlot.Consumable), out _));

            // Automatic placement: a focus takes the Main Focus first, then the Sidearm.
            var first = Give(l, "EQP-031");
            var second = Give(l, "EQP-032");
            Assert.Same(first, l.ItemIn(EquipmentSlot.MainFocus));
            Assert.Same(second, l.ItemIn(EquipmentSlot.Sidearm));
            Assert.False(l.HoldsIssued(EquipmentSlot.MainFocus));
        }

        [Fact]
        public void ADefinitionCannotOccupyBothFociOrBothTrinketSlots()
        {
            var l = Loadout("WIZ-001");
            Give(l, "EQP-001", Placement.In(EquipmentSlot.MainFocus));
            Assert.Equal(TransactionCode.DuplicateEquippedDefinition, l.CheckAdd(Cat.Equipment("EQP-001"), Placement.In(EquipmentSlot.Sidearm), out _));

            Give(l, "EQP-096", Placement.In(EquipmentSlot.TrinketA));
            Assert.Equal(TransactionCode.DuplicateEquippedDefinition, l.CheckAdd(Cat.Equipment("EQP-096"), Placement.In(EquipmentSlot.TrinketB), out _));
            Assert.Equal(TransactionCode.Success, l.CheckAdd(Cat.Equipment("EQP-097"), Placement.In(EquipmentSlot.TrinketB), out _));

            // A second copy is legal to own: automatic placement puts it in reserve (spec L585 "two copies").
            var copy = Give(l, "EQP-096");
            Assert.Contains(copy, l.Reserve);
            Assert.Equal(TransactionCode.DuplicateEquippedDefinition, l.Move(copy.InstanceId, Placement.In(EquipmentSlot.TrinketB)));
            Assert.Empty(l.Validate());
        }

        [Fact]
        public void ReserveHoldsThreeItems()
        {
            var l = Loadout("WIZ-001");
            Give(l, "EQP-096", Placement.Reserve);
            Give(l, "EQP-097", Placement.Reserve);
            Give(l, "EQP-098", Placement.Reserve);
            Assert.Equal(TransactionCode.ReserveFull, l.CheckAdd(Cat.Equipment("EQP-099"), Placement.Reserve, out _));
            // Slots still free: automatic placement uses them.
            Assert.Equal(TransactionCode.Success, l.CheckAdd(Cat.Equipment("EQP-099"), null, out var where));
            Assert.Equal(Placement.In(EquipmentSlot.TrinketA), where);
            Give(l, "EQP-099");
            Give(l, "EQP-100");
            Assert.Equal(TransactionCode.NoFreeSpace, l.CheckAdd(Cat.Equipment("EQP-101"), null, out _));
        }

        [Fact]
        public void MoveSwapsAndRejectsIllegalArrangementsAtomically()
        {
            var l = Loadout("WIZ-001");
            var wand = Give(l, "EQP-001", Placement.In(EquipmentSlot.MainFocus));
            var staff = Give(l, "EQP-016", Placement.Reserve);
            // Staff into the Main Focus swaps the wand to the reserve slot the staff left.
            Assert.Equal(TransactionCode.Success, l.Move(staff.InstanceId, Placement.In(EquipmentSlot.MainFocus)));
            Assert.Same(staff, l.ItemIn(EquipmentSlot.MainFocus));
            Assert.Contains(wand, l.Reserve);
            // The staff cannot go to the Sidearm; nothing moves.
            Assert.Equal(TransactionCode.SlotRejectsItem, l.Move(staff.InstanceId, Placement.In(EquipmentSlot.Sidearm)));
            Assert.Same(staff, l.ItemIn(EquipmentSlot.MainFocus));
            // Swapping the wand into the Main Focus would push the staff into the reserve: legal.
            Assert.Equal(TransactionCode.Success, l.Move(wand.InstanceId, Placement.In(EquipmentSlot.MainFocus)));
            Assert.Contains(staff, l.Reserve);
            Assert.Equal(TransactionCode.UnknownItem, l.Move(999, Placement.Reserve));
        }

        [Fact]
        public void AtMostOneActiveIsEnabledAndOthersAreLabeledDormant()
        {
            var l = Loadout("WIZ-001");
            var forked = Give(l, "EQP-007");   // wand, Focus input active -> Main Focus, enabled by default
            var aegis = Give(l, "EQP-062");    // armor, Armor input active
            var veil = Give(l, "EQP-077");     // clothing, Clothing input active
            Assert.Same(forked, l.EnabledActive);
            Assert.True(l.IsActivationEnabled(forked));
            Assert.True(l.IsDormantActive(aegis));
            Assert.True(l.IsDormantActive(veil));
            Assert.StartsWith("Dormant", l.ActiveStatusLabel(aegis));

            Assert.Equal(TransactionCode.Success, l.SetEnabledActive(aegis.InstanceId));
            Assert.Same(aegis, l.EnabledActive);
            Assert.True(l.IsDormantActive(forked));
            Assert.Equal(1, l.EquippedItems.Count(i => l.IsActivationEnabled(i)));

            Assert.Equal(TransactionCode.Success, l.SetEnabledActive(null));
            Assert.Null(l.EnabledActive);

            var passive = Give(l, "EQP-096");
            Assert.Equal(TransactionCode.NotAnActiveItem, l.SetEnabledActive(passive.InstanceId));
            var pocketRain = Give(l, "EQP-116");
            Assert.Equal(TransactionCode.NotAnActiveItem, l.SetEnabledActive(pocketRain.InstanceId)); // consumables use their own key
        }

        [Fact]
        public void ASidearmCannotUseItsActivation()
        {
            var l = Loadout("WIZ-001");
            Give(l, "EQP-031", Placement.In(EquipmentSlot.MainFocus));
            var forked = Give(l, "EQP-007", Placement.In(EquipmentSlot.Sidearm));
            Assert.Null(l.EnabledActive); // not auto-enabled from the Sidearm (spec L577)
            Assert.Equal(TransactionCode.ActiveItemNotEnableable, l.SetEnabledActive(forked.InstanceId));
            Assert.Contains("Sidearm", l.ActiveStatusLabel(forked));

            // Moving the enabled active into the reserve clears the choice.
            var l2 = Loadout("WIZ-001");
            var aegis = Give(l2, "EQP-062");
            Assert.Same(aegis, l2.EnabledActive);
            Assert.Equal(TransactionCode.Success, l2.Move(aegis.InstanceId, Placement.Reserve));
            Assert.Null(l2.EnabledActive);
            Assert.Equal(TransactionCode.ActiveItemNotEnableable, l2.SetEnabledActive(aegis.InstanceId));
        }

        [Fact]
        public void ConsumablesCarryOnePurchasedChargeEachAndSpentOnesAreInert()
        {
            var l = Loadout("WIZ-002");
            var rain = Give(l, "EQP-116");
            var spare = Give(l, "EQP-116"); // second purchase: reserve, its own charge
            Assert.Same(rain, l.ItemIn(EquipmentSlot.Consumable));
            Assert.Contains(spare, l.Reserve);
            Assert.Equal(2, l.HeldConsumableCharges);
            Assert.Equal(200, rain.SellValue); // 50% of 400 (spec L212)

            Assert.Equal(TransactionCode.Success, l.SpendConsumableCharge());
            Assert.True(rain.IsSpent);
            Assert.Equal(0, rain.SellValue); // a used consumable sells for zero
            Assert.Equal(1, l.HeldConsumableCharges);
            Assert.Equal(TransactionCode.NoConsumableCharge, l.SpendConsumableCharge());
            Assert.False(EquipmentLiveness.IsPresent(rain));
            Assert.False(EquipmentLiveness.SuppliesTag(l, rain, "wet"));
        }

        [Fact]
        public void IssuedGearHasNoResaleAndIsNotAnItem()
        {
            Assert.Equal(0, IssuedEquipment.For(EquipmentSlot.MainFocus).ResaleValue);
            Assert.Null(IssuedEquipment.For(EquipmentSlot.TrinketB));
            Assert.Equal("issued:armor", IssuedEquipment.For(EquipmentSlot.Armor).Key);
            Assert.False(Cat.Exists(IssuedEquipment.For(EquipmentSlot.Armor).Key)); // outside the 120-item catalogue
        }

        [Fact]
        public void AnOpenDraftFreezesEquipmentAndSpells()
        {
            var p = Player("WIZ-001");
            var item = Give(p.Loadout, "EQP-096");
            Assert.True(AugmentDraft.TryOpen(p, Conclave1, 7UL, ContentAvailability.All, out var draft, NoChanges));
            Assert.True(p.Loadout.IsFrozen); // spec L227
            Assert.Equal(TransactionCode.LoadoutFrozen, p.Loadout.SetEquippedSpells(p.Loadout.KitSpellIds.Skip(2).ToArray()));
            Assert.Equal(TransactionCode.LoadoutFrozen, p.Loadout.Move(item.InstanceId, Placement.Reserve));
            Assert.Equal(TransactionCode.LoadoutFrozen, p.Loadout.RemoveItem(item.InstanceId, out _));
            Assert.Equal(TransactionCode.LoadoutFrozen, p.Loadout.CheckAdd(Cat.Equipment("EQP-097"), null, out _));
            Assert.Equal(TransactionCode.LoadoutFrozen, p.Loadout.SetEnabledActive(null));
            draft.Pass(1);
            Assert.False(p.Loadout.IsFrozen);
            Assert.Equal(TransactionCode.Success, p.Loadout.Move(item.InstanceId, Placement.Reserve));
        }
    }
}
