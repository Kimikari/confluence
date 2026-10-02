using System;
using System.Collections.Generic;
using System.Text;
using Confluence.Core.Rules;

namespace Confluence.Core.Content
{
    /// <summary>
    /// Checks an augment's compulsory acquisition plan against the actual draft schedule of a mode (spec L600:
    /// "Acquisition plans are checked against the actual draft schedule, not only against numerical rarity
    /// ordering"; L643: a child cannot "demand more compulsory rarity-stage selections than the match offers").
    /// A plan is the augment itself plus every parent its eligibility requires (recursively), each parent's rank 2
    /// where required, and unnamed parents demanded by unrestricted count leaves. Each needs one free draft choice:
    /// a new definition at a draft of exactly its rarity, a rank 2 at a later draft, parents before children, and
    /// round_at_least / feat thresholds met at the drafting round. 'any' branches and enumerated-ID count leaves
    /// offer alternatives; the augment is acquirable when one alternative fits. Searches that exceed the budget
    /// are treated as acquirable, so the check never reports a false error.
    /// </summary>
    internal sealed class AcquisitionPlanner
    {
        private const int MaxDemandsPerNode = 32;
        private const int MaxPlansPerTarget = 512;
        private const int MaxDepth = 8;

        private readonly Catalogue _cat;
        private readonly int _maxRank;
        private string _self;
        private bool _gaveUp;
        private int _plansTried;

        public AcquisitionPlanner(Catalogue catalogue)
        {
            _cat = catalogue;
            _maxRank = catalogue.RuleFlags.AugmentMaxRank;
        }

        /// <summary>True if some plan fits the mode's draft schedule; otherwise reason describes the smallest plan.</summary>
        public bool CanAcquire(AugmentDef augment, MatchMode mode, out string reason)
        {
            reason = null;
            if (!augment.TryGetRarity(out var rarity)) return true; // reported by the rarity check
            _self = augment.Id;
            _gaveUp = false;
            _plansTried = 0;

            var drafts = Drafts(mode);
            Plan smallest = null;
            foreach (var plan in Plans(augment, rarity))
            {
                if (Fits(plan, drafts)) return true;
                if (smallest == null || plan.Jobs.Count < smallest.Jobs.Count) smallest = plan;
                if (++_plansTried >= MaxPlansPerTarget)
                {
                    _gaveUp = true;
                    break;
                }
            }

            if (_gaveUp || smallest == null) return true;
            reason = Describe(smallest, mode);
            return false;
        }

        // ---- Demands: what one alternative of a predicate requires before the augment is drafted -------------

        private sealed class Demand
        {
            public readonly SortedDictionary<string, int> Parents = new SortedDictionary<string, int>(StringComparer.Ordinal);
            public int AnyParentsRank1;
            public int AnyParentsRank2;
            public int MinRound;
            public int MinCompletedDuels;

            public static readonly Demand Empty = new Demand();

            public Demand Merge(Demand other)
            {
                // Unnamed parents of two count leaves may be the same augments, so the larger demand covers both.
                int rankTwo = Math.Max(AnyParentsRank2, other.AnyParentsRank2);
                int total = Math.Max(AnyParentsRank1 + AnyParentsRank2, other.AnyParentsRank1 + other.AnyParentsRank2);
                var merged = new Demand
                {
                    AnyParentsRank1 = total - rankTwo,
                    AnyParentsRank2 = rankTwo,
                    MinRound = Math.Max(MinRound, other.MinRound),
                    MinCompletedDuels = Math.Max(MinCompletedDuels, other.MinCompletedDuels),
                };
                foreach (var p in Parents) merged.Parents[p.Key] = p.Value;
                foreach (var p in other.Parents)
                    merged.Parents[p.Key] = merged.Parents.TryGetValue(p.Key, out int rank) ? Math.Max(rank, p.Value) : p.Value;
                return merged;
            }

            public string Key()
            {
                var sb = new StringBuilder();
                foreach (var p in Parents) sb.Append(p.Key).Append(':').Append(p.Value).Append(',');
                sb.Append('|').Append(AnyParentsRank1).Append('|').Append(AnyParentsRank2).Append('|').Append(MinRound).Append('|').Append(MinCompletedDuels);
                return sb.ToString();
            }
        }

        private List<Demand> Demands(Predicate node)
        {
            switch (node)
            {
                case AllNode all:
                {
                    var result = new List<Demand> { Demand.Empty };
                    foreach (var child in all.Children)
                    {
                        var next = new List<Demand>();
                        foreach (var left in result)
                            foreach (var right in Demands(child))
                                next.Add(left.Merge(right));
                        result = Distinct(next);
                    }
                    return result;
                }
                case AnyNode any:
                {
                    var result = new List<Demand>();
                    foreach (var child in any.Children) result.AddRange(Demands(child));
                    return Distinct(result);
                }
                case AugmentLeaf leaf:
                    if (leaf.AugmentId == _self || !_cat.AugmentsById.ContainsKey(leaf.AugmentId)) return One(Demand.Empty);
                    return One(Named(leaf.AugmentId, Clamp(leaf.Rank)));
                case AugmentTagCountLeaf t:
                    return Select(t.IsRestricted, t.Ids, t.Count, 1, false, a => a.HasTag(t.Tag));
                case AugmentRankCountLeaf r:
                    return Select(r.IsRestricted, r.Ids, r.Count, Clamp(r.RankAtLeast), r.DistinctFamilies, a => true);
                case AugmentFamilyCountLeaf f:
                    return Select(f.IsRestricted, f.Ids, f.Count, 1, true, a => true);
                case RoundAtLeastLeaf round:
                    return One(new Demand { MinRound = round.Round });
                case FeatLeaf feat:
                    return One(new Demand { MinCompletedDuels = feat.AtLeast });
                default:
                    return One(Demand.Empty); // 'not' branches and loadout leaves need no draft choice
            }
        }

        private int Clamp(int rank) => Math.Max(1, Math.Min(_maxRank, rank));

        private static Demand Named(string id, int rank)
        {
            var d = new Demand();
            d.Parents[id] = rank;
            return d;
        }

        private static List<Demand> One(Demand d) => new List<Demand> { d };

        // "At least count of these parents": for every split of the count across the three rarity stages, the
        // cheapest listed candidates (unconditional first) that realise it.
        private List<Demand> Select(bool restricted, IReadOnlyList<string> ids, int count, int rank, bool distinctFamilies, Func<AugmentDef, bool> filter)
        {
            if (count <= 0) return One(Demand.Empty);
            if (!restricted)
            {
                return One(rank >= 2 ? new Demand { AnyParentsRank2 = count } : new Demand { AnyParentsRank1 = count });
            }

            var candidates = new List<AugmentDef>();
            foreach (var id in ids)
                if (id != _self && _cat.TryGetAugment(id, out var a) && a.HasValidRarity && filter(a)) candidates.Add(a);
            candidates.Sort((x, y) =>
            {
                int cx = x.IsUnconditional ? 0 : 1, cy = y.IsUnconditional ? 0 : 1;
                return cx != cy ? cx.CompareTo(cy) : string.CompareOrdinal(x.Id, y.Id);
            });

            var result = new List<Demand>();
            for (int silver = count; silver >= 0; silver--)
            {
                for (int gold = count - silver; gold >= 0; gold--)
                {
                    int[] need = { silver, gold, count - silver - gold };
                    var chosen = new Demand();
                    var families = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var a in candidates)
                    {
                        int stage = (int)a.RarityEnum;
                        if (need[stage] == 0) continue;
                        if (distinctFamilies && !families.Add(a.Family)) continue;
                        chosen.Parents[a.Id] = rank;
                        need[stage]--;
                    }

                    if (need[0] == 0 && need[1] == 0 && need[2] == 0) result.Add(chosen);
                }
            }

            return Distinct(result); // empty when too few listed parents exist: no plan at all
        }

        private List<Demand> Distinct(List<Demand> demands)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<Demand>();
            foreach (var d in demands)
            {
                if (!seen.Add(d.Key())) continue;
                if (result.Count == MaxDemandsPerNode)
                {
                    _gaveUp = true;
                    break;
                }
                result.Add(d);
            }
            return result;
        }

        // ---- Plans: draft jobs with precedence ---------------------------------------------------------------

        private sealed class Job
        {
            public string Label;
            public Rarity? Rarity; // exact stage for a new definition; null for rank 2 or an unnamed parent's stage
            public int MinRound;
            public int MinCompletedDuels;
            public readonly List<string> Predecessors = new List<string>();
        }

        private sealed class Plan
        {
            public readonly Dictionary<string, Job> Jobs = new Dictionary<string, Job>(StringComparer.Ordinal);
            public readonly List<string> Order = new List<string>();
            public string TargetKey;

            public Plan Clone()
            {
                var copy = new Plan { TargetKey = TargetKey };
                foreach (var key in Order)
                {
                    var job = Jobs[key];
                    var j = new Job { Label = job.Label, Rarity = job.Rarity, MinRound = job.MinRound, MinCompletedDuels = job.MinCompletedDuels };
                    j.Predecessors.AddRange(job.Predecessors);
                    copy.Add(key, j);
                }
                return copy;
            }

            public void Add(string key, Job job)
            {
                Jobs.Add(key, job);
                Order.Add(key);
            }
        }

        private IEnumerable<Plan> Plans(AugmentDef target, Rarity rarity)
        {
            foreach (var demand in Demands(target.Eligibility))
            {
                var plan = new Plan { TargetKey = target.Id };
                var job = new Job { Label = target.Id + " itself (" + rarity + ")", Rarity = rarity, MinRound = demand.MinRound, MinCompletedDuels = demand.MinCompletedDuels };
                var pending = AddDemand(plan, target.Id, job, demand);
                foreach (var complete in Expand(plan, pending, new List<string> { target.Id }, 0))
                    yield return complete;
                if (_gaveUp) yield break;
            }
        }

        // Adds the job for an augment and returns the named parents it still needs expanded.
        private List<KeyValuePair<string, int>> AddDemand(Plan plan, string key, Job job, Demand demand)
        {
            var pending = new List<KeyValuePair<string, int>>();
            int namedRankTwo = 0;
            foreach (var parent in demand.Parents)
            {
                job.Predecessors.Add(parent.Value >= 2 ? parent.Key + "#2" : parent.Key);
                pending.Add(parent);
                if (parent.Value >= 2) namedRankTwo++;
            }

            // Named parents may also count toward unrestricted count leaves, so only the shortfall is unnamed.
            int unnamedRankTwo = Math.Max(0, demand.AnyParentsRank2 - namedRankTwo);
            int unnamed = Math.Max(unnamedRankTwo, demand.AnyParentsRank1 + demand.AnyParentsRank2 - demand.Parents.Count);
            for (int i = 0; i < unnamed; i++)
            {
                string anyKey = key + "*" + i;
                plan.Add(anyKey, new Job { Label = "another augment" });
                if (i < unnamedRankTwo)
                {
                    var rankTwo = new Job { Label = "that augment's rank 2" };
                    rankTwo.Predecessors.Add(anyKey);
                    plan.Add(anyKey + "#2", rankTwo);
                    job.Predecessors.Add(anyKey + "#2");
                }
                else
                {
                    job.Predecessors.Add(anyKey);
                }
            }

            plan.Add(key, job);
            return pending;
        }

        private IEnumerable<Plan> Expand(Plan plan, List<KeyValuePair<string, int>> pending, List<string> chain, int depth)
        {
            if (pending.Count == 0)
            {
                yield return plan;
                yield break;
            }

            if (depth > MaxDepth)
            {
                _gaveUp = true;
                yield break;
            }

            var parent = pending[0];
            var rest = pending.GetRange(1, pending.Count - 1);
            string id = parent.Key;

            if (chain.Contains(id))
            {
                _gaveUp = true; // a dependency cycle, reported by the dependency-graph check
                yield break;
            }

            if (plan.Jobs.ContainsKey(id))
            {
                // A parent shared by several requirements is drafted once.
                if (parent.Value >= 2 && !plan.Jobs.ContainsKey(id + "#2"))
                {
                    plan = plan.Clone();
                    AddRankTwo(plan, id);
                }
                foreach (var complete in Expand(plan, rest, chain, depth))
                    yield return complete;
                yield break;
            }

            if (!_cat.TryGetAugment(id, out var augment) || !augment.TryGetRarity(out var rarity))
            {
                yield break;
            }

            foreach (var demand in Demands(augment.Eligibility))
            {
                var branch = plan.Clone();
                var job = new Job { Label = id + " (" + rarity + ")", Rarity = rarity, MinRound = demand.MinRound, MinCompletedDuels = demand.MinCompletedDuels };
                var more = AddDemand(branch, id, job, demand);
                if (parent.Value >= 2) AddRankTwo(branch, id);
                var next = new List<KeyValuePair<string, int>>(rest);
                next.AddRange(more);
                var nextChain = new List<string>(chain) { id };
                foreach (var complete in Expand(branch, next, nextChain, depth + 1))
                    yield return complete;
                if (_gaveUp) yield break;
            }
        }

        private static void AddRankTwo(Plan plan, string id)
        {
            // A later draft is always at least the original rarity because stages never decrease (spec L231).
            var rankTwo = new Job { Label = id + " rank 2" };
            rankTwo.Predecessors.Add(id);
            plan.Add(id + "#2", rankTwo);
        }

        // ---- Scheduling --------------------------------------------------------------------------------------

        private struct Draft
        {
            public Rarity Rarity;
            public int PredicateRound;
            public int CompletedDuels;
        }

        private static Draft[] Drafts(MatchMode mode)
        {
            var rounds = Schedules.DraftRounds(mode);
            var drafts = new Draft[rounds.Count];
            for (int i = 0; i < rounds.Count; i++)
            {
                Schedules.DraftRarity(mode, rounds[i], out var rarity);
                drafts[i] = new Draft
                {
                    Rarity = rarity,
                    PredicateRound = Schedules.PredicateRound(mode, rounds[i]),
                    CompletedDuels = Schedules.CompletedDuelsBefore(rounds[i]),
                };
            }
            return drafts;
        }

        private static bool Fits(Plan plan, Draft[] drafts)
        {
            if (plan.Jobs.Count > drafts.Length) return false;
            return Place(plan, drafts, 0, new HashSet<string>(StringComparer.Ordinal));
        }

        // One free choice per draft, in schedule order; a draft may also be spent on something else.
        private static bool Place(Plan plan, Draft[] drafts, int position, HashSet<string> placed)
        {
            if (placed.Count == plan.Jobs.Count) return true;
            if (plan.Jobs.Count - placed.Count > drafts.Length - position) return false;

            Draft draft = drafts[position];
            foreach (var key in plan.Order)
            {
                if (placed.Contains(key)) continue;
                var job = plan.Jobs[key];
                if (job.Rarity.HasValue && job.Rarity.Value != draft.Rarity) continue;
                if (draft.PredicateRound < job.MinRound || draft.CompletedDuels < job.MinCompletedDuels) continue;
                if (!AllPlaced(job.Predecessors, placed)) continue;
                placed.Add(key);
                if (Place(plan, drafts, position + 1, placed)) return true;
                placed.Remove(key);
            }

            return Place(plan, drafts, position + 1, placed);
        }

        private static bool AllPlaced(List<string> keys, HashSet<string> placed)
        {
            foreach (var key in keys)
                if (!placed.Contains(key)) return false;
            return true;
        }

        private static string Describe(Plan plan, MatchMode mode)
        {
            var parts = new List<string>();
            foreach (var key in plan.Order)
                if (key != plan.TargetKey) parts.Add(plan.Jobs[key].Label);

            var target = plan.Jobs[plan.TargetKey];
            string conditions = "";
            if (target.MinRound > 0) conditions += ", round_at_least " + target.MinRound;
            if (target.MinCompletedDuels > 0) conditions += ", " + target.MinCompletedDuels + " completed duel(s) for its feat";
            string before = parts.Count == 0 ? "" : " after " + string.Join(", ", parts);
            int[] stages = new int[3];
            foreach (var d in Drafts(mode)) stages[(int)d.Rarity]++;
            return "cannot be drafted in " + mode + ": the smallest plan needs " + target.Label + conditions + before + ", but "
                + mode + " offers " + stages[0] + " Silver, " + stages[1] + " Gold, and " + stages[2] + " Prismatic choices in that order (spec L600, L643)";
        }
    }
}
