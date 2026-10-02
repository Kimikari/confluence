using System.Linq;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Economy.Augments;
using Confluence.Economy.Eligibility;
using Confluence.Economy.Loadouts;
using Confluence.Economy.Shop;
using static Confluence.Economy.Tests.TestData;

namespace Confluence.Economy.Tests
{
    /// <summary>Acquisition-only versus ongoing conditions and dormancy resolution (spec L247, L602, L639-L646).</summary>
    public class DormancyTests
    {
        [Fact]
        [Verifies("AUG-006")]
        public void RoundIsCheckedAtAcquisitionAndEquipmentStaysOngoing()
        {
            var l = Loadout("WIZ-001");
            var glass = Give(l, "EQP-002"); // mastery
            var owned = Grant(l, "AUG-006", Conclave3);
            Assert.True(DormancyResolver.Resolve(l, NoChanges).IsActive("AUG-006"));

            // The recorded round value is reused: resolution never re-reads the round.
            Assert.True(owned.Acquisition.TryGetRecorded(((AllNode)owned.Definition.Eligibility).Children[1], out bool roundValue));
            Assert.True(roundValue);

            l.Move(glass.InstanceId, Placement.Reserve);
            var report = DormancyResolver.Resolve(l, NoChanges);
            Assert.Equal(new[] { "AUG-006" }, report.NewlyDormant);
            Assert.True(owned.IsDormant);
            Assert.Equal(1, owned.Rank);
            Assert.Equal(0, l.AugmentSlotOf("AUG-006")); // keeps its slot and rank
            Assert.Contains(owned.MissingRequirements, m => m.Text.Contains("'mastery'"));

            l.Move(glass.InstanceId, Placement.In(EquipmentSlot.MainFocus));
            report = DormancyResolver.Resolve(l, NoChanges);
            Assert.Equal(new[] { "AUG-006" }, report.Reactivated);
            Assert.Empty(owned.MissingRequirements);
        }

        [Fact]
        public void AcquisitionOnlyNegationCannotFlipLater()
        {
            var predicate = new NotNode(new FeatLeaf("FEAT-001", 1));
            var l = Loadout("WIZ-001");
            var none = new EligibilityContext(l, StructuralProfile.Build(l, null, NoChanges), null, Conclave1, FeatLedger.None);
            var evaluator = new PredicateEvaluator(none);
            var record = AcquisitionRecord.Capture(predicate, Conclave1, l.WizardId, leaf => leaf.Accept(evaluator));
            Assert.Equal(new[] { false }, record.LeafValues); // the feat leaf was false, so NOT was true

            var later = Feats("FEAT-001");
            var live = new EligibilityContext(l, StructuralProfile.Build(l, null, NoChanges), null, Conclave3, later);
            Assert.False(PredicateEvaluator.Evaluate(predicate, live));                                // read live: flipped
            Assert.True(PredicateEvaluator.Evaluate(predicate, live.WithSelf(null, record)));         // recorded: unchanged

            var restored = AcquisitionRecord.FromValues(predicate, Conclave1, l.WizardId, record.LeafValues);
            Assert.True(PredicateEvaluator.Evaluate(predicate, live.WithSelf(null, restored)));
        }

        [Fact]
        [Verifies("AUG-011")]
        public void FeatPrerequisiteIsNotReEvaluatedAfterAcquisition()
        {
            var l = Loadout("WIZ-001");
            var glass = Give(l, "EQP-002");
            Grant(l, "AUG-011", Conclave5, Feats("FEAT-002"));
            // Dormancy resolution has no feat counts at all: the recorded acquisition value is used.
            Assert.True(DormancyResolver.Resolve(l, NoChanges).IsActive("AUG-011"));
            l.Move(glass.InstanceId, Placement.Reserve);
            Assert.False(DormancyResolver.Resolve(l, NoChanges).IsActive("AUG-011")); // its equipment part is ongoing
        }

        [Fact]
        [Verifies("AUG-079", "AUG-093")]
        public void ADormantParentMakesDependentsDormantUntilRestored()
        {
            var p = Player("WIZ-006", 1000);
            var l = p.Loadout;
            var rapier = Give(l, "EQP-038");  // melee + guard
            Give(l, "EQP-031");               // light_blade melee, Sidearm
            Grant(l, "AUG-079");
            Grant(l, "AUG-093", Conclave9);
            Assert.All(l.Augments, a => Assert.True(a.IsActive));

            // Selling the guard item in a later shop makes the parent dormant, and with it the child (spec L587).
            var shop = new EquipmentShop(p, Conclave5, 1, ContentAvailability.All, NoChanges);
            var sale = shop.Sell(1, rapier.InstanceId);
            Assert.Equal(new[] { "AUG-079", "AUG-093" }, sale.Dormancy.NewlyDormant);
            var child = l.FindAugment("AUG-093");
            Assert.Equal(new[] { "AUG-079" }, child.DormantParents);
            Assert.Contains(child.MissingRequirements, m => m.Text.Contains("AUG-079"));
            Assert.DoesNotContain(child.MissingRequirements, m => m.Text.Contains("light_blade")); // its own item is still there

            // A guard item restores the parent and the child at the next finalization.
            var tabard = Give(l, "EQP-076");
            var restored = DormancyResolver.Resolve(l, NoChanges);
            Assert.Equal(new[] { "AUG-079", "AUG-093" }, restored.Reactivated);
            Assert.NotNull(tabard);
        }

        [Fact]
        [Verifies("AUG-097", "AUG-117")]
        public void ResolutionIsTopologicalNotSlotOrder()
        {
            var l = Loadout("WIZ-001");
            Give(l, "EQP-081");
            Give(l, "EQP-067"); // two movement items
            Grant(l, "AUG-149");                      // slot 0 filler
            Grant(l, "AUG-097");                      // slot 1 parent
            var result = AugmentAcquisition.AcquireNew(l, Cat.Augment("AUG-117"), Conclave9, null, 0, NoChanges); // child replaces slot 0
            Assert.True(result.Succeeded);
            Assert.Equal("AUG-149", result.Replaced.Id);
            Assert.Equal(0, l.AugmentSlotOf("AUG-117"));
            Assert.Equal(1, l.AugmentSlotOf("AUG-097"));
            var report = DormancyResolver.Resolve(l, NoChanges);
            Assert.True(report.IsActive("AUG-117")); // the parent in slot 1 resolved first
            Assert.Equal(new[] { "AUG-117", "AUG-097" }, report.Statuses.Select(s => s.Id)); // reported in slot order
        }

        [Fact]
        [Verifies("AUG-177", "AUG-085")]
        public void NotAugmentIsOngoing()
        {
            var l = Loadout("WIZ-006");
            Give(l, "EQP-038"); // guard
            Grant(l, "AUG-177", Conclave5);
            Assert.True(DormancyResolver.Resolve(l, NoChanges).IsActive("AUG-177"));
            Grant(l, "AUG-085", Conclave5);
            var report = DormancyResolver.Resolve(l, NoChanges);
            Assert.False(report.IsActive("AUG-177"));
            Assert.True(report.IsActive("AUG-085"));
        }

        [Fact]
        [Verifies("AUG-078")]
        public void DormantAugmentKeepsRankTwo()
        {
            var l = Loadout("WIZ-001");
            var glass = Give(l, "EQP-002");
            GrantRankTwo(l, "AUG-078");
            var anchored = Grant(l, "AUG-006", Conclave3);
            Assert.Equal(TransactionCode.Success, AugmentAcquisition.Upgrade(l, "AUG-006", Rarity.Silver, Conclave5, NoChanges).Code);
            l.Move(glass.InstanceId, Placement.Reserve);
            DormancyResolver.Resolve(l, NoChanges);
            Assert.True(anchored.IsDormant);
            Assert.Equal(2, anchored.Rank);
            Assert.Equal(2, l.Augments.Count);
            Assert.Equal(1, l.ActiveAugments.Count);
        }

        [Fact]
        public void DormantAugmentsDoNotQualifyOthersAndSpellSwapsAreFinalizedChanges()
        {
            var p = Player("WIZ-001", 0);
            var l = p.Loadout;
            Grant(l, "AUG-001");                         // projectile (Sunmark)
            Grant(l, "AUG-022", Conclave9);              // AUG-001 + projectile
            var shop = new EquipmentShop(p, Conclave9, 1, ContentAvailability.All, NoChanges);
            var swap = shop.SetEquippedSpells(1, new[] { "SPL-001", "SPL-002", "SPL-003", "SPL-004", "SPL-005", "SPL-007" });
            Assert.Equal(new[] { "AUG-001", "AUG-022" }, swap.Dormancy.NewlyDormant);
            Assert.Equal(new[] { "AUG-001" }, l.FindAugment("AUG-022").DormantParents);
            var back = shop.SetEquippedSpells(2, new[] { "SPL-001", "SPL-002", "SPL-003", "SPL-004", "SPL-005", "SPL-008" }); // Radiant Reversal is a projectile
            Assert.Equal(new[] { "AUG-001", "AUG-022" }, back.Dormancy.Reactivated);
        }
    }
}
