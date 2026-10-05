using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Economy;
using Confluence.Economy.Augments;
using Confluence.Economy.Eligibility;
using Confluence.Economy.Loadouts;
using Confluence.Sim.Content;
using Confluence.Sim.Content.Wizards.Wiz002;
using Confluence.Sim.Spells;

namespace Confluence.Hosting.Tests.Wiz002
{
    /// <summary>
    /// Economy's eligibility profile of WIZ-002's sixteen transformations with the shipped content registry populated
    /// (spec L243 two-profile rule, L543, L637; DECISIONS D-012): each transformation is acquired through the production
    /// acquisition path, the dormancy resolver builds the profile from Core's <see cref="StructuralDeltas"/>, and the
    /// Sim's resolved spell form carries the same structural tags.
    /// </summary>
    public sealed class Wiz002StructuralProfileTests
    {
        private static readonly RoundInfo Round = new RoundInfo(MatchMode.Conclave, 3);

        // Expected structural changes (Wiz002StructuralDeltas): the twelve others keep their base tags.
        public static IEnumerable<object[]> Transformations()
        {
            yield return new object[] { "AUG-305", "SPL-009", "", "" };
            yield return new object[] { "AUG-306", "SPL-009", "", "" };
            yield return new object[] { "AUG-307", "SPL-010", "field", "" };
            yield return new object[] { "AUG-308", "SPL-010", "", "" };
            yield return new object[] { "AUG-309", "SPL-011", "", "" };
            yield return new object[] { "AUG-310", "SPL-011", "cone", "" };
            yield return new object[] { "AUG-311", "SPL-012", "", "" };
            yield return new object[] { "AUG-312", "SPL-012", "", "" };
            yield return new object[] { "AUG-313", "SPL-013", "", "" };
            yield return new object[] { "AUG-314", "SPL-013", "", "" };
            yield return new object[] { "AUG-315", "SPL-014", "", "" };
            yield return new object[] { "AUG-316", "SPL-014", "trap,field", "" };
            yield return new object[] { "AUG-317", "SPL-015", "", "burn" };
            yield return new object[] { "AUG-318", "SPL-015", "", "" };
            yield return new object[] { "AUG-319", "SPL-016", "", "" };
            yield return new object[] { "AUG-320", "SPL-016", "", "" };
        }

        private static Catalogue Cat => HostFixture.Catalogue;

        private static Loadout Bram(string target)
        {
            var l = new Loadout(Cat, "WIZ-002");
            if (!l.EquippedSpellIds.Contains(target))
            {
                // SPL-015 and SPL-016 are not in the default six: swap the target in for SPL-011.
                var spells = l.EquippedSpellIds.ToList();
                spells[spells.IndexOf("SPL-011")] = target;
                Assert.Equal(TransactionCode.Success, l.SetEquippedSpells(spells));
            }

            return l;
        }

        private static string[] Split(string csv) => csv.Length == 0 ? new string[0] : csv.Split(',');

        [Theory]
        [MemberData(nameof(Transformations))]
        [Verifies("AUG-305", "AUG-306", "AUG-307", "AUG-308", "AUG-309", "AUG-310", "AUG-311", "AUG-312")]
        [Verifies("AUG-313", "AUG-314", "AUG-315", "AUG-316", "AUG-317", "AUG-318", "AUG-319", "AUG-320")]
        public void Economy_Sees_The_Structural_Profile_Of_Each_Transformation(string augmentId, string target, string adds, string removes)
        {
            Assert.True(ContentRegistry.Default.TryGetTransform(augmentId, out ISpellTransform transform)); // populates the deltas
            AugmentDef def = Cat.Augment(augmentId);
            Assert.True(def.IsTransformation);
            Assert.Equal(target, def.TargetSpellId);

            Loadout l = Bram(target);
            AcquisitionResult granted = AugmentAcquisition.AcquireNew(l, def, Round, null, null, StructuralChangeSources.Default);
            Assert.True(granted.Succeeded, augmentId + ": " + granted.Code);
            DormancyReport report = DormancyResolver.Resolve(l, StructuralChangeSources.Default);
            Assert.True(report.IsActive(augmentId));

            SpellProfile profile = report.EligibilityProfile.For(target);
            var expected = new SortedSet<string>(Cat.Spell(target).Tags, StringComparer.Ordinal);
            foreach (string t in Split(adds)) expected.Add(t);
            foreach (string t in Split(removes)) expected.Remove(t);
            Assert.Equal(expected, new SortedSet<string>(profile.Tags, StringComparer.Ordinal));
            if (adds.Length + removes.Length > 0)
            {
                Assert.Equal(new[] { augmentId }, profile.AppliedChangeIds);
                Assert.True(StructuralDeltas.TryGet(augmentId, out StructuralDelta delta));
                Assert.Equal(target, delta.TargetSpellId);
            }
            else
            {
                Assert.Empty(profile.AppliedChangeIds);
                Assert.False(StructuralDeltas.TryGet(augmentId, out _));
            }

            // School affinity remains and other equipped spells are untouched (spec L243).
            Assert.True(profile.HasTag("school:heat"));
            foreach (SpellProfile other in report.EligibilityProfile.Spells.Where(s => s.SpellId != target))
            {
                Assert.Equal(Cat.Spell(other.SpellId).Tags.OrderBy(t => t, StringComparer.Ordinal), other.Tags.OrderBy(t => t, StringComparer.Ordinal));
            }

            // The Sim resolves the same structural tags for the form the duel runs (the delta replaces the tags first).
            Assert.True(ContentRegistry.Default.TryGetSpell(target, out ISpellBehavior behavior));
            for (int rank = 1; rank <= 2; rank++)
            {
                SpellForm form = SpellFormResolver.Resolve(Cat.Spell(target), behavior,
                    new[] { new ActiveTransform(augmentId, rank, transform, def.ExclusiveGroup) });
                foreach (string t in Split(adds)) Assert.True(form.HasTag(t), augmentId + " form lacks " + t);
                foreach (string t in Split(removes)) Assert.False(form.HasTag(t), augmentId + " form keeps " + t);
                foreach (string t in profile.Tags) Assert.True(form.HasTag(t), augmentId + " form lacks profile tag " + t);
            }
        }

        [Fact]
        [Verifies("AUG-307", "AUG-316")]
        public void A_Transformation_That_Adds_A_Field_Raises_The_Equipped_Field_Count()
        {
            // 009, 010, 011, 012, 014, 015: Ash Curtain is the only field spell until Sweep Wake or Boiler Mine.
            string[] six = { "SPL-009", "SPL-010", "SPL-011", "SPL-012", "SPL-014", "SPL-015" };
            int Fields(Loadout l) => DormancyResolver.Resolve(l, StructuralChangeSources.Default).EligibilityProfile.Spells.Count(s => s.HasTag("field"));
            Assert.True(ContentRegistry.Default.TryGetTransform("AUG-307", out _));

            var plain = new Loadout(Cat, "WIZ-002");
            Assert.Equal(TransactionCode.Success, plain.SetEquippedSpells(six));
            Assert.Equal(1, Fields(plain));

            var wake = new Loadout(Cat, "WIZ-002");
            Assert.Equal(TransactionCode.Success, wake.SetEquippedSpells(six));
            Assert.True(AugmentAcquisition.AcquireNew(wake, Cat.Augment("AUG-307"), Round, null, null, StructuralChangeSources.Default).Succeeded);
            Assert.Equal(2, Fields(wake));
            Assert.True(AugmentAcquisition.AcquireNew(wake, Cat.Augment("AUG-316"), Round, null, null, StructuralChangeSources.Default).Succeeded);
            Assert.Equal(3, Fields(wake));
            Assert.True(DormancyResolver.Resolve(wake, StructuralChangeSources.Default).EligibilityProfile.For("SPL-014").HasTag("trap"));

            // Unequipping the target makes the transformation dormant and its delta disappears from the profile (spec L543).
            Assert.Equal(TransactionCode.Success, wake.SetEquippedSpells(new[] { "SPL-009", "SPL-011", "SPL-012", "SPL-013", "SPL-014", "SPL-015" }));
            DormancyReport report = DormancyResolver.Resolve(wake, StructuralChangeSources.Default);
            Assert.Null(report.EligibilityProfile.For("SPL-010"));
            Assert.Equal(3, report.EligibilityProfile.Spells.Count(s => s.HasTag("field"))); // Ash Curtain, Phoenix Rush, Boiler Mine
        }

        [Theory]
        [InlineData("AUG-305", "AUG-306")]
        [InlineData("AUG-307", "AUG-308")]
        [InlineData("AUG-315", "AUG-316")]
        [Verifies("AUG-305", "AUG-306", "AUG-307", "AUG-308", "AUG-315", "AUG-316")]
        public void Economy_Refuses_A_Second_Transformation_Of_The_Same_Exclusive_Group(string first, string second)
        {
            AugmentDef a = Cat.Augment(first);
            AugmentDef b = Cat.Augment(second);
            Assert.Equal(a.ExclusiveGroup, b.ExclusiveGroup);
            Loadout l = Bram(a.TargetSpellId);
            Assert.True(AugmentAcquisition.AcquireNew(l, a, Round, null, null, StructuralChangeSources.Default).Succeeded);
            AcquisitionResult refused = AugmentAcquisition.AcquireNew(l, b, Round, null, null, StructuralChangeSources.Default);
            Assert.False(refused.Succeeded);
        }

        [Theory]
        [InlineData("AUG-309", "AUG-310")]
        [InlineData("AUG-311", "AUG-312")]
        [InlineData("AUG-313", "AUG-314")]
        [InlineData("AUG-317", "AUG-318")]
        [InlineData("AUG-319", "AUG-320")]
        [Verifies("AUG-309", "AUG-310", "AUG-311", "AUG-312", "AUG-313", "AUG-314", "AUG-317", "AUG-318", "AUG-319", "AUG-320")]
        public void Ungrouped_Transformations_Of_One_Spell_Can_Be_Owned_Together(string first, string second)
        {
            AugmentDef a = Cat.Augment(first);
            AugmentDef b = Cat.Augment(second);
            Assert.False(a.HasExclusiveGroup || b.HasExclusiveGroup);
            Loadout l = Bram(a.TargetSpellId);
            Assert.True(AugmentAcquisition.AcquireNew(l, a, Round, null, null, StructuralChangeSources.Default).Succeeded);
            Assert.True(AugmentAcquisition.AcquireNew(l, b, Round, null, null, StructuralChangeSources.Default).Succeeded);
            DormancyReport report = DormancyResolver.Resolve(l, StructuralChangeSources.Default);
            Assert.True(report.IsActive(first) && report.IsActive(second));
        }

        [Fact]
        public void Every_WIZ_002_Transformation_Has_A_Registered_Behavior()
        {
            List<AugmentDef> transformations = Cat.Augments.Where(x => x.IsTransformation &&
                Cat.Spell(x.TargetSpellId).DeclaredWizardId == "WIZ-002").ToList();
            Assert.Equal(16, transformations.Count);
            foreach (AugmentDef def in transformations)
            {
                Assert.True(ContentRegistry.Default.TryGetTransform(def.Id, out _), def.Id);
            }

            // Exactly the three delivery groups of the catalogue (SPL-009, SPL-010, SPL-014).
            Assert.Equal(new[] { "SPL-009-delivery", "SPL-010-delivery", "SPL-014-delivery" },
                transformations.Where(t => t.HasExclusiveGroup).Select(t => t.ExclusiveGroup).Distinct().OrderBy(g => g, StringComparer.Ordinal));
            Wiz002StructuralDeltas.EnsureRegistered();
        }
    }
}
