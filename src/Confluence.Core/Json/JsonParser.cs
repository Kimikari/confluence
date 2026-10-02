using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Confluence.Core.Json
{
    /// <summary>
    /// Strict RFC 8259 recursive-descent parser. Rejects comments, trailing commas, single quotes, leading zeros,
    /// NaN/Infinity, unescaped control characters, unpaired surrogates, and duplicate object keys.
    /// A leading byte-order mark is ignored. Numbers use the invariant culture.
    /// </summary>
    public static class JsonParser
    {
        public const int MaxDepth = 256;

        public static JsonValue Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var reader = new Reader(text);
            return reader.ParseDocument();
        }

        public static bool TryParse(string text, out JsonValue value, out JsonParseException error)
        {
            try
            {
                value = Parse(text);
                error = null;
                return true;
            }
            catch (JsonParseException e)
            {
                value = null;
                error = e;
                return false;
            }
        }

        private sealed class Reader
        {
            private readonly string _text;
            private int _pos;
            private int _depth;
            private readonly StringBuilder _scratch = new StringBuilder();

            public Reader(string text)
            {
                _text = text;
                if (text.Length > 0 && text[0] == '﻿') _pos = 1;
            }

            public JsonValue ParseDocument()
            {
                SkipWhitespace();
                if (_pos >= _text.Length) throw Error(_pos, "empty document, expected a JSON value");
                var value = ParseValue();
                SkipWhitespace();
                if (_pos < _text.Length) throw Error(_pos, "unexpected " + Describe(_pos) + " after the top-level value");
                return value;
            }

            private JsonValue ParseValue()
            {
                if (_pos >= _text.Length) throw Error(_pos, "unexpected end of input, expected a value");
                char c = _text[_pos];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return JsonValue.From(ParseString());
                    case 't': ExpectLiteral("true"); return JsonValue.True;
                    case 'f': ExpectLiteral("false"); return JsonValue.False;
                    case 'n': ExpectLiteral("null"); return JsonValue.Null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                        throw Error(_pos, "unexpected " + Describe(_pos) + ", expected a value");
                }
            }

            private JsonValue ParseObject()
            {
                int start = _pos;
                Enter(start);
                _pos++; // '{'
                var members = new List<KeyValuePair<string, JsonValue>>();
                var index = new Dictionary<string, int>(StringComparer.Ordinal);
                SkipWhitespace();
                if (Peek() == '}')
                {
                    _pos++;
                    _depth--;
                    return JsonValue.CreateObjectOwned(members, index);
                }
                while (true)
                {
                    SkipWhitespace();
                    if (Peek() != '"')
                        throw Error(_pos, "expected a string member name, found " + Describe(_pos));
                    int keyPos = _pos;
                    string key = ParseString();
                    if (index.ContainsKey(key)) throw Error(keyPos, "duplicate object key \"" + key + "\"");
                    index.Add(key, members.Count);
                    SkipWhitespace();
                    if (Peek() != ':') throw Error(_pos, "expected ':' after member name, found " + Describe(_pos));
                    _pos++;
                    SkipWhitespace();
                    var value = ParseValue();
                    members.Add(new KeyValuePair<string, JsonValue>(key, value));
                    SkipWhitespace();
                    char c = Peek();
                    if (c == ',')
                    {
                        _pos++;
                        continue;
                    }
                    if (c == '}')
                    {
                        _pos++;
                        break;
                    }
                    throw Error(_pos, "expected ',' or '}' in object, found " + Describe(_pos));
                }
                _depth--;
                return JsonValue.CreateObjectOwned(members, index);
            }

            private JsonValue ParseArray()
            {
                Enter(_pos);
                _pos++; // '['
                var items = new List<JsonValue>();
                SkipWhitespace();
                if (Peek() == ']')
                {
                    _pos++;
                    _depth--;
                    return JsonValue.CreateArrayOwned(items);
                }
                while (true)
                {
                    SkipWhitespace();
                    items.Add(ParseValue());
                    SkipWhitespace();
                    char c = Peek();
                    if (c == ',')
                    {
                        _pos++;
                        continue;
                    }
                    if (c == ']')
                    {
                        _pos++;
                        break;
                    }
                    throw Error(_pos, "expected ',' or ']' in array, found " + Describe(_pos));
                }
                _depth--;
                return JsonValue.CreateArrayOwned(items);
            }

            private string ParseString()
            {
                int open = _pos;
                _pos++; // opening quote
                int runStart = _pos;
                _scratch.Clear();
                bool escaped = false;
                while (true)
                {
                    if (_pos >= _text.Length) throw Error(open, "unterminated string");
                    char c = _text[_pos];
                    if (c == '"')
                    {
                        string result;
                        if (!escaped) result = _text.Substring(runStart, _pos - runStart);
                        else
                        {
                            _scratch.Append(_text, runStart, _pos - runStart);
                            result = _scratch.ToString();
                        }
                        _pos++;
                        return result;
                    }
                    if (c == '\\')
                    {
                        _scratch.Append(_text, runStart, _pos - runStart);
                        escaped = true;
                        ParseEscape();
                        runStart = _pos;
                        continue;
                    }
                    if (c < 0x20) throw Error(_pos, "unescaped control character U+" + ((int)c).ToString("X4", CultureInfo.InvariantCulture) + " in string");
                    if (char.IsHighSurrogate(c))
                    {
                        if (_pos + 1 >= _text.Length || !char.IsLowSurrogate(_text[_pos + 1]))
                            throw Error(_pos, "unpaired high surrogate in string");
                        _pos += 2;
                        continue;
                    }
                    if (char.IsLowSurrogate(c)) throw Error(_pos, "unpaired low surrogate in string");
                    _pos++;
                }
            }

            // Appends the decoded escape at _pos (which points at the backslash) to _scratch.
            private void ParseEscape()
            {
                int at = _pos;
                if (_pos + 1 >= _text.Length) throw Error(at, "unterminated escape sequence");
                char e = _text[_pos + 1];
                _pos += 2;
                switch (e)
                {
                    case '"': _scratch.Append('"'); return;
                    case '\\': _scratch.Append('\\'); return;
                    case '/': _scratch.Append('/'); return;
                    case 'b': _scratch.Append('\b'); return;
                    case 'f': _scratch.Append('\f'); return;
                    case 'n': _scratch.Append('\n'); return;
                    case 'r': _scratch.Append('\r'); return;
                    case 't': _scratch.Append('\t'); return;
                    case 'u':
                    {
                        char unit = ReadHex4(at);
                        if (char.IsHighSurrogate(unit))
                        {
                            if (_pos + 1 < _text.Length && _text[_pos] == '\\' && _text[_pos + 1] == 'u')
                            {
                                int lowAt = _pos;
                                _pos += 2;
                                char low = ReadHex4(lowAt);
                                if (!char.IsLowSurrogate(low))
                                    throw Error(lowAt, "high surrogate \\u" + ((int)unit).ToString("X4", CultureInfo.InvariantCulture) + " must be followed by a low surrogate escape");
                                _scratch.Append(unit).Append(low);
                                return;
                            }
                            throw Error(at, "unpaired high surrogate escape \\u" + ((int)unit).ToString("X4", CultureInfo.InvariantCulture));
                        }
                        if (char.IsLowSurrogate(unit))
                            throw Error(at, "unpaired low surrogate escape \\u" + ((int)unit).ToString("X4", CultureInfo.InvariantCulture));
                        _scratch.Append(unit);
                        return;
                    }
                    default:
                        throw Error(at, "invalid escape sequence '\\" + Printable(e) + "'");
                }
            }

            private char ReadHex4(int escapeStart)
            {
                if (_pos + 4 > _text.Length) throw Error(escapeStart, "truncated \\u escape, expected four hex digits");
                int value = 0;
                for (int i = 0; i < 4; i++)
                {
                    char h = _text[_pos + i];
                    int digit;
                    if (h >= '0' && h <= '9') digit = h - '0';
                    else if (h >= 'a' && h <= 'f') digit = h - 'a' + 10;
                    else if (h >= 'A' && h <= 'F') digit = h - 'A' + 10;
                    else throw Error(_pos + i, "invalid hex digit '" + Printable(h) + "' in \\u escape");
                    value = (value << 4) | digit;
                }
                _pos += 4;
                return (char)value;
            }

            private JsonValue ParseNumber()
            {
                int start = _pos;
                if (_text[_pos] == '-') _pos++;
                if (_pos >= _text.Length) throw Error(start, "incomplete number, expected a digit after '-'");
                char c = _text[_pos];
                if (c == '0')
                {
                    _pos++;
                    if (_pos < _text.Length && IsDigit(_text[_pos])) throw Error(start, "leading zeros are not allowed in numbers");
                }
                else if (c >= '1' && c <= '9')
                {
                    while (_pos < _text.Length && IsDigit(_text[_pos])) _pos++;
                }
                else throw Error(_pos, "expected a digit in number, found " + Describe(_pos));

                bool integral = true;
                if (_pos < _text.Length && _text[_pos] == '.')
                {
                    integral = false;
                    _pos++;
                    if (_pos >= _text.Length || !IsDigit(_text[_pos])) throw Error(_pos, "expected a digit after the decimal point");
                    while (_pos < _text.Length && IsDigit(_text[_pos])) _pos++;
                }
                if (_pos < _text.Length && (_text[_pos] == 'e' || _text[_pos] == 'E'))
                {
                    integral = false;
                    _pos++;
                    if (_pos < _text.Length && (_text[_pos] == '+' || _text[_pos] == '-')) _pos++;
                    if (_pos >= _text.Length || !IsDigit(_text[_pos])) throw Error(_pos, "expected a digit in the exponent");
                    while (_pos < _text.Length && IsDigit(_text[_pos])) _pos++;
                }

                int length = _pos - start;
                double value;
                // Fast path for short integers, which dominate catalogue data.
                if (integral && length <= 15)
                {
                    bool negative = _text[start] == '-';
                    long acc = 0;
                    for (int i = negative ? start + 1 : start; i < _pos; i++) acc = acc * 10 + (_text[i] - '0');
                    value = negative ? -(double)acc : acc;
                    if (negative && acc == 0) value = -0.0;
                }
                else
                {
                    if (!double.TryParse(_text.AsSpan(start, length), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                        || double.IsInfinity(value) || double.IsNaN(value))
                        throw Error(start, "number '" + _text.Substring(start, length) + "' is out of range");
                }
                return JsonValue.From(value);
            }

            private void ExpectLiteral(string literal)
            {
                if (string.CompareOrdinal(_text, _pos, literal, 0, literal.Length) != 0)
                    throw Error(_pos, "invalid literal, expected '" + literal + "'");
                _pos += literal.Length;
            }

            private void SkipWhitespace()
            {
                while (_pos < _text.Length)
                {
                    char c = _text[_pos];
                    if (c == ' ' || c == '\n' || c == '\r' || c == '\t') _pos++;
                    else break;
                }
            }

            private char Peek() => _pos < _text.Length ? _text[_pos] : '\0';

            private void Enter(int at)
            {
                if (++_depth > MaxDepth) throw Error(at, "nesting deeper than " + MaxDepth + " levels");
            }

            private static bool IsDigit(char c) => c >= '0' && c <= '9';

            private string Describe(int at)
            {
                if (at >= _text.Length) return "end of input";
                return "'" + Printable(_text[at]) + "'";
            }

            private static string Printable(char c)
            {
                if (c < 0x20 || c == 0x7F) return "U+" + ((int)c).ToString("X4", CultureInfo.InvariantCulture);
                return c.ToString();
            }

            private JsonParseException Error(int offset, string message)
            {
                int line = 1;
                int column = 1;
                int limit = Math.Min(offset, _text.Length);
                for (int i = 0; i < limit; i++)
                {
                    char c = _text[i];
                    if (c == '\n')
                    {
                        line++;
                        column = 1;
                    }
                    else if (c == '\r')
                    {
                        if (i + 1 < _text.Length && _text[i + 1] == '\n') continue; // CRLF counts once
                        line++;
                        column = 1;
                    }
                    else column++;
                }
                return new JsonParseException(message, line, column, offset);
            }
        }
    }

    /// <summary>Parse failure with a 1-based line and column (UTF-16 code units) and a 0-based character offset.</summary>
    public sealed class JsonParseException : FormatException
    {
        public JsonParseException(string reason, int line, int column, int offset)
            : base("JSON parse error at line " + line + ", column " + column + ": " + reason + ".")
        {
            Reason = reason;
            Line = line;
            Column = column;
            Offset = offset;
        }

        public string Reason { get; }
        public int Line { get; }
        public int Column { get; }
        public int Offset { get; }
    }
}
