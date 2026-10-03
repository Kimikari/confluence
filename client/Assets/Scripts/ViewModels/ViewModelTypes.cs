using System;
using System.Collections.Generic;

namespace Confluence.Client.ViewModels
{
    /// <summary>
    /// A screen's data source. Screens render only what a view model exposes and call only its commands, so the same
    /// screen runs against local Confluence.Economy / Confluence.Sim objects (the offline Laboratory) or, later, against
    /// state received from the server (docs/ARCHITECTURE.md "Networking": shop and draft actions are reliable
    /// request/response messages validated by the server). <see cref="Changed"/> fires after any visible value changes.
    /// </summary>
    public interface IViewModel
    {
        event Action Changed;
    }

    // ---- Build panel (spec L355) -------------------------------------------------------------------------------

    /// <summary>One of the eight equipment slots (spec L210).</summary>
    public sealed class EquipmentSlotView
    {
        public string SlotName { get; set; }

        /// <summary>Owned item instance, or -1 for issued gear or an empty slot.</summary>
        public int InstanceId { get; set; } = -1;

        public string ItemId { get; set; }
        public string ItemName { get; set; }

        /// <summary>The slot holds its plain issued item (no bonus, no resale, spec L202).</summary>
        public bool Issued { get; set; }

        public bool Empty { get; set; }

        /// <summary>"Enabled active", "Dormant: ...", or null for passive items (spec L210 "plainly labeled").</summary>
        public string ActiveLabel { get; set; }

        public int SellValue { get; set; }
        public string Effect { get; set; }
    }

    /// <summary>An item held in one of the three reserve slots (spec L210).</summary>
    public sealed class ReserveItemView
    {
        public int InstanceId { get; set; }
        public string ItemId { get; set; }
        public string ItemName { get; set; }
        public int SellValue { get; set; }
        public string SlotText { get; set; }
    }

    /// <summary>One of the wizard's eight exclusive spells (spec L292).</summary>
    public sealed class SpellView
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Role { get; set; }

        /// <summary>"20 mana, 6 s cooldown, 0.6 s cast" from the catalogue record.</summary>
        public string CostText { get; set; }

        public bool Equipped { get; set; }

        /// <summary>1-6 when equipped, else 0.</summary>
        public int SlotNumber { get; set; }
    }

    /// <summary>One of the six augment slots (spec L231, L641: dormant augments show their missing requirements).</summary>
    public sealed class AugmentSlotView
    {
        public int Slot { get; set; }
        public bool Empty { get; set; } = true;
        public string Id { get; set; }
        public string Name { get; set; }
        public int Rank { get; set; }
        public string Rarity { get; set; }
        public bool IsTransformation { get; set; }
        public string TargetSpellName { get; set; }
        public bool Dormant { get; set; }
        public IReadOnlyList<string> Missing { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> DormantParents { get; set; } = Array.Empty<string>();
    }

    /// <summary>A final statistic; raw and effective differ when a global cap applies (spec L355).</summary>
    public sealed class StatView
    {
        public string Name { get; set; }
        public string Raw { get; set; }
        public string Effective { get; set; }
        public bool Capped { get; set; }
    }

    /// <summary>A candidate for the one enabled equipment active (spec L575), or "none".</summary>
    public sealed class ActiveChoiceView
    {
        /// <summary>Owned item instance, or null for "no active".</summary>
        public int? InstanceId { get; set; }

        public string Name { get; set; }
        public bool Selected { get; set; }

        /// <summary>False when the item sits where its activation is unusable (Sidearm, reserve).</summary>
        public bool Enableable { get; set; } = true;
    }

    // ---- Shop (spec L204-L213, L353) ---------------------------------------------------------------------------

    public enum ShopCardStatus
    {
        Offered,
        Purchased,
        OutsideBudget,
        SaveCrowns,
    }

    /// <summary>One of the three equipment cards (spec L353: name, tier price, effect, occupied slot, reroll indicator).</summary>
    public sealed class ShopCardView
    {
        public int Position { get; set; }
        public ShopCardStatus Status { get; set; }
        public string DefinitionId { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public int Tier { get; set; }
        public int Price { get; set; }
        public string SlotText { get; set; }
        public string Effect { get; set; }
        public string Activation { get; set; }
        public string Drawback { get; set; }

        /// <summary>The card's one free reroll is unused (spec L206).</summary>
        public bool RerollAvailable { get; set; }

        public bool CanBuy { get; set; }
        public bool CanReroll { get; set; }

        /// <summary>Label for an inactive or dormant component, shown before purchase (spec L353), or null.</summary>
        public string Notice { get; set; }
    }

    // ---- Augment draft (spec L214-L233, L353) ------------------------------------------------------------------

    /// <summary>One of the five free augment cards (spec L353).</summary>
    public sealed class DraftCardView
    {
        public int Position { get; set; }
        public string AugmentId { get; set; }
        public string Name { get; set; }
        public string Rarity { get; set; }
        public int Rank { get; set; }
        public bool IsUpgrade { get; set; }
        public bool IsTransformation { get; set; }

        /// <summary>Target spell name and id for a transformation, or "Shared augment".</summary>
        public string TargetSpell { get; set; }

        /// <summary>Plain-language eligibility (spec L237) and why this card is offered now.</summary>
        public string Eligibility { get; set; }

        /// <summary>What the card does at the offered rank (the transformation text for transformations).</summary>
        public string Transformation { get; set; }

        public string Drawback { get; set; }
        public string Counterplay { get; set; }
        public bool RerollAvailable { get; set; }
    }

    public enum DraftStep
    {
        /// <summary>Choosing among the five cards.</summary>
        Choosing,

        /// <summary>All six augment slots are full: the player picks the augment the selection replaces (spec L231).</summary>
        ChoosingReplacement,

        /// <summary>The selection makes augments dormant: the player confirms the listed consequences (spec L231, L645).</summary>
        Confirming,

        /// <summary>The draft ended (selected, passed, or timed out).</summary>
        Closed,
    }

    /// <summary>The pending selection that needs a replacement slot or a confirmation.</summary>
    public sealed class DraftPendingView
    {
        public int Position { get; set; }
        public string AugmentName { get; set; }

        /// <summary>Slot being replaced, or -1.</summary>
        public int TargetSlot { get; set; } = -1;

        /// <summary>Name of the augment the selection removes, or null.</summary>
        public string ReplacedName { get; set; }

        /// <summary>Owned augments that become dormant, with their reasons.</summary>
        public IReadOnlyList<string> BecomeDormant { get; set; } = Array.Empty<string>();
    }

    // ---- Round reveal (spec L44, L329) -------------------------------------------------------------------------

    public sealed class MaterialDescription
    {
        public string Label { get; set; }
        public string Description { get; set; }
    }

    // ---- HUD (spec L347-L349) ----------------------------------------------------------------------------------

    public enum SlotState
    {
        Ready,
        Cooldown,
        NoMana,
        Blocked,
        Casting,
        Empty,
    }

    /// <summary>One of the six spell slots on the HUD.</summary>
    public sealed class HudSlotView
    {
        public int Number { get; set; }
        public string SpellId { get; set; }
        public string Name { get; set; }
        public SlotState State { get; set; }
        public float CooldownSec { get; set; }

        /// <summary>Charges where relevant (spec L349), or -1.</summary>
        public int Charges { get; set; } = -1;

        public float ManaCost { get; set; }
        public bool Selected { get; set; }
    }

    /// <summary>The signature, enabled active, or consumable cell.</summary>
    public sealed class HudActionView
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public SlotState State { get; set; }
        public string Detail { get; set; }
    }

    public enum StatusIconKind
    {
        /// <summary>Hard-control immunity after a hard control ends (spec L116, L347).</summary>
        ControlImmunity,

        /// <summary>Wet: eligible for conduction and freezing (spec L347).</summary>
        Wet,

        /// <summary>Damage over time (spec L118, L347).</summary>
        DamageOverTime,

        /// <summary>Remaining duel healing budget (spec L114, L347).</summary>
        HealingBudget,

        /// <summary>Stun, root, silence, displacement.</summary>
        HardControl,

        Slow,
        Other,
    }

    /// <summary>A status icon: a short glyph, plain label, and remaining time (never color alone, spec L371).</summary>
    public sealed class StatusIconView
    {
        public StatusIconKind Kind { get; set; }
        public string Glyph { get; set; }
        public string Label { get; set; }

        /// <summary>Remaining seconds, or a negative value when the icon has no duration.</summary>
        public float Seconds { get; set; } = -1f;

        /// <summary>Extra value text such as remaining healing.</summary>
        public string Value { get; set; }
    }

    public enum BoundaryCue
    {
        /// <summary>Before 55 s: the contour is steady.</summary>
        Steady,

        /// <summary>55-85 s: the announced contraction (spec L48).</summary>
        Contracting,

        /// <summary>The player is outside the legal boundary and takes boundary damage (spec L51).</summary>
        Outside,
    }

    // ---- Results (spec L49, L359-L361) -------------------------------------------------------------------------

    public sealed class ResolveLine
    {
        public string Name { get; set; }
        public int Before { get; set; }
        public int After { get; set; }
        public int Change => After - Before;
    }
}
