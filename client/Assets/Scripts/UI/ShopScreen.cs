using System;
using System.Collections.Generic;
using System.Globalization;
using Confluence.Client.ViewModels;
using UnityEngine.UIElements;

namespace Confluence.Client.UI
{
    /// <summary>
    /// Equipment shop screen (spec L45, L204-L213, L353-L355) from an <see cref="IShopViewModel"/>: Crowns and the
    /// phase timer, three equipment cards (name, tier price, slot, effect, activation, drawback, reroll indicator, and
    /// a label for an inactive component before purchase), Buy and Reroll per card, and the build panel with selling,
    /// reserve moves, the active-item choice, and six-of-eight spell selection; Ready ends the phase early.
    /// Keyboard: Tab/arrows move focus between buttons and Enter or Space activates the focused one; 1-3 buy a card,
    /// Shift+1-3 reroll it.
    /// </summary>
    public sealed class ShopScreen
    {
        private readonly IShopViewModel _model;
        private readonly Label _title;
        private readonly Label _crowns;
        private readonly Label _timer;
        private readonly Label _message;
        private readonly VisualElement _cards;
        private readonly Button _ready;
        private readonly BuildPanel _build;
        private readonly List<CardView> _cardViews = new List<CardView>();

        public ShopScreen(IShopViewModel model, string sessionLabel)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            Root = Ui.Element("cf-screen", "cf-screen--match");
            Root.focusable = true;
            if (!string.IsNullOrEmpty(sessionLabel))
            {
                Root.Add(Ui.Label(sessionLabel, "cf-lab-label"));
            }

            VisualElement header = Ui.Element("cf-match-header");
            _title = Ui.Label(string.Empty, "cf-heading");
            _crowns = Ui.Label(string.Empty, "cf-crowns");
            _timer = Ui.Label(string.Empty, "cf-phase-timer");
            header.Add(_title);
            header.Add(_crowns);
            header.Add(_timer);
            Root.Add(header);

            VisualElement body = Ui.Element("cf-match-body");
            VisualElement left = Ui.Element("cf-match-main");
            _cards = Ui.Element("cf-card-row");
            left.Add(_cards);
            _message = Ui.Label(string.Empty, "cf-match-message");
            left.Add(_message);
            VisualElement footer = Ui.Element("cf-row");
            _ready = Ui.Button("Ready", () => _model.SetReady(!_model.IsReady), "cf-button--primary", "cf-button--wide");
            footer.Add(_ready);
            footer.Add(Ui.Muted("Keys: 1-3 buy, Shift+1-3 reroll, Tab/arrows and Enter navigate. Selling refunds 50% (spec L212)."));
            left.Add(footer);
            body.Add(left);

            var actions = new BuildPanelActions
            {
                Sell = id => _model.Sell(id),
                ToggleReserve = id => _model.ToggleReserve(id),
                SwapSpells = (equipped, bench) => _model.SwapSpells(equipped, bench),
                SetActive = id => _model.SetEnabledActive(id),
            };
            _build = new BuildPanel(_model.Build, actions);
            _build.Root.AddToClassList("cf-match-side");
            body.Add(_build.Root);
            Root.Add(body);

            for (int i = 0; i < 3; i++)
            {
                var card = new CardView(i, _model);
                _cardViews.Add(card);
                _cards.Add(card.Root);
            }

            Root.RegisterCallback<KeyDownEvent>(OnKey);
            _model.Changed += Refresh;
            Refresh();
        }

        public VisualElement Root { get; }

        public void Detach()
        {
            _model.Changed -= Refresh;
            _build.Detach();
        }

        public void Refresh()
        {
            _title.text = _model.Title;
            _crowns.text = ContentFormat.Crowns(_model.Crowns);
            _timer.text = (_model.IsOpen ? "Shop closes in " : "Shop closed ") + _model.SecondsRemaining.ToString("0", CultureInfo.InvariantCulture) + " s"
                + (_model.TimerPaused ? " (paused, Laboratory)" : string.Empty);
            _message.text = _model.Message ?? string.Empty;
            _message.EnableInClassList("cf-match-message--error", _model.MessageIsError);
            _ready.text = _model.IsReady ? "Ready (click to cancel)" : "Ready";
            _ready.EnableInClassList("cf-button--selected", _model.IsReady);
            IReadOnlyList<ShopCardView> cards = _model.Cards;
            for (int i = 0; i < _cardViews.Count; i++)
            {
                _cardViews[i].Set(i < cards.Count ? cards[i] : null);
            }
        }

        public void FocusFirstCard() => _cardViews[0].FocusBuy();

        private void OnKey(KeyDownEvent e)
        {
            int index = e.keyCode >= UnityEngine.KeyCode.Alpha1 && e.keyCode <= UnityEngine.KeyCode.Alpha3 ? e.keyCode - UnityEngine.KeyCode.Alpha1 : -1;
            if (index >= 0)
            {
                if (e.shiftKey)
                {
                    _model.Reroll(index);
                }
                else
                {
                    _model.Buy(index);
                }

                e.StopPropagation();
            }
        }

        private sealed class CardView
        {
            private readonly IShopViewModel _model;
            private readonly int _position;
            private readonly Label _name;
            private readonly Label _price;
            private readonly Label _slot;
            private readonly Label _effect;
            private readonly Label _activation;
            private readonly Label _drawback;
            private readonly Label _notice;
            private readonly Label _reroll;
            private readonly Button _buy;
            private readonly Button _rerollButton;

            public CardView(int position, IShopViewModel model)
            {
                _position = position;
                _model = model;
                Root = Ui.Element("cf-card");
                _name = Ui.Label(string.Empty, "cf-card-name");
                _price = Ui.Label(string.Empty, "cf-card-price");
                _slot = Ui.Label(string.Empty, "cf-card-meta");
                _effect = Ui.Label(string.Empty, "cf-card-text");
                _activation = Ui.Label(string.Empty, "cf-card-small");
                _drawback = Ui.Label(string.Empty, "cf-card-drawback");
                _notice = Ui.Label(string.Empty, "cf-card-notice");
                _reroll = Ui.Label(string.Empty, "cf-card-reroll");
                Root.Add(Ui.Label("Card " + (position + 1), "cf-card-index"));
                Root.Add(_name);
                Root.Add(_price);
                Root.Add(_slot);
                Root.Add(_effect);
                Root.Add(_activation);
                Root.Add(_drawback);
                Root.Add(_notice);
                VisualElement spacer = Ui.Element("cf-grow");
                Root.Add(spacer);
                Root.Add(_reroll);
                VisualElement buttons = Ui.Element("cf-row");
                _buy = Ui.Button("Buy", () => _model.Buy(_position), "cf-button--small", "cf-button--primary");
                _rerollButton = Ui.Button("Reroll", () => _model.Reroll(_position), "cf-button--small");
                buttons.Add(_buy);
                buttons.Add(_rerollButton);
                Root.Add(buttons);
            }

            public VisualElement Root { get; }

            public void FocusBuy() => _buy.Focus();

            public void Set(ShopCardView card)
            {
                if (card == null)
                {
                    Root.style.display = DisplayStyle.None;
                    return;
                }

                Root.style.display = DisplayStyle.Flex;
                Root.EnableInClassList("cf-card--purchased", card.Status == ShopCardStatus.Purchased);
                Root.EnableInClassList("cf-card--muted", card.Status == ShopCardStatus.OutsideBudget || card.Status == ShopCardStatus.SaveCrowns);
                _name.text = card.Name ?? string.Empty;
                _price.text = card.Tier > 0 ? "Tier " + card.Tier + "  -  " + ContentFormat.Crowns(card.Price) + (string.IsNullOrEmpty(card.Category) ? string.Empty : "  -  " + card.Category) : string.Empty;
                _slot.text = string.IsNullOrEmpty(card.SlotText) ? string.Empty : "Slot: " + card.SlotText;
                _effect.text = card.Effect ?? string.Empty;
                _activation.text = string.IsNullOrEmpty(card.Activation) ? string.Empty : "Activation: " + card.Activation;
                _drawback.text = string.IsNullOrEmpty(card.Drawback) ? string.Empty : "Drawback: " + card.Drawback;
                _notice.text = card.Notice ?? string.Empty;
                _notice.style.display = string.IsNullOrEmpty(card.Notice) ? DisplayStyle.None : DisplayStyle.Flex;
                _reroll.text = card.RerollAvailable ? "Free reroll available" : "Reroll used";
                _reroll.EnableInClassList("cf-card-reroll--used", !card.RerollAvailable);
                _buy.SetEnabled(card.CanBuy);
                _rerollButton.SetEnabled(card.CanReroll);
            }
        }
    }
}
