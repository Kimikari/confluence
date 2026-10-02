using System;
using System.Collections.Generic;

namespace Confluence.Economy
{
    /// <summary>
    /// Per-player record of processed transaction requests for the whole match (spec L429 "prevents duplicate purchase
    /// requests"). A repeated request ID with the same operation returns the stored result without re-applying it; the
    /// same ID with a different operation is rejected. Every processed request is stored, including failures, so a
    /// retransmission always sees the original outcome.
    /// </summary>
    internal sealed class RequestLedger
    {
        private readonly Dictionary<long, Entry> _entries = new Dictionary<long, Entry>();

        public int Count => _entries.Count;

        public static bool IsValidId(long requestId) => requestId > 0;

        /// <summary>Looks up a previous request. Returns true when the ID was seen; conflict tells whether the operation differed.</summary>
        public bool TryGet(long requestId, string fingerprint, out object result, out bool conflict)
        {
            if (_entries.TryGetValue(requestId, out var entry))
            {
                conflict = !string.Equals(entry.Fingerprint, fingerprint, StringComparison.Ordinal);
                result = conflict ? null : entry.Result;
                return true;
            }
            result = null;
            conflict = false;
            return false;
        }

        public void Record(long requestId, string fingerprint, object result)
        {
            if (!IsValidId(requestId)) throw new ArgumentOutOfRangeException(nameof(requestId));
            _entries.Add(requestId, new Entry(fingerprint, result));
        }

        private sealed class Entry
        {
            public Entry(string fingerprint, object result)
            {
                Fingerprint = fingerprint;
                Result = result;
            }

            public string Fingerprint { get; }
            public object Result { get; }
        }
    }
}
