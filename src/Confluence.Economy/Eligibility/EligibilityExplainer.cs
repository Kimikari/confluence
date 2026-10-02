using System;
using System.Collections.Generic;
using System.Text;
using Confluence.Core.Content;

namespace Confluence.Economy.Eligibility
{
    /// <summary>Whether a predicate holds, and if not, the minimal list of unmet requirements for the card or codex.</summary>
    public sealed class EligibilityResult
    {
        private static readonly IReadOnlyList<MissingRequirement> None = Array.AsReadOnly(new MissingRequirement[0]);

        internal EligibilityResult(bool isMet, IReadOnlyList<MissingRequirement> missing)
        {
            IsMet = isMet;
            Missing = missing ?? None;
        }

        public bool IsMet { get; }

        /// <summary>Empty when met.</summary>
        public IReadOnlyList<MissingRequirement> Missing { get; }

        /// <summary>One line for card UI: "Missing: a; b".</summary>
        public string Summary
        {
            get
            {
                if (IsMet) return "All requirements met.";
                var parts = new string[Missing.Count];
                for (int i = 0; i < parts.Length; i++) parts[i] = Missing[i].Text;
                return "Missing: " + string.Join("; ", parts) + ".";
            }
        }
    }

    /// <summary>
    /// Evaluates a predicate and explains each failing part in plain language with <see cref="PredicateDescriber"/>
    /// (spec L237, L641, L651): every failing child of a failing ALL, a failing ANY as one "one of" requirement, a failing
    /// NOT, and failing leaves. Count leaves add the current count so the player sees how far they are.
    /// </summary>
    public static class EligibilityExplainer
    {
        public static EligibilityResult Explain(Predicate predicate, EligibilityContext context)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            var evaluator = new PredicateEvaluator(context);
            if (predicate.Accept(evaluator)) return new EligibilityResult(true, null);
            var missing = new List<MissingRequirement>();
            Collect(predicate, context, evaluator, missing);
            return new EligibilityResult(false, missing.AsReadOnly());
        }

        private static void Collect(Predicate node, EligibilityContext ctx, PredicateEvaluator evaluator, List<MissingRequirement> missing)
        {
            if (node is AllNode all)
            {
                foreach (var child in all.Children)
                    if (!child.Accept(evaluator)) Collect(child, ctx, evaluator, missing);
                return;
            }
            if (node is AnyNode any && any.Children.Count == 1)
            {
                Collect(any.Children[0], ctx, evaluator, missing);
                return;
            }
            missing.Add(new MissingRequirement(node, Describe(node, ctx)));
        }

        private static string Describe(Predicate node, EligibilityContext ctx)
        {
            var sb = new StringBuilder(PredicateDescriber.Describe(node, ctx.Catalogue.NameOf));
            if (node is AnyNode) sb.Insert(0, "one of: ");
            int? current = CurrentCount(node, ctx, new PredicateEvaluator(ctx));
            if (current.HasValue) sb.Append(" (currently ").Append(current.Value).Append(')');
            if (node.Timing == PredicateTiming.AcquisitionOnly) sb.Append(" [acquisition only]");
            return sb.ToString();
        }

        /// <summary>The current value a count leaf compares against, or null for leaves without a count.</summary>
        public static int? CurrentCount(Predicate leaf, EligibilityContext ctx, PredicateEvaluator evaluator)
        {
            switch (leaf)
            {
                case EquippedTagLeaf tag:
                    return evaluator.CountTagSources(tag.Tag);
                case SpellTagLeaf spell:
                {
                    int n = 0;
                    foreach (var p in ctx.Profile.Spells)
                        if (p.HasTag(spell.Tag) && p.HasAllTags(spell.Also)) n++;
                    return n;
                }
                case SpellTagExactLeaf exact:
                {
                    int n = 0;
                    foreach (var p in ctx.Profile.Spells)
                        if (p.HasTag(exact.Tag)) n++;
                    return n;
                }
                case ConsumableChargesAtMostLeaf _:
                    return ctx.Loadout.HeldConsumableCharges;
                case FeatLeaf feat:
                    return ctx.Feats?.CompletedDuelsMeeting(feat.FeatId);
                default:
                    return null;
            }
        }
    }
}
