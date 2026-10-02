using System;
using System.Collections.Generic;
using Confluence.Arena.Modules;
using Confluence.Arena.Props;
using Confluence.Core.Content;
using Confluence.Core.Random;
using Confluence.Core.Rules;

namespace Confluence.Arena.Generation
{
    /// <summary>
    /// Read-only generation inputs built once per catalogue: authored module geometry by category, prop definitions
    /// with shapes, and the biome material affinities that bias module and prop choice (spec L318 "according to the
    /// biome's material and visibility budget"). Safe to share across threads.
    /// </summary>
    public sealed class ArenaContent
    {
        private readonly List<ModuleGeometry>[] _byCategory;

        public ArenaContent(Catalogue catalogue)
        {
            Catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
            Modules = new ModuleLibrary(catalogue);
            int categories = Enum.GetValues(typeof(ModuleCategory)).Length;
            _byCategory = new List<ModuleGeometry>[categories];
            for (int c = 0; c < categories; c++)
            {
                _byCategory[c] = Modules.OfCategory((ModuleCategory)c);
            }

            var props = new List<PropDef>();
            for (int i = 0; i < catalogue.Props.Count; i++)
            {
                if (PropShapes.TryGet(catalogue.Props[i].Id, out PropShape _))
                {
                    props.Add(catalogue.Props[i]);
                }
            }

            props.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            Props = props;
        }

        public Catalogue Catalogue { get; }
        public ModuleLibrary Modules { get; }

        /// <summary>Prop definitions that have a physical shape, in id order.</summary>
        public IReadOnlyList<PropDef> Props { get; }

        public IReadOnlyList<ModuleGeometry> ModulesOf(ModuleCategory category) => _byCategory[(int)category];

        // ---- Biome affinity ---------------------------------------------------------------------------

        /// <summary>Module materials that suit a biome material (biome lists name elements; modules name substances).</summary>
        public static string[] ModuleMaterialsFor(string biomeMaterial)
        {
            switch (biomeMaterial)
            {
                case "Water": return new[] { "water", "porous" };
                case "Metal": return new[] { "metal" };
                case "Storm": return new[] { "metal", "water" };
                case "Heat": return new[] { "metal", "growth" };
                case "Growth": return new[] { "growth", "hedge" };
                case "Frost": return new[] { "ice" };
                case "Light": return new[] { "glass" };
                case "Arcane": return new[] { "glass" };
                case "Sound": return new[] { "metal", "wood" };
                case "Earth": return new[] { "stone", "sand" };
                case "Sand": return new[] { "sand" };
                case "Force": return new[] { "wood" };
                case "Venom": return new[] { "growth" };
                default: return new[] { "stone" };
            }
        }

        /// <summary>Prop categories that suit a biome material.</summary>
        public static PropCategory[] PropCategoriesFor(string biomeMaterial)
        {
            switch (biomeMaterial)
            {
                case "Water": return new[] { PropCategory.Reservoir };
                case "Metal": return new[] { PropCategory.Conductive };
                case "Storm": return new[] { PropCategory.Conductive };
                case "Heat": return new[] { PropCategory.Fuel };
                case "Growth": return new[] { PropCategory.Fuel };
                case "Frost": return new[] { PropCategory.Reservoir, PropCategory.Reactive };
                case "Light": return new[] { PropCategory.Optical };
                case "Shadow": return new[] { PropCategory.Optical };
                case "Sound": return new[] { PropCategory.Acoustic };
                case "Earth": return new[] { PropCategory.Kinetic, PropCategory.Utility };
                case "Gravity": return new[] { PropCategory.Kinetic };
                case "Force": return new[] { PropCategory.Kinetic };
                case "Sand": return new[] { PropCategory.Utility, PropCategory.Reactive };
                default: return new[] { PropCategory.Reactive, PropCategory.Utility };
            }
        }

        /// <summary>Selection weight of a module in a biome: 1 plus 2 for each biome material its materials suit.</summary>
        public static float ModuleWeight(ModuleGeometry module, ModuleDef def, BiomeDef biome)
        {
            float weight = 1f;
            if (biome == null)
            {
                return weight;
            }

            for (int i = 0; i < biome.Materials.Count; i++)
            {
                string[] suits = ModuleMaterialsFor(biome.Materials[i]);
                for (int j = 0; j < def.Materials.Count; j++)
                {
                    if (Array.IndexOf(suits, def.Materials[j]) >= 0)
                    {
                        weight += 2f;
                        break;
                    }
                }
            }

            return weight;
        }

        public static float PropWeight(PropDef prop, BiomeDef biome)
        {
            float weight = 1f;
            if (biome == null || !prop.HasValidCategory)
            {
                return weight;
            }

            for (int i = 0; i < biome.Materials.Count; i++)
            {
                if (Array.IndexOf(PropCategoriesFor(biome.Materials[i]), prop.Category) >= 0)
                {
                    weight += 3f;
                }
            }

            return weight;
        }

        /// <summary>Weighted index draw (weights must be non-negative; falls back to uniform when all are zero).</summary>
        public static int WeightedPick(Rng rng, IReadOnlyList<float> weights)
        {
            float total = 0f;
            for (int i = 0; i < weights.Count; i++)
            {
                total += weights[i];
            }

            if (total <= 0f)
            {
                return rng.NextInt(weights.Count);
            }

            float x = rng.NextFloat() * total;
            for (int i = 0; i < weights.Count; i++)
            {
                x -= weights[i];
                if (x < 0f)
                {
                    return i;
                }
            }

            return weights.Count - 1;
        }
    }
}
