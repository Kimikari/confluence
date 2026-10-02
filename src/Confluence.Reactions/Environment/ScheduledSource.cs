using System.Collections.Generic;

namespace Confluence.Reactions.Environment
{
    /// <summary>
    /// A fixed, announced refill pulse of map-owned material (spec L138 "A map with actual rainfall supplies a fixed,
    /// announced refill pulse and respects the same water-cell maximum as spells"; L668 "Their finite units and any
    /// refill schedule are part of the biome rule"). Each pulse adds natural units to every listed cell up to the
    /// per-cell maximum and never touches bodies.
    /// </summary>
    public sealed class ScheduledSource
    {
        public int Id { get; set; }

        /// <summary>Catalogue record whose rule this implements (for example BIO-001).</summary>
        public string RuleId { get; set; }

        public Material Material { get; set; } = Material.Water;
        public int UnitsPerPulse { get; set; } = 1;
        public int MaxUnitsPerCell { get; set; } = Materials.MaxUnits;

        /// <summary>Absolute tick of the first pulse.</summary>
        public int FirstPulseTick { get; set; }

        public int IntervalTicks { get; set; }

        /// <summary>Length of the visible pulse cue (BIO-001: 0.5 s).</summary>
        public int VisibleTicks { get; set; }

        /// <summary>Patch id the cells came from (for presentation and mirrored pairing), or -1.</summary>
        public int PatchIndex { get; set; } = -1;

        /// <summary>Grid cells refilled by each pulse, ascending.</summary>
        public List<int> Cells { get; set; } = new List<int>();

        /// <summary>Next pulse tick (runtime state).</summary>
        public int NextPulseTick { get; set; }

        /// <summary>Pulses fired so far (runtime state).</summary>
        public int PulsesFired { get; set; }

        public ScheduledSource Clone()
        {
            var copy = (ScheduledSource)MemberwiseClone();
            copy.Cells = new List<int>(Cells);
            return copy;
        }
    }

    /// <summary>A gust from the arena's announced schedule (spec L664 "gust schedules").</summary>
    public sealed class ScheduledGust
    {
        public int StartTick { get; set; }
        public Operations.WindGust Gust { get; set; }
        public bool Fired { get; set; }
    }
}
