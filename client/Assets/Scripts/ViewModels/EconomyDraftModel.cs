using System;
using System.Collections.Generic;
using Confluence.Client.Laboratory;
using Confluence.Core.Content;
using Confluence.Economy;
using Confluence.Economy.Augments;
using Confluence.Economy.Loadouts;

namespace Confluence.Client.ViewModels
{
    /// <summary>
    /// <see cref="IDraftViewModel"/> over a local Confluence.Economy <see cref="AugmentDraft"/>. Selecting follows the
    /// spec's confirmation flow: with all six slots occupied the player first names the augment to replace (spec L231),
    /// then, if the change would make owned augments dormant, sees that list and confirms (spec L231, L645) before
    /// the request is sent with <c>confirmDormancy</c>. Rerolls replace only their own card (spec L229).
    /// </summary>
    public sealed class EconomyDraftModel : IDraftViewModel
    {
        private readonly AugmentDraft _draft;
        private readonly PlayerEconomy _player;
        private readonly PhaseTimer _timer;
        private readonly Func<long> _nextRequestId;
        private readonly LoadoutBuildModel _build;
        private readonly List<DraftCardView> _cards = new List<DraftCardView>();

        public EconomyDraftModel(AugmentDraft draft, PlayerEconomy player, PhaseTimer timer, Func<long> nextRequestId, string title)
        {
            _draft = draft ?? throw new ArgumentNullException(nameof(draft));
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _timer = timer ?? throw new ArgumentNullException(nameof(timer));
            _nextRequestId = nextRequestId ?? throw new ArgumentNullException(nameof(nextRequestId));
            _build = new LoadoutBuildModel(player);
            Title = title;
            Rebuild();
        }

        public event Action Changed;

        /// <summary>Raised once when the draft ends (selected, passed, or timed out).</summary>
        public event Action Closed;

        public AugmentDraft Draft => _draft;
        public string Title { get; }
        public string Rarity => ContentFormat.RarityName(_draft.Rarity);
        public float SecondsRemaining => _timer.SecondsRemaining;
        public DraftStep Step { get; private set; } = DraftStep.Choosing;
        public IReadOnlyList<DraftCardView> Cards => _cards;
        public DraftPendingView Pending { get; private set; }
        public string GuaranteeNote => _draft.GuaranteeNote;
        public string Message { get; private set; } = "Choose one free augment, or pass. Each card has one free reroll.";
        public bool MessageIsError { get; private set; }
        public IBuildViewModel Build => _build;

        public void Reroll(int position)
        {
            if (Step != DraftStep.Choosing)
            {
                return;
            }

            DraftResult result = _draft.Reroll(_nextRequestId(), position);
            SetMessage(result.Code, result.Succeeded ? "Rerolled card " + (position + 1) + "." : null);
            Rebuild();
        }

        public void Select(int position)
        {
            if (Step != DraftStep.Choosing || position < 0 || position >= _cards.Count)
            {
                return;
            }

            DraftCardView card = _cards[position];
            Pending = new DraftPendingView { Position = position, AugmentName = card.Name };
            if (!card.IsUpgrade && _player.Loadout.FirstFreeAugmentSlot < 0)
            {
                Step = DraftStep.ChoosingReplacement; // spec L231: a new definition with six slots full replaces one
                SetMessage(TransactionCode.ReplacementRequired, null);
                Changed?.Invoke();
                return;
            }

            Preview(position, null);
        }

        public void ChooseReplacement(int slot)
        {
            if (Step != DraftStep.ChoosingReplacement || Pending == null)
            {
                return;
            }

            Pending.TargetSlot = slot;
            Preview(Pending.Position, slot);
        }

        public void Confirm()
        {
            if (Step != DraftStep.Confirming || Pending == null)
            {
                return;
            }

            Send(Pending.Position, Pending.TargetSlot >= 0 ? Pending.TargetSlot : (int?)null, confirm: true);
        }

        public void CancelPending()
        {
            if (Step == DraftStep.Closed)
            {
                return;
            }

            Pending = null;
            Step = DraftStep.Choosing;
            SetMessage(TransactionCode.Success, "Selection cancelled.");
            Changed?.Invoke();
        }

        public void Pass()
        {
            if (Step == DraftStep.Closed)
            {
                return;
            }

            DraftResult result = _draft.Pass(_nextRequestId());
            SetMessage(result.Code, "Passed: no augment chosen and nothing banked (spec L229).");
            AfterRequest();
        }

        /// <summary>The 30 s timer expired: nothing is chosen (spec L229).</summary>
        public void Timeout()
        {
            if (Step == DraftStep.Closed)
            {
                return;
            }

            _draft.Timeout();
            SetMessage(TransactionCode.Success, "Time expired: the build is unchanged (spec L229).");
            AfterRequest();
        }

        public void Tick() => Changed?.Invoke();

        public void Rebuild()
        {
            _cards.Clear();
            Catalogue catalogue = _player.Catalogue;
            IReadOnlyList<DraftCard> cards = _draft.Cards;
            for (int i = 0; i < cards.Count; i++)
            {
                _cards.Add(ToView(cards[i], catalogue));
            }

            _build.Refresh();
            Changed?.Invoke();
        }

        public static DraftCardView ToView(DraftCard card, Catalogue catalogue)
        {
            AugmentDef def = card.Definition;
            string rankText = card.Rank >= 2 && !string.IsNullOrEmpty(def.Rank2) ? def.Rank2 : def.Rank1;
            return new DraftCardView
            {
                Position = card.Position,
                AugmentId = def.Id,
                Name = def.Name,
                Rarity = ContentFormat.RarityName(card.Rarity),
                Rank = card.Rank,
                IsUpgrade = card.IsUpgrade,
                IsTransformation = def.IsTransformation,
                TargetSpell = def.IsTransformation ? "Transforms " + ContentFormat.SpellName(catalogue, def.TargetSpellId) : "Shared augment",
                Eligibility = string.IsNullOrEmpty(card.EligibilityText) ? "Always eligible." : card.EligibilityText,
                Transformation = (card.IsUpgrade ? "Upgrade to rank 2: " : "Rank " + card.Rank + ": ") + rankText,
                Drawback = def.Drawback,
                Counterplay = def.Counterplay,
                RerollAvailable = card.RerollAvailable,
            };
        }

        private void Preview(int position, int? slot)
        {
            SelectionPreview preview = _draft.PreviewSelection(position, slot);
            if (preview.Code != TransactionCode.Success)
            {
                Pending = null;
                Step = DraftStep.Choosing;
                SetMessage(preview.Code, null);
                Changed?.Invoke();
                return;
            }

            Pending.ReplacedName = preview.Replaced != null ? preview.Replaced.Definition.Name : null;
            if (!preview.NeedsConfirmation && preview.Replaced == null)
            {
                Send(position, slot, confirm: false);
                return;
            }

            Pending.BecomeDormant = DormantLines(preview.BecomeDormant);
            Step = DraftStep.Confirming;
            SetMessage(TransactionCode.Success, "Confirm the consequences before selecting.");
            Changed?.Invoke();
        }

        private void Send(int position, int? slot, bool confirm)
        {
            DraftResult result = _draft.Select(_nextRequestId(), position, slot, confirm);
            string name = position >= 0 && position < _cards.Count ? _cards[position].Name : "augment";
            SetMessage(result.Code, result.Succeeded ? "Selected " + name + "." : null);
            if (!result.Succeeded)
            {
                Pending = null;
                Step = DraftStep.Choosing;
                Rebuild();
                return;
            }

            AfterRequest();
        }

        private void AfterRequest()
        {
            Pending = null;
            bool closed = !_draft.IsOpen;
            Step = closed ? DraftStep.Closed : DraftStep.Choosing;
            Rebuild();
            if (closed)
            {
                Closed?.Invoke();
            }
        }

        // "AUG-305 Name: requires ..." for each augment the selection would make dormant.
        private IReadOnlyList<string> DormantLines(IReadOnlyList<string> ids)
        {
            var lines = new List<string>(ids.Count);
            Catalogue catalogue = _player.Catalogue;
            Loadout loadout = _player.Loadout;
            for (int i = 0; i < ids.Count; i++)
            {
                string name = catalogue.AugmentsById.TryGetValue(ids[i], out AugmentDef def) ? def.Name + " (" + def.Id + ")" : ids[i];
                OwnedAugment owned = loadout.FindAugment(ids[i]);
                string reason = owned == null ? " (the selected card would start dormant)" : string.Empty;
                lines.Add(name + reason);
            }

            return lines;
        }

        private void SetMessage(TransactionCode code, string success)
        {
            bool ok = code == TransactionCode.Success;
            Message = ok ? success ?? ContentFormat.Transaction(code) : ContentFormat.Transaction(code);
            MessageIsError = !ok && code != TransactionCode.ReplacementRequired;
        }
    }
}
