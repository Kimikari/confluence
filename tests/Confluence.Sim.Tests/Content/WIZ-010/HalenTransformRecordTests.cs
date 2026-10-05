using System;
using System.Linq;
using Confluence.Core.Content;
using Confluence.Sim.Content;
using Confluence.Sim.Content.Wizards.HalenTide;
using Confluence.Sim.Spells;

namespace Confluence.Sim.Tests.Content.Halen
{
    /// <summary>
    /// Record-level checks of WIZ-010's sixteen transformations: each is registered, targets its record's spell, quotes its
    /// prose numbers from the record text (D-011), and declares its structural delta exactly as Core's registry holds it.
    /// </summary>
    public class HalenTransformRecordTests
    {
        public static readonly TheoryData<string> Ids = new TheoryData<string>
        {
            "AUG-433", "AUG-434", "AUG-435", "AUG-436", "AUG-437", "AUG-438", "AUG-439", "AUG-440",
            "AUG-441", "AUG-442", "AUG-443", "AUG-444", "AUG-445", "AUG-446", "AUG-447", "AUG-448",
        };

        [Theory]
        [MemberData(nameof(Ids))]
        [Verifies("AUG-433", "AUG-434", "AUG-435", "AUG-436", "AUG-437", "AUG-438", "AUG-439", "AUG-440",
            "AUG-441", "AUG-442", "AUG-443", "AUG-444", "AUG-445", "AUG-446", "AUG-447", "AUG-448")]
        public void Each_Transformation_Is_Registered_And_Quotes_Its_Record(string id)
        {
            AugmentDef record = HalenTestKit.Catalogue.Augment(id);
            Assert.Equal(new[] { HalenTestKit.Wizard }, record.EligibleWizards.ToArray());
            Assert.True(ContentRegistry.Default.TryGetTransform(id, out ISpellTransform transform), id + " is not registered");
            Assert.Equal("Confluence.Sim.Content.Wizards.HalenTide", transform.GetType().Namespace);
            string text = string.Join(" | ", record.Rank1, record.Rank2, record.Drawback, record.Counterplay);
            Assert.NotEmpty(transform.Parameters);
            foreach (ProseParameter p in transform.Parameters)
            {
                Assert.True(text.Contains(p.Quote, StringComparison.Ordinal), id + ": \"" + p.Quote + "\" is not in the record");
                Assert.True(p.Quote.Contains(p.WrittenText, StringComparison.Ordinal), id + ": \"" + p.Quote + "\" does not contain " + p.WrittenText);
            }

            // The form resolves on its target spell, at both ranks, with every structural delta it declares.
            string spell = record.TargetSpellId;
            foreach (int rank in new[] { 1, 2 })
            {
                SpellForm form = HalenTestKit.Form(spell, HalenTestKit.Aug(id, rank));
                Assert.Equal(spell, form.SpellId);
                var row = HalenStructure.Table.SingleOrDefault(t => t.Item1 == id);
                if (row == null)
                {
                    Assert.False(StructuralDeltas.TryGet(id, out _), id + " declares no structure change");
                    continue;
                }

                Assert.True(StructuralDeltas.TryGet(id, out var delta));
                Assert.Equal(spell, delta.TargetSpellId);
                Assert.Equal(row.Item3, delta.AddTags.ToArray());
                Assert.Equal(row.Item4, delta.RemoveTags.ToArray());
                foreach (string tag in row.Item3) Assert.True(form.HasTag(tag), id + " should add " + tag);
                foreach (string tag in row.Item4) Assert.False(form.HasTag(tag), id + " should remove " + tag);
            }
        }

        [Fact]
        [Verifies("AUG-435", "AUG-436", "AUG-439", "AUG-440")]
        public void The_Exclusive_Groups_Of_The_Kit_Are_Enforced()
        {
            foreach (var group in HalenTestKit.Catalogue.Augments
                         .Where(a => a.HasExclusiveGroup && a.EligibleWizards.Contains(HalenTestKit.Wizard))
                         .GroupBy(a => a.ExclusiveGroup))
            {
                var members = group.ToList();
                if (members.Count < 2) continue;
                Assert.ThrowsAny<InvalidOperationException>(() =>
                    HalenTestKit.Form(members[0].TargetSpellId, HalenTestKit.Aug(members[0].Id), HalenTestKit.Aug(members[1].Id)));
            }
        }
    }
}
