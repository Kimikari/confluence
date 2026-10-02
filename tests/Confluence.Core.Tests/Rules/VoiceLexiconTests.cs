using System;
using System.Linq;
using Confluence.Core.Rules;

namespace Confluence.Core.Tests.Rules
{
    public class VoiceLexiconTests
    {
        [Fact]
        public void Lexicon_And_Phrases_Match_The_Spec()
        {
            // Spec L486-L488: eight syllables; Oren's eight phrases in local spell-index order; 0.75 s cadence.
            Assert.Equal("WIZ-004", VoiceLexicon.WizardId);
            Assert.Equal(0.75f, VoiceLexicon.CadenceSec);
            Assert.Equal(new[] { "ka", "lo", "mi", "na", "pe", "ru", "se", "ti" }, VoiceLexicon.Syllables);
            string[] phrases = { "ka-lo-mi", "na-pe-ru", "se-ti-ka", "lo-mi-na", "pe-ru-se", "ti-ka-lo", "mi-na-pe", "ru-se-ti" };
            Assert.Equal(phrases, Enumerable.Range(0, VoiceLexicon.PhraseCount).Select(VoiceLexicon.PhraseText));
            Assert.Throws<ArgumentOutOfRangeException>(() => VoiceLexicon.Phrase(8));
            Assert.Throws<ArgumentOutOfRangeException>(() => VoiceLexicon.Phrase(-1));
        }

        [Fact]
        public void Phrases_Are_Distinct_Three_Syllable_Sequences_From_The_Lexicon()
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < VoiceLexicon.PhraseCount; i++)
            {
                var phrase = VoiceLexicon.Phrase(i);
                Assert.Equal(VoiceLexicon.SyllablesPerPhrase, phrase.Count);
                Assert.All(phrase, s => Assert.True(VoiceLexicon.IsSyllable(s), s));
                Assert.True(seen.Add(VoiceLexicon.PhraseText(i)));
            }

            // One phrase per WIZ-004 kit spell (SPL-025..SPL-032).
            Assert.Equal(TestCatalogueSpellCount(), VoiceLexicon.PhraseCount);
            Assert.False(VoiceLexicon.IsSyllable("KA"));
        }

        private static int TestCatalogueSpellCount() => Confluence.Core.Tests.Content.TestCatalogue.Real.Wizard(VoiceLexicon.WizardId).SpellIds.Count;
    }
}
