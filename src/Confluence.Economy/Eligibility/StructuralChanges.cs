using System;
using System.Collections.Generic;
using Confluence.Core.Content;

namespace Confluence.Economy.Eligibility
{
    /// <summary>
    /// A change to one spell's structural tags: removal first, then addition, order preserved without duplicates
    /// (same semantics as Core <see cref="StructuralDelta"/>; spec L243, L543).
    /// </summary>
    public sealed class SpellTagChange
    {
        private static readonly IReadOnlyList<string> NoTags = Array.AsReadOnly(new string[0]);

        public SpellTagChange(string sourceId, IEnumerable<string> addTags, IEnumerable<string> removeTags)
        {
            if (string.IsNullOrEmpty(sourceId)) throw new ArgumentException("A source ID is required.", nameof(sourceId));
            SourceId = sourceId;
            AddTags = Copy(addTags);
            RemoveTags = Copy(removeTags);
        }

        /// <summary>The transformation or equipment ID declaring the change.</summary>
        public string SourceId { get; }

        public IReadOnlyList<string> AddTags { get; }
        public IReadOnlyList<string> RemoveTags { get; }

        public IReadOnlyList<string> Apply(IReadOnlyList<string> tags)
        {
            if (tags == null) throw new ArgumentNullException(nameof(tags));
            var removed = new HashSet<string>(RemoveTags, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>(tags.Count + AddTags.Count);
            foreach (var tag in tags)
                if (!removed.Contains(tag) && seen.Add(tag)) result.Add(tag);
            foreach (var tag in AddTags)
                if (seen.Add(tag)) result.Add(tag);
            return result.AsReadOnly();
        }

        private static IReadOnlyList<string> Copy(IEnumerable<string> tags)
        {
            if (tags == null) return NoTags;
            var list = new List<string>();
            foreach (var t in tags)
            {
                if (string.IsNullOrEmpty(t)) throw new ArgumentException("Tags cannot be null or empty.");
                list.Add(t);
            }
            return list.AsReadOnly();
        }
    }

    /// <summary>
    /// Where structural tag changes come from. Wizard transformations declare theirs once in Core
    /// <see cref="StructuralDeltas"/> (DECISIONS D-012); chosen shared equipment structural effects are supplied here as
    /// well. General shared augments never contribute: the profile builder only asks about owned, active
    /// transformations (spec L243 two-profile rule).
    /// </summary>
    public interface IStructuralChangeSource
    {
        /// <summary>The change a wizard transformation makes to its target spell, if it declares one.</summary>
        bool TryGetTransformationChange(AugmentDef transformation, string targetSpellId, out SpellTagChange change);

        /// <summary>The change a live equipped item makes to an equipped spell (given its current tags), if any.</summary>
        bool TryGetEquipmentChange(EquipmentDef item, SpellDef spell, IReadOnlyList<string> currentTags, out SpellTagChange change);
    }

    public static class StructuralChangeSources
    {
        /// <summary>
        /// Production source: transformations read the Core registry. No supplied equipment record changes a spell's
        /// structural tags (split, return, and portal modes keep the projectile a projectile), so equipment supplies none.
        /// </summary>
        public static readonly IStructuralChangeSource Default = new CoreRegistrySource();

        private sealed class CoreRegistrySource : IStructuralChangeSource
        {
            public bool TryGetTransformationChange(AugmentDef transformation, string targetSpellId, out SpellTagChange change)
            {
                change = null;
                if (transformation == null || !StructuralDeltas.TryGet(transformation.Id, out var delta)) return false;
                if (delta.TargetSpellId != targetSpellId) return false;
                change = new SpellTagChange(delta.AugmentId, delta.AddTags, delta.RemoveTags);
                return true;
            }

            public bool TryGetEquipmentChange(EquipmentDef item, SpellDef spell, IReadOnlyList<string> currentTags, out SpellTagChange change)
            {
                change = null;
                return false;
            }
        }
    }

    /// <summary>
    /// An explicit table of structural changes, for the Laboratory, tests, and future equipment structural modes.
    /// Lookups are by ID; registration order never affects results.
    /// </summary>
    public sealed class StructuralChangeTable : IStructuralChangeSource
    {
        private readonly Dictionary<string, KeyValuePair<string, SpellTagChange>> _transformations =
            new Dictionary<string, KeyValuePair<string, SpellTagChange>>(StringComparer.Ordinal);

        private readonly Dictionary<string, EquipmentRule> _equipment = new Dictionary<string, EquipmentRule>(StringComparer.Ordinal);

        /// <summary>Declares that the augment changes its target spell's tags.</summary>
        public StructuralChangeTable AddTransformation(string augmentId, string targetSpellId, string[] addTags, string[] removeTags)
        {
            if (string.IsNullOrEmpty(targetSpellId)) throw new ArgumentException("A target spell is required.", nameof(targetSpellId));
            _transformations[augmentId] = new KeyValuePair<string, SpellTagChange>(targetSpellId, new SpellTagChange(augmentId, addTags, removeTags));
            return this;
        }

        /// <summary>Declares that a live equipped item changes every equipped spell currently carrying requiredTag (or every spell when null).</summary>
        public StructuralChangeTable AddEquipment(string equipmentId, string requiredTag, string[] addTags, string[] removeTags)
        {
            _equipment[equipmentId] = new EquipmentRule(requiredTag, new SpellTagChange(equipmentId, addTags, removeTags));
            return this;
        }

        public bool TryGetTransformationChange(AugmentDef transformation, string targetSpellId, out SpellTagChange change)
        {
            change = null;
            if (transformation == null || !_transformations.TryGetValue(transformation.Id, out var entry)) return false;
            if (entry.Key != targetSpellId) return false;
            change = entry.Value;
            return true;
        }

        public bool TryGetEquipmentChange(EquipmentDef item, SpellDef spell, IReadOnlyList<string> currentTags, out SpellTagChange change)
        {
            change = null;
            if (item == null || !_equipment.TryGetValue(item.Id, out var rule)) return false;
            if (rule.RequiredTag != null)
            {
                bool has = false;
                foreach (var t in currentTags)
                    if (t == rule.RequiredTag) has = true;
                if (!has) return false;
            }
            change = rule.Change;
            return true;
        }

        private sealed class EquipmentRule
        {
            public EquipmentRule(string requiredTag, SpellTagChange change)
            {
                RequiredTag = requiredTag;
                Change = change;
            }

            public string RequiredTag { get; }
            public SpellTagChange Change { get; }
        }
    }
}
