using Confluence.Core.Arena;

namespace Confluence.Reactions.Environment
{
    /// <summary>
    /// Biome environment hooks for the reaction world (spec L138, L656 "Biomes supply art and one declared
    /// environmental rule", L668). Installs the scheduled sources and gusts a biome's rule announces; each biome rule
    /// lives in its own behavior class (<see cref="RaincourtRain"/>, <see cref="WindmillYardGusts"/>).
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

            WindmillYardGusts.InstallGusts(world, layout);
        }

        /// <summary>BIO-001 gutter-bed rain (see <see cref="RaincourtRain.Install"/>). Returns the number of beds.</summary>
        public static int InstallRaincourtRain(ReactionWorld world, ArenaLayout layout) => RaincourtRain.Install(world, layout);

        /// <summary>A Raincourt gutter-bed pulse schedule with no cells yet.</summary>
        public static ScheduledSource RaincourtSource(int combatStartTick) => RaincourtRain.Source(combatStartTick);

        /// <summary>The layout's announced gusts (see <see cref="WindmillYardGusts.InstallGusts"/>).</summary>
        public static void InstallGusts(ReactionWorld world, ArenaLayout layout) => WindmillYardGusts.InstallGusts(world, layout);
    }
}
