using System;
using Confluence.Core.Content;
using Confluence.Sim.Content;
using Confluence.Sim.Spells;

namespace Confluence.Sim.Tests.Content.Wiz002
{
    /// <summary>
    /// D-011 for the sixteen WIZ-002 transformations: every prose-only parameter quotes its augment record (rank 1, rank
    /// 2, drawback or counterplay) and the quote contains the number the behavior uses.
    /// </summary>
    public class TransformProseTests
    {
        [Theory]
        [InlineData("AUG-305")]
        [InlineData("AUG-306")]
        [InlineData("AUG-307")]
        [InlineData("AUG-308")]
        [InlineData("AUG-309")]
        [InlineData("AUG-310")]
        [InlineData("AUG-311")]
        [InlineData("AUG-312")]
        [InlineData("AUG-313")]
        [InlineData("AUG-314")]
        [InlineData("AUG-315")]
        [InlineData("AUG-316")]
        [InlineData("AUG-317")]
        [InlineData("AUG-318")]
        [InlineData("AUG-319")]
        [InlineData("AUG-320")]
        [Verifies("AUG-305")]
        [Verifies("AUG-306")]
        [Verifies("AUG-307")]
        [Verifies("AUG-308")]
        [Verifies("AUG-309")]
        [Verifies("AUG-310")]
        [Verifies("AUG-311")]
        [Verifies("AUG-312")]
        [Verifies("AUG-313")]
        [Verifies("AUG-314")]
        [Verifies("AUG-315")]
        [Verifies("AUG-316")]
        [Verifies("AUG-317")]
        [Verifies("AUG-318")]
        [Verifies("AUG-319")]
        [Verifies("AUG-320")]
        public void Each_Prose_Parameter_Quotes_The_Augment_Record_And_Contains_Its_Number(string id)
        {
            Assert.True(ContentRegistry.Default.TryGetTransform(id, out ISpellTransform behavior), id + " has no registered transformation");
            AugmentDef def = BramKit.Catalogue.Augment(id);
            Assert.Contains("WIZ-002", def.EligibleWizards);
            string text = string.Join(" | ", def.Rank1, def.Rank2, def.Drawback, def.Counterplay);
            Assert.NotEmpty(behavior.Parameters);
            foreach (ProseParameter p in behavior.Parameters)
            {
                Assert.True(text.Contains(p.Quote, StringComparison.Ordinal), id + ": \"" + p.Quote + "\" is not in the record");
                Assert.True(p.Quote.Contains(p.WrittenText, StringComparison.Ordinal), id + ": \"" + p.Quote + "\" does not contain " + p.WrittenText);
            }
        }
    }
}
