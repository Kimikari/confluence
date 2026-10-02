using System;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Time;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;

namespace Confluence.Reactions.Environment
{
    /// <summary>
    /// The arena's announced gust schedule (spec L664 "gust schedules") and BIO-011 Windmill Yard: "Every 12 s an
    /// announced 2 s gust moves clouds up to 2 m along the shown compass direction; force strength 1." Gusts follow
    /// the global wind rule (strength above 1 translates clouds, L151) unless the biome's own text moves clouds at
    /// strength 1, which only Windmill Yard does, bounded by its 2 m (DECISIONS D-023, D-038). A gust only moves
    /// volumes: "The gust does not move players or damaging props" (BIO-011 fairness), so it never queues prop forces.
    /// </summary>
    [ContentBehavior("BIO-011")]
    public static class WindmillYardGusts
    {
        public const string BiomeId = "BIO-011";

        /// <summary>"moves clouds up to 2 m".</summary>
        public const float GustMaxDistance = 2f;

        /// <summary>Converts the layout's announced gusts into scheduled winds.</summary>
        public static void InstallGusts(ReactionWorld world, ArenaLayout layout)
        {
            if (layout.Gusts == null)
            {
                return;
            }

            bool windmill = layout.BiomeId == BiomeId;
            for (int i = 0; i < layout.Gusts.Count; i++)
            {
                GustEvent g = layout.Gusts[i];
                if (g == null)
                {
                    continue;
                }

                var gust = new WindGust
                {
                    Area = g.Radius > 0f ? Footprint.Circle(g.Center, g.Radius) : (Footprint?)null,
                    Direction = g.Direction.Normalized(),
                    Strength = g.Strength,
                    DurationTicks = Math.Max(1, Ticks.FromSeconds(g.DurationSec)),
                    Owner = ReactionRules.MapOwner,
                    SourceEventId = ReactionRules.NoSource,
                };
                if (windmill)
                {
                    gust.LocalCloudOverride = true;
                    gust.MaxDistance = GustMaxDistance;
                }

                world.AddScheduledGust(world.CombatStartTick + Ticks.FromSeconds(g.TimeSec), gust);
            }
        }
    }
}
