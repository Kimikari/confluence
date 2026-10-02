using System;
using System.Collections.Generic;
using Confluence.Core.Json;
using Confluence.Core.Rules;

namespace Confluence.Core.Content
{
    // ---- Composite nodes ----------------------------------------------------------------------

    /// <summary>Every child predicate is true.</summary>
    public sealed class AllNode : Predicate
    {
        private readonly IReadOnlyList<Predicate> _children;

        public AllNode(IEnumerable<Predicate> children)
        {
            _children = PredicateArgs.Children(children, "all");
        }

        public override PredicateKind Kind => PredicateKind.All;
        public override IReadOnlyList<Predicate> Children => _children;
        public override T Accept<T>(IPredicateVisitor<T> visitor) => visitor.Visit(this);

        public override JsonValue ToJson()
        {
            var items = new List<JsonValue>(_children.Count);
            foreach (var child in _children) items.Add(child.ToJson());
            return JsonValue.Object(("all", JsonValue.Array(items)));
        }
    }

    /// <summary>At least one child is true.</summary>
    public sealed class AnyNode : Predicate
    {
        private readonly IReadOnlyList<Predicate> _children;

        public AnyNode(IEnumerable<Predicate> children)
        {
            _children = PredicateArgs.Children(children, "any");
        }

        public override PredicateKind Kind => PredicateKind.Any;
        public override IReadOnlyList<Predicate> Children => _children;
        public override T Accept<T>(IPredicateVisitor<T> visitor) => visitor.Visit(this);

        public override JsonValue ToJson()
        {
            var items = new List<JsonValue>(_children.Count);
            foreach (var child in _children) items.Add(child.ToJson());
            return JsonValue.Object(("any", JsonValue.Array(items)));
        }
    }

    /// <summary>Its one child is false.</summary>
    public sealed class NotNode : Predicate
    {
        private readonly IReadOnlyList<Predicate> _children;

        public NotNode(Predicate child)
        {
            Child = child ?? throw new ArgumentNullException(nameof(child));
            _children = Array.AsReadOnly(new[] { child });
        }

        public Predicate Child { get; }
        public override PredicateKind Kind => PredicateKind.Not;
        public override IReadOnlyList<Predicate> Children => _children;
        public override T Accept<T>(IPredicateVisitor<T> visitor) => visitor.Visit(this);
        public override JsonValue ToJson() => JsonValue.Object(("not", Child.ToJson()));
    }

    // ---- Identity, items, and owned augments ------------------------------------------------

    /// <summary>No additional acquisition prerequisite.</summary>
    public sealed class AlwaysLeaf : Predicate
    {
        public static readonly AlwaysLeaf Instance = new AlwaysLeaf();

        private AlwaysLeaf()
        {
        }

        public override PredicateKind Kind => PredicateKind.Always;
        public override T Accept<T>(IPredicateVisitor<T> visitor) => visitor.Visit(this);
        public override JsonValue ToJson() => JsonValue.Object(("always", JsonValue.True));
    }

    /// <summary>Exact wizard identity (acquisition-only).</summary>
    public sealed class WizardLeaf : Predicate
    {
        public WizardLeaf(string wizardId)
        {
            WizardId = PredicateArgs.Id(wizardId, nameof(wizardId));
        }

        public string WizardId { get; }
        public override PredicateKind Kind => PredicateKind.Wizard;
        public override IReadOnlyList<string> DirectReferences => new[] { WizardId };
        public override T Accept<T>(IPredicateVisitor<T> visitor) => visitor.Visit(this);
        public override JsonValue ToJson() => JsonValue.Object(("wizard", WizardId));
    }

    /// <summary>The exact item is currently equipped in an occupied slot.</summary>
    public sealed class EquippedItemLeaf : Predicate
    {
        public EquippedItemLeaf(string equipmentId)
        {
            EquipmentId = PredicateArgs.Id(equipmentId, nameof(equipmentId));
        }

        public string EquipmentId { get; }
        public override PredicateKind Kind => PredicateKind.EquippedItem;
        public override IReadOnlyList<string> DirectReferences => new[] { EquipmentId };
        public override T Accept<T>(IPredicateVisitor<T> visitor) => visitor.Visit(this);
        public override JsonValue ToJson() => JsonValue.Object(("equipped_item", EquipmentId));
    }

    /// <summary>The exact item is held, including reserve inventory.</summary>
    public sealed class OwnedItemLeaf : Predicate
    {
        public OwnedItemLeaf(string equipmentId)
        {
            EquipmentId = PredicateArgs.Id(equipmentId, nameof(equipmentId));
        }

        public string EquipmentId { get; }
        public override PredicateKind Kind => PredicateKind.OwnedItem;
        public override IReadOnlyList<string> DirectReferences => new[] { EquipmentId };
        public override T Accept<T>(IPredicateVisitor<T> visitor) => visitor.Visit(this);
        public override JsonValue ToJson() => JsonValue.Object(("owned_item", EquipmentId));
    }

    /// <summary>At least Count distinct equipped definitions supply the live tag; a disabled active does not count.</summary>
    public sealed class EquippedTagLeaf : Predicate
    {
        public EquippedTagLeaf(string tag, int count)
        {
            Tag = PredicateArgs.Text(tag, nameof(tag));
            Count = PredicateArgs.NonNegative(count, nameof(count));
        }

        public string Tag { get; }
        public int Count { get; }
        public override PredicateKind Kind => PredicateKind.EquippedTag;
        public override T Accept<T>(IPredicateVisitor<T> visitor) => visitor.Visit(this);

        public override JsonValue ToJson() =>
            JsonValue.Object(("equipped_tag", JsonValue.Object(("tag", Tag), ("count", Count))));
    }

    /// <summary>The exact parent augment is active at the stated minimum rank.</summary>
    public sealed class AugmentLeaf : Predicate
    {
        public AugmentLeaf(string augmentId, int rank)
        {
            AugmentId = PredicateArgs.Id(augmentId, nameof(augmentId));
            Rank = PredicateArgs.NonNegative(rank, nameof(rank));
        }

        public string AugmentId { get; }
        public int Rank { get; }
        public override PredicateKind Kind => PredicateKind.Augment;
        public override IReadOnlyList<string> DirectReferences => new[] { AugmentId };
        public override T Accept<T>(IPredicateVisitor<T> visitor) => visitor.Visit(this);

        public override JsonValue ToJson() =>
            JsonValue.Object(("augment", JsonValue.Object(("id", AugmentId), ("rank", Rank))));
    }

    // ---- Spell profile -------------------------------------------------------------------------

    /// <summary>The exact spell belongs to the wizard's owned exclusive kit.</summary>
    public sealed class SpellLeaf : Predicate
    {
        public SpellLeaf(string spellId)
        {
            SpellId = PredicateArgs.Id(spellId, nameof(spellId));
        }

        public string SpellId { get; }
        public override PredicateKind Kind => PredicateKind.Spell;
        public override IReadOnlyList<string> DirectReferences => new[] { SpellId };
        public override T Accept<T>(IPredicateVisitor<T> visitor) => visitor.Visit(this);
        public override JsonValue ToJson() => JsonValue.Object(("spell", SpellId));
    }

    /// <summary>At least Count equipped structural spell profiles have Tag; every Also tag must be on the same spell.</summary>
    public sealed class SpellTagLeaf : Predicate
    {
        public SpellTagLeaf(string tag, int count, IEnumerable<string> also = null)
        {
            Tag = PredicateArgs.Text(tag, nameof(tag));
            Count = PredicateArgs.NonNegative(count, nameof(count));
            HasAlsoField = also != null;
            Also = PredicateArgs.Strings(also, nameof(also));
        }

        public string Tag { get; }
        public int Count { get; }
        public IReadOnlyList<string> Also { get; }
        public bool HasAlsoField { get; }
        public override PredicateKind Kind => PredicateKind.SpellTag;
        public override T Accept<T>(IPredicateVisitor<T> visitor) => visitor.Visit(this);

        public override JsonValue ToJson()
        {
            var fields = new List<KeyValuePair<string, JsonValue>>
            {
                PredicateArgs.Field("tag", Tag),
                PredicateArgs.Field("count", Count),
            };
            if (HasAlsoField) fields.Add(PredicateArgs.Field("also", JsonValue.StringArray(Also)));
            return JsonValue.Object(("spell_tag", JsonValue.Object(fields)));
        }
    }

    /// <summary>Exactly Count equipped structural profiles have Tag.</summary>
    public sealed class SpellTagExactLeaf : Predicate
    {
        public SpellTagExactLeaf(string tag, int count)
        {
            Tag = PredicateArgs.Text(tag, nameof(tag));
            Count = PredicateArgs.NonNegative(count, nameof(count));
        }

        public string Tag { get; }
        public int Count { get; }
        public override PredicateKind Kind => PredicateKind.SpellTagExact;
        public override T Accept<T>(IPredicateVisitor<T> visitor) => visitor.Visit(this);

        public override JsonValue ToJson() =>
            JsonValue.Object(("spell_tag_exact", JsonValue.Object(("tag", Tag), ("count", Count))));
    }

    /// <summary>At least Count distinct tags from Set across qualifying equipped profiles; Also filters which profiles qualify.</summary>
    public sealed class SpellDistinctTagsLeaf : Predicate
    {
        public SpellDistinctTagsLeaf(IEnumerable<string> set, int count, IEnumerable<string> also = null)
        {
            Set = PredicateArgs.Strings(set ?? throw new ArgumentNullException(nameof(set)), nameof(set));
            Count = PredicateArgs.NonNegative(count, nameof(count));
            HasAlsoField = also != null;
            Also = PredicateArgs.Strings(also, nameof(also));
        }

        public IReadOnlyList<string> Set { get; }
        public int Count { get; }
        public IReadOnlyList<string> Also { get; }
        public bool HasAlsoField { get; }
        public override PredicateKind Kind => PredicateKind.SpellDistinctTags;
        public override T Accept<T>(IPredicateVisitor<T> visitor) => visitor.Visit(this);

        public override JsonValue ToJson()
        {
            var fields = new List<KeyValuePair<string, JsonValue>>
            {
                PredicateArgs.Field("set", JsonValue.StringArray(Set)),
                PredicateArgs.Field("count", Count),
            };
            if (HasAlsoField) fields.Add(PredicateArgs.Field("also", JsonValue.StringArray(Also)));
            return JsonValue.Object(("spell_distinct_tags", JsonValue.Object(fields)));
        }
    }

    public enum BoundKind
    {
        AtLeast,
        AtMost,
    }

    /// <summary>Inclusive numeric threshold (spec L635 "Numeric at_most and at_least thresholds are inclusive").</summary>
    public sealed class Threshold
    {
        public Threshold(BoundKind bound, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value));
            Bound = bound;
            Value = value;
        }

        public BoundKind Bound { get; }
        public double Value { get; }

        public string JsonKey => Bound == BoundKind.AtLeast ? "at_least" : "at_most";

        public bool Includes(double candidate) => Bound == BoundKind.AtLeast ? candidate >= Value : candidate <= Value;

        public override string ToString() => (Bound == BoundKind.AtLeast ? ">= " : "<= ") + JsonWriter.FormatNumber(Value);
    }

    /// <summary>At least Count equipped spells meet the base cooldown threshold (seconds).</summary>
    public sealed class SpellCooldownCountLeaf : Predicate
    {
        public SpellCooldownCountLeaf(int count, Threshold threshold)
        {
            Count = PredicateArgs.NonNegative(count, nameof(count));
            Threshold = threshold ?? throw new ArgumentNullException(nameof(threshold));
        }

        public int Count { get; }
        public Threshold Threshold { get; }
        public override PredicateKind Kind => PredicateKind.SpellCooldownCount;
        public override T Accept<T>(IPredicateVisitor<T> visitor) => visitor.Visit(this);

        public override JsonValue ToJson() =>
            JsonValue.Object(("spell_cooldown_count", JsonValue.Object((Threshold.JsonKey, Threshold.Value), ("count", Count))));
    }

    /// <summary>At least Count equipped spells meet the intrinsic windup (cast_time) threshold (seconds).</summary>
    public sealed class SpellCastTimeCountLeaf : Predicate
    {
        public SpellCastTimeCountLeaf(int count, Threshold threshold)
        {
            Count = PredicateArgs.NonNegative(count, nameof(count));
            Threshold = threshold ?? throw new ArgumentNullException(nameof(threshold));
        }

        public int Count { get; }
        public Threshold Threshold { get; }
        public override PredicateKind Kind => PredicateKind.SpellCastTimeCount;
        public override T Accept<T>(IPredicateVisitor<T> visitor) => visitor.Visit(this);

        public override JsonValue ToJson() =>
            JsonValue.Object(("spell_cast_time_count", JsonValue.Object((Threshold.JsonKey, Threshold.Value), ("count", Count))));
    }

    // ---- Slots and categories ------------------------------------------------------------------

    /// <summary>The Main Focus holds equipment of exactly this category.</summary>
    public sealed class MainFocusCategoryLeaf : Predicate
    {
        public MainFocusCategoryLeaf(EquipmentCategory category)
        {
            Category = category;
        }

        public EquipmentCategory Category { get; }
        public override PredicateKind Kind => PredicateKind.MainFocusCategory;
        public override T Accept<T>(IPredicateVisitor<T> visitor) => visitor.Visit(this);
        public override JsonValue ToJson() => JsonValue.Object(("main_focus_category", ContentText.CategoryName(Category)));
    }

    /// <summary>The Sidearm holds equipment of exactly this category.</summary>
    public sealed class SidearmCategoryLeaf : Predicate
    {
        public SidearmCategoryLeaf(EquipmentCategory category)
        {
            Category = category;
        }

        public EquipmentCategory Category { get; }
        public override PredicateKind Kind => PredicateKind.SidearmCategory;
        public override T Accept<T>(IPredicateVisitor<T> visitor) => visitor.Visit(this);
        public override JsonValue ToJson() => JsonValue.Object(("sidearm_category", ContentText.CategoryName(Category)));
    }

    /// <summary>The slot holds only its plain issued equipment.</summary>
    public sealed class IssuedSlotLeaf : Predicate
    {
        public IssuedSlotLeaf(EquipmentSlot slot)
        {
            Slot = slot;
        }

        public EquipmentSlot Slot { get; }
        public override PredicateKind Kind => PredicateKind.IssuedSlot;
        public override T Accept<T>(IPredicateVisitor<T> visitor) => visitor.Visit(this);
        public override JsonValue ToJson() => JsonValue.Object(("issued_slot", ContentText.SlotName(Slot)));
    }

    /// <summary>The occupied slot holds catalogue equipment of at least the stated tier.</summary>
    public sealed class SlotTierLeaf : Predicate
    {
        public SlotTierLeaf(EquipmentSlot slot, int atLeast)
        {
            Slot = slot;
            AtLeast = PredicateArgs.NonNegative(atLeast, nameof(atLeast));
        }

        public EquipmentSlot Slot { get; }
        public int AtLeast { get; }
        public override PredicateKind Kind => PredicateKind.SlotTier;
        public override T Accept<T>(IPredicateVisitor<T> visitor) => visitor.Visit(this);

        public override JsonValue ToJson() =>
            JsonValue.Object(("slot_tier", JsonValue.Object(("slot", ContentText.SlotName(Slot)), ("at_least", AtLeast))));
    }

    /// <summary>At least Count items of this category occupy legal slots; optionally with different non-empty Unique groups.</summary>
    public sealed class EquippedCategoryLeaf : Predicate
    {
        public EquippedCategoryLeaf(EquipmentCategory category, int count, bool distinctUniqueGroups, bool hasDistinctField = true)
        {
            Category = category;
            Count = PredicateArgs.NonNegative(count, nameof(count));
            DistinctUniqueGroups = distinctUniqueGroups;
            HasDistinctField = hasDistinctField || distinctUniqueGroups;
        }

        public EquipmentCategory Category { get; }
        public int Count { get; }
        public bool DistinctUniqueGroups { get; }
        public bool HasDistinctField { get; }
        public override PredicateKind Kind => PredicateKind.EquippedCategory;
        public override T Accept<T>(IPredicateVisitor<T> visitor) => visitor.Visit(this);

        public override JsonValue ToJson()
        {
            var fields = new List<KeyValuePair<string, JsonValue>>
            {
                PredicateArgs.Field("category", ContentText.CategoryName(Category)),
                PredicateArgs.Field("count", Count),
            };
            if (HasDistinctField) fields.Add(PredicateArgs.Field("distinct_unique_groups", DistinctUniqueGroups));
            return JsonValue.Object(("equipped_category", JsonValue.Object(fields)));
        }
    }

    /// <summary>Held purchased consumable charges do not exceed AtMost.</summary>
    public sealed class ConsumableChargesAtMostLeaf : Predicate
    {
        public ConsumableChargesAtMostLeaf(int atMost)
        {
            AtMost = PredicateArgs.NonNegative(atMost, nameof(atMost));
        }

        public int AtMost { get; }
        public override PredicateKind Kind => PredicateKind.ConsumableChargesAtMost;
        public override T Accept<T>(IPredicateVisitor<T> visitor) => visitor.Visit(this);
        public override JsonValue ToJson() => JsonValue.Object(("consumable_charges_at_most", AtMost));
    }

    // ---- Owned-augment counts --------------------------------------------------------------

    /// <summary>At least Count active parent definitions supply Tag; only enumerated Ids count when present.</summary>
    public sealed class AugmentTagCountLeaf : Predicate
    {
        public AugmentTagCountLeaf(string tag, int count, IEnumerable<string> ids = null)
        {
            Tag = PredicateArgs.Text(tag, nameof(tag));
            Count = PredicateArgs.NonNegative(count, nameof(count));
            IsRestricted = ids != null;
            Ids = PredicateArgs.Strings(ids, nameof(ids));
        }

        public string Tag { get; }
        public int Count { get; }

        /// <summary>Enumerated parents; meaningful only when <see cref="IsRestricted"/>.</summary>
        public IReadOnlyList<string> Ids { get; }

        public bool IsRestricted { get; }
        public override PredicateKind Kind => PredicateKind.AugmentTagCount;
        public override IReadOnlyList<string> DirectReferences => Ids;
        public override T Accept<T>(IPredicateVisitor<T> visitor) => visitor.Visit(this);

        public override JsonValue ToJson()
        {
            var fields = new List<KeyValuePair<string, JsonValue>>
            {
                PredicateArgs.Field("tag", Tag),
                PredicateArgs.Field("count", Count),
            };
            if (IsRestricted) fields.Add(PredicateArgs.Field("ids", JsonValue.StringArray(Ids)));
            return JsonValue.Object(("augment_tag_count", JsonValue.Object(fields)));
        }
    }

    /// <summary>At least Count active parents are at RankAtLeast or higher; optionally from different families.</summary>
    public sealed class AugmentRankCountLeaf : Predicate
    {
        public AugmentRankCountLeaf(int rankAtLeast, int count, bool distinctFamilies, IEnumerable<string> ids = null, bool hasDistinctField = true)
        {
            RankAtLeast = PredicateArgs.NonNegative(rankAtLeast, nameof(rankAtLeast));
            Count = PredicateArgs.NonNegative(count, nameof(count));
            DistinctFamilies = distinctFamilies;
            HasDistinctField = hasDistinctField || distinctFamilies;
            IsRestricted = ids != null;
            Ids = PredicateArgs.Strings(ids, nameof(ids));
        }

        public int RankAtLeast { get; }
        public int Count { get; }
        public bool DistinctFamilies { get; }
        public bool HasDistinctField { get; }
        public IReadOnlyList<string> Ids { get; }
        public bool IsRestricted { get; }
        public override PredicateKind Kind => PredicateKind.AugmentRankCount;
        public override IReadOnlyList<string> DirectReferences => Ids;
        public override T Accept<T>(IPredicateVisitor<T> visitor) => visitor.Visit(this);

        public override JsonValue ToJson()
        {
            var fields = new List<KeyValuePair<string, JsonValue>>
            {
                PredicateArgs.Field("rank_at_least", RankAtLeast),
                PredicateArgs.Field("count", Count),
            };
            if (HasDistinctField) fields.Add(PredicateArgs.Field("distinct_families", DistinctFamilies));
            if (IsRestricted) fields.Add(PredicateArgs.Field("ids", JsonValue.StringArray(Ids)));
            return JsonValue.Object(("augment_rank_count", JsonValue.Object(fields)));
        }
    }

    /// <summary>Active enumerated parents represent at least Count different families.</summary>
    public sealed class AugmentFamilyCountLeaf : Predicate
    {
        public AugmentFamilyCountLeaf(int count, IEnumerable<string> ids = null)
        {
            Count = PredicateArgs.NonNegative(count, nameof(count));
            IsRestricted = ids != null;
            Ids = PredicateArgs.Strings(ids, nameof(ids));
        }

        public int Count { get; }
        public IReadOnlyList<string> Ids { get; }
        public bool IsRestricted { get; }
        public override PredicateKind Kind => PredicateKind.AugmentFamilyCount;
        public override IReadOnlyList<string> DirectReferences => Ids;
        public override T Accept<T>(IPredicateVisitor<T> visitor) => visitor.Visit(this);

        public override JsonValue ToJson()
        {
            var fields = new List<KeyValuePair<string, JsonValue>> { PredicateArgs.Field("count", Count) };
            if (IsRestricted) fields.Add(PredicateArgs.Field("ids", JsonValue.StringArray(Ids)));
            return JsonValue.Object(("augment_family_count", JsonValue.Object(fields)));
        }
    }

    // ---- Match history (acquisition-only) ---------------------------------------------------

    /// <summary>
    /// The scheduled round number meets the threshold (acquisition-only). Thresholds use the Conclave schedule;
    /// evaluators compare against <see cref="Schedules.PredicateRound"/> so Duel maps by draft (DECISIONS D-029).
    /// </summary>
    public sealed class RoundAtLeastLeaf : Predicate
    {
        public RoundAtLeastLeaf(int round)
        {
            Round = PredicateArgs.NonNegative(round, nameof(round));
        }

        public int Round { get; }
        public override PredicateKind Kind => PredicateKind.RoundAtLeast;
        public override T Accept<T>(IPredicateVisitor<T> visitor) => visitor.Visit(this);
        public override JsonValue ToJson() => JsonValue.Object(("round_at_least", Round));
    }

    /// <summary>At least AtLeast previous completed duels in this match met the feat (acquisition-only; spec L280).</summary>
    public sealed class FeatLeaf : Predicate
    {
        public FeatLeaf(string featId, int atLeast)
        {
            FeatId = PredicateArgs.Id(featId, nameof(featId));
            AtLeast = PredicateArgs.NonNegative(atLeast, nameof(atLeast));
        }

        public string FeatId { get; }
        public int AtLeast { get; }
        public override PredicateKind Kind => PredicateKind.Feat;
        public override IReadOnlyList<string> DirectReferences => new[] { FeatId };
        public override T Accept<T>(IPredicateVisitor<T> visitor) => visitor.Visit(this);

        public override JsonValue ToJson() =>
            JsonValue.Object(("feat", JsonValue.Object(("id", FeatId), ("at_least", AtLeast))));
    }

    internal static class PredicateArgs
    {
        private static readonly IReadOnlyList<string> NoStrings = Array.AsReadOnly(new string[0]);

        public static IReadOnlyList<Predicate> Children(IEnumerable<Predicate> children, string node)
        {
            if (children == null) throw new ArgumentNullException(nameof(children));
            var list = new List<Predicate>();
            foreach (var child in children)
                list.Add(child ?? throw new ArgumentException("'" + node + "' children cannot be null."));
            if (list.Count == 0) throw new ArgumentException("'" + node + "' needs at least one child.");
            return list.AsReadOnly();
        }

        public static string Id(string value, string name)
        {
            if (string.IsNullOrEmpty(value)) throw new ArgumentException("An ID is required.", name);
            return value;
        }

        public static string Text(string value, string name)
        {
            if (string.IsNullOrEmpty(value)) throw new ArgumentException("A non-empty value is required.", name);
            return value;
        }

        public static int NonNegative(int value, string name)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(name, "Must be zero or greater.");
            return value;
        }

        public static IReadOnlyList<string> Strings(IEnumerable<string> values, string name)
        {
            if (values == null) return NoStrings;
            var list = new List<string>();
            foreach (var v in values) list.Add(Text(v, name));
            return list.Count == 0 ? NoStrings : list.AsReadOnly();
        }

        public static KeyValuePair<string, JsonValue> Field(string key, JsonValue value) => new KeyValuePair<string, JsonValue>(key, value);
    }
}
