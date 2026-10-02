using System;
using System.Collections.Generic;
using Confluence.Core.Rules;

namespace Confluence.Core.Content
{
    /// <summary>One of the 120 shared equipment definitions (spec §5 "Equipment shop", "Shared equipment rules").</summary>
    public sealed class EquipmentDef : ContentDef
    {
        public const int MinTier = 1;
        public const int MaxTier = 4;

        private readonly EquipmentCategory _category;
        private readonly bool _categoryValid;
        private readonly EquipmentActivationKind _activationKind;
        private readonly bool _activationValid;

        internal EquipmentDef(RecordReader r)
            : base(r.RequireId(), r.String("name"), r.StringList("tags"), r.Json)
        {
            CategoryText = r.String("category");
            Slot = r.String("slot");
            Tier = r.Int("tier");
            Effect = r.String("effect");
            Activation = r.String("activation");
            Drawback = r.String("drawback");
            UniqueGroup = r.String("unique_group");
            Counterplay = r.String("counterplay");
            r.Finish();

            _categoryValid = ContentText.TryParseEquipmentCategory(CategoryText, out _category);
            HasLegalSlotField = ContentText.TryParseSlotField(Slot, out var slots);
            AllowedSlots = slots;
            _activationValid = ContentText.TryParseActivation(Activation, out _activationKind);
        }

        public override ContentType Type => ContentType.Equipment;

        /// <summary>Category as written ("Magical clothing").</summary>
        public string CategoryText { get; }

        /// <summary>Slot field as written ("Main Focus or Sidearm"). See <see cref="AllowedSlots"/>.</summary>
        public string Slot { get; }

        public int Tier { get; }
        public string Effect { get; }

        /// <summary>Activation text: "Passive" or the logical input plus cost, cooldown, and charge behavior.</summary>
        public string Activation { get; }

        public string Drawback { get; }
        public string UniqueGroup { get; }
        public string Counterplay { get; }

        public bool HasValidCategory => _categoryValid;
        public bool HasLegalSlotField { get; }
        public bool HasValidActivation => _activationValid;

        public EquipmentCategory Category
        {
            get
            {
                if (!_categoryValid) throw new InvalidOperationException(Id + " has unrecognised category '" + CategoryText + "'.");
                return _category;
            }
        }

        /// <summary>Slots this definition may occupy; empty when the slot field is not one of the legal forms.</summary>
        public IReadOnlyList<EquipmentSlot> AllowedSlots { get; }

        public bool CanOccupy(EquipmentSlot slot)
        {
            for (int i = 0; i < AllowedSlots.Count; i++)
                if (AllowedSlots[i] == slot) return true;
            return false;
        }

        public EquipmentActivationKind ActivationKind
        {
            get
            {
                if (!_activationValid) throw new InvalidOperationException(Id + " has unrecognised activation '" + Activation + "'.");
                return _activationKind;
            }
        }

        /// <summary>Uses the single equipment-active key: not passive and not a consumable (spec L575).</summary>
        public bool IsActive => ActivationKind != EquipmentActivationKind.Passive && ActivationKind != EquipmentActivationKind.ConsumableInput;

        public bool IsConsumable => Category == EquipmentCategory.Consumable;

        /// <summary>Wands, staffs, and weapons can occupy the Main Focus slot (spec L210).</summary>
        public bool IsFocusCategory => ContentText.IsFocusCategory(Category);

        public bool HasUniqueGroup => UniqueGroup.Length > 0;

        /// <summary>Crown price of this definition's tier.</summary>
        public int Price
        {
            get
            {
                if (Tier < MinTier || Tier > MaxTier) throw new InvalidOperationException(Id + " has invalid tier " + Tier + ".");
                return PriceForTier(Tier);
            }
        }

        /// <summary>Tier prices 200/400/600/800 (spec L195-L200).</summary>
        public static int PriceForTier(int tier)
        {
            switch (tier)
            {
                case 1: return 200;
                case 2: return 400;
                case 3: return 600;
                case 4: return 800;
                default: throw new ArgumentOutOfRangeException(nameof(tier), "Equipment tiers are 1-4.");
            }
        }
    }
}
