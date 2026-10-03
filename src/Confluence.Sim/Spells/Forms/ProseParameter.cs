using System;
using System.Globalization;

namespace Confluence.Sim.Spells
{
    /// <summary>
    /// A rule number that exists only in a catalogue record's prose (DECISIONS D-011): the value used, the number as it
    /// is written, and the exact quoted phrase it comes from. A registry test asserts every quote appears verbatim in
    /// its record and contains the written number, so a typo or a catalogue change cannot silently drift.
    /// </summary>
    public sealed class ProseParameter
    {
        public ProseParameter(string name, double written, string quote, double scale = 1.0)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("A parameter name is required.", nameof(name));
            if (string.IsNullOrEmpty(quote)) throw new ArgumentException("A quoted source phrase is required.", nameof(quote));
            Name = name;
            Written = written;
            Quote = quote;
            Scale = scale;
        }

        /// <summary>A plain number ("24 m/s projectile" gives 24).</summary>
        public static ProseParameter Of(string name, double written, string quote) => new ProseParameter(name, written, quote);

        /// <summary>A percentage written as a whole number and used as a fraction ("slows 15%" gives 0.15).</summary>
        public static ProseParameter Percent(string name, double writtenPercent, string quote) => new ProseParameter(name, writtenPercent, quote, 0.01);

        /// <summary>A full angle written in degrees and used as a half angle ("90-degree cone" gives 45).</summary>
        public static ProseParameter HalfAngle(string name, double writtenDegrees, string quote) => new ProseParameter(name, writtenDegrees, quote, 0.5);

        public string Name { get; }

        /// <summary>The number exactly as the record writes it.</summary>
        public double Written { get; }

        /// <summary>The phrase quoted from the record (damage, effect, world interaction, or another text field).</summary>
        public string Quote { get; }

        /// <summary>Conversion from the written number to the rule value (1 for plain numbers).</summary>
        public double Scale { get; }

        /// <summary>The value rules use.</summary>
        public double Value => Written * Scale;

        public float Float => (float)Value;

        /// <summary>The written number formatted as the catalogue writes numbers ("1.8", "24", "0.35").</summary>
        public string WrittenText => Written.ToString("0.####", CultureInfo.InvariantCulture);

        public override string ToString() => Name + " = " + Value.ToString("0.####", CultureInfo.InvariantCulture) + " (\"" + Quote + "\")";
    }
}
