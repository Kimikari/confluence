using System;
using System.Collections.Generic;

namespace Confluence.Core.Content
{
    /// <summary>
    /// A transformation's change to the structural tags of its target spell, for example projectile to trap/field
    /// (spec L243, L543; DECISIONS D-012). Removal applies before addition.
    /// </summary>
    public sealed class StructuralDelta
    {
        internal StructuralDelta(string augmentId, string targetSpellId, IReadOnlyList<string> addTags, IReadOnlyList<string> removeTags)
        {
            AugmentId = augmentId;
            TargetSpellId = targetSpellId;
            AddTags = addTags;
            RemoveTags = removeTags;
        }

        public string AugmentId { get; }
        public string TargetSpellId { get; }
        public IReadOnlyList<string> AddTags { get; }
        public IReadOnlyList<string> RemoveTags { get; }

        /// <summary>Returns the base tags with RemoveTags dropped and AddTags appended (order preserved, no duplicates).</summary>
        public IReadOnlyList<string> Apply(IEnumerable<string> baseTags)
        {
            if (baseTags == null) throw new ArgumentNullException(nameof(baseTags));
            var removed = new HashSet<string>(RemoveTags, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>();
            foreach (var tag in baseTags)
                if (!removed.Contains(tag) && seen.Add(tag)) result.Add(tag);
            foreach (var tag in AddTags)
                if (seen.Add(tag)) result.Add(tag);
            return result.AsReadOnly();
        }

        internal bool SameAs(StructuralDelta other)
        {
            return AugmentId == other.AugmentId && TargetSpellId == other.TargetSpellId
                && SameSet(AddTags, other.AddTags) && SameSet(RemoveTags, other.RemoveTags);
        }

        private static bool SameSet(IReadOnlyList<string> a, IReadOnlyList<string> b)
        {
            var set = new HashSet<string>(a, StringComparer.Ordinal);
            return set.SetEquals(b);
        }
    }

    /// <summary>
    /// Process-wide registry of structural deltas, declared once by transformation behaviors and read by both Economy
    /// eligibility (structural spell profile) and Sim. A transformation without a declaration keeps its base tags.
    /// Registering the same delta twice is idempotent; a conflicting re-registration throws.
    /// </summary>
    public static class StructuralDeltas
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, StructuralDelta> ByAugment = new Dictionary<string, StructuralDelta>(StringComparer.Ordinal);

        public static StructuralDelta Register(string augmentId, string targetSpellId, string[] addTags, string[] removeTags)
        {
            if (string.IsNullOrEmpty(augmentId)) throw new ArgumentException("An augment ID is required.", nameof(augmentId));
            if (string.IsNullOrEmpty(targetSpellId)) throw new ArgumentException("A target spell ID is required.", nameof(targetSpellId));
            var delta = new StructuralDelta(augmentId, targetSpellId, CopyTags(addTags, nameof(addTags)), CopyTags(removeTags, nameof(removeTags)));
            lock (Gate)
            {
                if (ByAugment.TryGetValue(augmentId, out var existing))
                {
                    if (existing.SameAs(delta)) return existing;
                    throw new InvalidOperationException("A different structural delta is already registered for " + augmentId + ".");
                }
                ByAugment.Add(augmentId, delta);
                return delta;
            }
        }

        public static bool TryGet(string augmentId, out StructuralDelta delta)
        {
            lock (Gate)
            {
                if (augmentId != null && ByAugment.TryGetValue(augmentId, out delta)) return true;
                delta = null;
                return false;
            }
        }

        /// <summary>Snapshot of every registered delta, ordered by augment ID.</summary>
        public static IReadOnlyList<StructuralDelta> All
        {
            get
            {
                lock (Gate)
                {
                    var list = new List<StructuralDelta>(ByAugment.Values);
                    list.Sort((a, b) => string.CompareOrdinal(a.AugmentId, b.AugmentId));
                    return list.AsReadOnly();
                }
            }
        }

        /// <summary>Removes every registration. Intended for tests.</summary>
        public static void Clear()
        {
            lock (Gate) ByAugment.Clear();
        }

        private static IReadOnlyList<string> CopyTags(string[] tags, string name)
        {
            if (tags == null) return Array.AsReadOnly(new string[0]);
            var copy = new string[tags.Length];
            for (int i = 0; i < tags.Length; i++)
            {
                if (string.IsNullOrEmpty(tags[i])) throw new ArgumentException("Tags cannot be null or empty.", name);
                copy[i] = tags[i];
            }
            return Array.AsReadOnly(copy);
        }
    }
}
