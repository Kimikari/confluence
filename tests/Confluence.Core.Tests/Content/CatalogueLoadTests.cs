using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Confluence.Core.Content;
using Confluence.Core.Json;
using Confluence.Core.Rules;
using Xunit.Abstractions;

namespace Confluence.Core.Tests.Content
{
    public class CatalogueLoadTests
    {
        private readonly ITestOutputHelper _output;
        private static Catalogue Cat => TestCatalogue.Real;

        public CatalogueLoadTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void LoadsSuppliedCatalogueWithAllCounts()
        {
            var watch = Stopwatch.StartNew();
            var cat = Catalogue.Load(Catalogue.FindDefaultPath());
            watch.Stop();
            _output.WriteLine("Loaded catalogue in " + watch.Elapsed.TotalMilliseconds + " ms");

            Assert.Equal("Confluence Complete Game Design", cat.Title);
            Assert.Equal("2.0", cat.DesignVersion);
            Assert.Equal("2026-10-01", cat.Date);
            Assert.Equal(36, cat.Wizards.Count);
            Assert.Equal(288, cat.Spells.Count);
            Assert.Equal(120, cat.EquipmentItems.Count);
            Assert.Equal(864, cat.Augments.Count);
            Assert.Equal(24, cat.Biomes.Count);
            Assert.Equal(72, cat.Modules.Count);
            Assert.Equal(48, cat.Props.Count);
            Assert.Equal(48, cat.Builds.Count);
            foreach (var type in ContentIds.AllTypes)
            {
                Assert.Equal(cat.RecordsOf(type).Count, cat.Counts.Of(type));
                Assert.Equal(ContentIds.ExpectedCount(type), cat.RecordsOf(type).Count);
            }
            Assert.Equal(288, cat.SharedAugments.Count);
            Assert.Equal(576, cat.Transformations.Count);
            Assert.Empty(cat.DuplicateIds);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), "load took " + watch.Elapsed);
        }

        [Fact]
        public void ReadsRuleFlags()
        {
            var r = Cat.RuleFlags;
            Assert.True(r.EquipmentShared);
            Assert.True(r.SpellsExclusive);
            Assert.True(r.AugmentsFree);
            Assert.Equal(5, r.AugmentCandidates);
            Assert.Equal(1, r.RerollsPerAugmentCard);
            Assert.Equal(1, r.ChoicesPerDraft);
            Assert.Equal(8, r.WizardSpellCount);
            Assert.Equal(6, r.EquippedSpellCount);
            Assert.Equal(6, r.MaxAugmentSlots);
            Assert.Equal(2, r.AugmentMaxRank);
        }

        [Fact]
        public void MapsWizardFields()
        {
            var w = Cat.Wizard("WIZ-002");
            Assert.Equal("Bram Cinder", w.Name);
            Assert.Equal("The Kilnheart", w.Title);
            Assert.StartsWith("Pressure release", w.CastingMode);
            Assert.StartsWith("Hold primary", w.InputSequence);
            Assert.StartsWith("Any release after 0.20 s", w.NormalCast);
            Assert.Contains("100 ms crest window", w.Mastery);
            Assert.StartsWith("Kiln Vent", w.Signature);
            Assert.StartsWith("Tempered Hands", w.Passive);
            Assert.StartsWith("Mid and close range", w.RangePreference);
            Assert.StartsWith("Crest timing", w.Weakness);
            Assert.StartsWith("Peek between", w.Counterplay);
            Assert.Equal(Enumerable.Range(9, 8).Select(n => "SPL-" + n.ToString("D3")), w.SpellIds);
            Assert.Equal(w.SpellIds.Take(6), w.DefaultSpellIds);
            Assert.StartsWith("An industrial spellwright", w.Lore);
            Assert.StartsWith("Soot-black", w.VisualIdentity);
            Assert.StartsWith("A building bellows", w.AudioIdentity);
            Assert.Equal(2, w.Difficulty);
            Assert.True(w.HasTag("release-timing"));
            Assert.False(w.HasTag("release-Timing"));
            Assert.True(w.OwnsSpell("SPL-016"));
            Assert.False(w.OwnsSpell("SPL-017"));
            Assert.Equal(ContentType.Wizard, w.Type);
        }

        [Fact]
        public void MapsSpellFieldsAndDerivedEnums()
        {
            var s = Cat.Spell("SPL-009");
            Assert.Equal("Fireball", s.Name);
            Assert.Equal("Heat", s.School);
            Assert.Equal(School.Heat, s.SchoolEnum);
            Assert.Equal("projectile burst", s.Role);
            Assert.Equal("thermal", s.DamageChannel);
            Assert.Equal(DamageChannel.Thermal, s.Channel);
            Assert.Equal(18, s.Mana);
            Assert.Equal(5, s.Cooldown);
            Assert.Equal(0.35, s.CastTime);
            Assert.Equal(30, s.Range);
            Assert.Equal("48 direct OR 28 splash", s.Damage);
            Assert.StartsWith("24 m/s projectile", s.Effect);
            Assert.StartsWith("Heat 2 at impact", s.WorldInteraction);
            Assert.StartsWith("Dodge the visible", s.Counterplay);
            Assert.Equal("WIZ-002", s.WizardId);
            Assert.Equal("WIZ-002", s.DeclaredWizardId);
            Assert.True(s.IsDamaging);
            Assert.True(s.HasTag("school:heat"));

            foreach (var spell in Cat.Spells)
            {
                Assert.True(spell.HasValidSchool && spell.HasValidChannel, spell.Id);
                Assert.Equal(ContentIds.OwnerOfSpell(spell.Id), spell.WizardId);
            }
        }

        [Fact]
        public void MapsEquipmentFieldsAndDerivedRules()
        {
            var wand = Cat.Equipment("EQP-001");
            Assert.Equal("Surveyor's Point", wand.Name);
            Assert.Equal(EquipmentCategory.Wand, wand.Category);
            Assert.Equal("Wand", wand.CategoryText);
            Assert.Equal("Main Focus or Sidearm", wand.Slot);
            Assert.Equal(new[] { EquipmentSlot.MainFocus, EquipmentSlot.Sidearm }, wand.AllowedSlots);
            Assert.True(wand.CanOccupy(EquipmentSlot.Sidearm));
            Assert.False(wand.CanOccupy(EquipmentSlot.Armor));
            Assert.Equal(1, wand.Tier);
            Assert.Equal(200, wand.Price);
            Assert.Equal("Passive", wand.Activation);
            Assert.Equal(EquipmentActivationKind.Passive, wand.ActivationKind);
            Assert.False(wand.IsActive);
            Assert.True(wand.IsFocusCategory);
            Assert.False(wand.IsConsumable);
            Assert.Equal("prepared_shot", wand.UniqueGroup);
            Assert.StartsWith("Moving faster", wand.Drawback);
            Assert.StartsWith("Break sight", wand.Counterplay);
            Assert.StartsWith("After aiming", wand.Effect);

            var active = Cat.Equipment("EQP-007");
            Assert.Equal(EquipmentActivationKind.FocusInput, active.ActivationKind);
            Assert.True(active.IsActive);

            var flask = Cat.Equipment("EQP-111");
            Assert.Equal(EquipmentCategory.Consumable, flask.Category);
            Assert.Equal(EquipmentActivationKind.ConsumableInput, flask.ActivationKind);
            Assert.True(flask.IsConsumable);
            Assert.False(flask.IsActive);
            Assert.Equal(new[] { EquipmentSlot.Consumable }, flask.AllowedSlots);

            var clothing = Cat.Equipment("EQP-066");
            Assert.Equal(EquipmentCategory.MagicalClothing, clothing.Category);
            Assert.Equal("Magical clothing", clothing.CategoryText);
            Assert.Equal(new[] { EquipmentSlot.Clothing }, clothing.AllowedSlots);

            Assert.Equal(new[] { EquipmentSlot.TrinketA, EquipmentSlot.TrinketB }, Cat.Equipment("EQP-096").AllowedSlots);
            Assert.Equal(new[] { EquipmentSlot.MainFocus }, Cat.Equipment("EQP-016").AllowedSlots);
            Assert.False(Cat.Equipment("EQP-016").CanOccupy(EquipmentSlot.Sidearm));

            Assert.Equal(new[] { 200, 400, 600, 800 }, new[] { 1, 2, 3, 4 }.Select(EquipmentDef.PriceForTier));
            Assert.Throws<ArgumentOutOfRangeException>(() => EquipmentDef.PriceForTier(5));

            var kinds = Cat.EquipmentItems.GroupBy(e => e.ActivationKind).ToDictionary(g => g.Key, g => g.Count());
            Assert.Equal(99, kinds[EquipmentActivationKind.Passive]);
            Assert.Equal(5, kinds[EquipmentActivationKind.FocusInput]);
            Assert.Equal(2, kinds[EquipmentActivationKind.ArmorInput]);
            Assert.Equal(2, kinds[EquipmentActivationKind.ClothingInput]);
            Assert.Equal(1, kinds[EquipmentActivationKind.BootsInput]);
            Assert.Equal(1, kinds[EquipmentActivationKind.TrinketInput]);
            Assert.Equal(10, kinds[EquipmentActivationKind.ConsumableInput]);
            Assert.Equal(11, Cat.EquipmentItems.Count(e => e.IsActive));
            Assert.All(Cat.EquipmentItems.Where(e => e.IsActive), e => Assert.True(e.HasTag("active_equipment"), e.Id));
        }

        [Fact]
        public void MapsSharedAugmentFields()
        {
            var a = Cat.Augment("AUG-001");
            Assert.Equal("Steady Hand", a.Name);
            Assert.Equal("Casting mastery", a.Family);
            Assert.StartsWith("Release a direct projectile", a.Condition);
            Assert.Equal("Gain 6% projectile speed.", a.Rank1);
            Assert.Equal("Gain 10% projectile speed.", a.Rank2);
            Assert.Equal("Sprinting resets preparation.", a.Drawback);
            Assert.Equal("Silver", a.Rarity);
            Assert.Equal(Rarity.Silver, a.RarityEnum);
            Assert.Empty(a.EligibleWizards);
            Assert.Empty(a.TargetSpellIds);
            Assert.Equal("", a.ExclusiveGroup);
            Assert.False(a.HasExclusiveGroup);
            Assert.False(a.IsTransformation);
            Assert.True(a.IsShared);
            Assert.Null(a.TargetSpellId);
            Assert.Null(a.OwnerWizardId);
            Assert.Null(a.Cost);
            Assert.Null(a.StackingRule);
            var leaf = Assert.IsType<SpellTagLeaf>(a.Eligibility);
            Assert.Equal("projectile", leaf.Tag);
            Assert.Equal(1, leaf.Count);
            Assert.True(Cat.Augment("AUG-012").IsUnconditional);
        }

        [Fact]
        public void MapsTransformationFieldsFromBothRecordShapes()
        {
            var early = Cat.Augment("AUG-305");
            Assert.True(early.IsTransformation);
            Assert.Equal("Skipping Fire", early.Name);
            Assert.Equal("WIZ-002 spell transformations", early.Family);
            Assert.Equal(0.0, early.Cost);
            Assert.Equal("SPL-009", early.RawSpellId);
            Assert.Equal("SPL-009", early.TargetSpellId);
            Assert.Equal("WIZ-002", early.OwnerWizardId);
            Assert.Equal("SPL-009-delivery", early.ExclusiveGroup);
            Assert.Null(early.WizardId);
            Assert.Null(early.SpellId);

            var late = Cat.Augment("AUG-577");
            Assert.Equal("WIZ-019", late.WizardId);
            Assert.Equal("SPL-145", late.SpellId);
            Assert.Equal("Mote Dart", late.TargetSpellName);
            Assert.StartsWith("Compatible branches apply together", late.StackingRule);
            Assert.StartsWith("Modifies the target spell", late.ActivationRule);
            Assert.StartsWith("World fields affect both players", late.ReactionControl);
            Assert.Null(late.Cost);
            Assert.Null(late.RawSpellId);
            Assert.Equal("WIZ-019", late.OwnerWizardId);
        }

        [Fact]
        public void MapsArenaRecords()
        {
            var biome = Cat.Biome("BIO-001");
            Assert.Equal("Raincourt", biome.Name);
            Assert.Equal(new[] { "Water", "Metal" }, biome.Materials);
            Assert.Equal(new[] { 24, 32, 40, 48 }, biome.Sizes);
            Assert.True(biome.SupportsSize(40));
            Assert.StartsWith("Every 8 s", biome.EnvironmentRule);
            Assert.StartsWith("Blue slate", biome.VisualIdentity);
            Assert.StartsWith("Build connected", biome.TacticalUse);
            Assert.StartsWith("Dry routes", biome.Fairness);

            var module = Cat.Module("MOD-001");
            Assert.Equal(ModuleCategory.Topology, module.Category);
            Assert.Equal("6 × 6 m", module.Footprint);
            Assert.Equal(6, module.FootprintWidth);
            Assert.Equal(6, module.FootprintDepth);
            Assert.Equal(0, module.HeightM);
            Assert.Equal(new[] { "stone" }, module.Materials);
            Assert.StartsWith("Load-bearing", module.Durability);
            Assert.StartsWith("Two 2.8 meter doors", module.Geometry);
            Assert.StartsWith("2.5m or wider", module.Sockets);
            Assert.All(Cat.Modules, m => Assert.True(m.HasValidFootprint && m.HasValidCategory, m.Id));
            var oblong = Cat.Modules.First(m => m.Footprint == "10 × 4 m");
            Assert.Equal(10, oblong.FootprintWidth);
            Assert.Equal(4, oblong.FootprintDepth);
            Assert.Equal(-2, Cat.Module("MOD-035").HeightM);

            var prop = Cat.Prop("PROP-001");
            Assert.Equal("Copper Barrel", prop.Name);
            Assert.Equal(PropCategory.Conductive, prop.Category);
            Assert.Equal(PropMass.Medium, prop.Mass);
            Assert.Equal("medium", prop.MassText);
            Assert.Equal(60, prop.Durability);
            Assert.StartsWith("Stores one electrical branch", prop.Effect);
            Assert.StartsWith("Mirrored or resource-equivalent pair", prop.Placement);
            Assert.All(Cat.Props, p => Assert.True(p.HasValidMass && p.HasValidCategory, p.Id));
            Assert.Contains(Cat.Props, p => p.IsAnchored);
        }

        [Theory]
        [InlineData("6 × 6 m", 6, 6)]
        [InlineData("12 × 5 m", 12, 5)]
        [InlineData("2.5 x 4 m", 2.5, 4)]
        [InlineData("3×2m", 3, 2)]
        public void ParsesFootprints(string text, double width, double depth)
        {
            Assert.True(ModuleDef.TryParseFootprint(text, out double w, out double d));
            Assert.Equal(width, w);
            Assert.Equal(depth, d);
        }

        [Theory]
        [InlineData("")]
        [InlineData("6 m")]
        [InlineData("6 × m")]
        [InlineData("-6 × 6 m")]
        [InlineData("0 × 6 m")]
        [InlineData("six × 6 m")]
        public void RejectsBadFootprints(string text)
        {
            Assert.False(ModuleDef.TryParseFootprint(text, out _, out _));
        }

        [Fact]
        public void MapsBuildsOfBothShapes()
        {
            var plan = Cat.Build("BLD-001");
            Assert.Equal("Measured Crossfire", plan.Name);
            Assert.Equal("WIZ-001", plan.WizardId);
            Assert.Equal(6, plan.EquippedSpellIds.Count);
            Assert.Equal(5, plan.EquipmentIds.Count);
            Assert.Equal(6, plan.AugmentIds.Count);
            Assert.Equal(6, plan.DraftPlan.Count);
            Assert.StartsWith("Round 1: AUG-149", plan.DraftPlan[0]);
            Assert.Equal(1200, plan.EquipmentCost);
            Assert.Equal(5, plan.EquipmentInteractions.Count);
            Assert.Equal(3, plan.TransformationInteractions.Count);
            Assert.NotNull(plan.Sequence);
            Assert.NotNull(plan.MasteryConnection);
            Assert.Null(plan.Eligibility);
            Assert.Null(plan.UnlockPlan);
            Assert.False(plan.IsConditionalExample);
            Assert.Equal(1, plan.RankOf("AUG-149"));

            var conditional = Cat.Build("BLD-037");
            Assert.True(conditional.IsConditionalExample);
            Assert.Empty(conditional.DraftPlan);
            Assert.Null(conditional.EquipmentCost);
            Assert.StartsWith("Take Armed Scholar", conditional.UnlockPlan);
            Assert.NotNull(conditional.CombinedInteraction);
            Assert.NotNull(conditional.FreeChoiceBudget);
            var all = Assert.IsType<AllNode>(conditional.Eligibility);
            Assert.Equal(3, all.Children.Count);

            var ranked = Cat.Build("BLD-048");
            Assert.Equal(2, ranked.RankOf("AUG-078"));
            Assert.Equal(2, ranked.RankOf("AUG-156"));
            Assert.Equal(1, ranked.RankOf("AUG-263"));
            Assert.Equal(3, ranked.AugmentRanks.Count);
        }

        [Fact]
        public void RelationshipQueries()
        {
            Assert.Equal(Enumerable.Range(9, 8).Select(n => "SPL-" + n.ToString("D3")), Cat.SpellsOf("WIZ-002").Select(s => s.Id));
            var transforms = Cat.TransformationsOf("WIZ-002");
            Assert.Equal(16, transforms.Count);
            Assert.All(transforms, t => Assert.Equal("WIZ-002", t.OwnerWizardId));
            var forFireball = Cat.TransformationsFor("SPL-009").Select(a => a.Id).ToList();
            Assert.Equal(2, forFireball.Count);
            Assert.Contains("AUG-305", forFireball);
            Assert.All(Cat.SharedAugments, a => Assert.False(a.IsTransformation));
            Assert.Empty(Cat.SpellsOf("WIZ-999"));
            Assert.Empty(Cat.TransformationsFor("nope"));
            Assert.Empty(Cat.TransformationsOf(null));
            foreach (var wizard in Cat.Wizards)
            {
                Assert.Equal(8, Cat.SpellsOf(wizard.Id).Count);
                Assert.Equal(16, Cat.TransformationsOf(wizard.Id).Count);
            }
        }

        [Fact]
        public void TypedLookupsAndExistence()
        {
            Assert.True(Cat.Exists("WIZ-036"));
            Assert.True(Cat.Exists("PROP-048"));
            Assert.True(Cat.Exists("BLD-048"));
            Assert.False(Cat.Exists("WIZ-037"));
            Assert.False(Cat.Exists("FEAT-001"));
            Assert.False(Cat.Exists(null));
            Assert.Equal("Fireball", Cat.NameOf("SPL-009"));
            Assert.Equal("Win without using a consumable", Cat.NameOf("FEAT-016"));
            Assert.Null(Cat.NameOf("XYZ-001"));
            Assert.Equal(ContentType.Module, Cat.Get("MOD-010").Type);

            Assert.True(Cat.TryGetWizard("WIZ-001", out var w) && w.Id == "WIZ-001");
            Assert.True(Cat.TryGetSpell("SPL-001", out _));
            Assert.True(Cat.TryGetEquipment("EQP-120", out _));
            Assert.True(Cat.TryGetAugment("AUG-864", out _));
            Assert.True(Cat.TryGetBiome("BIO-024", out _));
            Assert.True(Cat.TryGetModule("MOD-072", out _));
            Assert.True(Cat.TryGetProp("PROP-048", out _));
            Assert.True(Cat.TryGetBuild("BLD-048", out _));
            Assert.False(Cat.TryGetWizard("SPL-001", out var none));
            Assert.Null(none);
            Assert.False(Cat.TryGet("nope", out _));

            var e = Assert.Throws<KeyNotFoundException>(() => Cat.Wizard("SPL-009"));
            Assert.Contains("No wizard with ID 'SPL-009'", e.Message);
            Assert.Throws<KeyNotFoundException>(() => Cat.Spell("SPL-289"));
            Assert.Throws<KeyNotFoundException>(() => Cat.Equipment("EQP-000"));
            Assert.Throws<KeyNotFoundException>(() => Cat.Augment("AUG-865"));
            Assert.Throws<KeyNotFoundException>(() => Cat.Biome("BIO-025"));
            Assert.Throws<KeyNotFoundException>(() => Cat.Module("MOD-073"));
            Assert.Throws<KeyNotFoundException>(() => Cat.Prop("PROP-049"));
            Assert.Throws<KeyNotFoundException>(() => Cat.Build("BLD-049"));
            Assert.Throws<KeyNotFoundException>(() => Cat.Get("nope"));

            Assert.Equal(1500, Cat.AllRecords().Count());
            Assert.Equal(Cat.AllRecords().Count(), Cat.AllRecords().Select(r => r.Id).Distinct().Count());
        }

        [Fact]
        public void EveryRecordKeepsItsRawJson()
        {
            foreach (var record in Cat.AllRecords())
                Assert.Equal(record.Id, record.Raw.GetString("id"));
            Assert.Equal(TestCatalogue.Source["content"]["spells"][8], Cat.Spell("SPL-009").Raw);
        }

        [Fact]
        public void ContentIdsHelpers()
        {
            Assert.True(ContentIds.TryParse("PROP-007", out var type, out int n));
            Assert.Equal(ContentType.Prop, type);
            Assert.Equal(7, n);
            Assert.False(ContentIds.TryParse("PROP-7", out _, out _));
            Assert.False(ContentIds.TryParse("PROP-0007", out _, out _));
            Assert.False(ContentIds.TryParse("XYZ-001", out _, out _));
            Assert.False(ContentIds.TryParse("SPL-000", out _, out _));
            Assert.False(ContentIds.TryParse("SPL-01a", out _, out _));
            Assert.True(ContentIds.TryParse("AUG-1000", out _, out int big) && big == 1000);
            Assert.Equal("WIZ-001", ContentIds.OwnerOfSpell("SPL-008"));
            Assert.Equal("WIZ-002", ContentIds.OwnerOfSpell("SPL-009"));
            Assert.Equal("WIZ-036", ContentIds.OwnerOfSpell("SPL-288"));
            Assert.Null(ContentIds.OwnerOfSpell("WIZ-001"));
            Assert.Equal(new[] { "SPL-281", "SPL-282", "SPL-283", "SPL-284", "SPL-285", "SPL-286", "SPL-287", "SPL-288" }, ContentIds.SpellIdsOwnedBy("WIZ-036"));
        }

        [Fact]
        public void EveryEligibilityRoundTripsToItsSourceJson()
        {
            foreach (var augment in Cat.Augments)
                Assert.True(augment.EligibilityJson.Equals(augment.Eligibility.ToJson()), augment.Id + ": " + augment.Eligibility);
            foreach (var build in Cat.Builds.Where(b => b.Eligibility != null))
                Assert.True(build.EligibilityJson.Equals(build.Eligibility.ToJson()), build.Id);
        }

        // ---- Strict structure -------------------------------------------------------------------

        [Fact]
        public void RejectsUnknownRecordField()
        {
            var json = TestCatalogue.EditRecord(TestCatalogue.Source, "SPL-010", "speed", 24);
            var e = Assert.Throws<CatalogueFormatException>(() => Catalogue.FromJson(json));
            Assert.Equal("SPL-010", e.RecordId);
            Assert.Contains("unknown field 'speed'", e.Message);
        }

        [Fact]
        public void RejectsMissingRequiredField()
        {
            var json = TestCatalogue.EditRecord(TestCatalogue.Source, "EQP-004", r => r.Without("tier"));
            var e = Assert.Throws<CatalogueFormatException>(() => Catalogue.FromJson(json));
            Assert.Equal("EQP-004", e.RecordId);
            Assert.Contains("missing required field 'tier'", e.Message);
        }

        [Fact]
        public void RejectsWrongFieldKind()
        {
            var json = TestCatalogue.EditRecord(TestCatalogue.Source, "WIZ-003", "difficulty", "hard");
            var e = Assert.Throws<CatalogueFormatException>(() => Catalogue.FromJson(json));
            Assert.Contains("WIZ-003: field 'difficulty' must be an integer, found String", e.Message);

            json = TestCatalogue.EditRecord(TestCatalogue.Source, "BIO-002", "sizes", TestCatalogue.Strings("24"));
            e = Assert.Throws<CatalogueFormatException>(() => Catalogue.FromJson(json));
            Assert.Equal("BIO-002", e.RecordId);
        }

        [Fact]
        public void RejectsMalformedEligibilityNamingTheAugment()
        {
            var json = TestCatalogue.EditRecord(TestCatalogue.Source, "AUG-007", "eligibility",
                TestCatalogue.Parse("{\"all\":[{\"spell_tag\":{\"tag\":\"projectile\",\"cnt\":1}}]}"));
            var e = Assert.Throws<CatalogueFormatException>(() => Catalogue.FromJson(json));
            Assert.Equal("AUG-007", e.RecordId);
            Assert.Contains("AUG-007 eligibility at $.all[0].spell_tag", e.Message);
            Assert.DoesNotContain("AUG-007: AUG-007", e.Message);
        }

        [Fact]
        public void RejectsUnknownTopLevelAndContentKeys()
        {
            Assert.Throws<CatalogueFormatException>(() => Catalogue.FromJson(TestCatalogue.Source.With("extra", 1)));
            var content = TestCatalogue.Source["content"].With("spells2", JsonValue.Array());
            Assert.Throws<CatalogueFormatException>(() => Catalogue.FromJson(TestCatalogue.Source.With("content", content)));
            Assert.Throws<CatalogueFormatException>(() => Catalogue.FromJson(TestCatalogue.Source.With("content", TestCatalogue.Source["content"].Without("props"))));
            Assert.Throws<CatalogueFormatException>(() => Catalogue.FromJson(TestCatalogue.Source.With("rules", TestCatalogue.Source["rules"].With("bonus", true))));
        }

        [Fact]
        public void LoadWrapsJsonSyntaxErrorsWithPath()
        {
            string path = Path.Combine(Path.GetTempPath(), "confluence-bad-" + Environment.ProcessId + ".json");
            File.WriteAllText(path, "{\"title\": \"x\",\n  \"counts\": }");
            try
            {
                var e = Assert.Throws<CatalogueFormatException>(() => Catalogue.Load(path));
                Assert.Contains(path, e.Message);
                Assert.Contains("line 2", e.Message);
                Assert.IsType<JsonParseException>(e.InnerException);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void InvalidEnumTextLoadsButIsFlagged()
        {
            var json = TestCatalogue.EditRecord(TestCatalogue.Source, "AUG-001", "rarity", "Bronze");
            var cat = Catalogue.FromJson(json);
            var a = cat.Augment("AUG-001");
            Assert.False(a.HasValidRarity);
            Assert.False(a.TryGetRarity(out _));
            var e = Assert.Throws<InvalidOperationException>(() => a.RarityEnum);
            Assert.Contains("AUG-001", e.Message);
        }
    }

    [CollectionDefinition("Environment", DisableParallelization = true)]
    public class EnvironmentCollection
    {
    }

    [Collection("Environment")]
    public class CataloguePathTests
    {
        [Fact]
        public void FindsCatalogueByWalkingUp()
        {
            string previous = Environment.GetEnvironmentVariable(Catalogue.EnvironmentVariable);
            try
            {
                Environment.SetEnvironmentVariable(Catalogue.EnvironmentVariable, null);
                string path = Catalogue.FindDefaultPath();
                Assert.True(File.Exists(path));
                Assert.EndsWith(Path.Combine("docs", "confluence", "confluence-catalogue.json"), path);
            }
            finally
            {
                Environment.SetEnvironmentVariable(Catalogue.EnvironmentVariable, previous);
            }
        }

        [Fact]
        public void HonorsEnvironmentVariable()
        {
            string previous = Environment.GetEnvironmentVariable(Catalogue.EnvironmentVariable);
            string copy = Path.Combine(Path.GetTempPath(), "confluence-env-" + Environment.ProcessId + ".json");
            try
            {
                File.WriteAllText(copy, TestCatalogue.Text);
                Environment.SetEnvironmentVariable(Catalogue.EnvironmentVariable, copy);
                Assert.Equal(Path.GetFullPath(copy), Catalogue.FindDefaultPath());
                Assert.Equal(36, Catalogue.LoadDefault().Wizards.Count);

                Environment.SetEnvironmentVariable(Catalogue.EnvironmentVariable, copy + ".missing");
                Assert.Throws<FileNotFoundException>(() => Catalogue.FindDefaultPath());
            }
            finally
            {
                Environment.SetEnvironmentVariable(Catalogue.EnvironmentVariable, previous);
                File.Delete(copy);
            }
        }
    }
}
