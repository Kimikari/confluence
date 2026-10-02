using System;
using System.Collections.Generic;
using Confluence.Arena.Navigation;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Arena.Validation
{
    /// <summary>Usable floor and cover areas of a layout (spec L306, L310).</summary>
    public struct CoverStats
    {
        public float UsableArea;
        public float CoverArea;
        public float CoverAreaSide0;
        public float CoverAreaSide1;
        public float ElevatedAreaSide0;
        public float ElevatedAreaSide1;

        public float CoverFraction => UsableArea <= 0f ? 0f : CoverArea / UsableArea;
    }

    /// <summary>
    /// Measurements shared by the generator's tuning passes and the validator. Distances are meters of travel for a
    /// player capsule on the navigation grid (walking, common jumps, safe drops). Results are cached per survey; a
    /// survey belongs to one layout state and one navigation build.
    /// </summary>
    public sealed class ArenaSurvey
    {
        private static readonly NavQuery AnyClearance = new NavQuery { MinClearance = 0f };

        private readonly float[][] _spawnDistances = new float[2][];
        private readonly int[] _spawnNodes = { -2, -2 };
        private List<int> _finalFloor;
        private bool[] _reachable;
        private bool[] _reachableAny;
        private List<Solid> _coverSolids;

        public ArenaSurvey(ArenaLayout layout, ArenaNav nav)
        {
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            Nav = nav ?? throw new ArgumentNullException(nameof(nav));
        }

        public ArenaLayout Layout { get; }
        public ArenaNav Nav { get; }

        // ---- Anchor nodes ---------------------------------------------------------------------------

        /// <summary>Navigation node at a side's exact spawn position, or -1.</summary>
        public int SpawnNode(int side)
        {
            if (_spawnNodes[side] == -2)
            {
                SpawnPoint spawn = Layout.Spawns[side];
                _spawnNodes[side] = Nav.FindNode(spawn.Position, 0.5f, 1.0f, ArenaRules.CapsuleClearance);
            }

            return _spawnNodes[side];
        }

        /// <summary>Main-floor nodes on the central final floor (radius 4 m, D-027) a capsule can stand on.</summary>
        public IReadOnlyList<int> FinalFloorNodes
        {
            get
            {
                if (_finalFloor == null)
                {
                    _finalFloor = new List<int>();
                    float r = Layout.FinalFloorRadius - ArenaRules.CapsuleClearance;
                    for (int node = 0; node < Nav.NodeCount; node++)
                    {
                        Vec3 p = Nav.NodePosition(node);
                        if (MathF.Abs(p.Y - Layout.Center.Y) <= ArenaRules.FloorLevelTolerance
                            && Vec3.DistanceXZ(p, Layout.Center) <= r && Nav.Clearance(node) >= ArenaRules.CapsuleClearance)
                        {
                            _finalFloor.Add(node);
                        }
                    }
                }

                return _finalFloor;
            }
        }

        // ---- Distances --------------------------------------------------------------------------------

        public float[] DistancesFromSpawn(int side)
        {
            if (_spawnDistances[side] == null)
            {
                int node = SpawnNode(side);
                _spawnDistances[side] = node < 0 ? Fill(float.PositiveInfinity) : Nav.Distances(new[] { node }, NavQuery.Capsule);
            }

            return _spawnDistances[side];
        }

        /// <summary>Initial shortest spawn-to-spawn path (spec L310).</summary>
        public float SpawnPathLength()
        {
            int other = SpawnNode(1);
            return other < 0 ? float.PositiveInfinity : DistancesFromSpawn(0)[other];
        }

        /// <summary>Shortest path from a spawn to the central final floor.</summary>
        public float CenterPathLength(int side) => MinOver(DistancesFromSpawn(side), FinalFloorNodes);

        /// <summary>Nodes reachable by a capsule from either spawn (the accessible floor).</summary>
        public bool[] ReachableFromSpawns
        {
            get
            {
                if (_reachable == null)
                {
                    _reachable = Nav.Reachable(new[] { SpawnNode(0), SpawnNode(1) }, NavQuery.Capsule);
                }

                return _reachable;
            }
        }

        // ---- Central cover (spec L310) ----------------------------------------------------------------

        /// <summary>
        /// "Useful central cover" for a side: a capsule node on the main floor within
        /// <see cref="ArenaRules.CentralCoverRadius"/> of the center, standing within
        /// <see cref="ArenaRules.UsefulCoverAdjacency"/> of a cover solid that by itself blocks the line from a
        /// crouched body (<see cref="ArenaRules.CoverTestHeight"/>) to the opponent spawn eye (1.6 m). Alcove walls never count.
        /// </summary>
        public List<int> UsefulCoverNodes(int side)
        {
            var result = new List<int>();
            Vec3 enemyEye = Layout.Spawns[1 - side].Position + new Vec3(0f, GlobalRules.Movement.StandingEyeHeight, 0f);
            var candidates = new List<Solid>();
            foreach (Solid s in CoverSolids())
            {
                if (s.Kind != SolidKind.AlcoveWall && FootprintDistance(s.Box, Layout.Center.XZ()) <= ArenaRules.CentralCoverRadius)
                {
                    candidates.Add(s);
                }
            }

            for (int node = 0; node < Nav.NodeCount; node++)
            {
                Vec3 p = Nav.NodePosition(node);
                if (MathF.Abs(p.Y - Layout.Center.Y) > ArenaRules.FloorLevelTolerance || Nav.Clearance(node) < ArenaRules.CapsuleClearance
                    || Vec3.DistanceXZ(p, Layout.Center) > ArenaRules.CentralCoverRadius)
                {
                    continue;
                }

                Vec3 eye = p + new Vec3(0f, ArenaRules.CoverTestHeight, 0f);
                for (int i = 0; i < candidates.Count; i++)
                {
                    Solid s = candidates[i];
                    if (FootprintDistance(s.Box, p.XZ()) <= ArenaRules.UsefulCoverAdjacency && SegmentHitsSolid(s, eye, enemyEye))
                    {
                        result.Add(node);
                        break;
                    }
                }
            }

            return result;
        }

        /// <summary>Seconds a walking melee build needs from its spawn to useful central cover (spec L310).</summary>
        public float CentralReachSeconds(int side)
        {
            float d = MinOver(DistancesFromSpawn(side), UsefulCoverNodes(side));
            return d / ArenaRules.CentralReachReferenceSpeed;
        }

        // ---- Cover and usable area (spec L306, L310) -----------------------------------------------------

        /// <summary>
        /// Solids that count as cover: sight-blocking pieces at least crouch-eye high (1.0 m), including solid platforms,
        /// standing
        /// on the main floor or on a walkable surface. Overhead pieces (lintels, bells, roofs) do not count.
        /// </summary>
        public List<Solid> CoverSolids()
        {
            if (_coverSolids != null)
            {
                return _coverSolids;
            }

            _coverSolids = new List<Solid>();
            for (int i = 0; i < Layout.Solids.Count; i++)
            {
                Solid s = Layout.Solids[i];
                if (IsCover(s, Layout.Solids))
                {
                    _coverSolids.Add(s);
                }
            }

            return _coverSolids;
        }

        public static bool IsCover(Solid s, IReadOnlyList<Solid> all)
        {
            // A solid platform at least crouch-eye high shields the floor beside it like any wall; its top is
            // elevated floor and counts once in the usable area.
            if (s.Walkable && s.Kind == SolidKind.Platform && s.Box.Bottom <= 0.05f && s.Box.Top >= ArenaRules.CoverMinHeight - 1e-3f)
            {
                return true;
            }

            if (s.Walkable || s.Kind == SolidKind.Floor || !s.BlocksSight || s.Box.Top - s.Box.Bottom < ArenaRules.CoverMinHeight - 1e-3f)
            {
                return false;
            }

            if (s.Box.Bottom <= 0.05f)
            {
                return true;
            }

            Vec2 c = s.Box.Center.XZ();
            for (int i = 0; i < all.Count; i++)
            {
                Solid w = all[i];
                if (w.Walkable && MathF.Abs(w.Box.Top - s.Box.Bottom) <= 0.05f && w.Box.ContainsXZ(c))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Usable floor: grid cells inside the initial contour that are accessible floor or lie under cover. Cover
        /// fraction is the covered share of that area (spec L310 "Cover occupies 18 to 30 percent of usable floor").
        /// </summary>
        public CoverStats Cover()
        {
            int columns = Nav.ColumnCount;
            var covered = new bool[columns];
            foreach (Solid s in CoverSolids())
            {
                Rect2Columns(s.Box, (column, center) =>
                {
                    if (s.Box.ContainsXZ(center))
                    {
                        covered[column] = true;
                    }
                });
            }

            if (_reachableAny == null)
            {
                _reachableAny = Nav.Reachable(new[] { SpawnNode(0), SpawnNode(1) }, AnyClearance);
            }

            float cell = Nav.CellSize * Nav.CellSize;
            var stats = new CoverStats();
            for (int c = 0; c < columns; c++)
            {
                Vec2 center = Nav.ColumnCenter(c);
                if (Layout.Boundary.RadialMargin(center.ToXZ(), 0f) < 0f)
                {
                    continue;
                }

                bool side0 = center.Y < Layout.Center.Z;
                bool accessible = false;
                bool elevated = false;
                for (int m = Nav.FirstNodeInColumn(c), end = m + Nav.NodeCountInColumn(c); m < end; m++)
                {
                    if (_reachableAny[m])
                    {
                        accessible = true;
                        elevated |= Nav.NodeHeight(m) > ArenaRules.FloorLevelTolerance;
                    }
                }

                if (covered[c])
                {
                    stats.CoverArea += cell;
                    if (side0) stats.CoverAreaSide0 += cell; else stats.CoverAreaSide1 += cell;
                }

                if (covered[c] || accessible)
                {
                    stats.UsableArea += cell;
                }

                if (elevated)
                {
                    if (side0) stats.ElevatedAreaSide0 += cell; else stats.ElevatedAreaSide1 += cell;
                }
            }

            return stats;
        }

        // ---- Approaches (spec L308, L310, L312) -------------------------------------------------------

        /// <summary>
        /// Up to two disjoint approaches from a spawn to the final floor along 2.5 m routes: the shortest route, then
        /// the shortest route that keeps at least <see cref="ArenaRules.ApproachSeparation"/> from it everywhere
        /// except inside the spawn alcove and on the final floor, so no single 3 m placement touches both.
        /// </summary>
        public List<List<int>> DisjointApproaches(int side) => DisjointApproaches(side, FinalFloorNodes);

        /// <summary>Up to two disjoint 2.5 m approaches from a spawn to any of the target nodes (see above).</summary>
        public List<List<int>> DisjointApproaches(int side, IReadOnlyList<int> targets)
        {
            var result = new List<List<int>>();
            int start = SpawnNode(side);
            if (start < 0 || targets.Count == 0)
            {
                return result;
            }

            int[] parents = new int[Nav.NodeCount];
            float[] dist = Nav.Distances(new[] { start }, NavQuery.Route, parents);
            int goal = ArgMin(dist, targets);
            if (goal < 0)
            {
                return result;
            }

            List<int> first = ArenaNav.TracePath(parents, goal);
            result.Add(first);

            bool[] blocked = BlockNear(first, ArenaRules.ApproachSeparation, Layout.Spawns[side].Position.XZ(), ArenaRules.AlcoveExemptRadius);
            NavQuery separated = NavQuery.Route.WithBlocked(blocked);
            dist = Nav.Distances(new[] { start }, separated, parents);
            goal = ArgMin(dist, targets);
            if (goal >= 0)
            {
                result.Add(ArenaNav.TracePath(parents, goal));
            }

            return result;
        }

        /// <summary>Capsule nodes on the main floor inside a lane's corridor (within half a lane zone of its segment).</summary>
        public List<int> LaneCorridorNodes(FiringLane lane, float halfWidth)
        {
            var result = new List<int>();
            for (int node = 0; node < Nav.NodeCount; node++)
            {
                Vec3 p = Nav.NodePosition(node);
                if (MathF.Abs(p.Y - Layout.Center.Y) > ArenaRules.FloorLevelTolerance || Nav.Clearance(node) < ArenaRules.CapsuleClearance)
                {
                    continue;
                }

                Vec2 q = p.XZ();
                if (Vec2.Distance(Intersections.ClosestPointOnSegment(q, lane.A, lane.B), q) <= halfWidth)
                {
                    result.Add(node);
                }
            }

            return result;
        }

        /// <summary>Marks nodes within a horizontal radius of any path node, sparing an exempt disc and the final floor.</summary>
        public bool[] BlockNear(List<int> path, float radius, Vec2 exemptCenter, float exemptRadius, float centralExempt = -1f)
        {
            var blocked = new bool[Nav.NodeCount];
            float finalExempt = centralExempt >= 0f ? centralExempt : Layout.FinalFloorRadius + 1f;
            for (int i = 0; i < path.Count; i++)
            {
                Vec2 p = Nav.NodePosition(path[i]).XZ();
                if (Vec2.Distance(p, exemptCenter) <= exemptRadius || Vec2.Distance(p, Layout.Center.XZ()) <= finalExempt)
                {
                    continue;
                }

                MarkDisc(blocked, p, radius, exemptCenter, exemptRadius, finalExempt);
            }

            return blocked;
        }

        /// <summary>Marks every node whose column center lies within radius of p (except exempt regions).</summary>
        public void MarkDisc(bool[] blocked, Vec2 p, float radius, Vec2 exemptCenter, float exemptRadius, float finalExempt)
        {
            int c0 = Nav.ColumnAt(new Vec2(p.X - radius, p.Y - radius));
            int c1 = Nav.ColumnAt(new Vec2(p.X + radius, p.Y + radius));
            int n = Nav.Size;
            int i0 = c0 < 0 ? 0 : c0 % n;
            int j0 = c0 < 0 ? 0 : c0 / n;
            int i1 = c1 < 0 ? n - 1 : c1 % n;
            int j1 = c1 < 0 ? n - 1 : c1 / n;
            float r2 = radius * radius;
            for (int j = j0; j <= j1; j++)
            {
                for (int i = i0; i <= i1; i++)
                {
                    int c = j * n + i;
                    Vec2 q = Nav.ColumnCenter(c);
                    if (Vec2.DistanceSquared(q, p) > r2 || Vec2.Distance(q, exemptCenter) <= exemptRadius
                        || Vec2.Distance(q, Layout.Center.XZ()) <= finalExempt)
                    {
                        continue;
                    }

                    for (int m = Nav.FirstNodeInColumn(c), end = m + Nav.NodeCountInColumn(c); m < end; m++)
                    {
                        blocked[m] = true;
                    }
                }
            }
        }

        // ---- Contraction (spec L48, L676) -------------------------------------------------------------

        /// <summary>
        /// First accessible cell that loses every route to the final floor while the boundary contracts. Every
        /// <see cref="ArenaRules.ContractionSampleStepSec"/> from the start to the end of the contraction, the route
        /// may use only cells a capsule fits inside the contour; cells within
        /// <see cref="ArenaRules.ContractionEdgeTolerance"/> of the moving edge are already leaving the legal space.
        /// Being cut off is monotone in time (the contour only shrinks), so a cell first found cut at a sample was cut
        /// somewhere in the preceding interval; when it was still outside the edge band at the interval start, the
        /// interval is re-sampled <see cref="ArenaRules.ContractionRefineSteps"/> times to find whether the cut came
        /// while the cell was still well inside the contour.
        /// </summary>
        public bool FindContractionFailure(out float timeSec, out int failedNode)
        {
            timeSec = 0f;
            failedNode = -1;
            BoundaryContour b = Layout.Boundary;
            bool[] reached = ReachableFromSpawns;
            int count = Nav.NodeCount;
            var angle = new float[count];
            var dist = new float[count];
            for (int node = 0; node < count; node++)
            {
                Vec3 p = Nav.NodePosition(node) - b.Center;
                angle[node] = CMath.YawOf(new Vec3(p.X, 0f, p.Z));
                dist[node] = MathF.Sqrt(p.X * p.X + p.Z * p.Z);
            }

            var allowed = new bool[count];
            var suspects = new List<int>();
            bool[] previous = null;
            float previousT = b.ContractStartSec;
            float step = ArenaRules.ContractionSampleStepSec;
            for (float t = b.ContractStartSec; t <= b.ContractEndSec + 1e-3f; t += step)
            {
                bool[] canReach = CanReachFinalFloorAt(b, t, angle, dist, allowed);
                suspects.Clear();
                for (int node = 0; node < count; node++)
                {
                    if (!reached[node] || canReach[node] || Nav.Clearance(node) < ArenaRules.CapsuleClearance)
                    {
                        continue;
                    }

                    if (b.RadiusAt(angle[node], t) - dist[node] >= ArenaRules.ContractionEdgeTolerance)
                    {
                        timeSec = t;
                        failedNode = node;
                        return true;
                    }

                    if (previous != null && previous[node] && b.RadiusAt(angle[node], previousT) - dist[node] >= ArenaRules.ContractionEdgeTolerance)
                    {
                        suspects.Add(node);
                    }
                }

                for (int k = 1; k < ArenaRules.ContractionRefineSteps && suspects.Count > 0; k++)
                {
                    float tau = previousT + step * k / ArenaRules.ContractionRefineSteps;
                    bool[] fine = CanReachFinalFloorAt(b, tau, angle, dist, allowed);
                    for (int i = suspects.Count - 1; i >= 0; i--)
                    {
                        int node = suspects[i];
                        float margin = b.RadiusAt(angle[node], tau) - dist[node];
                        if (!fine[node] && margin >= ArenaRules.ContractionEdgeTolerance)
                        {
                            timeSec = tau;
                            failedNode = node;
                            return true;
                        }

                        if (!fine[node] || margin < ArenaRules.ContractionEdgeTolerance)
                        {
                            suspects.RemoveAt(i); // cut only once inside the edge band, or already inside it
                        }
                    }
                }

                previous = canReach;
                previousT = t;
            }

            return false;
        }

        /// <summary>Cells that can still walk to the final floor through cells a capsule fits inside the contour at a time.</summary>
        public bool[] CanReachFinalFloorAt(float timeSec)
        {
            BoundaryContour b = Layout.Boundary;
            int count = Nav.NodeCount;
            var allowed = new bool[count];
            for (int node = 0; node < count; node++)
            {
                allowed[node] = b.RadialMargin(Nav.NodePosition(node), timeSec) >= ArenaRules.CapsuleClearance;
            }

            return Nav.CanReach(FinalFloorNodes, NavQuery.Capsule.WithAllowed(allowed));
        }

        private bool[] CanReachFinalFloorAt(BoundaryContour b, float t, float[] angle, float[] dist, bool[] allowed)
        {
            for (int node = 0; node < allowed.Length; node++)
            {
                allowed[node] = b.RadiusAt(angle[node], t) - dist[node] >= ArenaRules.CapsuleClearance;
            }

            return Nav.CanReach(FinalFloorNodes, NavQuery.Capsule.WithAllowed(allowed));
        }

        // ---- Helpers ----------------------------------------------------------------------------------

        public static float FootprintDistance(YawBox box, Vec2 p) => Vec2.Distance(box.ClosestPointXZ(p), p);

        public static bool SegmentHitsSolid(Solid s, Vec3 a, Vec3 b)
        {
            return s.RampRise > 0f
                ? Intersections.SegmentVsRamp(a, b, new RampShape(s.Box, s.RampRise), out RayHit _)
                : Intersections.SegmentVsYawBox(a, b, s.Box, out RayHit _);
        }

        public void Rect2Columns(YawBox box, Action<int, Vec2> visit)
        {
            Aabb b = box.Bounds();
            int n = Nav.Size;
            Vec2 o = Nav.Origin;
            float cs = Nav.CellSize;
            int i0 = Math.Max(0, (int)MathF.Floor((b.Min.X - o.X) / cs));
            int j0 = Math.Max(0, (int)MathF.Floor((b.Min.Z - o.Y) / cs));
            int i1 = Math.Min(n - 1, (int)MathF.Floor((b.Max.X - o.X) / cs));
            int j1 = Math.Min(n - 1, (int)MathF.Floor((b.Max.Z - o.Y) / cs));
            for (int j = j0; j <= j1; j++)
            {
                for (int i = i0; i <= i1; i++)
                {
                    int c = j * n + i;
                    visit(c, Nav.ColumnCenter(c));
                }
            }
        }

        public static float MinOver(float[] dist, IReadOnlyList<int> nodes)
        {
            float best = float.PositiveInfinity;
            for (int i = 0; i < nodes.Count; i++)
            {
                best = Math.Min(best, dist[nodes[i]]);
            }

            return best;
        }

        public static int ArgMin(float[] dist, IReadOnlyList<int> nodes)
        {
            int best = -1;
            float bestD = float.PositiveInfinity;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (dist[nodes[i]] < bestD)
                {
                    bestD = dist[nodes[i]];
                    best = nodes[i];
                }
            }

            return best;
        }

        private float[] Fill(float value)
        {
            var a = new float[Nav.NodeCount];
            for (int i = 0; i < a.Length; i++)
            {
                a[i] = value;
            }

            return a;
        }
    }
}
