using System;
using System.Collections.Generic;
using Confluence.Core.Json;
using Confluence.Core.Rules;

namespace Confluence.Core.Content
{
    /// <summary>
    /// One of the 864 free augments: 288 shared definitions and 576 wizard spell transformations (spec L594).
    /// A transformation is identified by a non-empty target_spell_ids list.
    /// </summary>
    public sealed class AugmentDef : ContentDef
    {
        public const int MaxRank = 2; // spec L231

        private readonly Rarity _rarity;
        private readonly bool _rarityValid;

        internal AugmentDef(RecordReader r)
            : base(r.RequireId(), r.String("name"), r.StringList("tags"), r.Json)
        {
            Family = r.String("family");
            Condition = r.String("condition");
            Rank1 = r.String("rank1");
            Rank2 = r.String("rank2");
            Drawback = r.String("drawback");
            Counterplay = r.String("counterplay");
            EligibleWizards = r.StringList("eligible_wizards");
            Rarity = r.String("rarity");
            EligibilityJson = r.Object("eligibility");
            ExclusiveGroup = r.String("exclusive_group");
            TargetSpellIds = r.StringList("target_spell_ids");

            // Optional fields present on transformation records only.
            Cost = r.OptionalNumber("cost");
            RawSpellId = r.OptionalString("raw_spell_id");
            WizardId = r.OptionalString("wizard_id");
            SpellId = r.OptionalString("spell_id");
            StackingRule = r.OptionalString("stacking_rule");
            TargetSpellName = r.OptionalString("target_spell_name");
            ActivationRule = r.OptionalString("activation_rule");
            ReactionControl = r.OptionalString("reaction_control");
            r.Finish();

            try
            {
                Eligibility = PredicateParser.Parse(EligibilityJson, Id);
            }
            catch (PredicateFormatException e)
            {
                throw new CatalogueFormatException(Id, e.Message, e);
            }
            _rarityValid = ContentText.TryParseRarity(Rarity, out _rarity);
        }

        public override ContentType Type => ContentType.Augment;

        public string Family { get; }

        /// <summary>Plain-language trigger and draft requirement text.</summary>
        public string Condition { get; }

        public string Rank1 { get; }
        public string Rank2 { get; }
        public string Drawback { get; }
        public string Counterplay { get; }

        /// <summary>Wizards allowed to draft this record; the single owner for transformations, empty for shared augments.</summary>
        public IReadOnlyList<string> EligibleWizards { get; }

        /// <summary>Rarity as written ("Silver"). See <see cref="RarityEnum"/>.</summary>
        public string Rarity { get; }

        /// <summary>Typed eligibility predicate (spec "Predicate grammar").</summary>
        public Predicate Eligibility { get; }

        /// <summary>The eligibility object exactly as it appears in the catalogue.</summary>
        public JsonValue EligibilityJson { get; }

        /// <summary>Mutually exclusive branches share a non-empty group; empty means compatible (spec L541).</summary>
        public string ExclusiveGroup { get; }

        public IReadOnlyList<string> TargetSpellIds { get; }

        public double? Cost { get; }
        public string RawSpellId { get; }
        public string WizardId { get; }
        public string SpellId { get; }
        public string StackingRule { get; }
        public string TargetSpellName { get; }
        public string ActivationRule { get; }
        public string ReactionControl { get; }

        public bool IsTransformation => TargetSpellIds.Count > 0;
        public bool IsShared => TargetSpellIds.Count == 0;
        public bool HasExclusiveGroup => ExclusiveGroup.Length > 0;

        /// <summary>Unconditional: the eligibility is exactly {"always": true}.</summary>
        public bool IsUnconditional => Eligibility is AlwaysLeaf;

        /// <summary>The transformed spell, or null for shared augments.</summary>
        public string TargetSpellId => TargetSpellIds.Count > 0 ? TargetSpellIds[0] : null;

        /// <summary>The wizard owning a transformation (its single eligible wizard, else its wizard_id field); null for shared augments.</summary>
        public string OwnerWizardId
        {
            get
            {
                if (!IsTransformation) return null;
                if (EligibleWizards.Count == 1) return EligibleWizards[0];
                return WizardId;
            }
        }

        public bool HasValidRarity => _rarityValid;

        public Rarity RarityEnum
        {
            get
            {
                if (!_rarityValid) throw new InvalidOperationException(Id + " has unrecognised rarity '" + Rarity + "'.");
                return _rarity;
            }
        }

        public bool TryGetRarity(out Rarity rarity)
        {
            rarity = _rarity;
            return _rarityValid;
        }
    }
}
