using System;
using System.Collections.Generic;

namespace Confluence.Core.Rules
{
    /// <summary>A server-verified condition met within one completed duel (spec §5 "Completed duel feat definitions").</summary>
    public sealed class FeatDef
    {
        public FeatDef(string id, string description)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Description = description ?? throw new ArgumentNullException(nameof(description));
        }

        public string Id { get; }
        public string Description { get; }

        public override string ToString() => Id + " " + Description;
    }

    /// <summary>
    /// FEAT-001..FEAT-024 are rule data from the design table (spec L253-L278), not catalogue records.
    /// A feat leaf counts completed duels in this match meeting the definition; training feats never count (spec L280).
    /// </summary>
    public static class Feats
    {
        public const int Count = 24;

        public static readonly IReadOnlyList<FeatDef> All = new[]
        {
            new FeatDef("FEAT-001", "Deal at least 100 damage to the opponent"),
            new FeatDef("FEAT-002", "Land three different exclusive spells"),
            new FeatDef("FEAT-003", "Perform two successful perfect guards"),
            new FeatDef("FEAT-004", "Land three common melee attacks"),
            new FeatDef("FEAT-005", "Travel 60 meters of valid combat movement"),
            new FeatDef("FEAT-006", "Produce three distinct world reactions"),
            new FeatDef("FEAT-007", "Break two destructible covers"),
            new FeatDef("FEAT-008", "Land two hits within 1 second after a dodge"),
            new FeatDef("FEAT-009", "Land three direct hits from at least 15 meters"),
            new FeatDef("FEAT-010", "Survive below 25 percent health and win"),
            new FeatDef("FEAT-011", "Clear two hostile or neutral clouds"),
            new FeatDef("FEAT-012", "Deal at least 30 damage in each of two damage channels"),
            new FeatDef("FEAT-013", "Spend at least 120 mana through legal commitments"),
            new FeatDef("FEAT-014", "Complete five valid mastery casts"),
            new FeatDef("FEAT-015", "Destroy two hostile summons"),
            new FeatDef("FEAT-016", "Win without using a consumable"),
            new FeatDef("FEAT-017", "Absorb at least 60 damage with shields"),
            new FeatDef("FEAT-018", "Restore at least 40 health within the shared healing budget"),
            new FeatDef("FEAT-019", "Interrupt one committed hostile cast"),
            new FeatDef("FEAT-020", "Deal 30 damage through bounded conductivity reactions"),
            new FeatDef("FEAT-021", "Hit the opponent with one moved prop"),
            new FeatDef("FEAT-022", "Land three spell then weapon combinations within 1 second"),
            new FeatDef("FEAT-023", "Remove two hostile statuses"),
            new FeatDef("FEAT-024", "Return inside the closing boundary twice after taking its damage"),
        };

        private static readonly Dictionary<string, FeatDef> ById = BuildIndex();

        private static Dictionary<string, FeatDef> BuildIndex()
        {
            var index = new Dictionary<string, FeatDef>(StringComparer.Ordinal);
            foreach (var feat in All) index.Add(feat.Id, feat);
            return index;
        }

        public static bool Exists(string id) => id != null && ById.ContainsKey(id);

        public static bool TryGet(string id, out FeatDef feat)
        {
            if (id == null)
            {
                feat = null;
                return false;
            }
            return ById.TryGetValue(id, out feat);
        }

        public static FeatDef Get(string id)
        {
            if (!TryGet(id, out var feat)) throw new KeyNotFoundException("Unknown feat ID '" + id + "' (valid: FEAT-001..FEAT-024).");
            return feat;
        }
    }
}
