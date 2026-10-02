using System;
using System.Collections.Generic;
using Confluence.Core.Json;

namespace Confluence.Core.Content
{
    /// <summary>Structural problem in the catalogue JSON: wrong kinds, missing required fields, or unknown fields.</summary>
    public sealed class CatalogueFormatException : FormatException
    {
        public CatalogueFormatException(string recordId, string message, Exception inner = null)
            : base(Prefix(recordId, message), inner)
        {
            RecordId = recordId;
        }

        private static string Prefix(string recordId, string message)
        {
            if (recordId == null || message.StartsWith(recordId, StringComparison.Ordinal)) return message;
            return recordId + ": " + message;
        }

        /// <summary>Catalogue ID (or location such as "content.spells[3]") of the failing record; null for document-level errors.</summary>
        public string RecordId { get; }
    }

    /// <summary>
    /// Strict reader for one catalogue record object. Every field must be consumed; <see cref="Finish"/> rejects
    /// unknown fields so a renamed or added field can never be silently ignored.
    /// </summary>
    internal sealed class RecordReader
    {
        private static readonly IReadOnlyList<string> NoStrings = Array.AsReadOnly(new string[0]);

        private readonly JsonValue _json;
        private readonly HashSet<string> _consumed = new HashSet<string>(StringComparer.Ordinal);

        public RecordReader(JsonValue json, string location)
        {
            if (json == null || !json.IsObject)
                throw new CatalogueFormatException(location, "record must be a JSON object, found " + (json == null ? "nothing" : json.Kind.ToString()));
            _json = json;
            Location = location;
            if (json.Has("id"))
            {
                var id = json.Get("id");
                if (!id.IsString || id.AsString().Length == 0) throw new CatalogueFormatException(location, "field 'id' must be a non-empty string");
                Id = id.AsString();
                Location = Id;
            }
            _consumed.Add("id");
        }

        public string Id { get; }
        public string Location { get; }
        public JsonValue Json => _json;

        public string RequireId()
        {
            if (Id == null) throw Fail("missing required field 'id'");
            return Id;
        }

        public string String(string key)
        {
            var value = Required(key);
            if (!value.IsString) throw WrongKind(key, "a string", value);
            return value.AsString();
        }

        public string OptionalString(string key)
        {
            if (!Optional(key, out var value)) return null;
            if (!value.IsString) throw WrongKind(key, "a string", value);
            return value.AsString();
        }

        public double Number(string key)
        {
            var value = Required(key);
            if (!value.IsNumber) throw WrongKind(key, "a number", value);
            return value.AsDouble();
        }

        public double? OptionalNumber(string key)
        {
            if (!Optional(key, out var value)) return null;
            if (!value.IsNumber) throw WrongKind(key, "a number", value);
            return value.AsDouble();
        }

        public int Int(string key)
        {
            var value = Required(key);
            return ToInt(key, value);
        }

        public int? OptionalInt(string key)
        {
            if (!Optional(key, out var value)) return null;
            return ToInt(key, value);
        }

        public bool Bool(string key)
        {
            var value = Required(key);
            if (!value.IsBool) throw WrongKind(key, "a boolean", value);
            return value.AsBool();
        }

        public IReadOnlyList<string> StringList(string key)
        {
            return ToStringList(key, Required(key));
        }

        public IReadOnlyList<string> OptionalStringList(string key)
        {
            if (!Optional(key, out var value)) return NoStrings;
            return ToStringList(key, value);
        }

        public IReadOnlyList<int> IntList(string key)
        {
            var value = Required(key);
            if (!value.IsArray) throw WrongKind(key, "an array of integers", value);
            var items = value.AsArray();
            var result = new int[items.Count];
            for (int i = 0; i < items.Count; i++) result[i] = ToInt(key + "[" + i + "]", items[i]);
            return Array.AsReadOnly(result);
        }

        public JsonValue Object(string key)
        {
            var value = Required(key);
            if (!value.IsObject) throw WrongKind(key, "an object", value);
            return value;
        }

        public JsonValue OptionalObject(string key)
        {
            if (!Optional(key, out var value)) return null;
            if (!value.IsObject) throw WrongKind(key, "an object", value);
            return value;
        }

        /// <summary>Rejects any field that was not read.</summary>
        public void Finish()
        {
            foreach (var key in _json.Keys)
            {
                if (!_consumed.Contains(key)) throw Fail("unknown field '" + key + "'");
            }
        }

        public CatalogueFormatException Fail(string message, Exception inner = null)
        {
            return new CatalogueFormatException(Location, message, inner);
        }

        private JsonValue Required(string key)
        {
            _consumed.Add(key);
            if (!_json.TryGet(key, out var value)) throw Fail("missing required field '" + key + "'");
            return value;
        }

        private bool Optional(string key, out JsonValue value)
        {
            _consumed.Add(key);
            return _json.TryGet(key, out value) && !value.IsNull;
        }

        private int ToInt(string key, JsonValue value)
        {
            if (!value.IsNumber) throw WrongKind(key, "an integer", value);
            double d = value.AsDouble();
            if (d != Math.Floor(d) || d < int.MinValue || d > int.MaxValue) throw Fail("field '" + key + "' must be an integer, found " + d);
            return (int)d;
        }

        private IReadOnlyList<string> ToStringList(string key, JsonValue value)
        {
            if (!value.IsArray) throw WrongKind(key, "an array of strings", value);
            var items = value.AsArray();
            if (items.Count == 0) return NoStrings;
            var result = new string[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                if (!items[i].IsString) throw Fail("field '" + key + "' element " + i + " must be a string, found " + items[i].Kind);
                result[i] = items[i].AsString();
            }
            return Array.AsReadOnly(result);
        }

        private CatalogueFormatException WrongKind(string key, string expected, JsonValue actual)
        {
            return Fail("field '" + key + "' must be " + expected + ", found " + actual.Kind);
        }
    }
}
