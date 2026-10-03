using System.Collections.Generic;

namespace Confluence.Client.ViewModels
{
    /// <summary>
    /// Build panel (spec L355): occupied slots, reserve equipment, the eight exclusive spells, six augment slots with
    /// dormant labels and missing requirements, the active item choice, and final statistics with raw and effective
    /// values when a cap applies.
    /// </summary>
    public interface IBuildViewModel : IViewModel
    {
        string WizardName { get; }
        string WizardId { get; }
        string Signature { get; }
        string Passive { get; }
        IReadOnlyList<EquipmentSlotView> Slots { get; }
        IReadOnlyList<ReserveItemView> Reserve { get; }
        int ReserveCapacity { get; }
        IReadOnlyList<SpellView> Spells { get; }
        IReadOnlyList<AugmentSlotView> Augments { get; }
        IReadOnlyList<StatView> Stats { get; }

        /// <summary>Name of the enabled equipment active, or "None".</summary>
        string EnabledActive { get; }

        IReadOnlyList<ActiveChoiceView> ActiveChoices { get; }
    }

    /// <summary>
    /// Equipment shop phase (spec L45, L204-L213, L353-L355): three cards with buy and one free reroll each, selling,
    /// reserve and slot moves, the active-item choice, six-of-eight spell selection, Crowns, the phase timer, and ready.
    /// Commands are requests; the result (accepted or the reason it was refused) appears in <see cref="Message"/>.
    /// </summary>
    public interface IShopViewModel : IViewModel
    {
        string Title { get; }
        int Crowns { get; }
        float SecondsRemaining { get; }
        bool IsOpen { get; }
        bool IsReady { get; }
        bool TimerPaused { get; }

        /// <summary>The outcome of the last request in plain language.</summary>
        string Message { get; }

        bool MessageIsError { get; }
        IReadOnlyList<ShopCardView> Cards { get; }
        IBuildViewModel Build { get; }

        void Buy(int position);
        void Reroll(int position);
        void Sell(int instanceId);

        /// <summary>Moves an equipped item to the reserve, or a reserve item into its first legal slot.</summary>
        void ToggleReserve(int instanceId);

        /// <summary>
        /// Swaps an equipped spell for one of the two unequipped kit spells, keeping its slot number (six of the eight
        /// kit spells stay equipped, spec L45, L292).
        /// </summary>
        void SwapSpells(string equippedSpellId, string benchSpellId);

        void SetEnabledActive(int? instanceId);
        void SetReady(bool ready);
    }

    /// <summary>
    /// Five-card free augment draft (spec L214-L233, L353): rarity, rank, target spell, eligibility, transformation
    /// text, and a per-card reroll; select (choosing a slot to replace when all six are full), pass, and a
    /// confirmation that lists the augments a selection makes dormant (spec L231, L645).
    /// </summary>
    public interface IDraftViewModel : IViewModel
    {
        string Title { get; }
        string Rarity { get; }
        float SecondsRemaining { get; }
        DraftStep Step { get; }
        IReadOnlyList<DraftCardView> Cards { get; }

        /// <summary>The selection waiting for a replacement slot or a confirmation, or null.</summary>
        DraftPendingView Pending { get; }

        /// <summary>Explanation when no transformation was eligible (spec L227), or null.</summary>
        string GuaranteeNote { get; }

        string Message { get; }
        bool MessageIsError { get; }
        IBuildViewModel Build { get; }

        void Reroll(int position);
        void Select(int position);
        void ChooseReplacement(int slot);
        void Confirm();
        void CancelPending();
        void Pass();
    }

    /// <summary>
    /// Round reveal (spec L44, L329): the next opponent and their completed previous-round loadout, the generated
    /// arena (diameter, biome, environment rule, major terrain, props, material descriptions), and remaining Resolve.
    /// </summary>
    public interface IRoundRevealViewModel : IViewModel
    {
        string Title { get; }
        string OpponentName { get; }
        string OpponentWizard { get; }
        IReadOnlyList<string> OpponentLoadout { get; }
        int PlayerResolve { get; }
        int OpponentResolve { get; }
        bool ArenaReady { get; }
        string ArenaStatus { get; }
        int Diameter { get; }
        string Biome { get; }
        string EnvironmentRule { get; }
        IReadOnlyList<string> MajorTerrain { get; }
        IReadOnlyList<string> Props { get; }
        IReadOnlyList<MaterialDescription> Materials { get; }
        string NextPhase { get; }
        bool CanContinue { get; }

        void Continue();
    }

    /// <summary>
    /// In-combat HUD (spec L347-L349, L51-L53): health with temporary shield, mana, stamina, the selected spell and its
    /// cooldown, six slot states, signature, the enabled active, consumable, status icons, round timer, boundary cue,
    /// and the final-15-second judgement indicator.
    /// </summary>
    public interface IHudViewModel : IViewModel
    {
        float Health { get; }
        float MaxHealth { get; }
        float Shield { get; }
        float Mana { get; }
        float MaxMana { get; }
        float Stamina { get; }
        float MaxStamina { get; }
        int SelectedSlot { get; }
        IReadOnlyList<HudSlotView> Slots { get; }

        /// <summary>The selected spell's state in words, e.g. "Ready", "Cooldown 3.2 s", "Blocked: alcove ward".</summary>
        string SelectedSpellLine { get; }

        HudActionView Signature { get; }
        HudActionView ActiveItem { get; }
        HudActionView Consumable { get; }
        IReadOnlyList<StatusIconView> Statuses { get; }

        /// <summary>"Alcove 3.4" or "1:12" (spec L46-L47).</summary>
        string TimerText { get; }

        string PhaseText { get; }
        BoundaryCue Boundary { get; }
        string BoundaryText { get; }
        bool JudgementVisible { get; }

        /// <summary>1 ahead, 0 equal, -1 behind (spec L53).</summary>
        int Judgement { get; }

        string GuardText { get; }
        string MeleeText { get; }

        /// <summary>A note shown when a system is missing in this build (for example no spell runtime), or null.</summary>
        string Notice { get; }
    }

    /// <summary>
    /// Round results (spec L49, L359-L361): winner, Resolve change, next baseline income, winner bonus, the remaining
    /// build, and the decisive sequence (a numeric event list until the explanation generator exists).
    /// </summary>
    public interface IRoundResultsViewModel : IViewModel
    {
        string Title { get; }
        string Outcome { get; }
        string Reason { get; }
        IReadOnlyList<ResolveLine> Resolve { get; }
        int NextIncome { get; }
        int WinnerBonus { get; }

        /// <summary>Whose sequence is listed and over what window, for example "How the training dummy fell (last 5 s ...)".</summary>
        string DecisiveHeading { get; }

        IReadOnlyList<string> DecisiveSequence { get; }
        IReadOnlyList<string> RemainingBuild { get; }
        string ContinueLabel { get; }
        bool MatchOver { get; }

        void Continue();
    }
}
