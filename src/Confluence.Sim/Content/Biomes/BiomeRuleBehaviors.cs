using System;
using Confluence.Core.Content;
using Confluence.Reactions.Environment;
using Confluence.Sim.Duel;
using Confluence.Sim.World.Materials;

namespace Confluence.Sim.Content.Biomes
{
    /// <summary>
    /// BIO-001 Raincourt: "Every 8 s a 0.5 s visible rainfall pulse adds one water unit to the two mirrored 2 m
    /// gutter beds, up to four. No rain wets players automatically." Installs the announced refill pulse
    /// (<see cref="RaincourtRain"/>, spec L138, L668) into the duel's reaction world, timed from combat start. Players
    /// become Wet only by standing in a wet gutter cell (the duel's wet-cell rule), never from the rain itself.
    /// </summary>
    [ContentBehavior("BIO-001")]
    public sealed class RaincourtRainRule : IBiomeRuleBehavior
    {
        public void Install(DuelWorld world, BiomeDef biome)
        {
            ReactionMaterials materials = BiomeRules.RequireMaterials(world, RaincourtRain.BiomeId);
            RaincourtRain.Install(materials.World, world.Layout);
            EnvironmentRules.InstallAnnouncedGusts(materials.World, world.Layout, false, float.PositiveInfinity);
        }
    }

    /// <summary>
    /// BIO-011 Windmill Yard: "Every 12 s an announced 2 s gust moves clouds up to 2 m along the shown compass
    /// direction; force strength 1." Installs the layout's announced gusts with the biome's own exception to the wind
    /// rule (strength 1 moves clouds, at most 2 m per gust and volume; spec L151, D-023, D-038). "The gust does not move
    /// players or damaging props": the gusts queue no prop forces and no player displacement.
    /// </summary>
    [ContentBehavior("BIO-011")]
    public sealed class WindmillYardGustRule : IBiomeRuleBehavior
    {
        public void Install(DuelWorld world, BiomeDef biome)
        {
            ReactionMaterials materials = BiomeRules.RequireMaterials(world, WindmillYardGusts.BiomeId);
            WindmillYardGusts.Install(materials.World, world.Layout);
        }
    }

    internal static class BiomeRules
    {
        public static ReactionMaterials RequireMaterials(DuelWorld world, string biomeId)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (!(world.Materials is ReactionMaterials materials) || materials.World == null)
            {
                throw new InvalidOperationException(biomeId + " needs the duel's reaction world (ReactionMaterials).");
            }

            return materials;
        }
    }
}
