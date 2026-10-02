using System;
using System.Collections.Generic;
using System.Globalization;

namespace Confluence.Arena.Modules
{
    /// <summary>
    /// Durability values parsed from a module's durability text, for example "Load-bearing terrain anchored;
    /// attached low cover 80 and center cover 120." gives {"attached low cover": 80, "center cover": 120} and marks
    /// load-bearing terrain anchored (spec L171, L686). Builders look values up by label so every number comes
    /// from the catalogue text.
    /// </summary>
    public sealed class ModuleDurability
    {
        private readonly List<KeyValuePair<string, float>> _entries = new List<KeyValuePair<string, float>>();

        public ModuleDurability(string text)
        {
            Text = text ?? string.Empty;
            foreach (string clause in Text.Split(new[] { ';', ',', '.' }, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (string raw in clause.Split(new[] { " and " }, StringSplitOptions.RemoveEmptyEntries))
                {
                    // "attached low cover 80", "Ice 45 per tile", "Separate edge cover has 80 durability".
                    string[] tokens = raw.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int t = tokens.Length - 1; t > 0; t--)
                    {
                        if (!float.TryParse(tokens[t], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                        {
                            continue;
                        }

                        int end = t;
                        if (end > 1 && string.Equals(tokens[end - 1], "has", StringComparison.OrdinalIgnoreCase))
                        {
                            end--;
                        }

                        string label = string.Join(" ", tokens, 0, end).ToLowerInvariant();
                        _entries.Add(new KeyValuePair<string, float>(label, value));
                        break;
                    }
                }
            }
        }

        public string Text { get; }

        public IReadOnlyList<KeyValuePair<string, float>> Entries => _entries;

        /// <summary>Value whose label contains the key (case-insensitive). Throws when the text has no such entry.</summary>
        public float Get(string key)
        {
            if (TryGet(key, out float value))
            {
                return value;
            }

            throw new InvalidOperationException("Durability text '" + Text + "' has no entry for '" + key + "'.");
        }

        public bool TryGet(string key, out float value)
        {
            string k = key.ToLowerInvariant();
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Key.IndexOf(k, StringComparison.Ordinal) >= 0)
                {
                    value = _entries[i].Value;
                    return true;
                }
            }

            value = 0f;
            return false;
        }

        /// <summary>True when the text declares load-bearing geometry or the ordinary floor anchored.</summary>
        public bool DeclaresAnchoredTerrain => Text.IndexOf("anchored", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>True if the value appears among the parsed entries.</summary>
        public bool Contains(float value)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (Math.Abs(_entries[i].Value - value) < 1e-4f)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
