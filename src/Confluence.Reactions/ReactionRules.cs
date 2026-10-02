using Confluence.Core.Rules;

namespace Confluence.Reactions
{
    /// <summary>
    /// Numbers the reaction engine needs that <see cref="GlobalRules.World"/> does not define. Each value cites the
    /// spec line (docs/confluence/confluence-design.md) or catalogue record it comes from; values the spec leaves
    /// open are marked "decision" and are listed in docs/notes/reactions.md for consolidation into DECISIONS.md.
    /// </summary>
    public static class ReactionRules
    {
        /// <summary>Owner id of map-owned material, natural sources, and their reactions (spec L668).</summary>
        public const int MapOwner = -1;

        /// <summary>No owner / no source event.</summary>
        public const int NoSource = 0;

        // ---- Sight (L167) ----

        /// <summary>
        /// Float slack for the 2 m acquisition threshold, so a cloud exactly 2.0 m deep blocks even when the summed
        /// path length lands a few ulps below 2 (spec L167; decision).
        /// </summary>
        public const float SightBlockTolerance = 1e-4f;

        /// <summary>Steam column height above its surface: the "2 meter rise" of spec L144 read as the cloud's height.</summary>
        public const float SteamHeight = GlobalRules.World.SteamRise;

        // ---- Shards (L147) ----

        /// <summary>L147 gives the shard cone a 1 m length but no angle; a 90 degree cone (decision).</summary>
        public const float ShardConeHalfAngleDeg = 45f;

        // ---- Electricity (L148-L150) ----

        /// <summary>
        /// L149 "object glows before release" gives no duration; PROP-001 states "its shell glows for 0.25 s before a
        /// 1 m pulse", used as the standard glow for every conductor.
        /// </summary>
        public const float ConductorGlowSec = 0.25f;

        /// <summary>
        /// L149 "Leave its conductive contact area": the area is 1 m around the energized object, the pulse and zone
        /// radius stated by PROP-001 and PROP-005.
        /// </summary>
        public const float ConductorContactRadius = 1f;

        /// <summary>A strike point within this distance of a conductor's shape touches it (decision).</summary>
        public const float ConductorTouchDistance = 0.25f;

        /// <summary>
        /// A wet cell touches a grounded conductor when the conductor's shape lies within one cell of the cell center
        /// (L150 "Water and anchored conductor"; decision).
        /// </summary>
        public const float GroundTouchDistance = GlobalRules.World.CellSize;

        /// <summary>Default branches a grounded prop absorbs before overloading (L150; PROP-002 and BIO-012 absorb one).</summary>
        public const int DefaultGroundCapacity = 1;

        /// <summary>Ungrounded time after an overload (PROP-002 "ungrounded for 4 s", BIO-012 "loses its grounding for 4 s").</summary>
        public const float GroundOverloadSec = 4f;

        /// <summary>Water units that flood an insulation tile so it conducts (PROP-047 "Water strength 3 floods it").</summary>
        public const int InsulationFloodWater = 3;

        // ---- Damp sand (L154) ----

        /// <summary>
        /// L154's response column "Dry it with bounded heat": one heat unit dries one damp unit once no liquid water
        /// is left in the cell (decision; the heat is consumed, the bound water is already spent).
        /// </summary>
        public const int DryHeatPerDampUnit = 1;

        // ---- Contact with bodies ----

        /// <summary>
        /// Feet within this height of a cell's surface touch the cell. Equal to the motor's step height (D-024), so a
        /// body standing on the surface always touches it and a jumping body clears it (L148 "jump the short pulse").
        /// </summary>
        public const float GroundContactTolerance = GlobalRules.Movement.StepHeight;

        /// <summary>
        /// Per-second contact hazards (burning growth, red-hot surfaces, venom) hit one body at most once per second,
        /// each hit dealing one second of the declared rate (L173 applies the same cadence to props; decision).
        /// </summary>
        public const float ContactIntervalSec = 1f;

        /// <summary>A body touches a hot surface when its capsule comes within this distance of the surface box (decision).</summary>
        public const float SurfaceContactTolerance = 0.1f;

        // ---- Fields and volumes ----

        /// <summary>Player-created fields and clouds last at most 8 s (spec L128).</summary>
        public const float PlayerFieldMaxLifetimeSec = GlobalRules.FieldsAndSummons.MaxLifetimeSec;

        /// <summary>Safety bound on repeated in-cell reactions in one step; every iteration consumes units, so it never binds.</summary>
        public const int ConsumptionIterationLimit = 32;

        // ---- Sound (L156) ----

        /// <summary>Hearing transmission through one ordinary solid (decision; presentation-level, never damage).</summary>
        public const float HearingTransmissionPerSolid = 0.5f;

        /// <summary>Hearing transmission through an absorbent face (PROP-033 "Attenuates footsteps ... by 25%").</summary>
        public const float HearingTransmissionAbsorbent = 0.75f;

        // ---- Gravity (L160) ----

        /// <summary>BIO-023 coils deflect at most 10 degrees, stricter than the global 20 (L160).</summary>
        public const float MagneticCoilMaxDeflectionDeg = 10f;
    }
}
