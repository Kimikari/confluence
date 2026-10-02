using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Economy.Augments;
using Confluence.Economy.Loadouts;

namespace Confluence.Economy.Shop
{
    public enum ShopCardState
    {
        /// <summary>A visible, affordable definition that can be bought.</summary>
        Offered,

        /// <summary>Bought this shop; the position stays empty until the next shop (spec L206).</summary>
        Purchased,

        /// <summary>
        /// The card became unaffordable after a purchase and folded into an outside-budget placeholder; it grants no free
        /// redraw, but its unused reroll can draw from the new affordable pool (spec L208).
        /// </summary>
        OutsideBudget,

        /// <summary>No legal affordable definition could be drawn; the reroll was not consumed (spec L208).</summary>
        SaveCrowns,
    }

    /// <summary>
    /// Snapshot of one of the three equipment cards (spec L353: name, tier price, effect, occupied slot, reroll indicator).
    /// </summary>
    public sealed class ShopCard
    {
        internal ShopCard(int position, ShopCardState state, EquipmentDef definition, bool rerollAvailable)
        {
            Position = position;
            State = state;
            Definition = definition;
            RerollAvailable = rerollAvailable;
        }

        public int Position { get; }
        public ShopCardState State { get; }

        /// <summary>The offered definition, or for an outside-budget placeholder the folded one; null otherwise.</summary>
        public EquipmentDef Definition { get; }

        /// <summary>The card's one independent free reroll is unused (spec L206).</summary>
        public bool RerollAvailable { get; }

        public bool IsPurchasable => State == ShopCardState.Offered;

        /// <summary>Tier price of the shown definition, or 0.</summary>
        public int Price => Definition != null ? Definition.Price : 0;

        /// <summary>Slots the definition can occupy, for the card's "occupied slot" line.</summary>
        public IReadOnlyList<EquipmentSlot> Slots => Definition != null ? SlotRules.SlotsFor(Definition) : new EquipmentSlot[0];

        public override string ToString() => Position + ":" + State + (Definition != null ? " " + Definition.Id : "") + (RerollAvailable ? "" : " (rerolled)");
    }

    /// <summary>Outcome of a shop request. On any failure nothing changed.</summary>
    public sealed class ShopResult
    {
        internal ShopResult(TransactionCode code, int crowns, int crownsDelta, ShopCard card = null, OwnedEquipment item = null,
            DormancyReport dormancy = null, bool isDuplicate = false)
        {
            Code = code;
            Crowns = crowns;
            CrownsDelta = crownsDelta;
            Card = card;
            Item = item;
            Dormancy = dormancy;
            IsDuplicate = isDuplicate;
        }

        public TransactionCode Code { get; }
        public bool Succeeded => Code == TransactionCode.Success;

        /// <summary>Balance after the request.</summary>
        public int Crowns { get; }

        /// <summary>Crowns gained (positive, a sale) or spent (negative, a purchase).</summary>
        public int CrownsDelta { get; }

        /// <summary>The affected card after the request.</summary>
        public ShopCard Card { get; }

        /// <summary>The bought, sold, or moved item.</summary>
        public OwnedEquipment Item { get; }

        /// <summary>Augment resolution after a finalized loadout change (spec L249, L587); null when nothing changed.</summary>
        public DormancyReport Dormancy { get; }

        /// <summary>The request ID was already processed; this is the stored result and nothing was re-applied (spec L429).</summary>
        public bool IsDuplicate { get; }

        internal ShopResult AsDuplicate() => new ShopResult(Code, Crowns, CrownsDelta, Card, Item, Dormancy, true);

        public override string ToString() => Code + (IsDuplicate ? " (duplicate)" : "") + " crowns=" + Crowns;
    }
}
