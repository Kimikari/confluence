using System;
using System.Collections.Generic;
using Confluence.Core.Rules;
using Confluence.Economy.Loadouts;
using Confluence.Sim.Players;

namespace Confluence.Hosting.Economics
{
    /// <summary>What one equipment slot holds when a duel starts.</summary>
    public struct SlotContent
    {
        public EquipmentSlot Slot;

        /// <summary>Catalogue id of the purchased item, the issued gear key (spec L571), or null for an empty slot.</summary>
        public string ItemId;

        /// <summary>Category of a purchased item; null for issued gear and empty slots.</summary>
        public EquipmentCategory? Category;

        /// <summary>The slot holds its plain issued item (Main Focus, Sidearm, Armor, Clothing, Boots; L202, L571).</summary>
        public bool IsIssued;

        /// <summary>Remaining charges of a consumable (L212); 0 otherwise.</summary>
        public int Charges;

        /// <summary>This item's activation is the one enabled active (L575).</summary>
        public bool ActivationEnabled;

        public bool IsEmpty => ItemId == null;

        public override string ToString() => Slot + "=" + (ItemId ?? "-") + (Charges > 0 ? "(" + Charges + ")" : "") + (ActivationEnabled ? "*" : "");
    }

    /// <summary>
    /// A finalized Economy loadout translated for one duel: the Sim setup plus the full per-slot picture (issued gear,
    /// categories, charges) and the augment split into active and dormant.
    /// </summary>
    public sealed class MappedLoadout
    {
        internal MappedLoadout(DuelPlayerSetup setup, IReadOnlyList<SlotContent> slots, int consumableCharges,
            IReadOnlyList<ActiveAugment> activeAugments, IReadOnlyList<string> dormantAugmentIds)
        {
            Setup = setup;
            Slots = slots;
            ConsumableCharges = consumableCharges;
            ActiveAugments = activeAugments;
            DormantAugmentIds = dormantAugmentIds;
        }

        /// <summary>The Sim's view (spawn side, wizard, six spells, equipped items, active augments, enabled active).</summary>
        public DuelPlayerSetup Setup { get; }

        /// <summary>All eight slots in slot order.</summary>
        public IReadOnlyList<SlotContent> Slots { get; }

        /// <summary>Charges of the equipped consumable available for this duel (one purchased charge, L212).</summary>
        public int ConsumableCharges { get; }

        /// <summary>Non-dormant augments with ranks, in augment-slot order.</summary>
        public IReadOnlyList<ActiveAugment> ActiveAugments { get; }

        /// <summary>Dormant augments: they occupy their slot and keep their rank but grant nothing (L247, L641).</summary>
        public IReadOnlyList<string> DormantAugmentIds { get; }

        public SlotContent SlotOf(EquipmentSlot slot) => Slots[(int)slot];
    }

    /// <summary>
    /// Maps an Economy loadout to the Sim's <see cref="DuelPlayerSetup"/> at duel start. Rules applied:
    /// <list type="bullet">
    /// <item>Only equipment slots go to the duel; reserve items stay behind (L210).</item>
    /// <item>A slot holding its issued item is left empty in the Sim setup: the Sim treats an empty Main Focus or Sidearm
    /// as the issued plain focus with ordinary common actions (L571), and issued gear has no bonus (L202).</item>
    /// <item>A spent consumable is an inert shell that supplies nothing (L212, D-074) and is not sent.</item>
    /// <item>The enabled active is sent only when its slot can activate (never the Sidearm, L577).</item>
    /// <item>Only active (non-dormant) augments are sent, with their ranks (L247, L641). Run
    /// <c>DormancyResolver.Resolve</c> on the finalized loadout first (duel start, L249).</item>
    /// </list>
    /// </summary>
    public static class LoadoutMapper
    {
        public static MappedLoadout Map(Loadout loadout, string playerId, int spawnSide)
        {
            if (loadout == null) throw new ArgumentNullException(nameof(loadout));
            if (spawnSide != 0 && spawnSide != 1) throw new ArgumentOutOfRangeException(nameof(spawnSide));
            var setup = new DuelPlayerSetup
            {
                PlayerId = playerId,
                WizardId = loadout.WizardId,
                SpawnSide = spawnSide,
            };
            setup.EquippedSpellIds.AddRange(loadout.EquippedSpellIds);

            OwnedEquipment enabled = loadout.EnabledActive;
            var slots = new SlotContent[SlotRules.AllSlots.Count];
            int consumableCharges = 0;
            foreach (EquipmentSlot slot in SlotRules.AllSlots)
            {
                var content = new SlotContent { Slot = slot };
                OwnedEquipment item = loadout.ItemIn(slot);
                if (item == null)
                {
                    IssuedEquipment issued = IssuedEquipment.For(slot);
                    if (issued != null)
                    {
                        content.ItemId = issued.Key;
                        content.IsIssued = true;
                    }
                }
                else
                {
                    content.ItemId = item.Id;
                    content.Category = item.Definition.Category;
                    content.Charges = item.IsConsumable ? item.Charges : 0;
                    content.ActivationEnabled = enabled != null && enabled.InstanceId == item.InstanceId && SlotRules.SlotAllowsActivation(slot);
                    if (!item.IsSpent)
                    {
                        setup.Equipment.Add(EquippedItem.FromDefinition(item.Definition, slot));
                    }

                    if (slot == EquipmentSlot.Consumable) consumableCharges = Math.Max(0, item.Charges);
                }

                slots[(int)slot] = content;
            }

            if (enabled != null)
            {
                loadout.FindItem(enabled.InstanceId, out Placement placement);
                if (placement.IsSlot && SlotRules.SlotAllowsActivation(placement.Slot)) setup.EnabledActiveItemId = enabled.Id;
            }

            var active = new List<ActiveAugment>();
            var dormant = new List<string>();
            foreach (OwnedAugment augment in loadout.Augments)
            {
                if (augment.IsActive) active.Add(new ActiveAugment(augment.Id, augment.Rank));
                else dormant.Add(augment.Id);
            }

            setup.Augments.AddRange(active);
            return new MappedLoadout(setup, Array.AsReadOnly(slots), consumableCharges, active.AsReadOnly(), dormant.AsReadOnly());
        }
    }
}
