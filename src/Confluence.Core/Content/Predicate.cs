using System;
using System.Collections.Generic;
using Confluence.Core.Json;

namespace Confluence.Core.Content
{
    /// <summary>Every node and leaf of the eligibility grammar (spec L239-L241, L606-L633).</summary>
    public enum PredicateKind
    {
        All,
        Any,
        Not,
        Always,
        Wizard,
        EquippedItem,
        OwnedItem,
        EquippedTag,
        Augment,
        Spell,
        SpellTag,
        SpellTagExact,
        SpellDistinctTags,
        SpellCooldownCount,
        SpellCastTimeCount,
        MainFocusCategory,
        SidearmCategory,
        IssuedSlot,
        SlotTier,
        EquippedCategory,
        ConsumableChargesAtMost,
        AugmentTagCount,
        AugmentRankCount,
        AugmentFamilyCount,
        RoundAtLeast,
        Feat,
    }

    /// <summary>
    /// When a condition is checked. Acquisition-only conditions gate the draft offer; ongoing ones are re-evaluated
    /// after every finalized loadout change and at duel start and make the augment dormant when unmet (spec L247, L602).
    /// </summary>
    public enum PredicateTiming
    {
        AcquisitionOnly,
        Ongoing,
        Mixed,
    }

    public static class PredicateKinds
    {
        private static readonly string[] Keys =
        {
            "all", "any", "not", "always", "wizard", "equipped_item", "owned_item", "equipped_tag", "augment", "spell",
            "spell_tag", "spell_tag_exact", "spell_distinct_tags", "spell_cooldown_count", "spell_cast_time_count",
            "main_focus_category", "sidearm_category", "issued_slot", "slot_tier", "equipped_category",
            "consumable_charges_at_most", "augment_tag_count", "augment_rank_count", "augment_family_count",
            "round_at_least", "feat",
        };

        private static readonly Dictionary<string, PredicateKind> ByKey = BuildIndex();

        private static Dictionary<string, PredicateKind> BuildIndex()
        {
            var index = new Dictionary<string, PredicateKind>(StringComparer.Ordinal);
            for (int i = 0; i < Keys.Length; i++) index.Add(Keys[i], (PredicateKind)i);
            return index;
        }

        /// <summary>The canonical JSON key ("spell_tag_exact").</summary>
        public static string JsonKey(PredicateKind kind) => Keys[(int)kind];

        public static bool TryParseKey(string key, out PredicateKind kind) => ByKey.TryGetValue(key ?? "", out kind);

        public static IReadOnlyList<string> AllKeys => Keys;

        public static bool IsComposite(PredicateKind kind)
        {
            return kind == PredicateKind.All || kind == PredicateKind.Any || kind == PredicateKind.Not;
        }

        /// <summary>
        /// Wizard identity, round, and completed-duel feats are acquisition-only (spec L247, L602). "always" has no
        /// condition and never needs re-evaluation, so it is classified with them. Every equipment, augment, and
        /// spell leaf is ongoing.
        /// </summary>
        public static bool IsAcquisitionOnly(PredicateKind kind)
        {
            return kind == PredicateKind.Wizard || kind == PredicateKind.RoundAtLeast || kind == PredicateKind.Feat
                || kind == PredicateKind.Always;
        }

        public static bool IsOngoing(PredicateKind kind) => !IsComposite(kind) && !IsAcquisitionOnly(kind);
    }

    /// <summary>
    /// Immutable typed eligibility predicate. Evaluation lives in Economy; Core provides structure, visiting,
    /// plain-language description, reference collection, and canonical JSON.
    /// </summary>
    public abstract class Predicate
    {
        protected static readonly IReadOnlyList<Predicate> NoChildren = Array.AsReadOnly(new Predicate[0]);
        protected static readonly IReadOnlyList<string> NoIds = Array.AsReadOnly(new string[0]);

        public abstract PredicateKind Kind { get; }

        public string JsonKey => PredicateKinds.JsonKey(Kind);

        public bool IsComposite => PredicateKinds.IsComposite(Kind);

        /// <summary>Child predicates of all/any/not; empty for leaves.</summary>
        public virtual IReadOnlyList<Predicate> Children => NoChildren;

        /// <summary>Catalogue or feat IDs named directly by this leaf (including enumerated id lists).</summary>
        public virtual IReadOnlyList<string> DirectReferences => NoIds;

        public abstract T Accept<T>(IPredicateVisitor<T> visitor);

        /// <summary>Acquisition-only, ongoing, or (for composites over both) mixed.</summary>
        public PredicateTiming Timing
        {
            get
            {
                if (!IsComposite) return PredicateKinds.IsAcquisitionOnly(Kind) ? PredicateTiming.AcquisitionOnly : PredicateTiming.Ongoing;
                bool acquisition = false;
                bool ongoing = false;
                foreach (var node in DescendantsAndSelf())
                {
                    if (node.IsComposite) continue;
                    if (PredicateKinds.IsAcquisitionOnly(node.Kind)) acquisition = true;
                    else ongoing = true;
                }
                if (acquisition && ongoing) return PredicateTiming.Mixed;
                return ongoing ? PredicateTiming.Ongoing : PredicateTiming.AcquisitionOnly;
            }
        }

        /// <summary>True when any leaf must be re-evaluated after loadout changes.</summary>
        public bool HasOngoingConditions
        {
            get
            {
                foreach (var node in DescendantsAndSelf())
                    if (PredicateKinds.IsOngoing(node.Kind)) return true;
                return false;
            }
        }

        /// <summary>Pre-order traversal of this node and all descendants.</summary>
        public IEnumerable<Predicate> DescendantsAndSelf()
        {
            var stack = new Stack<Predicate>();
            stack.Push(this);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                yield return node;
                var children = node.Children;
                for (int i = children.Count - 1; i >= 0; i--) stack.Push(children[i]);
            }
        }

        /// <summary>Distinct referenced IDs (wizards, spells, equipment, augments, feats) in first-seen order.</summary>
        public IReadOnlyList<string> CollectReferencedIds()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>();
            foreach (var node in DescendantsAndSelf())
            {
                foreach (var id in node.DirectReferences)
                    if (seen.Add(id)) result.Add(id);
            }
            return result;
        }

        /// <summary>Plain-language card text. <paramref name="nameOf"/> may map IDs to display names (return null if unknown).</summary>
        public string Describe(Func<string, string> nameOf = null)
        {
            return PredicateDescriber.Describe(this, nameOf);
        }

        /// <summary>Canonical JSON. Optional fields are written only when they were present in the source.</summary>
        public abstract JsonValue ToJson();

        public override string ToString() => ToJson().ToString();
    }

    public interface IPredicateVisitor<out T>
    {
        T Visit(AllNode node);
        T Visit(AnyNode node);
        T Visit(NotNode node);
        T Visit(AlwaysLeaf leaf);
        T Visit(WizardLeaf leaf);
        T Visit(EquippedItemLeaf leaf);
        T Visit(OwnedItemLeaf leaf);
        T Visit(EquippedTagLeaf leaf);
        T Visit(AugmentLeaf leaf);
        T Visit(SpellLeaf leaf);
        T Visit(SpellTagLeaf leaf);
        T Visit(SpellTagExactLeaf leaf);
        T Visit(SpellDistinctTagsLeaf leaf);
        T Visit(SpellCooldownCountLeaf leaf);
        T Visit(SpellCastTimeCountLeaf leaf);
        T Visit(MainFocusCategoryLeaf leaf);
        T Visit(SidearmCategoryLeaf leaf);
        T Visit(IssuedSlotLeaf leaf);
        T Visit(SlotTierLeaf leaf);
        T Visit(EquippedCategoryLeaf leaf);
        T Visit(ConsumableChargesAtMostLeaf leaf);
        T Visit(AugmentTagCountLeaf leaf);
        T Visit(AugmentRankCountLeaf leaf);
        T Visit(AugmentFamilyCountLeaf leaf);
        T Visit(RoundAtLeastLeaf leaf);
        T Visit(FeatLeaf leaf);
    }
}
