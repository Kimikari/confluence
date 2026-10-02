using System;
using System.Collections.Generic;
using System.Globalization;
using Confluence.Core.Rules;

namespace Confluence.Core.Content
{
    /// <summary>
    /// Semantic validation of a loaded catalogue (spec L435 "Automated validation rejects dangling IDs, invalid tiers,
    /// unsupported slots, negative durations/costs, missing counterplay"). Errors break binding rules; warnings mark
    /// departures from descriptive distribution statements in the design text.
    /// </summary>
    public sealed class CatalogueValidator
    {
        // Equipment category by ID interval (spec L562-L569).
        private static readonly (EquipmentCategory Category, int First, int Last)[] EquipmentIntervals =
        {
            (EquipmentCategory.Wand, 1, 15),
            (EquipmentCategory.Staff, 16, 30),
            (EquipmentCategory.Weapon, 31, 50),
            (EquipmentCategory.Armor, 51, 65),
            (EquipmentCategory.MagicalClothing, 66, 80),
            (EquipmentCategory.Boots, 81, 95),
            (EquipmentCategory.Trinket, 96, 110),
            (EquipmentCategory.Consumable, 111, 120),
        };

        public const int TransformationsPerSpell = 2;         // spec L284
        public const int TransformationsPerWizard = 16;       // spec L284
        public const int SharedAugmentCount = 288;            // spec L594
        public const int TransformationCount = 576;           // spec L594
        public const int CompoundSharedPredicates = 121;      // spec L596

        // Each wizard has five Silver, six Gold, and five Prismatic transformations (spec L596).
        private static readonly int[] TransformationRarityTarget = { 5, 6, 5 };

        // Unconditional shared pool: 43 Silver, 29 Gold, 16 Prismatic (spec L596).
        private static readonly int[] UnconditionalPoolTarget = { 43, 29, 16 };

        // Plain issued gear exists for these slots only (spec L571).
        private static readonly EquipmentSlot[] IssuedSlots =
        {
            EquipmentSlot.MainFocus, EquipmentSlot.Sidearm, EquipmentSlot.Armor, EquipmentSlot.Clothing, EquipmentSlot.Boots,
        };

        private readonly Catalogue _cat;
        private readonly List<ValidationIssue> _issues = new List<ValidationIssue>();

        private CatalogueValidator(Catalogue catalogue)
        {
            _cat = catalogue;
        }

        public static List<ValidationIssue> Validate(Catalogue catalogue)
        {
            if (catalogue == null) throw new ArgumentNullException(nameof(catalogue));
            var validator = new CatalogueValidator(catalogue);
            validator.Run();
            return validator._issues;
        }

        private void Run()
        {
            CheckCounts();
            CheckRuleFlags();
            CheckIds();
            CheckWizardsAndSpells();
            CheckEquipment();
            CheckAugments();
            CheckTransformationDistribution();
            CheckSharedDistribution();
            CheckPredicates();
            CheckDependencyGraph();
            CheckExclusiveGroups();
            CheckBuilds();
            CheckArena();
            CheckCounterplay();
        }

        // ---- Totals and IDs -------------------------------------------------------------------

        private void CheckCounts()
        {
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var type in ContentIds.AllTypes)
            {
                string key = ContentIds.JsonKey(type);
                known.Add(key);
                int actual = _cat.RecordsOf(type).Count;
                int expected = ContentIds.ExpectedCount(type);
                int? declared = _cat.Counts.Of(type);
                if (declared == null) Error(ValidationChecks.Counts, "counts", "counts block omits '" + key + "'");
                else if (declared.Value != actual)
                    Error(ValidationChecks.Counts, "counts", "counts." + key + " declares " + declared.Value + " but content has " + actual + " records");
                if (actual != expected)
                    Error(ValidationChecks.Counts, "counts", "content has " + actual + " " + key + "; the complete game defines " + expected);
            }
            foreach (var key in _cat.Counts.Keys)
                if (!known.Contains(key)) Warning(ValidationChecks.Counts, "counts", "counts block has unknown key '" + key + "'");
        }

        private void CheckRuleFlags()
        {
            var r = _cat.RuleFlags;
            Flag(r.EquipmentShared, "equipment_shared", "L185");
            Flag(r.SpellsExclusive, "spells_exclusive", "L185");
            Flag(r.AugmentsFree, "augments_free", "L185");
            Expect(r.AugmentCandidates, GlobalRules.Draft.Candidates, "augment_candidates", "L227");
            Expect(r.RerollsPerAugmentCard, GlobalRules.Draft.RerollsPerCandidate, "rerolls_per_augment_card", "L229");
            Expect(r.ChoicesPerDraft, GlobalRules.Draft.ChoicesPerDraft, "choices_per_draft", "L229");
            Expect(r.WizardSpellCount, GlobalRules.Loadout.SpellsPerWizard, "wizard_spell_count", "L292");
            Expect(r.EquippedSpellCount, GlobalRules.Loadout.EquippedSpellSlots, "equipped_spell_count", "L292");
            Expect(r.MaxAugmentSlots, GlobalRules.Draft.AugmentSlots, "max_augment_slots", "L231");
            Expect(r.AugmentMaxRank, GlobalRules.Draft.MaxRank, "augment_max_rank", "L231");
        }

        private void Flag(bool value, string key, string line)
        {
            if (!value) Error(ValidationChecks.RuleFlags, "rules", "rules." + key + " must be true (spec " + line + ")");
        }

        private void Expect(int value, int expected, string key, string line)
        {
            if (value != expected) Error(ValidationChecks.RuleFlags, "rules", "rules." + key + " is " + value + "; spec " + line + " requires " + expected);
        }

        private void CheckIds()
        {
            foreach (var id in _cat.DuplicateIds) Error(ValidationChecks.DuplicateId, id, "ID appears more than once");

            foreach (var type in ContentIds.AllTypes)
            {
                var records = _cat.RecordsOf(type);
                int expected = ContentIds.ExpectedCount(type);
                var present = new HashSet<int>();
                bool reportedOrder = false;
                for (int i = 0; i < records.Count; i++)
                {
                    string id = records[i].Id;
                    if (!ContentIds.TryParse(id, out var actualType, out int n) || actualType != type)
                    {
                        Error(ValidationChecks.IdFormat, id, "is not a well-formed " + ContentIds.Prefix(type) + "-NNN ID");
                        continue;
                    }
                    present.Add(n);
                    if (n > expected) Error(ValidationChecks.IdInterval, id, "is outside " + ContentIds.Format(type, 1) + ".." + ContentIds.Format(type, expected));
                    if (n != i + 1 && !reportedOrder)
                    {
                        Warning(ValidationChecks.IdOrder, id, "appears at position " + (i + 1) + " of content." + ContentIds.JsonKey(type) + "; records are not in ID order");
                        reportedOrder = true;
                    }
                }
                for (int n = 1; n <= expected; n++)
                    if (!present.Contains(n)) Error(ValidationChecks.IdInterval, ContentIds.Format(type, n), "is missing from the contiguous interval");
            }
        }

        // ---- Wizards and spells ---------------------------------------------------------------

        private void CheckWizardsAndSpells()
        {
            foreach (var wizard in _cat.Wizards)
            {
                if (!ContentIds.IsWellFormed(wizard.Id, ContentType.Wizard)) continue;
                var owned = ContentIds.SpellIdsOwnedBy(wizard.Id);
                if (!SameSequence(wizard.SpellIds, owned))
                    Error(ValidationChecks.SpellOwnership, wizard.Id, "spell_ids are [" + Join(wizard.SpellIds) + "]; " + wizard.Id + " must own " + owned[0] + ".." + owned[owned.Count - 1] + " in order (spec L294)");
                foreach (var spellId in wizard.SpellIds)
                    if (!_cat.SpellsById.ContainsKey(spellId)) Error(ValidationChecks.SpellOwnership, wizard.Id, "lists missing spell " + spellId);

                var expectedDefaults = new List<string>();
                for (int i = 0; i < Math.Min(GlobalRules.Loadout.EquippedSpellSlots, wizard.SpellIds.Count); i++) expectedDefaults.Add(wizard.SpellIds[i]);
                if (!SameSequence(wizard.DefaultSpellIds, expectedDefaults))
                    Error(ValidationChecks.DefaultSpells, wizard.Id, "default_spell_ids must be the first six spell_ids (spec L292)");

                if (wizard.Difficulty < 1) Warning(ValidationChecks.NumericRange, wizard.Id, "difficulty " + wizard.Difficulty + " is below 1");
            }

            foreach (var spell in _cat.Spells)
            {
                string owner = ContentIds.OwnerOfSpell(spell.Id);
                if (spell.DeclaredWizardId != null && owner != null && spell.DeclaredWizardId != owner)
                    Error(ValidationChecks.SpellOwnership, spell.Id, "wizard_id is " + spell.DeclaredWizardId + " but the ID interval assigns it to " + owner + " (spec L294)");
                if (spell.WizardId != null && !_cat.WizardsById.ContainsKey(spell.WizardId))
                    Error(ValidationChecks.SpellOwnership, spell.Id, "owner " + spell.WizardId + " does not exist");

                if (!spell.HasValidSchool) Error(ValidationChecks.SpellFields, spell.Id, "unrecognised school '" + spell.School + "'");
                if (!spell.HasValidChannel) Error(ValidationChecks.SpellFields, spell.Id, "unrecognised damage channel '" + spell.DamageChannel + "'");
                NonNegative(spell.Id, "mana", spell.Mana);
                NonNegative(spell.Id, "cooldown", spell.Cooldown);
                NonNegative(spell.Id, "cast_time", spell.CastTime);
                NonNegative(spell.Id, "range", spell.Range);

                // Namespaced school and channel tags drive eligibility (spec L533-L535).
                if (spell.HasValidSchool)
                {
                    string school = spell.School.ToLowerInvariant();
                    if (!spell.HasTag("school:" + school)) Error(ValidationChecks.SpellTags, spell.Id, "missing namespaced tag 'school:" + school + "' (spec L533)");
                    if (!spell.HasTag(school)) Warning(ValidationChecks.SpellTags, spell.Id, "missing bare school affinity tag '" + school + "' (spec L533)");
                }
                if (spell.IsDamaging && spell.HasValidChannel && spell.Channel != DamageChannel.None)
                {
                    string tag = "channel:" + ContentText.ChannelName(spell.Channel);
                    if (!spell.HasTag(tag)) Error(ValidationChecks.SpellTags, spell.Id, "damaging spell lacks channel tag '" + tag + "' (spec L533)");
                }
            }
        }

        private void NonNegative(string id, string field, double value)
        {
            if (value < 0) Error(ValidationChecks.NumericRange, id, field + " " + Num(value) + " is negative (spec L435)");
        }

        // ---- Equipment ------------------------------------------------------------------------

        private void CheckEquipment()
        {
            foreach (var item in _cat.EquipmentItems)
            {
                if (!item.HasValidCategory)
                    Error(ValidationChecks.EquipmentCategory, item.Id, "unrecognised category '" + item.CategoryText + "'");
                else if (ContentIds.TryParse(item.Id, out var type, out int n) && type == ContentType.Equipment)
                {
                    foreach (var interval in EquipmentIntervals)
                    {
                        if (n < interval.First || n > interval.Last) continue;
                        if (item.Category != interval.Category)
                            Error(ValidationChecks.EquipmentCategory, item.Id, "category is " + item.CategoryText + " but "
                                + ContentIds.Format(ContentType.Equipment, interval.First) + ".." + ContentIds.Format(ContentType.Equipment, interval.Last)
                                + " are " + ContentText.CategoryName(interval.Category) + " (spec L562-L569)");
                    }
                }

                if (!item.HasLegalSlotField)
                    Error(ValidationChecks.EquipmentSlot, item.Id, "slot '" + item.Slot + "' is not a legal slot string");
                else if (item.HasValidCategory && item.Slot != ContentText.ExpectedSlotField(item.Category))
                    Error(ValidationChecks.EquipmentSlot, item.Id, "slot '" + item.Slot + "' does not match category " + item.CategoryText + " (expected '" + ContentText.ExpectedSlotField(item.Category) + "', spec L560-L569)");

                if (item.Tier < EquipmentDef.MinTier || item.Tier > EquipmentDef.MaxTier)
                    Error(ValidationChecks.EquipmentTier, item.Id, "tier " + item.Tier + " is outside 1-4 (spec L195-L200)");

                if (!item.HasUniqueGroup) Error(ValidationChecks.UniqueGroup, item.Id, "unique_group is empty");

                if (!item.HasValidActivation)
                    Error(ValidationChecks.EquipmentActivation, item.Id, "activation '" + item.Activation + "' does not start with Passive or a known logical input");
                else if (item.HasValidCategory)
                {
                    bool consumableInput = item.ActivationKind == EquipmentActivationKind.ConsumableInput;
                    if (consumableInput != item.IsConsumable)
                        Error(ValidationChecks.EquipmentActivation, item.Id, item.IsConsumable
                            ? "consumable must use the Consumable input (spec L575)"
                            : "only consumables may use the Consumable input (spec L575)");
                }
            }
        }

        // ---- Augments -------------------------------------------------------------------------

        private void CheckAugments()
        {
            foreach (var augment in _cat.Augments)
            {
                if (!augment.HasValidRarity)
                    Error(ValidationChecks.AugmentRarity, augment.Id, "rarity '" + augment.Rarity + "' is not Silver, Gold, or Prismatic");
                if (augment.Cost.HasValue && augment.Cost.Value < 0)
                    Error(ValidationChecks.AugmentCost, augment.Id, "cost " + Num(augment.Cost.Value) + " is negative; augments are free (spec L594)");
                if (augment.IsTransformation) CheckTransformation(augment);
                else
                {
                    if (augment.EligibleWizards.Count > 0)
                        Warning(ValidationChecks.SharedAugments, augment.Id, "shared augment lists eligible_wizards [" + Join(augment.EligibleWizards) + "]; identity gates belong in the eligibility predicate");
                    foreach (var w in augment.EligibleWizards)
                        if (!_cat.WizardsById.ContainsKey(w)) Error(ValidationChecks.PredicateReference, augment.Id, "eligible wizard " + w + " does not exist");
                }
            }

            foreach (var spell in _cat.Spells)
            {
                int n = _cat.TransformationsFor(spell.Id).Count;
                if (n != TransformationsPerSpell)
                    Error(ValidationChecks.TransformationsPerSpell, spell.Id, "has " + n + " transformations; every spell has exactly two (spec L284)");
            }
        }

        private void CheckTransformation(AugmentDef augment)
        {
            string id = augment.Id;
            if (augment.EligibleWizards.Count != 1)
            {
                Error(ValidationChecks.Transformation, id, "transformation must have exactly one eligible wizard, has " + augment.EligibleWizards.Count);
                return;
            }
            if (augment.TargetSpellIds.Count != 1)
            {
                Error(ValidationChecks.Transformation, id, "transformation must target exactly one spell, targets " + augment.TargetSpellIds.Count + " (spec L286)");
                return;
            }
            string wizardId = augment.EligibleWizards[0];
            string spellId = augment.TargetSpellIds[0];
            if (!_cat.WizardsById.ContainsKey(wizardId)) Error(ValidationChecks.Transformation, id, "eligible wizard " + wizardId + " does not exist");
            if (!_cat.TryGetSpell(spellId, out var spell))
            {
                Error(ValidationChecks.Transformation, id, "target spell " + spellId + " does not exist");
            }
            else
            {
                if (spell.WizardId != wizardId)
                    Error(ValidationChecks.Transformation, id, "targets " + spellId + " owned by " + spell.WizardId + ", not its eligible wizard " + wizardId + " (spec L288)");
                if (augment.TargetSpellName != null && augment.TargetSpellName != spell.Name)
                    Warning(ValidationChecks.Transformation, id, "target_spell_name '" + augment.TargetSpellName + "' differs from " + spellId + " name '" + spell.Name + "'");
            }

            bool hasWizard = false;
            bool hasSpell = false;
            foreach (var node in augment.Eligibility.DescendantsAndSelf())
            {
                if (node is WizardLeaf w && w.WizardId == wizardId) hasWizard = true;
                if (node is SpellLeaf s && s.SpellId == spellId) hasSpell = true;
            }
            if (!hasWizard) Error(ValidationChecks.Transformation, id, "eligibility does not require its wizard " + wizardId);
            if (!hasSpell) Error(ValidationChecks.Transformation, id, "eligibility does not require its target spell " + spellId);

            if (augment.RawSpellId != null && augment.RawSpellId != spellId)
                Error(ValidationChecks.Transformation, id, "raw_spell_id " + augment.RawSpellId + " differs from target " + spellId);
            if (augment.SpellId != null && augment.SpellId != spellId)
                Error(ValidationChecks.Transformation, id, "spell_id " + augment.SpellId + " differs from target " + spellId);
            if (augment.WizardId != null && augment.WizardId != wizardId)
                Error(ValidationChecks.Transformation, id, "wizard_id " + augment.WizardId + " differs from eligible wizard " + wizardId);
        }

        private void CheckTransformationDistribution()
        {
            if (_cat.Transformations.Count != TransformationCount)
                Error(ValidationChecks.SharedAugments, "augments", _cat.Transformations.Count + " transformations; the catalogue defines " + TransformationCount + " (spec L594)");

            var summary = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var wizard in _cat.Wizards)
            {
                var transformations = _cat.TransformationsOf(wizard.Id);
                var byRarity = new int[3];
                foreach (var t in transformations)
                    if (t.TryGetRarity(out var rarity)) byRarity[(int)rarity]++;
                if (transformations.Count != TransformationsPerWizard)
                    Error(ValidationChecks.TransformationRarity, wizard.Id, "has " + transformations.Count + " transformations; each wizard has sixteen (spec L284)");
                string key = byRarity[0] + "/" + byRarity[1] + "/" + byRarity[2];
                if (!summary.TryGetValue(key, out var list)) summary.Add(key, list = new List<string>());
                list.Add(wizard.Id);
            }
            foreach (var entry in summary)
            {
                string target = TransformationRarityTarget[0] + "/" + TransformationRarityTarget[1] + "/" + TransformationRarityTarget[2];
                if (entry.Key == target) continue;
                Warning(ValidationChecks.TransformationRarity, "augments", "transformation Silver/Gold/Prismatic split is " + entry.Key + " for "
                    + entry.Value.Count + " wizard(s) [" + Join(entry.Value) + "]; spec L596 states " + target);
            }
        }

        private void CheckSharedDistribution()
        {
            if (_cat.SharedAugments.Count != SharedAugmentCount)
                Error(ValidationChecks.SharedAugments, "augments", _cat.SharedAugments.Count + " shared augments; the catalogue defines " + SharedAugmentCount + " (spec L594)");

            var unconditional = new int[3];
            int compound = 0;
            int allAny = 0;
            var notRoots = new List<string>();
            foreach (var augment in _cat.SharedAugments)
            {
                var root = augment.Eligibility;
                if (augment.IsUnconditional && augment.TryGetRarity(out var rarity)) unconditional[(int)rarity]++;
                if (root.IsComposite) compound++;
                if (root.Kind == PredicateKind.All || root.Kind == PredicateKind.Any) allAny++;
                if (root.Kind == PredicateKind.Not) notRoots.Add(augment.Id);
            }
            for (int i = 0; i < 3; i++)
            {
                if (unconditional[i] == UnconditionalPoolTarget[i]) continue;
                Warning(ValidationChecks.UnconditionalPool, "augments", "unconditional shared " + (Rarity)i + " pool has " + unconditional[i] + "; spec L596 states " + UnconditionalPoolTarget[i]);
            }
            // A compound acquisition predicate is one whose root is all, any, or not.
            if (compound != CompoundSharedPredicates)
            {
                string detail = allAny == CompoundSharedPredicates
                    ? "; roots of all/any alone number " + allAny + ", matching the spec, and the remaining " + notRoots.Count + " are 'not' roots [" + Join(notRoots) + "]"
                    : "; all/any roots alone number " + allAny;
                Warning(ValidationChecks.CompoundPredicates, "augments", compound + " shared augments have a compound (all/any/not) eligibility root; spec L596 states "
                    + CompoundSharedPredicates + detail);
            }
        }

        // ---- Predicates -------------------------------------------------------------------------

        private void CheckPredicates()
        {
            foreach (var augment in _cat.Augments)
            {
                CheckPredicate(augment.Id, augment.Eligibility);
                foreach (var id in augment.Eligibility.CollectReferencedIds())
                    if (id == augment.Id) Error(ValidationChecks.SelfReference, augment.Id, "eligibility references the augment itself (spec L247)");
                CheckParentRarity(augment);
            }
            foreach (var build in _cat.Builds)
                if (build.Eligibility != null) CheckPredicate(build.Id, build.Eligibility);
        }

        private void CheckPredicate(string owner, Predicate root)
        {
            int maxRank = _cat.RuleFlags.AugmentMaxRank;
            int spellSlots = _cat.RuleFlags.EquippedSpellCount;
            foreach (var node in root.DescendantsAndSelf())
            {
                switch (node)
                {
                    case WizardLeaf w:
                        Reference(owner, w.WizardId, ContentType.Wizard, "wizard");
                        break;
                    case EquippedItemLeaf e:
                        Reference(owner, e.EquipmentId, ContentType.Equipment, "equipped_item");
                        break;
                    case OwnedItemLeaf o:
                        Reference(owner, o.EquipmentId, ContentType.Equipment, "owned_item");
                        break;
                    case SpellLeaf s:
                        Reference(owner, s.SpellId, ContentType.Spell, "spell");
                        break;
                    case AugmentLeaf a:
                        Reference(owner, a.AugmentId, ContentType.Augment, "augment");
                        if (a.Rank < 1 || a.Rank > maxRank) Error(ValidationChecks.PredicateValue, owner, "augment " + a.AugmentId + " rank " + a.Rank + " is outside 1-" + maxRank);
                        break;
                    case FeatLeaf f:
                        if (!Feats.Exists(f.FeatId)) Error(ValidationChecks.PredicateReference, owner, "feat leaf references unknown feat " + f.FeatId);
                        if (f.AtLeast < 1) Error(ValidationChecks.PredicateValue, owner, "feat " + f.FeatId + " at_least must be at least 1");
                        break;
                    case AugmentTagCountLeaf t:
                        References(owner, t.Ids, "augment_tag_count");
                        PositiveCount(owner, "augment_tag_count", t.Count);
                        break;
                    case AugmentRankCountLeaf r:
                        References(owner, r.Ids, "augment_rank_count");
                        PositiveCount(owner, "augment_rank_count", r.Count);
                        if (r.RankAtLeast < 1 || r.RankAtLeast > maxRank) Error(ValidationChecks.PredicateValue, owner, "augment_rank_count rank_at_least " + r.RankAtLeast + " is outside 1-" + maxRank);
                        if (r.IsRestricted && r.Count > r.Ids.Count) Error(ValidationChecks.PredicateValue, owner, "augment_rank_count needs " + r.Count + " parents from only " + r.Ids.Count + " listed IDs");
                        break;
                    case AugmentFamilyCountLeaf fam:
                        References(owner, fam.Ids, "augment_family_count");
                        PositiveCount(owner, "augment_family_count", fam.Count);
                        break;
                    case EquippedTagLeaf et:
                        PositiveCount(owner, "equipped_tag", et.Count);
                        break;
                    case SpellTagLeaf st:
                        PositiveCount(owner, "spell_tag", st.Count);
                        SpellSlots(owner, "spell_tag", st.Count, spellSlots);
                        break;
                    case SpellTagExactLeaf ste:
                        SpellSlots(owner, "spell_tag_exact", ste.Count, spellSlots);
                        break;
                    case SpellDistinctTagsLeaf sd:
                        PositiveCount(owner, "spell_distinct_tags", sd.Count);
                        if (sd.Count > sd.Set.Count) Error(ValidationChecks.PredicateValue, owner, "spell_distinct_tags needs " + sd.Count + " distinct tags from a set of " + sd.Set.Count);
                        break;
                    case SpellCooldownCountLeaf sc:
                        PositiveCount(owner, "spell_cooldown_count", sc.Count);
                        SpellSlots(owner, "spell_cooldown_count", sc.Count, spellSlots);
                        break;
                    case SpellCastTimeCountLeaf sct:
                        PositiveCount(owner, "spell_cast_time_count", sct.Count);
                        SpellSlots(owner, "spell_cast_time_count", sct.Count, spellSlots);
                        break;
                    case MainFocusCategoryLeaf mf:
                        if (!ContentText.IsFocusCategory(mf.Category))
                            Error(ValidationChecks.PredicateValue, owner, "main_focus_category " + ContentText.CategoryName(mf.Category) + " can never occupy the Main Focus (spec L210)");
                        break;
                    case SidearmCategoryLeaf sa:
                        if (sa.Category != EquipmentCategory.Wand && sa.Category != EquipmentCategory.Weapon)
                            Error(ValidationChecks.PredicateValue, owner, "sidearm_category " + ContentText.CategoryName(sa.Category) + " can never occupy the Sidearm (spec L210)");
                        break;
                    case IssuedSlotLeaf iss:
                        if (Array.IndexOf(IssuedSlots, iss.Slot) < 0)
                            Error(ValidationChecks.PredicateValue, owner, "issued_slot " + ContentText.SlotName(iss.Slot) + " has no plain issued equipment (spec L571)");
                        break;
                    case SlotTierLeaf tier:
                        if (tier.AtLeast < EquipmentDef.MinTier || tier.AtLeast > EquipmentDef.MaxTier)
                            Error(ValidationChecks.PredicateValue, owner, "slot_tier at_least " + tier.AtLeast + " is outside " + EquipmentDef.MinTier + "-" + EquipmentDef.MaxTier);
                        break;
                    case EquippedCategoryLeaf ec:
                        PositiveCount(owner, "equipped_category", ec.Count);
                        int capacity = CategoryCapacity(ec.Category);
                        if (ec.Count > capacity)
                            Error(ValidationChecks.PredicateValue, owner, "equipped_category needs " + ec.Count + " " + ContentText.CategoryName(ec.Category) + " items but only " + capacity + " can be equipped (spec L635)");
                        break;
                    case RoundAtLeastLeaf round:
                        if (round.Round < 1) Warning(ValidationChecks.PredicateValue, owner, "round_at_least " + round.Round + " is always satisfied");
                        break;
                }
            }
        }

        private void Reference(string owner, string id, ContentType expected, string leaf)
        {
            if (_cat.TryGet(id, out var def))
            {
                if (def.Type != expected)
                    Error(ValidationChecks.PredicateReference, owner, leaf + " leaf references " + id + ", which is a " + def.Type.ToString().ToLowerInvariant() + ", not a " + expected.ToString().ToLowerInvariant());
                return;
            }
            Error(ValidationChecks.PredicateReference, owner, leaf + " leaf references missing " + expected.ToString().ToLowerInvariant() + " " + id);
        }

        private void References(string owner, IReadOnlyList<string> ids, string leaf)
        {
            foreach (var id in ids) Reference(owner, id, ContentType.Augment, leaf);
        }

        private void PositiveCount(string owner, string leaf, int count)
        {
            if (count < 1) Warning(ValidationChecks.PredicateValue, owner, leaf + " count " + count + " is always satisfied");
        }

        private void SpellSlots(string owner, string leaf, int count, int slots)
        {
            if (count > slots) Error(ValidationChecks.PredicateValue, owner, leaf + " needs " + count + " equipped spells but only " + slots + " can be equipped");
        }

        private static int CategoryCapacity(EquipmentCategory category)
        {
            switch (category)
            {
                case EquipmentCategory.Wand:
                case EquipmentCategory.Weapon: return 2; // Main Focus and Sidearm
                case EquipmentCategory.Trinket: return 2; // Trinket A and Trinket B
                default: return 1;
            }
        }

        /// <summary>A parent required on every path (through 'all' only) cannot have a later rarity than its child (spec L247, L643).</summary>
        private void CheckParentRarity(AugmentDef augment)
        {
            if (!augment.TryGetRarity(out var childRarity)) return;
            foreach (var leaf in MandatoryAugmentLeaves(augment.Eligibility))
            {
                if (!_cat.TryGetAugment(leaf.AugmentId, out var parent) || !parent.TryGetRarity(out var parentRarity)) continue;
                if (parentRarity > childRarity)
                    Error(ValidationChecks.ParentRarity, augment.Id, "requires " + parent.Id + " (" + parentRarity + ") before its own first acquisition at " + childRarity + " (spec L247)");
            }
        }

        private static IEnumerable<AugmentLeaf> MandatoryAugmentLeaves(Predicate node)
        {
            if (node is AugmentLeaf leaf) yield return leaf;
            else if (node is AllNode all)
                foreach (var child in all.Children)
                    foreach (var found in MandatoryAugmentLeaves(child))
                        yield return found;
        }

        // ---- Dependency graph -------------------------------------------------------------------

        private void CheckDependencyGraph()
        {
            // Edges: augment -> every augment named by its eligibility (augment leaves and enumerated id lists).
            var edges = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var augment in _cat.Augments)
            {
                var targets = new List<string>();
                foreach (var id in augment.Eligibility.CollectReferencedIds())
                    if (id != augment.Id && _cat.AugmentsById.ContainsKey(id)) targets.Add(id);
                edges[augment.Id] = targets;
            }

            // Iterative three-colour DFS; report each back edge's cycle once.
            var state = new Dictionary<string, int>(StringComparer.Ordinal);
            var reported = new HashSet<string>(StringComparer.Ordinal);
            foreach (var start in edges.Keys)
            {
                if (state.ContainsKey(start)) continue;
                var path = new List<string>();
                var stack = new Stack<(string Node, int Next)>();
                stack.Push((start, 0));
                state[start] = 1;
                path.Add(start);
                while (stack.Count > 0)
                {
                    var (node, next) = stack.Pop();
                    var targets = edges[node];
                    if (next < targets.Count)
                    {
                        stack.Push((node, next + 1));
                        string target = targets[next];
                        state.TryGetValue(target, out int color);
                        if (color == 0)
                        {
                            state[target] = 1;
                            path.Add(target);
                            stack.Push((target, 0));
                        }
                        else if (color == 1)
                        {
                            int from = path.IndexOf(target);
                            var cycle = path.GetRange(from, path.Count - from);
                            string key = CanonicalCycle(cycle);
                            if (reported.Add(key))
                                Error(ValidationChecks.DependencyCycle, target, "eligibility dependency cycle " + string.Join(" -> ", cycle) + " -> " + target + " (spec L643)");
                        }
                    }
                    else
                    {
                        state[node] = 2;
                        path.RemoveAt(path.Count - 1);
                    }
                }
            }
        }

        private static string CanonicalCycle(List<string> cycle)
        {
            int min = 0;
            for (int i = 1; i < cycle.Count; i++)
                if (string.CompareOrdinal(cycle[i], cycle[min]) < 0) min = i;
            var rotated = new List<string>();
            for (int i = 0; i < cycle.Count; i++) rotated.Add(cycle[(min + i) % cycle.Count]);
            return string.Join(">", rotated);
        }

        // ---- Exclusive groups -------------------------------------------------------------------

        private void CheckExclusiveGroups()
        {
            var groups = new Dictionary<string, List<AugmentDef>>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var augment in _cat.Augments)
            {
                if (!augment.HasExclusiveGroup) continue;
                if (!groups.TryGetValue(augment.ExclusiveGroup, out var members))
                {
                    groups.Add(augment.ExclusiveGroup, members = new List<AugmentDef>());
                    order.Add(augment.ExclusiveGroup);
                }
                members.Add(augment);
            }
            foreach (var name in order)
            {
                var members = groups[name];
                if (members.Count == 1)
                    Warning(ValidationChecks.ExclusiveGroup, members[0].Id, "exclusive group '" + name + "' has a single member and excludes nothing");
                bool anyTransformation = false;
                bool anyShared = false;
                var spells = new HashSet<string>(StringComparer.Ordinal);
                foreach (var m in members)
                {
                    if (m.IsTransformation)
                    {
                        anyTransformation = true;
                        spells.Add(m.TargetSpellId);
                    }
                    else anyShared = true;
                }
                if (anyTransformation && anyShared)
                    Error(ValidationChecks.ExclusiveGroup, members[0].Id, "exclusive group '" + name + "' mixes transformations and shared augments");
                if (spells.Count > 1)
                {
                    var ids = new List<string>();
                    foreach (var m in members) ids.Add(m.Id);
                    Error(ValidationChecks.ExclusiveGroup, members[0].Id, "exclusive group '" + name + "' groups branches of different spells [" + Join(new List<string>(spells)) + "] via [" + Join(ids) + "] (spec L541)");
                }
            }
        }

        // ---- Builds -------------------------------------------------------------------------------

        private void CheckBuilds()
        {
            var rules = _cat.RuleFlags;
            for (int index = 0; index < _cat.Builds.Count; index++)
            {
                var build = _cat.Builds[index];
                string id = build.Id;
                bool wizardExists = _cat.TryGetWizard(build.WizardId, out var wizard);
                if (!wizardExists) Error(ValidationChecks.Build, id, "wizard " + build.WizardId + " does not exist");

                // Spells: exist, belong to the wizard, at most six, no repeats.
                if (build.EquippedSpellIds.Count > rules.EquippedSpellCount)
                    Error(ValidationChecks.Build, id, "equips " + build.EquippedSpellIds.Count + " spells; at most " + rules.EquippedSpellCount + " can be equipped");
                else if (build.EquippedSpellIds.Count < rules.EquippedSpellCount)
                    Warning(ValidationChecks.BuildPlan, id, "equips " + build.EquippedSpellIds.Count + " spells; worked builds use six (spec L691)");
                Distinct(id, build.EquippedSpellIds, "equipped_spell_ids");
                foreach (var spellId in build.EquippedSpellIds)
                {
                    if (!_cat.TryGetSpell(spellId, out var spell)) Error(ValidationChecks.Build, id, "equipped spell " + spellId + " does not exist");
                    else if (wizardExists && spell.WizardId != wizard.Id)
                        Error(ValidationChecks.Build, id, "equipped spell " + spellId + " belongs to " + spell.WizardId + ", not " + wizard.Id + " (spec L521)");
                }

                CheckBuildEquipment(build);

                // Augments: exist, fit six slots, transformations belong to the wizard and target equipped spells.
                Distinct(id, build.AugmentIds, "augment_ids");
                if (build.AugmentIds.Count > rules.MaxAugmentSlots)
                    Error(ValidationChecks.Build, id, "lists " + build.AugmentIds.Count + " augments; there are " + rules.MaxAugmentSlots + " augment slots (spec L231)");
                var rarity = new int[3];
                var transformed = new HashSet<string>(StringComparer.Ordinal);
                int transformations = 0;
                foreach (var augmentId in build.AugmentIds)
                {
                    if (!_cat.TryGetAugment(augmentId, out var augment))
                    {
                        Error(ValidationChecks.Build, id, "augment " + augmentId + " does not exist");
                        continue;
                    }
                    if (augment.TryGetRarity(out var r)) rarity[(int)r]++;
                    if (!augment.IsTransformation) continue;
                    transformations++;
                    if (wizardExists && augment.OwnerWizardId != wizard.Id)
                        Error(ValidationChecks.Build, id, "transformation " + augmentId + " belongs to " + augment.OwnerWizardId + ", not " + wizard.Id + " (spec L288)");
                    string target = augment.TargetSpellId;
                    if (!Contains(build.EquippedSpellIds, target))
                        Error(ValidationChecks.Build, id, "transformation " + augmentId + " targets " + target + ", which the build does not equip");
                    if (!transformed.Add(target))
                        Warning(ValidationChecks.BuildPlan, id, "two transformations target " + target + "; worked builds transform different spells (spec L691)");
                }
                foreach (var entry in build.AugmentRanks)
                {
                    if (!Contains(build.AugmentIds, entry.Key))
                        Error(ValidationChecks.Build, id, "augment_ranks names " + entry.Key + ", which is not in augment_ids");
                    if (entry.Value < 1 || entry.Value > rules.AugmentMaxRank)
                        Error(ValidationChecks.Build, id, "augment_ranks gives " + entry.Key + " rank " + entry.Value + "; ranks are 1-" + rules.AugmentMaxRank);
                }

                // Per-wizard plans: BLD-001..036 cover each wizard once with 2/2/2 rarity and three transformations (spec L691).
                if (build.DraftPlan.Count > 0)
                {
                    if (index < _cat.Wizards.Count && build.WizardId != _cat.Wizards[index].Id)
                        Warning(ValidationChecks.BuildPlan, id, "per-wizard plan " + (index + 1) + " is for " + build.WizardId + ", expected " + _cat.Wizards[index].Id + " (spec L691)");
                    if (rarity[0] != 2 || rarity[1] != 2 || rarity[2] != 2)
                        Warning(ValidationChecks.BuildPlan, id, "free selections are " + rarity[0] + " Silver / " + rarity[1] + " Gold / " + rarity[2] + " Prismatic; per-wizard plans use 2/2/2 (spec L691)");
                    if (transformations != 3)
                        Warning(ValidationChecks.BuildPlan, id, "has " + transformations + " transformations; per-wizard plans use three (spec L691)");
                }
                if (build.EquipmentCost.HasValue)
                {
                    int total = 0;
                    bool priced = true;
                    foreach (var eqId in build.EquipmentIds)
                    {
                        if (_cat.TryGetEquipment(eqId, out var item) && item.Tier >= EquipmentDef.MinTier && item.Tier <= EquipmentDef.MaxTier) total += item.Price;
                        else priced = false;
                    }
                    if (priced && total != build.EquipmentCost.Value)
                        Warning(ValidationChecks.BuildPlan, id, "equipment_cost " + build.EquipmentCost.Value + " differs from the tier price total " + total + " (spec L697)");
                    if (build.EquipmentCost.Value < 0) Error(ValidationChecks.NumericRange, id, "equipment_cost is negative");
                }
            }
        }

        /// <summary>The build's equipment must fit the eight equipment slots with distinct Unique groups (spec L210).</summary>
        private void CheckBuildEquipment(BuildDef build)
        {
            string id = build.Id;
            Distinct(id, build.EquipmentIds, "equipment_ids");
            var items = new List<EquipmentDef>();
            foreach (var eqId in build.EquipmentIds)
            {
                if (!_cat.TryGetEquipment(eqId, out var item)) Error(ValidationChecks.Build, id, "equipment " + eqId + " does not exist");
                else if (item.HasValidCategory) items.Add(item);
            }

            int staffs = 0, sidearmCapable = 0, foci = 0, trinkets = 0;
            var single = new Dictionary<EquipmentCategory, int>();
            var groups = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                if (item.HasUniqueGroup && !groups.Add(item.UniqueGroup))
                    Error(ValidationChecks.Build, id, "equips Unique group '" + item.UniqueGroup + "' twice (spec L210)");
                switch (item.Category)
                {
                    case EquipmentCategory.Staff:
                        staffs++;
                        foci++;
                        break;
                    case EquipmentCategory.Wand:
                    case EquipmentCategory.Weapon:
                        sidearmCapable++;
                        foci++;
                        break;
                    case EquipmentCategory.Trinket:
                        trinkets++;
                        break;
                    default:
                        single.TryGetValue(item.Category, out int n);
                        single[item.Category] = n + 1;
                        break;
                }
            }
            if (foci > 2 || staffs > 1)
                Error(ValidationChecks.Build, id, "equips " + foci + " foci (" + staffs + " staffs); Main Focus and Sidearm hold at most two and a staff cannot be a sidearm");
            if (trinkets > 2) Error(ValidationChecks.Build, id, "equips " + trinkets + " trinkets; there are two trinket slots");
            foreach (var entry in single)
                if (entry.Value > 1)
                    Error(ValidationChecks.Build, id, "equips " + entry.Value + " " + ContentText.CategoryName(entry.Key) + " items; that slot holds one (spec L635)");
        }

        // ---- Arena and counterplay ----------------------------------------------------------------

        private void CheckArena()
        {
            foreach (var biome in _cat.Biomes)
            {
                if (biome.Sizes.Count == 0) Error(ValidationChecks.ArenaFields, biome.Id, "supports no arena sizes");
                foreach (int size in biome.Sizes)
                    if (!Schedules.IsValidArenaDiameter(size))
                        Error(ValidationChecks.ArenaFields, biome.Id, "size " + size + " is not a legal arena diameter (24, 32, 40, 48; spec L306)");
            }
            foreach (var module in _cat.Modules)
            {
                if (!module.HasValidCategory) Error(ValidationChecks.ArenaFields, module.Id, "unrecognised module category '" + module.CategoryText + "'");
                if (!module.HasValidFootprint) Error(ValidationChecks.ArenaFields, module.Id, "footprint '" + module.Footprint + "' is not 'W × D m'");
            }
            foreach (var prop in _cat.Props)
            {
                if (!prop.HasValidCategory) Error(ValidationChecks.ArenaFields, prop.Id, "unrecognised prop category '" + prop.CategoryText + "'");
                if (!prop.HasValidMass) Error(ValidationChecks.ArenaFields, prop.Id, "unrecognised mass '" + prop.MassText + "'");
                if (prop.Durability <= 0) Error(ValidationChecks.NumericRange, prop.Id, "durability " + Num(prop.Durability) + " must be positive");
            }
        }

        private void CheckCounterplay()
        {
            foreach (var w in _cat.Wizards) Counterplay(w.Id, w.Counterplay);
            foreach (var s in _cat.Spells) Counterplay(s.Id, s.Counterplay);
            foreach (var e in _cat.EquipmentItems) Counterplay(e.Id, e.Counterplay);
            foreach (var a in _cat.Augments) Counterplay(a.Id, a.Counterplay);
            foreach (var p in _cat.Props) Counterplay(p.Id, p.Counterplay);
            foreach (var b in _cat.Builds) Counterplay(b.Id, b.Counterplay);
        }

        private void Counterplay(string id, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) Error(ValidationChecks.Counterplay, id, "counterplay is missing (spec L435)");
        }

        // ---- Helpers ------------------------------------------------------------------------------

        private void Distinct(string owner, IReadOnlyList<string> ids, string field)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in ids)
                if (!seen.Add(id)) Error(ValidationChecks.Build, owner, field + " lists " + id + " more than once");
        }

        private static bool Contains(IReadOnlyList<string> list, string value)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] == value) return true;
            return false;
        }

        private static bool SameSequence(IReadOnlyList<string> a, IReadOnlyList<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        private static string Join(IEnumerable<string> ids) => string.Join(", ", ids);

        private static string Num(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        private void Error(string check, string id, string message) => _issues.Add(new ValidationIssue(ValidationSeverity.Error, check, id, message));

        private void Warning(string check, string id, string message) => _issues.Add(new ValidationIssue(ValidationSeverity.Warning, check, id, message));
    }
}
