using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Economy.Augments;
using Confluence.Economy.Eligibility;
using Confluence.Economy.Loadouts;

namespace Confluence.Economy.Builds
{
    /// <summary>One free choice taken while replaying a worked build.</summary>
    public sealed class ReplayedPick
    {
        internal ReplayedPick(PlannedPick pick, IReadOnlyList<string> heldEquipmentIds, bool activeEnabled)
        {
            Pick = pick;
            HeldEquipmentIds = heldEquipmentIds;
            ActiveEnabled = activeEnabled;
        }

        /// <summary>The draft round, augment, and rank actually taken.</summary>
        public PlannedPick Pick { get; }

        /// <summary>The build's equipment held at that draft (tier unlocked and affordable by then).</summary>
        public IReadOnlyList<string> HeldEquipmentIds { get; }

        /// <summary>Whether an equipment active was enabled for that draft.</summary>
        public bool ActiveEnabled { get; }

        public override string ToString() => Pick.ToString();
    }

    /// <summary>The outcome of replaying a worked build as a Conclave draft path.</summary>
    public sealed class BuildReplay
    {
        internal BuildReplay(BuildDef build, Loadout final, IReadOnlyList<ReplayedPick> picks, IReadOnlyList<string> problems,
            bool? conditionHeld)
        {
            Build = build;
            FinalLoadout = final;
            Picks = picks;
            Problems = problems;
            BuildConditionHeld = conditionHeld;
        }

        public BuildDef Build { get; }

        /// <summary>The complete target loadout with every augment taken so far.</summary>
        public Loadout FinalLoadout { get; }

        public IReadOnlyList<ReplayedPick> Picks { get; }

        /// <summary>Plain-language catalogue inconsistencies; empty when the build is a possible draft path.</summary>
        public IReadOnlyList<string> Problems { get; }

        public bool IsPossible => Problems.Count == 0;

        /// <summary>
        /// For conditional examples (BLD-037..048): whether the build's stated eligibility held when its final augment was
        /// drafted; null for per-wizard plans or when the final augment was never reached.
        /// </summary>
        public bool? BuildConditionHeld { get; }
    }

    /// <summary>
    /// Replays a worked build as a Conclave path (spec L691-L697): each shop buys the build's items whose tier is unlocked
    /// within the scheduled income (L193, no winner bonus assumed), then the draft takes the next planned augment through
    /// the production pool and acquisition rules. Per-wizard plans (BLD-001..036) use their stated rounds; conditional
    /// examples (BLD-037..048) take their augments in listed order at the earliest draft where each is a legal choice,
    /// with the feats their stated condition names completed in earlier duels (L693 "a possible draft path"). The player's
    /// active-item choice (L575) is the default enabled active, or none when only that makes the plan legal.
    /// </summary>
    public static class BuildReplayer
    {
        private const MatchMode Mode = MatchMode.Conclave;

        public static BuildReplay Replay(Catalogue catalogue, BuildDef build, IContentAvailability availability,
            IStructuralChangeSource changes = null)
        {
            if (catalogue == null) throw new ArgumentNullException(nameof(catalogue));
            if (build == null) throw new ArgumentNullException(nameof(build));
            if (availability == null) throw new ArgumentNullException(nameof(availability));
            var problems = new List<string>();
            var targets = Targets(build, problems);
            var feats = StatedFeats(build);
            var picks = new List<ReplayedPick>();
            Loadout current = null;
            bool? conditionHeld = null;
            int lastRound = 0;

            for (int t = 0; t < targets.Count; t++)
            {
                var target = targets[t];
                if (!catalogue.TryGetAugment(target.AugmentId, out var def))
                {
                    problems.Add(build.Id + ": " + target.AugmentId + " is not an augment.");
                    continue;
                }
                bool isFinal = t == targets.Count - 1;
                ReplayedPick taken = null;
                string lastFailure = null;
                foreach (int round in CandidateRounds(target, lastRound))
                {
                    if (TryTake(catalogue, build, availability, changes, feats, current, def, target.Rank, round, isFinal,
                            out var next, out taken, out bool? held, out lastFailure))
                    {
                        current = next;
                        lastRound = round;
                        if (isFinal && build.Eligibility != null) conditionHeld = held;
                        break;
                    }
                }
                if (taken == null)
                {
                    problems.Add(build.Id + ": " + def.Id + " rank " + target.Rank + " is never a legal choice"
                        + (target.Round > 0 ? " at round " + target.Round : " after round " + lastRound) + " (" + lastFailure + ").");
                    continue;
                }
                picks.Add(taken);
            }

            var final = CompleteLoadout(catalogue, build, changes, current, picks, problems);
            if (build.Eligibility != null && conditionHeld == false)
                problems.Add(build.Id + ": the stated eligibility does not hold when the final augment is drafted.");
            return new BuildReplay(build, final, picks.AsReadOnly(), problems.AsReadOnly(), conditionHeld);
        }

        /// <summary>
        /// Items held after the shop of a round: the build's items in unlock order (listed order within a tier), each bought
        /// once its tier is unlocked if the running total fits the income received so far (spec L193, L197-L200, L697).
        /// </summary>
        public static IReadOnlyList<string> HeldEquipment(Catalogue catalogue, BuildDef build, MatchMode mode, int round)
        {
            var items = new List<EquipmentDef>();
            foreach (var id in build.EquipmentIds)
                if (catalogue.TryGetEquipment(id, out var def)) items.Add(def);
            var order = new List<int>();
            for (int i = 0; i < items.Count; i++) order.Add(i);
            order.Sort((a, b) =>
            {
                int byUnlock = Schedules.TierUnlockRound(mode, items[a].Tier).CompareTo(Schedules.TierUnlockRound(mode, items[b].Tier));
                return byUnlock != 0 ? byUnlock : a.CompareTo(b);
            });
            int income = IncomeThrough(round);
            int spent = 0;
            var held = new List<string>();
            foreach (int i in order)
            {
                var def = items[i];
                if (!Schedules.IsTierUnlocked(mode, round, def.Tier) || spent + def.Price > income) continue;
                spent += def.Price;
                held.Add(def.Id);
            }
            return held.AsReadOnly();
        }

        /// <summary>Crowns received by the shop of a round without winner bonuses (spec L193): 600, then 500 per later shop.</summary>
        public static int IncomeThrough(int round)
        {
            int total = 0;
            for (int r = 1; r <= round; r++) total += Schedules.ShopIncome(r == 1);
            return total;
        }

        private static bool TryTake(Catalogue catalogue, BuildDef build, IContentAvailability availability,
            IStructuralChangeSource changes, IReadOnlyList<FeatLeaf> feats, Loadout previous, AugmentDef def, int rank, int round,
            bool isFinal, out Loadout next, out ReplayedPick taken, out bool? conditionHeld, out string failure)
        {
            next = null;
            taken = null;
            conditionHeld = null;
            failure = null;
            var info = new RoundInfo(Mode, round);
            if (!info.TryGetDraftRarity(out var rarity))
            {
                failure = "round " + round + " has no draft";
                return false;
            }
            var held = HeldEquipment(catalogue, build, Mode, round);
            var ledger = LedgerBefore(round, feats);

            // The default active choice first, then none (spec L575: the player chooses the enabled active in the shop).
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var loadout = LoadoutAt(catalogue, build, held, previous, out var setup);
                if (setup.Count > 0)
                {
                    failure = setup[0];
                    return false;
                }
                if (attempt == 1)
                {
                    if (loadout.EnabledActive == null) break;
                    loadout.SetEnabledActive(null);
                }
                var report = DormancyResolver.Resolve(loadout, changes);
                string dormantId = FirstDormant(report);
                if (dormantId != null)
                {
                    failure = "earlier augment " + dormantId + " is dormant at round " + round;
                    continue;
                }
                var judge = new CandidateJudge(loadout, info, ledger, changes);
                var offer = AugmentPool.Judge(judge, def, rarity, availability);
                if (offer == null || offer.Rank != rank)
                {
                    failure = DescribeRejection(judge, def, rank, rarity, round, availability, loadout);
                    continue;
                }
                bool? held2 = null;
                if (isFinal && build.Eligibility != null)
                    held2 = PredicateEvaluator.Evaluate(build.Eligibility, judge.ContextFor(def));
                var result = rank == 1
                    ? AugmentAcquisition.AcquireNew(loadout, def, info, ledger, null, changes)
                    : AugmentAcquisition.Upgrade(loadout, def.Id, rarity, info, changes);
                if (!result.Succeeded)
                {
                    failure = "acquisition failed: " + result.Code;
                    continue;
                }
                next = loadout;
                conditionHeld = held2;
                taken = new ReplayedPick(new PlannedPick(round, def.Id, rank), held, loadout.EnabledActive != null);
                return true;
            }
            return false;
        }

        private static string DescribeRejection(CandidateJudge judge, AugmentDef def, int rank, Rarity rarity, int round,
            IContentAvailability availability, Loadout loadout)
        {
            if (!availability.IsAvailable(def.Id)) return def.Id + " is not available";
            var owned = loadout.FindAugment(def.Id);
            if (rank == 1 && owned != null) return def.Id + " is already owned";
            if (rank == 2 && owned == null) return def.Id + " rank 1 is not owned";
            if (rank == 1 && def.RarityEnum != rarity) return def.Id + " is " + def.Rarity + " but round " + round + " drafts " + rarity;
            if (rank == 2) return def.Id + " rank 2 cannot appear in a " + rarity + " draft";
            var code = loadout.CheckNewAugment(def);
            if (code != TransactionCode.Success) return code.ToString();
            return "round " + round + ": " + judge.Explain(def).Summary;
        }

        // A fresh loadout holding the items bought so far, carrying the augments already taken in their slots.
        private static Loadout LoadoutAt(Catalogue catalogue, BuildDef build, IReadOnlyList<string> held, Loadout previous,
            out IReadOnlyList<string> problems)
        {
            var heldSet = new HashSet<string>(held, StringComparer.Ordinal);
            var loadout = WorkedBuilds.CreateLoadout(catalogue, build, out problems, d => heldSet.Contains(d.Id));
            if (previous == null) return loadout;
            var slots = previous.AugmentSlots;
            for (int i = 0; i < slots.Count; i++)
                if (slots[i] != null) loadout.PlaceAugment(i, slots[i].Clone());
            return loadout;
        }

        // Completed duels before a round's draft (one per earlier round, spec L280), the first ones meeting the stated feats.
        private static IFeatCounts LedgerBefore(int round, IReadOnlyList<FeatLeaf> feats)
        {
            if (feats.Count == 0) return FeatLedger.None;
            var ledger = new FeatLedger();
            int duels = Schedules.CompletedDuelsBefore(round);
            for (int d = 0; d < duels; d++)
            {
                var met = new List<string>();
                foreach (var f in feats)
                    if (d < f.AtLeast) met.Add(f.FeatId);
                ledger.RecordCompletedDuel("worked-build-duel-" + (d + 1), false, met);
            }
            return ledger;
        }

        // Feats named by the build's own stated condition (L693 "a completed-duel feat"); none for per-wizard plans.
        private static IReadOnlyList<FeatLeaf> StatedFeats(BuildDef build)
        {
            var feats = new List<FeatLeaf>();
            if (build.Eligibility == null) return feats;
            foreach (var node in build.Eligibility.DescendantsAndSelf())
                if (node is FeatLeaf f) feats.Add(f);
            return feats;
        }

        // Explicit draft plan picks, or the listed augments in order (rank 2 targets follow their rank 1) with no fixed round.
        private static List<PlannedPick> Targets(BuildDef build, List<string> problems)
        {
            var targets = new List<PlannedPick>();
            if (build.DraftPlan.Count > 0)
            {
                targets.AddRange(WorkedBuilds.ParseDraftPlan(build, out var parseProblems));
                problems.AddRange(parseProblems);
                return targets;
            }
            foreach (var id in build.AugmentIds)
            {
                int rank = build.RankOf(id);
                for (int r = 1; r <= rank; r++) targets.Add(new PlannedPick(0, id, r));
            }
            return targets;
        }

        private static IEnumerable<int> CandidateRounds(PlannedPick target, int afterRound)
        {
            if (target.Round > 0)
            {
                if (target.Round > afterRound) yield return target.Round;
                yield break;
            }
            foreach (int round in Schedules.DraftRounds(Mode))
                if (round > afterRound) yield return round;
        }

        // The complete target loadout keeps every taken augment active once all its gear is bought (spec L247, L645).
        private static Loadout CompleteLoadout(Catalogue catalogue, BuildDef build, IStructuralChangeSource changes,
            Loadout current, List<ReplayedPick> picks, List<string> problems)
        {
            var final = LoadoutAt(catalogue, build, build.EquipmentIds, current, out var setup);
            problems.AddRange(setup);
            if (picks.Count > 0 && !picks[picks.Count - 1].ActiveEnabled && final.EnabledActive != null) final.SetEnabledActive(null);
            string dormantId = FirstDormant(DormancyResolver.Resolve(final, changes));
            if (dormantId != null) problems.Add(build.Id + ": " + dormantId + " is dormant in the complete target loadout.");
            foreach (var p in final.Validate()) problems.Add(build.Id + ": " + p);
            return final;
        }

        private static string FirstDormant(DormancyReport report)
        {
            foreach (var s in report.Statuses)
                if (s.IsDormant) return s.Id;
            return null;
        }
    }
}
