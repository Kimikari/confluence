using System;
using System.Collections.Generic;

namespace Confluence.Core.Content
{
    /// <summary>
    /// Which catalogue records may appear in draft and shop pools. Only implemented records are offered, so a
    /// menu entry is never an empty handler (DECISIONS D-010).
    /// </summary>
    public interface IContentAvailability
    {
        bool IsAvailable(string id);
    }

    public static class ContentAvailability
    {
        /// <summary>Every ID is available (full-catalogue tooling and tests).</summary>
        public static readonly IContentAvailability All = new Constant(true);

        /// <summary>Nothing is available.</summary>
        public static readonly IContentAvailability None = new Constant(false);

        /// <summary>Exactly the given IDs are available.</summary>
        public static IContentAvailability FromIds(IEnumerable<string> ids)
        {
            if (ids == null) throw new ArgumentNullException(nameof(ids));
            return new IdSet(ids);
        }

        private sealed class Constant : IContentAvailability
        {
            private readonly bool _value;

            public Constant(bool value)
            {
                _value = value;
            }

            public bool IsAvailable(string id) => _value;
        }

        private sealed class IdSet : IContentAvailability
        {
            private readonly HashSet<string> _ids;

            public IdSet(IEnumerable<string> ids)
            {
                _ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var id in ids)
                    if (!string.IsNullOrEmpty(id)) _ids.Add(id);
            }

            public bool IsAvailable(string id) => id != null && _ids.Contains(id);
        }
    }
}
