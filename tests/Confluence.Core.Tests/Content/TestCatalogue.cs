using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Confluence.Core.Content;
using Confluence.Core.Json;

namespace Confluence.Core.Tests.Content
{
    /// <summary>The supplied catalogue, parsed once per test run, plus helpers that derive edited copies.</summary>
    internal static class TestCatalogue
    {
        private static readonly Lazy<string> LazyText = new Lazy<string>(() => File.ReadAllText(Catalogue.FindDefaultPath()));
        private static readonly Lazy<JsonValue> LazyJson = new Lazy<JsonValue>(() => JsonParser.Parse(LazyText.Value));
        private static readonly Lazy<Catalogue> LazyCatalogue = new Lazy<Catalogue>(() => Catalogue.FromJson(LazyJson.Value));

        public static string Text => LazyText.Value;
        public static JsonValue Source => LazyJson.Value;
        public static Catalogue Real => LazyCatalogue.Value;

        public static JsonValue Parse(string json) => JsonParser.Parse(json);

        /// <summary>Copy of the catalogue JSON with one record replaced by <paramref name="update"/>'s result.</summary>
        public static JsonValue EditRecord(JsonValue root, string id, Func<JsonValue, JsonValue> update)
        {
            var content = root["content"];
            foreach (var list in content.AsObject())
            {
                var items = list.Value.AsArray();
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].GetString("id") != id) continue;
                    var copy = items.ToList();
                    copy[i] = update(items[i]);
                    return root.With("content", content.With(list.Key, JsonValue.Array(copy)));
                }
            }
            throw new ArgumentException("No record " + id);
        }

        public static JsonValue EditRecord(JsonValue root, string id, string field, JsonValue value) =>
            EditRecord(root, id, r => r.With(field, value));

        public static JsonValue EditList(JsonValue root, string listKey, Func<List<JsonValue>, IEnumerable<JsonValue>> update)
        {
            var content = root["content"];
            var items = content[listKey].AsArray().ToList();
            return root.With("content", content.With(listKey, JsonValue.Array(update(items))));
        }

        public static JsonValue With(this JsonValue obj, string key, JsonValue value)
        {
            var members = obj.AsObject().ToList();
            int index = members.FindIndex(m => m.Key == key);
            var member = new KeyValuePair<string, JsonValue>(key, value);
            if (index >= 0) members[index] = member;
            else members.Add(member);
            return JsonValue.Object(members);
        }

        public static JsonValue Without(this JsonValue obj, string key)
        {
            return JsonValue.Object(obj.AsObject().Where(m => m.Key != key));
        }

        public static JsonValue Strings(params string[] values) => JsonValue.StringArray(values);
    }
}
