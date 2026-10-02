using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Confluence.Core.Rules;

namespace Confluence.Match
{
    /// <summary>
    /// Opens one player's equipment shop for a round. Match owns timing and the income schedule (spec L193); the
    /// economy owns balances, offers, prices, rerolls, selling, and spell selection.
    /// </summary>
    public sealed class ShopOpening
    {
        public ShopOpening(int player, MatchMode mode, int round, int bracketRound, bool isFirstShop, int baselineIncome, int winnerBonus)
        {
            if (round < 1) throw new ArgumentOutOfRangeException(nameof(round));
            if (baselineIncome < 0) throw new ArgumentOutOfRangeException(nameof(baselineIncome));
            if (winnerBonus < 0) throw new ArgumentOutOfRangeException(nameof(winnerBonus));
            Player = player;
            Mode = mode;
            Round = round;
            BracketRound = bracketRound;
            IsFirstShop = isFirstShop;
            BaselineIncome = baselineIncome;
            WinnerBonus = winnerBonus;
            HighestUnlockedTier = Schedules.HighestUnlockedTier(mode, round);
        }

        public int Player { get; }
        public MatchMode Mode { get; }

        /// <summary>Scheduled round; Conclave bracket rounds are 13+, where tier 4 stays unlocked (L38).</summary>
        public int Round { get; }

        /// <summary>1-3 in the Conclave bracket, otherwise 0.</summary>
        public int BracketRound { get; }

        public bool IsFirstShop { get; }

        /// <summary>600 before the first shop (or the custom starting Crowns), 500 before each later shop (L193, L71).</summary>
        public int BaselineIncome { get; }

        /// <summary>100 when the player won the previous combat round outside a replay; otherwise 0 (L193, L61).</summary>
        public int WinnerBonus { get; }

        /// <summary>Crowns to credit before the shop opens.</summary>
        public int Income => BaselineIncome + WinnerBonus;

        /// <summary>Highest purchasable equipment tier this round (L195-L200, L65).</summary>
        public int HighestUnlockedTier { get; }
    }

    /// <summary>Opens one player's free five-card augment draft (spec L216-L229).</summary>
    public sealed class DraftOpening
    {
        public DraftOpening(int player, MatchMode mode, int round, Rarity rarity)
        {
            Player = player;
            Mode = mode;
            Round = round;
            Rarity = rarity;
        }

        public int Player { get; }
        public MatchMode Mode { get; }
        public int Round { get; }

        /// <summary>The rarity stage shared across the lobby (L225).</summary>
        public Rarity Rarity { get; }
    }

    /// <summary>
    /// A player's finalized, publicly inspectable loadout for a round (spec L44, L46, L391): wizard, equipped spells,
    /// equipment, and augments with ranks. Stored by Match so the next reveal can show the opponent's completed
    /// previous-round loadout.
    /// </summary>
    public sealed class PublicLoadout
    {
        public PublicLoadout(string wizardId, IEnumerable<string> spellIds, IEnumerable<string> equipmentIds,
            IEnumerable<string> augmentIds, IEnumerable<int> augmentRanks)
        {
            WizardId = wizardId;
            SpellIds = Freeze(spellIds);
            EquipmentIds = Freeze(equipmentIds);
            AugmentIds = Freeze(augmentIds);
            AugmentRanks = new ReadOnlyCollection<int>(augmentRanks == null ? new List<int>() : new List<int>(augmentRanks));
            if (AugmentRanks.Count != AugmentIds.Count)
            {
                throw new ArgumentException("Every augment needs exactly one rank.", nameof(augmentRanks));
            }
        }

        public string WizardId { get; }
        public IReadOnlyList<string> SpellIds { get; }
        public IReadOnlyList<string> EquipmentIds { get; }
        public IReadOnlyList<string> AugmentIds { get; }

        /// <summary>Rank (1 or 2) of the augment at the same index.</summary>
        public IReadOnlyList<int> AugmentRanks { get; }

        public override string ToString()
        {
            var augments = new List<string>(AugmentIds.Count);
            for (int i = 0; i < AugmentIds.Count; i++) augments.Add(AugmentIds[i] + "r" + AugmentRanks[i]);
            return (WizardId ?? "-") + " spells=[" + string.Join(",", SpellIds) + "] gear=[" + string.Join(",", EquipmentIds) +
                "] augments=[" + string.Join(",", augments) + "]";
        }

        private static IReadOnlyList<string> Freeze(IEnumerable<string> ids) =>
            new ReadOnlyCollection<string>(ids == null ? new List<string>() : new List<string>(ids));
    }

    /// <summary>
    /// One player's side of a completed duel, forwarded to the feat ledger: feat leaves count completed duels in
    /// this match meeting a definition (spec L280). Voided duels are never reported.
    /// </summary>
    public sealed class CompletedDuelRecord
    {
        public CompletedDuelRecord(int player, int opponent, int round, int bracketRound, int attempt, RoundOutcome outcome, IEnumerable<string> featIds)
        {
            if (outcome != RoundOutcome.Win && outcome != RoundOutcome.Loss && outcome != RoundOutcome.Draw)
            {
                throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "A completed duel is a win, loss, or draw.");
            }

            Player = player;
            Opponent = opponent;
            Round = round;
            BracketRound = bracketRound;
            Attempt = attempt;
            Outcome = outcome;
            FeatIds = new ReadOnlyCollection<string>(featIds == null ? new List<string>() : new List<string>(featIds));
        }

        public int Player { get; }
        public int Opponent { get; }
        public int Round { get; }
        public int BracketRound { get; }
        public int Attempt { get; }
        public RoundOutcome Outcome { get; }

        /// <summary>FEAT-xxx the server verified for this player in this duel.</summary>
        public IReadOnlyList<string> FeatIds { get; }
    }

    /// <summary>
    /// Economy seam. Match calls these at phase boundaries and never computes prices or offers itself. Implemented
    /// by the server's adapter over Confluence.Economy.
    /// </summary>
    public interface IMatchEconomy
    {
        /// <summary>Credit income and open the shop (L45). Also resets the player's ready flag.</summary>
        void OpenShop(ShopOpening opening);

        /// <summary>Shop time ended: commit completed purchases and legal equipped choices only (L355) and freeze them (L227).</summary>
        void CloseShop(int player);

        /// <summary>Open the free draft on a frozen loadout (L227). Also resets the player's ready flag.</summary>
        void OpenDraft(DraftOpening opening);

        /// <summary>Draft time ended; a timeout keeps the build and chooses nothing (L229).</summary>
        void CloseDraft(int player);

        /// <summary>Freeze the round's loadout at alcove entry; it becomes visible to the opponent (L46).</summary>
        PublicLoadout FinalizeLoadout(int player, int round);

        /// <summary>True when the player has readied in the open shop, or has selected or passed in the open draft (L45, L229).</summary>
        bool IsReady(int player);

        /// <summary>A duel completed (not voided): feed the feat ledger and completed-duel counts (L280, D-029).</summary>
        void RecordCompletedDuel(CompletedDuelRecord record);
    }
}
