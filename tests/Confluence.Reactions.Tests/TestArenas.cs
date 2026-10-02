using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Reactions.Tests
{
    /// <summary>Hand-built arena layouts for reaction tests (the generator is a separate subsystem).</summary>
    internal static class TestArenas
    {
        /// <summary>An empty 24 m arena centered on the origin.</summary>
        public static ArenaLayout Empty(int diameter = 24, string biomeId = null)
        {
            return new ArenaLayout { Diameter = diameter, Center = Vec3.Zero, BiomeId = biomeId };
        }

        /// <summary>A natural, map-owned rectangular bed (spec L668).</summary>
        public static MaterialPatch Bed(Vec2 center, float halfX, float halfZ, int water = 0, int sand = 0, int dust = 0, int growth = 0, int heat = 0,
            MaterialFlags flags = MaterialFlags.None, string refillRule = null)
        {
            return new MaterialPatch
            {
                Center = center,
                HalfSize = new Vec2(halfX, halfZ),
                Water = water,
                Sand = sand,
                Dust = dust,
                Growth = growth,
                Heat = heat,
                Flags = flags,
                NaturalSource = true,
                RefillRule = refillRule,
            };
        }
    }
}
