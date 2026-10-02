using System;
using System.Collections.Generic;
using System.Globalization;
using Confluence.Core.Content;
using Confluence.Core.Random;
using Confluence.Core.Rules;
using Confluence.Economy.Eligibility;
using Confluence.Economy.Loadouts;

namespace Confluence.Economy.Augments
{
    /// <summary>One of the five free augment cards (spec L353: rarity, rank, target spell, eligibility, reroll indicator).</summary>
    public sealed class DraftCard
    {
        internal DraftCard(int position, AugmentOffer offer, bool rerollAvailable, string eligibilityText)
        {
            Position = position;
            Offer = offer;
            RerollAvailable = rerollAvailable;
            EligibilityText = eligibilityText;
        }

        public int Position { get; }
        public AugmentOffer Offer { get; }
        public AugmentDef Definition => Offer.Definition;
        public string AugmentId => Offer.Id;
        public int Rank => Offer.Rank;
        public bool IsUpgrade => Offer.IsUpgrade;
        public Rarity Rarity => Offer.Definition.RarityEnum;
        public string TargetSpellId => Offer.Definition.TargetSpellId;

        /// <summary>The card's one free reroll is still unused (spec L229).</summary>
        public bool RerollAvailable { get; }

        /// <summary>Plain-language eligibility of the definition (spec L237).</summary>
        public string EligibilityText { get; }

        public override string ToString() => Position + ":" + Offer + (RerollAvailable ? "" : " (rerolled)");
    }

    public enum DraftOutcome
    {
        Open,
        Selected,
        Passed,
        TimedOut,
    }

    /// <summary>What selecting a card would do to the build, shown for confirmation before selection (spec L231, L645).</summary>
    public sealed class SelectionPreview
    {
        internal SelectionPreview(TransactionCode code, OwnedAugment replaced, IReadOnlyList<string> becomeDormant)
        {
            Code = code;
            Replaced = replaced;
            BecomeDormant = becomeDormant ?? Array.AsReadOnly(new string[0]);
        }

        /// <summary>Success when the selection is possible as described.</summary>
        public TransactionCode Code { get; }

        /// <summary>The owned augment that would be removed, if the selection replaces one.</summary>
        public OwnedAugment Replaced { get; }

        /// <summary>Owned augments (and possibly the chosen one) that would be dormant afterwards but are active now.</summary>
        public IReadOnlyList<string> BecomeDormant { get; }

        public bool NeedsConfirmation => BecomeDormant.Count > 0;
    }

    /// <summary>Outcome of a draft request.</summary>
    public sealed class DraftResult
    {
        internal DraftResult(TransactionCode code, DraftCard card, AcquisitionResult acquisition, SelectionPreview preview, bool isDuplicate = false)
        {
            Code = code;
            Card = card;
            Acquisition = acquisition;
            Preview = preview;
            IsDuplicate = isDuplicate;
        }

        public TransactionCode Code { get; }
        public bool Succeeded => Code == TransactionCode.Success;

        /// <summary>The affected card after the operation (the new card after a reroll).</summary>
        public DraftCard Card { get; }

        /// <summary>The applied choice after a successful selection.</summary>
        public AcquisitionResult Acquisition { get; }

        /// <summary>For ConfirmationRequired: the consequences the player must confirm.</summary>
        public SelectionPreview Preview { get; }

        /// <summary>The request ID was already processed; this is the stored result and nothing was re-applied.</summary>
        public bool IsDuplicate { get; }

        internal DraftResult AsDuplicate() => new DraftResult(Code, Card, Acquisition, Preview, true);
    }

    /// <summary>
    /// One player's 30 second five-card augment draft in a scheduled draft round (spec L214-L233, L594, L645).
    /// The loadout is frozen while it is open. Opening shows five distinct eligible cards: at least one current-wizard spell
    /// transformation when one is eligible, at least two universal or broadly conditional shared alternatives, and the rest
    /// sampled from the full eligible pool. Each card has one free reroll that replaces only that card and never shows a
    /// definition already shown in this draft or currently visible. Selecting one card ends the draft; passing or timing
    /// out chooses nothing and banks nothing. All randomness comes from the player's Draft stream.
    /// </summary>
    public sealed class AugmentDraft
    {
        private readonly PlayerEconomy _player;
        private readonly IContentAvailability _availability;
        private readonly IStructuralChangeSource _changes;
        private readonly Rng _rng;
        private readonly IReadOnlyList<AugmentOffer> _pool;
        private readonly DraftCard[] _cards;
        private readonly HashSet<string> _shown = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _shownOrder = new List<string>();

        private AugmentDraft(PlayerEconomy player, RoundInfo round, Rarity rarity, ulong matchSeed, IContentAvailability availability, IStructuralChangeSource changes)
        {
            _player = player;
            _availability = availability;
            _changes = changes ?? StructuralChangeSources.Default;
            Round = round;
            Rarity = rarity;
            _rng = RngStreams.For(matchSeed, RngStream.Draft, round.Round, player.PlayerSlot);

            var loadout = player.Loadout;
            DormancyResolver.Resolve(loadout, _changes); // judge against the finalized loadout
            loadout.IsFrozen = true;                     // spec L227 "Freeze the player's equipment and equipped spells"
            _pool = AugmentPool.Eligible(loadout, round, rarity, player.Feats, availability, _changes);
            _cards = OpenCards(out bool transformationShown, out bool transformationEligible);
            TransformationGuaranteeMet = transformationShown;
            TransformationEligible = transformationEligible;
        }

        /// <summary>Opens the round's draft, or returns false when the round schedules none (spec L216-L225).</summary>
        public static bool TryOpen(PlayerEconomy player, RoundInfo round, ulong matchSeed, IContentAvailability availability,
            out AugmentDraft draft, IStructuralChangeSource changes = null)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (round == null) throw new ArgumentNullException(nameof(round));
            if (availability == null) throw new ArgumentNullException(nameof(availability));
            draft = null;
            if (!round.TryGetDraftRarity(out var rarity)) return false;
            lock (player.SyncRoot)
            {
                if (player.Loadout.IsFrozen) throw new InvalidOperationException("A draft is already open for this player.");
                draft = new AugmentDraft(player, round, rarity, matchSeed, availability, changes);
            }
            return true;
        }

        public RoundInfo Round { get; }
        public Rarity Rarity { get; }
        public DraftOutcome Outcome { get; private set; } = DraftOutcome.Open;
        public bool IsOpen => Outcome == DraftOutcome.Open;

        /// <summary>Current cards in position order.</summary>
        public IReadOnlyList<DraftCard> Cards => Array.AsReadOnly((DraftCard[])_cards.Clone());

        /// <summary>Every definition shown in this draft so far, in order.</summary>
        public IReadOnlyList<string> ShownDefinitions => _shownOrder.AsReadOnly();

        /// <summary>Legal alternatives available to this draft (the spec L233 floor requires at least ten).</summary>
        public int EligiblePoolSize => _pool.Count;

        public bool MeetsFloor => _pool.Count >= GlobalRules.Draft.MinLegalAlternativesPerRarity;

        /// <summary>A current-wizard spell transformation was eligible at opening.</summary>
        public bool TransformationEligible { get; }

        /// <summary>The opening included a transformation. When none was eligible an ordinary choice replaced it (spec L227).</summary>
        public bool TransformationGuaranteeMet { get; }

        /// <summary>Training-mode explanation when the transformation guarantee was replaced (spec L227 "explain it in training, not as an error").</summary>
        public string GuaranteeNote => TransformationEligible
            ? null
            : "No spell transformation is eligible with this loadout, so an ordinary eligible choice took its place.";

        /// <summary>The chosen augment after a selection.</summary>
        public OwnedAugment Selected { get; private set; }

        // ---- Opening -----------------------------------------------------------------------------

        private DraftCard[] OpenCards(out bool transformationShown, out bool transformationEligible)
        {
            var chosen = new List<AugmentOffer>(GlobalRules.Draft.Candidates);
            var transformations = Filter(o => o.IsTransformation, chosen);
            transformationEligible = transformations.Count > 0;
            transformationShown = false;
            for (int i = 0; i < GlobalRules.Draft.GuaranteedTransformations && transformations.Count > 0; i++)
            {
                chosen.Add(TakeRandom(transformations));
                transformationShown = true;
            }
            var broad = Filter(o => o.IsBroad, chosen);
            for (int i = 0; i < GlobalRules.Draft.GuaranteedBroadAlternatives && broad.Count > 0; i++)
                chosen.Add(TakeRandom(broad));
            var rest = Filter(o => true, chosen);
            while (chosen.Count < GlobalRules.Draft.Candidates && rest.Count > 0)
                chosen.Add(TakeRandom(rest));

            _rng.Shuffle(chosen); // positions carry no category tell
            var cards = new DraftCard[chosen.Count];
            for (int i = 0; i < cards.Length; i++)
            {
                cards[i] = MakeCard(i, chosen[i], true);
                MarkShown(chosen[i].Id);
            }
            return cards;
        }

        private List<AugmentOffer> Filter(Func<AugmentOffer, bool> predicate, List<AugmentOffer> excluded)
        {
            var list = new List<AugmentOffer>();
            foreach (var offer in _pool)
            {
                if (!predicate(offer) || _shown.Contains(offer.Id)) continue;
                bool taken = false;
                foreach (var e in excluded)
                    if (e.Id == offer.Id) taken = true;
                if (!taken) list.Add(offer);
            }
            return list;
        }

        private AugmentOffer TakeRandom(List<AugmentOffer> list)
        {
            int index = _rng.NextInt(list.Count);
            var offer = list[index];
            list.RemoveAt(index);
            return offer;
        }

        private DraftCard MakeCard(int position, AugmentOffer offer, bool rerollAvailable)
        {
            string text = _player.Catalogue.DescribeEligibility(offer.Definition.Eligibility);
            return new DraftCard(position, offer, rerollAvailable, text);
        }

        private void MarkShown(string id)
        {
            if (_shown.Add(id)) _shownOrder.Add(id);
        }

        // ---- Requests ----------------------------------------------------------------------------

        /// <summary>Uses a card's single free reroll (spec L229).</summary>
        public DraftResult Reroll(long requestId, int position)
        {
            return Process(requestId, "reroll:" + position, () =>
            {
                if (!IsOpen) return new DraftResult(TransactionCode.DraftClosed, null, null, null);
                if (position < 0 || position >= _cards.Length) return new DraftResult(TransactionCode.InvalidCardPosition, null, null, null);
                var card = _cards[position];
                if (!card.RerollAvailable) return new DraftResult(TransactionCode.RerollAlreadyUsed, card, null, null);

                // Excludes every definition shown previously in this draft, which includes all current cards.
                var candidates = Filter(o => true, new List<AugmentOffer>());
                if (candidates.Count == 0) return new DraftResult(TransactionCode.NoAlternative, card, null, null);
                var offer = TakeRandom(candidates);
                var replacement = MakeCard(position, offer, false);
                _cards[position] = replacement;
                MarkShown(offer.Id);
                return new DraftResult(TransactionCode.Success, replacement, null, null);
            });
        }

        /// <summary>
        /// The consequences of selecting a card into a target slot (null: the first free slot), without changing anything.
        /// </summary>
        public SelectionPreview PreviewSelection(int position, int? targetSlot)
        {
            lock (_player.SyncRoot) return Preview(position, targetSlot);
        }

        private SelectionPreview Preview(int position, int? targetSlot)
        {
            if (!IsOpen) return new SelectionPreview(TransactionCode.DraftClosed, null, null);
            if (position < 0 || position >= _cards.Length) return new SelectionPreview(TransactionCode.InvalidCardPosition, null, null);
            var card = _cards[position];
            var loadout = _player.Loadout;
            var copy = loadout.Clone();
            AcquisitionResult trial;
            if (card.IsUpgrade)
            {
                trial = AugmentAcquisition.Upgrade(copy, card.AugmentId, Rarity, Round, _changes);
            }
            else
            {
                var code = AugmentAcquisition.ResolveSlot(loadout, targetSlot, out int slot);
                if (code != TransactionCode.Success) return new SelectionPreview(code, null, null);
                trial = AugmentAcquisition.AcquireNew(copy, card.Definition, Round, _player.Feats, slot, _changes);
            }
            if (!trial.Succeeded) return new SelectionPreview(trial.Code, null, null);

            var becomeDormant = new List<string>();
            foreach (var status in trial.Dormancy.Statuses)
            {
                var before = loadout.FindAugment(status.Id);
                bool activeBefore = before == null || before.IsActive; // the chosen card counts as active before
                if (activeBefore && status.IsDormant) becomeDormant.Add(status.Id);
            }
            return new SelectionPreview(TransactionCode.Success, trial.Replaced, becomeDormant.AsReadOnly());
        }

        /// <summary>
        /// Selects a card, ending the draft (spec L229). A new definition goes to targetSlot, or the first free slot when null;
        /// with all six slots occupied a slot to replace must be named. When the change would make augments dormant the player
        /// must pass confirmDormancy (spec L231 "The player confirms that consequence before selection").
        /// </summary>
        public DraftResult Select(long requestId, int position, int? targetSlot = null, bool confirmDormancy = false)
        {
            string fingerprint = "select:" + position + ":" + (targetSlot.HasValue ? targetSlot.Value.ToString(CultureInfo.InvariantCulture) : "auto") + ":" + confirmDormancy;
            return Process(requestId, fingerprint, () =>
            {
                var preview = Preview(position, targetSlot);
                if (preview.Code != TransactionCode.Success) return new DraftResult(preview.Code, CardAt(position), null, preview);
                if (preview.NeedsConfirmation && !confirmDormancy)
                    return new DraftResult(TransactionCode.ConfirmationRequired, CardAt(position), null, preview);

                var card = _cards[position];
                var loadout = _player.Loadout;
                AcquisitionResult result = card.IsUpgrade
                    ? AugmentAcquisition.Upgrade(loadout, card.AugmentId, Rarity, Round, _changes)
                    : AugmentAcquisition.AcquireNew(loadout, card.Definition, Round, _player.Feats, targetSlot, _changes);
                if (!result.Succeeded) return new DraftResult(result.Code, card, result, preview);
                Selected = result.Augment;
                Close(DraftOutcome.Selected);
                return new DraftResult(TransactionCode.Success, card, result, preview);
            });
        }

        /// <summary>Passing is legal but grants no banked token (spec L229).</summary>
        public DraftResult Pass(long requestId)
        {
            return Process(requestId, "pass", () =>
            {
                if (!IsOpen) return new DraftResult(TransactionCode.DraftClosed, null, null, null);
                Close(DraftOutcome.Passed);
                return new DraftResult(TransactionCode.Success, null, null, null);
            });
        }

        /// <summary>The 30 second timer expired: keep the existing build and choose nothing (spec L229).</summary>
        public void Timeout()
        {
            lock (_player.SyncRoot)
            {
                if (IsOpen) Close(DraftOutcome.TimedOut);
            }
        }

        private DraftCard CardAt(int position) => position >= 0 && position < _cards.Length ? _cards[position] : null;

        private void Close(DraftOutcome outcome)
        {
            Outcome = outcome;
            _player.Loadout.IsFrozen = false;
        }

        private DraftResult Process(long requestId, string operation, Func<DraftResult> apply)
        {
            lock (_player.SyncRoot)
            {
                if (!RequestLedger.IsValidId(requestId)) return new DraftResult(TransactionCode.InvalidRequestId, null, null, null);
                string fingerprint = "draft:" + Round.Mode + ":" + Round.Round + ":" + operation;
                if (_player.Requests.TryGet(requestId, fingerprint, out var previous, out bool conflict))
                {
                    if (conflict) return new DraftResult(TransactionCode.RequestIdConflict, null, null, null);
                    return ((DraftResult)previous).AsDuplicate();
                }
                var result = apply();
                _player.Requests.Record(requestId, fingerprint, result);
                return result;
            }
        }
    }
}
