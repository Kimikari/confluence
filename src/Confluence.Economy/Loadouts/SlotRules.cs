using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Core.Rules;

namespace Confluence.Economy.Loadouts
{
    /// <summary>
    /// Which equipment fits which slot (spec L210, L560-L569): Main Focus accepts a wand, staff, or weapon; Sidearm a
    /// wand or weapon; Armor, Clothing, Boots, and Consumable their own category; Trinket A and B a trinket.
    /// Both the record's slot field and its category must agree.
    /// </summary>
    public static class SlotRules
    {
        /// <summary>The eight equipment slots in display order.</summary>
        public static readonly IReadOnlyList<EquipmentSlot> AllSlots = Array.AsReadOnly(new[]
        {
            EquipmentSlot.MainFocus, EquipmentSlot.Sidearm, EquipmentSlot.Armor, EquipmentSlot.Clothing,
            EquipmentSlot.Boots, EquipmentSlot.TrinketA, EquipmentSlot.TrinketB, EquipmentSlot.Consumable,
        });

        public static bool CategoryFits(EquipmentSlot slot, EquipmentCategory category)
        {
            switch (slot)
            {
                case EquipmentSlot.MainFocus: return ContentText.IsFocusCategory(category);
                case EquipmentSlot.Sidearm: return category == EquipmentCategory.Wand || category == EquipmentCategory.Weapon;
                case EquipmentSlot.Armor: return category == EquipmentCategory.Armor;
                case EquipmentSlot.Clothing: return category == EquipmentCategory.MagicalClothing;
                case EquipmentSlot.Boots: return category == EquipmentCategory.Boots;
                case EquipmentSlot.TrinketA:
                case EquipmentSlot.TrinketB: return category == EquipmentCategory.Trinket;
                case EquipmentSlot.Consumable: return category == EquipmentCategory.Consumable;
                default: return false;
            }
        }

        /// <summary>A definition the shop may offer and a slot may hold: valid category, slot field, activation, and tier.</summary>
        public static bool IsLegalDefinition(EquipmentDef def)
        {
            return def != null && def.HasValidCategory && def.HasLegalSlotField && def.HasValidActivation
                && def.Tier >= EquipmentDef.MinTier && def.Tier <= EquipmentDef.MaxTier;
        }

        public static bool Accepts(EquipmentSlot slot, EquipmentDef def)
        {
            return IsLegalDefinition(def) && def.CanOccupy(slot) && CategoryFits(slot, def.Category);
        }

        /// <summary>Slots the definition may occupy, in display order.</summary>
        public static IReadOnlyList<EquipmentSlot> SlotsFor(EquipmentDef def)
        {
            var result = new List<EquipmentSlot>();
            foreach (var slot in AllSlots)
                if (Accepts(slot, def)) result.Add(slot);
            return result;
        }

        /// <summary>
        /// The activation of an active item works only outside the Sidearm: "A sidearm can use the stated common melee
        /// profile and compatible passive effects, but not its special magic activation" (spec L577).
        /// </summary>
        public static bool SlotAllowsActivation(EquipmentSlot slot) => slot != EquipmentSlot.Sidearm && slot != EquipmentSlot.Consumable;
    }

    /// <summary>
    /// Plain issued gear that fills an empty Main Focus, Sidearm, Armor, Clothing, or Boots slot. It has no bonus, no
    /// resale value, and is not one of the 120 catalogue definitions (spec L202, L571). Trinket and Consumable slots have
    /// no issued item and are simply empty.
    /// </summary>
    public sealed class IssuedEquipment
    {
        private static readonly IssuedEquipment[] BySlot =
        {
            new IssuedEquipment(EquipmentSlot.MainFocus, "issued:main_focus", "Plain Focus"),
            new IssuedEquipment(EquipmentSlot.Sidearm, "issued:sidearm", "Plain Sidearm"),
            new IssuedEquipment(EquipmentSlot.Armor, "issued:armor", "Plain Armor"),
            new IssuedEquipment(EquipmentSlot.Clothing, "issued:clothing", "Plain Clothing"),
            new IssuedEquipment(EquipmentSlot.Boots, "issued:boots", "Plain Boots"),
            null,
            null,
            null,
        };

        private IssuedEquipment(EquipmentSlot slot, string key, string name)
        {
            Slot = slot;
            Key = key;
            Name = name;
        }

        public EquipmentSlot Slot { get; }

        /// <summary>Stable non-catalogue key, for presentation and logs.</summary>
        public string Key { get; }

        public string Name { get; }

        public int ResaleValue => GlobalRules.Economy.IssuedGearResaleValue; // spec L202

        public static bool HasIssued(EquipmentSlot slot) => BySlot[(int)slot] != null;

        /// <summary>The issued item for a slot, or null for Trinket A/B and Consumable.</summary>
        public static IssuedEquipment For(EquipmentSlot slot) => BySlot[(int)slot];

        public override string ToString() => Key;
    }
}
