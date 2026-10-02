using System;
using System.Collections.Generic;

namespace Confluence.Casting
{
    /// <summary>
    /// Per-spell grammar parameters (a fixed motif, vessel target, weight ratio, dial reference...). Grammar data is
    /// fixed per spell for the whole match (spec L478 and e.g. WIZ-017, WIZ-024, WIZ-021). Keys are kept in ordinal
    /// order so enumeration is deterministic.
    /// </summary>
    public sealed class GrammarParameters
    {
        private readonly string[] _keys;
        private readonly double[] _values;

        private GrammarParameters(string[] keys, double[] values)
        {
            _keys = keys;
            _values = values;
        }

        public static GrammarParameters Empty { get; } = new GrammarParameters(new string[0], new double[0]);

        public int Count => _keys.Length;

        public string KeyAt(int index) => _keys[index];

        public double ValueAt(int index) => _values[index];

        public bool TryGet(string key, out double value)
        {
            int i = Array.BinarySearch(_keys, key, StringComparer.Ordinal);
            if (i >= 0)
            {
                value = _values[i];
                return true;
            }

            value = 0.0;
            return false;
        }

        public double Get(string key, double fallback) => TryGet(key, out double v) ? v : fallback;

        /// <summary>A copy with <paramref name="key"/> set to <paramref name="value"/>.</summary>
        public GrammarParameters With(string key, double value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            var keys = new List<string>(_keys);
            var values = new List<double>(_values);
            int i = Array.BinarySearch(_keys, key, StringComparer.Ordinal);
            if (i >= 0)
            {
                values[i] = value;
            }
            else
            {
                keys.Insert(~i, key);
                values.Insert(~i, value);
            }

            return new GrammarParameters(keys.ToArray(), values.ToArray());
        }
    }
}
