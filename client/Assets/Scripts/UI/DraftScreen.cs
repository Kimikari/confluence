using System;
using System.Collections.Generic;
using System.Globalization;
using Confluence.Client.ViewModels;
using UnityEngine.UIElements;

namespace Confluence.Client.UI
{
    /// <summary>
    /// Free augment draft screen (spec L214-L233, L353) from an <see cref="IDraftViewModel"/>: five cards showing
    /// rarity, rank, target spell, eligibility, the transformation text at the offered rank, drawback, and a per-card
    /// reroll indicator; Select and Reroll per card; Pass. With six augments owned, selecting first asks which one to
    /// replace; a selection that makes owned augments dormant lists them and must be confirmed (spec L231, L645).
    /// The build panel (read-only) stays beside the cards. Keyboard: 1-5 select, Shift+1-5 reroll, P pass,
    /// Tab/arrows and Enter navigate, Esc cancels a pending confirmation.
    /// </summary>
    public sealed class DraftScreen
    {
        private readonly IDraftViewModel _model;
        private readonly Label _title;
        private readonly Label _timer;
        private readonly Label _note;
        private readonly Label _message;
        private readonly VisualElement _cards;
        private readonly VisualElement _dialog;
        private readonly BuildPanel _build;
        private readonly List<CardView> _cardViews = new List<CardView>();
        private DraftStep _shownStep = (DraftStep)(-1);

        public DraftScreen(IDraftViewModel model, string sessionLabel)
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
            _timer = Ui.Label(string.Empty, "cf-phase-timer");
            header.Add(_title);
            header.Add(_timer);
            Root.Add(header);

            VisualElement body = Ui.Element("cf-match-body");
            VisualElement main = Ui.Element("cf-match-main");
            _note = Ui.Label(string.Empty, "cf-muted");
            main.Add(_note);
            _cards = Ui.Element("cf-card-row", "cf-card-row--draft");
            main.Add(_cards);
            _message = Ui.Label(string.Empty, "cf-match-message");
            main.Add(_message);
            VisualElement footer = Ui.Element("cf-row");
            footer.Add(Ui.Button("Pass", () => _model.Pass(), "cf-button--small"));
            footer.Add(Ui.Muted("Keys: 1-5 select, Shift+1-5 reroll, P pass. Passing banks nothing (spec L229)."));
            main.Add(footer);
            _dialog = Ui.Element("cf-draft-dialog");
            main.Add(_dialog);
            body.Add(main);

            _build = new BuildPanel(_model.Build);
            _build.Root.AddToClassList("cf-match-side");
            body.Add(_build.Root);
            Root.Add(body);

            for (int i = 0; i < 5; i++)
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
            _timer.text = _model.Step == DraftStep.Closed ? "Draft closed" : "Draft closes in " + _model.SecondsRemaining.ToString("0", CultureInfo.InvariantCulture) + " s";
            _note.text = "Rarity: " + _model.Rarity + ". " + (_model.GuaranteeNote ?? "Each card has its own free reroll; a reroll never repeats a card shown in this draft.");
            _message.text = _model.Message ?? string.Empty;
            _message.EnableInClassList("cf-match-message--error", _model.MessageIsError);
            IReadOnlyList<DraftCardView> cards = _model.Cards;
            bool choosing = _model.Step == DraftStep.Choosing;
            for (int i = 0; i < _cardViews.Count; i++)
            {
                _cardViews[i].Set(i < cards.Count ? cards[i] : null, choosing);
            }

            if (_shownStep != _model.Step || _model.Step != DraftStep.Choosing)
            {
                BuildDialog();
            }

            _shownStep = _model.Step;
        }

        private void BuildDialog()
        {
            _dialog.Clear();
            DraftPendingView pending = _model.Pending;
            switch (_model.Step)
            {
                case DraftStep.ChoosingReplacement:
                    _dialog.style.display = DisplayStyle.Flex;
                    _dialog.Add(Ui.Label("All six augment slots are occupied. Choose the augment " + pending?.AugmentName + " replaces:", "cf-text"));
                    foreach (AugmentSlotView slot in _model.Build.Augments)
                    {
                        if (slot.Empty)
                        {
                            continue;
                        }

                        int index = slot.Slot;
                        _dialog.Add(Ui.Button("Replace slot " + (index + 1) + ": " + slot.Name + (slot.Dormant ? " (dormant)" : string.Empty),
                            () => _model.ChooseReplacement(index), "cf-button--small"));
                    }

                    _dialog.Add(Ui.Button("Cancel", () => _model.CancelPending(), "cf-button--small"));
                    break;
                case DraftStep.Confirming:
                    _dialog.style.display = DisplayStyle.Flex;
                    _dialog.Add(Ui.Label("Confirm selecting " + pending?.AugmentName, "cf-build-title"));
                    if (!string.IsNullOrEmpty(pending?.ReplacedName))
                    {
                        _dialog.Add(Ui.Label("Replaces and removes: " + pending.ReplacedName, "cf-text"));
                    }

                    if (pending != null && pending.BecomeDormant.Count > 0)
                    {
                        _dialog.Add(Ui.Label("These augments become dormant (they stay owned and wake when their requirements return):", "cf-text"));
                        foreach (string line in pending.BecomeDormant)
                        {
                            _dialog.Add(Ui.Label("- " + line, "cf-build-dormant"));
                        }
                    }
                    else
                    {
                        _dialog.Add(Ui.Muted("No other augment becomes dormant."));
                    }

                    VisualElement buttons = Ui.Element("cf-row");
                    buttons.Add(Ui.Button("Confirm", () => _model.Confirm(), "cf-button--small", "cf-button--primary"));
                    buttons.Add(Ui.Button("Cancel", () => _model.CancelPending(), "cf-button--small"));
                    _dialog.Add(buttons);
                    break;
                default:
                    _dialog.style.display = DisplayStyle.None;
                    break;
            }
        }

        private void OnKey(KeyDownEvent e)
        {
            if (e.keyCode == UnityEngine.KeyCode.Escape && _model.Step != DraftStep.Choosing)
            {
                _model.CancelPending();
                e.StopPropagation();
                return;
            }

            if (e.keyCode == UnityEngine.KeyCode.P)
            {
                _model.Pass();
                e.StopPropagation();
                return;
            }

            int index = e.keyCode >= UnityEngine.KeyCode.Alpha1 && e.keyCode <= UnityEngine.KeyCode.Alpha5 ? e.keyCode - UnityEngine.KeyCode.Alpha1 : -1;
            if (index >= 0)
            {
                if (e.shiftKey)
                {
                    _model.Reroll(index);
                }
                else
                {
                    _model.Select(index);
                }

                e.StopPropagation();
            }
        }

        private sealed class CardView
        {
            private readonly IDraftViewModel _model;
            private readonly int _position;
            private readonly Label _rarity;
            private readonly Label _name;
            private readonly Label _target;
            private readonly Label _transformation;
            private readonly Label _eligibility;
            private readonly Label _drawback;
            private readonly Label _reroll;
            private readonly Button _select;
            private readonly Button _rerollButton;

            public CardView(int position, IDraftViewModel model)
            {
                _position = position;
                _model = model;
                Root = Ui.Element("cf-card", "cf-card--augment");
                _rarity = Ui.Label(string.Empty, "cf-card-rarity");
                _name = Ui.Label(string.Empty, "cf-card-name");
                _target = Ui.Label(string.Empty, "cf-card-meta");
                _transformation = Ui.Label(string.Empty, "cf-card-text");
                _eligibility = Ui.Label(string.Empty, "cf-card-small");
                _drawback = Ui.Label(string.Empty, "cf-card-drawback");
                _reroll = Ui.Label(string.Empty, "cf-card-reroll");
                Root.Add(_rarity);
                Root.Add(_name);
                Root.Add(_target);
                Root.Add(_transformation);
                Root.Add(_eligibility);
                Root.Add(_drawback);
                Root.Add(Ui.Element("cf-grow"));
                Root.Add(_reroll);
                VisualElement buttons = Ui.Element("cf-row");
                _select = Ui.Button("Select", () => _model.Select(_position), "cf-button--small", "cf-button--primary");
                _rerollButton = Ui.Button("Reroll", () => _model.Reroll(_position), "cf-button--small");
                buttons.Add(_select);
                buttons.Add(_rerollButton);
                Root.Add(buttons);
            }

            public VisualElement Root { get; }

            public void Set(DraftCardView card, bool choosing)
            {
                if (card == null)
                {
                    Root.style.display = DisplayStyle.None;
                    return;
                }

                Root.style.display = DisplayStyle.Flex;
                foreach (string rarity in new[] { "silver", "gold", "prismatic" })
                {
                    Root.EnableInClassList("cf-card--" + rarity, string.Equals(card.Rarity, rarity, StringComparison.OrdinalIgnoreCase));
                }

                _rarity.text = (_position + 1) + "  " + card.Rarity.ToUpperInvariant() + "  -  rank " + card.Rank + (card.IsUpgrade ? " (upgrade)" : string.Empty)
                    + (card.IsTransformation ? "  -  transformation" : string.Empty);
                _name.text = card.Name + "  (" + card.AugmentId + ")";
                _target.text = card.TargetSpell;
                _transformation.text = card.Transformation;
                _eligibility.text = "Eligibility: " + card.Eligibility;
                _drawback.text = string.IsNullOrEmpty(card.Drawback) ? string.Empty : "Drawback: " + card.Drawback;
                _reroll.text = card.RerollAvailable ? "Free reroll available" : "Reroll used";
                _reroll.EnableInClassList("cf-card-reroll--used", !card.RerollAvailable);
                _select.SetEnabled(choosing);
                _rerollButton.SetEnabled(choosing && card.RerollAvailable);
            }
        }
    }
}
