using System;
using Confluence.Core.Content;
using Confluence.Core.Rules;

namespace Confluence.Economy.Loadouts
{
    /// <summary>Where an owned item sits: one of the eight equipment slots or the reserve (spec L210).</summary>
    public readonly struct Placement : IEquatable<Placement>
    {
        private Placement(bool isReserve, EquipmentSlot slot)
        {
            IsReserve = isReserve;
            Slot = slot;
        }

        public static Placement Reserve => new Placement(true, default);

        public static Placement In(EquipmentSlot slot) => new Placement(false, slot);

        public bool IsReserve { get; }

        /// <summary>The equipment slot; meaningless when <see cref="IsReserve"/>.</summary>
        public EquipmentSlot Slot { get; }

        public bool IsSlot => !IsReserve;

        public bool Equals(Placement other) => IsReserve == other.IsReserve && (IsReserve || Slot == other.Slot);
        public override bool Equals(object obj) => obj is Placement other && Equals(other);
        public override int GetHashCode() => IsReserve ? -1 : (int)Slot;
        public override string ToString() => IsReserve ? "Reserve" : ContentText.SlotName(Slot);
    }

    /// <summary>
    /// One purchased copy of a shared equipment definition. Tracks the price paid (for 50 percent resale) and, for
    /// consumables, the single purchased charge (spec L212). Instance IDs are assigned sequentially per loadout.
    /// </summary>
    public sealed class OwnedEquipment
    {
        internal OwnedEquipment(int instanceId, EquipmentDef definition, int paidPrice, int charges)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (paidPrice < 0) throw new ArgumentOutOfRangeException(nameof(paidPrice));
            InstanceId = instanceId;
            Definition = definition;
            PaidPrice = paidPrice;
            Charges = charges;
        }

        public int InstanceId { get; }
        public EquipmentDef Definition { get; }
        public string Id => Definition.Id;

        /// <summary>Crowns actually paid; resale returns half of it (spec L212).</summary>
        public int PaidPrice { get; }

        /// <summary>Remaining purchased uses: one per consumable purchase, spent on commitment (spec L212, L575). Zero for other items.</summary>
        public int Charges { get; internal set; }

        public bool IsConsumable => Definition.IsConsumable;

        /// <summary>A consumable whose single charge was spent. It is inert: it supplies no item, tag, or category to any predicate.</summary>
        public bool IsSpent => IsConsumable && Charges <= 0;

        /// <summary>Uses the single equipment-active key (spec L575).</summary>
        public bool IsActive => Definition.IsActive;

        /// <summary>
        /// Refund when sold: 50 percent of the paid price; a used consumable sells for zero (spec L212).
        /// Issued gear is never an <see cref="OwnedEquipment"/> and cannot be sold (spec L202).
        /// </summary>
        public int SellValue
        {
            get
            {
                if (IsSpent) return GlobalRules.Economy.UsedConsumableSellValue;
                return (int)Math.Floor(PaidPrice * (double)GlobalRules.Economy.SellFraction);
            }
        }

        internal OwnedEquipment Clone() => new OwnedEquipment(InstanceId, Definition, PaidPrice, Charges);

        public override string ToString() => "#" + InstanceId + " " + Definition.Id + (IsConsumable ? " (" + Charges + " charge)" : "");
    }
}
