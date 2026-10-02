using System.Collections.Generic;
using Confluence.Arena.Generation;
using Confluence.Client.Arena;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Client.Tests
{
    /// <summary>Shared catalogue for the presentation tests (loading takes about a second).</summary>
    internal static class TestCatalogue
    {
        private static Catalogue _catalogue;

        public static Catalogue Instance => _catalogue ?? (_catalogue = Catalogue.Load(Catalogue.FindDefaultPath()));
    }

    /// <summary>
    /// Arenas released by Confluence.Arena's generator for the client tests. Generation takes seconds in the editor, so
    /// each request is generated once per test run and the tests read the cached result (layouts are deep-cloned
    /// before a test may mutate one).
    /// </summary>
    internal static class TestArenas
    {
        public const ulong Seed = 7919;

        private static readonly Dictionary<string, GeneratedArena> Cache = new Dictionary<string, GeneratedArena>();

        /// <summary>Seed 7919, round 1, 32 m Raincourt (BIO-001: water and metal).</summary>
        public static GeneratedArena Raincourt32 => Generate(Seed, 1, 32, "BIO-001");

        /// <summary>Seed 7919, round 2, 24 m with the seeded biome.</summary>
        public static GeneratedArena Small24 => Generate(Seed, 2, 24, null);

        public static GeneratedArena Generate(ulong seed, int round, int diameter, string biomeId)
        {
            string key = seed + "/" + round + "/" + diameter + "/" + (biomeId ?? "seeded");
            if (!Cache.TryGetValue(key, out GeneratedArena arena))
            {
                var request = new ArenaRequest { MatchSeed = seed, Round = round, Diameter = diameter, BiomeId = biomeId };
                arena = ArenaGenerationService.GeneratorFor(TestCatalogue.Instance).Generate(request);
                Cache[key] = arena;
            }

            return arena;
        }

        /// <summary>A minimal hand-made layout with one 1.5 m ramp and one final-center cover piece (renderer unit cases).</summary>
        public static ArenaLayout RampAndRetiringCover()
        {
            var layout = new ArenaLayout { Diameter = 24, Center = Vec3.Zero, Boundary = BoundaryContour.Circle(Vec3.Zero, 12f) };
            layout.Solids.Add(new Solid
            {
                Id = 1, Kind = SolidKind.Floor, Box = YawBox.OnFloor(new Vec3(0f, -0.5f, 0f), new Vec3(30f, 0.5f, 30f), 0f),
                Material = "stone", Anchored = true, Walkable = true, BlocksSight = false,
            });
            RampShape ramp = RampShape.Wedge(new Vec3(6f, 0f, 2f), 1.25f, 1.6f, 37f, 1.5f);
            layout.Solids.Add(new Solid { Id = 2, Kind = SolidKind.Ramp, Box = ramp.Box, RampRise = ramp.Rise, Material = "stone", Anchored = true, Walkable = true });
            layout.Solids.Add(new Solid
            {
                Id = 3, Kind = SolidKind.Cover, Box = YawBox.OnFloor(new Vec3(-1.5f, 0f, 0.5f), new Vec3(1.8f, 1.2f, 0.4f), 0f),
                Material = "hedge", Flags = MaterialFlags.Growth | MaterialFlags.Combustible, Durability = 60f, RetiresAtFinal = true,
            });
            return layout;
        }
    }
}
