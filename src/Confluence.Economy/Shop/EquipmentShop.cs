using System;
using System.Collections.Generic;
using System.Globalization;
using Confluence.Core.Content;
using Confluence.Core.Random;
using Confluence.Core.Rules;
using Confluence.Economy.Augments;
using Confluence.Economy.Eligibility;
using Confluence.Economy.Loadouts;

namespace Confluence.Economy.Shop
{
    /// <summary>
    /// One player's equipment shop for one shop phase (spec L204-L213, L351-L355). Three cards, each with one independent
    /// free reroll, drawn from unlocked affordable tiers (DECISIONS D-020) with the player's Offer random stream only.
    /// Every request is an atomic transaction keyed by a request ID: money, slots, reserve space, and Unique groups are
    /// rechecked, nothing changes on failure, and a repeated ID returns the stored result (spec L208, L429). Spell
    /// selection from the wizard's own kit happens in the same phase (spec L45). After every finalized loadout change the
    /// augments are re-resolved, so a sold prerequisite makes its dependents dormant immediately (spec L249, L587).
    /// </summary>
    public sealed class EquipmentShop
    {
        private readonly PlayerEconomy _player;
        private readonly EquipmentPool _pool;
        private readonly IStructuralChangeSource _changes;
        private readonly Rng _rng;
        private readonly CardSlot[] _cards = new CardSlot[GlobalRules.Economy.ShopOffers];

        public EquipmentShop(PlayerEconomy player, RoundInfo round, ulong matchSeed, IContentAvailability availability, IStructuralChangeSource changes = null)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            Round = round ?? throw new ArgumentNullException(nameof(round));
            _pool = new EquipmentPool(player.Catalogue, availability ?? throw new ArgumentNullException(nameof(availability)));
            _changes = changes ?? StructuralChangeSources.Default;
            _rng = RngStreams.For(matchSeed, RngStream.Offer, round.Round, player.PlayerSlot);
            lock (player.SyncRoot)
            {
                if (player.Loadout.IsFrozen) throw new InvalidOperationException("The loadout is frozen by an open augment draft.");
                for (int i = 0; i < _cards.Length; i++)
                {
                    _cards[i] = new CardSlot();
                    var def = _pool.Draw(_rng, Round, player.Crowns, VisibleIds(-1));
                    if (def != null) _cards[i].Show(def);
                    else _cards[i].State = ShopCardState.SaveCrowns;
                }
                IsOpen = true;
            }
        }

        public RoundInfo Round { get; }
        public bool IsOpen { get; private set; }
        public PlayerEconomy Player => _player;

        public IReadOnlyList<ShopCard> Cards
        {
            get
            {
                lock (_player.SyncRoot)
                {
                    var list = new ShopCard[_cards.Length];
                    for (int i = 0; i < list.Length; i++) list[i] = Snapshot(i);
                    return list;
                }
            }
        }

        // ---- Requests ----------------------------------------------------------------------------

        /// <summary>
        /// Buys an offered card into the requested placement, or automatically into the first empty legal slot, else the
        /// reserve. The position then stays empty until the next shop; remaining cards the new balance cannot afford fold
        /// into outside-budget placeholders (spec L206, L208).
        /// </summary>
        public ShopResult Buy(long requestId, int position, Placement? destination = null)
        {
            return Process(requestId, "buy:" + position + ":" + Describe(destination), () => ExecuteBuy(position, destination));
        }

        /// <summary>
        /// Buys by definition ID, for clients that address cards by content ID. Only a currently offered equipment card can
        /// be bought; exclusive spells are never purchasable (spec L185, L292, L521).
        /// </summary>
        public ShopResult BuyById(long requestId, string definitionId, Placement? destination = null)
        {
            return Process(requestId, "buy-id:" + definitionId + ":" + Describe(destination), () =>
            {
                if (definitionId != null && _player.Catalogue.TryGetSpell(definitionId, out _)) return Fail(TransactionCode.SpellsAreNotPurchasable);
                for (int i = 0; i < _cards.Length; i++)
                    if (_cards[i].State == ShopCardState.Offered && _cards[i].Definition.Id == definitionId) return ExecuteBuy(i, destination);
                var open = CheckOpen(true);
                return Fail(open != TransactionCode.Success ? open : TransactionCode.CardNotOffered);
            });
        }

        private ShopResult ExecuteBuy(int position, Placement? destination)
        {
            var check = CheckOpen(true);
            if (check != TransactionCode.Success) return Fail(check);
            if (position < 0 || position >= _cards.Length) return Fail(TransactionCode.InvalidCardPosition);
            var card = _cards[position];
            if (card.State != ShopCardState.Offered) return Fail(TransactionCode.CardNotOffered, position);
            int price = card.Definition.Price; // same Crown price for every wizard (spec L556)
            if (price > _player.Crowns) return Fail(TransactionCode.InsufficientCrowns, position);

            var loadout = _player.Loadout;
            var code = loadout.CheckAdd(card.Definition, destination, out _);
            if (code != TransactionCode.Success) return Fail(code, position);
            _player.Debit(price);
            code = loadout.AddItem(card.Definition, price, destination, out var item);
            if (code != TransactionCode.Success) throw new InvalidOperationException("Validated purchase failed: " + code);

            card.State = ShopCardState.Purchased;
            card.Definition = null;
            FoldUnaffordable();
            var report = DormancyResolver.Resolve(loadout, _changes);
            return new ShopResult(TransactionCode.Success, _player.Crowns, -price, Snapshot(position), item, report);
        }

        /// <summary>
        /// Uses a card's single free reroll (spec L206, L208). The new card is never the definition it replaced nor any other
        /// visible definition, and never costs more than the balance. An outside-budget placeholder draws from the new
        /// affordable pool. With nothing to draw an offered card stays as it is, and a placeholder shows Save Crowns; in
        /// both cases the reroll is kept.
        /// </summary>
        public ShopResult Reroll(long requestId, int position)
        {
            return Process(requestId, "reroll:" + position, () =>
            {
                var check = CheckOpen(false);
                if (check != TransactionCode.Success) return Fail(check);
                if (position < 0 || position >= _cards.Length) return Fail(TransactionCode.InvalidCardPosition);
                var card = _cards[position];
                if (card.State == ShopCardState.Purchased) return Fail(TransactionCode.CardNotRerollable, position);
                if (!card.RerollAvailable) return Fail(TransactionCode.RerollAlreadyUsed, position);

                // Never the definition it replaces, the folded one, or any other visible definition (spec L206).
                var excluded = VisibleIds(position);
                if (card.Definition != null) excluded.Add(card.Definition.Id);
                if (card.ReplacedId != null) excluded.Add(card.ReplacedId);
                var def = _pool.Draw(_rng, Round, _player.Crowns, excluded);
                if (def == null)
                {
                    if (card.State == ShopCardState.Offered) return Fail(TransactionCode.RerollPoolEmpty, position);
                    card.State = ShopCardState.SaveCrowns;
                    card.Definition = null;
                    return new ShopResult(TransactionCode.SaveCrowns, _player.Crowns, 0, Snapshot(position));
                }
                card.Show(def);
                card.RerollAvailable = false;
                return new ShopResult(TransactionCode.Success, _player.Crowns, 0, Snapshot(position));
            });
        }

        /// <summary>
        /// Sells an owned item for 50 percent of its paid price; a used consumable sells for zero and disappears (spec L212).
        /// Selling never refills cards, restores rerolls, refunds augments, or adds choices (spec L208, L587).
        /// </summary>
        public ShopResult Sell(long requestId, int instanceId)
        {
            return Process(requestId, "sell:" + instanceId, () =>
            {
                var check = CheckOpen(true);
                if (check != TransactionCode.Success) return Fail(check);
                var loadout = _player.Loadout;
                var code = loadout.RemoveItem(instanceId, out var removed);
                if (code != TransactionCode.Success) return Fail(code);
                int refund = removed.SellValue;
                _player.Credit(refund);
                var report = DormancyResolver.Resolve(loadout, _changes);
                return new ShopResult(TransactionCode.Success, _player.Crowns, refund, null, removed, report);
            });
        }

        /// <summary>Moves an owned item between slots and the reserve, swapping with an occupant (spec L210).</summary>
        public ShopResult Move(long requestId, int instanceId, Placement target)
        {
            return Process(requestId, "move:" + instanceId + ":" + target, () =>
            {
                var check = CheckOpen(true);
                if (check != TransactionCode.Success) return Fail(check);
                var loadout = _player.Loadout;
                var code = loadout.Move(instanceId, target);
                if (code != TransactionCode.Success) return Fail(code);
                var item = loadout.FindItem(instanceId, out _);
                var report = DormancyResolver.Resolve(loadout, _changes);
                return new ShopResult(TransactionCode.Success, _player.Crowns, 0, null, item, report);
            });
        }

        /// <summary>Chooses the one enabled equipment active, or none (spec L575).</summary>
        public ShopResult SetEnabledActive(long requestId, int? instanceId)
        {
            string key = instanceId.HasValue ? instanceId.Value.ToString(CultureInfo.InvariantCulture) : "none";
            return Process(requestId, "active:" + key, () =>
            {
                var check = CheckOpen(true);
                if (check != TransactionCode.Success) return Fail(check);
                var loadout = _player.Loadout;
                var code = loadout.SetEnabledActive(instanceId);
                if (code != TransactionCode.Success) return Fail(code);
                var report = DormancyResolver.Resolve(loadout, _changes);
                return new ShopResult(TransactionCode.Success, _player.Crowns, 0, null, loadout.EnabledActive, report);
            });
        }

        /// <summary>Sets the six equipped spells from the wizard's own eight-spell kit (spec L45, L292).</summary>
        public ShopResult SetEquippedSpells(long requestId, IReadOnlyList<string> spellIds)
        {
            string key = spellIds == null ? "null" : string.Join(",", spellIds);
            return Process(requestId, "spells:" + key, () =>
            {
                var check = CheckOpen(true);
                if (check != TransactionCode.Success) return Fail(check);
                var loadout = _player.Loadout;
                var code = loadout.SetEquippedSpells(spellIds);
                if (code != TransactionCode.Success) return Fail(code);
                var report = DormancyResolver.Resolve(loadout, _changes);
                return new ShopResult(TransactionCode.Success, _player.Crowns, 0, null, null, report);
            });
        }

        /// <summary>
        /// Ends the shop phase (all ready or timeout). Only completed purchases and legal equipped choices remain; nothing is
        /// bought automatically (spec L355). The finalized loadout is resolved.
        /// </summary>
        public DormancyReport Close()
        {
            lock (_player.SyncRoot)
            {
                IsOpen = false;
                return DormancyResolver.Resolve(_player.Loadout, _changes);
            }
        }

        // ---- Internals ---------------------------------------------------------------------------

        private TransactionCode CheckOpen(bool changesLoadout)
        {
            if (!IsOpen) return TransactionCode.ShopClosed;
            if (changesLoadout && _player.Loadout.IsFrozen) return TransactionCode.LoadoutFrozen;
            return TransactionCode.Success;
        }

        // A purchase lowered the balance: remaining cards above it fold, without a free redraw (spec L208).
        private void FoldUnaffordable()
        {
            foreach (var card in _cards)
            {
                if (card.State != ShopCardState.Offered || card.Definition.Price <= _player.Crowns) continue;
                card.State = ShopCardState.OutsideBudget;
                card.ReplacedId = card.Definition.Id;
            }
        }

        // Definitions currently shown on other cards, including folded placeholders.
        private HashSet<string> VisibleIds(int exceptPosition)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < _cards.Length; i++)
            {
                if (i == exceptPosition || _cards[i] == null || _cards[i].Definition == null) continue;
                ids.Add(_cards[i].Definition.Id);
            }
            return ids;
        }

        private ShopCard Snapshot(int position)
        {
            var c = _cards[position];
            return new ShopCard(position, c.State, c.Definition, c.RerollAvailable);
        }

        private ShopResult Fail(TransactionCode code, int position = -1) =>
            new ShopResult(code, _player.Crowns, 0, position >= 0 ? Snapshot(position) : null);

        private ShopResult Process(long requestId, string operation, Func<ShopResult> apply)
        {
            lock (_player.SyncRoot)
            {
                if (!RequestLedger.IsValidId(requestId)) return Fail(TransactionCode.InvalidRequestId);
                string fingerprint = "shop:" + Round.Mode + ":" + Round.Round + ":" + operation;
                if (_player.Requests.TryGet(requestId, fingerprint, out var previous, out bool conflict))
                {
                    if (conflict) return Fail(TransactionCode.RequestIdConflict);
                    return ((ShopResult)previous).AsDuplicate();
                }
                var result = apply();
                _player.Requests.Record(requestId, fingerprint, result);
                return result;
            }
        }

        private static string Describe(Placement? placement) => placement.HasValue ? placement.Value.ToString() : "auto";

        private sealed class CardSlot
        {
            public ShopCardState State;
            public EquipmentDef Definition;
            public bool RerollAvailable = GlobalRules.Economy.RerollsPerOffer > 0;

            /// <summary>The definition this card showed before folding or rerolling; a reroll never returns it.</summary>
            public string ReplacedId;

            public void Show(EquipmentDef def)
            {
                if (Definition != null) ReplacedId = Definition.Id;
                Definition = def;
                State = ShopCardState.Offered;
            }
        }
    }
}
