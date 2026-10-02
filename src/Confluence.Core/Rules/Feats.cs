using System;
using System.Collections.Generic;

namespace Confluence.Core.Rules
{
    /// <summary>
    /// A server-verified condition met within one completed duel (spec §5 "Completed duel feat definitions").
    /// The typed thresholds are the numbers of the description; the feat tracker reads them instead of copying
    /// literals. A parameter that the feat does not use is 0 (false for <see cref="RequiresWin"/>).
    /// </summary>
    public sealed class FeatDef
    {
        public FeatDef(string id, string description, int count = 0, float amount = 0f, float windowSec = 0f,
            float minRangeMeters = 0f, float healthFractionBelow = 0f, bool requiresWin = false)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Description = description ?? throw new ArgumentNullException(nameof(description));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (!(amount >= 0f) || !(windowSec >= 0f) || !(minRangeMeters >= 0f)) throw new ArgumentOutOfRangeException(nameof(amount));
            if (!(healthFractionBelow >= 0f && healthFractionBelow <= 1f)) throw new ArgumentOutOfRangeException(nameof(healthFractionBelow));
            Count = count;
            Amount = amount;
            WindowSec = windowSec;
            MinRangeMeters = minRangeMeters;
            HealthFractionBelow = healthFractionBelow;
            RequiresWin = requiresWin;
        }

        public string Id { get; }

        /// <summary>Display text, exactly as in the spec table.</summary>
        public string Description { get; }

        /// <summary>Required number of qualifying events, distinct spells/reactions, or damage channels.</summary>
        public int Count { get; }

        /// <summary>Required accumulated amount: damage, mana, health, shield absorption, or meters traveled.</summary>
        public float Amount { get; }

        /// <summary>Each qualifying event must follow its trigger within this many seconds.</summary>
        public float WindowSec { get; }

        /// <summary>Each qualifying hit must land from at least this distance.</summary>
        public float MinRangeMeters { get; }

        /// <summary>The player must fall below this fraction of maximum health during the duel.</summary>
        public float HealthFractionBelow { get; }

        /// <summary>The player must also win the duel.</summary>
        public bool RequiresWin { get; }

        public override string ToString() => Id + " " + Description;
    }

    /// <summary>
    /// FEAT-001..FEAT-024 are rule data from the design table (spec L253-L278), not catalogue records.
    /// A feat leaf counts completed duels in this match meeting the definition; training feats never count (spec L280).
    /// </summary>
    public static class Feats
    {
        public const int Count = 24;

        public static readonly IReadOnlyList<FeatDef> All = Array.AsReadOnly(new[]
        {
            new FeatDef("FEAT-001", "Deal at least 100 damage to the opponent", amount: 100f),                        // spec L255
            new FeatDef("FEAT-002", "Land three different exclusive spells", count: 3),                                 // spec L256
            new FeatDef("FEAT-003", "Perform two successful perfect guards", count: 2),                                 // spec L257
            new FeatDef("FEAT-004", "Land three common melee attacks", count: 3),                                       // spec L258
            new FeatDef("FEAT-005", "Travel 60 meters of valid combat movement", amount: 60f),                          // spec L259
            new FeatDef("FEAT-006", "Produce three distinct world reactions", count: 3),                                // spec L260
            new FeatDef("FEAT-007", "Break two destructible covers", count: 2),                                         // spec L261
            new FeatDef("FEAT-008", "Land two hits within 1 second after a dodge", count: 2, windowSec: 1f),            // spec L262
            new FeatDef("FEAT-009", "Land three direct hits from at least 15 meters", count: 3, minRangeMeters: 15f),   // spec L263
            new FeatDef("FEAT-010", "Survive below 25 percent health and win", healthFractionBelow: 0.25f, requiresWin: true), // spec L264
            new FeatDef("FEAT-011", "Clear two hostile or neutral clouds", count: 2),                                   // spec L265
            new FeatDef("FEAT-012", "Deal at least 30 damage in each of two damage channels", count: 2, amount: 30f),  // spec L266
            new FeatDef("FEAT-013", "Spend at least 120 mana through legal commitments", amount: 120f),                 // spec L267
            new FeatDef("FEAT-014", "Complete five valid mastery casts", count: 5),                                     // spec L268
            new FeatDef("FEAT-015", "Destroy two hostile summons", count: 2),                                           // spec L269
            new FeatDef("FEAT-016", "Win without using a consumable", requiresWin: true),                               // spec L270: zero consumable uses
            new FeatDef("FEAT-017", "Absorb at least 60 damage with shields", amount: 60f),                             // spec L271
            new FeatDef("FEAT-018", "Restore at least 40 health within the shared healing budget", amount: 40f),        // spec L272
            new FeatDef("FEAT-019", "Interrupt one committed hostile cast", count: 1),                                  // spec L273
            new FeatDef("FEAT-020", "Deal 30 damage through bounded conductivity reactions", amount: 30f),              // spec L274
            new FeatDef("FEAT-021", "Hit the opponent with one moved prop", count: 1),                                  // spec L275
            new FeatDef("FEAT-022", "Land three spell then weapon combinations within 1 second", count: 3, windowSec: 1f), // spec L276
            new FeatDef("FEAT-023", "Remove two hostile statuses", count: 2),                                           // spec L277
            new FeatDef("FEAT-024", "Return inside the closing boundary twice after taking its damage", count: 2),      // spec L278
        });

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
