using System;
using System.Collections.Generic;
using Confluence.Core.Arena;
using Confluence.Core.Rules;

namespace Confluence.Hosting.Arenas
{
    /// <summary>One plain-language material line of the arena preview (spec L329).</summary>
    public sealed class MaterialNote
    {
        internal MaterialNote(string key, string description, int count)
        {
            Key = key;
            Description = description;
            Count = count;
        }

        /// <summary>Stable key (for example "conductive", "water") for icons and localization.</summary>
        public string Key { get; }

        /// <summary>Plain description such as "conductive rails" or "shallow water" (L329).</summary>
        public string Description { get; }

        /// <summary>Solids and material patches carrying it.</summary>
        public int Count { get; }

        public override string ToString() => Key + " x" + Count + ": " + Description;
    }

    /// <summary>
    /// Plain descriptions of the materials in a layout, so players do not have to infer behavior from color (spec
    /// L329). Each line names a reaction-relevant property from the rule chapters: metal conducts (L149), growth burns
    /// (L153), porous surfaces absorb water, glass and prisms reflect rays (L157), ice is brittle and slippery (L175).
    /// </summary>
    public static class MaterialDescriptions
    {
        private static readonly Entry[] Entries =
        {
            new Entry("conductive", MaterialFlags.Conductive, "Conductive metal: rails, plates and fittings carry electricity along connected wet or metal routes"),
            new Entry("grounded", MaterialFlags.Grounded, "Grounding fixtures: absorb an electrical branch that reaches them"),
            new Entry("water", MaterialFlags.Water, "Shallow water: wet cells conduct electricity and turn to steam under heat"),
            new Entry("combustible", MaterialFlags.Combustible, "Combustible growth: hedges and planting ignite under sustained heat"),
            new Entry("porous", MaterialFlags.Porous, "Porous surfaces: absorb water and dampen spread"),
            new Entry("reflective", MaterialFlags.Reflective, "Prism and mirror surfaces: reflect light rays"),
            new Entry("glass", MaterialFlags.Glass, "Glass: transparent, breakable cover"),
            new Entry("ice", MaterialFlags.Ice, "Ice: slippery footing that shatters and melts"),
            new Entry("brittle", MaterialFlags.Brittle, "Brittle pieces: break under heavy impacts"),
            new Entry("sand", MaterialFlags.Sand, "Sand beds: absorb water and become damp sand"),
            new Entry("acoustic", MaterialFlags.Acoustic, "Acoustic plates and bells: ring audibly when struck"),
        };

        /// <summary>Material notes for a layout in a fixed order; only materials present appear.</summary>
        public static List<MaterialNote> Describe(ArenaLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            var counts = new int[Entries.Length];
            int destructible = 0;
            foreach (Solid solid in layout.Solids)
            {
                Count(solid.Flags, counts);
                if (solid.IsDestructible) destructible++;
            }

            int heat = 0, frost = 0, dust = 0;
            foreach (MaterialPatch patch in layout.Materials)
            {
                // Unit counts imply the material even where a patch's flags were left empty (D-025).
                MaterialFlags flags = patch.Flags;
                if (patch.Water > 0) flags |= MaterialFlags.Water;
                if (patch.Sand > 0) flags |= MaterialFlags.Sand;
                if (patch.Growth > 0) flags |= MaterialFlags.Growth | MaterialFlags.Combustible;
                if (patch.Heat > 0) heat++;
                if (patch.Frost > 0) frost++;
                if (patch.Dust > 0) dust++;
                Count(flags, counts);
            }

            var notes = new List<MaterialNote>();
            for (int i = 0; i < Entries.Length; i++)
            {
                if (counts[i] > 0) notes.Add(new MaterialNote(Entries[i].Key, Entries[i].Description, counts[i]));
            }

            if (heat > 0) notes.Add(new MaterialNote("heat", "Heated cells: warm ground that ignites growth and turns water to steam", heat));
            if (frost > 0) notes.Add(new MaterialNote("frost", "Frosted cells: chill water into ice", frost));
            if (dust > 0) notes.Add(new MaterialNote("dust", "Dust: blown into sight-blocking clouds", dust));
            if (destructible > 0) notes.Add(new MaterialNote("destructible", "Destructible cover: takes damage and breaks", destructible));
            return notes;
        }

        private static void Count(MaterialFlags flags, int[] counts)
        {
            for (int i = 0; i < Entries.Length; i++)
                if ((flags & Entries[i].Flag) != 0) counts[i]++;
        }

        private struct Entry
        {
            public Entry(string key, MaterialFlags flag, string description)
            {
                Key = key;
                Flag = flag;
                Description = description;
            }

            public string Key;
            public MaterialFlags Flag;
            public string Description;
        }
    }
}
