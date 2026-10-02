using System.Linq;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Economy.Augments;
using Confluence.Economy.Eligibility;
using Confluence.Economy.Loadouts;
using static Confluence.Economy.Tests.TestData;

namespace Confluence.Economy.Tests
{
    /// <summary>The two-profile rule (spec L243, L543, L637, L713).</summary>
    public class StructuralProfileTests
    {
        // Sunmark (SPL-006, WIZ-001's only projectile in the default six) transformed into a trap, as Stage 5 might declare.
        private static StructuralChangeTable SunmarkTrap() =>
            new StructuralChangeTable().AddTransformation("AUG-299", "SPL-006", new[] { "trap", "field" }, new[] { "projectile" });

        [Fact]
        public void ProfileStartsFromTheBaseKitTagsOfTheSixEquippedSpells()
        {
            var l = Loadout("WIZ-002");
            var profile = StructuralProfile.Build(l, null, NoChanges);
            Assert.Equal(l.EquippedSpellIds, profile.Spells.Select(s => s.SpellId));
            Assert.Equal(Cat.Spell("SPL-009").Tags, profile.For("SPL-009").Tags);
            Assert.Null(profile.For("SPL-015")); // owned but not equipped
        }

        [Fact]
        [Verifies("AUG-299", "AUG-001")]
        public void AnEnabledTransformationReshapesItsTargetSpell()
        {
            var changes = SunmarkTrap();
            var l = Loadout("WIZ-001");
            Grant(l, "AUG-001", Conclave1, null, changes);
            Grant(l, "AUG-299", Conclave3, null, changes);
            var report = DormancyResolver.Resolve(l, changes);
            var sunmark = report.EligibilityProfile.For("SPL-006");
            Assert.False(sunmark.HasTag("projectile"));
            Assert.True(sunmark.HasTag("trap"));
            Assert.True(sunmark.HasTag("field"));
            Assert.True(sunmark.HasTag("school:light"));            // school affinity remains (spec L243)
            Assert.Equal(new[] { "AUG-299" }, sunmark.AppliedChangeIds);
            // "A transformed projectile trap no longer qualifies as a projectile" (spec L713).
            Assert.False(report.IsActive("AUG-001"));
            Assert.True(report.IsActive("AUG-299"));
            Assert.False(new CandidateJudge(l, Conclave5, null, changes).IsEligible(Cat.Augment("AUG-007")));
        }

        [Fact]
        public void ATransformationOfAnUnequippedSpellChangesNothing()
        {
            var changes = SunmarkTrap();
            var l = Loadout("WIZ-001");
            l.SetEquippedSpells(new[] { "SPL-001", "SPL-002", "SPL-003", "SPL-004", "SPL-005", "SPL-008" });
            Grant(l, "AUG-299", Conclave1, null, changes);
            var report = DormancyResolver.Resolve(l, changes);
            Assert.True(report.EligibilityProfile.For("SPL-008").HasTag("projectile"));
            Assert.All(report.EligibilityProfile.Spells, s => Assert.Empty(s.AppliedChangeIds));
        }

        [Fact]
        [Verifies("AUG-001")]
        public void ASharedAugmentCannotQualifyItself()
        {
            // Suppose AUG-001's effect were declared as adding 'projectile' to Prism Ray: the eligibility profile ignores
            // shared augment effects, so AUG-001 cannot create its own prerequisite (spec L243 two-profile rule).
            var changes = new StructuralChangeTable().AddTransformation("AUG-001", "SPL-001", new[] { "projectile" }, null);
            var l = Loadout("WIZ-001");
            l.SetEquippedSpells(new[] { "SPL-001", "SPL-002", "SPL-003", "SPL-004", "SPL-005", "SPL-007" }); // no projectile
            Assert.False(new CandidateJudge(l, Conclave1, null, changes).IsEligible(Cat.Augment("AUG-001")));

            // Owned while a projectile was equipped, it goes dormant when the projectile leaves; its own effect cannot keep it.
            var owned = Loadout("WIZ-001");
            Grant(owned, "AUG-001", Conclave1, null, changes);
            owned.SetEquippedSpells(new[] { "SPL-001", "SPL-002", "SPL-003", "SPL-004", "SPL-005", "SPL-007" });
            var report = DormancyResolver.Resolve(owned, changes);
            Assert.False(report.IsActive("AUG-001"));
            Assert.Empty(report.EligibilityProfile.For("SPL-001").AppliedChangeIds);
        }

        [Fact]
        public void ADormantTransformationDoesNotReshapeTheProfile()
        {
            var l = Loadout("WIZ-001");
            var profile = StructuralProfile.Build(l, new[] { Cat.Augment("AUG-001") }, SunmarkTrap()); // shared defs are ignored
            Assert.True(profile.For("SPL-006").HasTag("projectile"));
            var reshaped = StructuralProfile.Build(l, new[] { Cat.Augment("AUG-299") }, SunmarkTrap());
            Assert.False(reshaped.For("SPL-006").HasTag("projectile"));
        }

        [Fact]
        public void ChosenEquipmentStructuralChangesApplyOnlyWhileLive()
        {
            var changes = new StructuralChangeTable().AddEquipment("EQP-007", "projectile", new[] { "split" }, null);
            var l = Loadout("WIZ-001");
            var hazel = Give(l, "EQP-007");
            Assert.True(StructuralProfile.Build(l, null, changes).For("SPL-006").HasTag("split"));
            Assert.False(StructuralProfile.Build(l, null, changes).For("SPL-001").HasTag("split")); // requires projectile
            l.SetEnabledActive(null); // a disabled active has no live effect
            Assert.False(StructuralProfile.Build(l, null, changes).For("SPL-006").HasTag("split"));
            l.SetEnabledActive(hazel.InstanceId);
            // Application order: transformation first, then equipment (spec L286).
            var both = new StructuralChangeTable()
                .AddTransformation("AUG-299", "SPL-006", new[] { "trap" }, new[] { "projectile" })
                .AddEquipment("EQP-007", "projectile", new[] { "split" }, null);
            var profile = StructuralProfile.Build(l, new[] { Cat.Augment("AUG-299") }, both).For("SPL-006");
            Assert.False(profile.HasTag("split"));
            Assert.Equal(new[] { "AUG-299" }, profile.AppliedChangeIds);
        }

        [Fact]
        public void TheDefaultSourceReadsTheCoreStructuralDeltaRegistry()
        {
            // A structurally neutral registration (light is already a Sunmark tag) so no other test is affected.
            StructuralDeltas.Register("AUG-300", "SPL-006", new[] { "light" }, null);
            var l = Loadout("WIZ-001");
            var profile = StructuralProfile.Build(l, new[] { Cat.Augment("AUG-300") }, StructuralChangeSources.Default).For("SPL-006");
            Assert.Equal(new[] { "AUG-300" }, profile.AppliedChangeIds);
            Assert.Equal(Cat.Spell("SPL-006").Tags.OrderBy(t => t, System.StringComparer.Ordinal), profile.Tags.OrderBy(t => t, System.StringComparer.Ordinal));
            Assert.Empty(StructuralProfile.Build(l, new[] { Cat.Augment("AUG-299") }, StructuralChangeSources.Default).For("SPL-006").AppliedChangeIds);
        }
    }
}
