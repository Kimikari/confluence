using System;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Arena
{
    /// <summary>
    /// Arena-generation numbers the spec leaves open or states only in prose. Global caps stay in
    /// <see cref="GlobalRules.Arena"/>; everything here is either a local reading of §7 (spec L302-L335) or a
    /// measurement convention the validator and bots share. Each value cites the line it interprets.
    /// </summary>
    public static class ArenaRules
    {
        // ---- Navigation grid -------------------------------------------------------------------

        /// <summary>Navigation and validation cell size; the same 0.5 m lattice as interaction cells (spec L136).</summary>
        public const float NavCellSize = GlobalRules.World.CellSize;

        /// <summary>Clearance a player capsule needs to pass (capsule radius, DECISIONS D-024).</summary>
        public const float CapsuleClearance = GlobalRules.Movement.CapsuleRadius;

        /// <summary>Clearance of a 2.5 m route: half the minimum route width (spec L312).</summary>
        public const float RouteClearance = GlobalRules.Arena.MinRouteWidth * 0.5f;

        /// <summary>Headroom a walkable surface needs: a standing capsule (D-024).</summary>
        public const float Headroom = GlobalRules.Movement.StandingHeight;

        /// <summary>Extra path cost of a common-jump ledge climb, in meters of walking (MOD-030, MOD-066 terraces).</summary>
        public const float JumpEdgePenalty = 1.0f;

        /// <summary>Extra path cost of a safe drop, in meters of walking.</summary>
        public const float DropEdgePenalty = 0.5f;

        // ---- Spawn and central reach (spec L310) ----------------------------------------------

        /// <summary>
        /// Reference speed of the "mobile melee build" that must reach useful central cover in 2.0-3.5 s (spec L310).
        /// Generation is build-independent (L325), so no equipment bonus applies; sprint disallows normal melee
        /// strikes (L80), so a melee build that arrives ready to strike moves at walk speed. At sprint speed the
        /// 2.0 s floor would demand at least 14 m to central cover, which the 24 m arena cannot combine with the
        /// size-graded 16-34 m spawn path; walk speed keeps all §7 distance limits jointly satisfiable.
        /// </summary>
        public const float CentralReachReferenceSpeed = GlobalRules.Movement.WalkSpeed;

        /// <summary>
        /// "Useful central cover": a standing node within this distance of the center whose crouched sightline to
        /// the opponent's spawn is blocked by a center-module cover piece within <see cref="UsefulCoverAdjacency"/>.
        /// The distance is the final floor radius plus 1.5 m: the center module ring around the 8 m floor (spec L48, L310, L678).
        /// </summary>
        public const float CentralCoverRadius = GlobalRules.Boundary.FinalFloorRadius + 1.5f;

        /// <summary>A node is "at" a cover piece when the piece's footprint lies within this horizontal distance.</summary>
        public const float UsefulCoverAdjacency = 1.0f;

        /// <summary>
        /// Height of the crouched body a useful cover piece must shield (the middle of the 1.2 m crouched capsule, D-024):
        /// a 1 m crouch cover (MOD-013) hides a crouched player's body from a standing opponent.
        /// </summary>
        public const float CoverTestHeight = 0.7f;

        /// <summary>Minimum cover height that shields a crouched player (crouch eye height, D-024).</summary>
        public const float CoverMinHeight = GlobalRules.Movement.CrouchEyeHeight;

        /// <summary>
        /// Shortest spawn-to-spawn path band "according to size" (spec L310): the 16-34 m range is split so the
        /// lower and upper limits each grow with the diameter. Both ends stay inside the global 16-34 m range.
        /// </summary>
        public static void SpawnPathBand(int diameter, out float min, out float max)
        {
            float t = CMath.Clamp01((diameter - GlobalRules.Arena.DiameterSmall)
                / (float)(GlobalRules.Arena.DiameterHuge - GlobalRules.Arena.DiameterSmall));
            min = GlobalRules.Arena.SpawnPathMin + t * 6f;  // 16 .. 22 m
            max = GlobalRules.Arena.SpawnPathMax - (1f - t) * 1.5f; // 32.5 .. 34 m
        }

        // ---- Firing lanes (spec L310) ----------------------------------------------------------

        /// <summary>Lane endpoints are standing players on the main floor; the lane is open where nothing blocks this
        /// band of heights above the floor (chest 1.2 m to eye 1.6 m, D-024).</summary>
        public const float LaneBandLow = 1.2f;
        public const float LaneBandHigh = GlobalRules.Movement.StandingEyeHeight;

        /// <summary>Directions sampled when measuring lanes (every 7.5 degrees over a half turn).</summary>
        public const int LaneDirections = 24;

        /// <summary>Spacing of parallel sample lines when measuring lanes.</summary>
        public const float LaneLineSpacing = 0.5f;

        // ---- Fields, walls, and chokepoints (spec L312, L322, L676) -----------------------------

        /// <summary>
        /// Representative control placement: a disc of this radius covers any 3 m wall (SPL-018, SPL-067 "3 m-wide")
        /// in any orientation, and any field up to 3 m across.
        /// </summary>
        public const float RepresentativeFieldRadius = 1.5f;

        /// <summary>Two approaches are "disjoint" when no single representative placement can touch both.</summary>
        public const float ApproachSeparation = 2f * RepresentativeFieldRadius;

        /// <summary>
        /// Both approaches of a spawn leave through its alcove, so separation and field placements spare a disc of this
        /// radius around the spawn point (the alcove interior; a wall formed under a player lifts them, spec L171).
        /// </summary>
        public const float AlcoveExemptRadius = 3.0f;

        /// <summary>Spacing of representative field placements along each approach.</summary>
        public const float FieldSampleSpacing = 2f;

        // ---- Contraction (spec L48, L676) ------------------------------------------------------

        /// <summary>Contraction is checked at this time step between 55 s and 85 s.</summary>
        public const float ContractionSampleStepSec = 2.5f;

        /// <summary>
        /// A floor cell must keep a route to the final floor once it is at least this far inside the contour. Routes
        /// may use any cell inside the contour. Cells closer to the moving edge are already leaving the legal space.
        /// </summary>
        public const float ContractionEdgeTolerance = 2.0f;

        // ---- Parity (spec L308) ----------------------------------------------------------------

        /// <summary>Mirrored distances may differ by grid quantization only.</summary>
        public const float ParityDistanceTolerance = 0.75f;

        /// <summary>Mirrored area counts (cover, elevation) may differ by this fraction.</summary>
        public const float ParityAreaTolerance = 0.03f;

        /// <summary>Geometric mirror match tolerance for solids, patches, and props.</summary>
        public const float MirrorTolerance = 1e-3f;

        // ---- Elevation (spec L312) -------------------------------------------------------------

        /// <summary>Lowest walkable height relative to the main floor (MOD-035 Landing Bowl is -2 m).</summary>
        public const float MinWalkableHeight = -2.0f;

        /// <summary>Main-floor tolerance: nodes within this height of 0 count as the ordinary floor.</summary>
        public const float FloorLevelTolerance = 0.35f;

        // ---- Layout dimensions ----------------------------------------------------------------

        /// <summary>Floor beyond the widest contour sample: stepping outside the boundary stays on solid ground.</summary>
        public const float FloorMarginBeyondContour = 2.5f;

        /// <summary>Main floor slab thickness: deep enough that sunken modules (-2 m) never open under it.</summary>
        public const float FloorSlabThickness = 4f;

        /// <summary>Contour irregularity: radius samples vary by at most this fraction around the base radius.</summary>
        public const float ContourIrregularity = 0.06f;

        /// <summary>
        /// Islands and filler rocks either straddle the contour (outcrops) or leave at least this strip inside it, so a
        /// cell between them and the boundary keeps a walkable route along the strip while the boundary contracts
        /// (spec L676); a narrower seam would strand the floor behind it.
        /// </summary>
        public const float ContourStripWidth = 2.25f;

        /// <summary>Margin between a placed feature and the initial contour.</summary>
        public const float ContourKeepInMargin = 0.75f;

        /// <summary>Prop instances never sit closer than this to the final-circle edge (spec L662 "final-circle support").</summary>
        public const float PropFinalCircleMargin = 0.75f;

        // ---- Standard heights for authored geometry --------------------------------------------

        public const float WallThickness = 0.5f;
        public const float TallWallHeight = 3.0f;
        public const float LowCoverHeight = 1.0f;

        /// <summary>Standardized stairs: 0.25 m risers (below the 0.35 m step, D-024) and 0.45 m treads, about 29 degrees.</summary>
        public const float StairRiser = 0.25f;
        public const float StairTread = 0.45f;

        public static float StairRunFor(float rise) => (float)Math.Ceiling(rise / StairRiser) * StairTread;

    }
}
