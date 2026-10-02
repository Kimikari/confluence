using System;
using System.Collections.Generic;
using System.Globalization;
using Confluence.Core.Rules;

namespace Confluence.Core.Content
{
    /// <summary>One of the 72 arena modules supplying gameplay geometry and sockets (spec L656-L660).</summary>
    public sealed class ModuleDef : ContentDef
    {
        private readonly ModuleCategory _category;
        private readonly bool _categoryValid;
        private readonly double _width;
        private readonly double _depth;

        internal ModuleDef(RecordReader r)
            : base(r.RequireId(), r.String("name"), r.StringList("tags"), r.Json)
        {
            CategoryText = r.String("category");
            Footprint = r.String("footprint");
            HeightM = r.Number("height_m");
            Geometry = r.String("geometry");
            Materials = r.StringList("materials");
            Durability = r.String("durability");
            Fairness = r.String("fairness");
            Sockets = r.String("sockets");
            r.Finish();

            _categoryValid = ContentText.TryParseModuleCategory(CategoryText, out _category);
            HasValidFootprint = TryParseFootprint(Footprint, out _width, out _depth);
        }

        public override ContentType Type => ContentType.Module;

        public string CategoryText { get; }

        /// <summary>Footprint as written, including connecting clear space (spec L686), e.g. "6 × 6 m".</summary>
        public string Footprint { get; }

        /// <summary>Height relative to the connected floor in meters; negative for sunken modules.</summary>
        public double HeightM { get; }

        public string Geometry { get; }
        public IReadOnlyList<string> Materials { get; }

        /// <summary>Durability prose (load-bearing terrain and attached cover health).</summary>
        public string Durability { get; }

        public string Fairness { get; }
        public string Sockets { get; }

        public bool HasValidCategory => _categoryValid;
        public bool HasValidFootprint { get; }

        public ModuleCategory Category
        {
            get
            {
                if (!_categoryValid) throw new InvalidOperationException(Id + " has unrecognised module category '" + CategoryText + "'.");
                return _category;
            }
        }

        /// <summary>First footprint dimension in meters ("6 × 4 m" gives 6).</summary>
        public double FootprintWidth
        {
            get
            {
                if (!HasValidFootprint) throw new InvalidOperationException(Id + " has unparseable footprint '" + Footprint + "'.");
                return _width;
            }
        }

        /// <summary>Second footprint dimension in meters ("6 × 4 m" gives 4).</summary>
        public double FootprintDepth
        {
            get
            {
                if (!HasValidFootprint) throw new InvalidOperationException(Id + " has unparseable footprint '" + Footprint + "'.");
                return _depth;
            }
        }

        /// <summary>Parses "W × D m" (multiplication sign U+00D7; ASCII 'x' is also accepted). Both sides must be positive.</summary>
        public static bool TryParseFootprint(string text, out double width, out double depth)
        {
            width = 0;
            depth = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string s = text.Trim();
            if (s.EndsWith("m", StringComparison.Ordinal)) s = s.Substring(0, s.Length - 1).TrimEnd();
            int sep = s.IndexOf('×');
            if (sep < 0) sep = s.IndexOfAny(new[] { 'x', 'X' });
            if (sep <= 0 || sep == s.Length - 1) return false;
            string left = s.Substring(0, sep).Trim();
            string right = s.Substring(sep + 1).Trim();
            const NumberStyles style = NumberStyles.AllowDecimalPoint;
            if (!double.TryParse(left, style, CultureInfo.InvariantCulture, out width)) return false;
            if (!double.TryParse(right, style, CultureInfo.InvariantCulture, out depth)) return false;
            return width > 0 && depth > 0;
        }
    }
}
