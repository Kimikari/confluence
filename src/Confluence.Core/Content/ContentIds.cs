using System;
using System.Collections.Generic;
using System.Globalization;

namespace Confluence.Core.Content
{
    public enum ContentType
    {
        Wizard,
        Spell,
        Equipment,
        Augment,
        Biome,
        Module,
        Prop,
        Build,
    }

    /// <summary>Stable catalogue ID conventions: PREFIX-NNN, contiguous from 001.</summary>
    public static class ContentIds
    {
        public static readonly IReadOnlyList<ContentType> AllTypes = new[]
        {
            ContentType.Wizard, ContentType.Spell, ContentType.Equipment, ContentType.Augment,
            ContentType.Biome, ContentType.Module, ContentType.Prop, ContentType.Build,
        };

        public const int SpellsPerWizard = 8;   // spec L292
        public const int DefaultSpellCount = 6; // spec L292 "Equip six, defaulting to the first six"

        public static string Prefix(ContentType type)
        {
            switch (type)
            {
                case ContentType.Wizard: return "WIZ";
                case ContentType.Spell: return "SPL";
                case ContentType.Equipment: return "EQP";
                case ContentType.Augment: return "AUG";
                case ContentType.Biome: return "BIO";
                case ContentType.Module: return "MOD";
                case ContentType.Prop: return "PROP";
                case ContentType.Build: return "BLD";
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        /// <summary>Key of the record list under the catalogue's "content" and "counts" objects.</summary>
        public static string JsonKey(ContentType type)
        {
            switch (type)
            {
                case ContentType.Wizard: return "wizards";
                case ContentType.Spell: return "spells";
                case ContentType.Equipment: return "equipment";
                case ContentType.Augment: return "augments";
                case ContentType.Biome: return "biomes";
                case ContentType.Module: return "modules";
                case ContentType.Prop: return "props";
                case ContentType.Build: return "builds";
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        /// <summary>
        /// Complete-game record counts: 36 wizards, 288 spells, 120 equipment, 864 augments (spec L294-L296),
        /// 24 biomes, 72 modules, 48 props (L656), 48 worked builds (L691).
        /// </summary>
        public static int ExpectedCount(ContentType type)
        {
            switch (type)
            {
                case ContentType.Wizard: return 36;
                case ContentType.Spell: return 288;
                case ContentType.Equipment: return 120;
                case ContentType.Augment: return 864;
                case ContentType.Biome: return 24;
                case ContentType.Module: return 72;
                case ContentType.Prop: return 48;
                case ContentType.Build: return 48;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        public static string Format(ContentType type, int number)
        {
            return Prefix(type) + "-" + number.ToString("D3", CultureInfo.InvariantCulture);
        }

        /// <summary>Parses a well-formed catalogue ID (prefix, hyphen, at least three digits, number at least 1).</summary>
        public static bool TryParse(string id, out ContentType type, out int number)
        {
            type = default;
            number = 0;
            if (string.IsNullOrEmpty(id)) return false;
            int dash = id.IndexOf('-');
            if (dash <= 0 || dash == id.Length - 1) return false;
            string prefix = id.Substring(0, dash);
            bool found = false;
            foreach (var candidate in AllTypes)
            {
                if (string.Equals(Prefix(candidate), prefix, StringComparison.Ordinal))
                {
                    type = candidate;
                    found = true;
                    break;
                }
            }
            if (!found) return false;
            int digits = id.Length - dash - 1;
            if (digits < 3) return false;
            int value = 0;
            for (int i = dash + 1; i < id.Length; i++)
            {
                char c = id[i];
                if (c < '0' || c > '9') return false;
                if (value > 100000) return false;
                value = value * 10 + (c - '0');
            }
            if (value < 1) return false;
            // Canonical form only: exactly D3 for numbers below 1000, no extra leading zeros.
            if (!string.Equals(Format(type, value), id, StringComparison.Ordinal)) return false;
            number = value;
            return true;
        }

        public static bool IsWellFormed(string id, ContentType type)
        {
            return TryParse(id, out var actual, out _) && actual == type;
        }

        public static int NumberOf(string id)
        {
            if (!TryParse(id, out _, out int number)) throw new FormatException("'" + id + "' is not a catalogue ID.");
            return number;
        }

        /// <summary>WIZ-n owns SPL-(8n-7)..SPL-8n (spec L294).</summary>
        public static string OwnerOfSpell(string spellId)
        {
            if (!TryParse(spellId, out var type, out int n) || type != ContentType.Spell) return null;
            return Format(ContentType.Wizard, (n + SpellsPerWizard - 1) / SpellsPerWizard);
        }

        /// <summary>The eight spell IDs a wizard owns, in order (spec L294).</summary>
        public static IReadOnlyList<string> SpellIdsOwnedBy(string wizardId)
        {
            if (!TryParse(wizardId, out var type, out int n) || type != ContentType.Wizard)
                throw new FormatException("'" + wizardId + "' is not a wizard ID.");
            var ids = new string[SpellsPerWizard];
            for (int i = 0; i < SpellsPerWizard; i++) ids[i] = Format(ContentType.Spell, SpellsPerWizard * n - (SpellsPerWizard - 1) + i);
            return ids;
        }
    }
}
