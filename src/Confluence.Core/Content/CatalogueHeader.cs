using System;
using System.Collections.Generic;
using Confluence.Core.Json;

namespace Confluence.Core.Content
{
    /// <summary>The catalogue's declared "counts" block (record totals per type).</summary>
    public sealed class CatalogueCounts
    {
        private readonly Dictionary<string, int> _byKey;

        internal CatalogueCounts(JsonValue json)
        {
            if (json == null || !json.IsObject) throw new CatalogueFormatException("counts", "'counts' must be an object");
            _byKey = new Dictionary<string, int>(StringComparer.Ordinal);
            var keys = new List<string>();
            foreach (var member in json.AsObject())
            {
                var v = member.Value;
                if (!v.IsNumber || v.AsDouble() != Math.Floor(v.AsDouble()) || v.AsDouble() < 0)
                    throw new CatalogueFormatException("counts", "count '" + member.Key + "' must be a non-negative integer");
                _byKey.Add(member.Key, (int)v.AsDouble());
                keys.Add(member.Key);
            }
            Keys = keys.AsReadOnly();
            Raw = json;
        }

        /// <summary>Declared keys in document order (normally the eight content types).</summary>
        public IReadOnlyList<string> Keys { get; }

        public JsonValue Raw { get; }

        public bool TryGet(string key, out int count) => _byKey.TryGetValue(key, out count);

        /// <summary>Declared count for a content type, or null when the block omits it.</summary>
        public int? Of(ContentType type) => _byKey.TryGetValue(ContentIds.JsonKey(type), out int n) ? n : (int?)null;

        public int Wizards => Of(ContentType.Wizard) ?? 0;
        public int Spells => Of(ContentType.Spell) ?? 0;
        public int Equipment => Of(ContentType.Equipment) ?? 0;
        public int Augments => Of(ContentType.Augment) ?? 0;
        public int Biomes => Of(ContentType.Biome) ?? 0;
        public int Modules => Of(ContentType.Module) ?? 0;
        public int Props => Of(ContentType.Prop) ?? 0;
        public int Builds => Of(ContentType.Build) ?? 0;
    }

    /// <summary>The catalogue's "rules" block: global content flags restated as data.</summary>
    public sealed class CatalogueRules
    {
        internal CatalogueRules(JsonValue json)
        {
            var r = new RecordReader(json, "rules");
            EquipmentShared = r.Bool("equipment_shared");
            SpellsExclusive = r.Bool("spells_exclusive");
            AugmentsFree = r.Bool("augments_free");
            AugmentCandidates = r.Int("augment_candidates");
            RerollsPerAugmentCard = r.Int("rerolls_per_augment_card");
            ChoicesPerDraft = r.Int("choices_per_draft");
            WizardSpellCount = r.Int("wizard_spell_count");
            EquippedSpellCount = r.Int("equipped_spell_count");
            MaxAugmentSlots = r.Int("max_augment_slots");
            AugmentMaxRank = r.Int("augment_max_rank");
            r.Finish();
            Raw = json;
        }

        public bool EquipmentShared { get; }
        public bool SpellsExclusive { get; }
        public bool AugmentsFree { get; }
        public int AugmentCandidates { get; }
        public int RerollsPerAugmentCard { get; }
        public int ChoicesPerDraft { get; }
        public int WizardSpellCount { get; }
        public int EquippedSpellCount { get; }
        public int MaxAugmentSlots { get; }
        public int AugmentMaxRank { get; }
        public JsonValue Raw { get; }
    }
}
