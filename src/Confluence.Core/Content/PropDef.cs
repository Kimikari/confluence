using System;
using Confluence.Core.Rules;

namespace Confluence.Core.Content
{
    /// <summary>One of the 48 interactive prop definitions with bounded material interactions (spec L656, L662).</summary>
    public sealed class PropDef : ContentDef
    {
        private readonly PropCategory _category;
        private readonly bool _categoryValid;
        private readonly PropMass _mass;
        private readonly bool _massValid;

        internal PropDef(RecordReader r)
            : base(r.RequireId(), r.String("name"), r.StringList("tags"), r.Json)
        {
            CategoryText = r.String("category");
            MassText = r.String("mass");
            Durability = r.Number("durability");
            Effect = r.String("effect");
            TacticalUse = r.String("tactical_use");
            Counterplay = r.String("counterplay");
            Placement = r.String("placement");
            r.Finish();

            _categoryValid = ContentText.TryParsePropCategory(CategoryText, out _category);
            _massValid = ContentText.TryParsePropMass(MassText, out _mass);
        }

        public override ContentType Type => ContentType.Prop;

        public string CategoryText { get; }
        public string MassText { get; }

        /// <summary>Prop health; a prop uses its own durability before module defaults (spec L662).</summary>
        public double Durability { get; }

        public string Effect { get; }
        public string TacticalUse { get; }
        public string Counterplay { get; }
        public string Placement { get; }

        public bool HasValidCategory => _categoryValid;
        public bool HasValidMass => _massValid;

        public PropCategory Category
        {
            get
            {
                if (!_categoryValid) throw new InvalidOperationException(Id + " has unrecognised prop category '" + CategoryText + "'.");
                return _category;
            }
        }

        public PropMass Mass
        {
            get
            {
                if (!_massValid) throw new InvalidOperationException(Id + " has unrecognised mass '" + MassText + "'.");
                return _mass;
            }
        }

        /// <summary>Anchored props can lose functional or cover portions but cannot move (spec L662).</summary>
        public bool IsAnchored => _massValid && _mass == PropMass.Anchored;
    }
}
