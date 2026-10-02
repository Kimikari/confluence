using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Economy.Eligibility;
using Confluence.Economy.Loadouts;

namespace Confluence.Economy.Augments
{
    /// <summary>A definition the draft may show: a new rank 1 definition or the rank 2 of an owned one.</summary>
    public sealed class AugmentOffer
    {
        internal AugmentOffer(AugmentDef definition, int rank)
        {
            Definition = definition;
            Rank = rank;
        }

        public AugmentDef Definition { get; }
        public string Id => Definition.Id;

        /// <summary>1 for a new definition, 2 for the upgrade of an owned rank 1 (spec L231).</summary>
        public int Rank { get; }

        public bool IsUpgrade => Rank > 1;
        public bool IsTransformation => Definition.IsTransformation;

        /// <summary>A universal or broadly conditional shared alternative (spec L227).</summary>
        public bool IsBroad => AugmentClassification.IsUniversalOrBroad(Definition);

        public override string ToString() => Id + " r" + Rank;
    }

    /// <summary>Draft-guarantee categories of augment definitions (spec L227, L233, L596).</summary>
    public static class AugmentClassification
    {
        /// <summary>A shared definition whose eligibility is exactly {"always": true} (spec L596 "unconditional pool").</summary>
        public static bool IsUniversal(AugmentDef def) => def != null && def.IsShared && def.IsUnconditional && def.EligibleWizards.Count == 0;

        /// <summary>
        /// A shared definition whose condition reads only the current shared loadout (equipment and equipped-spell profile
        /// leaves) and no identity (wizard, kit spell), lineage (owned augments), history (feats), or schedule (round)
        /// leaf. Any wizard can meet it through its own spells or shared gear (spec L227, L556).
        /// </summary>
        public static bool IsBroadlyConditional(AugmentDef def)
        {
            if (def == null || !def.IsShared || def.EligibleWizards.Count > 0) return false;
            foreach (var node in def.Eligibility.DescendantsAndSelf())
            {
                switch (node.Kind)
                {
                    case PredicateKind.Wizard:
                    case PredicateKind.Spell:
                    case PredicateKind.Augment:
                    case PredicateKind.AugmentTagCount:
                    case PredicateKind.AugmentRankCount:
                    case PredicateKind.AugmentFamilyCount:
                    case PredicateKind.Feat:
                    case PredicateKind.RoundAtLeast:
                        return false;
                }
            }
            return true;
        }

        public static bool IsUniversalOrBroad(AugmentDef def) => IsUniversal(def) || IsBroadlyConditional(def);
    }

    /// <summary>
    /// The currently legal draft alternatives for one player (spec L227-L233): new definitions of exactly the draft
    /// rarity that are available (implemented), eligible, owned by this wizard if transformations, and free of
    /// exclusivity-group conflicts with owned augments; plus the rank 2 of each owned, active rank 1 definition whose
    /// original rarity is at most the draft rarity. Owned rank 2 definitions never appear. Catalogue order.
    /// </summary>
    public static class AugmentPool
    {
        public static IReadOnlyList<AugmentOffer> Eligible(Loadout loadout, RoundInfo round, Rarity rarity, IFeatCounts feats,
            IContentAvailability availability, IStructuralChangeSource changes = null)
        {
            if (loadout == null) throw new ArgumentNullException(nameof(loadout));
            if (availability == null) throw new ArgumentNullException(nameof(availability));
            var judge = new CandidateJudge(loadout, round, feats, changes);
            var offers = new List<AugmentOffer>();
            foreach (var def in loadout.Catalogue.Augments)
            {
                var offer = Judge(judge, def, rarity, availability);
                if (offer != null) offers.Add(offer);
            }
            return offers.AsReadOnly();
        }

        /// <summary>The offer this definition would make in a draft of the rarity, or null when it is not a legal alternative.</summary>
        public static AugmentOffer Judge(CandidateJudge judge, AugmentDef def, Rarity rarity, IContentAvailability availability)
        {
            if (def == null || !def.HasValidRarity || !availability.IsAvailable(def.Id)) return null;
            var loadout = judge.Loadout;
            var owned = loadout.FindAugment(def.Id);
            if (owned != null)
            {
                // Rank 2 at any stage whose rarity is at least the original; an owned rank 2 never appears (spec L231, L600).
                bool upgradeable = owned.Rank < GlobalRules.Draft.MaxRank && owned.IsActive
                    && Schedules.AllowsRankTwoOffer(owned.OriginalRarity, rarity);
                return upgradeable ? new AugmentOffer(def, owned.Rank + 1) : null;
            }
            if (def.RarityEnum != rarity) return null;                                 // new definitions match the draft rarity (L231)
            if (loadout.CheckNewAugment(def) != TransactionCode.Success) return null;  // owner wizard, exclusivity groups (L233, L541)
            return judge.IsEligible(def) ? new AugmentOffer(def, 1) : null;
        }

        /// <summary>Number of currently legal alternatives (the spec L233 floor counts these).</summary>
        public static int CountLegalAlternatives(Loadout loadout, RoundInfo round, Rarity rarity, IFeatCounts feats,
            IContentAvailability availability, IStructuralChangeSource changes = null) =>
            Eligible(loadout, round, rarity, feats, availability, changes).Count;
    }
}
