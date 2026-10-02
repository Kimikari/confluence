using System;
using System.Collections.Generic;
using Confluence.Core.Json;

namespace Confluence.Core.Content
{
    /// <summary>Common shape of every immutable catalogue definition.</summary>
    public abstract class ContentDef
    {
        protected ContentDef(string id, string name, IReadOnlyList<string> tags, JsonValue raw)
        {
            Id = id;
            Name = name;
            Tags = tags;
            Raw = raw;
        }

        public abstract ContentType Type { get; }

        public string Id { get; }
        public string Name { get; }
        public IReadOnlyList<string> Tags { get; }

        /// <summary>The source JSON record, unchanged.</summary>
        public JsonValue Raw { get; }

        /// <summary>Exact (ordinal) tag membership.</summary>
        public bool HasTag(string tag)
        {
            if (tag == null) return false;
            for (int i = 0; i < Tags.Count; i++)
                if (string.Equals(Tags[i], tag, StringComparison.Ordinal)) return true;
            return false;
        }

        public override string ToString() => Id + " " + Name;
    }
}
