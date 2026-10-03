using System.Globalization;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Economy;
using Confluence.Sim.Players;

namespace Confluence.Client.ViewModels
{
    /// <summary>Plain-language text for catalogue records and economy outcomes shown by the M1 screens.</summary>
    public static class ContentFormat
    {
        public static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        public static string Seconds(float seconds) => seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";

        /// <summary>"20 mana, 6 s cooldown, 0.6 s cast, 30 m" from the record's numeric fields.</summary>
        public static string SpellCost(SpellDef spell)
        {
            if (spell == null)
            {
                return string.Empty;
            }

            string text = Number(spell.Mana) + " mana, " + Number(spell.Cooldown) + " s cooldown, " + Number(spell.CastTime) + " s cast";
            return spell.Range > 0 ? text + ", " + Number(spell.Range) + " m" : text;
        }

        public static string SpellName(Catalogue catalogue, string spellId)
        {
            if (spellId == null)
            {
                return null;
            }

            return catalogue != null && catalogue.SpellsById.TryGetValue(spellId, out SpellDef spell) ? spell.Name + " (" + spell.Id + ")" : spellId;
        }

        public static string WizardName(Catalogue catalogue, string wizardId)
        {
            return catalogue != null && wizardId != null && catalogue.WizardsById.TryGetValue(wizardId, out WizardDef wizard)
                ? wizard.Name + ", " + wizard.Title
                : wizardId;
        }

        public static string SlotName(EquipmentSlot slot) => ContentText.SlotName(slot);

        /// <summary>Readable name of a statistic line from <see cref="StatSheet.Inspect"/>.</summary>
        public static string StatName(StatLine line)
        {
            switch (line.Stat)
            {
                case StatKind.MovementBonus: return "Movement bonus";
                case StatKind.CooldownReduction: return "Cooldown reduction";
                case StatKind.CostReduction: return "Cost reduction";
                case StatKind.RefundFraction: return "Refund";
                case StatKind.DashCharges: return "Dash charges";
                case StatKind.DashDistance: return "Dash distance bonus";
                case StatKind.TeleportDistance: return "Teleport distance bonus";
                case StatKind.ManaRegenBonus: return "Mana regeneration";
                case StatKind.Lifesteal: return "Lifesteal";
                case StatKind.MaxHealthBonus: return "Maximum health";
                case StatKind.Traction: return "Ice traction";
                case StatKind.Resistance: return ContentText.ChannelName(line.Channel) + " resistance";
                case StatKind.PassiveReduction: return ContentText.ChannelName(line.Channel) + " total reduction";
                default: return line.Stat.ToString();
            }
        }

        /// <summary>Formats a statistic value the way the build panel shows it (fractions as percentages).</summary>
        public static string StatValue(StatKind stat, float value)
        {
            switch (stat)
            {
                case StatKind.MovementBonus:
                case StatKind.CooldownReduction:
                case StatKind.CostReduction:
                case StatKind.RefundFraction:
                case StatKind.Lifesteal:
                case StatKind.Resistance:
                case StatKind.PassiveReduction:
                case StatKind.Traction:
                    return (value * 100f).ToString("0.#", CultureInfo.InvariantCulture) + "%";
                case StatKind.ManaRegenBonus:
                    return value.ToString("0.#", CultureInfo.InvariantCulture) + "/s";
                case StatKind.DashDistance:
                case StatKind.TeleportDistance:
                    return value.ToString("0.#", CultureInfo.InvariantCulture) + " m";
                default:
                    return value.ToString("0.#", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Why a shop or draft request was refused (or that it succeeded).</summary>
        public static string Transaction(TransactionCode code)
        {
            switch (code)
            {
                case TransactionCode.Success: return "Done.";
                case TransactionCode.ShopClosed: return "The shop is closed.";
                case TransactionCode.DraftClosed: return "The draft is closed.";
                case TransactionCode.LoadoutFrozen: return "The loadout is frozen during the augment draft.";
                case TransactionCode.InsufficientCrowns: return "Not enough Crowns.";
                case TransactionCode.CardNotOffered: return "That card is not on offer.";
                case TransactionCode.RerollAlreadyUsed: return "This card's free reroll is already used.";
                case TransactionCode.RerollPoolEmpty: return "No other affordable item exists; the reroll is kept.";
                case TransactionCode.SaveCrowns: return "Save Crowns: nothing affordable to show; the reroll is kept.";
                case TransactionCode.NoFreeSpace: return "No legal slot or reserve space is free.";
                case TransactionCode.SlotRejectsItem: return "That slot does not accept this item.";
                case TransactionCode.SlotOccupied: return "That slot is occupied.";
                case TransactionCode.ReserveFull: return "The reserve is full (three items).";
                case TransactionCode.DuplicateEquippedDefinition: return "The same item cannot occupy two slots.";
                case TransactionCode.UniqueGroupConflict: return "A Unique item of this group is already equipped.";
                case TransactionCode.NotAnActiveItem: return "That item has no activation.";
                case TransactionCode.ActiveItemNotEnableable: return "Only an equipped item outside the Sidearm slot can be the enabled active.";
                case TransactionCode.InvalidSpellSelection: return "Exactly six different kit spells must be equipped.";
                case TransactionCode.SpellNotInKit: return "That spell is not in this wizard's kit.";
                case TransactionCode.DuplicateSpell: return "That spell is already equipped.";
                case TransactionCode.AugmentSlotsFull: return "All six augment slots are occupied.";
                case TransactionCode.ReplacementRequired: return "Choose the augment this selection replaces.";
                case TransactionCode.ConfirmationRequired: return "Confirm the augments that would become dormant.";
                case TransactionCode.ExclusiveGroupConflict: return "An owned augment shares its exclusivity group.";
                case TransactionCode.IneligibleAugment: return "The build no longer meets this augment's eligibility.";
                case TransactionCode.RankUnavailable: return "That rank is not available in this draft.";
                case TransactionCode.CardNotRerollable: return "This card cannot be rerolled.";
                default: return code.ToString();
            }
        }

        /// <summary>Spec L193: "600 before the first equipment shop and 500 before each later" shop.</summary>
        public static string Crowns(int amount) => amount.ToString(CultureInfo.InvariantCulture) + " Crowns";

        public static string Percent(float fraction) => (fraction * 100f).ToString("0", CultureInfo.InvariantCulture) + "%";

        public static string RarityName(Rarity rarity) => ContentText.RarityName(rarity);
    }
}
