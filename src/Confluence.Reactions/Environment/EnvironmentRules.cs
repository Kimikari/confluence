using System;
using System.Collections.Generic;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;

namespace Confluence.Reactions.Environment
{
    /// <summary>
    /// Biome environment hooks for the reaction world (spec L138, L656 "Biomes supply art and one declared
    /// environmental rule", L668). Installs the scheduled sources and gusts a biome's rule announces.
    /// </summary>
    public static class EnvironmentRules
    {
        public const string RaincourtId = "BIO-001";
        public const string WindmillYardId = "BIO-011";

        // BIO-001 Raincourt: "Every 8 s a 0.5 s visible rainfall pulse adds one water unit to the two mirrored 2 m
        // gutter beds, up to four. No rain wets players automatically."
        public const float RaincourtPulseIntervalSec = 8f;
        public const float RaincourtPulseVisibleSec = 0.5f;
        public const int RaincourtUnitsPerPulse = 1;
        public const int RaincourtMaxUnits = 4;

        // BIO-011 Windmill Yard: "Every 12 s an announced 2 s gust moves clouds up to 2 m along the shown compass
        // direction; force strength 1." Strength 1 moves clouds only because the record says so (D-023).
        public const float WindmillGustMaxDistance = 2f;

        /// <summary>Installs the layout's biome rule and gust schedule into the world.</summary>
        public static void Install(ReactionWorld world, ArenaLayout layout)
        {
            if (world == null || layout == null)
            {
                return;
            }

            if (layout.BiomeId == RaincourtId)
            {
                InstallRaincourtRain(world, layout);
            }

            InstallGusts(world, layout);
        }

        /// <summary>
        /// BIO-001: one scheduled source per gutter bed. Gutter beds are the natural water patches carrying a refill
        /// rule (the arena generator's contract); when no patch carries one, every natural water patch is a gutter bed.
        /// Pulses fire at combat time 8 s, 16 s, ... and add the unit at the start of the visible 0.5 s pulse.
        /// </summary>
        public static int InstallRaincourtRain(ReactionWorld world, ArenaLayout layout)
        {
            var beds = new List<int>();
            for (int i = 0; i < layout.Materials.Count; i++)
            {
                MaterialPatch patch = layout.Materials[i];
                if (patch != null && patch.NaturalSource && patch.Water > 0 && !string.IsNullOrEmpty(patch.RefillRule))
                {
                    beds.Add(i);
                }
            }

            if (beds.Count == 0)
            {
                for (int i = 0; i < layout.Materials.Count; i++)
                {
                    MaterialPatch patch = layout.Materials[i];
                    if (patch != null && patch.NaturalSource && patch.Water > 0 && (patch.Flags & MaterialFlags.Ice) == 0)
                    {
                        beds.Add(i);
                    }
                }
            }

            for (int b = 0; b < beds.Count; b++)
            {
                var source = RaincourtSource(world.CombatStartTick);
                source.PatchIndex = beds[b];
                world.RasterizePatch(layout.Materials[beds[b]], source.Cells);
                world.AddScheduledSource(source);
            }

            return beds.Count;
        }

        /// <summary>A Raincourt gutter-bed pulse schedule with no cells yet.</summary>
        public static ScheduledSource RaincourtSource(int combatStartTick)
        {
            int interval = Ticks.FromSeconds(RaincourtPulseIntervalSec);
            return new ScheduledSource
            {
                RuleId = RaincourtId,
                Material = Material.Water,
                UnitsPerPulse = RaincourtUnitsPerPulse,
                MaxUnitsPerCell = RaincourtMaxUnits,
                FirstPulseTick = combatStartTick + interval,
                IntervalTicks = interval,
                VisibleTicks = Ticks.FromSeconds(RaincourtPulseVisibleSec),
            };
        }

        /// <summary>
        /// Converts the layout's announced gusts (spec L664) into scheduled winds. Gusts follow the global wind rule
        /// (strength above 1 translates clouds, L151) unless the biome's own text moves clouds at strength 1 (BIO-011).
        /// </summary>
        public static void InstallGusts(ReactionWorld world, ArenaLayout layout)
        {
            if (layout.Gusts == null)
            {
                return;
            }

            bool windmill = layout.BiomeId == WindmillYardId;
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
                    gust.MaxDistance = WindmillGustMaxDistance;
                }

                world.AddScheduledGust(world.CombatStartTick + Ticks.FromSeconds(g.TimeSec), gust);
            }
        }
    }
}
