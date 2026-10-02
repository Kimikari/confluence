using System.Linq;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Economy;
using Confluence.Economy.Augments;
using Confluence.Economy.Eligibility;
using Confluence.Economy.Loadouts;
using Confluence.Hosting.Economics;

namespace Confluence.Hosting.Tests
{
    /// <summary>Economy loadout to Sim duel setup (spec L46, L202, L210, L212, L247, L571-L577, L641).</summary>
    public sealed class LoadoutMapperTests
    {
        private static Catalogue Cat => HostFixture.Catalogue;
        private static readonly IStructuralChangeSource NoChanges = new StructuralChangeTable();

        private static OwnedEquipment Give(Loadout loadout, EquipmentDef def, Placement? at = null)
        {
            Assert.Equal(TransactionCode.Success, loadout.AddItem(def, def.Price, at, out OwnedEquipment item));
            return item;
        }

        [Fact]
        public void Maps_Spells_Slots_Issued_Gear_Consumable_Charges_And_The_Enabled_Active()
        {
            var loadout = new Loadout(Cat, "WIZ-002");
            EquipmentDef consumable = Cat.EquipmentItems.First(d => d.IsConsumable && SlotRules.IsLegalDefinition(d));
            EquipmentDef active = Cat.EquipmentItems.First(d => d.IsActive && !d.IsConsumable && SlotRules.IsLegalDefinition(d) &&
                SlotRules.SlotsFor(d).Any(SlotRules.SlotAllowsActivation));
            EquipmentSlot activeSlot = SlotRules.SlotsFor(active).First(SlotRules.SlotAllowsActivation);
            OwnedEquipment potion = Give(loadout, consumable, Placement.In(EquipmentSlot.Consumable));
            OwnedEquipment trinket = Give(loadout, active, Placement.In(activeSlot));
            Assert.Equal(TransactionCode.Success, loadout.SetEnabledActive(trinket.InstanceId));

            MappedLoadout mapped = LoadoutMapper.Map(loadout, "P3", 1);
            Assert.Equal("P3", mapped.Setup.PlayerId);
            Assert.Equal("WIZ-002", mapped.Setup.WizardId);
            Assert.Equal(1, mapped.Setup.SpawnSide);
            Assert.Equal(loadout.EquippedSpellIds, mapped.Setup.EquippedSpellIds);
            Assert.Equal(active.Id, mapped.Setup.EnabledActiveItemId);
            Assert.Equal(activeSlot, mapped.Setup.ItemIn(activeSlot).Slot);
            Assert.Equal(active.Category, mapped.Setup.ItemIn(activeSlot).Category);
            Assert.True(mapped.SlotOf(activeSlot).ActivationEnabled);

            // Consumable: one purchased charge (L212).
            Assert.Equal(potion.Charges, mapped.ConsumableCharges);
            Assert.True(mapped.ConsumableCharges > 0);
            Assert.Equal(consumable.Id, mapped.Setup.ItemIn(EquipmentSlot.Consumable).Id);

            // Every other slot holds its issued plain item, which the Sim treats as an empty slot (L202, L571).
            Assert.Equal(SlotRules.AllSlots.Count, mapped.Slots.Count);
            foreach (EquipmentSlot slot in SlotRules.AllSlots)
            {
                SlotContent content = mapped.SlotOf(slot);
                if (slot == activeSlot || slot == EquipmentSlot.Consumable) continue;
                Assert.Null(mapped.Setup.ItemIn(slot));
                Assert.Equal(IssuedEquipment.HasIssued(slot), content.IsIssued);
                if (content.IsIssued) Assert.Equal(IssuedEquipment.For(slot).Key, content.ItemId);
            }

            // A spent consumable is an inert shell: it keeps the slot but supplies nothing (L212).
            Assert.Equal(TransactionCode.Success, loadout.SpendConsumableCharge());
            MappedLoadout spent = LoadoutMapper.Map(loadout, "P3", 0);
            Assert.Null(spent.Setup.ItemIn(EquipmentSlot.Consumable));
            Assert.Equal(consumable.Id, spent.SlotOf(EquipmentSlot.Consumable).ItemId);
            Assert.Equal(0, spent.ConsumableCharges);
        }

        [Fact]
        public void Reserve_Items_Stay_Behind_And_Dormant_Augments_Grant_Nothing()
        {
            // AUG-006 needs an equipped mastery item (EQP-002) and round 3 (the Economy dormancy scenario).
            var loadout = new Loadout(Cat, "WIZ-001");
            OwnedEquipment glass = Give(loadout, Cat.Equipment("EQP-002"));
            AcquisitionResult granted = AugmentAcquisition.AcquireNew(loadout, Cat.Augment("AUG-006"), new RoundInfo(MatchMode.Conclave, 3), null, null, NoChanges);
            Assert.True(granted.Succeeded);

            MappedLoadout active = LoadoutMapper.Map(loadout, "P0", 0);
            Assert.Contains(active.Setup.Augments, a => a.Id == "AUG-006" && a.Rank == 1);
            Assert.Contains(active.Setup.Equipment, e => e.Id == "EQP-002");

            Assert.Equal(TransactionCode.Success, loadout.Move(glass.InstanceId, Placement.Reserve));
            DormancyResolver.Resolve(loadout, NoChanges); // duel start (L249)
            MappedLoadout dormant = LoadoutMapper.Map(loadout, "P0", 0);
            Assert.DoesNotContain(dormant.Setup.Equipment, e => e.Id == "EQP-002");           // reserve stays behind (L210)
            Assert.DoesNotContain(dormant.Setup.Augments, a => a.Id == "AUG-006");          // dormant: no effect (L247)
            Assert.Equal(new[] { "AUG-006" }, dormant.DormantAugmentIds);
            Assert.Empty(dormant.ActiveAugments);
        }
    }
}
