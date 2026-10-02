using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Economy.Builds;
using Confluence.Economy.Loadouts;
using static Confluence.Economy.Tests.TestData;

namespace Confluence.Economy.Tests
{
    /// <summary>The 48 worked builds as legal loadouts and possible draft paths (spec L689-L707).</summary>
    public class WorkedBuildTests
    {
        public static TheoryData<string> AllBuilds()
        {
            var data = new TheoryData<string>();
            foreach (var b in Cat.Builds) data.Add(b.Id);
            return data;
        }

        public static TheoryData<string> PerWizardPlans()
        {
            var data = new TheoryData<string>();
            foreach (var b in Cat.Builds.Where(b => !b.IsConditionalExample)) data.Add(b.Id);
            return data;
        }

        public static TheoryData<string> ConditionalExamples()
        {
            var data = new TheoryData<string>();
            foreach (var b in Cat.Builds.Where(b => b.IsConditionalExample)) data.Add(b.Id);
            return data;
        }

        [Fact]
        public void TheCatalogueHoldsThirtySixPlansAndTwelveConditionalExamples()
        {
            Assert.Equal(48, Cat.Builds.Count);
            Assert.Equal(36, Cat.Builds.Count(b => !b.IsConditionalExample));
            Assert.Equal(Enumerable.Range(37, 12).Select(i => "BLD-0" + i), Cat.Builds.Where(b => b.IsConditionalExample).Select(b => b.Id));
        }

        [Theory]
        [MemberData(nameof(AllBuilds))]
        public void EveryListedLoadoutIsLegal(string buildId)
        {
            var build = Cat.Build(buildId);
            var loadout = WorkedBuilds.CreateLoadout(Cat, build, out var problems);
            Assert.Empty(problems);
            Assert.Empty(loadout.Validate());
            Assert.Equal(build.EquippedSpellIds, loadout.EquippedSpellIds);
            Assert.Equal(build.EquipmentIds.OrderBy(i => i), loadout.EquippedItems.Select(i => i.Id).OrderBy(i => i));
            Assert.Empty(loadout.Reserve);

            // Distinct Unique groups and no definition twice (spec L210), legal slot categories (L560-L569).
            var groups = loadout.EquippedItems.Where(i => i.Definition.HasUniqueGroup).Select(i => i.Definition.UniqueGroup).ToList();
            Assert.Equal(groups.Count, groups.Distinct().Count());
            foreach (var slot in SlotRules.AllSlots)
            {
                var item = loadout.ItemIn(slot);
                if (item != null) Assert.True(SlotRules.Accepts(slot, item.Definition), buildId + " " + item.Id + " in " + slot);
            }

            // Augments: own wizard's transformations, distinct definitions and exclusivity groups (spec L541, L233).
            var augments = build.AugmentIds.Select(Cat.Augment).ToList();
            Assert.Equal(augments.Count, augments.Select(a => a.Id).Distinct().Count());
            Assert.True(augments.Count <= GlobalRules.Draft.AugmentSlots);
            var exclusive = augments.Where(a => a.HasExclusiveGroup).Select(a => a.ExclusiveGroup).ToList();
            Assert.Equal(exclusive.Count, exclusive.Distinct().Count());
            foreach (var a in augments)
            {
                Assert.True(loadout.BelongsToThisWizard(a), buildId + " " + a.Id + " belongs to another wizard");
                foreach (var target in a.TargetSpellIds) Assert.True(loadout.OwnsSpell(target), buildId + " " + a.Id + " targets " + target);
            }
        }

        [Theory]
        [MemberData(nameof(PerWizardPlans))]
        public void PerWizardPlansFollowTheDraftSchedule(string buildId)
        {
            // Spec L691: six equipped exclusive spells, three transformations on different equipped spells, three shared
            // augments, two Silver/Gold/Prismatic free selections at rounds 1, 3, 5, 7, 9, and 11.
            var build = Cat.Build(buildId);
            var picks = WorkedBuilds.ParseDraftPlan(build, out var problems);
            Assert.Empty(problems);
            Assert.Equal(Schedules.ConclaveDraftRounds, picks.Select(p => p.Round));
            Assert.Equal(build.AugmentIds.OrderBy(i => i), picks.Select(p => p.AugmentId).OrderBy(i => i));
            foreach (var pick in picks)
            {
                Assert.True(Schedules.DraftRarity(MatchMode.Conclave, pick.Round, out var rarity));
                Assert.Equal(rarity, Cat.Augment(pick.AugmentId).RarityEnum);
                Assert.Equal(1, pick.Rank);
            }
            var transformations = build.AugmentIds.Select(Cat.Augment).Where(a => a.IsTransformation).ToList();
            Assert.Equal(3, transformations.Count);
            Assert.Equal(3, transformations.Select(t => t.TargetSpellId).Distinct().Count());
            Assert.All(transformations, t => Assert.Contains(t.TargetSpellId, build.EquippedSpellIds));
            Assert.Equal(build.EquipmentCost, WorkedBuilds.EquipmentCost(Cat, build));
        }

        [Theory]
        [MemberData(nameof(AllBuilds))]
        public void EveryBuildIsAPossibleDraftPath(string buildId)
        {
            var replay = BuildReplayer.Replay(Cat, Cat.Build(buildId), ContentAvailability.All, NoChanges);
            Assert.True(replay.IsPossible, string.Join("\n", replay.Problems));
            var expected = Cat.Build(buildId).AugmentIds.Sum(id => Cat.Build(buildId).RankOf(id));
            Assert.Equal(expected, replay.Picks.Count);
            Assert.All(replay.FinalLoadout.Augments, a => Assert.True(a.IsActive, a.ToString()));
        }

        [Theory]
        [MemberData(nameof(ConditionalExamples))]
        public void EachConditionalExampleMeetsItsStatedConditionWhenItsPayoffIsDrafted(string buildId)
        {
            var replay = BuildReplayer.Replay(Cat, Cat.Build(buildId), ContentAvailability.All, NoChanges);
            Assert.True(replay.BuildConditionHeld, buildId);
        }

        [Fact]
        public void ShopsBuyOnlyUnlockedTiersWithinIncome()
        {
            // Round 1: 600 Crowns buys three of the four tier 1 items; the tier 2 trinket waits for round 3 (spec L193-L200).
            var build = Cat.Build("BLD-001");
            Assert.Equal(new[] { "EQP-001", "EQP-051", "EQP-081" }, BuildReplayer.HeldEquipment(Cat, build, MatchMode.Conclave, 1));
            Assert.Equal(new[] { "EQP-001", "EQP-051", "EQP-081", "EQP-096" }, BuildReplayer.HeldEquipment(Cat, build, MatchMode.Conclave, 2));
            Assert.Equal(build.EquipmentIds.OrderBy(i => i), BuildReplayer.HeldEquipment(Cat, build, MatchMode.Conclave, 3).OrderBy(i => i));
            Assert.Equal(600, BuildReplayer.IncomeThrough(1));
            Assert.Equal(1100, BuildReplayer.IncomeThrough(2));
        }

        [Fact]
        [Verifies("AUG-242", "AUG-261")]
        public void WeaponGraduateWaitsForTheWeaponAndTierThreeArmor()
        {
            // EQP-033 is tier 2 (round 3) and EQP-061 tier 3 (round 6): Armed Scholar is first legal at the second Silver
            // draft and Graduated Covenant at the first Prismatic draft (BLD-037 unlock plan).
            var replay = BuildReplayer.Replay(Cat, Cat.Build("BLD-037"), ContentAvailability.All, NoChanges);
            Assert.Equal(new[] { 3, 9 }, replay.Picks.Select(p => p.Pick.Round));
            Assert.Equal(EquipmentCategory.Weapon, replay.FinalLoadout.ItemIn(EquipmentSlot.MainFocus).Definition.Category);
        }

        [Fact]
        [Verifies("AUG-078", "AUG-156", "AUG-263")]
        public void TwoDefendedThesesTakesFourRanksThenTheThesis()
        {
            // Spec L705: Guard Economy rank 1 then 2 in Silver, Full Breath rank 1 then 2 in Gold, then Double Thesis.
            var replay = BuildReplayer.Replay(Cat, Cat.Build("BLD-048"), ContentAvailability.All, NoChanges);
            Assert.Equal(new[] { 1, 3, 5, 7, 9 }, replay.Picks.Select(p => p.Pick.Round));
            Assert.Equal(new[] { 1, 2, 1, 2, 1 }, replay.Picks.Select(p => p.Pick.Rank));
            Assert.Equal(3, replay.FinalLoadout.Augments.Count); // two families occupy two slots
            Assert.Equal(2, replay.FinalLoadout.FindAugment("AUG-078").Rank);
            Assert.Equal(2, replay.FinalLoadout.FindAugment("AUG-156").Rank);
        }

        [Fact]
        [Verifies("AUG-286", "AUG-262")]
        public void FeatExamplesNeedAPreviousDuelAndPassiveGear()
        {
            var weaver = BuildReplayer.Replay(Cat, Cat.Build("BLD-040"), ContentAvailability.All, NoChanges);
            Assert.Equal(9, weaver.Picks[1].Pick.Round);
            Assert.True(weaver.FinalLoadout.FindAugment("AUG-286").Acquisition != null);

            var purist = BuildReplayer.Replay(Cat, Cat.Build("BLD-041"), ContentAvailability.All, NoChanges);
            Assert.True(purist.IsPossible, string.Join("\n", purist.Problems));
            Assert.False(purist.Picks[1].ActiveEnabled);
            Assert.Equal(0, purist.FinalLoadout.HeldConsumableCharges);
        }

        [Fact]
        public void AMissingParentIsReportedAsAnInconsistencyRatherThanSkipped()
        {
            var withoutParent = ContentAvailability.FromIds(Cat.Augments.Select(a => a.Id).Where(id => id != "AUG-265")
                .Concat(Cat.EquipmentItems.Select(e => e.Id)));
            var replay = BuildReplayer.Replay(Cat, Cat.Build("BLD-039"), withoutParent, NoChanges);
            Assert.False(replay.IsPossible);
            Assert.Contains(replay.Problems, p => p.Contains("AUG-265") && p.Contains("not available"));
            Assert.Contains(replay.Problems, p => p.Contains("AUG-285"));
        }
    }
}
