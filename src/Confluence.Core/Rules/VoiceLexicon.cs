using System;
using System.Collections.Generic;

namespace Confluence.Core.Rules
{
    /// <summary>
    /// Oren Vey's (WIZ-004) fixed voice-casting lexicon (spec L486-L488). It is rule data, not a catalogue
    /// field: recognition only distinguishes these eight syllables, and the equivalent rhythm route uses the same
    /// three-beat cadence. Beat tolerances live in the WIZ-004 record and its grammar.
    /// </summary>
    public static class VoiceLexicon
    {
        public const string WizardId = "WIZ-004";
        public const int SyllablesPerPhrase = 3;  // spec L486
        public const float CadenceSec = 0.75f;    // spec L486

        /// <summary>The eight fictional syllables (spec L488).</summary>
        public static readonly IReadOnlyList<string> Syllables = Array.AsReadOnly(new[] { "ka", "lo", "mi", "na", "pe", "ru", "se", "ti" });

        // Phrases in local spell-index order: index 0 is the wizard's first spell (spec L488).
        private static readonly string[][] PhraseTable =
        {
            new[] { "ka", "lo", "mi" },
            new[] { "na", "pe", "ru" },
            new[] { "se", "ti", "ka" },
            new[] { "lo", "mi", "na" },
            new[] { "pe", "ru", "se" },
            new[] { "ti", "ka", "lo" },
            new[] { "mi", "na", "pe" },
            new[] { "ru", "se", "ti" },
        };

        public static int PhraseCount => PhraseTable.Length;

        /// <summary>The three syllables of the phrase for a local spell index 0-7.</summary>
        public static IReadOnlyList<string> Phrase(int localSpellIndex)
        {
            if (localSpellIndex < 0 || localSpellIndex >= PhraseTable.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(localSpellIndex), localSpellIndex, "Oren has eight phrases (0-7).");
            }

            return Array.AsReadOnly(PhraseTable[localSpellIndex]);
        }

        /// <summary>Display form of a phrase, for example "ka-lo-mi".</summary>
        public static string PhraseText(int localSpellIndex) => string.Join("-", Phrase(localSpellIndex));

        public static bool IsSyllable(string syllable)
        {
            for (int i = 0; i < Syllables.Count; i++)
            {
                if (string.Equals(Syllables[i], syllable, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
