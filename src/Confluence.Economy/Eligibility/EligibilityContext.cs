using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Economy.Loadouts;

namespace Confluence.Economy.Eligibility
{
    /// <summary>
    /// Everything a predicate may read (spec L237-L249): the wizard, the finalized loadout and its structural spell
    /// profile, the qualifying active parent augments, the scheduled round, and completed-duel feats. It never contains
    /// the opponent's build, future arena details, or client claims (spec L249, L602).
    /// When <see cref="Acquisition"/> is set, wizard, round, and feat leaves return their recorded acquisition-time
    /// values instead of being read live (spec L247).
    /// </summary>
    public sealed class EligibilityContext
    {
        private static readonly IReadOnlyList<OwnedAugment> NoAugments = Array.AsReadOnly(new OwnedAugment[0]);

        public EligibilityContext(Loadout loadout, StructuralProfile profile, IReadOnlyList<OwnedAugment> qualifyingAugments,
            RoundInfo round, IFeatCounts feats, string selfId = null, AcquisitionRecord acquisition = null)
        {
            Loadout = loadout ?? throw new ArgumentNullException(nameof(loadout));
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            QualifyingAugments = qualifyingAugments ?? NoAugments;
            Round = round;
            Feats = feats;
            SelfId = selfId;
            Acquisition = acquisition;
        }

        public Loadout Loadout { get; }
        public Catalogue Catalogue => Loadout.Catalogue;
        public string WizardId => Loadout.WizardId;

        /// <summary>Profiles of the six equipped spells (spec L243).</summary>
        public StructuralProfile Profile { get; }

        /// <summary>Owned augments that may qualify a dependent: active ones only (spec L602 "A disabled or removed parent cannot qualify descendants").</summary>
        public IReadOnlyList<OwnedAugment> QualifyingAugments { get; }

        /// <summary>Scheduled round for live round_at_least leaves; may be null when an acquisition record supplies them.</summary>
        public RoundInfo Round { get; }

        /// <summary>Completed-duel feat counts for live feat leaves; may be null when an acquisition record supplies them.</summary>
        public IFeatCounts Feats { get; }

        /// <summary>The augment being judged; it never counts toward its own augment leaves (spec L247 "cannot qualify itself").</summary>
        public string SelfId { get; }

        public AcquisitionRecord Acquisition { get; }

        public EligibilityContext WithSelf(string selfId, AcquisitionRecord acquisition) =>
            new EligibilityContext(Loadout, Profile, QualifyingAugments, Round, Feats, selfId, acquisition);
    }
}
