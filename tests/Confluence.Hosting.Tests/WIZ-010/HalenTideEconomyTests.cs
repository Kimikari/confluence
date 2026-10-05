using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Economy;
using Confluence.Economy.Augments;
using Confluence.Economy.Eligibility;
using Confluence.Economy.Loadouts;
using Confluence.Sim.Content;
using Confluence.Sim.Content.Wizards.HalenTide;

namespace Confluence.Hosting.Tests.HalenTide
{
    /// <summary>
    /// Economy reads WIZ-010's transformation structure from the populated content registry (Core StructuralDeltas, D-012):
    /// each transformation's eligibility profile is the base kit tags plus exactly its declared delta (spec L243, L537, L543).
    /// </summary>
    public sealed class HalenTideEconomyTests
    {
        private static readonly string[] Transformations =
        {
            "AUG-433", "AUG-434", "AUG-435", "AUG-436", "AUG-437", "AUG-438", "AUG-439", "AUG-440",
            "AUG-441", "AUG-442", "AUG-443", "AUG-444", "AUG-445", "AUG-446", "AUG-447", "AUG-448",
        };

        private static Catalogue Cat => HostFixture.Catalogue;

        private static Loadout Halen(string spellId)
        {
            var l = new Loadout(Cat, "WIZ-010");
            if (!l.EquippedSpellIds.Contains(spellId))
            {
                var six = new List<string> { spellId };
                six.AddRange(l.EquippedSpellIds.Take(GlobalRules.Loadout.EquippedSpellSlots - 1));
                Assert.Equal(TransactionCode.Success, l.SetEquippedSpells(six));
            }

            return l;
        }

        [Fact]
        [Verifies("AUG-433", "AUG-434", "AUG-435", "AUG-436", "AUG-437", "AUG-438", "AUG-439", "AUG-440",
            "AUG-441", "AUG-442", "AUG-443", "AUG-444", "AUG-445", "AUG-446", "AUG-447", "AUG-448")]
        public void Economy_Sees_Each_Transformation_s_Structural_Profile_From_The_Registry()
        {
            ContentRegistry registry = ContentRegistry.Default; // creating the behaviors registers the kit's deltas
            foreach (string id in Transformations)
            {
                Assert.True(registry.Contains(id), id);
                AugmentDef aug = Cat.Augment(id);
                string spell = aug.TargetSpellId;
                Loadout l = Halen(spell);
                StructuralProfile profile = StructuralProfile.Build(l, new[] { aug }, StructuralChangeSources.Default);
                SpellProfile sp = profile.For(spell);
                var row = HalenStructure.Table.SingleOrDefault(t => t.Item1 == id);
                IEnumerable<string> expected = Cat.Spell(spell).Tags;
                if (row != null) expected = expected.Except(row.Item4).Union(row.Item3);
                Assert.Equal(expected.OrderBy(t => t).ToArray(), sp.Tags.OrderBy(t => t).ToArray());
                Assert.Equal(row == null ? new string[0] : new[] { id }, sp.AppliedChangeIds.ToArray());

                // The other five spells keep their base tags.
                foreach (SpellProfile other in profile.Spells.Where(s => s.SpellId != spell))
                {
                    Assert.Equal(Cat.Spell(other.SpellId).Tags.OrderBy(t => t), other.Tags.OrderBy(t => t));
                }
            }
        }

        [Fact]
        [Verifies("AUG-437", "AUG-436")]
        public void An_Owned_Transformation_Changes_Real_Eligibility()
        {
            // Without Pressure Jet or Deepglass Prison, Halen has no projectile: AUG-001 Steady Hand ("at least one
            // projectile spell equipped") is not offered. Owning AUG-437 makes Bubble Buckler a projectile in the
            // eligibility profile, so it is (spec L243 two-profile rule: transformations shape the eligibility profile).
            var noProjectile = new[] { "SPL-074", "SPL-075", "SPL-076", "SPL-077", "SPL-078", "SPL-080" };
            IStructuralChangeSource changes = StructuralChangeSources.Default;
            Assert.True(ContentRegistry.Default.Contains("AUG-437"));
            var round = new RoundInfo(MatchMode.Conclave, 5);

            var plain = new Loadout(Cat, "WIZ-010");
            Assert.Equal(TransactionCode.Success, plain.SetEquippedSpells(noProjectile));
            DormancyResolver.Resolve(plain, changes);
            Assert.False(new CandidateJudge(plain, round, null, changes).IsEligible(Cat.Augment("AUG-001")));

            var released = new Loadout(Cat, "WIZ-010");
            Assert.Equal(TransactionCode.Success, released.SetEquippedSpells(noProjectile));
            AcquisitionResult got = AugmentAcquisition.AcquireNew(released, Cat.Augment("AUG-437"), round, null, null, changes);
            Assert.True(got.Succeeded, got.Code.ToString());
            DormancyReport report = DormancyResolver.Resolve(released, changes);
            Assert.True(report.IsActive("AUG-437"));
            Assert.True(report.EligibilityProfile.For("SPL-075").HasTag("projectile"));
            Assert.True(new CandidateJudge(released, round, null, changes).IsEligible(Cat.Augment("AUG-001")));

            // AUG-436 removes Water Hammer's push in the same profile Economy uses.
            var fan = new Loadout(Cat, "WIZ-010");
            Assert.True(AugmentAcquisition.AcquireNew(fan, Cat.Augment("AUG-436"), round, null, null, changes).Succeeded);
            SpellProfile hammer = DormancyResolver.Resolve(fan, changes).EligibilityProfile.For("SPL-074");
            Assert.True(hammer.HasTag("cone"));
            Assert.False(hammer.HasTag("push"));
        }
    }
}
