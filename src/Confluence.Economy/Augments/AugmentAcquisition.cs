using System;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Economy.Eligibility;
using Confluence.Economy.Loadouts;

namespace Confluence.Economy.Augments
{
    /// <summary>Outcome of taking an augment choice.</summary>
    public sealed class AcquisitionResult
    {
        internal AcquisitionResult(TransactionCode code, OwnedAugment augment, OwnedAugment replaced, DormancyReport dormancy)
        {
            Code = code;
            Augment = augment;
            Replaced = replaced;
            Dormancy = dormancy;
        }

        public TransactionCode Code { get; }
        public bool Succeeded => Code == TransactionCode.Success;

        /// <summary>The new or upgraded augment.</summary>
        public OwnedAugment Augment { get; }

        /// <summary>The owned augment removed by a replacement, if any (spec L231).</summary>
        public OwnedAugment Replaced { get; }

        /// <summary>Resolution after the change; null on failure.</summary>
        public DormancyReport Dormancy { get; }
    }

    /// <summary>
    /// Applies one free choice to a loadout (spec L229-L233, L600, L645). A new definition fills a free slot or replaces a
    /// chosen owned augment; replacement gives no Crowns, rerolls, or choices, and the removed augment's dependents become
    /// dormant. A rank 2 improves the owned augment in its slot. Acquisition-only leaves are recorded now and never
    /// re-read (spec L247). The loadout is re-resolved afterwards.
    /// </summary>
    public static class AugmentAcquisition
    {
        /// <summary>
        /// Takes a new definition. targetSlot null uses the first free slot (and fails with ReplacementRequired when all six
        /// are occupied); a slot index places into that slot, replacing its augment if occupied.
        /// </summary>
        public static AcquisitionResult AcquireNew(Loadout loadout, AugmentDef definition, RoundInfo round, IFeatCounts feats,
            int? targetSlot, IStructuralChangeSource changes = null)
        {
            if (loadout == null) throw new ArgumentNullException(nameof(loadout));
            if (definition == null) return Fail(TransactionCode.UnknownItem);
            if (!definition.HasValidRarity) return Fail(TransactionCode.IneligibleAugment);
            var code = ResolveSlot(loadout, targetSlot, out int slot);
            if (code != TransactionCode.Success) return Fail(code);
            code = loadout.CheckNewAugment(definition, slot);
            if (code != TransactionCode.Success) return Fail(code);

            // Judge and record against the frozen pre-choice loadout (spec L227).
            var judge = new CandidateJudge(loadout, round, feats, changes);
            if (!judge.IsEligible(definition)) return Fail(TransactionCode.IneligibleAugment);
            var record = judge.CaptureAcquisition(definition);

            var replaced = loadout.AugmentAt(slot);
            var acquired = new OwnedAugment(definition, 1, record, round);
            loadout.PlaceAugment(slot, acquired);
            var report = DormancyResolver.Resolve(loadout, changes);
            return new AcquisitionResult(TransactionCode.Success, acquired, replaced, report);
        }

        /// <summary>Takes the rank 2 of an owned rank 1 definition, in place (spec L231, L600).</summary>
        public static AcquisitionResult Upgrade(Loadout loadout, string augmentId, Rarity draftRarity, RoundInfo round,
            IStructuralChangeSource changes = null)
        {
            if (loadout == null) throw new ArgumentNullException(nameof(loadout));
            var owned = loadout.FindAugment(augmentId);
            if (owned == null) return Fail(TransactionCode.AugmentNotOwned);
            if (owned.Rank >= GlobalRules.Draft.MaxRank) return Fail(TransactionCode.RankUnavailable);
            if (!Schedules.AllowsRankTwoOffer(owned.OriginalRarity, draftRarity)) return Fail(TransactionCode.RankUnavailable);
            owned.Rank++;
            owned.UpgradedAt = round;
            var report = DormancyResolver.Resolve(loadout, changes);
            return new AcquisitionResult(TransactionCode.Success, owned, null, report);
        }

        /// <summary>The slot a new definition would take, or a failure code.</summary>
        public static TransactionCode ResolveSlot(Loadout loadout, int? targetSlot, out int slot)
        {
            if (targetSlot.HasValue)
            {
                slot = targetSlot.Value;
                return slot >= 0 && slot < GlobalRules.Draft.AugmentSlots ? TransactionCode.Success : TransactionCode.InvalidAugmentSlot;
            }
            slot = loadout.FirstFreeAugmentSlot;
            return slot >= 0 ? TransactionCode.Success : TransactionCode.ReplacementRequired;
        }

        private static AcquisitionResult Fail(TransactionCode code) => new AcquisitionResult(code, null, null, null);
    }
}
