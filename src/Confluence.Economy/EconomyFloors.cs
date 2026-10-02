using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Economy.Augments;
using Confluence.Economy.Eligibility;
using Confluence.Economy.Loadouts;

namespace Confluence.Economy
{
    /// <summary>Legal alternatives of one rarity for one wizard and loadout.</summary>
    public sealed class RarityFloor
    {
        internal RarityFloor(Rarity rarity, int legalAlternatives, int required)
        {
            Rarity = rarity;
            LegalAlternatives = legalAlternatives;
            Required = required;
        }

        public Rarity Rarity { get; }
        public int LegalAlternatives { get; }
        public int Required { get; }
        public bool Meets => LegalAlternatives >= Required;

        public override string ToString() => Rarity + " " + LegalAlternatives + "/" + Required;
    }

    /// <summary>Draft floor of one wizard and loadout across the three rarities (spec L233).</summary>
    public sealed class DraftFloorReport
    {
        internal DraftFloorReport(string wizardId, IReadOnlyList<RarityFloor> rarities)
        {
            WizardId = wizardId;
            Rarities = rarities;
        }

        public string WizardId { get; }
        public IReadOnlyList<RarityFloor> Rarities { get; }

        public bool MeetsAll
        {
            get
            {
                foreach (var r in Rarities)
                    if (!r.Meets) return false;
                return true;
            }
        }

        public RarityFloor For(Rarity rarity)
        {
            foreach (var r in Rarities)
                if (r.Rarity == rarity) return r;
            return null;
        }

        public override string ToString() => WizardId + ": " + string.Join(", ", Rarities);
    }

    /// <summary>Equipment available to the shop per tier and the first-shop floor (DECISIONS D-010).</summary>
    public sealed class EquipmentFloorReport
    {
        internal EquipmentFloorReport(IReadOnlyList<int> perTier, int required)
        {
            AvailablePerTier = perTier;
            Required = required;
        }

        /// <summary>Available legal definitions of tiers 1-4 (index 0 is tier 1).</summary>
        public IReadOnlyList<int> AvailablePerTier { get; }

        /// <summary>Three offers plus a distinct reroll for each, all from tier 1 (the only tier at the first shop).</summary>
        public int Required { get; }

        public bool Meets => AvailablePerTier[0] >= Required;

        public override string ToString() => "Equipment tier 1 " + AvailablePerTier[0] + "/" + Required;
    }

    /// <summary>Everything the server checks before starting a match.</summary>
    public sealed class MatchFloorReport
    {
        internal MatchFloorReport(IReadOnlyList<DraftFloorReport> wizards, EquipmentFloorReport equipment)
        {
            Wizards = wizards;
            Equipment = equipment;
        }

        public IReadOnlyList<DraftFloorReport> Wizards { get; }
        public EquipmentFloorReport Equipment { get; }

        public bool MeetsAll
        {
            get
            {
                if (!Equipment.Meets) return false;
                foreach (var w in Wizards)
                    if (!w.MeetsAll) return false;
                return true;
            }
        }

        /// <summary>Every gap in plain language; empty when the match may start.</summary>
        public IReadOnlyList<string> Gaps
        {
            get
            {
                var gaps = new List<string>();
                if (!Equipment.Meets) gaps.Add(Equipment.ToString());
                foreach (var w in Wizards)
                    foreach (var r in w.Rarities)
                        if (!r.Meets) gaps.Add(w.WizardId + " " + r);
                return gaps.AsReadOnly();
            }
        }
    }

    /// <summary>
    /// Design floors asserted at match start (spec L233, DECISIONS D-010): at least ten currently legal alternatives in each
    /// rarity for every wizard and loadout, and enough implemented equipment for three offers plus their rerolls. A failing
    /// floor is reported, never silently shrunk.
    /// </summary>
    public static class EconomyFloors
    {
        /// <summary>The draft floor for a wizard at its match-start loadout: default spells, issued gear, no augments or feats.</summary>
        public static DraftFloorReport CheckDraftFloor(Catalogue catalogue, IContentAvailability availability, string wizardId,
            MatchMode mode, IStructuralChangeSource changes = null)
        {
            return CheckDraftFloor(new Loadout(catalogue, wizardId), FeatLedger.None, mode, availability, changes);
        }

        /// <summary>
        /// The draft floor for a specific loadout: per rarity, the legal alternatives at that rarity's first scheduled draft.
        /// </summary>
        public static DraftFloorReport CheckDraftFloor(Loadout loadout, IFeatCounts feats, MatchMode mode,
            IContentAvailability availability, IStructuralChangeSource changes = null)
        {
            if (loadout == null) throw new ArgumentNullException(nameof(loadout));
            var copy = loadout.Clone();
            DormancyResolver.Resolve(copy, changes);
            var rarities = new List<RarityFloor>();
            foreach (Rarity rarity in new[] { Rarity.Silver, Rarity.Gold, Rarity.Prismatic })
            {
                var round = RoundInfo.FirstDraftOf(mode, rarity);
                int count = AugmentPool.CountLegalAlternatives(copy, round, rarity, feats, availability, changes);
                rarities.Add(new RarityFloor(rarity, count, GlobalRules.Draft.MinLegalAlternativesPerRarity));
            }
            return new DraftFloorReport(loadout.WizardId, rarities.AsReadOnly());
        }

        public static EquipmentFloorReport CheckEquipmentFloor(Catalogue catalogue, IContentAvailability availability)
        {
            if (catalogue == null) throw new ArgumentNullException(nameof(catalogue));
            if (availability == null) throw new ArgumentNullException(nameof(availability));
            var perTier = new int[GlobalRules.Economy.TierCount];
            foreach (var def in catalogue.EquipmentItems)
                if (SlotRules.IsLegalDefinition(def) && availability.IsAvailable(def.Id)) perTier[def.Tier - 1]++;
            int required = GlobalRules.Economy.ShopOffers * (1 + GlobalRules.Economy.RerollsPerOffer);
            return new EquipmentFloorReport(Array.AsReadOnly(perTier), required);
        }

        /// <summary>The match-start check: every listed wizard's draft floor and the equipment floor.</summary>
        public static MatchFloorReport CheckMatchStart(Catalogue catalogue, IContentAvailability availability, MatchMode mode,
            IEnumerable<string> wizardIds, IStructuralChangeSource changes = null)
        {
            if (wizardIds == null) throw new ArgumentNullException(nameof(wizardIds));
            var wizards = new List<DraftFloorReport>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in wizardIds)
                if (seen.Add(id)) wizards.Add(CheckDraftFloor(catalogue, availability, id, mode, changes));
            return new MatchFloorReport(wizards.AsReadOnly(), CheckEquipmentFloor(catalogue, availability));
        }
    }
}
