using System.Collections.Generic;

namespace Confluence.Core.Content
{
    /// <summary>One of the 24 biomes: materials, art identity, and exactly one announced environment rule (spec L656, L684).</summary>
    public sealed class BiomeDef : ContentDef
    {
        internal BiomeDef(RecordReader r)
            : base(r.RequireId(), r.String("name"), r.StringList("tags"), r.Json)
        {
            VisualIdentity = r.String("visual_identity");
            Materials = r.StringList("materials");
            EnvironmentRule = r.String("environment_rule");
            TacticalUse = r.String("tactical_use");
            Fairness = r.String("fairness");
            Sizes = r.IntList("sizes");
            r.Finish();
        }

        public override ContentType Type => ContentType.Biome;

        public string VisualIdentity { get; }
        public IReadOnlyList<string> Materials { get; }
        public string EnvironmentRule { get; }
        public string TacticalUse { get; }
        public string Fairness { get; }

        /// <summary>Arena diameters in meters this biome supports.</summary>
        public IReadOnlyList<int> Sizes { get; }

        public bool SupportsSize(int diameter)
        {
            for (int i = 0; i < Sizes.Count; i++)
                if (Sizes[i] == diameter) return true;
            return false;
        }
    }
}
