using System;
using Confluence.Core.Rules;

namespace Confluence.Arena.Generation
{
    /// <summary>
    /// Layout parameters of one diameter tier. The spec fixes the limits (spec L306-L312, L660); these values place
    /// the skeleton inside them. Every tier keeps a similar spawn-to-center travel (about 14-16 m of walking): the
    /// spawn path band (16-34 m) and the central reach window (2.0-3.5 s at walk speed, 11-19.25 m) leave little
    /// else, so "larger arenas add routes and useful cover, with approach travel and sightlines still constrained"
    /// (L660) means larger tiers add space around and behind the spawns rather than distance between them.
    /// </summary>
    public sealed class SizeProfile
    {
        private SizeProfile(int diameter, float spawnDistance, float bastionOffset, float frontWallHalf, int[] quota, float laneHalfLength, int outcropPairs)
        {
            Diameter = diameter;
            Radius = diameter * 0.5f;
            SpawnDistance = spawnDistance;
            BastionOffset = bastionOffset;
            FrontWallHalfWidth = frontWallHalf;
            HalfQuota = quota;
            LaneHalfLength = laneHalfLength;
            OutcropPairs = outcropPairs;
        }

        public int Diameter { get; }
        public float Radius { get; }

        /// <summary>Distance from the center to each spawn point along the spawn axis (side 0 at -Z).</summary>
        public float SpawnDistance { get; }

        /// <summary>Lateral offset of each bastion from the spawn axis (seeded sign; 0 where the arena is too small).</summary>
        public float BastionOffset { get; }

        /// <summary>
        /// Starting half width of the bastion's front screen. The screen hides the spawn (spec L47, L310) and sets the
        /// detour to the center; the assembler widens or narrows it to hit the central reach window.
        /// </summary>
        public float FrontWallHalfWidth { get; }

        /// <summary>Island modules per half arena, indexed by <see cref="ModuleCategory"/> (center excluded).</summary>
        public int[] HalfQuota { get; }

        /// <summary>Half length of the designated firing lane along the X axis (0: bounded by the contour).</summary>
        public float LaneHalfLength { get; }

        /// <summary>Mirrored pairs of boundary outcrops that dress the irregular contour.</summary>
        public int OutcropPairs { get; }

        public float TargetArea => (float)(Math.PI * Radius * Radius);

        public int QuotaFor(ModuleCategory category) => HalfQuota[(int)category];

        public static SizeProfile For(int diameter)
        {
            // Quotas: Topology, Cover, Elevation, Material, Approach, Center (per half; mirrored to the full arena).
            // Elevation 1-2 per half gives 2-4 modules and cover 2-3 per half gives 4-6 (spec L660 "two to four
            // elevation modules, and three to six cover modules"; mirroring makes the counts even).
            // Spawn distances are measured on true-size navigation: at 40 m, 10.5 m left sub-capsule slots between the
            // wing tips, the lane-end noses, and corner cover of the center module, so the spawn path ran just past the
            // 33.5 m band (spec L310).
            switch (diameter)
            {
                case GlobalRules.Arena.DiameterSmall:
                    return new SizeProfile(diameter, 8.25f, 0f, 4.5f, new[] { 0, 2, 1, 1, 1, 0 }, 0f, 6);
                case GlobalRules.Arena.DiameterMedium:
                    return new SizeProfile(diameter, 9.75f, 0f, 4.0f, new[] { 1, 2, 1, 1, 1, 0 }, 7f, 6);
                case GlobalRules.Arena.DiameterLarge:
                    return new SizeProfile(diameter, 10.25f, 0f, 3.5f, new[] { 2, 3, 2, 1, 2, 0 }, 7f, 7);
                case GlobalRules.Arena.DiameterHuge:
                    return new SizeProfile(diameter, 11.25f, 0f, 3.0f, new[] { 3, 3, 2, 2, 3, 0 }, 7f, 8);
                default:
                    throw new ArgumentOutOfRangeException(nameof(diameter), diameter, "Arena diameters are 24, 32, 40, or 48 m (spec L306).");
            }
        }
    }
}
