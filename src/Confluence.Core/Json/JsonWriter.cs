using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Confluence.Core.Json
{
    public sealed class JsonWriterOptions
    {
        public static readonly JsonWriterOptions Compact = new JsonWriterOptions(false, 0, false);
        public static readonly JsonWriterOptions Indented = new JsonWriterOptions(true, 2, false);

        public JsonWriterOptions(bool indented, int indentSize, bool sortKeys, string newLine = "\n")
        {
            if (indentSize < 0) throw new ArgumentOutOfRangeException(nameof(indentSize));
            IsIndented = indented;
            IndentSize = indentSize;
            SortKeys = sortKeys;
            NewLine = newLine ?? "\n";
        }

        public bool IsIndented { get; }
        public int IndentSize { get; }

        /// <summary>When false (default) object members are written in insertion order; when true, ordinal key order.</summary>
        public bool SortKeys { get; }

        public string NewLine { get; }
    }

    /// <summary>
    /// Deterministic JSON text output: stable key order, invariant-culture shortest round-trip numbers,
    /// and RFC 8259 escaping. Output is identical on every runtime for the same value.
    /// </summary>
    public static class JsonWriter
    {
        public static string Write(JsonValue value, bool indented = false)
        {
            return Write(value, indented ? JsonWriterOptions.Indented : JsonWriterOptions.Compact);
        }

        public static string Write(JsonValue value, JsonWriterOptions options)
        {
            var sb = new StringBuilder();
            Write(value, sb, options);
            return sb.ToString();
        }

        public static void Write(JsonValue value, StringBuilder output, JsonWriterOptions options)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            WriteValue(value ?? JsonValue.Null, output, options ?? JsonWriterOptions.Compact, 0);
        }

        private static void WriteValue(JsonValue value, StringBuilder sb, JsonWriterOptions options, int depth)
        {
            switch (value.Kind)
            {
                case JsonKind.Null:
                    sb.Append("null");
                    break;
                case JsonKind.Bool:
                    sb.Append(value.AsBool() ? "true" : "false");
                    break;
                case JsonKind.Number:
                    sb.Append(FormatNumber(value.AsDouble()));
                    break;
                case JsonKind.String:
                    WriteString(value.AsString(), sb);
                    break;
                case JsonKind.Array:
                    WriteArray(value.AsArray(), sb, options, depth);
                    break;
                case JsonKind.Object:
                    WriteObject(value.AsObject(), sb, options, depth);
                    break;
            }
        }

        private static void WriteArray(IReadOnlyList<JsonValue> items, StringBuilder sb, JsonWriterOptions options, int depth)
        {
            if (items.Count == 0)
            {
                sb.Append("[]");
                return;
            }
            sb.Append('[');
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append(',');
                NewLine(sb, options, depth + 1);
                WriteValue(items[i], sb, options, depth + 1);
            }
            NewLine(sb, options, depth);
            sb.Append(']');
        }

        private static void WriteObject(IReadOnlyList<KeyValuePair<string, JsonValue>> members, StringBuilder sb, JsonWriterOptions options, int depth)
        {
            if (members.Count == 0)
            {
                sb.Append("{}");
                return;
            }
            IReadOnlyList<KeyValuePair<string, JsonValue>> ordered = members;
            if (options.SortKeys)
            {
                var sorted = new List<KeyValuePair<string, JsonValue>>(members);
                sorted.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
                ordered = sorted;
            }
            sb.Append('{');
            for (int i = 0; i < ordered.Count; i++)
            {
                if (i > 0) sb.Append(',');
                NewLine(sb, options, depth + 1);
                WriteString(ordered[i].Key, sb);
                sb.Append(options.IsIndented ? ": " : ":");
                WriteValue(ordered[i].Value, sb, options, depth + 1);
            }
            NewLine(sb, options, depth);
            sb.Append('}');
        }

        private static void NewLine(StringBuilder sb, JsonWriterOptions options, int depth)
        {
            if (!options.IsIndented) return;
            sb.Append(options.NewLine);
            sb.Append(' ', depth * options.IndentSize);
        }

        /// <summary>Integers within +/-2^53 print without a fraction; other values use the shortest round-trip form.</summary>
        public static string FormatNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "JSON cannot represent NaN or Infinity.");
            if (value == Math.Floor(value) && Math.Abs(value) <= 9007199254740992d)
                return ((long)value).ToString(CultureInfo.InvariantCulture);
            string text = value.ToString("R", CultureInfo.InvariantCulture);
            // Older runtimes can produce a non-round-tripping "R" string; G17 is always exact.
            if (double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture) != value)
                text = value.ToString("G17", CultureInfo.InvariantCulture);
            return text;
        }

        public static string EscapeString(string value)
        {
            var sb = new StringBuilder(value.Length + 2);
            WriteString(value, sb);
            return sb.ToString();
        }

        private static void WriteString(string value, StringBuilder sb)
        {
            sb.Append('"');
            int runStart = 0;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                string escape = null;
                switch (c)
                {
                    case '"': escape = "\\\""; break;
                    case '\\': escape = "\\\\"; break;
                    case '\b': escape = "\\b"; break;
                    case '\f': escape = "\\f"; break;
                    case '\n': escape = "\\n"; break;
                    case '\r': escape = "\\r"; break;
                    case '\t': escape = "\\t"; break;
                    default:
                        if (c < 0x20) escape = UnicodeEscape(c);
                        else if (char.IsHighSurrogate(c))
                        {
                            if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                            {
                                i++; // valid pair, copied verbatim
                                continue;
                            }
                            escape = UnicodeEscape(c); // keep output valid even for malformed UTF-16
                        }
                        else if (char.IsLowSurrogate(c)) escape = UnicodeEscape(c);
                        break;
                }
                if (escape == null) continue;
                sb.Append(value, runStart, i - runStart);
                sb.Append(escape);
                runStart = i + 1;
            }
            sb.Append(value, runStart, value.Length - runStart);
            sb.Append('"');
        }

        private static string UnicodeEscape(char c)
        {
            return "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture);
        }
    }
}
