using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Economy.Loadouts;

namespace Confluence.Economy.Eligibility
{
    /// <summary>
    /// Builds eligibility contexts in the resolution order of spec L641: equipment and spell selection first, then wizard
    /// transformation legality (judged on the base-kit profile), then the structural profile with enabled transformations,
    /// then shared predicates (judged on that profile).
    /// </summary>
    public static class LoadoutEligibility
    {
        /// <summary>Profile for judging transformations: base kit tags plus live equipment structural changes.</summary>
        public static StructuralProfile TransformationProfile(Loadout loadout, IStructuralChangeSource changes) =>
            StructuralProfile.Build(loadout, null, changes);

        /// <summary>Profile for judging shared augments: also applies the given enabled transformations (spec L243).</summary>
        public static StructuralProfile SharedProfile(Loadout loadout, IEnumerable<AugmentDef> enabledTransformations, IStructuralChangeSource changes) =>
            StructuralProfile.Build(loadout, enabledTransformations, changes);

        /// <summary>Context for judging a draft candidate against the loadout's current resolution.</summary>
        public static EligibilityContext ForCandidate(Loadout loadout, AugmentDef candidate, RoundInfo round, IFeatCounts feats, IStructuralChangeSource changes) =>
            new CandidateJudge(loadout, round, feats, changes).ContextFor(candidate);

        /// <summary>Evaluates and explains a candidate's eligibility right now (codex and draft card text, spec L237).</summary>
        public static EligibilityResult ExplainCandidate(Loadout loadout, AugmentDef candidate, RoundInfo round, IFeatCounts feats, IStructuralChangeSource changes) =>
            new CandidateJudge(loadout, round, feats, changes).Explain(candidate);
    }

    /// <summary>
    /// Judges augments the player does not yet own (draft candidates, codex inspection) against one loadout state. The
    /// loadout's non-dormant augments qualify, wizard/round/feat leaves are read live, and a candidate never counts
    /// itself. Profiles are built once per judge.
    /// </summary>
    public sealed class CandidateJudge
    {
        private readonly EligibilityContext _transformationContext;
        private readonly EligibilityContext _sharedContext;

        public CandidateJudge(Loadout loadout, RoundInfo round, IFeatCounts feats, IStructuralChangeSource changes)
        {
            Loadout = loadout ?? throw new ArgumentNullException(nameof(loadout));
            Round = round;
            Feats = feats ?? FeatLedger.None;
            var active = loadout.ActiveAugments;
            var activeTransformations = new List<OwnedAugment>();
            var transformationDefs = new List<AugmentDef>();
            foreach (var a in active)
            {
                if (!a.IsTransformation) continue;
                activeTransformations.Add(a);
                transformationDefs.Add(a.Definition);
            }

            // Transformations resolve before shared predicates (spec L641), so they see only other transformations.
            _transformationContext = new EligibilityContext(loadout, LoadoutEligibility.TransformationProfile(loadout, changes),
                activeTransformations.AsReadOnly(), round, Feats);
            _sharedContext = new EligibilityContext(loadout, LoadoutEligibility.SharedProfile(loadout, transformationDefs, changes),
                active, round, Feats);
        }

        public Loadout Loadout { get; }
        public RoundInfo Round { get; }
        public IFeatCounts Feats { get; }

        /// <summary>The structural profile shared candidates are judged against.</summary>
        public StructuralProfile SharedProfile => _sharedContext.Profile;

        public EligibilityContext ContextFor(AugmentDef candidate)
        {
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            var basis = candidate.IsTransformation ? _transformationContext : _sharedContext;
            return basis.WithSelf(candidate.Id, null);
        }

        public bool IsEligible(AugmentDef candidate) => PredicateEvaluator.Evaluate(candidate.Eligibility, ContextFor(candidate));

        public EligibilityResult Explain(AugmentDef candidate) => EligibilityExplainer.Explain(candidate.Eligibility, ContextFor(candidate));

        /// <summary>Records the acquisition-time values of the candidate's wizard, round, and feat leaves (spec L247).</summary>
        public AcquisitionRecord CaptureAcquisition(AugmentDef candidate)
        {
            var evaluator = new PredicateEvaluator(ContextFor(candidate));
            return AcquisitionRecord.Capture(candidate.Eligibility, Round, Loadout.WizardId, leaf => leaf.Accept(evaluator));
        }
    }
}
