using System;
using System.Collections.Generic;
using Confluence.Core.Arena;
using Confluence.Core.Rules;

namespace Confluence.Arena.Biomes
{
    public enum BiomeFeatureKind
    {
        /// <summary>A floor material patch (bed, pool, pad, rail section).</summary>
        Patch,

        /// <summary>A collision solid (prism, statue, plate, hedge, shutter, ...).</summary>
        Solid,

        /// <summary>A marked light shaft with its light patch.</summary>
        LightShaft,

        /// <summary>Scheduled gusts (no geometry).</summary>
        Gusts,
    }

    /// <summary>
    /// Geometry one biome's announced environment rule needs (spec L335 "one announced bounded environment rule";
    /// L668 natural sources). The arena places these sources and objects in mirrored pairs and flags them; the rule
    /// itself runs in Reactions and the Sim. Sizes come from the rule text ("2 m gutter beds", "3 m rail sections");
    /// where a biome states no durability the nearest catalogue object of the same kind supplies it (noted below).
    /// </summary>
    public sealed class BiomeFeature
    {
        public string BiomeId { get; set; }
        public BiomeFeatureKind Kind { get; set; }

        /// <summary>Mirrored pairs placed (two pairs for BIO-006's four rail sections).</summary>
        public int Pairs { get; set; } = 1;

        public float SizeX { get; set; }
        public float SizeZ { get; set; }
        public float Radius { get; set; }
        public float Height { get; set; }

        public int Water { get; set; }
        public int Heat { get; set; }
        public int Frost { get; set; }
        public int Dust { get; set; }
        public int Sand { get; set; }
        public int Growth { get; set; }
        public MaterialFlags Flags { get; set; }
        public bool NaturalSource { get; set; }

        /// <summary>Rule text stored with patches (RefillRule) so preview and Reactions read the same bounds.</summary>
        public string Rule { get; set; }

        public SolidKind SolidKind { get; set; } = SolidKind.Cover;
        public string Material { get; set; }

        /// <summary>Durability of a solid feature; 0 means anchored.</summary>
        public float Durability { get; set; }

        /// <summary>For BIO-024: a water patch on top of the basin solid.</summary>
        public bool PatchOnTop { get; set; }

        /// <summary>Plain description of the geometry this biome needs (reported and shown in preview, spec L329).</summary>
        public string Geometry { get; set; }

        /// <summary>Natural source patches this feature adds at combat start (both sides).</summary>
        public int SourcePatches => (NaturalSource || PatchOnTop ? 2 * Pairs : 0);

        /// <summary>Radius of the circle enclosing the feature footprint.</summary>
        public float FootprintRadius => Radius > 0f ? Radius : 0.5f * MathF.Sqrt(SizeX * SizeX + SizeZ * SizeZ);
    }

    /// <summary>The biome feature table for BIO-001..BIO-024.</summary>
    public static class BiomeFeatures
    {
        private static readonly Dictionary<string, BiomeFeature> Table = Build();

        public static bool TryGet(string biomeId, out BiomeFeature feature) => Table.TryGetValue(biomeId ?? string.Empty, out feature);

        public static int Count => Table.Count;

        public static IEnumerable<string> Ids
        {
            get
            {
                var ids = new List<string>(Table.Keys);
                ids.Sort(StringComparer.Ordinal);
                return ids;
            }
        }

        private static Dictionary<string, BiomeFeature> Build()
        {
            var t = new Dictionary<string, BiomeFeature>(StringComparer.Ordinal);
            void Add(BiomeFeature f) => t[f.BiomeId] = f;

            Add(new BiomeFeature
            {
                BiomeId = "BIO-001", Kind = BiomeFeatureKind.Patch, SizeX = 2f, SizeZ = 2f, Water = 1, Flags = MaterialFlags.Water, NaturalSource = true,
                Rule = "BIO-001:rain-pulse;every=8s;pulse=0.5s;water+=1;max=4",
                Geometry = "Two mirrored 2 m gutter beds (natural water sources, Water 1) refilled by the 8 s rainfall pulse.",
            });
            Add(new BiomeFeature
            {
                BiomeId = "BIO-002", Kind = BiomeFeatureKind.Patch, SizeX = 2f, SizeZ = 2f, Heat = 1, Growth = 1,
                Flags = MaterialFlags.Growth | MaterialFlags.Combustible, NaturalSource = true,
                Rule = "BIO-002:root-bed;fuel=one-ignition;refill=none",
                Geometry = "Two mirrored 2 m root beds starting with Heat 1 and Growth 1.",
            });
            Add(new BiomeFeature
            {
                BiomeId = "BIO-003", Kind = BiomeFeatureKind.Solid, SizeX = 0.8f, SizeZ = 0.8f, Height = 1.8f, SolidKind = SolidKind.Pillar,
                Material = ArenaMaterials.Prism, Durability = 0f,
                Geometry = "Two anchored prisms with a fixed, previewed reflecting face (one reflection at 70% damage).",
            });
            Add(new BiomeFeature
            {
                BiomeId = "BIO-004", Kind = BiomeFeatureKind.Patch, SizeX = 2f, SizeZ = 2f, Water = 1,
                Flags = MaterialFlags.Water | MaterialFlags.Ice | MaterialFlags.Brittle, NaturalSource = true,
                Rule = "BIO-004:frozen-tile;durability=45;thaw=normal;refreeze=none",
                Geometry = "Two shallow mirrored water tiles that begin frozen with 45 durability.",
            });
            Add(new BiomeFeature
            {
                // Durability from the porous wall module MOD-021 ("porous wall 100").
                BiomeId = "BIO-005", Kind = BiomeFeatureKind.Solid, SizeX = 2f, SizeZ = 0.6f, Height = 2f, Material = ArenaMaterials.Porous, Durability = 100f,
                Geometry = "Two mirrored porous walls that absorb their first water unit and release it when struck for 20.",
            });
            Add(new BiomeFeature
            {
                BiomeId = "BIO-006", Kind = BiomeFeatureKind.Patch, Pairs = 2, SizeX = 3f, SizeZ = 0.5f,
                Flags = MaterialFlags.Metal | MaterialFlags.Conductive, NaturalSource = true,
                Rule = "BIO-006:rail-section;length=3m;stops-at=insulated-gap",
                Geometry = "Four visible 3 m conductive rail sections (two mirrored pairs) separated by insulated gaps.",
            });
            Add(new BiomeFeature
            {
                BiomeId = "BIO-007", Kind = BiomeFeatureKind.Patch, SizeX = 2f, SizeZ = 2f, Sand = 2, Water = 1,
                Flags = MaterialFlags.Sand | MaterialFlags.Water, NaturalSource = true,
                Rule = "BIO-007:damp-bed;dust-suppression=3s;refill=none",
                Geometry = "Two mirrored 2 m beds starting with Sand 2 Water 1.",
            });
            Add(new BiomeFeature
            {
                BiomeId = "BIO-008", Kind = BiomeFeatureKind.LightShaft, Radius = 1.5f, NaturalSource = true,
                Rule = "BIO-008:light-shaft;shadow-lifetime=-1s/s",
                Geometry = "Two marked light shafts (natural light patches) that shorten shadow fields.",
            });
            Add(new BiomeFeature
            {
                BiomeId = "BIO-009", Kind = BiomeFeatureKind.Solid, SizeX = 1.6f, SizeZ = 0.3f, Height = 2f, SolidKind = SolidKind.Wall,
                Material = ArenaMaterials.AcousticPlate, Durability = 0f,
                Geometry = "Two anchored acoustic plates that ring publicly for 1 s when struck.",
            });
            Add(new BiomeFeature
            {
                BiomeId = "BIO-010", Kind = BiomeFeatureKind.Patch, Radius = 1f,
                Rule = "BIO-010:low-gravity-pad;fall-damage=-50%;marker=overhead;jump-bonus=none",
                Geometry = "Two mirrored 2 m low-gravity pads with overhead markers.",
            });
            Add(new BiomeFeature
            {
                BiomeId = "BIO-011", Kind = BiomeFeatureKind.Gusts,
                Geometry = "No geometry: an announced 2 s strength-1 gust every 12 s along a previewed compass direction.",
            });
            Add(new BiomeFeature
            {
                // Durability from the grounding statue of MOD-043 and PROP-002 (80).
                BiomeId = "BIO-012", Kind = BiomeFeatureKind.Solid, SizeX = 0.8f, SizeZ = 0.8f, Height = 2f, SolidKind = SolidKind.Pillar,
                Material = ArenaMaterials.GroundingStatue, Durability = 80f,
                Geometry = "Two grounded statues that absorb one electrical branch each, then overload for 4 s.",
            });
            Add(new BiomeFeature
            {
                BiomeId = "BIO-013", Kind = BiomeFeatureKind.Solid, SizeX = 2f, SizeZ = 0.5f, Height = 1.8f, Material = ArenaMaterials.Hedge, Durability = 60f,
                Geometry = "Two 60-durability hedge panels with one finite fuel layer.",
            });
            Add(new BiomeFeature
            {
                BiomeId = "BIO-014", Kind = BiomeFeatureKind.Solid, SizeX = 1.5f, SizeZ = 0.3f, Height = 2.2f, SolidKind = SolidKind.Wall,
                Material = ArenaMaterials.Mirror, Durability = 0f,
                Geometry = "Two opaque mirror slabs that reflect only non-damaging preparation flashes.",
            });
            Add(new BiomeFeature
            {
                BiomeId = "BIO-015", Kind = BiomeFeatureKind.Patch, SizeX = 2f, SizeZ = 2f, Dust = 2, Flags = MaterialFlags.Sand, NaturalSource = true,
                Rule = "BIO-015:dust-bed;water-suppresses=1-layer;force>20=3s-dust-field",
                Geometry = "Two mirrored dust beds starting with Dust 2.",
            });
            Add(new BiomeFeature
            {
                // Durability from the matching Venom Cap prop PROP-038 (30).
                BiomeId = "BIO-016", Kind = BiomeFeatureKind.Solid, SizeX = 0.6f, SizeZ = 0.6f, Height = 0.6f, SolidKind = SolidKind.Barrier,
                Material = ArenaMaterials.VenomCap, Durability = 30f,
                Geometry = "Two marked one-use caps releasing a 1 m, 2 s corrosive cloud when destroyed.",
            });
            Add(new BiomeFeature
            {
                // Durability from the lantern prop PROP-018 Seed Lantern (30).
                BiomeId = "BIO-017", Kind = BiomeFeatureKind.Solid, SizeX = 0.5f, SizeZ = 0.5f, Height = 1.4f, SolidKind = SolidKind.Barrier,
                Material = ArenaMaterials.Lantern, Durability = 30f,
                Geometry = "Two spirit lanterns that shatter once into a 2 s, 1.5 m public illumination field.",
            });
            Add(new BiomeFeature
            {
                BiomeId = "BIO-018", Kind = BiomeFeatureKind.Solid, SizeX = 2f, SizeZ = 0.3f, Height = 2.5f, SolidKind = SolidKind.Barrier,
                Material = ArenaMaterials.Shutter, Durability = 0f,
                Geometry = "Two brass shutters (collision when closed) toggling at 20 s and 40 s with 2 s warnings.",
            });
            Add(new BiomeFeature
            {
                BiomeId = "BIO-019", Kind = BiomeFeatureKind.Patch, SizeX = 2f, SizeZ = 2f, Water = 2, Flags = MaterialFlags.Water, NaturalSource = true,
                Rule = "BIO-019:dark-pool;state=ordinary-water-2;edge-pattern=visible",
                Geometry = "Two mirrored 2 m dark pools with ordinary Water 2.",
            });
            Add(new BiomeFeature
            {
                // Durability from the heat sink faces of MOD-045 ("metal plate 90").
                BiomeId = "BIO-020", Kind = BiomeFeatureKind.Solid, SizeX = 1.6f, SizeZ = 0.4f, Height = 2f, Material = ArenaMaterials.HeatSink, Durability = 90f,
                Geometry = "Two heat sinks absorbing the first 2 heat units on their face.",
            });
            Add(new BiomeFeature
            {
                BiomeId = "BIO-021", Kind = BiomeFeatureKind.Solid, SizeX = 1.2f, SizeZ = 0.6f, Height = 1.4f, Material = ArenaMaterials.Crystal, Durability = 45f,
                Geometry = "Two 45-durability brittle crystal covers that fracture under a 20+ impact.",
            });
            Add(new BiomeFeature
            {
                // Durability from the medium urn prop PROP-007 Water Urn (40).
                BiomeId = "BIO-022", Kind = BiomeFeatureKind.Solid, SizeX = 0.9f, SizeZ = 0.9f, Height = 1.2f, SolidKind = SolidKind.Barrier,
                Material = ArenaMaterials.DustUrn, Durability = 40f,
                Geometry = "Two medium sand urns that spill a 2 m Dust 1 patch once when destroyed.",
            });
            Add(new BiomeFeature
            {
                BiomeId = "BIO-023", Kind = BiomeFeatureKind.Solid, SizeX = 0.8f, SizeZ = 0.8f, Height = 1.5f, SolidKind = SolidKind.Pillar,
                Material = ArenaMaterials.MagneticCoil, Durability = 0f,
                Geometry = "Two fixed magnetic coils deflecting physical projectiles by at most 10 degrees within 1.5 m.",
            });
            Add(new BiomeFeature
            {
                BiomeId = "BIO-024", Kind = BiomeFeatureKind.Solid, SizeX = 1.0f, SizeZ = 1.0f, Height = 0.9f, SolidKind = SolidKind.Barrier,
                Material = ArenaMaterials.Basin, Durability = 0f, PatchOnTop = true, Water = 2, Flags = MaterialFlags.Water,
                Rule = "BIO-024:basin;releases=1-unit-per-direct-impact;max-releases=2",
                Geometry = "Two sealed basins holding Water 2 that release one unit per direct impact, at most twice.",
            });
            return t;
        }
    }
}
