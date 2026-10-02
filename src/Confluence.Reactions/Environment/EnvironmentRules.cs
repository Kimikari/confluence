using System;
using Confluence.Core.Arena;
using Confluence.Core.Time;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;

namespace Confluence.Reactions.Environment
{
    /// <summary>
    /// Biome environment hooks for the reaction world (spec L138, L656 "Biomes supply art and one declared
    /// environmental rule", L668). Installs the scheduled sources and gusts a biome's rule announces; each biome rule
    /// lives in its own class (<see cref="RaincourtRain"/>, <see cref="WindmillYardGusts"/>). In a duel the Sim
    /// installs them through its biome behaviors; <see cref="Install"/> is the standalone path (tests, Laboratory).
    /// </summary>
    public static class EnvironmentRules
    {
        public const string RaincourtId = RaincourtRain.BiomeId;
        public const string WindmillYardId = WindmillYardGusts.BiomeId;

        public const float RaincourtPulseIntervalSec = RaincourtRain.PulseIntervalSec;
        public const float RaincourtPulseVisibleSec = RaincourtRain.PulseVisibleSec;
        public const int RaincourtUnitsPerPulse = RaincourtRain.UnitsPerPulse;
        public const int RaincourtMaxUnits = RaincourtRain.MaxUnits;

        public const float WindmillGustMaxDistance = WindmillYardGusts.GustMaxDistance;

        /// <summary>Installs the layout's biome rule and gust schedule into the world.</summary>
        public static void Install(ReactionWorld world, ArenaLayout layout)
        {
            if (world == null || layout == null)
            {
                return;
            }

            if (layout.BiomeId == RaincourtId)
            {
                RaincourtRain.Install(world, layout);
            }

            InstallGusts(world, layout);
        }

        /// <summary>BIO-001 gutter-bed rain (see <see cref="RaincourtRain.Install"/>). Returns the number of beds.</summary>
        public static int InstallRaincourtRain(ReactionWorld world, ArenaLayout layout) => RaincourtRain.Install(world, layout);

        /// <summary>A Raincourt gutter-bed pulse schedule with no cells yet.</summary>
        public static ScheduledSource RaincourtSource(int combatStartTick) => RaincourtRain.Source(combatStartTick);

        /// <summary>The layout's announced gusts, with the BIO-011 cloud rule when the layout is Windmill Yard.</summary>
        public static void InstallGusts(ReactionWorld world, ArenaLayout layout)
        {
            if (layout != null && layout.BiomeId == WindmillYardId)
            {
                WindmillYardGusts.Install(world, layout);
            }
            else
            {
                InstallAnnouncedGusts(world, layout, false, float.PositiveInfinity);
            }
        }

        /// <summary>
        /// Converts the layout's announced gusts (spec L664 "gust schedules") into scheduled winds. They follow the
        /// global wind rule (strength above 1 translates clouds, L151, D-023) unless <paramref name="cloudsMoveAtStrengthOne"/>
        /// states the biome's own exception, bounded by <paramref name="maxDistance"/> per volume. Returns the count.
        /// </summary>
        public static int InstallAnnouncedGusts(ReactionWorld world, ArenaLayout layout, bool cloudsMoveAtStrengthOne, float maxDistance)
        {
            if (world == null || layout == null || layout.Gusts == null)
            {
                return 0;
            }

            int count = 0;
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
                if (cloudsMoveAtStrengthOne)
                {
                    gust.LocalCloudOverride = true;
                    gust.MaxDistance = maxDistance;
                }

                world.AddScheduledGust(world.CombatStartTick + Ticks.FromSeconds(g.TimeSec), gust);
                count++;
            }

            return count;
        }
    }
}
