using System;
using System.Collections.Generic;
using Confluence.Core.Json;
using Confluence.Core.Rules;

namespace Confluence.Core.Content
{
    /// <summary>Malformed eligibility JSON. The message names the owning record and the JSON path.</summary>
    public sealed class PredicateFormatException : FormatException
    {
        public PredicateFormatException(string ownerId, string path, string reason)
            : base((ownerId ?? "predicate") + " eligibility at " + path + ": " + reason)
        {
            OwnerId = ownerId;
            Path = path;
            Reason = reason;
        }

        public string OwnerId { get; }
        public string Path { get; }
        public string Reason { get; }
    }

    /// <summary>
    /// Strict parser for the canonical eligibility JSON. Each node is an object with exactly one key; unknown keys,
    /// unknown fields, wrong kinds, and negative counts are errors that name the owning record.
    /// </summary>
    public static class PredicateParser
    {
        public const int MaxDepth = 32;

        public static Predicate Parse(JsonValue json, string ownerId)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            return ParseNode(json, "$", ownerId, 0);
        }

        public static Predicate Parse(string jsonText, string ownerId)
        {
            return Parse(JsonParser.Parse(jsonText), ownerId);
        }

        private static Predicate ParseNode(JsonValue json, string path, string owner, int depth)
        {
            if (depth > MaxDepth) throw new PredicateFormatException(owner, path, "predicate nested deeper than " + MaxDepth + " levels");
            if (!json.IsObject) throw new PredicateFormatException(owner, path, "a predicate must be an object, found " + json.Kind);
            if (json.Count != 1)
                throw new PredicateFormatException(owner, path, "a predicate object must have exactly one key, found " + json.Count);

            var member = json.AsObject()[0];
            string key = member.Key;
            var value = member.Value;
            string here = path + "." + key;
            if (!PredicateKinds.TryParseKey(key, out var kind))
                throw new PredicateFormatException(owner, path, "unknown predicate node '" + key + "'");

            switch (kind)
            {
                case PredicateKind.All:
                    return new AllNode(ParseChildren(value, here, owner, depth));
                case PredicateKind.Any:
                    return new AnyNode(ParseChildren(value, here, owner, depth));
                case PredicateKind.Not:
                    if (!value.IsObject) throw new PredicateFormatException(owner, here, "'not' takes one predicate object, found " + value.Kind);
                    return new NotNode(ParseNode(value, here, owner, depth + 1));
                case PredicateKind.Always:
                    if (!value.IsBool || !value.AsBool()) throw new PredicateFormatException(owner, here, "'always' must be true");
                    return AlwaysLeaf.Instance;
                case PredicateKind.Wizard:
                    return new WizardLeaf(BareString(value, here, owner));
                case PredicateKind.EquippedItem:
                    return new EquippedItemLeaf(BareString(value, here, owner));
                case PredicateKind.OwnedItem:
                    return new OwnedItemLeaf(BareString(value, here, owner));
                case PredicateKind.Spell:
                    return new SpellLeaf(BareString(value, here, owner));
                case PredicateKind.MainFocusCategory:
                    return new MainFocusCategoryLeaf(Category(BareString(value, here, owner), here, owner));
                case PredicateKind.SidearmCategory:
                    return new SidearmCategoryLeaf(Category(BareString(value, here, owner), here, owner));
                case PredicateKind.IssuedSlot:
                    return new IssuedSlotLeaf(Slot(BareString(value, here, owner), here, owner));
                case PredicateKind.ConsumableChargesAtMost:
                    return new ConsumableChargesAtMostLeaf(BareCount(value, here, owner));
                case PredicateKind.RoundAtLeast:
                    return new RoundAtLeastLeaf(BareCount(value, here, owner));
                default:
                    return ParseFieldLeaf(kind, value, here, owner);
            }
        }

        private static Predicate ParseFieldLeaf(PredicateKind kind, JsonValue value, string path, string owner)
        {
            var f = new Fields(value, path, owner);
            Predicate result;
            switch (kind)
            {
                case PredicateKind.EquippedTag:
                    result = new EquippedTagLeaf(f.Text("tag"), f.Count("count"));
                    break;
                case PredicateKind.Augment:
                    result = new AugmentLeaf(f.Text("id"), f.Count("rank"));
                    break;
                case PredicateKind.SpellTag:
                    result = new SpellTagLeaf(f.Text("tag"), f.Count("count"), f.OptionalTextList("also"));
                    break;
                case PredicateKind.SpellTagExact:
                    result = new SpellTagExactLeaf(f.Text("tag"), f.Count("count"));
                    break;
                case PredicateKind.SpellDistinctTags:
                    result = new SpellDistinctTagsLeaf(f.TextList("set"), f.Count("count"), f.OptionalTextList("also"));
                    break;
                case PredicateKind.SpellCooldownCount:
                    result = new SpellCooldownCountLeaf(f.Count("count"), f.Bound());
                    break;
                case PredicateKind.SpellCastTimeCount:
                    result = new SpellCastTimeCountLeaf(f.Count("count"), f.Bound());
                    break;
                case PredicateKind.SlotTier:
                    result = new SlotTierLeaf(Slot(f.Text("slot"), path + ".slot", owner), f.Count("at_least"));
                    break;
                case PredicateKind.EquippedCategory:
                {
                    var category = Category(f.Text("category"), path + ".category", owner);
                    int count = f.Count("count");
                    bool present = f.Has("distinct_unique_groups");
                    result = new EquippedCategoryLeaf(category, count, f.OptionalBool("distinct_unique_groups"), present);
                    break;
                }
                case PredicateKind.AugmentTagCount:
                    result = new AugmentTagCountLeaf(f.Text("tag"), f.Count("count"), f.OptionalTextList("ids"));
                    break;
                case PredicateKind.AugmentRankCount:
                {
                    int rank = f.Count("rank_at_least");
                    int count = f.Count("count");
                    bool present = f.Has("distinct_families");
                    bool distinct = f.OptionalBool("distinct_families");
                    result = new AugmentRankCountLeaf(rank, count, distinct, f.OptionalTextList("ids"), present);
                    break;
                }
                case PredicateKind.AugmentFamilyCount:
                    result = new AugmentFamilyCountLeaf(f.Count("count"), f.OptionalTextList("ids"));
                    break;
                case PredicateKind.Feat:
                    result = new FeatLeaf(f.Text("id"), f.Count("at_least"));
                    break;
                default:
                    throw new PredicateFormatException(owner, path, "unsupported predicate '" + PredicateKinds.JsonKey(kind) + "'");
            }
            f.Finish();
            return result;
        }

        private static List<Predicate> ParseChildren(JsonValue value, string path, string owner, int depth)
        {
            if (!value.IsArray) throw new PredicateFormatException(owner, path, "expected an array of predicates, found " + value.Kind);
            var items = value.AsArray();
            if (items.Count == 0) throw new PredicateFormatException(owner, path, "needs at least one child predicate");
            var children = new List<Predicate>(items.Count);
            for (int i = 0; i < items.Count; i++) children.Add(ParseNode(items[i], path + "[" + i + "]", owner, depth + 1));
            return children;
        }

        private static string BareString(JsonValue value, string path, string owner)
        {
            if (!value.IsString || value.AsString().Length == 0)
                throw new PredicateFormatException(owner, path, "expected a non-empty string, found " + Shape(value));
            return value.AsString();
        }

        private static int BareCount(JsonValue value, string path, string owner)
        {
            return ToCount(value, path, owner);
        }

        private static int ToCount(JsonValue value, string path, string owner)
        {
            if (!value.IsNumber) throw new PredicateFormatException(owner, path, "expected a non-negative integer, found " + Shape(value));
            double d = value.AsDouble();
            if (d != Math.Floor(d) || d < 0 || d > int.MaxValue)
                throw new PredicateFormatException(owner, path, "expected a non-negative integer, found " + JsonWriter.FormatNumber(d));
            return (int)d;
        }

        private static EquipmentCategory Category(string text, string path, string owner)
        {
            if (!ContentText.TryParseEquipmentCategory(text, out var category))
                throw new PredicateFormatException(owner, path, "unknown equipment category '" + text + "'");
            return category;
        }

        private static EquipmentSlot Slot(string text, string path, string owner)
        {
            if (!ContentText.TryParseSlotName(text, out var slot))
                throw new PredicateFormatException(owner, path, "unknown equipment slot '" + text + "'");
            return slot;
        }

        private static string Shape(JsonValue value) => value.IsString ? "\"" + value.AsString() + "\"" : value.Kind.ToString();

        /// <summary>Field reader for an object-valued leaf; rejects unknown fields on Finish.</summary>
        private sealed class Fields
        {
            private readonly JsonValue _json;
            private readonly string _path;
            private readonly string _owner;
            private readonly HashSet<string> _used = new HashSet<string>(StringComparer.Ordinal);

            public Fields(JsonValue json, string path, string owner)
            {
                if (!json.IsObject) throw new PredicateFormatException(owner, path, "expected an object of fields, found " + Shape(json));
                _json = json;
                _path = path;
                _owner = owner;
            }

            public bool Has(string key) => _json.Has(key);

            public string Text(string key)
            {
                return BareString(Required(key), _path + "." + key, _owner);
            }

            public int Count(string key)
            {
                return ToCount(Required(key), _path + "." + key, _owner);
            }

            public bool OptionalBool(string key)
            {
                _used.Add(key);
                if (!_json.TryGet(key, out var value)) return false;
                if (!value.IsBool) throw new PredicateFormatException(_owner, _path + "." + key, "expected a boolean, found " + Shape(value));
                return value.AsBool();
            }

            public List<string> TextList(string key)
            {
                return ToTextList(Required(key), _path + "." + key);
            }

            /// <summary>Null when the field is absent (distinguishes "no filter" from an explicit list).</summary>
            public List<string> OptionalTextList(string key)
            {
                _used.Add(key);
                if (!_json.TryGet(key, out var value)) return null;
                return ToTextList(value, _path + "." + key);
            }

            /// <summary>Exactly one of at_least / at_most.</summary>
            public Threshold Bound()
            {
                bool hasLeast = _json.Has("at_least");
                bool hasMost = _json.Has("at_most");
                _used.Add("at_least");
                _used.Add("at_most");
                if (hasLeast == hasMost)
                    throw new PredicateFormatException(_owner, _path, "expected exactly one of 'at_least' or 'at_most'");
                string key = hasLeast ? "at_least" : "at_most";
                var value = _json.Get(key);
                if (!value.IsNumber || value.AsDouble() < 0)
                    throw new PredicateFormatException(_owner, _path + "." + key, "expected a non-negative number, found " + Shape(value));
                return new Threshold(hasLeast ? BoundKind.AtLeast : BoundKind.AtMost, value.AsDouble());
            }

            public void Finish()
            {
                foreach (var key in _json.Keys)
                    if (!_used.Contains(key)) throw new PredicateFormatException(_owner, _path, "unknown field '" + key + "'");
            }

            private JsonValue Required(string key)
            {
                _used.Add(key);
                if (!_json.TryGet(key, out var value)) throw new PredicateFormatException(_owner, _path, "missing required field '" + key + "'");
                return value;
            }

            private List<string> ToTextList(JsonValue value, string path)
            {
                if (!value.IsArray) throw new PredicateFormatException(_owner, path, "expected an array of strings, found " + Shape(value));
                var items = value.AsArray();
                var result = new List<string>(items.Count);
                for (int i = 0; i < items.Count; i++) result.Add(BareString(items[i], path + "[" + i + "]", _owner));
                return result;
            }
        }
    }
}
