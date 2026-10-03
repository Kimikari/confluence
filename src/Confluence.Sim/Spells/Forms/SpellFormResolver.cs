using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Sim.Content;

namespace Confluence.Sim.Spells
{
    /// <summary>One active transformation on a spell: its augment, rank, and behavior.</summary>
    public sealed class ActiveTransform
    {
        public ActiveTransform(string augmentId, int rank, ISpellTransform behavior, string exclusiveGroup = "")
        {
            if (string.IsNullOrEmpty(augmentId)) throw new ArgumentException("An augment id is required.", nameof(augmentId));
            if (rank < 1 || rank > 2) throw new ArgumentOutOfRangeException(nameof(rank), "Ranks are 1 or 2.");
            AugmentId = augmentId;
            Rank = rank;
            Behavior = behavior ?? throw new ArgumentNullException(nameof(behavior));
            ExclusiveGroup = exclusiveGroup ?? string.Empty;
        }

        public string AugmentId { get; }
        public int Rank { get; }
        public ISpellTransform Behavior { get; }

        /// <summary>Mutually exclusive branches share a non-empty group (spec L510, L541).</summary>
        public string ExclusiveGroup { get; }
    }

    /// <summary>
    /// Resolves a spell form for one duel: base behavior, then transformations in stage order (delivery/trajectory,
    /// payload/reaction, equipment) and ascending augment id inside a stage (spec L286, L510), structural tag deltas
    /// declared in <see cref="StructuralDeltas"/> (D-012), and global caps. Two branches of one exclusive group are
    /// refused (spec L286 "cannot silently coexist"). Mastery is applied per cast by the runtime.
    /// </summary>
    public static class SpellFormResolver
    {
        public static SpellForm Resolve(SpellDef def, ISpellBehavior behavior, IReadOnlyList<ActiveTransform> transforms = null,
            IReadOnlyList<ActiveFormStage> sharedAugments = null)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (behavior == null) throw new ArgumentNullException(nameof(behavior));
            SpellFormBuilder form = SpellFormBuilder.FromDefinition(def);
            form.Parameters.AddRange(behavior.Parameters);
            behavior.Build(form);
            Apply(form, transforms);
            AugmentFormStages.Apply(form, sharedAugments); // spec L543: general augments on the resolved structure, then caps
            return form.Build();
        }

        /// <summary>Applies transformations to a form under construction (exposed for signatures and tests).</summary>
        public static void Apply(SpellFormBuilder form, IReadOnlyList<ActiveTransform> transforms)
        {
            if (form == null) throw new ArgumentNullException(nameof(form));
            if (transforms == null || transforms.Count == 0) return;
            var ordered = new List<ActiveTransform>(transforms);
            CheckExclusiveGroups(form.SpellId, ordered);
            ordered.Sort(CompareApplicationOrder);
            for (int i = 0; i < ordered.Count; i++)
            {
                ActiveTransform t = ordered[i];
                if (StructuralDeltas.TryGet(t.AugmentId, out StructuralDelta delta))
                {
                    if (!string.Equals(delta.TargetSpellId, form.SpellId, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(t.AugmentId + " declares a structural delta for " + delta.TargetSpellId + ", not " + form.SpellId + ".");
                    }

                    IReadOnlyList<string> tags = delta.Apply(form.Tags);
                    form.Tags.Clear();
                    form.Tags.AddRange(tags);
                }

                form.Parameters.AddRange(t.Behavior.Parameters);
                t.Behavior.Apply(form, t.Rank);
                form.AppliedTransforms.Add(t.AugmentId);
            }
        }

        private static int CompareApplicationOrder(ActiveTransform a, ActiveTransform b)
        {
            int stage = ((int)a.Behavior.Stage).CompareTo((int)b.Behavior.Stage);
            return stage != 0 ? stage : string.CompareOrdinal(a.AugmentId, b.AugmentId);
        }

        private static void CheckExclusiveGroups(string spellId, List<ActiveTransform> transforms)
        {
            for (int i = 0; i < transforms.Count; i++)
            {
                for (int j = i + 1; j < transforms.Count; j++)
                {
                    if (string.Equals(transforms[i].AugmentId, transforms[j].AugmentId, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(transforms[i].AugmentId + " is active twice on " + spellId + ".");
                    }

                    string g = transforms[i].ExclusiveGroup;
                    if (g.Length > 0 && string.Equals(g, transforms[j].ExclusiveGroup, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(transforms[i].AugmentId + " and " + transforms[j].AugmentId + " share exclusive group '" + g +
                            "' and cannot both transform " + spellId + " (spec L286, L510).");
                    }
                }
            }
        }
    }
}
