using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;

namespace Confluence.Sim.Content
{
    /// <summary>
    /// A shared augment's edit of a resolved spell structure (spec L543: "For actual combat, apply all active general
    /// augments to that resolved structure, then mastery and global caps"; L286 order: base, delivery/trajectory,
    /// payload/reaction, equipment, then mastery and caps). An <see cref="IAugmentBehavior"/> that also implements this
    /// interface edits each equipped exclusive spell form of its owner after the wizard transformations, in ascending
    /// augment id order, before the form's global caps are applied and it freezes. Signature techniques are not spells
    /// and are not edited. The edit decides from the form's own structure (delivery kind, tags, numbers) whether the
    /// record applies.
    /// </summary>
    public interface IAugmentFormStage
    {
        void ApplyToForm(SpellFormBuilder form, int rank);
    }

    /// <summary>One active shared augment that edits spell forms, with its rank.</summary>
    public sealed class ActiveFormStage
    {
        public ActiveFormStage(string augmentId, int rank, IAugmentFormStage stage)
        {
            AugmentId = augmentId ?? throw new ArgumentNullException(nameof(augmentId));
            Rank = Math.Max(1, Math.Min(2, rank));
            Stage = stage ?? throw new ArgumentNullException(nameof(stage));
        }

        public string AugmentId { get; }
        public int Rank { get; }
        public IAugmentFormStage Stage { get; }
    }

    /// <summary>Finds and applies the form stages of a player's active shared augments.</summary>
    public static class AugmentFormStages
    {
        /// <summary>The player's active shared augments with a registered form stage, in ascending augment id order.</summary>
        public static List<ActiveFormStage> For(DuelPlayerSetup setup, Catalogue catalogue, ContentRegistry registry)
        {
            var result = new List<ActiveFormStage>();
            if (setup == null || setup.Augments == null || registry == null)
            {
                return result;
            }

            for (int i = 0; i < setup.Augments.Count; i++)
            {
                ActiveAugment a = setup.Augments[i];
                if (a.Id == null)
                {
                    continue;
                }

                if (catalogue != null && catalogue.AugmentsById.TryGetValue(a.Id, out AugmentDef def) && def.IsTransformation)
                {
                    continue;
                }

                if (registry.TryGetAugment(a.Id, out IAugmentBehavior behavior) && behavior is IAugmentFormStage stage)
                {
                    result.Add(new ActiveFormStage(a.Id, a.Rank, stage));
                }
            }

            result.Sort((x, y) => string.CompareOrdinal(x.AugmentId, y.AugmentId));
            return result;
        }

        /// <summary>Applies the stages to a form under construction (after transformations, before caps).</summary>
        public static void Apply(SpellFormBuilder form, IReadOnlyList<ActiveFormStage> stages)
        {
            if (form == null) throw new ArgumentNullException(nameof(form));
            if (stages == null || form.IsSignature) return;
            for (int i = 0; i < stages.Count; i++)
            {
                stages[i].Stage.ApplyToForm(form, stages[i].Rank);
            }
        }
    }
}
