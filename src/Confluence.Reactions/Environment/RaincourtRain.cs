using System.Collections.Generic;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Core.Time;

namespace Confluence.Reactions.Environment
{
    /// <summary>
    /// BIO-001 Raincourt: "Every 8 s a 0.5 s visible rainfall pulse adds one water unit to the two mirrored 2 m
    /// gutter beds, up to four. No rain wets players automatically." A fixed, announced refill pulse that respects the
    /// same water-cell maximum as spells (spec L138, L668; DECISIONS D-046). Rain only adds units to the gutter-bed
    /// cells; it never touches bodies, so a player becomes wet only by standing in a wet cell (Sim materials).
    /// </summary>
    [ContentBehavior("BIO-001")]
    public static class RaincourtRain
    {
        public const string BiomeId = "BIO-001";

        public const float PulseIntervalSec = 8f;   // "Every 8 s"
        public const float PulseVisibleSec = 0.5f;  // "a 0.5 s visible rainfall pulse"
        public const int UnitsPerPulse = 1;         // "adds one water unit"
        public const int MaxUnits = 4;              // "up to four" (the L136 cell maximum)

        /// <summary>
        /// One scheduled source per gutter bed. Gutter beds are the natural water patches carrying a refill rule (the
        /// arena generator's contract); when no patch carries one, every natural non-ice water patch is a gutter bed.
        /// Pulses fire at combat time 8 s, 16 s, ... and add the unit at the start of the visible 0.5 s pulse.
        /// Returns the number of beds.
        /// </summary>
        public static int Install(ReactionWorld world, ArenaLayout layout)
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
                ScheduledSource source = Source(world.CombatStartTick);
                source.PatchIndex = beds[b];
                world.RasterizePatch(layout.Materials[beds[b]], source.Cells);
                world.AddScheduledSource(source);
            }

            return beds.Count;
        }

        /// <summary>A gutter-bed pulse schedule with no cells yet.</summary>
        public static ScheduledSource Source(int combatStartTick)
        {
            int interval = Ticks.FromSeconds(PulseIntervalSec);
            return new ScheduledSource
            {
                RuleId = BiomeId,
                Material = Material.Water,
                UnitsPerPulse = UnitsPerPulse,
                MaxUnitsPerCell = MaxUnits,
                FirstPulseTick = combatStartTick + interval,
                IntervalTicks = interval,
                VisibleTicks = Ticks.FromSeconds(PulseVisibleSec),
            };
        }
    }
}
