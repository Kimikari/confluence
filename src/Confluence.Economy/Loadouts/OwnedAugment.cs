using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Economy.Eligibility;

namespace Confluence.Economy.Loadouts
{
    /// <summary>
    /// A free augment held in one of the six slots at rank 1 or 2 (spec L231). Its dormancy status is set by
    /// <see cref="Augments.DormancyResolver"/>: a dormant augment keeps its slot and rank but supplies no effect, tag, or
    /// parent qualification (spec L247, L641).
    /// </summary>
    public sealed class OwnedAugment
    {
        private static readonly IReadOnlyList<MissingRequirement> NoMissing = Array.AsReadOnly(new MissingRequirement[0]);
        private static readonly IReadOnlyList<string> NoIds = Array.AsReadOnly(new string[0]);

        internal OwnedAugment(AugmentDef definition, int rank, AcquisitionRecord acquisition, RoundInfo acquiredAt)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            if (rank < 1 || rank > GlobalRules.Draft.MaxRank) throw new ArgumentOutOfRangeException(nameof(rank));
            Acquisition = acquisition ?? throw new ArgumentNullException(nameof(acquisition));
            Rank = rank;
            AcquiredAt = acquiredAt;
            MissingRequirements = NoMissing;
            DormantParents = NoIds;
        }

        public AugmentDef Definition { get; }
        public string Id => Definition.Id;

        /// <summary>Current rank, 1 or 2 (spec L231).</summary>
        public int Rank { get; internal set; }

        /// <summary>The rarity the definition was drafted at; a rank 2 can appear at this rarity or later (spec L231, L600).</summary>
        public Rarity OriginalRarity => Definition.RarityEnum;

        public bool IsTransformation => Definition.IsTransformation;

        /// <summary>The draft that granted rank 1 (null for setup grants).</summary>
        public RoundInfo AcquiredAt { get; }

        /// <summary>The draft that granted rank 2, if any.</summary>
        public RoundInfo UpgradedAt { get; internal set; }

        /// <summary>Acquisition-time values of wizard, round, and feat leaves (spec L247).</summary>
        public AcquisitionRecord Acquisition { get; }

        /// <summary>True when its ongoing requirements are unmet at the last finalization (spec L641).</summary>
        public bool IsDormant { get; internal set; }

        public bool IsActive => !IsDormant;

        /// <summary>What a dormant augment is missing, in plain language; empty when active.</summary>
        public IReadOnlyList<MissingRequirement> MissingRequirements { get; internal set; }

        /// <summary>Owned parents this augment references that are themselves dormant (spec L247 "a dormant parent makes dependents dormant").</summary>
        public IReadOnlyList<string> DormantParents { get; internal set; }

        internal void SetStatus(bool dormant, IReadOnlyList<MissingRequirement> missing, IReadOnlyList<string> dormantParents)
        {
            IsDormant = dormant;
            MissingRequirements = missing ?? NoMissing;
            DormantParents = dormantParents ?? NoIds;
        }

        internal OwnedAugment Clone()
        {
            var copy = new OwnedAugment(Definition, Rank, Acquisition, AcquiredAt) { UpgradedAt = UpgradedAt };
            copy.SetStatus(IsDormant, MissingRequirements, DormantParents);
            return copy;
        }

        public override string ToString() => Id + " r" + Rank + (IsDormant ? " (dormant)" : "");
    }
}
