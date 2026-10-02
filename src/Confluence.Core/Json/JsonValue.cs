using System;
using System.Collections.Generic;
using System.Globalization;

namespace Confluence.Core.Json
{
    public enum JsonKind
    {
        Null,
        Bool,
        Number,
        String,
        Array,
        Object,
    }

    /// <summary>
    /// Immutable JSON document node. Objects keep member insertion order; lookups by key are O(1).
    /// Typed accessors throw <see cref="JsonAccessException"/> with the actual kind when the shape is wrong.
    /// </summary>
    public sealed class JsonValue : IEquatable<JsonValue>
    {
        private static readonly IReadOnlyList<JsonValue> EmptyArray = new JsonValue[0];
        private static readonly IReadOnlyList<KeyValuePair<string, JsonValue>> EmptyMembers = new KeyValuePair<string, JsonValue>[0];
        private static readonly IReadOnlyList<string> EmptyStrings = new string[0];

        public static readonly JsonValue Null = new JsonValue(JsonKind.Null);
        public static readonly JsonValue True = new JsonValue(JsonKind.Bool) { _bool = true };
        public static readonly JsonValue False = new JsonValue(JsonKind.Bool) { _bool = false };

        private bool _bool;
        private double _number;
        private string _string;
        private IReadOnlyList<JsonValue> _items;
        private IReadOnlyList<KeyValuePair<string, JsonValue>> _members;
        private Dictionary<string, int> _index;

        private JsonValue(JsonKind kind)
        {
            Kind = kind;
        }

        public JsonKind Kind { get; }

        public bool IsNull => Kind == JsonKind.Null;
        public bool IsBool => Kind == JsonKind.Bool;
        public bool IsNumber => Kind == JsonKind.Number;
        public bool IsString => Kind == JsonKind.String;
        public bool IsArray => Kind == JsonKind.Array;
        public bool IsObject => Kind == JsonKind.Object;

        /// <summary>Element count for arrays, member count for objects, zero otherwise.</summary>
        public int Count
        {
            get
            {
                if (Kind == JsonKind.Array) return _items.Count;
                if (Kind == JsonKind.Object) return _members.Count;
                return 0;
            }
        }

        // ---- Factories ----------------------------------------------------------------------

        public static JsonValue From(bool value) => value ? True : False;

        public static JsonValue From(string value)
        {
            if (value == null) return Null;
            return new JsonValue(JsonKind.String) { _string = value };
        }

        public static JsonValue From(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "JSON numbers must be finite.");
            return new JsonValue(JsonKind.Number) { _number = value };
        }

        public static JsonValue From(int value) => From((double)value);

        public static JsonValue From(long value) => From((double)value);

        public static JsonValue Array(IEnumerable<JsonValue> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            var list = new List<JsonValue>();
            foreach (var item in items) list.Add(item ?? Null);
            return CreateArrayOwned(list);
        }

        public static JsonValue Array(params JsonValue[] items) => Array((IEnumerable<JsonValue>)items);

        public static JsonValue StringArray(IEnumerable<string> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            var list = new List<JsonValue>();
            foreach (var item in items) list.Add(From(item));
            return CreateArrayOwned(list);
        }

        /// <summary>Builds an object from members in the given order. Duplicate keys are rejected.</summary>
        public static JsonValue Object(IEnumerable<KeyValuePair<string, JsonValue>> members)
        {
            if (members == null) throw new ArgumentNullException(nameof(members));
            var list = new List<KeyValuePair<string, JsonValue>>();
            foreach (var member in members)
            {
                if (member.Key == null) throw new ArgumentException("JSON object keys cannot be null.", nameof(members));
                list.Add(new KeyValuePair<string, JsonValue>(member.Key, member.Value ?? Null));
            }
            return CreateObjectOwned(list);
        }

        public static JsonValue Object(params (string Key, JsonValue Value)[] members)
        {
            var list = new List<KeyValuePair<string, JsonValue>>(members.Length);
            foreach (var (key, value) in members) list.Add(new KeyValuePair<string, JsonValue>(key, value));
            return Object(list);
        }

        public static implicit operator JsonValue(string value) => From(value);
        public static implicit operator JsonValue(double value) => From(value);
        public static implicit operator JsonValue(int value) => From(value);
        public static implicit operator JsonValue(long value) => From(value);
        public static implicit operator JsonValue(bool value) => From(value);

        // Used by the parser; takes ownership of the list without copying.
        internal static JsonValue CreateArrayOwned(List<JsonValue> items)
        {
            return new JsonValue(JsonKind.Array) { _items = items.Count == 0 ? EmptyArray : items };
        }

        // Takes ownership of the list without copying. Throws on duplicate keys.
        internal static JsonValue CreateObjectOwned(List<KeyValuePair<string, JsonValue>> members)
        {
            var index = new Dictionary<string, int>(members.Count, StringComparer.Ordinal);
            for (int i = 0; i < members.Count; i++)
            {
                if (index.ContainsKey(members[i].Key))
                    throw new ArgumentException("Duplicate JSON object key '" + members[i].Key + "'.");
                index.Add(members[i].Key, i);
            }
            return CreateObjectOwned(members, index);
        }

        // Used by the parser, which has already built and duplicate-checked the key index.
        internal static JsonValue CreateObjectOwned(List<KeyValuePair<string, JsonValue>> members, Dictionary<string, int> index)
        {
            return new JsonValue(JsonKind.Object)
            {
                _members = members.Count == 0 ? EmptyMembers : members,
                _index = index,
            };
        }

        // ---- Scalar accessors ---------------------------------------------------------------

        public string AsString()
        {
            Expect(JsonKind.String);
            return _string;
        }

        public double AsDouble()
        {
            Expect(JsonKind.Number);
            return _number;
        }

        /// <summary>The number as an int; throws if it has a fractional part or is out of range.</summary>
        public int AsInt()
        {
            Expect(JsonKind.Number);
            if (_number != Math.Floor(_number) || _number < int.MinValue || _number > int.MaxValue)
                throw new JsonAccessException("Expected an integer but found " + _number.ToString("R", CultureInfo.InvariantCulture) + ".");
            return (int)_number;
        }

        public long AsLong()
        {
            Expect(JsonKind.Number);
            if (_number != Math.Floor(_number) || _number < -9007199254740992d || _number > 9007199254740992d)
                throw new JsonAccessException("Expected an integer within +/-2^53 but found " + _number.ToString("R", CultureInfo.InvariantCulture) + ".");
            return (long)_number;
        }

        public bool AsBool()
        {
            Expect(JsonKind.Bool);
            return _bool;
        }

        public IReadOnlyList<JsonValue> AsArray()
        {
            Expect(JsonKind.Array);
            return _items;
        }

        /// <summary>Object members in document order.</summary>
        public IReadOnlyList<KeyValuePair<string, JsonValue>> AsObject()
        {
            Expect(JsonKind.Object);
            return _members;
        }

        /// <summary>Object keys in document order.</summary>
        public IEnumerable<string> Keys
        {
            get
            {
                Expect(JsonKind.Object);
                foreach (var member in _members) yield return member.Key;
            }
        }

        // ---- Object and array lookup --------------------------------------------------------

        public bool Has(string key)
        {
            return Kind == JsonKind.Object && _index.ContainsKey(key);
        }

        public bool TryGet(string key, out JsonValue value)
        {
            if (Kind == JsonKind.Object && _index.TryGetValue(key, out int i))
            {
                value = _members[i].Value;
                return true;
            }
            value = null;
            return false;
        }

        public JsonValue Get(string key)
        {
            Expect(JsonKind.Object);
            if (!_index.TryGetValue(key, out int i))
                throw new KeyNotFoundException("JSON object has no member '" + key + "'.");
            return _members[i].Value;
        }

        public JsonValue this[string key] => Get(key);

        public JsonValue this[int index]
        {
            get
            {
                Expect(JsonKind.Array);
                if (index < 0 || index >= _items.Count)
                    throw new ArgumentOutOfRangeException(nameof(index), "JSON array index " + index + " is outside 0.." + (_items.Count - 1) + ".");
                return _items[index];
            }
        }

        /// <summary>Member string, or <paramref name="defaultValue"/> when absent or null. Throws if present with another kind.</summary>
        public string GetString(string key, string defaultValue = null)
        {
            if (!TryGet(key, out var value) || value.IsNull) return defaultValue;
            return value.AsString();
        }

        public double GetDouble(string key, double defaultValue = 0)
        {
            if (!TryGet(key, out var value) || value.IsNull) return defaultValue;
            return value.AsDouble();
        }

        public int GetInt(string key, int defaultValue = 0)
        {
            if (!TryGet(key, out var value) || value.IsNull) return defaultValue;
            return value.AsInt();
        }

        public bool GetBool(string key, bool defaultValue = false)
        {
            if (!TryGet(key, out var value) || value.IsNull) return defaultValue;
            return value.AsBool();
        }

        /// <summary>Member array of strings; empty when absent or null. Throws if any element is not a string.</summary>
        public IReadOnlyList<string> GetStringList(string key)
        {
            if (!TryGet(key, out var value) || value.IsNull) return EmptyStrings;
            return value.AsStringList();
        }

        /// <summary>This array's elements as strings. Throws if this is not an array of strings.</summary>
        public IReadOnlyList<string> AsStringList()
        {
            var items = AsArray();
            if (items.Count == 0) return EmptyStrings;
            var result = new string[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Kind != JsonKind.String)
                    throw new JsonAccessException("Expected an array of strings but element " + i + " is " + items[i].Kind + ".");
                result[i] = items[i]._string;
            }
            return result;
        }

        private void Expect(JsonKind kind)
        {
            if (Kind != kind)
                throw new JsonAccessException("Expected JSON " + kind + " but found " + Kind + ".");
        }

        // ---- Equality and text --------------------------------------------------------------

        /// <summary>Structural equality. Object member order is ignored, array order is significant.</summary>
        public bool Equals(JsonValue other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other is null || other.Kind != Kind) return false;
            switch (Kind)
            {
                case JsonKind.Null: return true;
                case JsonKind.Bool: return _bool == other._bool;
                case JsonKind.Number: return _number.Equals(other._number) || _number == other._number;
                case JsonKind.String: return string.Equals(_string, other._string, StringComparison.Ordinal);
                case JsonKind.Array:
                    if (_items.Count != other._items.Count) return false;
                    for (int i = 0; i < _items.Count; i++)
                        if (!_items[i].Equals(other._items[i])) return false;
                    return true;
                case JsonKind.Object:
                    if (_members.Count != other._members.Count) return false;
                    foreach (var member in _members)
                    {
                        if (!other.TryGet(member.Key, out var otherValue) || !member.Value.Equals(otherValue)) return false;
                    }
                    return true;
                default:
                    return false;
            }
        }

        public override bool Equals(object obj) => Equals(obj as JsonValue);

        public override int GetHashCode()
        {
            unchecked
            {
                switch (Kind)
                {
                    case JsonKind.Bool: return _bool ? 3 : 5;
                    case JsonKind.Number: return _number == 0 ? 7 : _number.GetHashCode();
                    case JsonKind.String: return StringComparer.Ordinal.GetHashCode(_string);
                    case JsonKind.Array:
                    {
                        int hash = 17;
                        foreach (var item in _items) hash = hash * 31 + item.GetHashCode();
                        return hash;
                    }
                    case JsonKind.Object:
                    {
                        // Order-independent combination to match Equals.
                        int hash = 19;
                        foreach (var member in _members)
                            hash += StringComparer.Ordinal.GetHashCode(member.Key) ^ member.Value.GetHashCode();
                        return hash;
                    }
                    default: return 1;
                }
            }
        }

        public static bool operator ==(JsonValue a, JsonValue b) => a is null ? b is null : a.Equals(b);
        public static bool operator !=(JsonValue a, JsonValue b) => !(a == b);

        /// <summary>Compact JSON text.</summary>
        public override string ToString() => JsonWriter.Write(this, JsonWriterOptions.Compact);

        public string ToJson(bool indented = false) => JsonWriter.Write(this, indented ? JsonWriterOptions.Indented : JsonWriterOptions.Compact);
    }

    /// <summary>Thrown when a JSON value is read as the wrong kind or shape.</summary>
    public sealed class JsonAccessException : InvalidOperationException
    {
        public JsonAccessException(string message) : base(message)
        {
        }
    }
}
