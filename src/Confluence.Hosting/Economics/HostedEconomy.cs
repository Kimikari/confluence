using System;
using System.Collections.Generic;
using System.Globalization;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Economy;
using Confluence.Economy.Augments;
using Confluence.Economy.Eligibility;
using Confluence.Economy.Loadouts;
using Confluence.Economy.Shop;
using Confluence.Match;

namespace Confluence.Hosting.Economics
{
    /// <summary>Which economy phase a player is in.</summary>
    public enum EconomyPhase
    {
        None,
        Shop,
        Draft,
    }

    /// <summary>
    /// Match's <see cref="IMatchEconomy"/> over Confluence.Economy. Each player has one <see cref="PlayerEconomy"/> for the
    /// match; every shop phase opens a fresh <see cref="EquipmentShop"/> on the player's Offer stream and every draft round
    /// an <see cref="AugmentDraft"/> on the Draft stream (L435, D-084), both keyed by the scheduled round and the player's
    /// lobby slot. Match supplies the income and winner bonus (L193). The draft freezes the loadout while it is open
    /// (L227); dormancy is re-resolved when each phase closes and at duel start (L249). The loadout finalized at the
    /// alcove is kept as the round's snapshot, so replays and void reruns restore it in full (L38, L65).
    /// </summary>
    public sealed class HostedEconomy : IMatchEconomy
    {
        private readonly Seat[] _seats;
        private readonly IStructuralChangeSource _changes;

        public HostedEconomy(Catalogue catalogue, MatchMode mode, ulong matchSeed, IReadOnlyList<string> wizardIds,
            IContentAvailability pools, IStructuralChangeSource changes = null)
        {
            Catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
            if (wizardIds == null || wizardIds.Count < 2) throw new ArgumentException("A match needs at least two players.", nameof(wizardIds));
            Pools = pools ?? throw new ArgumentNullException(nameof(pools));
            Mode = mode;
            MatchSeed = matchSeed;
            _changes = changes ?? StructuralChangeSources.Default;
            _seats = new Seat[wizardIds.Count];
            for (int i = 0; i < _seats.Length; i++)
            {
                _seats[i] = new Seat(new PlayerEconomy(catalogue, wizardIds[i], i));
            }
        }

        public Catalogue Catalogue { get; }
        public MatchMode Mode { get; }
        public ulong MatchSeed { get; }

        /// <summary>Shop and draft availability (implemented content, D-010, unless a Laboratory override widens it).</summary>
        public IContentAvailability Pools { get; }

        public int PlayerCount => _seats.Length;

        public PlayerEconomy Player(int player) => SeatOf(player).Economy;

        public EconomyPhase PhaseOf(int player) => SeatOf(player).Phase;

        /// <summary>The player's open shop, or null.</summary>
        public EquipmentShop OpenShopOf(int player)
        {
            Seat seat = SeatOf(player);
            return seat.Phase == EconomyPhase.Shop && seat.Shop != null && seat.Shop.IsOpen ? seat.Shop : null;
        }

        /// <summary>The player's open draft, or null.</summary>
        public AugmentDraft OpenDraftOf(int player)
        {
            Seat seat = SeatOf(player);
            return seat.Phase == EconomyPhase.Draft && seat.Draft != null && seat.Draft.IsOpen ? seat.Draft : null;
        }

        /// <summary>The draft of the current round (open or finished), or null when the round has none.</summary>
        public AugmentDraft CurrentDraftOf(int player) => SeatOf(player).Draft;

        public bool IsShopReady(int player) => SeatOf(player).ShopReady;

        /// <summary>Marks a player ready in the open shop (L45). Returns false when no shop is open.</summary>
        public bool MarkShopReady(int player)
        {
            if (OpenShopOf(player) == null) return false;
            SeatOf(player).ShopReady = true;
            return true;
        }

        /// <summary>The loadout finalized at the last alcove: the round's snapshot every pass of the round fights with.</summary>
        public Loadout RoundLoadout(int player) => SeatOf(player).RoundLoadout;

        /// <summary>Dormancy resolution from the last finalized change.</summary>
        public DormancyReport LastDormancy(int player) => SeatOf(player).LastDormancy;

        /// <summary>The round's finalized loadout mapped for a duel (L46).</summary>
        public MappedLoadout MapForDuel(int player, string playerId, int spawnSide)
        {
            Seat seat = SeatOf(player);
            Loadout loadout = seat.RoundLoadout;
            if (loadout == null)
            {
                lock (seat.Economy.SyncRoot)
                {
                    DormancyResolver.Resolve(seat.Economy.Loadout, _changes);
                    loadout = seat.Economy.Loadout.Clone();
                }
            }

            return LoadoutMapper.Map(loadout, playerId, spawnSide);
        }

        /// <summary>
        /// A completed duel used consumables: each spends one purchased charge on the live loadout (L212). Replays restore
        /// the round snapshot instead (L38), and voided duels are never reported (L427).
        /// </summary>
        public int RecordConsumablesUsed(int player, int count)
        {
            Seat seat = SeatOf(player);
            int spent = 0;
            lock (seat.Economy.SyncRoot)
            {
                for (int i = 0; i < count; i++)
                    if (seat.Economy.Loadout.SpendConsumableCharge() == TransactionCode.Success) spent++;
            }

            return spent;
        }

        /// <summary>Match-start floors (L233, D-010, D-082) for every player's wizard against the current pools.</summary>
        public MatchFloorReport CheckFloors()
        {
            var wizards = new List<string>(_seats.Length);
            foreach (Seat seat in _seats) wizards.Add(seat.Economy.Loadout.WizardId);
            return EconomyFloors.CheckMatchStart(Catalogue, Pools, Mode, wizards, _changes);
        }

        // ---- IMatchEconomy ----------------------------------------------------------------------------------------

        public void OpenShop(ShopOpening opening)
        {
            if (opening == null) throw new ArgumentNullException(nameof(opening));
            Seat seat = SeatOf(opening.Player);
            seat.Economy.GrantCrowns(opening.Income); // L193: Match decides baseline income and the winner bonus
            var round = new RoundInfo(Mode, opening.Round, opening.BracketRound > 0);
            seat.Shop = new EquipmentShop(seat.Economy, round, MatchSeed, Pools, _changes);
            seat.Draft = null;
            seat.ShopReady = false;
            seat.Phase = EconomyPhase.Shop;
        }

        public void CloseShop(int player)
        {
            Seat seat = SeatOf(player);
            if (seat.Shop != null && seat.Shop.IsOpen) seat.LastDormancy = seat.Shop.Close(); // L355: only completed purchases remain
            seat.Phase = EconomyPhase.None;
        }

        public void OpenDraft(DraftOpening opening)
        {
            if (opening == null) throw new ArgumentNullException(nameof(opening));
            Seat seat = SeatOf(opening.Player);
            var round = new RoundInfo(Mode, opening.Round);
            // L227: the draft freezes equipment and spells while it is open.
            seat.Draft = AugmentDraft.TryOpen(seat.Economy, round, MatchSeed, Pools, out AugmentDraft draft, _changes) ? draft : null;
            seat.Phase = EconomyPhase.Draft;
        }

        public void CloseDraft(int player)
        {
            Seat seat = SeatOf(player);
            if (seat.Draft != null) seat.Draft.Timeout(); // L229: a timeout keeps the build and chooses nothing
            lock (seat.Economy.SyncRoot) seat.LastDormancy = DormancyResolver.Resolve(seat.Economy.Loadout, _changes);
            seat.Phase = EconomyPhase.None;
        }

        public PublicLoadout FinalizeLoadout(int player, int round)
        {
            Seat seat = SeatOf(player);
            Loadout snapshot;
            lock (seat.Economy.SyncRoot)
            {
                Loadout loadout = seat.Economy.Loadout;
                seat.LastDormancy = DormancyResolver.Resolve(loadout, _changes); // L249: reevaluate at duel start
                snapshot = loadout.Clone();
            }

            seat.RoundLoadout = snapshot;
            seat.Phase = EconomyPhase.None;
            return ToPublic(snapshot);
        }

        public bool IsReady(int player)
        {
            Seat seat = SeatOf(player);
            switch (seat.Phase)
            {
                case EconomyPhase.Shop:
                    return seat.ShopReady;
                case EconomyPhase.Draft:
                    return seat.Draft == null || !seat.Draft.IsOpen; // selecting or passing ends the draft (L229)
                default:
                    return true;
            }
        }

        public void RecordCompletedDuel(CompletedDuelRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            Seat seat = SeatOf(record.Player);
            string key = "r" + record.Round.ToString(CultureInfo.InvariantCulture) + ".b" + record.BracketRound.ToString(CultureInfo.InvariantCulture) +
                ".a" + record.Attempt.ToString(CultureInfo.InvariantCulture) + ".vs" + record.Opponent.ToString(CultureInfo.InvariantCulture);
            FeatRecordResult result;
            lock (seat.Economy.SyncRoot) result = seat.Economy.Feats.RecordCompletedDuel(key, false, record.FeatIds);
            if (result != FeatRecordResult.Recorded)
            {
                throw new InvalidOperationException("Feat ledger rejected duel " + key + " of player " + record.Player + ": " + result + ".");
            }
        }

        /// <summary>The publicly inspectable loadout (L44, L46): wizard, equipped spells, equipped items, owned augments with ranks.</summary>
        public static PublicLoadout ToPublic(Loadout loadout)
        {
            if (loadout == null) throw new ArgumentNullException(nameof(loadout));
            var equipment = new List<string>();
            foreach (OwnedEquipment item in loadout.EquippedItems) equipment.Add(item.Id);
            var augments = new List<string>();
            var ranks = new List<int>();
            foreach (OwnedAugment augment in loadout.Augments)
            {
                augments.Add(augment.Id);
                ranks.Add(augment.Rank);
            }

            return new PublicLoadout(loadout.WizardId, loadout.EquippedSpellIds, equipment, augments, ranks);
        }

        private Seat SeatOf(int player)
        {
            if (player < 0 || player >= _seats.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(player), player, "Player ids are 0.." + (_seats.Length - 1) + ".");
            }

            return _seats[player];
        }

        private sealed class Seat
        {
            public Seat(PlayerEconomy economy)
            {
                Economy = economy;
            }

            public PlayerEconomy Economy { get; }
            public EconomyPhase Phase { get; set; }
            public EquipmentShop Shop { get; set; }
            public AugmentDraft Draft { get; set; }
            public bool ShopReady { get; set; }
            public Loadout RoundLoadout { get; set; }
            public DormancyReport LastDormancy { get; set; }
        }
    }
}
