using System;
using System.Globalization;
using System.IO;
using System.Threading;
using Confluence.Core.Content;
using Confluence.Core.Json;

namespace Confluence.Core.Tests.Json
{
    public class JsonWriterTests
    {
        private static JsonValue Sample() => JsonValue.Object(
            ("name", "Fireball"),
            ("mana", 18),
            ("cast_time", 0.35),
            ("tags", JsonValue.StringArray(new[] { "heat", "projectile" })),
            ("nested", JsonValue.Object(("ok", true), ("none", JsonValue.Null))),
            ("empty_list", JsonValue.Array()),
            ("empty_obj", JsonValue.Object()));

        [Fact]
        public void WritesCompact()
        {
            Assert.Equal(
                "{\"name\":\"Fireball\",\"mana\":18,\"cast_time\":0.35,\"tags\":[\"heat\",\"projectile\"],\"nested\":{\"ok\":true,\"none\":null},\"empty_list\":[],\"empty_obj\":{}}",
                JsonWriter.Write(Sample()));
        }

        [Fact]
        public void WritesIndented()
        {
            string expected = string.Join("\n",
                "{",
                "  \"name\": \"Fireball\",",
                "  \"mana\": 18,",
                "  \"cast_time\": 0.35,",
                "  \"tags\": [",
                "    \"heat\",",
                "    \"projectile\"",
                "  ],",
                "  \"nested\": {",
                "    \"ok\": true,",
                "    \"none\": null",
                "  },",
                "  \"empty_list\": [],",
                "  \"empty_obj\": {}",
                "}");
            Assert.Equal(expected, JsonWriter.Write(Sample(), indented: true));
        }

        [Fact]
        public void KeepsInsertionOrderUnlessSorting()
        {
            var value = JsonValue.Object(("b", 1), ("a", 2), ("c", 3));
            Assert.Equal("{\"b\":1,\"a\":2,\"c\":3}", value.ToString());
            Assert.Equal("{\"a\":2,\"b\":1,\"c\":3}", JsonWriter.Write(value, new JsonWriterOptions(false, 0, true)));
        }

        [Theory]
        [InlineData("plain", "\"plain\"")]
        [InlineData("q\"b\\s/", "\"q\\\"b\\\\s/\"")]
        [InlineData("\b\f\n\r\t", "\"\\b\\f\\n\\r\\t\"")]
        [InlineData("\u0001\u001f", "\"\\u0001\\u001f\"")]
        [InlineData("caf\u00e9 \u00d7 \u20ac", "\"caf\u00e9 \u00d7 \u20ac\"")]
        [InlineData("\U0001F600", "\"\U0001F600\"")]
        public void EscapesStrings(string value, string expected)
        {
            Assert.Equal(expected, JsonWriter.Write(JsonValue.From(value)));
        }

        [Fact]
        public void EscapesUnpairedSurrogatesSoOutputStaysValid()
        {
            // Built in code: attribute strings are UTF-8 encoded and cannot carry lone surrogates.
            string lone = new string(new[] { 'a', (char)0xDE00, 'b', (char)0xD83D });
            string text = JsonWriter.Write(JsonValue.From(lone));
            Assert.Equal("\"a\\ude00b\\ud83d\"", text);
        }

        [Theory]
        [InlineData(0.0, "0")]
        [InlineData(1.0, "1")]
        [InlineData(-12.0, "-12")]
        [InlineData(0.1, "0.1")]
        [InlineData(0.35, "0.35")]
        [InlineData(-0.0125, "-0.0125")]
        [InlineData(9007199254740992.0, "9007199254740992")]
        [InlineData(1e21, "1E+21")]
        [InlineData(1.5e-7, "1.5E-07")]
        [InlineData(double.MaxValue, "1.7976931348623157E+308")]
        public void FormatsNumbersInvariantlyAndRoundTrips(double value, string expected)
        {
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                string text = JsonWriter.Write(JsonValue.From(value));
                Assert.Equal(expected, text);
                Assert.Equal(value, JsonParser.Parse(text).AsDouble());
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Fact]
        public void WritesNegativeZeroAsZero()
        {
            Assert.Equal("0", JsonWriter.Write(JsonValue.From(-0.0)));
        }

        [Fact]
        public void RejectsNonFiniteNumbers()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => JsonValue.From(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => JsonValue.From(double.PositiveInfinity));
        }

        [Fact]
        public void ObjectFactoryRejectsDuplicateKeys()
        {
            Assert.Throws<ArgumentException>(() => JsonValue.Object(("a", 1), ("a", 2)));
        }

        [Theory]
        [InlineData("{\"a\":[1,2.5,-3e-5,{\"b\":\"\\u00e9\\n\"}],\"c\":null,\"d\":false}")]
        [InlineData("[\"\\uD83D\\uDE00\",\"\\\\\",\"\\\"\",0,-0.5,1E+30]")]
        [InlineData("\"\\u0007bell\"")]
        [InlineData("[[],{},[[]],{\"\":{}}]")]
        public void RoundTripsSmallDocuments(string json)
        {
            var value = JsonParser.Parse(json);
            foreach (bool indented in new[] { false, true })
            {
                string written = JsonWriter.Write(value, indented);
                var reparsed = JsonParser.Parse(written);
                Assert.Equal(value, reparsed);
                Assert.Equal(written, JsonWriter.Write(reparsed, indented));
            }
        }

        [Fact]
        public void RoundTripsSuppliedCatalogue()
        {
            var original = JsonParser.Parse(File.ReadAllText(Catalogue.FindDefaultPath()));
            string compact = JsonWriter.Write(original);
            string indented = JsonWriter.Write(original, indented: true);
            Assert.Equal(original, JsonParser.Parse(compact));
            Assert.Equal(original, JsonParser.Parse(indented));
            Assert.Equal(compact, JsonWriter.Write(JsonParser.Parse(compact)));
        }

        [Fact]
        public void StructuralEqualityIgnoresMemberOrderButNotArrayOrder()
        {
            Assert.Equal(JsonParser.Parse("{\"a\":1,\"b\":[1,2]}"), JsonParser.Parse("{\"b\":[1,2],\"a\":1}"));
            Assert.Equal(JsonParser.Parse("{\"a\":1,\"b\":[1,2]}").GetHashCode(), JsonParser.Parse("{\"b\":[1,2],\"a\":1}").GetHashCode());
            Assert.NotEqual(JsonParser.Parse("[1,2]"), JsonParser.Parse("[2,1]"));
            Assert.NotEqual(JsonParser.Parse("1"), JsonParser.Parse("\"1\""));
            Assert.Equal(JsonParser.Parse("0"), JsonParser.Parse("-0"));
        }
    }
}
