using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Economy.Eligibility;
using Confluence.Economy.Loadouts;

namespace Confluence.Economy.Augments
{
    /// <summary>Resolution outcome of one owned augment.</summary>
    public sealed class AugmentStatus
    {
        internal AugmentStatus(OwnedAugment augment, bool wasActive, bool isActive, IReadOnlyList<MissingRequirement> missing,
            IReadOnlyList<string> dormantParents, bool inCycle)
        {
            Augment = augment;
            WasActive = wasActive;
            IsActive = isActive;
            Missing = missing;
            DormantParents = dormantParents;
            InCycle = inCycle;
        }

        public OwnedAugment Augment { get; }
        public string Id => Augment.Id;
        public bool WasActive { get; }
        public bool IsActive { get; }
        public bool IsDormant => !IsActive;
        public bool BecameDormant => WasActive && !IsActive;
        public bool Reactivated => !WasActive && IsActive;

        /// <summary>Missing requirements in plain language (spec L641 "The UI always displays the missing requirement").</summary>
        public IReadOnlyList<MissingRequirement> Missing { get; }

        /// <summary>Owned parents referenced by this augment that are dormant.</summary>
        public IReadOnlyList<string> DormantParents { get; }

        /// <summary>Owned augments formed a dependency cycle; that is invalid content (spec L643) and leaves them dormant.</summary>
        public bool InCycle { get; }

        public override string ToString() => Id + (IsActive ? " active" : " dormant");
    }

    /// <summary>The finalized resolution of a loadout's augments.</summary>
    public sealed class DormancyReport
    {
        internal DormancyReport(IReadOnlyList<AugmentStatus> statuses, StructuralProfile profile)
        {
            Statuses = statuses;
            EligibilityProfile = profile;
            var dormant = new List<string>();
            var reactivated = new List<string>();
            foreach (var s in statuses)
            {
                if (s.BecameDormant) dormant.Add(s.Id);
                if (s.Reactivated) reactivated.Add(s.Id);
            }
            NewlyDormant = dormant.AsReadOnly();
            Reactivated = reactivated.AsReadOnly();
        }

        /// <summary>One status per owned augment, in slot order.</summary>
        public IReadOnlyList<AugmentStatus> Statuses { get; }

        /// <summary>Structural spell profile used for shared predicates (spec L243).</summary>
        public StructuralProfile EligibilityProfile { get; }

        public IReadOnlyList<string> NewlyDormant { get; }
        public IReadOnlyList<string> Reactivated { get; }

        public AugmentStatus For(string augmentId)
        {
            foreach (var s in Statuses)
                if (s.Id == augmentId) return s;
            return null;
        }

        public bool IsActive(string augmentId) => For(augmentId)?.IsActive ?? false;
    }

    /// <summary>
    /// Re-evaluates owned augments after every finalized equipment or spell change and at duel start (spec L249, L641).
    /// Order: wizard transformations against the base-kit profile, then the structural profile with the enabled
    /// transformations, then shared augments. Within each group the walk is topological over augment-to-augment
    /// references, so a parent resolves before its dependents and a dormant parent makes them dormant (spec L247).
    /// Acquisition-only leaves use the values recorded at acquisition. A dormant augment keeps its slot and rank but
    /// supplies no effect, tag, or parent qualification; restoring its requirements reactivates it at the next resolution.
    /// </summary>
    public static class DormancyResolver
    {
        /// <summary>Resolves and stores each augment's status on the loadout.</summary>
        public static DormancyReport Resolve(Loadout loadout, IStructuralChangeSource changes = null)
        {
            if (loadout == null) throw new ArgumentNullException(nameof(loadout));
            changes = changes ?? StructuralChangeSources.Default;
            var owned = loadout.Augments;
            var wasActive = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var a in owned) wasActive[a.Id] = a.IsActive;

            var transformations = new List<OwnedAugment>();
            var shared = new List<OwnedAugment>();
            foreach (var a in owned)
                (a.IsTransformation ? transformations : shared).Add(a);

            var active = new List<OwnedAugment>();
            var results = new Dictionary<string, Result>(StringComparer.Ordinal);

            // 1. Wizard transformation legality on the base-kit profile (spec L641).
            var baseProfile = LoadoutEligibility.TransformationProfile(loadout, changes);
            ResolveGroup(loadout, transformations, baseProfile, active, results);

            // 2. Structural profile with enabled transformations; 3. shared predicates against it.
            var enabled = new List<AugmentDef>();
            foreach (var a in active) enabled.Add(a.Definition);
            var profile = LoadoutEligibility.SharedProfile(loadout, enabled, changes);
            ResolveGroup(loadout, shared, profile, active, results);

            var statuses = new List<AugmentStatus>(owned.Count);
            foreach (var a in owned)
            {
                var r = results[a.Id];
                var dormantParents = new List<string>();
                foreach (var id in ReferencedOwned(a, owned))
                    if (!results[id].Active) dormantParents.Add(id);
                a.SetStatus(!r.Active, r.Missing, dormantParents.AsReadOnly());
                statuses.Add(new AugmentStatus(a, wasActive[a.Id], r.Active, a.MissingRequirements, a.DormantParents, r.InCycle));
            }
            loadout.EligibilityProfile = profile;
            return new DormancyReport(statuses.AsReadOnly(), profile);
        }

        /// <summary>The resolution a hypothetical change would produce, computed on a copy (replacement confirmation, spec L645).</summary>
        public static DormancyReport Preview(Loadout loadout, IStructuralChangeSource changes = null) => Resolve(loadout.Clone(), changes);

        private static void ResolveGroup(Loadout loadout, List<OwnedAugment> group, StructuralProfile profile,
            List<OwnedAugment> active, Dictionary<string, Result> results)
        {
            var order = TopologicalOrder(group, out var cycle);
            foreach (var a in order)
            {
                var ctx = new EligibilityContext(loadout, profile, active.AsReadOnly(), a.Acquisition.Round, null, a.Id, a.Acquisition);
                var result = EligibilityExplainer.Explain(a.Definition.Eligibility, ctx);
                results[a.Id] = new Result(result.IsMet, result.Missing, false);
                if (result.IsMet) active.Add(a);
            }
            foreach (var a in cycle)
            {
                var missing = new[] { new MissingRequirement(a.Definition.Eligibility, "Dependency cycle among owned augments (invalid content)") };
                results[a.Id] = new Result(false, Array.AsReadOnly(missing), true);
            }
        }

        // Kahn's algorithm; ready nodes are taken in slot order so the result is deterministic.
        private static List<OwnedAugment> TopologicalOrder(List<OwnedAugment> group, out List<OwnedAugment> cycle)
        {
            int n = group.Count;
            var parents = new List<int>[n];
            for (int i = 0; i < n; i++)
            {
                parents[i] = new List<int>();
                bool dependsOnAll = HasUnrestrictedAugmentCount(group[i].Definition.Eligibility);
                var refs = new HashSet<string>(group[i].Definition.Eligibility.CollectReferencedIds(), StringComparer.Ordinal);
                for (int j = 0; j < n; j++)
                {
                    if (j == i) continue;
                    if (dependsOnAll || refs.Contains(group[j].Id)) parents[i].Add(j);
                }
            }

            var done = new bool[n];
            var order = new List<OwnedAugment>(n);
            bool progress = true;
            while (progress)
            {
                progress = false;
                for (int i = 0; i < n; i++)
                {
                    if (done[i] || !AllDone(parents[i], done)) continue;
                    done[i] = true;
                    order.Add(group[i]);
                    progress = true;
                    break; // restart from the lowest slot so ties always resolve in slot order
                }
            }

            cycle = new List<OwnedAugment>();
            for (int i = 0; i < n; i++)
                if (!done[i]) cycle.Add(group[i]);
            return order;
        }

        private static bool AllDone(List<int> indices, bool[] done)
        {
            foreach (int i in indices)
                if (!done[i]) return false;
            return true;
        }

        private static bool HasUnrestrictedAugmentCount(Predicate root)
        {
            foreach (var node in root.DescendantsAndSelf())
            {
                if (node is AugmentTagCountLeaf t && !t.IsRestricted) return true;
                if (node is AugmentRankCountLeaf r && !r.IsRestricted) return true;
                if (node is AugmentFamilyCountLeaf f && !f.IsRestricted) return true;
            }
            return false;
        }

        private static IEnumerable<string> ReferencedOwned(OwnedAugment child, IReadOnlyList<OwnedAugment> owned)
        {
            var refs = new HashSet<string>(child.Definition.Eligibility.CollectReferencedIds(), StringComparer.Ordinal);
            foreach (var a in owned)
                if (a.Id != child.Id && refs.Contains(a.Id)) yield return a.Id;
        }

        private sealed class Result
        {
            public Result(bool active, IReadOnlyList<MissingRequirement> missing, bool inCycle)
            {
                Active = active;
                Missing = missing;
                InCycle = inCycle;
            }

            public bool Active { get; }
            public IReadOnlyList<MissingRequirement> Missing { get; }
            public bool InCycle { get; }
        }
    }
}
