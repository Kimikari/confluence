using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Content;
using Confluence.Core.Json;
using Xunit.Abstractions;
using static Confluence.Core.Tests.Content.TestCatalogue;

namespace Confluence.Core.Tests.Content
{
    public class CatalogueValidatorTests
    {
        private readonly ITestOutputHelper _output;

        public CatalogueValidatorTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void SuppliedCatalogueHasNoErrors()
        {
            var issues = CatalogueValidator.Validate(Real);
            foreach (var issue in issues) _output.WriteLine(issue.ToString());
            Assert.DoesNotContain(issues, i => i.IsError);
            Assert.Equal(0, ValidationIssue.CountErrors(issues));
        }

        [Fact]
        public void SuppliedCatalogueWarningsAreExactlyTheKnownDataInconsistencies()
        {
            // Recorded in DECISIONS.md: spec L596 says 121 compound acquisition predicates; 124 shared augments have an
            // all/any/not root (121 all/any plus three 'not' roots).
            var warnings = CatalogueValidator.Validate(Real).Where(i => !i.IsError).ToList();
            var compound = Assert.Single(warnings);
            Assert.Equal(ValidationChecks.CompoundPredicates, compound.Check);
            Assert.Contains("124 shared augments", compound.Message);
            Assert.Contains("all/any alone number 121", compound.Message);
            Assert.Contains("AUG-241, AUG-245, AUG-246", compound.Message);
        }

        [Fact]
        public void CompoundWarningDisappearsWhenTheCountMatches()
        {
            var json = Source;
            foreach (var id in new[] { "AUG-241", "AUG-245", "AUG-246" })
                json = EditRecord(json, id, "eligibility", Parse("{\"equipped_tag\":{\"tag\":\"movement\",\"count\":1}}"));
            var issues = CatalogueValidator.Validate(Catalogue.FromJson(json));
            Assert.DoesNotContain(issues, i => i.Check == ValidationChecks.CompoundPredicates);
        }

        public sealed record BrokenCase(Func<JsonValue, JsonValue> Mutate, ValidationSeverity Severity, string Check, string Id);

        private static BrokenCase Err(string check, string id, Func<JsonValue, JsonValue> mutate) => new(mutate, ValidationSeverity.Error, check, id);
        private static BrokenCase Warn(string check, string id, Func<JsonValue, JsonValue> mutate) => new(mutate, ValidationSeverity.Warning, check, id);

        private static Func<JsonValue, JsonValue> Set(string id, string field, JsonValue value) => j => EditRecord(j, id, field, value);
        private static Func<JsonValue, JsonValue> Eligibility(string id, string predicate) => Set(id, "eligibility", Parse(predicate));

        private static Func<JsonValue, JsonValue> EditStrings(string id, string field, Func<List<string>, IEnumerable<string>> edit) =>
            j => EditRecord(j, id, r => r.With(field, JsonValue.StringArray(edit(r[field].AsStringList().ToList()))));

        private static readonly Dictionary<string, BrokenCase> Cases = new()
        {
            // Totals, rules block, IDs
            ["counts-declared"] = Err(ValidationChecks.Counts, "counts", j => j.With("counts", j["counts"].With("wizards", 35))),
            ["counts-missing-record"] = Err(ValidationChecks.Counts, "counts", j => EditList(j, "spells", l => l.Take(287))),
            ["id-missing"] = Err(ValidationChecks.IdInterval, "SPL-288", j => EditList(j, "spells", l => l.Take(287))),
            ["rule-flags"] = Err(ValidationChecks.RuleFlags, "rules", j => j.With("rules", j["rules"].With("augment_max_rank", 3))),
            ["id-format"] = Err(ValidationChecks.IdFormat, "BIO-3", Set("BIO-003", "id", "BIO-3")),
            ["duplicate-id"] = Err(ValidationChecks.DuplicateId, "BIO-001", Set("BIO-002", "id", "BIO-001")),
            ["id-outside-interval"] = Err(ValidationChecks.IdInterval, "MOD-073", Set("MOD-072", "id", "MOD-073")),
            ["id-order"] = Warn(ValidationChecks.IdOrder, "PROP-002", j => EditList(j, "props", l => { (l[0], l[1]) = (l[1], l[0]); return l; })),

            // Wizards and spells
            ["spell-ownership-list"] = Err(ValidationChecks.SpellOwnership, "WIZ-001", EditStrings("WIZ-001", "spell_ids", l => { l[0] = "SPL-009"; return l; })),
            ["spell-ownership-field"] = Err(ValidationChecks.SpellOwnership, "SPL-001", Set("SPL-001", "wizard_id", "WIZ-002")),
            ["default-spells"] = Err(ValidationChecks.DefaultSpells, "WIZ-001", EditStrings("WIZ-001", "default_spell_ids", l => Enumerable.Reverse(l))),
            ["spell-school"] = Err(ValidationChecks.SpellFields, "SPL-001", Set("SPL-001", "school", "Plasma")),
            ["spell-channel"] = Err(ValidationChecks.SpellFields, "SPL-002", Set("SPL-002", "damage_channel", "sonic")),
            ["spell-negative-mana"] = Err(ValidationChecks.NumericRange, "SPL-003", Set("SPL-003", "mana", -1)),
            ["spell-negative-cooldown"] = Err(ValidationChecks.NumericRange, "SPL-004", Set("SPL-004", "cooldown", -2)),
            ["spell-negative-cast-time"] = Err(ValidationChecks.NumericRange, "SPL-005", Set("SPL-005", "cast_time", -0.1)),
            ["spell-negative-range"] = Err(ValidationChecks.NumericRange, "SPL-006", Set("SPL-006", "range", -5)),
            ["spell-school-tag"] = Err(ValidationChecks.SpellTags, "SPL-009", EditStrings("SPL-009", "tags", l => l.Where(t => t != "school:heat"))),
            ["spell-channel-tag"] = Err(ValidationChecks.SpellTags, "SPL-009", EditStrings("SPL-009", "tags", l => l.Where(t => t != "channel:thermal"))),

            // Equipment
            ["equipment-category-interval"] = Err(ValidationChecks.EquipmentCategory, "EQP-001", Set("EQP-001", "category", "Staff")),
            ["equipment-category-unknown"] = Err(ValidationChecks.EquipmentCategory, "EQP-002", Set("EQP-002", "category", "Hat")),
            ["equipment-slot-illegal"] = Err(ValidationChecks.EquipmentSlot, "EQP-051", Set("EQP-051", "slot", "Helmet")),
            ["equipment-slot-mismatch"] = Err(ValidationChecks.EquipmentSlot, "EQP-016", Set("EQP-016", "slot", "Main Focus or Sidearm")),
            ["equipment-tier-high"] = Err(ValidationChecks.EquipmentTier, "EQP-003", Set("EQP-003", "tier", 5)),
            ["equipment-tier-zero"] = Err(ValidationChecks.EquipmentTier, "EQP-003", Set("EQP-003", "tier", 0)),
            ["unique-group-empty"] = Err(ValidationChecks.UniqueGroup, "EQP-004", Set("EQP-004", "unique_group", "")),
            ["activation-unknown"] = Err(ValidationChecks.EquipmentActivation, "EQP-005", Set("EQP-005", "activation", "Telepathic input, 5 mana")),
            ["activation-consumable"] = Err(ValidationChecks.EquipmentActivation, "EQP-111", Set("EQP-111", "activation", "Passive")),

            // Augments and transformations
            ["augment-rarity"] = Err(ValidationChecks.AugmentRarity, "AUG-001", Set("AUG-001", "rarity", "Bronze")),
            ["augment-cost"] = Err(ValidationChecks.AugmentCost, "AUG-305", Set("AUG-305", "cost", -5)),
            ["shared-eligible-wizards"] = Warn(ValidationChecks.SharedAugments, "AUG-001", Set("AUG-001", "eligible_wizards", Strings("WIZ-001"))),
            ["transformation-foreign-spell"] = Err(ValidationChecks.Transformation, "AUG-305", j => EditRecord(j, "AUG-305", r => r.With("target_spell_ids", Strings("SPL-001")).With("raw_spell_id", "SPL-001"))),
            ["transformation-missing-target"] = Err(ValidationChecks.Transformation, "AUG-305", Set("AUG-305", "target_spell_ids", Strings("SPL-999"))),
            ["transformation-eligibility-lacks-wizard"] = Err(ValidationChecks.Transformation, "AUG-305", Eligibility("AUG-305", "{\"spell\":\"SPL-009\"}")),
            ["transformation-eligibility-lacks-spell"] = Err(ValidationChecks.Transformation, "AUG-305", Eligibility("AUG-305", "{\"wizard\":\"WIZ-002\"}")),
            ["transformation-two-wizards"] = Err(ValidationChecks.Transformation, "AUG-305", Set("AUG-305", "eligible_wizards", Strings("WIZ-002", "WIZ-003"))),
            ["transformation-two-targets"] = Err(ValidationChecks.Transformation, "AUG-305", Set("AUG-305", "target_spell_ids", Strings("SPL-009", "SPL-010"))),
            ["transformation-raw-spell-id"] = Err(ValidationChecks.Transformation, "AUG-305", Set("AUG-305", "raw_spell_id", "SPL-010")),
            ["transformation-spell-id"] = Err(ValidationChecks.Transformation, "AUG-577", Set("AUG-577", "spell_id", "SPL-146")),
            ["transformation-wizard-id"] = Err(ValidationChecks.Transformation, "AUG-577", Set("AUG-577", "wizard_id", "WIZ-020")),
            ["transformation-name"] = Warn(ValidationChecks.Transformation, "AUG-577", Set("AUG-577", "target_spell_name", "Wrong Name")),
            ["transformations-per-spell"] = Err(ValidationChecks.TransformationsPerSpell, "SPL-009", j => EditRecord(j, "AUG-305", r => r
                .With("target_spell_ids", Strings("SPL-010")).With("raw_spell_id", "SPL-010")
                .With("eligibility", Parse("{\"all\":[{\"wizard\":\"WIZ-002\"},{\"spell\":\"SPL-010\"}]}")))),
            ["transformation-rarity-split"] = Warn(ValidationChecks.TransformationRarity, "augments", Set("AUG-305", "rarity", "Gold")),
            ["shared-count"] = Err(ValidationChecks.SharedAugments, "augments", j => EditRecord(j, "AUG-001", r => r
                .With("target_spell_ids", Strings("SPL-001")).With("eligible_wizards", Strings("WIZ-001"))
                .With("eligibility", Parse("{\"all\":[{\"wizard\":\"WIZ-001\"},{\"spell\":\"SPL-001\"}]}")))),
            ["unconditional-pool"] = Warn(ValidationChecks.UnconditionalPool, "augments", Eligibility("AUG-012", "{\"round_at_least\":3}")),

            // Predicates
            ["reference-missing-augment"] = Err(ValidationChecks.PredicateReference, "AUG-001", Eligibility("AUG-001", "{\"augment\":{\"id\":\"AUG-999\",\"rank\":1}}")),
            ["reference-wrong-type"] = Err(ValidationChecks.PredicateReference, "AUG-001", Eligibility("AUG-001", "{\"wizard\":\"SPL-001\"}")),
            ["reference-missing-feat"] = Err(ValidationChecks.PredicateReference, "AUG-001", Eligibility("AUG-001", "{\"feat\":{\"id\":\"FEAT-099\",\"at_least\":1}}")),
            ["reference-missing-equipment"] = Err(ValidationChecks.PredicateReference, "AUG-001", Eligibility("AUG-001", "{\"any\":[{\"equipped_item\":\"EQP-121\"},{\"owned_item\":\"EQP-001\"}]}")),
            ["reference-missing-spell"] = Err(ValidationChecks.PredicateReference, "AUG-001", Eligibility("AUG-001", "{\"not\":{\"spell\":\"SPL-300\"}}")),
            ["reference-missing-in-id-list"] = Err(ValidationChecks.PredicateReference, "AUG-001", Eligibility("AUG-001", "{\"augment_family_count\":{\"count\":1,\"ids\":[\"AUG-002\",\"AUG-900\"]}}")),
            ["reference-missing-wizard"] = Err(ValidationChecks.PredicateReference, "AUG-001", Eligibility("AUG-001", "{\"wizard\":\"WIZ-037\"}")),
            ["reference-in-build"] = Err(ValidationChecks.PredicateReference, "BLD-037", Eligibility("BLD-037", "{\"augment\":{\"id\":\"AUG-999\",\"rank\":1}}")),
            ["self-reference"] = Err(ValidationChecks.SelfReference, "AUG-022", Eligibility("AUG-022", "{\"all\":[{\"augment\":{\"id\":\"AUG-022\",\"rank\":1}},{\"always\":true}]}")),
            ["dependency-cycle"] = Err(ValidationChecks.DependencyCycle, null, j => EditRecord(
                EditRecord(j, "AUG-001", "eligibility", Parse("{\"augment\":{\"id\":\"AUG-003\",\"rank\":1}}")),
                "AUG-003", "eligibility", Parse("{\"augment_tag_count\":{\"tag\":\"mastery\",\"count\":1,\"ids\":[\"AUG-001\"]}}"))),
            ["parent-rarity"] = Err(ValidationChecks.ParentRarity, "AUG-001", Eligibility("AUG-001", "{\"all\":[{\"augment\":{\"id\":\"AUG-020\",\"rank\":1}}]}")),
            ["predicate-rank"] = Err(ValidationChecks.PredicateValue, "AUG-022", Eligibility("AUG-022", "{\"augment\":{\"id\":\"AUG-001\",\"rank\":3}}")),
            ["predicate-rank-count"] = Err(ValidationChecks.PredicateValue, "AUG-022", Eligibility("AUG-022", "{\"augment_rank_count\":{\"rank_at_least\":3,\"count\":1}}")),
            ["predicate-main-focus"] = Err(ValidationChecks.PredicateValue, "AUG-001", Eligibility("AUG-001", "{\"main_focus_category\":\"Armor\"}")),
            ["predicate-sidearm"] = Err(ValidationChecks.PredicateValue, "AUG-001", Eligibility("AUG-001", "{\"sidearm_category\":\"Staff\"}")),
            ["predicate-issued-slot"] = Err(ValidationChecks.PredicateValue, "AUG-001", Eligibility("AUG-001", "{\"issued_slot\":\"Trinket A\"}")),
            ["predicate-slot-tier"] = Err(ValidationChecks.PredicateValue, "AUG-001", Eligibility("AUG-001", "{\"slot_tier\":{\"slot\":\"Armor\",\"at_least\":5}}")),
            ["predicate-category-capacity"] = Err(ValidationChecks.PredicateValue, "AUG-001", Eligibility("AUG-001", "{\"equipped_category\":{\"category\":\"Armor\",\"count\":2}}")),
            ["predicate-spell-slots"] = Err(ValidationChecks.PredicateValue, "AUG-001", Eligibility("AUG-001", "{\"spell_tag\":{\"tag\":\"projectile\",\"count\":7}}")),
            ["predicate-distinct-tags"] = Err(ValidationChecks.PredicateValue, "AUG-001", Eligibility("AUG-001", "{\"spell_distinct_tags\":{\"set\":[\"ray\",\"cone\"],\"count\":3}}")),
            ["predicate-feat-threshold"] = Err(ValidationChecks.PredicateValue, "AUG-001", Eligibility("AUG-001", "{\"feat\":{\"id\":\"FEAT-001\",\"at_least\":0}}")),
            ["predicate-zero-count"] = Warn(ValidationChecks.PredicateValue, "AUG-001", Eligibility("AUG-001", "{\"equipped_tag\":{\"tag\":\"movement\",\"count\":0}}")),

            // Exclusive groups
            ["exclusive-group-mixed-spells"] = Err(ValidationChecks.ExclusiveGroup, "AUG-289", Set("AUG-305", "exclusive_group", "SPL-001-delivery")),
            ["exclusive-group-mixed-kinds"] = Err(ValidationChecks.ExclusiveGroup, null, Set("AUG-001", "exclusive_group", "SPL-009-delivery")),
            ["exclusive-group-single"] = Warn(ValidationChecks.ExclusiveGroup, "AUG-002", Set("AUG-002", "exclusive_group", "lonely")),

            // Builds
            ["build-wizard"] = Err(ValidationChecks.Build, "BLD-001", Set("BLD-001", "wizard_id", "WIZ-099")),
            ["build-foreign-spell"] = Err(ValidationChecks.Build, "BLD-001", EditStrings("BLD-001", "equipped_spell_ids", l => { l[5] = "SPL-009"; return l; })),
            ["build-too-many-spells"] = Err(ValidationChecks.Build, "BLD-001", EditStrings("BLD-001", "equipped_spell_ids", l => l.Append("SPL-007"))),
            ["build-repeated-spell"] = Err(ValidationChecks.Build, "BLD-001", EditStrings("BLD-001", "equipped_spell_ids", l => { l[5] = l[4]; return l; })),
            ["build-missing-equipment"] = Err(ValidationChecks.Build, "BLD-001", EditStrings("BLD-001", "equipment_ids", l => l.Append("EQP-999"))),
            ["build-two-armor"] = Err(ValidationChecks.Build, "BLD-001", EditStrings("BLD-001", "equipment_ids", l => l.Append("EQP-052"))),
            ["build-three-foci"] = Err(ValidationChecks.Build, "BLD-001", EditStrings("BLD-001", "equipment_ids", l => l.Concat(new[] { "EQP-002", "EQP-031" }))),
            ["build-missing-augment"] = Err(ValidationChecks.Build, "BLD-001", EditStrings("BLD-001", "augment_ids", l => { l[5] = "AUG-999"; return l; })),
            ["build-too-many-augments"] = Err(ValidationChecks.Build, "BLD-001", EditStrings("BLD-001", "augment_ids", l => l.Append("AUG-001"))),
            ["build-foreign-transformation"] = Err(ValidationChecks.Build, "BLD-001", EditStrings("BLD-001", "augment_ids", l => l.Select(a => a == "AUG-289" ? "AUG-305" : a))),
            ["build-transformation-unequipped"] = Err(ValidationChecks.Build, "BLD-001", EditStrings("BLD-001", "augment_ids", l => l.Select(a => a == "AUG-289" ? "AUG-302" : a))),
            ["build-rank-out-of-range"] = Err(ValidationChecks.Build, "BLD-048", Set("BLD-048", "augment_ranks", Parse("{\"AUG-078\":3}"))),
            ["build-rank-unlisted"] = Err(ValidationChecks.Build, "BLD-048", Set("BLD-048", "augment_ranks", Parse("{\"AUG-001\":2}"))),
            ["build-cost"] = Warn(ValidationChecks.BuildPlan, "BLD-001", Set("BLD-001", "equipment_cost", 1300)),
            ["build-plan-order"] = Warn(ValidationChecks.BuildPlan, "BLD-002", Set("BLD-002", "wizard_id", "WIZ-001")),

            // Arena and counterplay
            ["biome-size"] = Err(ValidationChecks.ArenaFields, "BIO-001", Set("BIO-001", "sizes", JsonValue.Array(24, 30))),
            ["module-footprint"] = Err(ValidationChecks.ArenaFields, "MOD-001", Set("MOD-001", "footprint", "big")),
            ["module-category"] = Err(ValidationChecks.ArenaFields, "MOD-002", Set("MOD-002", "category", "Roof")),
            ["prop-mass"] = Err(ValidationChecks.ArenaFields, "PROP-001", Set("PROP-001", "mass", "huge")),
            ["prop-category"] = Err(ValidationChecks.ArenaFields, "PROP-002", Set("PROP-002", "category", "Magic")),
            ["prop-durability"] = Err(ValidationChecks.NumericRange, "PROP-003", Set("PROP-003", "durability", 0)),
            ["counterplay-spell"] = Err(ValidationChecks.Counterplay, "SPL-001", Set("SPL-001", "counterplay", "")),
            ["counterplay-equipment"] = Err(ValidationChecks.Counterplay, "EQP-010", Set("EQP-010", "counterplay", "   ")),
        };

        public static IEnumerable<object[]> CaseNames() => Cases.Keys.Select(k => new object[] { k });

        [Theory]
        [MemberData(nameof(CaseNames))]
        public void BrokenCatalogueTriggersItsCheck(string name)
        {
            var c = Cases[name];
            var catalogue = Catalogue.FromJson(c.Mutate(Source));
            var issues = CatalogueValidator.Validate(catalogue);
            bool found = issues.Any(i => i.Severity == c.Severity && i.Check == c.Check && (c.Id == null || i.Id == c.Id));
            if (!found)
                foreach (var issue in issues) _output.WriteLine(issue.ToString());
            Assert.True(found, name + ": expected " + c.Severity + " [" + c.Check + "] " + (c.Id ?? "(any)"));
        }

        [Fact]
        public void EveryCheckCodeIsExercisedBySyntheticCases()
        {
            var codes = typeof(ValidationChecks).GetFields().Select(f => (string)f.GetValue(null)).ToList();
            var exercised = Cases.Values.Select(c => c.Check).Distinct().ToList();
            // compound-predicates is exercised against the supplied catalogue above.
            Assert.Empty(codes.Except(exercised).Except(new[] { ValidationChecks.CompoundPredicates }));
        }

        [Fact]
        public void ReportsCyclePath()
        {
            var json = EditRecord(EditRecord(Source, "AUG-001", "eligibility", Parse("{\"augment\":{\"id\":\"AUG-003\",\"rank\":1}}")),
                "AUG-003", "eligibility", Parse("{\"augment\":{\"id\":\"AUG-001\",\"rank\":1}}"));
            var cycle = Assert.Single(CatalogueValidator.Validate(Catalogue.FromJson(json)), i => i.Check == ValidationChecks.DependencyCycle);
            Assert.Contains("AUG-001 -> AUG-003 -> AUG-001", cycle.Message);
        }
    }
}
