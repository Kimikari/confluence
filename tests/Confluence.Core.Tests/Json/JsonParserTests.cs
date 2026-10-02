using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using Confluence.Core.Content;
using Confluence.Core.Json;
using Xunit.Abstractions;

namespace Confluence.Core.Tests.Json
{
    public class JsonParserTests
    {
        private readonly ITestOutputHelper _output;

        public JsonParserTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void ParsesScalars()
        {
            Assert.True(JsonParser.Parse("true").AsBool());
            Assert.False(JsonParser.Parse("false").AsBool());
            Assert.True(JsonParser.Parse("null").IsNull);
            Assert.Equal("hi", JsonParser.Parse("\"hi\"").AsString());
            Assert.Equal(42, JsonParser.Parse(" 42 ").AsInt());
            Assert.Equal("", JsonParser.Parse("\"\"").AsString());
        }

        [Theory]
        [InlineData("\"\\\"\"", "\"")]
        [InlineData("\"\\\\\"", "\\")]
        [InlineData("\"\\/\"", "/")]
        [InlineData("\"\\b\\f\\n\\r\\t\"", "\b\f\n\r\t")]
        [InlineData("\"a\\u0041b\"", "aAb")]
        [InlineData("\"\\u00e9\\u00E9\"", "\u00e9\u00e9")]
        [InlineData("\"\\u20AC\"", "\u20ac")]
        [InlineData("\"\\u0000\"", "\0")]
        [InlineData("\"mixed \\\"quote\\\" and \\\\ slash\"", "mixed \"quote\" and \\ slash")]
        public void DecodesEscapes(string json, string expected)
        {
            Assert.Equal(expected, JsonParser.Parse(json).AsString());
        }

        [Fact]
        public void DecodesSurrogatePairEscapes()
        {
            string value = JsonParser.Parse("\"\\uD83D\\uDE00!\"").AsString();
            Assert.Equal("\U0001F600!", value);
            Assert.Equal(0x1F600, char.ConvertToUtf32(value, 0));
        }

        [Fact]
        public void PassesRawUnicodeThrough()
        {
            Assert.Equal("6 \u00d7 6 m \U0001F600 \u2014 caf\u00e9", JsonParser.Parse("\"6 \u00d7 6 m \U0001F600 \u2014 caf\u00e9\"").AsString());
        }

        [Theory]
        [InlineData("\"\\uD83D\"", "unpaired high surrogate")]
        [InlineData("\"\\uD83Dx\"", "unpaired high surrogate")]
        [InlineData("\"\\uD83D\\u0041\"", "must be followed by a low surrogate")]
        [InlineData("\"\\uDE00\"", "unpaired low surrogate")]
        [InlineData("\"\\u12G4\"", "invalid hex digit")]
        [InlineData("\"\\u12\"", "truncated")]
        [InlineData("\"\\u12\"   ", "invalid hex digit")]
        [InlineData("\"\\x41\"", "invalid escape")]
        [InlineData("\"abc", "unterminated string")]
        [InlineData("\"a\tb\"", "unescaped control character")]
        public void RejectsBadStrings(string json, string reason)
        {
            var e = Assert.Throws<JsonParseException>(() => JsonParser.Parse(json));
            Assert.Contains(reason, e.Message);
        }

        [Theory]
        [InlineData("0", 0.0)]
        [InlineData("-0", 0.0)]
        [InlineData("12", 12.0)]
        [InlineData("-12", -12.0)]
        [InlineData("1.5", 1.5)]
        [InlineData("-0.25", -0.25)]
        [InlineData("1e3", 1000.0)]
        [InlineData("1E-3", 0.001)]
        [InlineData("2.5e+2", 250.0)]
        [InlineData("-1.25E-2", -0.0125)]
        [InlineData("0.35", 0.35)]
        [InlineData("123456789012345678", 123456789012345678.0)]
        [InlineData("1.7976931348623157e308", double.MaxValue)]
        [InlineData("5e-324", double.Epsilon)]
        public void ParsesNumbers(string json, double expected)
        {
            Assert.Equal(expected, JsonParser.Parse(json).AsDouble());
        }

        [Fact]
        public void NegativeZeroKeepsItsSign()
        {
            Assert.True(double.IsNegative(JsonParser.Parse("-0").AsDouble()));
        }

        [Theory]
        [InlineData("01")]
        [InlineData("-01")]
        [InlineData("1.")]
        [InlineData(".5")]
        [InlineData("-")]
        [InlineData("+1")]
        [InlineData("1e")]
        [InlineData("1e+")]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        [InlineData("-Infinity")]
        [InlineData("1e400")]
        [InlineData("0x10")]
        public void RejectsMalformedNumbers(string json)
        {
            Assert.Throws<JsonParseException>(() => JsonParser.Parse(json));
        }

        [Fact]
        public void NumbersIgnoreCurrentCulture()
        {
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                Assert.Equal(1.5, JsonParser.Parse("1.5").AsDouble());
                Assert.Equal(-0.0125, JsonParser.Parse("-1.25E-2").AsDouble());
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Fact]
        public void ParsesNestingAndPreservesKeyOrder()
        {
            var value = JsonParser.Parse("{\"z\":1,\"a\":[1,[2,[3,{\"k\":null}]]],\"m\":{\"x\":{\"y\":true}}}");
            Assert.Equal(new[] { "z", "a", "m" }, value.Keys);
            Assert.Equal(3, value["a"][1][1][0].AsInt());
            Assert.True(value["a"][1][1][1]["k"].IsNull);
            Assert.True(value["m"]["x"]["y"].AsBool());
            Assert.Equal(0, JsonParser.Parse("[]").Count);
            Assert.Equal(0, JsonParser.Parse("{}").Count);
            Assert.Equal(2, JsonParser.Parse("[ {} , [ ] ]").Count);
        }

        [Fact]
        public void RejectsExcessiveDepth()
        {
            string deep = new string('[', JsonParser.MaxDepth + 1) + new string(']', JsonParser.MaxDepth + 1);
            var e = Assert.Throws<JsonParseException>(() => JsonParser.Parse(deep));
            Assert.Contains("nesting deeper", e.Message);
            string ok = new string('[', JsonParser.MaxDepth) + new string(']', JsonParser.MaxDepth);
            Assert.True(JsonParser.Parse(ok).IsArray);
        }

        [Fact]
        public void ReportsLineAndColumn()
        {
            var e = Assert.Throws<JsonParseException>(() => JsonParser.Parse("{\n  \"a\": 1,\n  \"b\" 2\n}"));
            Assert.Equal(3, e.Line);
            Assert.Equal(7, e.Column);
            Assert.Contains("line 3, column 7", e.Message);
            Assert.Contains("expected ':'", e.Message);
        }

        [Fact]
        public void CountsCrLfAsOneLine()
        {
            var e = Assert.Throws<JsonParseException>(() => JsonParser.Parse("[1,\r\n2,\r\n x]"));
            Assert.Equal(3, e.Line);
            Assert.Equal(2, e.Column);
        }

        [Theory]
        [InlineData("[1,2,]", 1, 6, "expected a value")]
        [InlineData("{\"a\":1,}", 1, 8, "expected a string member name")]
        [InlineData("{\"a\":1 \"b\":2}", 1, 8, "expected ',' or '}'")]
        [InlineData("[1 2]", 1, 4, "expected ',' or ']'")]
        [InlineData("{'a':1}", 1, 2, "expected a string member name")]
        [InlineData("// c\n1", 1, 1, "unexpected '/'")]
        [InlineData("[1] x", 1, 5, "after the top-level value")]
        [InlineData("", 1, 1, "empty document")]
        [InlineData("   ", 1, 4, "empty document")]
        [InlineData("tru", 1, 1, "expected 'true'")]
        [InlineData("[nul]", 1, 2, "expected 'null'")]
        [InlineData("{\"a\":1,\"a\":2}", 1, 8, "duplicate object key")]
        [InlineData("[\n\n   ]]", 3, 5, "after the top-level value")]
        public void ErrorPositions(string json, int line, int column, string reason)
        {
            var e = Assert.Throws<JsonParseException>(() => JsonParser.Parse(json));
            Assert.Equal(line, e.Line);
            Assert.Equal(column, e.Column);
            Assert.Contains(reason, e.Message);
        }

        [Fact]
        public void SkipsLeadingByteOrderMark()
        {
            Assert.Equal(1, JsonParser.Parse("\uFEFF[1]").Count);
        }

        [Fact]
        public void TryParseReportsErrorsWithoutThrowing()
        {
            Assert.False(JsonParser.TryParse("[", out var value, out var error));
            Assert.Null(value);
            Assert.NotNull(error);
            Assert.True(JsonParser.TryParse("[1]", out value, out error));
            Assert.Null(error);
        }

        [Fact]
        public void TypedAccessorsAndDefaults()
        {
            var v = JsonParser.Parse("{\"s\":\"x\",\"n\":2.5,\"i\":7,\"b\":true,\"l\":[\"a\",\"b\"],\"z\":null,\"o\":{}}");
            Assert.Equal("x", v.GetString("s"));
            Assert.Equal("fallback", v.GetString("missing", "fallback"));
            Assert.Equal("fallback", v.GetString("z", "fallback"));
            Assert.Equal(2.5, v.GetDouble("n"));
            Assert.Equal(9.0, v.GetDouble("missing", 9.0));
            Assert.Equal(7, v.GetInt("i"));
            Assert.True(v.GetBool("b"));
            Assert.Equal(new[] { "a", "b" }, v.GetStringList("l"));
            Assert.Empty(v.GetStringList("missing"));
            Assert.True(v.Has("o"));
            Assert.False(v.Has("nope"));
            Assert.True(v.TryGet("i", out var i) && i.AsInt() == 7);
            Assert.False(v.TryGet("nope", out _));
            Assert.Equal("x", v["s"].AsString());
            Assert.Equal("b", v["l"][1].AsString());
            Assert.Equal(7, v.Count);

            Assert.Throws<JsonAccessException>(() => v["n"].AsInt());
            Assert.Throws<JsonAccessException>(() => v["s"].AsDouble());
            Assert.Throws<JsonAccessException>(() => v.GetString("n"));
            Assert.Throws<JsonAccessException>(() => v["s"].AsArray());
            Assert.Throws<JsonAccessException>(() => v["l"].Get("x"));
            Assert.Throws<JsonAccessException>(() => JsonParser.Parse("[1]").AsStringList());
            Assert.Throws<KeyNotFoundException>(() => v.Get("missing"));
            Assert.Throws<ArgumentOutOfRangeException>(() => v["l"][2]);
            Assert.False(v["s"].Has("x"));
        }

        [Fact]
        public void ParsesSuppliedCatalogueWellUnderOneSecond()
        {
            string path = Catalogue.FindDefaultPath();
            string text = File.ReadAllText(path);
            Assert.True(text.Length > 1_000_000, "catalogue should be about 1.8 MB");
            JsonParser.Parse(text); // warm-up (JIT)

            var watch = Stopwatch.StartNew();
            var root = JsonParser.Parse(text);
            watch.Stop();
            _output.WriteLine("Parsed " + text.Length + " chars in " + watch.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) + " ms");

            Assert.Equal(864, root["content"]["augments"].Count);
            Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(500), "parse took " + watch.Elapsed.TotalMilliseconds + " ms");
        }
    }
}
