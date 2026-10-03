using System;
using Confluence.Economy.Augments;
using Confluence.Economy.Shop;
using Confluence.Hosting.Commands;
using Confluence.Match;
using Confluence.Net.Game.Client;
using Confluence.Net.Game.Wire;
using Confluence.Net.Requests;

namespace Confluence.Net.Game.Headless
{
    /// <summary>
    /// Simple shop and draft heuristics over the client's private views, sent as ordinary requests (spec L429): in the
    /// shop buy up to <see cref="MaxPurchasesPerShop"/> affordable cards, most expensive first, then ready; in the draft
    /// select the first card (confirming the listed dormancy if the server asks for it), else pass. One request is in
    /// flight at a time and every decision waits a short think interval, like the server bots.
    /// </summary>
    public sealed class HeadlessEconomy
    {
        public const int MaxPurchasesPerShop = 2;

        private readonly GameClient _client;
        private readonly int _thinkUpdates;
        private int _cooldown;
        private bool _inFlight;
        private string _shopKey;
        private string _draftKey;
        private int _purchases;
        private bool _readied;
        private bool _drafted;
        private bool _confirmNext;

        public HeadlessEconomy(GameClient client, int thinkUpdates = 3)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _thinkUpdates = Math.Max(0, thinkUpdates);
        }

        public int Purchases { get; private set; }
        public int Readies { get; private set; }
        public int DraftSelections { get; private set; }
        public int DraftPasses { get; private set; }
        public int Rejections { get; private set; }

        /// <summary>Call once per client update.</summary>
        public void Act()
        {
            if (_inFlight) return;
            if (_cooldown > 0)
            {
                _cooldown--;
                return;
            }

            ClientMatchView view = _client.View;
            if (view.IsEliminated) return;
            MatchPhase phase = view.CurrentPhase;
            if (phase == MatchPhase.Shop && view.Shop != null && view.Shop.Open) ActShop(view);
            else if (phase == MatchPhase.Draft && view.Draft != null && view.Draft.Open && view.Draft.Outcome == DraftOutcome.Open) ActDraft(view);
        }

        private void ActShop(ClientMatchView view)
        {
            ShopStateMessage shop = view.Shop;
            string key = "r" + shop.Round + "@" + (view.Phase != null ? view.Phase.StartTick : 0);
            if (key != _shopKey)
            {
                _shopKey = key;
                _purchases = 0;
                _readied = false;
            }

            if (_readied || shop.Ready) return;
            if (_purchases < MaxPurchasesPerShop)
            {
                ShopCardWire best = null;
                foreach (ShopCardWire card in shop.Cards)
                {
                    if (card.State != ShopCardState.Offered || card.Price > shop.Crowns) continue;
                    if (best == null || card.Price > best.Price) best = card;
                }

                if (best != null)
                {
                    _purchases++;
                    Send(CommandRequest.Buy(best.Position), r =>
                    {
                        if (r.IsOk) Purchases++;
                    });
                    return;
                }
            }

            _readied = true;
            Send(CommandRequest.Ready(), r =>
            {
                if (r.IsOk) Readies++;
                else _readied = false;
            });
        }

        private void ActDraft(ClientMatchView view)
        {
            DraftStateMessage draft = view.Draft;
            string key = "r" + draft.Round + "@" + (view.Phase != null ? view.Phase.StartTick : 0);
            if (key != _draftKey)
            {
                _draftKey = key;
                _drafted = false;
                _confirmNext = false;
            }

            if (_drafted) return;
            if (draft.Cards.Count == 0)
            {
                _drafted = true;
                Send(CommandRequest.DraftPass(), r => { if (r.IsOk) DraftPasses++; });
                return;
            }

            bool confirm = _confirmNext;
            _drafted = true;
            Send(CommandRequest.DraftSelect(draft.Cards[0].Position, null, confirm), r =>
            {
                if (r.IsOk)
                {
                    DraftSelections++;
                    return;
                }

                // A selection the server refused (dormancy needs confirmation, slots full): confirm once, then pass.
                if (!confirm)
                {
                    _confirmNext = true;
                    _drafted = false;
                    return;
                }

                Send(CommandRequest.DraftPass(), p => { if (p.IsOk) DraftPasses++; });
            });
        }

        private void Send(CommandRequest command, Action<RequestResult<CommandResponse>> done)
        {
            _inFlight = true;
            _cooldown = _thinkUpdates;
            _client.Send(command, r =>
            {
                _inFlight = false;
                if (!r.IsOk) Rejections++;
                done?.Invoke(r);
            });
        }
    }
}
