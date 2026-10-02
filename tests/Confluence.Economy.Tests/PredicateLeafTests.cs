using System.Linq;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Economy.Eligibility;
using Confluence.Economy.Loadouts;
using static Confluence.Economy.Tests.TestData;

namespace Confluence.Economy.Tests
{
    /// <summary>
    /// Every predicate leaf (spec L604-L637) evaluated through real catalogue augments, plus a synthetic owned_item leaf
    /// (no supplied augment uses it).
    /// </summary>
    public class PredicateLeafTests
    {
        private static readonly RoundInfo Gold = Conclave5;
        private static readonly RoundInfo Prismatic = Conclave9;

        [Fact]
        [Verifies("AUG-149", "AUG-012")]
        public void AlwaysHoldsForEveryWizard()
        {
            foreach (var w in Cat.Wizards)
            {
                Assert.True(Eligible(Loadout(w.Id), "AUG-149"));
                Assert.True(Eligible(Loadout(w.Id), "AUG-012", Gold));
            }
        }

        [Fact]
        [Verifies("AUG-002", "AUG-003")]
        public void WizardIsExactIdentity()
        {
            Assert.True(Eligible(Loadout("WIZ-004"), "AUG-002"));
            Assert.False(Eligible(Loadout("WIZ-005"), "AUG-002"));
            Assert.True(Eligible(Loadout("WIZ-009"), "AUG-003"));   // any of the listed wizards
            Assert.True(Eligible(Loadout("WIZ-031"), "AUG-003"));
            Assert.False(Eligible(Loadout("WIZ-004"), "AUG-003"));
            Assert.Contains("acquisition only", Explain(Loadout("WIZ-004"), "AUG-003").Summary);
        }

        [Fact]
        [Verifies("AUG-048")]
        public void EquippedItemRequiresTheExactItemInASlotNotTheReserve()
        {
            var l = Loadout("WIZ-006"); // Iron Shard is a projectile
            Assert.False(Eligible(l, "AUG-048", Prismatic));
            var hazel = Give(l, "EQP-007", Placement.Reserve);
            Assert.False(Eligible(l, "AUG-048", Prismatic));
            Assert.Equal(TransactionCode.Success, l.Move(hazel.InstanceId, Placement.In(EquipmentSlot.MainFocus)));
            Assert.True(Eligible(l, "AUG-048", Prismatic));
            // Occupancy, not activation: a disabled Forked Hazel still counts as equipped.
            Assert.Equal(TransactionCode.Success, l.SetEnabledActive(null));
            Assert.True(Eligible(l, "AUG-048", Prismatic));
        }

        [Fact]
        public void OwnedItemIncludesReserveButNotSpentConsumables()
        {
            var leaf = new OwnedItemLeaf("EQP-096");
            var l = Loadout("WIZ-001");
            Assert.False(Evaluate(leaf, l));
            Give(l, "EQP-096", Placement.Reserve);
            Assert.True(Evaluate(leaf, l)); // spec L614 "including reserve"

            var rain = new OwnedItemLeaf("EQP-116");
            Give(l, "EQP-116");
            Assert.True(Evaluate(rain, l));
            l.SpendConsumableCharge();
            Assert.False(Evaluate(rain, l)); // a spent consumable is inert
        }

        [Fact]
        [Verifies("AUG-007")]
        public void EquippedTagCountsLiveTagsOfEquippedItems()
        {
            var l = Loadout("WIZ-006");
            Assert.False(Eligible(l, "AUG-007"));
            var soles = Give(l, "EQP-081", Placement.Reserve); // movement, but in reserve
            Assert.False(Eligible(l, "AUG-007"));
            l.Move(soles.InstanceId, Placement.In(EquipmentSlot.Boots));
            Assert.True(Eligible(l, "AUG-007"));
        }

        [Fact]
        [Verifies("AUG-021")]
        public void TwoCopiesOfOneDefinitionAreOneSource()
        {
            var l = Loadout("WIZ-001");
            Give(l, "EQP-109");                     // mastery trinket in Trinket A
            Give(l, "EQP-109");                     // second copy: reserve (cannot be equipped twice)
            Assert.False(Eligible(l, "AUG-021", Prismatic));
            Give(l, "EQP-002");                     // a different mastery definition
            Assert.True(Eligible(l, "AUG-021", Prismatic));
        }

        [Fact]
        [Verifies("AUG-010")]
        public void ADisabledActiveSuppliesNoActiveEffectTag()
        {
            var l = Loadout("WIZ-005");
            var sleeves = Give(l, "EQP-078");       // active clothing: active_equipment, casting, sequence
            Assert.Same(sleeves, l.EnabledActive);
            Assert.False(Eligible(l, "AUG-010", Gold)); // sequence present but an active is enabled
            l.SetEnabledActive(null);
            // Disabled: neither active_equipment nor its effect tag 'sequence' is live (spec L585).
            Assert.False(Eligible(l, "AUG-010", Gold));
            Assert.Contains("currently 0", Explain(l, "AUG-010", Gold).Summary);
            Give(l, "EQP-003");                     // an independent enabled passive supplies 'sequence'
            Assert.True(Eligible(l, "AUG-010", Gold));
            l.SetEnabledActive(sleeves.InstanceId);
            Assert.False(Eligible(l, "AUG-010", Gold));
        }

        [Fact]
        [Verifies("AUG-022", "AUG-001")]
        public void AugmentLeafNeedsTheActiveParentAtRank()
        {
            var l = Loadout("WIZ-006");
            Assert.False(Eligible(l, "AUG-022", Prismatic));
            Grant(l, "AUG-001");
            Assert.True(Eligible(l, "AUG-022", Prismatic));
            var rankTwo = new AugmentLeaf("AUG-001", 2);
            Assert.False(Evaluate(rankTwo, l));
            Augments.AugmentAcquisition.Upgrade(l, "AUG-001", Rarity.Silver, Conclave3, NoChanges);
            Assert.True(Evaluate(rankTwo, l));
        }

        [Fact]
        [Verifies("AUG-289")]
        public void SpellLeafReadsTheOwnedKitNotTheEquippedSix()
        {
            var l = Loadout("WIZ-001");
            Assert.True(Eligible(l, "AUG-289"));
            l.SetEquippedSpells(new[] { "SPL-002", "SPL-003", "SPL-004", "SPL-005", "SPL-006", "SPL-007" });
            Assert.True(Eligible(l, "AUG-289")); // SPL-001 is still in the kit (spec L239)
            Assert.False(Eligible(Loadout("WIZ-002"), "AUG-289"));
        }

        [Fact]
        [Verifies("AUG-001", "AUG-008")]
        public void SpellTagCountsEquippedProfilesWithAlsoOnTheSameSpell()
        {
            var l = Loadout("WIZ-001");
            Assert.True(Eligible(l, "AUG-001"));  // Sunmark is a projectile
            l.SetEquippedSpells(new[] { "SPL-001", "SPL-002", "SPL-003", "SPL-004", "SPL-005", "SPL-007" });
            Assert.False(Eligible(l, "AUG-001")); // owned but unequipped projectiles do not count

            var healer = Loadout("WIZ-020");      // SPL-158 is a channel but not damaging
            healer.SetEquippedSpells(new[] { "SPL-153", "SPL-154", "SPL-155", "SPL-156", "SPL-157", "SPL-158" });
            Assert.False(Eligible(healer, "AUG-008"));
            var striker = Loadout("WIZ-015");
            Assert.False(Eligible(striker, "AUG-008"));
            striker.SetEquippedSpells(new[] { "SPL-113", "SPL-114", "SPL-115", "SPL-116", "SPL-117", "SPL-120" });
            Assert.True(Eligible(striker, "AUG-008")); // channel and damaging on the same spell
        }

        [Fact]
        [Verifies("AUG-258")]
        public void SpellTagExactNeedsExactlyTheCount()
        {
            Assert.True(Eligible(Loadout("WIZ-001"), "AUG-258", Prismatic));  // Mirror Walk is the one summon
            Assert.False(Eligible(Loadout("WIZ-002"), "AUG-258", Prismatic)); // zero summons
            var l = Loadout("WIZ-001");
            var changes = new StructuralChangeTable().AddTransformation("AUG-299", "SPL-006", new[] { "summon" }, null);
            Grant(l, "AUG-299", Conclave1, null, changes); // Sunmark transformation, declared here to add a second summon
            Augments.DormancyResolver.Resolve(l, changes);
            Assert.False(new CandidateJudge(l, Prismatic, null, changes).IsEligible(Cat.Augment("AUG-258")));
        }

        [Fact]
        [Verifies("AUG-250", "AUG-256")]
        public void SpellDistinctTagsCountsDistinctSetTagsOnQualifyingProfiles()
        {
            Assert.True(Eligible(Loadout("WIZ-002"), "AUG-250", Gold));
            Assert.False(Eligible(Loadout("WIZ-025"), "AUG-250", Gold)); // only three of the set

            // AUG-256 counts only damaging profiles (also) and needs a sequence item.
            var l = Loadout("WIZ-002");
            Give(l, "EQP-003");
            Assert.True(Eligible(l, "AUG-256", Gold)); // projectile, cone/melee, field/dash on damaging spells
            var defensive = Loadout("WIZ-010");
            Give(defensive, "EQP-003");
            defensive.SetEquippedSpells(new[] { "SPL-073", "SPL-075", "SPL-076", "SPL-077", "SPL-078", "SPL-080" });
            Assert.False(Eligible(defensive, "AUG-256", Gold)); // the field spells are not damaging
        }

        [Fact]
        [Verifies("AUG-259")]
        public void SpellCooldownCountIsInclusive()
        {
            var l = Loadout("WIZ-006"); // default: 4.5 s and 6 s
            Assert.False(Eligible(l, "AUG-259", Prismatic));
            l.SetEquippedSpells(new[] { "SPL-041", "SPL-042", "SPL-047", "SPL-043", "SPL-044", "SPL-045" });
            Assert.True(Eligible(l, "AUG-259", Prismatic)); // 4.5, 6 (at_most 6 inclusive), 5
        }

        [Fact]
        [Verifies("AUG-041", "AUG-042", "AUG-260")]
        public void SpellCastTimeCountIsInclusive()
        {
            Assert.True(Eligible(Loadout("WIZ-002"), "AUG-042", Prismatic)); // Cinder Sweep 0.25 s
            Assert.True(Eligible(Loadout("WIZ-005"), "AUG-041", Prismatic)); // 0.4 s counts for at_least 0.4
            var l = Loadout("WIZ-002"); // 0.5 and 0.45
            Assert.False(Eligible(l, "AUG-260", Prismatic));
            l.SetEquippedSpells(new[] { "SPL-009", "SPL-010", "SPL-011", "SPL-012", "SPL-014", "SPL-016" });
            Assert.True(Eligible(l, "AUG-260", Prismatic));
        }

        [Fact]
        [Verifies("AUG-242", "AUG-243", "AUG-244", "AUG-094")]
        public void FocusCategoriesReadTheExactSlots()
        {
            var l = Loadout("WIZ-001");
            Assert.False(Eligible(l, "AUG-243")); // the issued plain focus has no category
            var wand = Give(l, "EQP-001");
            Assert.True(Eligible(l, "AUG-243"));
            Assert.False(Eligible(l, "AUG-242"));
            l.Move(wand.InstanceId, Placement.In(EquipmentSlot.Sidearm));
            Assert.False(Eligible(l, "AUG-243")); // the Sidearm is not the Main Focus

            var staff = Loadout("WIZ-001");
            Give(staff, "EQP-016");
            Assert.True(Eligible(staff, "AUG-244"));

            var twin = Loadout("WIZ-001");
            Give(twin, "EQP-031");
            Assert.False(Eligible(twin, "AUG-094", Prismatic));
            Give(twin, "EQP-032");
            Assert.True(Eligible(twin, "AUG-094", Prismatic)); // weapon in both Main Focus and Sidearm
            Assert.True(Eligible(twin, "AUG-242"));
        }

        [Fact]
        [Verifies("AUG-251")]
        public void IssuedSlotMeansOnlyThePlainIssuedItem()
        {
            var l = Loadout("WIZ-003");
            Assert.True(Eligible(l, "AUG-251", Gold));
            var vest = Give(l, "EQP-052");
            Assert.False(Eligible(l, "AUG-251", Gold));
            l.Move(vest.InstanceId, Placement.Reserve);
            Assert.True(Eligible(l, "AUG-251", Gold));
        }

        [Fact]
        [Verifies("AUG-252")]
        public void SlotTierIsAtLeastTheTier()
        {
            var l = Loadout("WIZ-006");
            Give(l, "EQP-031");
            var light = Give(l, "EQP-052"); // tier 1 armor
            Assert.False(Eligible(l, "AUG-252", Gold));
            l.Move(light.InstanceId, Placement.Reserve);
            Give(l, "EQP-061");             // tier 3 armor: inclusive threshold
            Assert.True(Eligible(l, "AUG-252", Gold));
        }

        [Fact]
        [Verifies("AUG-253")]
        public void EquippedCategoryNeedsDistinctNonEmptyUniqueGroups()
        {
            var l = Loadout("WIZ-005");
            Give(l, "EQP-096");
            Assert.False(Eligible(l, "AUG-253", Gold));
            Give(l, "EQP-096");             // a copy in reserve does not occupy a second trinket slot
            Assert.False(Eligible(l, "AUG-253", Gold));
            Give(l, "EQP-097");
            Assert.True(Eligible(l, "AUG-253", Gold));
            var beacon = Give(l, "EQP-094"); // an enabled active breaks the NOT branch
            Assert.False(Eligible(l, "AUG-253", Gold));
            l.SetEnabledActive(null);
            Assert.True(Eligible(l, "AUG-253", Gold));
            Assert.NotNull(beacon);
        }

        [Fact]
        [Verifies("AUG-254")]
        public void ConsumableChargesAtMostCountsHeldUnspentCharges()
        {
            var l = Loadout("WIZ-004");
            Assert.True(Eligible(l, "AUG-254", Gold));
            var tonic = Give(l, "EQP-113", Placement.Reserve); // reserve charges are held too
            Assert.False(Eligible(l, "AUG-254", Gold));
            l.Move(tonic.InstanceId, Placement.In(EquipmentSlot.Consumable));
            l.SpendConsumableCharge();
            Assert.True(Eligible(l, "AUG-254", Gold));
        }

        [Fact]
        [Verifies("AUG-144")]
        public void AugmentTagCountCountsOnlyEnumeratedActiveParentsWithTheTag()
        {
            var l = Loadout("WIZ-002");
            Give(l, "EQP-004"); // terrain
            Grant(l, "AUG-121"); // reaction-tagged, enumerated
            Assert.False(Eligible(l, "AUG-144", Prismatic));
            Grant(l, "AUG-125"); // enumerated but not reaction-tagged
            Assert.False(Eligible(l, "AUG-144", Prismatic));
            Grant(l, "AUG-149"); // not enumerated
            Assert.False(Eligible(l, "AUG-144", Prismatic));
            Grant(l, "AUG-129", Gold);
            Assert.True(Eligible(l, "AUG-144", Prismatic));
        }

        [Fact]
        [Verifies("AUG-263")]
        public void AugmentRankCountWithDistinctFamilies()
        {
            var l = Loadout("WIZ-006");
            Give(l, "EQP-096");
            Give(l, "EQP-097");
            GrantRankTwo(l, "AUG-078");                   // Melee and guard
            Assert.False(Eligible(l, "AUG-263", Prismatic));
            GrantRankTwo(l, "AUG-073");                   // same family: still one family
            Assert.False(Eligible(l, "AUG-263", Prismatic));
            Grant(l, "AUG-156", Gold);                    // a different family, rank 1 only
            Assert.False(Eligible(l, "AUG-263", Prismatic));
            Augments.AugmentAcquisition.Upgrade(l, "AUG-156", Rarity.Gold, new RoundInfo(MatchMode.Conclave, 7), NoChanges);
            Assert.True(Eligible(l, "AUG-263", Prismatic));
        }

        [Fact]
        [Verifies("AUG-288")]
        public void AugmentFamilyCountCountsDistinctFamilies()
        {
            var l = Loadout("WIZ-005");
            Give(l, "EQP-003"); // sequence
            Grant(l, "AUG-073");
            Grant(l, "AUG-074"); // same family (Melee and guard)
            Grant(l, "AUG-149"); // Resource and recovery
            Assert.False(Eligible(l, "AUG-288", Prismatic));
            Grant(l, "AUG-097"); // Movement arts
            Assert.True(Eligible(l, "AUG-288", Prismatic));
        }

        [Fact]
        [Verifies("AUG-006", "AUG-021")]
        public void RoundAtLeastUsesTheConclaveScheduleInBothModes()
        {
            var l = Loadout("WIZ-001");
            Give(l, "EQP-002"); // mastery
            Assert.False(Eligible(l, "AUG-006", Conclave1));
            Assert.True(Eligible(l, "AUG-006", Conclave3));
            Assert.False(Eligible(l, "AUG-006", new RoundInfo(MatchMode.Duel, 1)));
            Assert.True(Eligible(l, "AUG-006", new RoundInfo(MatchMode.Duel, 2))); // D-029: Duel 2 holds Conclave 3's draft
            Give(l, "EQP-006");
            Assert.False(Eligible(l, "AUG-021", new RoundInfo(MatchMode.Duel, 4)));
            Assert.True(Eligible(l, "AUG-021", new RoundInfo(MatchMode.Duel, 5)));
        }

        [Fact]
        [Verifies("AUG-011")]
        public void FeatCountsCompletedCompetitiveDuels()
        {
            var l = Loadout("WIZ-001");
            Give(l, "EQP-002");
            Assert.False(Eligible(l, "AUG-011", Gold, Feats()));
            var ledger = new FeatLedger();
            Assert.Equal(FeatRecordResult.IgnoredTraining, ledger.RecordCompletedDuel("practice", true, new[] { "FEAT-002" }));
            Assert.False(Eligible(l, "AUG-011", Gold, ledger)); // training feats never count (spec L280)
            Assert.Equal(FeatRecordResult.Recorded, ledger.RecordCompletedDuel("r1", false, new[] { "FEAT-002" }));
            Assert.True(Eligible(l, "AUG-011", Gold, ledger));
        }

        [Fact]
        [Verifies("AUG-245", "AUG-246", "AUG-241")]
        public void NotNegatesItsChild()
        {
            var l = Loadout("WIZ-003");
            Assert.True(Eligible(l, "AUG-245"));
            Assert.True(Eligible(l, "AUG-246"));
            Assert.True(Eligible(l, "AUG-241"));
            Give(l, "EQP-081"); // movement
            Give(l, "EQP-057"); // shield armor
            Assert.False(Eligible(l, "AUG-245"));
            Assert.False(Eligible(l, "AUG-246"));
            Assert.True(Eligible(l, "AUG-241"));
            Give(l, "EQP-077"); // an enabled active
            Assert.False(Eligible(l, "AUG-241"));
            var text = Explain(l, "AUG-246").Summary;
            Assert.Contains("no equipped item supplying the 'shield' tag", text, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        [Verifies("AUG-057")]
        public void AnyNeedsOneAlternativeAndExplainsTheGroup()
        {
            var frost = Cat.Wizards.First(w => w.SpellIds.Take(6).Any(id => Cat.Spell(id).HasTag("school:frost")));
            var f = Loadout(frost.Id);
            Assert.False(Eligible(f, "AUG-057", Gold));
            var explained = Explain(f, "AUG-057", Gold);
            Assert.Single(explained.Missing);
            Assert.StartsWith("one of:", explained.Missing[0].Text);
            Give(f, "EQP-116"); // Pocket Rain equipped
            Assert.True(Eligible(f, "AUG-057", Gold));
        }
    }
}
