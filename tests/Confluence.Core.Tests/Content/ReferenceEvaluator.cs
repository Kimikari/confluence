using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Content;
using Confluence.Core.Rules;

namespace Confluence.Core.Tests.Content
{
    /// <summary>
    /// Minimal loadout used only by the reference evaluator below. The production evaluator (structural profiles,
    /// dormancy, live tags) belongs to Confluence.Economy; this exists to exercise the AST and visitor contract.
    /// </summary>
    internal sealed class TestLoadout
    {
        public string WizardId = "WIZ-002";
        public HashSet<string> KitSpellIds = new HashSet<string>();
        public List<TestSpell> EquippedSpells = new List<TestSpell>();
        public List<TestItem> Equipped = new List<TestItem>();
        public HashSet<string> Reserve = new HashSet<string>();
        public int ConsumableCharges;
        public Dictionary<string, TestAugment> ActiveAugments = new Dictionary<string, TestAugment>();
        public int Round = 1;
        public Dictionary<string, int> CompletedFeats = new Dictionary<string, int>();
    }

    internal sealed class TestSpell
    {
        public string Id;
        public string[] Tags = new string[0];
        public double Cooldown;
        public double CastTime;
    }

    internal sealed class TestItem
    {
        public string Id;
        public EquipmentSlot Slot;
        public EquipmentCategory Category;
        public int Tier = 1;
        public string UniqueGroup = "";
        public string[] Tags = new string[0];
    }

    internal sealed class TestAugment
    {
        public int Rank = 1;
        public string Family = "";
        public string[] Tags = new string[0];
    }

    /// <summary>Straightforward reading of spec L606-L635 over <see cref="TestLoadout"/>. Thresholds are inclusive.</summary>
    internal sealed class ReferenceEvaluator : IPredicateVisitor<bool>
    {
        private readonly TestLoadout _l;

        public ReferenceEvaluator(TestLoadout loadout)
        {
            _l = loadout;
        }

        public static bool Evaluate(Predicate predicate, TestLoadout loadout) => predicate.Accept(new ReferenceEvaluator(loadout));

        public bool Visit(AllNode node) => node.Children.All(c => c.Accept(this));
        public bool Visit(AnyNode node) => node.Children.Any(c => c.Accept(this));
        public bool Visit(NotNode node) => !node.Child.Accept(this);
        public bool Visit(AlwaysLeaf leaf) => true;
        public bool Visit(WizardLeaf leaf) => _l.WizardId == leaf.WizardId;
        public bool Visit(EquippedItemLeaf leaf) => _l.Equipped.Any(i => i.Id == leaf.EquipmentId);
        public bool Visit(OwnedItemLeaf leaf) => _l.Equipped.Any(i => i.Id == leaf.EquipmentId) || _l.Reserve.Contains(leaf.EquipmentId);
        public bool Visit(EquippedTagLeaf leaf) => _l.Equipped.Where(i => i.Tags.Contains(leaf.Tag)).Select(i => i.Id).Distinct().Count() >= leaf.Count;
        public bool Visit(AugmentLeaf leaf) => _l.ActiveAugments.TryGetValue(leaf.AugmentId, out var a) && a.Rank >= leaf.Rank;
        public bool Visit(SpellLeaf leaf) => _l.KitSpellIds.Contains(leaf.SpellId);

        public bool Visit(SpellTagLeaf leaf) =>
            _l.EquippedSpells.Count(s => s.Tags.Contains(leaf.Tag) && leaf.Also.All(s.Tags.Contains)) >= leaf.Count;

        public bool Visit(SpellTagExactLeaf leaf) => _l.EquippedSpells.Count(s => s.Tags.Contains(leaf.Tag)) == leaf.Count;

        public bool Visit(SpellDistinctTagsLeaf leaf) =>
            _l.EquippedSpells.Where(s => leaf.Also.All(s.Tags.Contains))
                .SelectMany(s => s.Tags).Where(leaf.Set.Contains).Distinct().Count() >= leaf.Count;

        public bool Visit(SpellCooldownCountLeaf leaf) => _l.EquippedSpells.Count(s => leaf.Threshold.Includes(s.Cooldown)) >= leaf.Count;
        public bool Visit(SpellCastTimeCountLeaf leaf) => _l.EquippedSpells.Count(s => leaf.Threshold.Includes(s.CastTime)) >= leaf.Count;
        public bool Visit(MainFocusCategoryLeaf leaf) => _l.Equipped.Any(i => i.Slot == EquipmentSlot.MainFocus && i.Category == leaf.Category);
        public bool Visit(SidearmCategoryLeaf leaf) => _l.Equipped.Any(i => i.Slot == EquipmentSlot.Sidearm && i.Category == leaf.Category);
        public bool Visit(IssuedSlotLeaf leaf) => _l.Equipped.All(i => i.Slot != leaf.Slot);
        public bool Visit(SlotTierLeaf leaf) => _l.Equipped.Any(i => i.Slot == leaf.Slot && i.Tier >= leaf.AtLeast);

        public bool Visit(EquippedCategoryLeaf leaf)
        {
            var items = _l.Equipped.Where(i => i.Category == leaf.Category).ToList();
            if (!leaf.DistinctUniqueGroups) return items.Count >= leaf.Count;
            return items.Where(i => i.UniqueGroup.Length > 0).Select(i => i.UniqueGroup).Distinct().Count() >= leaf.Count;
        }

        public bool Visit(ConsumableChargesAtMostLeaf leaf) => _l.ConsumableCharges <= leaf.AtMost;

        public bool Visit(AugmentTagCountLeaf leaf) => Parents(leaf.IsRestricted, leaf.Ids).Count(a => a.Value.Tags.Contains(leaf.Tag)) >= leaf.Count;

        public bool Visit(AugmentRankCountLeaf leaf)
        {
            var qualifying = Parents(leaf.IsRestricted, leaf.Ids).Where(a => a.Value.Rank >= leaf.RankAtLeast).ToList();
            return (leaf.DistinctFamilies ? qualifying.Select(a => a.Value.Family).Distinct().Count() : qualifying.Count) >= leaf.Count;
        }

        public bool Visit(AugmentFamilyCountLeaf leaf) => Parents(leaf.IsRestricted, leaf.Ids).Select(a => a.Value.Family).Distinct().Count() >= leaf.Count;
        public bool Visit(RoundAtLeastLeaf leaf) => _l.Round >= leaf.Round;
        public bool Visit(FeatLeaf leaf) => _l.CompletedFeats.TryGetValue(leaf.FeatId, out int n) && n >= leaf.AtLeast;

        private IEnumerable<KeyValuePair<string, TestAugment>> Parents(bool restricted, IReadOnlyList<string> ids) =>
            _l.ActiveAugments.Where(a => !restricted || ids.Contains(a.Key));
    }
}
