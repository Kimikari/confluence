using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Economy.Loadouts;

namespace Confluence.Economy.Eligibility
{
    /// <summary>
    /// Server-side evaluation of every node and leaf of the eligibility grammar (spec L604-L637) over an
    /// <see cref="EligibilityContext"/>. Numeric thresholds are inclusive (L635). Spell leaves read the structural
    /// profile, never shared augment effects (L637). Equipment leaves read live equipped items: a disabled active supplies
    /// no active-effect tag and two copies of one definition are one source (L585).
    /// </summary>
    public sealed class PredicateEvaluator : IPredicateVisitor<bool>
    {
        private readonly EligibilityContext _ctx;

        public PredicateEvaluator(EligibilityContext context)
        {
            _ctx = context ?? throw new ArgumentNullException(nameof(context));
        }

        public static bool Evaluate(Predicate predicate, EligibilityContext context)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            return predicate.Accept(new PredicateEvaluator(context));
        }

        // ---- Composites ------------------------------------------------------------------------

        public bool Visit(AllNode node)
        {
            foreach (var child in node.Children)
                if (!child.Accept(this)) return false;
            return true;
        }

        public bool Visit(AnyNode node)
        {
            foreach (var child in node.Children)
                if (child.Accept(this)) return true;
            return false;
        }

        public bool Visit(NotNode node) => !node.Child.Accept(this);

        // ---- Acquisition-only leaves (spec L247): recorded value when an acquisition record exists ----

        public bool Visit(AlwaysLeaf leaf) => true;

        public bool Visit(WizardLeaf leaf)
        {
            if (Recorded(leaf, out bool value)) return value;
            return _ctx.WizardId == leaf.WizardId;
        }

        public bool Visit(RoundAtLeastLeaf leaf)
        {
            if (Recorded(leaf, out bool value)) return value;
            if (_ctx.Round == null) throw new InvalidOperationException("round_at_least needs a round or an acquisition record.");
            return _ctx.Round.PredicateRound >= leaf.Round; // DECISIONS D-029
        }

        public bool Visit(FeatLeaf leaf)
        {
            if (Recorded(leaf, out bool value)) return value;
            if (_ctx.Feats == null) throw new InvalidOperationException("feat needs feat counts or an acquisition record.");
            return _ctx.Feats.CompletedDuelsMeeting(leaf.FeatId) >= leaf.AtLeast; // spec L280, L633
        }

        private bool Recorded(Predicate leaf, out bool value)
        {
            value = false;
            return _ctx.Acquisition != null && _ctx.Acquisition.TryGetRecorded(leaf, out value);
        }

        // ---- Equipment (ongoing) ---------------------------------------------------------------

        /// <summary>Exact item currently equipped in an occupied slot (spec L613, L585).</summary>
        public bool Visit(EquippedItemLeaf leaf)
        {
            foreach (var item in _ctx.Loadout.EquippedItems)
                if (EquipmentLiveness.IsPresent(item) && item.Id == leaf.EquipmentId) return true;
            return false;
        }

        /// <summary>Exact item held, including reserve (spec L614, L585).</summary>
        public bool Visit(OwnedItemLeaf leaf)
        {
            foreach (var item in _ctx.Loadout.AllItems)
                if (EquipmentLiveness.IsPresent(item) && item.Id == leaf.EquipmentId) return true;
            return false;
        }

        /// <summary>At least count distinct equipped definitions supply the live tag (spec L615, L585).</summary>
        public bool Visit(EquippedTagLeaf leaf) => CountTagSources(leaf.Tag) >= leaf.Count;

        public int CountTagSources(string tag)
        {
            var definitions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in _ctx.Loadout.EquippedItems)
                if (EquipmentLiveness.SuppliesTag(_ctx.Loadout, item, tag)) definitions.Add(item.Id);
            return definitions.Count;
        }

        public bool Visit(MainFocusCategoryLeaf leaf) => EquipmentLiveness.IsCategory(_ctx.Loadout.ItemIn(EquipmentSlot.MainFocus), leaf.Category);

        public bool Visit(SidearmCategoryLeaf leaf) => EquipmentLiveness.IsCategory(_ctx.Loadout.ItemIn(EquipmentSlot.Sidearm), leaf.Category);

        /// <summary>The slot holds only its plain issued equipment (or nothing, for slots without issued gear) (spec L625).</summary>
        public bool Visit(IssuedSlotLeaf leaf) => !EquipmentLiveness.IsPresent(_ctx.Loadout.ItemIn(leaf.Slot));

        /// <summary>The occupied slot has catalogue equipment of at least the tier; issued gear has no tier (spec L626).</summary>
        public bool Visit(SlotTierLeaf leaf)
        {
            var item = _ctx.Loadout.ItemIn(leaf.Slot);
            return EquipmentLiveness.IsPresent(item) && item.Definition.Tier >= leaf.AtLeast;
        }

        /// <summary>
        /// At least count items of the category occupy legal slots; with distinct_unique_groups they must have different
        /// nonempty Unique groups (spec L627, L635).
        /// </summary>
        public bool Visit(EquippedCategoryLeaf leaf)
        {
            var groups = new HashSet<string>(StringComparer.Ordinal);
            var definitions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in _ctx.Loadout.EquippedItems)
            {
                if (!EquipmentLiveness.IsCategory(item, leaf.Category)) continue;
                definitions.Add(item.Id);
                if (item.Definition.HasUniqueGroup) groups.Add(item.Definition.UniqueGroup);
            }
            return (leaf.DistinctUniqueGroups ? groups.Count : definitions.Count) >= leaf.Count;
        }

        /// <summary>Held purchased charges, in the Consumable slot and reserve, do not exceed the value (spec L628).</summary>
        public bool Visit(ConsumableChargesAtMostLeaf leaf) => _ctx.Loadout.HeldConsumableCharges <= leaf.AtMost;

        // ---- Owned augments (ongoing) ----------------------------------------------------------

        /// <summary>The exact parent is active at the stated minimum rank (spec L616).</summary>
        public bool Visit(AugmentLeaf leaf)
        {
            foreach (var a in Parents(false, null))
                if (a.Id == leaf.AugmentId && a.Rank >= leaf.Rank) return true;
            return false;
        }

        /// <summary>At least count active (enumerated) parents carry the augment tag (spec L629).</summary>
        public bool Visit(AugmentTagCountLeaf leaf)
        {
            int n = 0;
            foreach (var a in Parents(leaf.IsRestricted, leaf.Ids))
                if (a.Definition.HasTag(leaf.Tag)) n++;
            return n >= leaf.Count;
        }

        /// <summary>At least count active (enumerated) parents at the rank; optionally counting distinct families (spec L630).</summary>
        public bool Visit(AugmentRankCountLeaf leaf)
        {
            var families = new HashSet<string>(StringComparer.Ordinal);
            int n = 0;
            foreach (var a in Parents(leaf.IsRestricted, leaf.Ids))
            {
                if (a.Rank < leaf.RankAtLeast) continue;
                n++;
                if (a.Definition.Family.Length > 0) families.Add(a.Definition.Family);
            }
            return (leaf.DistinctFamilies ? families.Count : n) >= leaf.Count;
        }

        /// <summary>Enumerated active parents represent at least count different families (spec L631, L635).</summary>
        public bool Visit(AugmentFamilyCountLeaf leaf)
        {
            var families = new HashSet<string>(StringComparer.Ordinal);
            foreach (var a in Parents(leaf.IsRestricted, leaf.Ids))
                if (a.Definition.Family.Length > 0) families.Add(a.Definition.Family);
            return families.Count >= leaf.Count;
        }

        // Qualifying (active) owned augments other than the one being judged; only the enumerated IDs when present (spec L635).
        private IEnumerable<OwnedAugment> Parents(bool restricted, IReadOnlyList<string> ids)
        {
            foreach (var a in _ctx.QualifyingAugments)
            {
                if (a == null || a.Id == _ctx.SelfId) continue;
                if (restricted && !Contains(ids, a.Id)) continue;
                yield return a;
            }
        }

        // ---- Spells (ongoing) ------------------------------------------------------------------

        /// <summary>The spell belongs to the wizard's owned exclusive kit (spec L239, L617).</summary>
        public bool Visit(SpellLeaf leaf) => _ctx.Loadout.OwnsSpell(leaf.SpellId);

        /// <summary>At least count equipped profiles have the tag, each also carrying every 'also' tag (spec L618).</summary>
        public bool Visit(SpellTagLeaf leaf)
        {
            int n = 0;
            foreach (var p in _ctx.Profile.Spells)
                if (p.HasTag(leaf.Tag) && p.HasAllTags(leaf.Also)) n++;
            return n >= leaf.Count;
        }

        /// <summary>Exactly count equipped profiles have the tag (spec L619).</summary>
        public bool Visit(SpellTagExactLeaf leaf)
        {
            int n = 0;
            foreach (var p in _ctx.Profile.Spells)
                if (p.HasTag(leaf.Tag)) n++;
            return n == leaf.Count;
        }

        /// <summary>Distinct tags of the set across equipped profiles that carry every 'also' tag (spec L620).</summary>
        public bool Visit(SpellDistinctTagsLeaf leaf)
        {
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in _ctx.Profile.Spells)
            {
                if (!p.HasAllTags(leaf.Also)) continue;
                foreach (var tag in leaf.Set)
                    if (p.HasTag(tag)) found.Add(tag);
            }
            return found.Count >= leaf.Count;
        }

        /// <summary>At least count equipped spells meet the base cooldown threshold (spec L621).</summary>
        public bool Visit(SpellCooldownCountLeaf leaf)
        {
            int n = 0;
            foreach (var p in _ctx.Profile.Spells)
                if (leaf.Threshold.Includes(p.BaseCooldown)) n++;
            return n >= leaf.Count;
        }

        /// <summary>At least count equipped spells meet the intrinsic windup threshold (spec L622).</summary>
        public bool Visit(SpellCastTimeCountLeaf leaf)
        {
            int n = 0;
            foreach (var p in _ctx.Profile.Spells)
                if (leaf.Threshold.Includes(p.BaseCastTime)) n++;
            return n >= leaf.Count;
        }

        private static bool Contains(IReadOnlyList<string> ids, string id)
        {
            for (int i = 0; i < ids.Count; i++)
                if (ids[i] == id) return true;
            return false;
        }
    }
}
