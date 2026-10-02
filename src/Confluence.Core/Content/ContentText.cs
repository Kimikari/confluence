using System;
using System.Collections.Generic;
using Confluence.Core.Rules;

namespace Confluence.Core.Content
{
    /// <summary>How an equipment record is activated (first clause of its "activation" field; spec L575).</summary>
    public enum EquipmentActivationKind
    {
        Passive,
        FocusInput,
        ArmorInput,
        ClothingInput,
        BootsInput,
        TrinketInput,
        ConsumableInput,
    }

    /// <summary>Mappings between catalogue strings and the shared enums. Matching is case-insensitive.</summary>
    public static class ContentText
    {
        private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

        private static readonly Dictionary<string, Rarity> Rarities = new Dictionary<string, Rarity>(Ci)
        {
            { "Silver", Rarity.Silver }, { "Gold", Rarity.Gold }, { "Prismatic", Rarity.Prismatic },
        };

        private static readonly Dictionary<string, DamageChannel> Channels = new Dictionary<string, DamageChannel>(Ci)
        {
            { "none", DamageChannel.None }, { "physical", DamageChannel.Physical }, { "thermal", DamageChannel.Thermal },
            { "electrical", DamageChannel.Electrical }, { "corrosive", DamageChannel.Corrosive }, { "arcane", DamageChannel.Arcane },
        };

        private static readonly Dictionary<string, EquipmentCategory> Categories = new Dictionary<string, EquipmentCategory>(Ci)
        {
            { "Wand", EquipmentCategory.Wand }, { "Staff", EquipmentCategory.Staff }, { "Weapon", EquipmentCategory.Weapon },
            { "Armor", EquipmentCategory.Armor }, { "Magical clothing", EquipmentCategory.MagicalClothing },
            { "Boots", EquipmentCategory.Boots }, { "Trinket", EquipmentCategory.Trinket }, { "Consumable", EquipmentCategory.Consumable },
        };

        private static readonly Dictionary<string, EquipmentSlot> Slots = new Dictionary<string, EquipmentSlot>(Ci)
        {
            { "Main Focus", EquipmentSlot.MainFocus }, { "Sidearm", EquipmentSlot.Sidearm }, { "Armor", EquipmentSlot.Armor },
            { "Clothing", EquipmentSlot.Clothing }, { "Boots", EquipmentSlot.Boots }, { "Trinket A", EquipmentSlot.TrinketA },
            { "Trinket B", EquipmentSlot.TrinketB }, { "Consumable", EquipmentSlot.Consumable },
        };

        // The only legal equipment "slot" strings (spec L210, L560-L569).
        private static readonly Dictionary<string, IReadOnlyList<EquipmentSlot>> SlotStrings = new Dictionary<string, IReadOnlyList<EquipmentSlot>>(StringComparer.Ordinal)
        {
            { "Main Focus or Sidearm", SlotList(EquipmentSlot.MainFocus, EquipmentSlot.Sidearm) },
            { "Main Focus", SlotList(EquipmentSlot.MainFocus) },
            { "Armor", SlotList(EquipmentSlot.Armor) },
            { "Clothing", SlotList(EquipmentSlot.Clothing) },
            { "Boots", SlotList(EquipmentSlot.Boots) },
            { "Trinket A or Trinket B", SlotList(EquipmentSlot.TrinketA, EquipmentSlot.TrinketB) },
            { "Consumable", SlotList(EquipmentSlot.Consumable) },
        };

        private static readonly Dictionary<string, EquipmentActivationKind> Activations = new Dictionary<string, EquipmentActivationKind>(Ci)
        {
            { "Passive", EquipmentActivationKind.Passive }, { "Focus input", EquipmentActivationKind.FocusInput },
            { "Armor input", EquipmentActivationKind.ArmorInput }, { "Clothing input", EquipmentActivationKind.ClothingInput },
            { "Boots input", EquipmentActivationKind.BootsInput }, { "Trinket input", EquipmentActivationKind.TrinketInput },
            { "Consumable input", EquipmentActivationKind.ConsumableInput },
        };

        private static readonly Dictionary<string, PropMass> Masses = new Dictionary<string, PropMass>(Ci)
        {
            { "light", PropMass.Light }, { "medium", PropMass.Medium }, { "heavy", PropMass.Heavy }, { "anchored", PropMass.Anchored },
        };

        public static bool TryParseRarity(string text, out Rarity value) => TryLookup(Rarities, text, out value);

        /// <summary>Catalogue channel strings are lower case ("thermal"); an empty string or "none" means no channel.</summary>
        public static bool TryParseChannel(string text, out DamageChannel value)
        {
            if (text != null && text.Length == 0)
            {
                value = DamageChannel.None;
                return true;
            }
            return TryLookup(Channels, text, out value);
        }

        public static bool TryParseSchool(string text, out School value) => TryParseEnumName(text, out value);

        public static bool TryParseEquipmentCategory(string text, out EquipmentCategory value) => TryLookup(Categories, text, out value);

        /// <summary>Single slot name as used in predicates ("Main Focus", "Armor", "Trinket A").</summary>
        public static bool TryParseSlotName(string text, out EquipmentSlot value) => TryLookup(Slots, text, out value);

        /// <summary>Equipment record "slot" field; only the seven catalogue forms are legal (exact match).</summary>
        public static bool TryParseSlotField(string text, out IReadOnlyList<EquipmentSlot> slots)
        {
            if (text != null && SlotStrings.TryGetValue(text, out var found))
            {
                slots = found;
                return true;
            }
            slots = NoSlots;
            return false;
        }

        private static readonly IReadOnlyList<EquipmentSlot> NoSlots = SlotList();

        private static IReadOnlyList<EquipmentSlot> SlotList(params EquipmentSlot[] slots) => Array.AsReadOnly(slots);

        /// <summary>Reads the activation kind from the leading clause, e.g. "Focus input, 5 mana, 12 s cooldown".</summary>
        public static bool TryParseActivation(string text, out EquipmentActivationKind value)
        {
            value = default;
            if (string.IsNullOrEmpty(text)) return false;
            int end = text.IndexOfAny(new[] { ',', ';', '.', '(' });
            string head = (end < 0 ? text : text.Substring(0, end)).Trim();
            return TryLookup(Activations, head, out value);
        }

        public static bool TryParsePropMass(string text, out PropMass value) => TryLookup(Masses, text, out value);

        public static bool TryParsePropCategory(string text, out PropCategory value) => TryParseEnumName(text, out value);

        public static bool TryParseModuleCategory(string text, out ModuleCategory value) => TryParseEnumName(text, out value);

        public static string RarityName(Rarity rarity) => rarity.ToString();

        public static string ChannelName(DamageChannel channel) => channel.ToString().ToLowerInvariant();

        public static string CategoryName(EquipmentCategory category)
        {
            return category == EquipmentCategory.MagicalClothing ? "Magical clothing" : category.ToString();
        }

        public static string SlotName(EquipmentSlot slot)
        {
            switch (slot)
            {
                case EquipmentSlot.MainFocus: return "Main Focus";
                case EquipmentSlot.TrinketA: return "Trinket A";
                case EquipmentSlot.TrinketB: return "Trinket B";
                default: return slot.ToString();
            }
        }

        /// <summary>The slot field each category must carry (spec L560-L569).</summary>
        public static string ExpectedSlotField(EquipmentCategory category)
        {
            switch (category)
            {
                case EquipmentCategory.Wand:
                case EquipmentCategory.Weapon: return "Main Focus or Sidearm";
                case EquipmentCategory.Staff: return "Main Focus";
                case EquipmentCategory.Armor: return "Armor";
                case EquipmentCategory.MagicalClothing: return "Clothing";
                case EquipmentCategory.Boots: return "Boots";
                case EquipmentCategory.Trinket: return "Trinket A or Trinket B";
                case EquipmentCategory.Consumable: return "Consumable";
                default: throw new ArgumentOutOfRangeException(nameof(category));
            }
        }

        public static bool IsFocusCategory(EquipmentCategory category)
        {
            return category == EquipmentCategory.Wand || category == EquipmentCategory.Staff || category == EquipmentCategory.Weapon;
        }

        private static bool TryLookup<T>(Dictionary<string, T> table, string text, out T value)
        {
            if (text != null && table.TryGetValue(text, out value)) return true;
            value = default;
            return false;
        }

        private static bool TryParseEnumName<T>(string text, out T value) where T : struct
        {
            value = default;
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text)
                if (!char.IsLetter(c)) return false; // reject numeric strings and combined flags
            return Enum.TryParse(text, true, out value) && Enum.IsDefined(typeof(T), value);
        }
    }
}
