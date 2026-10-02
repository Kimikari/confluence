using System;
using System.Collections.Generic;
using System.Text;
using Confluence.Core.Json;
using Confluence.Core.Rules;

namespace Confluence.Core.Content
{
    /// <summary>
    /// Plain-language card text for eligibility predicates (spec L237 "explained in plain language on the card").
    /// Nested composites are parenthesized so the reading is unambiguous.
    /// </summary>
    public static class PredicateDescriber
    {
        public static string Describe(Predicate predicate, Func<string, string> nameOf = null)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            string text = predicate.Accept(new Visitor(nameOf));
            int first = 0;
            while (first < text.Length && text[first] == '(') first++;
            if (first == text.Length) return text;
            return text.Substring(0, first) + char.ToUpperInvariant(text[first]) + text.Substring(first + 1);
        }

        private sealed class Visitor : IPredicateVisitor<string>
        {
            private readonly Func<string, string> _nameOf;

            public Visitor(Func<string, string> nameOf)
            {
                _nameOf = nameOf;
            }

            public string Visit(AllNode node) => Join(node.Children, " and ");
            public string Visit(AnyNode node) => Join(node.Children, " or ");

            public string Visit(NotNode node)
            {
                var child = node.Child;
                if (child is EquippedTagLeaf tag && tag.Count == 1)
                    return "no equipped item supplying the '" + tag.Tag + "' tag";
                return "not " + Wrapped(child);
            }

            public string Visit(AlwaysLeaf leaf) => "no prerequisite";
            public string Visit(WizardLeaf leaf) => "playing " + Ref(leaf.WizardId);
            public string Visit(EquippedItemLeaf leaf) => Ref(leaf.EquipmentId) + " equipped";
            public string Visit(OwnedItemLeaf leaf) => Ref(leaf.EquipmentId) + " owned (reserve counts)";

            public string Visit(EquippedTagLeaf leaf) =>
                "at least " + Count(leaf.Count, "equipped item") + " supplying the '" + leaf.Tag + "' tag";

            public string Visit(AugmentLeaf leaf) =>
                Ref(leaf.AugmentId) + " active" + (leaf.Rank > 1 ? " at rank " + leaf.Rank + " or higher" : "");

            public string Visit(SpellLeaf leaf) => Ref(leaf.SpellId) + " in your exclusive kit";

            public string Visit(SpellTagLeaf leaf)
            {
                var sb = new StringBuilder("at least " + Count(leaf.Count, "equipped spell") + " tagged '" + leaf.Tag + "'");
                if (leaf.Also.Count > 0) sb.Append(" and also ").Append(TagList(leaf.Also, " and "));
                return sb.ToString();
            }

            public string Visit(SpellTagExactLeaf leaf) =>
                "exactly " + Count(leaf.Count, "equipped spell") + " tagged '" + leaf.Tag + "'";

            public string Visit(SpellDistinctTagsLeaf leaf)
            {
                var sb = new StringBuilder("at least " + leaf.Count + " different tags among " + TagList(leaf.Set, ", ") + " across equipped spells");
                if (leaf.Also.Count > 0) sb.Append(" tagged ").Append(TagList(leaf.Also, " and "));
                return sb.ToString();
            }

            public string Visit(SpellCooldownCountLeaf leaf) =>
                "at least " + Count(leaf.Count, "equipped spell") + " with base cooldown " + Bound(leaf.Threshold) + " s";

            public string Visit(SpellCastTimeCountLeaf leaf) =>
                "at least " + Count(leaf.Count, "equipped spell") + " with intrinsic windup " + Bound(leaf.Threshold) + " s";

            public string Visit(MainFocusCategoryLeaf leaf) => Article(ContentText.CategoryName(leaf.Category).ToLowerInvariant()) + " in the Main Focus slot";
            public string Visit(SidearmCategoryLeaf leaf) => Article(ContentText.CategoryName(leaf.Category).ToLowerInvariant()) + " in the Sidearm slot";
            public string Visit(IssuedSlotLeaf leaf) => "only the plain issued item in the " + ContentText.SlotName(leaf.Slot) + " slot";

            public string Visit(SlotTierLeaf leaf) =>
                "tier " + leaf.AtLeast + " or higher equipment in the " + ContentText.SlotName(leaf.Slot) + " slot";

            public string Visit(EquippedCategoryLeaf leaf) =>
                "at least " + Count(leaf.Count, ContentText.CategoryName(leaf.Category).ToLowerInvariant() + " item") + " equipped"
                + (leaf.DistinctUniqueGroups ? " with different Unique groups" : "");

            public string Visit(ConsumableChargesAtMostLeaf leaf) =>
                leaf.AtMost == 0
                    ? "no purchased consumable charges held"
                    : "at most " + Count(leaf.AtMost, "purchased consumable charge") + " held";

            public string Visit(AugmentTagCountLeaf leaf) =>
                "at least " + Count(leaf.Count, "active augment") + " tagged '" + leaf.Tag + "'" + Among(leaf.IsRestricted, leaf.Ids);

            public string Visit(AugmentRankCountLeaf leaf) =>
                "at least " + Count(leaf.Count, "active augment") + " at rank " + leaf.RankAtLeast + " or higher"
                + (leaf.DistinctFamilies ? " from different families" : "") + Among(leaf.IsRestricted, leaf.Ids);

            public string Visit(AugmentFamilyCountLeaf leaf) =>
                "active augments from at least " + leaf.Count + " different families" + Among(leaf.IsRestricted, leaf.Ids);

            public string Visit(RoundAtLeastLeaf leaf) => "round " + leaf.Round + " or later";

            public string Visit(FeatLeaf leaf)
            {
                string feat = Feats.TryGet(leaf.FeatId, out var def) ? leaf.FeatId + " (" + def.Description + ")" : leaf.FeatId;
                return "feat " + feat + " completed in " + (leaf.AtLeast <= 1 ? "a previous duel" : "at least " + leaf.AtLeast + " previous duels");
            }

            private string Join(IReadOnlyList<Predicate> children, string separator)
            {
                if (children.Count == 1) return children[0].Accept(this);
                var parts = new string[children.Count];
                for (int i = 0; i < children.Count; i++) parts[i] = Wrapped(children[i]);
                return string.Join(separator, parts);
            }

            private string Wrapped(Predicate child)
            {
                string text = child.Accept(this);
                bool compound = (child.Kind == PredicateKind.All || child.Kind == PredicateKind.Any) && child.Children.Count > 1;
                return compound ? "(" + text + ")" : text;
            }

            private string Ref(string id)
            {
                string name = _nameOf?.Invoke(id);
                return string.IsNullOrEmpty(name) ? id : name + " (" + id + ")";
            }

            private static string Among(bool restricted, IReadOnlyList<string> ids)
            {
                if (!restricted) return "";
                return " from " + (ids.Count == 1 ? "1 listed parent" : ids.Count + " listed parents");
            }

            private static string Count(int n, string noun) => n + " " + noun + (n == 1 ? "" : "s");

            private static string Bound(Threshold t) =>
                (t.Bound == BoundKind.AtLeast ? "at least " : "at most ") + JsonWriter.FormatNumber(t.Value);

            private static string Article(string noun)
            {
                if (noun.Length == 0) return noun;
                char c = char.ToLowerInvariant(noun[0]);
                return (c == 'a' || c == 'e' || c == 'i' || c == 'o' || c == 'u' ? "an " : "a ") + noun;
            }

            private static string TagList(IReadOnlyList<string> tags, string separator)
            {
                var quoted = new string[tags.Count];
                for (int i = 0; i < tags.Count; i++) quoted[i] = "'" + tags[i] + "'";
                return string.Join(separator, quoted);
            }
        }
    }
}
