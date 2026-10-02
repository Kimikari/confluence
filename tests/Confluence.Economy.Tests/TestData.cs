using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Economy.Augments;
using Confluence.Economy.Eligibility;
using Confluence.Economy.Loadouts;

namespace Confluence.Economy.Tests
{
    /// <summary>The supplied catalogue, loaded once, plus small builders for loadouts used across tests.</summary>
    internal static class TestData
    {
        private static readonly Lazy<Catalogue> LazyCatalogue = new Lazy<Catalogue>(() => Catalogue.Load(Catalogue.FindDefaultPath()));

        public static Catalogue Cat => LazyCatalogue.Value;

        public static readonly RoundInfo Conclave1 = new RoundInfo(MatchMode.Conclave, 1);
        public static readonly RoundInfo Conclave3 = new RoundInfo(MatchMode.Conclave, 3);
        public static readonly RoundInfo Conclave5 = new RoundInfo(MatchMode.Conclave, 5);
        public static readonly RoundInfo Conclave9 = new RoundInfo(MatchMode.Conclave, 9);

        /// <summary>Changes source with nothing registered: profiles are exactly the base kit tags.</summary>
        public static readonly IStructuralChangeSource NoChanges = new StructuralChangeTable();

        public static PlayerEconomy Player(string wizardId, int crowns = 0, int slot = 0)
        {
            var p = new PlayerEconomy(Cat, wizardId, slot);
            if (crowns > 0) p.GrantCrowns(crowns);
            return p;
        }

        public static Loadout Loadout(string wizardId) => new Loadout(Cat, wizardId);

        /// <summary>Adds a copy of an item at its tier price (setup; bypasses the shop).</summary>
        public static OwnedEquipment Give(Loadout loadout, string equipmentId, Placement? at = null)
        {
            var def = Cat.Equipment(equipmentId);
            var code = loadout.AddItem(def, def.Price, at, out var item);
            if (code != TransactionCode.Success) throw new InvalidOperationException("Cannot give " + equipmentId + ": " + code);
            return item;
        }

        /// <summary>Takes an augment through the production acquisition path; fails the test if it is not eligible.</summary>
        public static OwnedAugment Grant(Loadout loadout, string augmentId, RoundInfo round = null, IFeatCounts feats = null,
            IStructuralChangeSource changes = null)
        {
            var result = AugmentAcquisition.AcquireNew(loadout, Cat.Augment(augmentId), round ?? Conclave1, feats, null, changes ?? NoChanges);
            if (!result.Succeeded) throw new InvalidOperationException("Cannot grant " + augmentId + ": " + result.Code);
            return result.Augment;
        }

        public static OwnedAugment GrantRankTwo(Loadout loadout, string augmentId, RoundInfo round = null, IStructuralChangeSource changes = null)
        {
            var owned = Grant(loadout, augmentId, round, null, changes);
            var result = AugmentAcquisition.Upgrade(loadout, augmentId, owned.OriginalRarity, round ?? Conclave3, changes ?? NoChanges);
            if (!result.Succeeded) throw new InvalidOperationException("Cannot upgrade " + augmentId + ": " + result.Code);
            return result.Augment;
        }

        /// <summary>Whether a not-yet-owned augment is eligible for this loadout right now.</summary>
        public static bool Eligible(Loadout loadout, string augmentId, RoundInfo round = null, IFeatCounts feats = null, IStructuralChangeSource changes = null)
        {
            DormancyResolver.Resolve(loadout, changes ?? NoChanges);
            return new CandidateJudge(loadout, round ?? Conclave1, feats, changes ?? NoChanges).IsEligible(Cat.Augment(augmentId));
        }

        public static EligibilityResult Explain(Loadout loadout, string augmentId, RoundInfo round = null, IFeatCounts feats = null)
        {
            DormancyResolver.Resolve(loadout, NoChanges);
            return new CandidateJudge(loadout, round ?? Conclave1, feats, NoChanges).Explain(Cat.Augment(augmentId));
        }

        /// <summary>Evaluates an arbitrary predicate (for leaves no catalogue augment uses).</summary>
        public static bool Evaluate(Predicate predicate, Loadout loadout, RoundInfo round = null, IFeatCounts feats = null)
        {
            DormancyResolver.Resolve(loadout, NoChanges);
            var profile = StructuralProfile.Build(loadout, null, NoChanges);
            var ctx = new EligibilityContext(loadout, profile, loadout.ActiveAugments, round ?? Conclave1, feats ?? FeatLedger.None);
            return PredicateEvaluator.Evaluate(predicate, ctx);
        }

        public static FeatLedger Feats(params string[] perDuel)
        {
            var ledger = new FeatLedger();
            for (int i = 0; i < perDuel.Length; i++)
                ledger.RecordCompletedDuel("duel-" + (i + 1), false, perDuel[i].Length == 0 ? new string[0] : perDuel[i].Split(','));
            return ledger;
        }

        public static IReadOnlyList<string> Ids(IEnumerable<OwnedAugment> augments)
        {
            var list = new List<string>();
            foreach (var a in augments) list.Add(a.Id);
            return list;
        }
    }
}
