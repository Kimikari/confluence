using System;
using System.Collections.Generic;
using Confluence.Core.Json;

namespace Confluence.Core.Content
{
    /// <summary>
    /// One of the 48 worked builds. BLD-001..036 are per-wizard plans with a draft plan and equipment cost;
    /// BLD-037..048 demonstrate acquisition conditions and carry an eligibility predicate (spec L691-L693).
    /// </summary>
    public sealed class BuildDef : ContentDef
    {
        private static readonly IReadOnlyDictionary<string, int> NoRanks = new Dictionary<string, int>();

        internal BuildDef(RecordReader r)
            : base(r.RequireId(), r.String("name"), r.StringList("tags"), r.Json)
        {
            WizardId = r.String("wizard_id");
            Objective = r.String("objective");
            EquippedSpellIds = r.StringList("equipped_spell_ids");
            EquipmentIds = r.StringList("equipment_ids");
            AugmentIds = r.StringList("augment_ids");
            Counterplay = r.String("counterplay");
            Constraints = r.String("constraints");

            // Per-wizard plan fields.
            DraftPlan = r.OptionalStringList("draft_plan");
            EquipmentCost = r.OptionalInt("equipment_cost");
            EquipmentInteractions = r.OptionalStringList("equipment_interactions");
            TransformationInteractions = r.OptionalStringList("transformation_interactions");
            Sequence = r.OptionalString("sequence");
            MasteryConnection = r.OptionalString("mastery_connection");

            // Conditional example fields.
            UnlockPlan = r.OptionalString("unlock_plan");
            EligibilityJson = r.OptionalObject("eligibility");
            CombinedInteraction = r.OptionalString("combined_interaction");
            FreeChoiceBudget = r.OptionalString("free_choice_budget");
            var ranks = r.OptionalObject("augment_ranks");
            r.Finish();

            if (EligibilityJson != null)
            {
                try
                {
                    Eligibility = PredicateParser.Parse(EligibilityJson, Id);
                }
                catch (PredicateFormatException e)
                {
                    throw new CatalogueFormatException(Id, e.Message, e);
                }
            }

            if (ranks == null) AugmentRanks = NoRanks;
            else
            {
                var map = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var member in ranks.AsObject())
                {
                    var v = member.Value;
                    if (!v.IsNumber || v.AsDouble() != Math.Floor(v.AsDouble()))
                        throw r.Fail("augment_ranks['" + member.Key + "'] must be an integer");
                    map.Add(member.Key, (int)v.AsDouble());
                }
                AugmentRanks = map;
            }
        }

        public override ContentType Type => ContentType.Build;

        public string WizardId { get; }
        public string Objective { get; }
        public IReadOnlyList<string> EquippedSpellIds { get; }
        public IReadOnlyList<string> EquipmentIds { get; }
        public IReadOnlyList<string> AugmentIds { get; }
        public string Counterplay { get; }
        public string Constraints { get; }

        /// <summary>Round-by-round free selections ("Round 1: AUG-149 First Savings rank 1 (free)"); empty if absent.</summary>
        public IReadOnlyList<string> DraftPlan { get; }

        /// <summary>Total final gear price excluding issued items (spec L697); null if absent.</summary>
        public int? EquipmentCost { get; }

        public IReadOnlyList<string> EquipmentInteractions { get; }
        public IReadOnlyList<string> TransformationInteractions { get; }
        public string Sequence { get; }
        public string MasteryConnection { get; }

        public string UnlockPlan { get; }

        /// <summary>The demonstrated acquisition condition, or null for per-wizard plans.</summary>
        public Predicate Eligibility { get; }

        public JsonValue EligibilityJson { get; }
        public string CombinedInteraction { get; }
        public string FreeChoiceBudget { get; }

        /// <summary>Explicit target ranks by augment ID; augments not listed are rank 1.</summary>
        public IReadOnlyDictionary<string, int> AugmentRanks { get; }

        public bool IsConditionalExample => Eligibility != null;

        public int RankOf(string augmentId)
        {
            return AugmentRanks.TryGetValue(augmentId, out int rank) ? rank : 1;
        }
    }
}
