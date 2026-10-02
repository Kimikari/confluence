using System.Linq;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using static Confluence.Economy.Tests.TestData;

namespace Confluence.Economy.Tests
{
    /// <summary>Design floors checked at match start (spec L233, DECISIONS D-010) and the feat ledger (spec L251-L280).</summary>
    public class FloorAndFeatTests
    {
        public static TheoryData<MatchMode> Modes => new TheoryData<MatchMode> { MatchMode.Conclave, MatchMode.Duel };

        [Theory]
        [MemberData(nameof(Modes))]
        public void EveryWizardHasTenLegalAlternativesInEveryRarityAtTheDefaultLoadout(MatchMode mode)
        {
            Assert.Equal(36, Cat.Wizards.Count);
            foreach (var wizard in Cat.Wizards)
            {
                var report = EconomyFloors.CheckDraftFloor(Cat, ContentAvailability.All, wizard.Id, mode, NoChanges);
                Assert.Equal(3, report.Rarities.Count);
                foreach (var rarity in report.Rarities)
                    Assert.True(rarity.Meets, wizard.Id + " " + rarity);
                Assert.True(report.MeetsAll);
            }
        }

        [Fact]
        public void MatchStartReportsGapsInsteadOfShrinkingTheRules()
        {
            var all = EconomyFloors.CheckMatchStart(Cat, ContentAvailability.All, MatchMode.Conclave, Cat.Wizards.Select(w => w.Id), NoChanges);
            Assert.True(all.MeetsAll);
            Assert.Empty(all.Gaps);
            Assert.Equal(36, all.Wizards.Count);

            // Only five Silver augments and three items implemented: both floors fail and are reported.
            var thin = ContentAvailability.FromIds(new[] { "AUG-149", "AUG-073", "AUG-074", "AUG-075", "AUG-076", "EQP-001", "EQP-003", "EQP-006" });
            var report = EconomyFloors.CheckMatchStart(Cat, thin, MatchMode.Duel, new[] { "WIZ-002", "WIZ-010", "WIZ-002" }, NoChanges);
            Assert.False(report.MeetsAll);
            Assert.Equal(2, report.Wizards.Count); // duplicates collapse
            Assert.False(report.Equipment.Meets);
            Assert.Equal(3, report.Equipment.AvailablePerTier[0]);
            Assert.Equal(6, report.Equipment.Required);
            Assert.Equal(5, report.Wizards[0].For(Rarity.Silver).LegalAlternatives);
            Assert.Equal(0, report.Wizards[0].For(Rarity.Gold).LegalAlternatives);
            Assert.Contains(report.Gaps, g => g.StartsWith("WIZ-010 Prismatic"));
        }

        [Fact]
        public void TheFullCatalogueHasEnoughEquipmentForThreeOffersAndRerolls()
        {
            var report = EconomyFloors.CheckEquipmentFloor(Cat, ContentAvailability.All);
            Assert.True(report.Meets);
            Assert.Equal(new[] { 22, 46, 31, 21 }, report.AvailablePerTier);
        }

        [Fact]
        public void FloorChecksAnyLoadoutWithoutChangingIt()
        {
            var l = Loadout("WIZ-001");
            Grant(l, "AUG-149");
            var report = EconomyFloors.CheckDraftFloor(l, FeatLedger.None, MatchMode.Conclave, ContentAvailability.All, NoChanges);
            Assert.True(report.MeetsAll);
            Assert.Single(l.Augments);
        }

        [Fact]
        public void FeatLedgerCountsCompletedDuelsOncePerFeat()
        {
            var ledger = new FeatLedger();
            Assert.Equal(FeatRecordResult.Recorded, ledger.RecordCompletedDuel("R1", false, new[] { "FEAT-003", "FEAT-003", "FEAT-009" }));
            Assert.Equal(1, ledger.CompletedDuelsMeeting("FEAT-003")); // one duel, counted once
            Assert.Equal(FeatRecordResult.DuplicateDuel, ledger.RecordCompletedDuel("R1", false, new[] { "FEAT-003" }));
            Assert.Equal(FeatRecordResult.Recorded, ledger.RecordCompletedDuel("R2", false, new[] { "FEAT-003" }));
            Assert.Equal(2, ledger.CompletedDuelsMeeting("FEAT-003"));    // a threshold of two needs two separate duels
            Assert.Equal(FeatRecordResult.IgnoredTraining, ledger.RecordCompletedDuel("lab", true, new[] { "FEAT-001" }));
            Assert.Equal(0, ledger.CompletedDuelsMeeting("FEAT-001"));
            Assert.Equal(FeatRecordResult.UnknownFeat, ledger.RecordCompletedDuel("R3", false, new[] { "FEAT-001", "FEAT-099" }));
            Assert.Equal(0, ledger.CompletedDuelsMeeting("FEAT-001")); // the whole report is rejected
            Assert.Equal(FeatRecordResult.Recorded, ledger.RecordCompletedDuel("R3", false, null)); // a duel with no feats still completes
            Assert.Equal(3, ledger.CompletedDuels);
            Assert.Equal(FeatRecordResult.InvalidDuel, ledger.RecordCompletedDuel("", false, null));
            Assert.Equal(0, FeatLedger.None.CompletedDuelsMeeting("FEAT-003"));
        }

        [Fact]
        public void FeatThresholdOfTwoNeedsTwoDuels()
        {
            var two = new FeatLeaf("FEAT-007", 2);
            var l = Loadout("WIZ-001");
            Assert.False(Evaluate(two, l, Conclave5, Feats("FEAT-007")));
            Assert.True(Evaluate(two, l, Conclave5, Feats("FEAT-007", "FEAT-007")));
            Assert.True(Evaluate(two, l, Conclave5, Feats("FEAT-007", "", "FEAT-007,FEAT-001")));
        }
    }
}
