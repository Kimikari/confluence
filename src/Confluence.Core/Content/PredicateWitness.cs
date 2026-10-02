using System;
using System.Collections.Generic;
using Confluence.Core.Rules;

namespace Confluence.Core.Content
{
    /// <summary>Whether some wizard and legal loadout can satisfy a predicate (spec L643 "acquisition witness").</summary>
    internal enum Witness
    {
        /// <summary>No wizard and loadout can ever satisfy it: an identity contradiction or an impossible count.</summary>
        None = 0,

        /// <summary>
        /// Only spell-profile tags are missing: no base kit carries them, and no declared structural delta adds them
        /// yet. A later transformation delta may supply the witness (spec L543, DECISIONS D-012).
        /// </summary>
        Provisional = 1,

        Found = 2,
    }

    /// <summary>
    /// Conservative satisfiability of eligibility predicates against the catalogue: identity (wizard, owned spells,
    /// transformation owners), equipment capacity, owned-augment counts, spell profiles of one wizard's kit
    /// (base tags plus declared structural deltas), and schedule-reachable round and feat thresholds. Children of
    /// 'all' are judged independently, so it can miss a contradiction between two resource leaves but never reports
    /// a satisfiable predicate as impossible. Draft ordering is checked separately by <see cref="AcquisitionPlanner"/>.
    /// </summary>
    internal sealed class PredicateWitnessFinder
    {
        private readonly Catalogue _cat;
        private readonly Dictionary<string, List<IReadOnlyList<string>>[]> _profiles = new Dictionary<string, List<IReadOnlyList<string>>[]>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _equipmentTagCapacity = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly int _maxParents;
        private readonly int _maxRank;
        private readonly int _spellSlots;
        private readonly int _latestDraftRound;

        private string _self;
        private AnyNode _forcedAny;
        private Predicate _forcedChild;

        public PredicateWitnessFinder(Catalogue catalogue)
        {
            _cat = catalogue;
            _maxParents = Math.Max(0, catalogue.RuleFlags.MaxAugmentSlots - 1); // the child itself needs a slot
            _maxRank = catalogue.RuleFlags.AugmentMaxRank;
            _spellSlots = catalogue.RuleFlags.EquippedSpellCount;
            var conclave = Schedules.DraftRounds(MatchMode.Conclave);
            _latestDraftRound = conclave[conclave.Count - 1];
        }

        /// <summary>Best witness over the given wizards. selfId is excluded from owned-augment counts.</summary>
        public Witness Best(Predicate root, IEnumerable<string> wizardIds, string selfId, out string bestWizard)
        {
            return Best(root, wizardIds, selfId, null, null, out bestWizard);
        }

        /// <summary>As <see cref="Best(Predicate, IEnumerable{string}, string, out string)"/> with one 'any' node restricted to a single alternative.</summary>
        public Witness Best(Predicate root, IEnumerable<string> wizardIds, string selfId, AnyNode forcedAny, Predicate forcedChild, out string bestWizard)
        {
            _self = selfId;
            _forcedAny = forcedAny;
            _forcedChild = forcedChild;
            Witness best = Witness.None;
            bestWizard = null;
            foreach (var wizard in wizardIds)
            {
                Witness w = Evaluate(root, wizard);
                if (w > best || bestWizard == null)
                {
                    best = w;
                    bestWizard = wizard;
                }

                if (best == Witness.Found) break;
            }

            _forcedAny = null;
            _forcedChild = null;
            return best;
        }

        /// <summary>Leaves on the compulsory path (reached through 'all' only) that fail for the wizard.</summary>
        public List<Predicate> FailingMandatoryLeaves(Predicate root, string wizardId, string selfId)
        {
            _self = selfId;
            var failing = new List<Predicate>();
            foreach (var leaf in PredicatePaths.MandatoryNodes(root))
                if (!leaf.IsComposite && Evaluate(leaf, wizardId) != Witness.Found) failing.Add(leaf);
            return failing;
        }

        public Witness Evaluate(Predicate node, string wizard)
        {
            switch (node)
            {
                case AllNode all:
                {
                    Witness result = Witness.Found;
                    foreach (var child in all.Children)
                    {
                        Witness w = Evaluate(child, wizard);
                        if (w < result) result = w;
                        if (result == Witness.None) break;
                    }
                    return result;
                }
                case AnyNode any:
                {
                    if (ReferenceEquals(any, _forcedAny)) return Evaluate(_forcedChild, wizard);
                    Witness result = Witness.None;
                    foreach (var child in any.Children)
                    {
                        Witness w = Evaluate(child, wizard);
                        if (w > result) result = w;
                        if (result == Witness.Found) break;
                    }
                    return result;
                }
                case NotNode not:
                    return CanBeFalse(not.Child, wizard) ? Witness.Found : Witness.None;
                case AlwaysLeaf _:
                    return Witness.Found;
                case WizardLeaf w:
                    return Is(w.WizardId == wizard);
                case SpellLeaf s:
                    return Is(OwnsSpell(wizard, s.SpellId));
                case AugmentLeaf a:
                    return Is(a.Rank >= 1 && a.Rank <= _maxRank && a.AugmentId != _self && Acquirable(a.AugmentId, wizard));
                case EquippedItemLeaf e:
                    return Is(_cat.EquipmentById.ContainsKey(e.EquipmentId));
                case OwnedItemLeaf o:
                    return Is(_cat.EquipmentById.ContainsKey(o.EquipmentId));
                case EquippedTagLeaf et:
                    return Is(et.Count <= EquipmentTagCapacity(et.Tag));
                case SpellTagLeaf st:
                    return SpellCount(st.Count, CountSpells(wizard, st.Tag, st.Also));
                case SpellTagExactLeaf ste:
                    return SpellTagExact(wizard, ste);
                case SpellDistinctTagsLeaf sd:
                    return SpellDistinctTags(wizard, sd);
                case SpellCooldownCountLeaf sc:
                    return Is(sc.Count <= _spellSlots && CountBase(wizard, s => sc.Threshold.Includes(s.Cooldown)) >= sc.Count);
                case SpellCastTimeCountLeaf sct:
                    return Is(sct.Count <= _spellSlots && CountBase(wizard, s => sct.Threshold.Includes(s.CastTime)) >= sct.Count);
                case MainFocusCategoryLeaf mf:
                    return Is(AnyItem(i => i.HasValidCategory && i.Category == mf.Category && i.CanOccupy(EquipmentSlot.MainFocus)));
                case SidearmCategoryLeaf sa:
                    return Is(AnyItem(i => i.HasValidCategory && i.Category == sa.Category && i.CanOccupy(EquipmentSlot.Sidearm)));
                case IssuedSlotLeaf _:
                    return Witness.Found; // slot legality is a structural check
                case SlotTierLeaf tier:
                    return Is(AnyItem(i => i.CanOccupy(tier.Slot) && i.Tier >= tier.AtLeast));
                case EquippedCategoryLeaf ec:
                    return Is(ec.Count <= EquippableInCategory(ec.Category));
                case ConsumableChargesAtMostLeaf _:
                    return Witness.Found;
                case AugmentTagCountLeaf t:
                    return Is(t.Count <= _maxParents && CountParents(t.IsRestricted, t.Ids, wizard, a => a.HasTag(t.Tag), false) >= t.Count);
                case AugmentRankCountLeaf r:
                    return Is(r.Count <= _maxParents && r.RankAtLeast >= 1 && r.RankAtLeast <= _maxRank
                        && CountParents(r.IsRestricted, r.Ids, wizard, a => true, r.DistinctFamilies) >= r.Count);
                case AugmentFamilyCountLeaf f:
                    return Is(f.Count <= _maxParents && CountParents(f.IsRestricted, f.Ids, wizard, a => true, true) >= f.Count);
                case RoundAtLeastLeaf round:
                    return Is(round.Round <= _latestDraftRound);
                case FeatLeaf feat:
                    return Is(Feats.Exists(feat.FeatId) && feat.AtLeast <= Schedules.CompletedDuelsBefore(_latestDraftRound));
                default:
                    return Witness.Found;
            }
        }

        // Whether the wizard can hold a loadout where the node is false.
        private bool CanBeFalse(Predicate node, string wizard)
        {
            switch (node)
            {
                case WizardLeaf w: return w.WizardId != wizard;
                case SpellLeaf s: return !OwnsSpell(wizard, s.SpellId);
                case AlwaysLeaf _: return false;
                case NotNode not: return Evaluate(not.Child, wizard) != Witness.None;
                case AllNode all:
                    foreach (var child in all.Children)
                        if (CanBeFalse(child, wizard)) return true;
                    return false;
                case AnyNode any:
                    foreach (var child in any.Children)
                        if (!CanBeFalse(child, wizard)) return false;
                    return true;
                default:
                    return true; // loadout leaves can be left unmet
            }
        }

        private static Witness Is(bool condition) => condition ? Witness.Found : Witness.None;

        private Witness SpellCount(int needed, int available)
        {
            if (needed > _spellSlots) return Witness.None;
            return available >= needed ? Witness.Found : Witness.Provisional;
        }

        private Witness SpellTagExact(string wizard, SpellTagExactLeaf leaf)
        {
            if (leaf.Count > _spellSlots) return Witness.None;
            int canHave = 0;
            int canLack = 0;
            foreach (var profiles in Profiles(wizard))
            {
                bool has = false;
                bool lacks = false;
                foreach (var tags in profiles)
                {
                    if (Contains(tags, leaf.Tag)) has = true;
                    else lacks = true;
                }
                if (has) canHave++;
                if (lacks) canLack++;
            }
            return canHave >= leaf.Count && canLack >= _spellSlots - leaf.Count ? Witness.Found : Witness.Provisional;
        }

        private Witness SpellDistinctTags(string wizard, SpellDistinctTagsLeaf leaf)
        {
            if (leaf.Count > leaf.Set.Count) return Witness.None;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var profiles in Profiles(wizard))
            {
                foreach (var tags in profiles)
                {
                    if (!ContainsAll(tags, leaf.Also)) continue;
                    foreach (var tag in leaf.Set)
                        if (Contains(tags, tag)) seen.Add(tag);
                }
            }
            return seen.Count >= leaf.Count ? Witness.Found : Witness.Provisional;
        }

        private int CountSpells(string wizard, string tag, IReadOnlyList<string> also)
        {
            int n = 0;
            foreach (var profiles in Profiles(wizard))
            {
                foreach (var tags in profiles)
                {
                    if (Contains(tags, tag) && ContainsAll(tags, also))
                    {
                        n++;
                        break;
                    }
                }
            }
            return n;
        }

        private int CountBase(string wizard, Func<SpellDef, bool> predicate)
        {
            int n = 0;
            foreach (var spell in _cat.SpellsOf(wizard))
                if (predicate(spell)) n++;
            return n;
        }

        // Candidate tag sets per kit spell: the base tags, then the base tags under each declared structural delta
        // of a transformation of that spell.
        private List<IReadOnlyList<string>>[] Profiles(string wizard)
        {
            if (_profiles.TryGetValue(wizard, out var cached)) return cached;
            var spells = _cat.SpellsOf(wizard);
            var deltas = StructuralDeltas.All;
            var result = new List<IReadOnlyList<string>>[spells.Count];
            for (int i = 0; i < spells.Count; i++)
            {
                var list = new List<IReadOnlyList<string>> { spells[i].Tags };
                foreach (var transformation in _cat.TransformationsFor(spells[i].Id))
                    foreach (var delta in deltas)
                        if (delta.AugmentId == transformation.Id && delta.TargetSpellId == spells[i].Id) list.Add(delta.Apply(spells[i].Tags));
                result[i] = list;
            }
            _profiles.Add(wizard, result);
            return result;
        }

        private bool OwnsSpell(string wizard, string spellId) =>
            _cat.TryGetSpell(spellId, out var spell) && spell.WizardId == wizard;

        private bool Acquirable(string augmentId, string wizard)
        {
            if (!_cat.TryGetAugment(augmentId, out var augment)) return false;
            return !augment.IsTransformation || augment.OwnerWizardId == wizard;
        }

        private int CountParents(bool restricted, IReadOnlyList<string> ids, string wizard, Func<AugmentDef, bool> filter, bool distinctFamilies)
        {
            var families = new HashSet<string>(StringComparer.Ordinal);
            var counted = new HashSet<string>(StringComparer.Ordinal);
            IEnumerable<AugmentDef> candidates = restricted ? Lookup(ids) : _cat.Augments;
            foreach (var augment in candidates)
            {
                if (augment.Id == _self || !Acquirable(augment.Id, wizard) || !filter(augment)) continue;
                if (distinctFamilies) families.Add(augment.Family);
                else counted.Add(augment.Id);
            }
            return distinctFamilies ? families.Count : counted.Count;
        }

        private IEnumerable<AugmentDef> Lookup(IReadOnlyList<string> ids)
        {
            foreach (var id in ids)
                if (_cat.TryGetAugment(id, out var augment)) yield return augment;
        }

        private bool AnyItem(Func<EquipmentDef, bool> predicate)
        {
            foreach (var item in _cat.EquipmentItems)
                if (predicate(item)) return true;
            return false;
        }

        // Items of a category that can be equipped together: one per non-empty Unique group, within slot capacity.
        private int EquippableInCategory(EquipmentCategory category)
        {
            var groups = new HashSet<string>(StringComparer.Ordinal);
            int ungrouped = 0;
            foreach (var item in _cat.EquipmentItems)
            {
                if (!item.HasValidCategory || item.Category != category) continue;
                if (item.HasUniqueGroup) groups.Add(item.UniqueGroup);
                else ungrouped++;
            }
            int slots = 0;
            foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
                if (AnyItem(i => i.HasValidCategory && i.Category == category && i.CanOccupy(slot))) slots++;
            return Math.Min(slots, groups.Count + ungrouped);
        }

        /// <summary>
        /// Most distinct items carrying the tag that one loadout can equip: a maximum matching of Unique groups to
        /// the eight equipment slots. The one-enabled-active rule is not applied, so the bound stays an upper bound.
        /// </summary>
        public int EquipmentTagCapacity(string tag)
        {
            if (_equipmentTagCapacity.TryGetValue(tag, out int cached)) return cached;
            var groupSlots = new List<List<EquipmentSlot>>();
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var item in _cat.EquipmentItems)
            {
                if (!item.HasTag(tag)) continue;
                string key = item.HasUniqueGroup ? item.UniqueGroup : "#" + item.Id;
                if (!index.TryGetValue(key, out int g))
                {
                    g = groupSlots.Count;
                    index.Add(key, g);
                    groupSlots.Add(new List<EquipmentSlot>());
                }
                foreach (var slot in item.AllowedSlots)
                    if (!groupSlots[g].Contains(slot)) groupSlots[g].Add(slot);
            }

            var owner = new Dictionary<EquipmentSlot, int>();
            int matched = 0;
            for (int g = 0; g < groupSlots.Count; g++)
                if (Augment(g, groupSlots, owner, new HashSet<EquipmentSlot>())) matched++;
            _equipmentTagCapacity.Add(tag, matched);
            return matched;
        }

        // Kuhn's augmenting path for the group-to-slot matching.
        private static bool Augment(int group, List<List<EquipmentSlot>> groupSlots, Dictionary<EquipmentSlot, int> owner, HashSet<EquipmentSlot> visited)
        {
            foreach (var slot in groupSlots[group])
            {
                if (!visited.Add(slot)) continue;
                if (!owner.TryGetValue(slot, out int current) || Augment(current, groupSlots, owner, visited))
                {
                    owner[slot] = group;
                    return true;
                }
            }
            return false;
        }

        private static bool Contains(IReadOnlyList<string> tags, string tag)
        {
            for (int i = 0; i < tags.Count; i++)
                if (string.Equals(tags[i], tag, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool ContainsAll(IReadOnlyList<string> tags, IReadOnlyList<string> required)
        {
            for (int i = 0; i < required.Count; i++)
                if (!Contains(tags, required[i])) return false;
            return true;
        }
    }

    /// <summary>Structural walks shared by the validator, the witness finder, and the acquisition planner.</summary>
    internal static class PredicatePaths
    {
        /// <summary>The root and every node reached from it through 'all' nodes only: conditions every witness must meet.</summary>
        public static IEnumerable<Predicate> MandatoryNodes(Predicate root)
        {
            var stack = new Stack<Predicate>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                yield return node;
                if (node is AllNode all)
                    for (int i = all.Children.Count - 1; i >= 0; i--) stack.Push(all.Children[i]);
            }
        }

        /// <summary>Every node with a 'not' somewhere between it and the root.</summary>
        public static IEnumerable<Predicate> NodesUnderNot(Predicate root)
        {
            var stack = new Stack<(Predicate Node, bool Negated)>();
            stack.Push((root, false));
            while (stack.Count > 0)
            {
                var (node, negated) = stack.Pop();
                if (negated) yield return node;
                bool childNegated = negated || node is NotNode;
                var children = node.Children;
                for (int i = children.Count - 1; i >= 0; i--) stack.Push((children[i], childNegated));
            }
        }
    }
}
