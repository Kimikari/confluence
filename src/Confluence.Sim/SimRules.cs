using Confluence.Core.Rules;
using Confluence.Core.Time;

namespace Confluence.Sim
{
    /// <summary>
    /// Simulation numbers that the spec leaves open and only the Sim needs. Global caps stay in
    /// <see cref="GlobalRules"/>; every value here interprets a cited spec line and is reported in the stage
    /// report for consolidation into docs/DECISIONS.md.
    /// </summary>
    public static class SimRules
    {
        /// <summary>Character motor tuning (spec L80-L82, L171-L175, L312; DECISIONS D-024).</summary>
        public static class Motor
        {
            /// <summary>Ramps up to 30 degrees are walkable (spec L312); the tolerance absorbs float error on exact 30-degree ramps.</summary>
            public const float MaxWalkableSlopeDeg = GlobalRules.Arena.RampMaxSlopeDeg + 0.5f;

            /// <summary>Longest motion per collision substep: below half the capsule radius, so no wall is tunneled.</summary>
            public const float MaxSubstepDistance = 0.15f;

            public const int DepenetrationIterations = 4;

            /// <summary>Ground snap keeps a walking player attached down standardized stairs and ramps (spec L312).</summary>
            public const float GroundSnapDistance = GlobalRules.Movement.StepHeight;

            /// <summary>Ground probes start this far above the feet so a touching contact still registers.</summary>
            public const float GroundProbeLift = 0.02f;

            /// <summary>Downward contact tolerance that still counts as standing.</summary>
            public const float GroundContactTolerance = 0.03f;

            /// <summary>Terminal fall speed (m/s); bounds per-tick motion for the collision substeps.</summary>
            public const float MaxFallSpeed = 30f;

            /// <summary>
            /// Ground acceleration factor on slippery ice: reduced braking and more lateral drift, never control loss
            /// (spec L175). A traction item interpolates back toward 1.
            /// </summary>
            public const float IceAccelerationFactor = 0.2f;

            /// <summary>An outside-map fall is detected this far below the lowest walkable surface (spec L175).</summary>
            public const float KillPlaneDepth = 6f;

            /// <summary>Movement axes below this magnitude are no movement.</summary>
            public const float MoveDeadzone = 0.05f;

            /// <summary>Sprint needs a forward movement component (the spec gives only speed and cost, L80).</summary>
            public const float SprintMinForwardInput = 0.1f;

            /// <summary>Iterations of the bisection that stops forced displacement at the boundary edge (spec L51).</summary>
            public const int EdgeBisectionIterations = 12;
        }

        /// <summary>Forced displacement (spec L116, L173).</summary>
        public static class Displacement
        {
            /// <summary>Default speed of a knockback: the 4 m cap (L173) completes in one third of a second.</summary>
            public const float KnockbackSpeed = 12f;

            /// <summary>Hostile displacement that prevents deliberate movement is hard control: at most 0.75 s (L116).</summary>
            public static readonly int MaxHostileTicks = Ticks.FromSeconds(GlobalRules.Control.HardControlMaxSec);
        }

        /// <summary>Common melee strike volume (spec L100-L102).</summary>
        public static class Melee
        {
            /// <summary>Ticks after the windup during which the strike volume sweeps (part of the recovery).</summary>
            public const int StrikeActiveTicks = 3;

            /// <summary>Samples of the sweep per active tick.</summary>
            public const int SamplesPerTick = 3;

            /// <summary>Strike origin height below the eye (chest/shoulder line).</summary>
            public const float StrikeDropBelowEye = 0.35f;

            /// <summary>Thrusts (butt strike, spear) sweep straight along the committed aim.</summary>
            public const float ThrustArcHalfDeg = 0f;

            /// <summary>Light blades slash across the committed aim, 35 degrees either side.</summary>
            public const float LightBladeArcHalfDeg = 35f;

            /// <summary>Heavy weapons sweep wider, 45 degrees either side.</summary>
            public const float HeavyArcHalfDeg = 45f;
        }

        /// <summary>Status timing (spec L116-L120).</summary>
        public static class Status
        {
            /// <summary>Damage-over-time statuses deal their per-second intensity in 0.5 s pulses.</summary>
            public const float DotIntervalSec = 0.5f;
        }

        /// <summary>Duel flow (spec L46-L53, L359).</summary>
        public static class Duel
        {
            /// <summary>Half size of the alcove used when a layout supplies no alcove volume (spec L46).</summary>
            public const float AlcoveFallbackHalfSize = 2f;

            /// <summary>Length of the results "decisive sequence" window before the duel ended (spec L359).</summary>
            public const float DecisiveSequenceSec = 5f;
        }

        /// <summary>Lag compensation (spec L423).</summary>
        public static class LagCompensation
        {
            /// <summary>Rewind depth: 150 ms is 9 ticks at 60 Hz.</summary>
            public static readonly int MaxRewindTicks = Ticks.FromMilliseconds(GlobalRules.Network.LagCompensationMaxMs);
        }
    }
}
