using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Content;
using Confluence.Core.Json;
using Confluence.Core.Rules;

namespace Confluence.Core.Tests.Content
{
    public class PredicateTests
    {
        private const string Owner = "AUG-TEST";

        private static Predicate P(string json) => PredicateParser.Parse(json, Owner);

        // One JSON example per grammar node/leaf, in the exact shapes the catalogue uses (plus owned_item, which the
        // grammar defines but the supplied catalogue never uses, and the at_least form of spell_cooldown_count).
        public static readonly IReadOnlyList<(PredicateKind Kind, string Json)> Examples = new[]
        {
            (PredicateKind.All, "{\"all\":[{\"wizard\":\"WIZ-002\"},{\"spell\":\"SPL-009\"}]}"),
            (PredicateKind.Any, "{\"any\":[{\"wizard\":\"WIZ-004\"}]}"),
            (PredicateKind.Not, "{\"not\":{\"equipped_tag\":{\"tag\":\"active_equipment\",\"count\":1}}}"),
            (PredicateKind.Always, "{\"always\":true}"),
            (PredicateKind.Wizard, "{\"wizard\":\"WIZ-002\"}"),
            (PredicateKind.EquippedItem, "{\"equipped_item\":\"EQP-048\"}"),
            (PredicateKind.OwnedItem, "{\"owned_item\":\"EQP-112\"}"),
            (PredicateKind.EquippedTag, "{\"equipped_tag\":{\"tag\":\"movement\",\"count\":2}}"),
            (PredicateKind.Augment, "{\"augment\":{\"id\":\"AUG-242\",\"rank\":1}}"),
            (PredicateKind.Spell, "{\"spell\":\"SPL-009\"}"),
            (PredicateKind.SpellTag, "{\"spell_tag\":{\"tag\":\"projectile\",\"count\":1}}"),
            (PredicateKind.SpellTag, "{\"spell_tag\":{\"tag\":\"field\",\"count\":2,\"also\":[\"damaging\"]}}"),
            (PredicateKind.SpellTagExact, "{\"spell_tag_exact\":{\"tag\":\"summon\",\"count\":1}}"),
            (PredicateKind.SpellDistinctTags, "{\"spell_distinct_tags\":{\"set\":[\"projectile\",\"melee\",\"field\"],\"count\":3}}"),
            (PredicateKind.SpellDistinctTags, "{\"spell_distinct_tags\":{\"set\":[\"projectile\",\"cone\"],\"count\":2,\"also\":[\"damaging\"]}}"),
            (PredicateKind.SpellCooldownCount, "{\"spell_cooldown_count\":{\"at_most\":6,\"count\":3}}"),
            (PredicateKind.SpellCooldownCount, "{\"spell_cooldown_count\":{\"at_least\":10,\"count\":1}}"),
            (PredicateKind.SpellCastTimeCount, "{\"spell_cast_time_count\":{\"at_least\":0.4,\"count\":1}}"),
            (PredicateKind.SpellCastTimeCount, "{\"spell_cast_time_count\":{\"at_most\":0.3,\"count\":1}}"),
            (PredicateKind.MainFocusCategory, "{\"main_focus_category\":\"Weapon\"}"),
            (PredicateKind.SidearmCategory, "{\"sidearm_category\":\"Weapon\"}"),
            (PredicateKind.IssuedSlot, "{\"issued_slot\":\"Armor\"}"),
            (PredicateKind.SlotTier, "{\"slot_tier\":{\"slot\":\"Armor\",\"at_least\":3}}"),
            (PredicateKind.EquippedCategory, "{\"equipped_category\":{\"category\":\"Trinket\",\"count\":2,\"distinct_unique_groups\":true}}"),
            (PredicateKind.ConsumableChargesAtMost, "{\"consumable_charges_at_most\":0}"),
            (PredicateKind.AugmentTagCount, "{\"augment_tag_count\":{\"tag\":\"reaction\",\"count\":2,\"ids\":[\"AUG-121\",\"AUG-122\"]}}"),
            (PredicateKind.AugmentRankCount, "{\"augment_rank_count\":{\"rank_at_least\":2,\"count\":2,\"distinct_families\":true,\"ids\":[\"AUG-001\",\"AUG-025\"]}}"),
            (PredicateKind.AugmentFamilyCount, "{\"augment_family_count\":{\"count\":3,\"ids\":[\"AUG-001\",\"AUG-025\",\"AUG-049\"]}}"),
            (PredicateKind.RoundAtLeast, "{\"round_at_least\":5}"),
            (PredicateKind.Feat, "{\"feat\":{\"id\":\"FEAT-020\",\"at_least\":1}}"),
        };

        public static IEnumerable<object[]> ExampleData() => Examples.Select(e => new object[] { e.Kind, e.Json });

        [Theory]
        [MemberData(nameof(ExampleData))]
        public void ParsesEveryNodeAndLeafAndRoundTrips(PredicateKind kind, string json)
        {
            var predicate = P(json);
            Assert.Equal(kind, predicate.Kind);
            Assert.Equal(PredicateKinds.JsonKey(kind), predicate.JsonKey);
            Assert.Equal(JsonParser.Parse(json), predicate.ToJson());
            Assert.Equal(predicate.ToJson(), P(predicate.ToString()).ToJson());
            Assert.Equal(kind.ToString(), predicate.Accept(new KindNameVisitor()));
            Assert.False(string.IsNullOrWhiteSpace(predicate.Describe()));
        }

        [Fact]
        public void ExamplesCoverTheWholeGrammar()
        {
            var covered = Examples.Select(e => e.Kind).Distinct().OrderBy(k => k).ToList();
            Assert.Equal(Enum.GetValues(typeof(PredicateKind)).Cast<PredicateKind>().OrderBy(k => k), covered);
            Assert.Equal(26, PredicateKinds.AllKeys.Count);
            foreach (PredicateKind kind in Enum.GetValues(typeof(PredicateKind)))
            {
                Assert.True(PredicateKinds.TryParseKey(PredicateKinds.JsonKey(kind), out var back));
                Assert.Equal(kind, back);
            }
        }

        [Fact]
        public void LeafFieldsAreTyped()
        {
            var tag = Assert.IsType<SpellTagLeaf>(P("{\"spell_tag\":{\"tag\":\"field\",\"count\":2,\"also\":[\"damaging\",\"ranged\"]}}"));
            Assert.Equal("field", tag.Tag);
            Assert.Equal(2, tag.Count);
            Assert.Equal(new[] { "damaging", "ranged" }, tag.Also);
            Assert.True(tag.HasAlsoField);
            Assert.False(Assert.IsType<SpellTagLeaf>(P("{\"spell_tag\":{\"tag\":\"x\",\"count\":1}}")).HasAlsoField);

            var cast = Assert.IsType<SpellCastTimeCountLeaf>(P("{\"spell_cast_time_count\":{\"at_least\":0.4,\"count\":3}}"));
            Assert.Equal(BoundKind.AtLeast, cast.Threshold.Bound);
            Assert.Equal(0.4, cast.Threshold.Value);
            Assert.True(cast.Threshold.Includes(0.4));
            Assert.False(cast.Threshold.Includes(0.39));
            Assert.Equal(3, cast.Count);

            var cooldown = Assert.IsType<SpellCooldownCountLeaf>(P("{\"spell_cooldown_count\":{\"at_most\":6,\"count\":3}}"));
            Assert.Equal(BoundKind.AtMost, cooldown.Threshold.Bound);
            Assert.True(cooldown.Threshold.Includes(6));
            Assert.False(cooldown.Threshold.Includes(6.01));

            var slot = Assert.IsType<SlotTierLeaf>(P("{\"slot_tier\":{\"slot\":\"Trinket B\",\"at_least\":2}}"));
            Assert.Equal(EquipmentSlot.TrinketB, slot.Slot);
            Assert.Equal(2, slot.AtLeast);

            var category = Assert.IsType<EquippedCategoryLeaf>(P("{\"equipped_category\":{\"category\":\"Magical clothing\",\"count\":1}}"));
            Assert.Equal(EquipmentCategory.MagicalClothing, category.Category);
            Assert.False(category.DistinctUniqueGroups);
            Assert.False(category.HasDistinctField);

            var focus = Assert.IsType<MainFocusCategoryLeaf>(P("{\"main_focus_category\":\"Staff\"}"));
            Assert.Equal(EquipmentCategory.Staff, focus.Category);
            Assert.Equal(EquipmentSlot.Armor, Assert.IsType<IssuedSlotLeaf>(P("{\"issued_slot\":\"Armor\"}")).Slot);

            var rank = Assert.IsType<AugmentRankCountLeaf>(P("{\"augment_rank_count\":{\"rank_at_least\":2,\"count\":2,\"distinct_families\":true,\"ids\":[\"AUG-001\"]}}"));
            Assert.Equal(2, rank.RankAtLeast);
            Assert.True(rank.DistinctFamilies);
            Assert.True(rank.IsRestricted);
            Assert.Equal(new[] { "AUG-001" }, rank.Ids);
            var unrestricted = Assert.IsType<AugmentFamilyCountLeaf>(P("{\"augment_family_count\":{\"count\":2}}"));
            Assert.False(unrestricted.IsRestricted);
            Assert.Empty(unrestricted.Ids);

            var feat = Assert.IsType<FeatLeaf>(P("{\"feat\":{\"id\":\"FEAT-022\",\"at_least\":2}}"));
            Assert.Equal("FEAT-022", feat.FeatId);
            Assert.Equal(2, feat.AtLeast);
            Assert.Equal(0, Assert.IsType<ConsumableChargesAtMostLeaf>(P("{\"consumable_charges_at_most\":0}")).AtMost);
            Assert.Equal(9, Assert.IsType<RoundAtLeastLeaf>(P("{\"round_at_least\":9}")).Round);
            Assert.Same(AlwaysLeaf.Instance, P("{\"always\":true}"));
            var not = Assert.IsType<NotNode>(P("{\"not\":{\"wizard\":\"WIZ-001\"}}"));
            Assert.Equal("WIZ-001", Assert.IsType<WizardLeaf>(not.Child).WizardId);
            Assert.Single(not.Children);
        }

        [Theory]
        [InlineData("{\"spel\":\"SPL-001\"}", "$", "unknown predicate node 'spel'")]
        [InlineData("{\"spell_tag\":{\"tag\":\"x\",\"count\":1,\"bogus\":2}}", "$.spell_tag", "unknown field 'bogus'")]
        [InlineData("{\"spell_tag\":{\"tag\":\"x\"}}", "$.spell_tag", "missing required field 'count'")]
        [InlineData("{\"wizard\":\"WIZ-001\",\"spell\":\"SPL-001\"}", "$", "exactly one key")]
        [InlineData("{}", "$", "exactly one key")]
        [InlineData("{\"all\":[]}", "$.all", "at least one child")]
        [InlineData("{\"any\":{\"always\":true}}", "$.any", "expected an array of predicates")]
        [InlineData("{\"not\":[{\"always\":true}]}", "$.not", "'not' takes one predicate object")]
        [InlineData("{\"always\":false}", "$.always", "'always' must be true")]
        [InlineData("{\"equipped_tag\":{\"tag\":\"x\",\"count\":-1}}", "$.equipped_tag.count", "non-negative integer")]
        [InlineData("{\"equipped_tag\":{\"tag\":\"x\",\"count\":1.5}}", "$.equipped_tag.count", "non-negative integer")]
        [InlineData("{\"spell_cast_time_count\":{\"count\":1,\"at_least\":0.4,\"at_most\":1}}", "$.spell_cast_time_count", "exactly one of 'at_least' or 'at_most'")]
        [InlineData("{\"spell_cooldown_count\":{\"count\":1}}", "$.spell_cooldown_count", "exactly one of 'at_least' or 'at_most'")]
        [InlineData("{\"main_focus_category\":\"Hat\"}", "$.main_focus_category", "unknown equipment category 'Hat'")]
        [InlineData("{\"slot_tier\":{\"slot\":\"Head\",\"at_least\":2}}", "$.slot_tier.slot", "unknown equipment slot 'Head'")]
        [InlineData("{\"wizard\":5}", "$.wizard", "expected a non-empty string")]
        [InlineData("{\"wizard\":\"\"}", "$.wizard", "expected a non-empty string")]
        [InlineData("[{\"always\":true}]", "$", "must be an object")]
        [InlineData("{\"augment_tag_count\":{\"tag\":\"x\",\"count\":1,\"ids\":\"AUG-001\"}}", "$.augment_tag_count.ids", "expected an array of strings")]
        [InlineData("{\"equipped_category\":{\"category\":\"Trinket\",\"count\":2,\"distinct_unique_groups\":\"yes\"}}", "$.equipped_category.distinct_unique_groups", "expected a boolean")]
        [InlineData("{\"all\":[{\"always\":true},{\"any\":[{\"feat\":{\"id\":\"FEAT-001\"}}]}]}", "$.all[1].any[0].feat", "missing required field 'at_least'")]
        [InlineData("{\"round_at_least\":\"5\"}", "$.round_at_least", "non-negative integer")]
        [InlineData("{\"augment\":{\"id\":\"AUG-001\",\"rank\":1,\"family\":\"x\"}}", "$.augment", "unknown field 'family'")]
        public void RejectsMalformedPredicatesNamingOwnerAndPath(string json, string path, string reason)
        {
            var e = Assert.Throws<PredicateFormatException>(() => P(json));
            Assert.Equal(Owner, e.OwnerId);
            Assert.Equal(path, e.Path);
            Assert.Contains(reason, e.Reason);
            Assert.StartsWith(Owner + " eligibility at " + path + ": ", e.Message);
        }

        [Fact]
        public void RejectsExcessiveNesting()
        {
            string json = "{\"always\":true}";
            for (int i = 0; i < PredicateParser.MaxDepth + 2; i++) json = "{\"not\":" + json + "}";
            Assert.Throws<PredicateFormatException>(() => P(json));
        }

        [Fact]
        public void ClassifiesAcquisitionOnlyAndOngoingLeaves()
        {
            // spec L247, L602: wizard identity, round, and completed-duel feats are acquisition-only.
            var acquisition = new[] { PredicateKind.Wizard, PredicateKind.RoundAtLeast, PredicateKind.Feat, PredicateKind.Always };
            foreach (PredicateKind kind in Enum.GetValues(typeof(PredicateKind)))
            {
                if (PredicateKinds.IsComposite(kind))
                {
                    Assert.Contains(kind, new[] { PredicateKind.All, PredicateKind.Any, PredicateKind.Not });
                    Assert.False(PredicateKinds.IsOngoing(kind));
                    continue;
                }
                Assert.Equal(acquisition.Contains(kind), PredicateKinds.IsAcquisitionOnly(kind));
                Assert.Equal(!acquisition.Contains(kind), PredicateKinds.IsOngoing(kind));
            }

            Assert.Equal(PredicateTiming.AcquisitionOnly, P("{\"wizard\":\"WIZ-001\"}").Timing);
            Assert.Equal(PredicateTiming.Ongoing, P("{\"spell\":\"SPL-001\"}").Timing);
            Assert.Equal(PredicateTiming.Ongoing, P("{\"equipped_item\":\"EQP-001\"}").Timing);
            Assert.Equal(PredicateTiming.Ongoing, P("{\"augment\":{\"id\":\"AUG-001\",\"rank\":1}}").Timing);
            Assert.Equal(PredicateTiming.Mixed, TestCatalogue.Real.Augment("AUG-305").Eligibility.Timing);
            Assert.Equal(PredicateTiming.Ongoing, TestCatalogue.Real.Augment("AUG-001").Eligibility.Timing);
            Assert.Equal(PredicateTiming.AcquisitionOnly, TestCatalogue.Real.Augment("AUG-002").Eligibility.Timing);
            Assert.Equal(PredicateTiming.AcquisitionOnly, AlwaysLeaf.Instance.Timing);
            Assert.True(P("{\"not\":{\"equipped_tag\":{\"tag\":\"x\",\"count\":1}}}").HasOngoingConditions);
            Assert.False(P("{\"any\":[{\"wizard\":\"WIZ-001\"},{\"feat\":{\"id\":\"FEAT-001\",\"at_least\":1}}]}").HasOngoingConditions);
        }

        [Fact]
        public void CollectsReferencedIdsInOrderWithoutDuplicates()
        {
            Assert.Equal(new[] { "WIZ-002", "SPL-009" }, TestCatalogue.Real.Augment("AUG-305").Eligibility.CollectReferencedIds());
            var p = P("{\"all\":[{\"augment\":{\"id\":\"AUG-022\",\"rank\":1}},{\"any\":[{\"feat\":{\"id\":\"FEAT-020\",\"at_least\":1}},"
                + "{\"augment_tag_count\":{\"tag\":\"x\",\"count\":1,\"ids\":[\"AUG-022\",\"AUG-023\"]}}]},{\"not\":{\"equipped_item\":\"EQP-051\"}},"
                + "{\"owned_item\":\"EQP-112\"},{\"spell_tag\":{\"tag\":\"projectile\",\"count\":2}}]}");
            Assert.Equal(new[] { "AUG-022", "FEAT-020", "AUG-023", "EQP-051", "EQP-112" }, p.CollectReferencedIds());
            Assert.Empty(AlwaysLeaf.Instance.CollectReferencedIds());
        }

        [Fact]
        public void DescribesPredicatesInPlainLanguage()
        {
            var cat = TestCatalogue.Real;
            Assert.Equal("Playing Bram Cinder (WIZ-002) and Fireball (SPL-009) in your exclusive kit",
                cat.DescribeEligibility(cat.Augment("AUG-305").Eligibility));
            Assert.Equal("At least 1 equipped spell tagged 'projectile'", cat.Augment("AUG-001").Eligibility.Describe());
            Assert.Equal("No prerequisite", AlwaysLeaf.Instance.Describe());
            Assert.Equal("Playing WIZ-002", P("{\"wizard\":\"WIZ-002\"}").Describe());
            Assert.Equal("No equipped item supplying the 'active_equipment' tag",
                P("{\"not\":{\"equipped_tag\":{\"tag\":\"active_equipment\",\"count\":1}}}").Describe());
            Assert.Equal("A weapon in the Main Focus slot and tier 3 or higher equipment in the Armor slot",
                P("{\"all\":[{\"main_focus_category\":\"Weapon\"},{\"slot_tier\":{\"slot\":\"Armor\",\"at_least\":3}}]}").Describe());
            Assert.Equal("At least 2 trinket items equipped with different Unique groups and no purchased consumable charges held",
                P("{\"all\":[{\"equipped_category\":{\"category\":\"Trinket\",\"count\":2,\"distinct_unique_groups\":true}},{\"consumable_charges_at_most\":0}]}").Describe());
            Assert.Equal("Feat FEAT-020 (Deal 30 damage through bounded conductivity reactions) completed in a previous duel",
                P("{\"feat\":{\"id\":\"FEAT-020\",\"at_least\":1}}").Describe());
            Assert.Equal("Round 5 or later or (playing WIZ-001 and not EQP-051 equipped)",
                P("{\"any\":[{\"round_at_least\":5},{\"all\":[{\"wizard\":\"WIZ-001\"},{\"not\":{\"equipped_item\":\"EQP-051\"}}]}]}").Describe());
            Assert.Equal("At least 1 equipped spell with intrinsic windup at least 0.4 s",
                P("{\"spell_cast_time_count\":{\"at_least\":0.4,\"count\":1}}").Describe());
            Assert.Equal("At least 2 active augments at rank 2 or higher from different families from 2 listed parents",
                P("{\"augment_rank_count\":{\"rank_at_least\":2,\"count\":2,\"distinct_families\":true,\"ids\":[\"AUG-001\",\"AUG-025\"]}}").Describe());

            foreach (var augment in cat.Augments)
            {
                string text = cat.DescribeEligibility(augment.Eligibility);
                Assert.False(string.IsNullOrWhiteSpace(text), augment.Id);
                Assert.DoesNotContain("{", text);
                Assert.True(char.IsUpper(text.TrimStart('(')[0]), augment.Id + ": " + text);
            }
        }

        [Fact]
        public void CatalogueUsesEveryLeafExceptOwnedItem()
        {
            var used = new HashSet<PredicateKind>();
            foreach (var augment in TestCatalogue.Real.Augments)
                foreach (var node in augment.Eligibility.DescendantsAndSelf())
                    used.Add(node.Kind);
            foreach (var build in TestCatalogue.Real.Builds.Where(b => b.Eligibility != null))
                foreach (var node in build.Eligibility.DescendantsAndSelf())
                    used.Add(node.Kind);
            var unused = Enum.GetValues(typeof(PredicateKind)).Cast<PredicateKind>().Where(k => !used.Contains(k)).ToList();
            Assert.Equal(new[] { PredicateKind.OwnedItem }, unused);
        }

        [Fact]
        public void ConstructorsValidateArguments()
        {
            Assert.Throws<ArgumentException>(() => new AllNode(new Predicate[0]));
            Assert.Throws<ArgumentNullException>(() => new NotNode(null));
            Assert.Throws<ArgumentException>(() => new WizardLeaf(""));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EquippedTagLeaf("x", -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Threshold(BoundKind.AtLeast, double.NaN));
            var built = new AllNode(new Predicate[] { new WizardLeaf("WIZ-002"), new SpellLeaf("SPL-009") });
            Assert.Equal(TestCatalogue.Real.Augment("AUG-305").EligibilityJson, built.ToJson());
        }

        [Fact]
        public void ReferenceEvaluatorReadsTheGrammarAsSpecified()
        {
            var cat = TestCatalogue.Real;
            var loadout = new TestLoadout { WizardId = "WIZ-002", KitSpellIds = new HashSet<string>(cat.Wizard("WIZ-002").SpellIds) };
            foreach (var id in cat.Wizard("WIZ-002").DefaultSpellIds)
            {
                var s = cat.Spell(id);
                loadout.EquippedSpells.Add(new TestSpell { Id = id, Tags = s.Tags.ToArray(), Cooldown = s.Cooldown, CastTime = s.CastTime });
            }

            Assert.True(ReferenceEvaluator.Evaluate(cat.Augment("AUG-305").Eligibility, loadout));
            Assert.True(ReferenceEvaluator.Evaluate(cat.Augment("AUG-001").Eligibility, loadout));
            loadout.WizardId = "WIZ-003";
            Assert.False(ReferenceEvaluator.Evaluate(cat.Augment("AUG-305").Eligibility, loadout));

            // Inclusive thresholds (spec L635).
            loadout.EquippedSpells = new List<TestSpell> { new TestSpell { Id = "x", CastTime = 0.4, Cooldown = 6 } };
            Assert.True(ReferenceEvaluator.Evaluate(P("{\"spell_cast_time_count\":{\"at_least\":0.4,\"count\":1}}"), loadout));
            Assert.True(ReferenceEvaluator.Evaluate(P("{\"spell_cooldown_count\":{\"at_most\":6,\"count\":1}}"), loadout));
            Assert.False(ReferenceEvaluator.Evaluate(P("{\"spell_cast_time_count\":{\"at_most\":0.3,\"count\":1}}"), loadout));

            // Distinct Unique groups: two trinkets with one shared group do not qualify (spec L585, L635).
            var trinkets = P("{\"equipped_category\":{\"category\":\"Trinket\",\"count\":2,\"distinct_unique_groups\":true}}");
            loadout.Equipped = new List<TestItem>
            {
                new TestItem { Id = "EQP-096", Slot = EquipmentSlot.TrinketA, Category = EquipmentCategory.Trinket, UniqueGroup = "g1" },
                new TestItem { Id = "EQP-097", Slot = EquipmentSlot.TrinketB, Category = EquipmentCategory.Trinket, UniqueGroup = "g1" },
            };
            Assert.False(ReferenceEvaluator.Evaluate(trinkets, loadout));
            loadout.Equipped[1].UniqueGroup = "g2";
            Assert.True(ReferenceEvaluator.Evaluate(trinkets, loadout));
            Assert.True(ReferenceEvaluator.Evaluate(P("{\"issued_slot\":\"Armor\"}"), loadout));
            var noCharges = P("{\"consumable_charges_at_most\":0}");
            Assert.True(ReferenceEvaluator.Evaluate(noCharges, loadout));
            loadout.ConsumableCharges = 1;
            Assert.False(ReferenceEvaluator.Evaluate(noCharges, loadout));

            // Family counts count distinct families, and only enumerated parents (spec L635).
            loadout.ActiveAugments["AUG-001"] = new TestAugment { Rank = 2, Family = "Casting mastery" };
            loadout.ActiveAugments["AUG-002"] = new TestAugment { Rank = 2, Family = "Casting mastery" };
            loadout.ActiveAugments["AUG-025"] = new TestAugment { Rank = 1, Family = "Projectile and beam shaping" };
            Assert.False(ReferenceEvaluator.Evaluate(P("{\"augment_rank_count\":{\"rank_at_least\":2,\"count\":2,\"distinct_families\":true,\"ids\":[\"AUG-001\",\"AUG-002\",\"AUG-025\"]}}"), loadout));
            Assert.True(ReferenceEvaluator.Evaluate(P("{\"augment_family_count\":{\"count\":2,\"ids\":[\"AUG-001\",\"AUG-025\"]}}"), loadout));
            Assert.False(ReferenceEvaluator.Evaluate(P("{\"augment_family_count\":{\"count\":2,\"ids\":[\"AUG-001\",\"AUG-002\"]}}"), loadout));
        }

        private sealed class KindNameVisitor : IPredicateVisitor<string>
        {
            public string Visit(AllNode node) => nameof(PredicateKind.All);
            public string Visit(AnyNode node) => nameof(PredicateKind.Any);
            public string Visit(NotNode node) => nameof(PredicateKind.Not);
            public string Visit(AlwaysLeaf leaf) => nameof(PredicateKind.Always);
            public string Visit(WizardLeaf leaf) => nameof(PredicateKind.Wizard);
            public string Visit(EquippedItemLeaf leaf) => nameof(PredicateKind.EquippedItem);
            public string Visit(OwnedItemLeaf leaf) => nameof(PredicateKind.OwnedItem);
            public string Visit(EquippedTagLeaf leaf) => nameof(PredicateKind.EquippedTag);
            public string Visit(AugmentLeaf leaf) => nameof(PredicateKind.Augment);
            public string Visit(SpellLeaf leaf) => nameof(PredicateKind.Spell);
            public string Visit(SpellTagLeaf leaf) => nameof(PredicateKind.SpellTag);
            public string Visit(SpellTagExactLeaf leaf) => nameof(PredicateKind.SpellTagExact);
            public string Visit(SpellDistinctTagsLeaf leaf) => nameof(PredicateKind.SpellDistinctTags);
            public string Visit(SpellCooldownCountLeaf leaf) => nameof(PredicateKind.SpellCooldownCount);
            public string Visit(SpellCastTimeCountLeaf leaf) => nameof(PredicateKind.SpellCastTimeCount);
            public string Visit(MainFocusCategoryLeaf leaf) => nameof(PredicateKind.MainFocusCategory);
            public string Visit(SidearmCategoryLeaf leaf) => nameof(PredicateKind.SidearmCategory);
            public string Visit(IssuedSlotLeaf leaf) => nameof(PredicateKind.IssuedSlot);
            public string Visit(SlotTierLeaf leaf) => nameof(PredicateKind.SlotTier);
            public string Visit(EquippedCategoryLeaf leaf) => nameof(PredicateKind.EquippedCategory);
            public string Visit(ConsumableChargesAtMostLeaf leaf) => nameof(PredicateKind.ConsumableChargesAtMost);
            public string Visit(AugmentTagCountLeaf leaf) => nameof(PredicateKind.AugmentTagCount);
            public string Visit(AugmentRankCountLeaf leaf) => nameof(PredicateKind.AugmentRankCount);
            public string Visit(AugmentFamilyCountLeaf leaf) => nameof(PredicateKind.AugmentFamilyCount);
            public string Visit(RoundAtLeastLeaf leaf) => nameof(PredicateKind.RoundAtLeast);
            public string Visit(FeatLeaf leaf) => nameof(PredicateKind.Feat);
        }
    }
}
