using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Economy.Loadouts;

namespace Confluence.Economy.Eligibility
{
    /// <summary>
    /// The eligibility profile of one equipped spell: its base kit tags after enabled wizard transformations and chosen
    /// equipment structural changes. School affinity remains; general shared augment effects never appear (spec L243, L543).
    /// Cooldown and windup thresholds read the base values (spec L621-L622 "base cooldown", "intrinsic windup").
    /// </summary>
    public sealed class SpellProfile
    {
        internal SpellProfile(SpellDef spell, IReadOnlyList<string> tags, IReadOnlyList<string> appliedChangeIds)
        {
            Spell = spell;
            Tags = tags;
            AppliedChangeIds = appliedChangeIds;
        }

        public SpellDef Spell { get; }
        public string SpellId => Spell.Id;
        public IReadOnlyList<string> Tags { get; }

        /// <summary>Transformations and equipment whose structural change shaped these tags, in application order.</summary>
        public IReadOnlyList<string> AppliedChangeIds { get; }

        public double BaseCooldown => Spell.Cooldown;
        public double BaseCastTime => Spell.CastTime;

        public bool HasTag(string tag)
        {
            for (int i = 0; i < Tags.Count; i++)
                if (string.Equals(Tags[i], tag, StringComparison.Ordinal)) return true;
            return false;
        }

        public bool HasAllTags(IReadOnlyList<string> tags)
        {
            for (int i = 0; i < tags.Count; i++)
                if (!HasTag(tags[i])) return false;
            return true;
        }

        public override string ToString() => SpellId + " [" + string.Join(",", Tags) + "]";
    }

    /// <summary>
    /// Structural profiles of the six equipped spells (spec L243 "a structural profile made from the selected base kit,
    /// active wizard-specific transformations, and chosen shared equipment structural changes"). Application order follows
    /// spec L286: base spell, then wizard transformations in catalogue order, then equipment.
    /// </summary>
    public sealed class StructuralProfile
    {
        private StructuralProfile(IReadOnlyList<SpellProfile> spells)
        {
            Spells = spells;
        }

        /// <summary>One profile per equipped spell, in equipped-slot order.</summary>
        public IReadOnlyList<SpellProfile> Spells { get; }

        public SpellProfile For(string spellId)
        {
            foreach (var p in Spells)
                if (p.SpellId == spellId) return p;
            return null;
        }

        /// <summary>
        /// Builds the profile for the loadout's equipped spells. Only the given transformations contribute (callers pass the
        /// owned, non-dormant ones); shared augments are ignored even if passed, so a shared augment can never create its
        /// own prerequisite tag (spec L243, L637).
        /// </summary>
        public static StructuralProfile Build(Loadout loadout, IEnumerable<AugmentDef> enabledTransformations, IStructuralChangeSource source)
        {
            if (loadout == null) throw new ArgumentNullException(nameof(loadout));
            source = source ?? StructuralChangeSources.Default;
            var transformations = new List<AugmentDef>();
            if (enabledTransformations != null)
            {
                foreach (var def in enabledTransformations)
                    if (def != null && def.IsTransformation) transformations.Add(def);
            }
            transformations.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id)); // catalogue order (spec L541)

            var cat = loadout.Catalogue;
            var profiles = new List<SpellProfile>();
            foreach (var spellId in loadout.EquippedSpellIds)
            {
                var spell = cat.Spell(spellId);
                IReadOnlyList<string> tags = spell.Tags;
                var applied = new List<string>();
                foreach (var t in transformations)
                {
                    if (t.TargetSpellId != spellId) continue;
                    if (!source.TryGetTransformationChange(t, spellId, out var change)) continue;
                    tags = change.Apply(tags);
                    applied.Add(change.SourceId);
                }
                foreach (var item in loadout.EquippedItems)
                {
                    if (!EquipmentLiveness.HasLiveEffect(loadout, item)) continue;
                    if (!source.TryGetEquipmentChange(item.Definition, spell, tags, out var change)) continue;
                    tags = change.Apply(tags);
                    applied.Add(change.SourceId);
                }
                profiles.Add(new SpellProfile(spell, tags, applied.AsReadOnly()));
            }
            return new StructuralProfile(profiles.AsReadOnly());
        }
    }
}
