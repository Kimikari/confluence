namespace Confluence.Economy
{
    /// <summary>
    /// Explicit outcome of every loadout, shop, and draft operation. Operations are atomic: any code other than
    /// <see cref="Success"/> (and the informational <see cref="SaveCrowns"/>) means nothing changed (spec L208, L429).
    /// </summary>
    public enum TransactionCode
    {
        Success = 0,

        // ---- Request handling (spec L429 "prevents duplicate purchase requests") ----
        InvalidRequestId,
        /// <summary>The request ID was already used for a different operation; nothing happened.</summary>
        RequestIdConflict,

        // ---- Phase state ----
        ShopClosed,
        DraftClosed,
        /// <summary>The loadout is frozen for the augment draft (spec L227).</summary>
        LoadoutFrozen,

        // ---- Money ----
        InsufficientCrowns,

        // ---- Shop cards ----
        InvalidCardPosition,
        CardNotOffered,
        RerollAlreadyUsed,
        /// <summary>No legal, affordable, non-visible definition exists for this reroll; the reroll is kept.</summary>
        RerollPoolEmpty,
        /// <summary>Informational: the card shows Save Crowns; its reroll was not consumed (spec L208).</summary>
        SaveCrowns,

        // ---- Equipment and slots (spec L210) ----
        UnknownItem,
        NotEquipment,
        IllegalDefinition,
        NoFreeSpace,
        SlotRejectsItem,
        SlotOccupied,
        ReserveFull,
        /// <summary>The same definition would occupy two equipment slots (Main Focus and Sidearm, or both trinkets).</summary>
        DuplicateEquippedDefinition,
        /// <summary>A nonempty Unique group would be equipped twice.</summary>
        UniqueGroupConflict,
        NotAnActiveItem,
        /// <summary>The item must be equipped in a slot whose activation is usable (not the Sidearm, spec L577).</summary>
        ActiveItemNotEnableable,
        NoConsumableCharge,

        // ---- Exclusive spells (spec L292, L521) ----
        InvalidSpellSelection,
        SpellNotInKit,
        DuplicateSpell,
        SpellsAreNotPurchasable,

        // ---- Augments and draft (spec L229-L233) ----
        InvalidAugmentSlot,
        AugmentSlotsFull,
        AugmentAlreadyOwned,
        AugmentNotOwned,
        RankUnavailable,
        ExclusiveGroupConflict,
        WrongWizard,
        IneligibleAugment,
        /// <summary>Selecting a new definition with all six slots occupied requires choosing the augment it replaces.</summary>
        ReplacementRequired,
        /// <summary>The selection would make augments dormant; the player must confirm first (spec L231, L645).</summary>
        ConfirmationRequired,
        NoAlternative,
        CardNotRerollable,
    }

    public static class TransactionCodes
    {
        /// <summary>True when the operation was applied (Save Crowns is a legal outcome that leaves state unchanged).</summary>
        public static bool IsSuccess(this TransactionCode code) => code == TransactionCode.Success;
    }
}
