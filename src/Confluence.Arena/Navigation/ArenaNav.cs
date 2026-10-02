using System;
using System.Collections.Generic;
using Confluence.Arena.Props;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Arena.Navigation
{
    /// <summary>How two navigation nodes connect.</summary>
    public enum NavEdgeKind : byte
    {
        /// <summary>Ordinary walking: steps up to the 0.35 m step height or slopes up to 30 degrees (spec L312, D-024).</summary>
        Walk = 0,

        /// <summary>Common-jump ledge climb of at most 1 m (D-024; MOD-030, MOD-066).</summary>
        Jump = 1,

        /// <summary>Safe drop of at most 3 m, below the fall-damage threshold (spec L175).</summary>
        Drop = 2,
    }

    /// <summary>Options for building an <see cref="ArenaNav"/>.</summary>
    public sealed class ArenaNavOptions
    {
        public float CellSize { get; set; } = ArenaRules.NavCellSize;

        /// <summary>Half the grid side in meters; 0 sizes the grid from the layout's boundary and floor.</summary>
        public float HalfExtent { get; set; }

        /// <summary>Treat placed props as obstacles (their initial positions).</summary>
        public bool PropsAsObstacles { get; set; } = true;

        /// <summary>Solids to include; null includes all. Use it to simulate destruction or retired cover.</summary>
        public Func<Solid, bool> IncludeSolid { get; set; }

        /// <summary>Extra blockers (for example a simulated player wall), as boxes standing on the floor.</summary>
        public List<YawBox> ExtraObstacles { get; set; }
    }

    /// <summary>
    /// Layered walkable grid over an assembled arena, reusable by validators and bots. Every 0.5 m column holds one
    /// node per standable surface (main floor, decks, ramp surfaces, sunken floors) that has standing headroom.
    /// Nodes connect by walking, common-jump ledges, or safe drops. Each node stores its clearance: the horizontal
    /// distance to the nearest wall or surface edge, so routes of a given width are queries on clearance
    /// (spec L312 "Minimum route width is 2.5 meters"). Built once per layout state; queries are allocation-light.
    /// </summary>
    public sealed class ArenaNav
    {
        private const float SurfaceMergeEpsilon = 0.02f;
        private const float HeadroomFloor = 0.05f;
        private const float SupportTolerance = 0.45f;
        private const int CrossingBisections = 6;
        private const float MirrorHeightTolerance = 1e-3f;

        private static readonly int[] DirX = { 1, 0, -1, 0, 1, -1, -1, 1 };
        private static readonly int[] DirZ = { 0, 1, 0, -1, 1, 1, -1, -1 };

        private readonly float _cs;
        private readonly int _n;
        private readonly Vec2 _origin;
        private readonly List<Collider> _colliders = new List<Collider>();
        private int[] _cellStart;
        private int[] _cellItems;

        private int[] _columnFirst;
        private float[] _nodeHeight;
        private int[] _nodeColumn;
        private int[] _nodeSupport;
        private int[] _edgeStart;
        private int[] _edgeTo;
        private float[] _edgeCost;
        private NavEdgeKind[] _edgeKind;
        private float[] _clearance;
        private int[] _inStart;
        private int[] _inFrom;
        private NavEdgeKind[] _inKind;
        private int[] _rank;
        private readonly float _tanSlope;

        private struct Collider
        {
            public YawBox Box;
            public float RampRise;
            public bool Walkable;
            public bool BlocksSight;
            public int SolidId;
            public Aabb Bounds;
        }

        private ArenaNav(Vec3 center, float halfExtent, float cellSize)
        {
            _cs = cellSize;
            _n = 2 * Math.Max(1, (int)Math.Ceiling(halfExtent / cellSize));
            float half = _n * _cs * 0.5f;
            _origin = new Vec2(center.X - half, center.Z - half);
            Center = center;
            _tanSlope = MathF.Tan((GlobalRules.Arena.RampMaxSlopeDeg + 1f) * CMath.Deg2Rad);
        }

        public Vec3 Center { get; }
        public float CellSize => _cs;

        /// <summary>Columns per grid side (even, so the grid is symmetric about the center).</summary>
        public int Size => _n;

        public Vec2 Origin => _origin;
        public int ColumnCount => _n * _n;
        public int NodeCount => _nodeHeight.Length;
        public int EdgeCount => _edgeTo.Length;

        // ---- Construction ----------------------------------------------------------------------

        public static ArenaNav Build(ArenaLayout layout, ArenaNavOptions options = null)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            options = options ?? new ArenaNavOptions();
            float half = options.HalfExtent;
            if (half <= 0f)
            {
                half = (layout.Boundary == null ? layout.Diameter * 0.5f : layout.Boundary.MaxRadiusAt(0f))
                    + ArenaRules.FloorMarginBeyondContour + 1f;
            }

            var nav = new ArenaNav(layout.Center, half, options.CellSize);
            nav.AddColliders(layout, options);
            nav.BinColliders();
            nav.BuildNodes();
            nav.BuildRank();
            nav.BuildEdges();
            nav.BuildReverseEdges();
            nav.ComputeClearance();
            nav.IsHalfTurnSymmetric = nav.GraphIsHalfTurnSymmetric();
            if (nav.IsHalfTurnSymmetric)
            {
                nav.SymmetrizeClearance();
            }

            return nav;
        }

        /// <summary>
        /// True when the walkable graph is its own image under the half turn about the center: every column holds
        /// as many nodes at the same heights as its mirror column, and every edge has a mirrored edge of the same kind.
        /// Generated arenas are symmetric (spec L308); a simulated field placement or one-sided state is not.
        /// </summary>
        public bool IsHalfTurnSymmetric { get; private set; }

        private void AddColliders(ArenaLayout layout, ArenaNavOptions options)
        {
            for (int i = 0; i < layout.Solids.Count; i++)
            {
                Solid s = layout.Solids[i];
                if (s == null || (options.IncludeSolid != null && !options.IncludeSolid(s)))
                {
                    continue;
                }

                AddCollider(s.Box, s.RampRise, s.Walkable, s.BlocksSight, s.Id);
            }

            if (options.PropsAsObstacles)
            {
                for (int i = 0; i < layout.Props.Count; i++)
                {
                    if (PropShapes.TryGetCollisionBox(layout.Props[i], out YawBox box))
                    {
                        AddCollider(box, 0f, false, false, -1);
                    }
                }
            }

            if (options.ExtraObstacles != null)
            {
                for (int i = 0; i < options.ExtraObstacles.Count; i++)
                {
                    AddCollider(options.ExtraObstacles[i], 0f, false, true, -1);
                }
            }
        }

        private void AddCollider(YawBox box, float rampRise, bool walkable, bool blocksSight, int solidId)
        {
            _colliders.Add(new Collider
            {
                Box = box,
                RampRise = rampRise > 0f ? new RampShape(box, rampRise).Rise : 0f,
                Walkable = walkable,
                BlocksSight = blocksSight,
                SolidId = solidId,
                Bounds = box.Bounds(),
            });
        }

        private void BinColliders()
        {
            int columns = _n * _n;
            var counts = new int[columns + 1];
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1)
                {
                    int running = 0;
                    for (int c = 0; c < columns; c++)
                    {
                        int k = counts[c];
                        counts[c] = running;
                        running += k;
                    }

                    counts[columns] = running;
                    _cellStart = (int[])counts.Clone();
                    _cellItems = new int[running];
                }

                for (int ci = 0; ci < _colliders.Count; ci++)
                {
                    Aabb b = _colliders[ci].Bounds;
                    ColumnRange(b.Min.X, b.Min.Z, b.Max.X, b.Max.Z, out int i0, out int j0, out int i1, out int j1);
                    for (int j = j0; j <= j1; j++)
                    {
                        for (int i = i0; i <= i1; i++)
                        {
                            int c = j * _n + i;
                            if (pass == 0)
                            {
                                counts[c]++;
                            }
                            else
                            {
                                _cellItems[counts[c]++] = ci;
                            }
                        }
                    }
                }
            }
        }

        private void ColumnRange(float minX, float minZ, float maxX, float maxZ, out int i0, out int j0, out int i1, out int j1)
        {
            i0 = CMath.Clamp((int)MathF.Floor((minX - _origin.X) / _cs), 0, _n - 1);
            j0 = CMath.Clamp((int)MathF.Floor((minZ - _origin.Y) / _cs), 0, _n - 1);
            i1 = CMath.Clamp((int)MathF.Floor((maxX - _origin.X) / _cs), 0, _n - 1);
            j1 = CMath.Clamp((int)MathF.Floor((maxZ - _origin.Y) / _cs), 0, _n - 1);
        }

        private void BuildNodes()
        {
            int columns = _n * _n;
            _columnFirst = new int[columns + 1];
            var heights = new List<float>(columns + columns / 4);
            var supports = new List<int>(columns + columns / 4);
            var columnOf = new List<int>(columns + columns / 4);
            var candidates = new List<KeyValuePair<float, int>>(8);
            for (int c = 0; c < columns; c++)
            {
                _columnFirst[c] = heights.Count;
                Vec2 p = ColumnCenter(c);
                candidates.Clear();
                for (int k = _cellStart[c]; k < _cellStart[c + 1]; k++)
                {
                    int ci = _cellItems[k];
                    Collider col = _colliders[ci];
                    if (!col.Walkable || !col.Box.ContainsXZ(p))
                    {
                        continue;
                    }

                    candidates.Add(new KeyValuePair<float, int>(SurfaceHeight(col, p), ci));
                }

                if (candidates.Count == 0)
                {
                    continue;
                }

                candidates.Sort((a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.Value.CompareTo(b.Value));
                float last = float.NegativeInfinity;
                for (int k = candidates.Count - 1; k >= 0; k--)
                {
                    // Keep the highest of nearly coincident surfaces; walk downward so dedupe keeps the top one.
                    float h = candidates[k].Key;
                    if (MathF.Abs(h - last) < SurfaceMergeEpsilon)
                    {
                        continue;
                    }

                    last = h;
                    if (HasHeadroom(c, p, h, ArenaRules.Headroom))
                    {
                        heights.Add(h);
                        supports.Add(candidates[k].Value);
                        columnOf.Add(c);
                    }
                }

                // Nodes of a column are stored lowest first.
                int first = _columnFirst[c];
                int count = heights.Count - first;
                if (count > 1)
                {
                    heights.Reverse(first, count);
                    supports.Reverse(first, count);
                }
            }

            _columnFirst[columns] = heights.Count;
            _nodeHeight = heights.ToArray();
            _nodeSupport = supports.ToArray();
            _nodeColumn = columnOf.ToArray();
        }

        /// <summary>
        /// Search tie-break order that commutes with the half turn (spec L308 functional symmetry). Column c and its
        /// mirror share the canonical column min(c, mirror c); nodes are ordered by (canonical column, layer, which
        /// of the pair), so a node pair on one side expands in the same relative order as its image pair. Row-major
        /// node indices reverse under the turn, which made clearance propagation and nearest-node ties lopsided.
        /// </summary>
        private void BuildRank()
        {
            int columns = _n * _n;
            _rank = new int[_nodeHeight.Length];
            int next = 0;
            for (int c = 0; c < columns / 2; c++)
            {
                int m = MirrorColumn(c);
                int layers = Math.Max(NodeCountInColumn(c), NodeCountInColumn(m));
                for (int k = 0; k < layers; k++)
                {
                    if (k < NodeCountInColumn(c))
                    {
                        _rank[_columnFirst[c] + k] = next++;
                    }

                    if (k < NodeCountInColumn(m))
                    {
                        _rank[_columnFirst[m] + k] = next++;
                    }
                }
            }
        }

        private static float SurfaceHeight(Collider col, Vec2 p)
        {
            if (col.RampRise > 0f)
            {
                float h = new RampShape(col.Box, col.RampRise).SurfaceHeightAt(p.X, p.Y);
                return float.IsNaN(h) ? col.Box.Top : h;
            }

            return col.Box.Top;
        }

        /// <summary>Top of the collider's solid at a ground point (the ramp surface for ramps).</summary>
        private static float OccupiedTop(Collider col, Vec2 p) => SurfaceHeight(col, p);

        private bool HasHeadroom(int column, Vec2 p, float h, float headroom)
        {
            float lo = h + HeadroomFloor;
            float hi = h + headroom;
            for (int k = _cellStart[column]; k < _cellStart[column + 1]; k++)
            {
                Collider col = _colliders[_cellItems[k]];
                if (col.Bounds.Max.Y <= lo || col.Bounds.Min.Y >= hi || !col.Box.ContainsXZ(p))
                {
                    continue;
                }

                if (OccupiedTop(col, p) > lo && col.Box.Bottom < hi)
                {
                    return false;
                }
            }

            return true;
        }

        private bool IntervalClear(int column, Vec2 p, float lo, float hi)
        {
            if (hi <= lo)
            {
                return true;
            }

            for (int k = _cellStart[column]; k < _cellStart[column + 1]; k++)
            {
                Collider col = _colliders[_cellItems[k]];
                if (col.Bounds.Max.Y <= lo || col.Bounds.Min.Y >= hi || !col.Box.ContainsXZ(p))
                {
                    continue;
                }

                if (OccupiedTop(col, p) > lo && col.Box.Bottom < hi)
                {
                    return false;
                }
            }

            return true;
        }

        private bool WalkCompatible(float h1, float h2, float horizontal)
        {
            float limit = MathF.Max(GlobalRules.Movement.StepHeight, horizontal * _tanSlope) + 0.02f;
            return MathF.Abs(h2 - h1) <= limit;
        }

        private int WalkNodeIn(int column, float h, float horizontal)
        {
            int best = -1;
            float bestDelta = float.PositiveInfinity;
            for (int m = _columnFirst[column]; m < _columnFirst[column + 1]; m++)
            {
                float d = MathF.Abs(_nodeHeight[m] - h);
                if (d < bestDelta && WalkCompatible(h, _nodeHeight[m], horizontal))
                {
                    best = m;
                    bestDelta = d;
                }
            }

            return best;
        }

        private void BuildEdges()
        {
            int nodeCount = _nodeHeight.Length;
            var start = new int[nodeCount + 1];
            var to = new List<int>(nodeCount * 8);
            var cost = new List<float>(nodeCount * 8);
            var kind = new List<NavEdgeKind>(nodeCount * 8);
            float diag = _cs * MathF.Sqrt(2f);
            for (int a = 0; a < nodeCount; a++)
            {
                start[a] = to.Count;
                int c = _nodeColumn[a];
                int ci = c % _n;
                int cj = c / _n;
                float h1 = _nodeHeight[a];
                for (int dir = 0; dir < 8; dir++)
                {
                    int ni = ci + DirX[dir];
                    int nj = cj + DirZ[dir];
                    if (ni < 0 || nj < 0 || ni >= _n || nj >= _n)
                    {
                        continue;
                    }

                    int nc = nj * _n + ni;
                    bool diagonal = dir >= 4;
                    float d = diagonal ? diag : _cs;
                    int walk = WalkNodeIn(nc, h1, d);
                    if (walk >= 0)
                    {
                        if (diagonal)
                        {
                            // No corner cutting: both orthogonal columns must carry the same surface.
                            int ca = cj * _n + ni;
                            int cb = nj * _n + ci;
                            if (WalkNodeIn(ca, h1, _cs) < 0 || WalkNodeIn(cb, h1, _cs) < 0)
                            {
                                continue;
                            }
                        }

                        to.Add(walk);
                        cost.Add(d);
                        kind.Add(NavEdgeKind.Walk);
                        continue;
                    }

                    if (diagonal)
                    {
                        continue;
                    }

                    // No walkable continuation: consider a ledge climb or a safe drop into the neighbor column.
                    Vec2 pa = ColumnCenter(c);
                    Vec2 pb = ColumnCenter(nc);
                    int jump = -1;
                    int drop = -1;
                    for (int m = _columnFirst[nc]; m < _columnFirst[nc + 1]; m++)
                    {
                        float dh = _nodeHeight[m] - h1;
                        if (dh > 0f && dh <= GlobalRules.Movement.MaxCommonJumpLedge + 0.01f)
                        {
                            if (jump < 0 && IntervalClear(c, pa, h1 + ArenaRules.Headroom, _nodeHeight[m] + ArenaRules.Headroom))
                            {
                                jump = m;
                            }
                        }
                        else if (dh < 0f && -dh <= GlobalRules.Terrain.FallDamageThreshold + 0.01f)
                        {
                            if (IntervalClear(nc, pb, _nodeHeight[m] + ArenaRules.Headroom, h1 + ArenaRules.Headroom))
                            {
                                drop = m; // highest qualifying surface below wins (nodes are sorted ascending)
                            }
                        }
                    }

                    if (jump >= 0)
                    {
                        to.Add(jump);
                        cost.Add(d + ArenaRules.JumpEdgePenalty);
                        kind.Add(NavEdgeKind.Jump);
                    }

                    if (drop >= 0)
                    {
                        to.Add(drop);
                        cost.Add(d + ArenaRules.DropEdgePenalty);
                        kind.Add(NavEdgeKind.Drop);

                        // A falling capsule carries forward: also land one column farther out, clear of the wall the
                        // ledge makes for the floor right below it.
                        int fi = ni + DirX[dir];
                        int fj = nj + DirZ[dir];
                        if (fi >= 0 && fj >= 0 && fi < _n && fj < _n)
                        {
                            int nc2 = fj * _n + fi;
                            int far = WalkNodeIn(nc2, _nodeHeight[drop], _cs);
                            if (far >= 0 && IntervalClear(nc2, ColumnCenter(nc2), _nodeHeight[far] + ArenaRules.Headroom, h1 + ArenaRules.Headroom))
                            {
                                to.Add(far);
                                cost.Add(2f * d + ArenaRules.DropEdgePenalty);
                                kind.Add(NavEdgeKind.Drop);
                            }
                        }
                    }
                }
            }

            start[nodeCount] = to.Count;
            _edgeStart = start;
            _edgeTo = to.ToArray();
            _edgeCost = cost.ToArray();
            _edgeKind = kind.ToArray();
        }

        private void BuildReverseEdges()
        {
            int nodeCount = _nodeHeight.Length;
            var start = new int[nodeCount + 1];
            for (int e = 0; e < _edgeTo.Length; e++)
            {
                start[_edgeTo[e] + 1]++;
            }

            for (int i = 0; i < nodeCount; i++)
            {
                start[i + 1] += start[i];
            }

            var fill = (int[])start.Clone();
            _inFrom = new int[_edgeTo.Length];
            _inKind = new NavEdgeKind[_edgeTo.Length];
            for (int a = 0; a < nodeCount; a++)
            {
                for (int e = _edgeStart[a]; e < _edgeStart[a + 1]; e++)
                {
                    int slot = fill[_edgeTo[e]]++;
                    _inFrom[slot] = a;
                    _inKind[slot] = _edgeKind[e];
                }
            }

            _inStart = start;
        }

        // ---- Clearance ---------------------------------------------------------------------------

        private void ComputeClearance()
        {
            int nodeCount = _nodeHeight.Length;
            _clearance = new float[nodeCount];
            var srcX = new float[nodeCount];
            var srcZ = new float[nodeCount];
            var heap = new NodeHeap(nodeCount, _rank);
            for (int a = 0; a < nodeCount; a++)
            {
                _clearance[a] = float.PositiveInfinity;
                int c = _nodeColumn[a];
                int ci = c % _n;
                int cj = c / _n;
                Vec2 p = ColumnCenter(c);
                for (int dir = 0; dir < 8; dir++)
                {
                    if (IsPassable(a, dir))
                    {
                        continue;
                    }

                    Vec2 target = new Vec2(p.X + DirX[dir] * _cs, p.Y + DirZ[dir] * _cs);
                    Vec2 crossing = EdgeCrossing(a, p, target, ci + DirX[dir], cj + DirZ[dir]);
                    float dist = Vec2.Distance(p, crossing);
                    if (dist < _clearance[a])
                    {
                        _clearance[a] = dist;
                        srcX[a] = crossing.X;
                        srcZ[a] = crossing.Y;
                    }
                }

                if (!float.IsPositiveInfinity(_clearance[a]))
                {
                    heap.Push(_clearance[a], a);
                }
            }

            while (heap.Count > 0)
            {
                int a = heap.Pop(out float key);
                if (key > _clearance[a])
                {
                    continue;
                }

                Vec2 src = new Vec2(srcX[a], srcZ[a]);
                for (int e = _edgeStart[a]; e < _edgeStart[a + 1]; e++)
                {
                    if (_edgeKind[e] != NavEdgeKind.Walk)
                    {
                        continue;
                    }

                    int b = _edgeTo[e];
                    float candidate = Vec2.Distance(ColumnCenter(_nodeColumn[b]), src);
                    if (candidate < _clearance[b])
                    {
                        _clearance[b] = candidate;
                        srcX[b] = src.X;
                        srcZ[b] = src.Y;
                        heap.Push(candidate, b);
                    }
                }
            }
        }

        /// <summary>Node at the same layer of the mirror column (meaningful when the graph is half-turn symmetric).</summary>
        private int LayerMirror(int node)
        {
            int c = _nodeColumn[node];
            return _columnFirst[MirrorColumn(c)] + (node - _columnFirst[c]);
        }

        private bool GraphIsHalfTurnSymmetric()
        {
            int columns = _n * _n;
            for (int c = 0; c < columns / 2; c++)
            {
                int m = MirrorColumn(c);
                int count = NodeCountInColumn(c);
                if (count != NodeCountInColumn(m))
                {
                    return false;
                }

                for (int k = 0; k < count; k++)
                {
                    if (MathF.Abs(_nodeHeight[_columnFirst[c] + k] - _nodeHeight[_columnFirst[m] + k]) > MirrorHeightTolerance)
                    {
                        return false;
                    }
                }
            }

            for (int a = 0; a < _nodeHeight.Length; a++)
            {
                int b = LayerMirror(a);
                if (_edgeStart[a + 1] - _edgeStart[a] != _edgeStart[b + 1] - _edgeStart[b])
                {
                    return false;
                }

                for (int e = _edgeStart[a]; e < _edgeStart[a + 1]; e++)
                {
                    int target = LayerMirror(_edgeTo[e]);
                    bool found = false;
                    for (int f = _edgeStart[b]; f < _edgeStart[b + 1] && !found; f++)
                    {
                        found = _edgeTo[f] == target && _edgeKind[f] == _edgeKind[e];
                    }

                    if (!found)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// The propagated clearance is the distance to a real surface edge, so it never underestimates; on a
        /// symmetric graph each node and its image measure the same geometry, and the smaller of the two estimates
        /// is the better one for both. Without this, float rounding in rotated boxes and propagation order left
        /// mirrored cells up to half a meter apart in clearance (and so in capsule and route admissibility).
        /// </summary>
        private void SymmetrizeClearance()
        {
            for (int a = 0; a < _nodeHeight.Length; a++)
            {
                int b = LayerMirror(a);
                if (b > a)
                {
                    float c = MathF.Min(_clearance[a], _clearance[b]);
                    _clearance[a] = c;
                    _clearance[b] = c;
                }
            }
        }

        /// <summary>
        /// True when the node connects toward a neighbor column by any movement: walking, a common-jump ledge, or a safe
        /// drop (diagonals need both of their orthogonal steps). Clearance measures distance to real boundaries only,
        /// so a capsule beside a jumpable ledge or at the lip of a safe drop still counts as standing room.
        /// </summary>
        private bool IsPassable(int node, int dir)
        {
            if (HasEdgeInDirection(node, dir))
            {
                return true;
            }

            if (dir < 4)
            {
                return false;
            }

            int ox = DirX[dir] > 0 ? 0 : 2;  // +X is direction 0, -X is direction 2
            int oz = DirZ[dir] > 0 ? 1 : 3;  // +Z is direction 1, -Z is direction 3
            return HasEdgeInDirection(node, ox) && HasEdgeInDirection(node, oz);
        }

        private bool HasEdgeInDirection(int node, int dir)
        {
            int c = _nodeColumn[node];
            int target = (c / _n + DirZ[dir]) * _n + (c % _n + DirX[dir]);
            for (int e = _edgeStart[node]; e < _edgeStart[node + 1]; e++)
            {
                if (_nodeColumn[_edgeTo[e]] == target)
                {
                    return true;
                }
            }

            return false;
        }

        // Where the standable surface of node a ends on the way from p to q (bisection on standability).
        private Vec2 EdgeCrossing(int a, Vec2 p, Vec2 q, int qi, int qj)
        {
            float h = _nodeHeight[a];
            if (StandableNear(p, h, out float _) == false)
            {
                return p;
            }

            float lo = 0f;
            float hi = 1f;
            if (qi >= 0 && qj >= 0 && qi < _n && qj < _n && StandableNear(q, h, out float _))
            {
                // The far column is standable at the center but not walk-connected (headroom or slope): use the midpoint.
                return Vec2.Lerp(p, q, 0.5f);
            }

            for (int it = 0; it < CrossingBisections; it++)
            {
                float mid = 0.5f * (lo + hi);
                Vec2 m = Vec2.LerpUnclamped(p, q, mid);
                if (StandableNear(m, h, out float hm))
                {
                    lo = mid;
                    h = hm;
                }
                else
                {
                    hi = mid;
                }
            }

            return Vec2.LerpUnclamped(p, q, 0.5f * (lo + hi));
        }

        /// <summary>True if a surface within the support tolerance of hRef exists at p with standing headroom.</summary>
        private bool StandableNear(Vec2 p, float hRef, out float surface)
        {
            surface = hRef;
            int column = ColumnAt(p);
            if (column < 0)
            {
                return false;
            }

            float best = float.NaN;
            for (int k = _cellStart[column]; k < _cellStart[column + 1]; k++)
            {
                Collider col = _colliders[_cellItems[k]];
                if (!col.Walkable || !col.Box.ContainsXZ(p))
                {
                    continue;
                }

                float s = SurfaceHeight(col, p);
                if (MathF.Abs(s - hRef) <= SupportTolerance && (float.IsNaN(best) || s > best))
                {
                    best = s;
                }
            }

            if (float.IsNaN(best) || !HasHeadroom(column, p, best, ArenaRules.Headroom))
            {
                return false;
            }

            surface = best;
            return true;
        }

        // ---- Geometry access ---------------------------------------------------------------------

        public Vec2 ColumnCenter(int column)
        {
            return new Vec2(_origin.X + (column % _n + 0.5f) * _cs, _origin.Y + (column / _n + 0.5f) * _cs);
        }

        /// <summary>Column containing a ground point, or -1 outside the grid.</summary>
        public int ColumnAt(Vec2 p)
        {
            int i = (int)MathF.Floor((p.X - _origin.X) / _cs);
            int j = (int)MathF.Floor((p.Y - _origin.Y) / _cs);
            if (i < 0 || j < 0 || i >= _n || j >= _n)
            {
                return -1;
            }

            return j * _n + i;
        }

        /// <summary>The column at the 180-degree mirror position about the center (exact on the symmetric grid).</summary>
        public int MirrorColumn(int column) => (_n - 1 - column / _n) * _n + (_n - 1 - column % _n);

        public int FirstNodeInColumn(int column) => _columnFirst[column];
        public int NodeCountInColumn(int column) => _columnFirst[column + 1] - _columnFirst[column];

        public float NodeHeight(int node) => _nodeHeight[node];
        public int NodeColumn(int node) => _nodeColumn[node];

        /// <summary>Id of the solid whose surface supports the node (-1 for a non-solid support).</summary>
        public int NodeSupportSolidId(int node) => _colliders[_nodeSupport[node]].SolidId;

        /// <summary>Horizontal distance from the node to the nearest wall or surface edge.</summary>
        public float Clearance(int node) => _clearance[node];

        public Vec3 NodePosition(int node)
        {
            Vec2 p = ColumnCenter(_nodeColumn[node]);
            return new Vec3(p.X, _nodeHeight[node], p.Y);
        }

        /// <summary>Node at the mirrored position with the closest height, or -1.</summary>
        public int MirrorNode(int node)
        {
            int mc = MirrorColumn(_nodeColumn[node]);
            return NodeInColumnNear(mc, _nodeHeight[node], 0.25f);
        }

        /// <summary>Node of a column closest to a height within a tolerance, or -1.</summary>
        public int NodeInColumnNear(int column, float height, float tolerance)
        {
            if (column < 0)
            {
                return -1;
            }

            int best = -1;
            float bestD = tolerance;
            for (int m = _columnFirst[column]; m < _columnFirst[column + 1]; m++)
            {
                float d = MathF.Abs(_nodeHeight[m] - height);
                if (d <= bestD)
                {
                    best = m;
                    bestD = d;
                }
            }

            return best;
        }

        /// <summary>
        /// Nearest node to a world point: searches outward ring by ring (up to maxRadius meters) for the column node
        /// whose surface lies within maxHeightDelta of the point, preferring the smallest 3D offset. The search is
        /// exact within maxRadius: a ring is only skipped when no node beyond it can be closer.
        /// </summary>
        public int FindNode(Vec3 position, float maxHeightDelta = 1.0f, float maxRadius = 2f, float minClearance = 0f)
        {
            int center = ColumnAt(position.XZ());
            if (center < 0)
            {
                return -1;
            }

            int ci = center % _n;
            int cj = center / _n;
            int rings = Math.Max(0, (int)Math.Ceiling(maxRadius / _cs));
            int best = -1;
            float bestD = float.PositiveInfinity;
            for (int r = 0; r <= rings; r++)
            {
                for (int j = cj - r; j <= cj + r; j++)
                {
                    for (int i = ci - r; i <= ci + r; i++)
                    {
                        if (i < 0 || j < 0 || i >= _n || j >= _n || (Math.Abs(i - ci) != r && Math.Abs(j - cj) != r))
                        {
                            continue;
                        }

                        int c = j * _n + i;
                        for (int m = _columnFirst[c]; m < _columnFirst[c + 1]; m++)
                        {
                            float dh = MathF.Abs(_nodeHeight[m] - position.Y);
                            if (dh > maxHeightDelta || _clearance[m] < minClearance)
                            {
                                continue;
                            }

                            // Equidistant candidates (a point on a cell edge) resolve by the mirror-equivariant rank,
                            // so mirrored points find mirrored nodes.
                            float d = Vec3.DistanceSquared(NodePosition(m), position);
                            if (d < bestD || (d == bestD && _rank[m] < _rank[best]))
                            {
                                bestD = d;
                                best = m;
                            }
                        }
                    }
                }

                if (best >= 0 && r * _cs >= MathF.Sqrt(bestD))
                {
                    break;
                }
            }

            return best;
        }

        /// <summary>Edges of a node as (target, cost, kind) triples.</summary>
        public int EdgeBegin(int node) => _edgeStart[node];
        public int EdgeEnd(int node) => _edgeStart[node + 1];
        public int EdgeTarget(int edge) => _edgeTo[edge];
        public float EdgeCost(int edge) => _edgeCost[edge];
        public NavEdgeKind EdgeKind(int edge) => _edgeKind[edge];

        // ---- Searches ----------------------------------------------------------------------------

        /// <summary>
        /// Multi-source Dijkstra over nodes that satisfy the query. Returns path cost per node (meters of travel,
        /// infinity when unreachable) and optionally each node's predecessor for path reconstruction.
        /// </summary>
        public float[] Distances(IReadOnlyList<int> sources, NavQuery query, int[] parents = null, float maxDistance = float.PositiveInfinity)
        {
            query = query ?? NavQuery.Capsule;
            int count = _nodeHeight.Length;
            var dist = new float[count];
            for (int i = 0; i < count; i++)
            {
                dist[i] = float.PositiveInfinity;
            }

            if (parents != null)
            {
                for (int i = 0; i < count; i++)
                {
                    parents[i] = -1;
                }
            }

            var heap = new NodeHeap(Math.Min(count, 4096), _rank);
            for (int s = 0; s < sources.Count; s++)
            {
                int node = sources[s];
                if (node < 0 || !query.Allows(this, node) || dist[node] == 0f)
                {
                    continue;
                }

                dist[node] = 0f;
                heap.Push(0f, node);
            }

            while (heap.Count > 0)
            {
                int a = heap.Pop(out float key);
                if (key > dist[a])
                {
                    continue;
                }

                if (key > maxDistance)
                {
                    break;
                }

                for (int e = _edgeStart[a]; e < _edgeStart[a + 1]; e++)
                {
                    if (!query.AllowsEdge(_edgeKind[e]))
                    {
                        continue;
                    }

                    int b = _edgeTo[e];
                    float nd = key + _edgeCost[e];
                    if (nd < dist[b] && query.Allows(this, b))
                    {
                        dist[b] = nd;
                        if (parents != null)
                        {
                            parents[b] = a;
                        }

                        heap.Push(nd, b);
                    }
                }
            }

            return dist;
        }

        /// <summary>Breadth-first reachability over nodes that satisfy the query.</summary>
        public bool[] Reachable(IReadOnlyList<int> sources, NavQuery query)
        {
            query = query ?? NavQuery.Capsule;
            int count = _nodeHeight.Length;
            var seen = new bool[count];
            var queue = new int[count];
            int head = 0;
            int tail = 0;
            for (int s = 0; s < sources.Count; s++)
            {
                int node = sources[s];
                if (node >= 0 && !seen[node] && query.Allows(this, node))
                {
                    seen[node] = true;
                    queue[tail++] = node;
                }
            }

            while (head < tail)
            {
                int a = queue[head++];
                for (int e = _edgeStart[a]; e < _edgeStart[a + 1]; e++)
                {
                    int b = _edgeTo[e];
                    if (!seen[b] && query.AllowsEdge(_edgeKind[e]) && query.Allows(this, b))
                    {
                        seen[b] = true;
                        queue[tail++] = b;
                    }
                }
            }

            return seen;
        }

        /// <summary>
        /// Nodes from which some target can be reached (breadth-first over reversed edges). Drops are one-way, so this
        /// is the set that keeps a route to the targets, for example to the central final floor (spec L676).
        /// </summary>
        public bool[] CanReach(IReadOnlyList<int> targets, NavQuery query)
        {
            query = query ?? NavQuery.Capsule;
            int count = _nodeHeight.Length;
            var seen = new bool[count];
            var queue = new int[count];
            int head = 0;
            int tail = 0;
            for (int s = 0; s < targets.Count; s++)
            {
                int node = targets[s];
                if (node >= 0 && !seen[node] && query.Allows(this, node))
                {
                    seen[node] = true;
                    queue[tail++] = node;
                }
            }

            while (head < tail)
            {
                int b = queue[head++];
                for (int k = _inStart[b]; k < _inStart[b + 1]; k++)
                {
                    int a = _inFrom[k];
                    if (!seen[a] && query.AllowsEdge(_inKind[k]) && query.Allows(this, a))
                    {
                        seen[a] = true;
                        queue[tail++] = a;
                    }
                }
            }

            return seen;
        }

        /// <summary>Shortest path between two nodes as a node list (empty when unreachable).</summary>
        public List<int> FindPath(int from, int to, NavQuery query, out float length)
        {
            var parents = new int[_nodeHeight.Length];
            float[] dist = Distances(new[] { from }, query, parents);
            length = to >= 0 ? dist[to] : float.PositiveInfinity;
            return float.IsPositiveInfinity(length) ? new List<int>() : TracePath(parents, to);
        }

        /// <summary>Follows predecessors from a target back to its source; returns source-to-target order.</summary>
        public static List<int> TracePath(int[] parents, int target)
        {
            var path = new List<int>();
            int guard = parents.Length + 1;
            for (int node = target; node >= 0 && guard-- > 0; node = parents[node])
            {
                path.Add(node);
            }

            path.Reverse();
            return path;
        }

        /// <summary>World polyline of a node path (node positions).</summary>
        public List<Vec3> PathPoints(IReadOnlyList<int> path)
        {
            var points = new List<Vec3>(path.Count);
            for (int i = 0; i < path.Count; i++)
            {
                points.Add(NodePosition(path[i]));
            }

            return points;
        }

        // ---- Sight ---------------------------------------------------------------------------------

        /// <summary>
        /// True if no sight-blocking solid intersects the segment between two points (props never block sight here:
        /// they move and break, so validation does not rely on them).
        /// </summary>
        public bool HasLineOfSight(Vec3 a, Vec3 b)
        {
            Vec3 delta = b - a;
            float length = delta.Length();
            if (length < CMath.Epsilon)
            {
                return true;
            }

            var ray = new Ray(a, delta / length);
            for (int i = 0; i < _colliders.Count; i++)
            {
                Collider col = _colliders[i];
                if (!col.BlocksSight)
                {
                    continue;
                }

                if (!col.Bounds.Raycast(ray, length, out RayHit _))
                {
                    continue;
                }

                bool hit = col.RampRise > 0f
                    ? Intersections.SegmentVsRamp(a, b, new RampShape(col.Box, col.RampRise), out RayHit _)
                    : Intersections.SegmentVsYawBox(a, b, col.Box, out RayHit _);
                if (hit)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>LOS blocked by one specific solid (by id) when tested alone.</summary>
        public bool SolidBlocksSegment(int solidId, Vec3 a, Vec3 b)
        {
            for (int i = 0; i < _colliders.Count; i++)
            {
                Collider col = _colliders[i];
                if (col.SolidId != solidId)
                {
                    continue;
                }

                return col.RampRise > 0f
                    ? Intersections.SegmentVsRamp(a, b, new RampShape(col.Box, col.RampRise), out RayHit _)
                    : Intersections.SegmentVsYawBox(a, b, col.Box, out RayHit _);
            }

            return false;
        }

        /// <summary>
        /// Sight-blocking footprints in a vertical band (absolute heights): each solid whose material spans the whole
        /// band at some point. Ramps contribute the part of their footprint where the surface reaches the band top.
        /// Up to two solids (a removal candidate and its image) can be left out.
        /// </summary>
        public List<YawBox> BandBlockers(float bandLow, float bandHigh, int excludeSolidA = -1, int excludeSolidB = -1)
        {
            var result = new List<YawBox>();
            for (int i = 0; i < _colliders.Count; i++)
            {
                Collider col = _colliders[i];
                bool excluded = col.SolidId >= 0 && (col.SolidId == excludeSolidA || col.SolidId == excludeSolidB);
                if (excluded || !col.BlocksSight || col.Box.Bottom > bandLow || col.Box.Top < bandHigh)
                {
                    continue;
                }

                if (col.RampRise <= 0f)
                {
                    result.Add(col.Box);
                    continue;
                }

                var ramp = new RampShape(col.Box, col.RampRise);
                float baseH = ramp.BaseHeight;
                float t = col.RampRise <= 0f ? 0f : CMath.Clamp01((bandHigh - baseH) / col.RampRise);
                float hz = col.Box.HalfExtents.Z;
                float z0 = -hz + 2f * hz * t;
                float len = hz - z0;
                if (len <= 0f)
                {
                    continue;
                }

                Vec3 localCenter = new Vec3(0f, 0f, (z0 + hz) * 0.5f);
                Vec3 worldCenter = col.Box.ToWorld(localCenter);
                result.Add(new YawBox(worldCenter, new Vec3(col.Box.HalfExtents.X, col.Box.HalfExtents.Y, len * 0.5f), col.Box.YawDeg));
            }

            return result;
        }
    }
}
