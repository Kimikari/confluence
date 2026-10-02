using System;
using System.Collections.Generic;
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

        /// <summary>Treat placed props as obstacles (their initial <see cref="PropPlacement.CollisionBox"/>).</summary>
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
    /// <para>
    /// Solids are seen at their true size, however thin: every edge is tested as a segment against the solids it
    /// passes (a 0.15 m wall between two column centers cuts the edge), and clearance to obstacles is the exact
    /// distance to their footprints, so routes, widths, and reach measured here are routes the Sim motor can walk.
    /// </para>
    /// On a half-turn symmetric layout the graph, clearances, nearest-node lookups, and search results are exact
    /// mirror images (<see cref="IsHalfTurnSymmetric"/>), so both spawns measure the same arena (spec L308).
    /// </summary>
    public sealed class ArenaNav
    {
        private const float SurfaceMergeEpsilon = 0.02f;
        private const float HeadroomFloor = 0.05f;
        private const float SupportTolerance = 0.45f;
        private const int CrossingBisections = 6;
        private const float MirrorHeightTolerance = 1e-3f;

        /// <summary>A collider and its layout image are treated as exact images when they agree this closely.</summary>
        private const float MirrorGeometryTolerance = 1e-4f;

        /// <summary>Colliders are binned into every column their footprint comes within this distance of.</summary>
        private const float BinPadding = 1e-3f;

        /// <summary>Margin on the per-column reach filter, so float rounding never skips a collider the full test keeps.</summary>
        private const float ReachSlack = 1e-4f;

        /// <summary>
        /// Obstacle distances are computed exactly for nodes within this radius of a footprint; farther nodes take
        /// them through propagation (an upper bound that stays close on open floor).
        /// </summary>
        private const float ExactClearanceRadius = 1.5f;

        /// <summary>A walkable surface this far above a layer that carries nodes of its own is a ledge, not a wall (D-024).</summary>
        private const float LedgeClimb = GlobalRules.Movement.MaxCommonJumpLedge + 0.01f;

        private static readonly int[] DirX = { 1, 0, -1, 0, 1, -1, -1, 1 };
        private static readonly int[] DirZ = { 0, 1, 0, -1, 1, 1, -1, -1 };

        private readonly float _cs;
        private readonly int _n;
        private readonly Vec2 _origin;
        private readonly List<Collider> _colliderList = new List<Collider>();
        private Collider[] _colliders;
        private int[] _cellStart;
        private int[] _cellItems;

        /// <summary>
        /// Per column, the highest feet height any binned collider can obstruct while walking (top minus its moving
        /// allowance); edges whose feet stay at or above it in both columns skip the segment test.
        /// </summary>
        private float[] _columnReach;

        private int[] _columnFirst;
        private float[] _nodeHeight;
        private float[] _nodeX;
        private float[] _nodeZ;
        private int[] _nodeColumn;
        private int[] _nodeSupport;

        /// <summary>Per node, bit d set when an edge leads into the neighbor column in direction d.</summary>
        private byte[] _edgeDirs;

        /// <summary>Per node, bit d set when the walk toward the neighbor column in direction d is cut by a solid.</summary>
        private byte[] _obstructedDirs;
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

        /// <summary>
        /// One collision volume. Box and Bounds are the layout's geometry (sight queries use them as given); the
        /// footprint fields (center, half extents, rotation, ramp direction) are what every walking predicate reads.
        /// For a solid or prop whose layout image was already added, the footprint is the exact negation of its
        /// partner's, so containment, segment, and distance tests on mirrored inputs give bit-identical answers.
        /// The fields every per-column scan reads come first, so a scan touches one cache line per collider.
        /// </summary>
        private struct Collider
        {
            public float Cx;
            public float Cz;
            public float Hx;
            public float Hz;
            public float Cos;
            public float Sin;
            public float Bottom;
            public float Top;
            public float RampRise;

            /// <summary>+1 when the ramp rises toward the footprint frame's +Z, -1 when toward -Z.</summary>
            public float RampSign;

            /// <summary>Ramp surface height at the low edge.</summary>
            public float BaseY;

            public bool Walkable;

            /// <summary>Walkable collider that carries at least one node (its edges are ledges, not walls).</summary>
            public bool SupportsNode;

            public bool BlocksSight;
            public int SolidId;

            public float MinX;
            public float MinZ;
            public float MaxX;
            public float MaxZ;

            public YawBox Box;
            public Aabb Bounds;

            public bool IsRamp => RampRise > 0f;
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
            var solidCollider = new Dictionary<int, int>();
            for (int i = 0; i < layout.Solids.Count; i++)
            {
                Solid s = layout.Solids[i];
                if (s == null || (options.IncludeSolid != null && !options.IncludeSolid(s)))
                {
                    continue;
                }

                int index = AddCollider(s.Box, s.RampRise, s.Walkable, s.BlocksSight, s.Id);
                if (s.MirrorId >= 0 && s.MirrorId != s.Id && solidCollider.TryGetValue(s.MirrorId, out int partner))
                {
                    AdoptMirrorFootprint(index, partner);
                }

                solidCollider[s.Id] = index;
            }

            if (options.PropsAsObstacles)
            {
                var propCollider = new Dictionary<int, int>();
                for (int i = 0; i < layout.Props.Count; i++)
                {
                    PropPlacement p = layout.Props[i];
                    if (p == null || !p.HasCollision)
                    {
                        continue;
                    }

                    // Props are obstacles and never supports or sight blockers here (D-068).
                    int index = AddCollider(p.CollisionBox, 0f, false, false, -1);
                    if (p.MirrorId >= 0 && p.MirrorId != p.Id && propCollider.TryGetValue(p.MirrorId, out int partner))
                    {
                        AdoptMirrorFootprint(index, partner);
                    }

                    propCollider[p.Id] = index;
                }
            }

            if (options.ExtraObstacles != null)
            {
                for (int i = 0; i < options.ExtraObstacles.Count; i++)
                {
                    AddCollider(options.ExtraObstacles[i], 0f, false, true, -1);
                }
            }

            _colliders = _colliderList.ToArray();
        }

        private int AddCollider(YawBox box, float rampRise, bool walkable, bool blocksSight, int solidId)
        {
            float rise = rampRise > 0f ? new RampShape(box, rampRise).Rise : 0f;
            CMath.SinCosDeg(box.YawDeg, out float sin, out float cos);
            var col = new Collider
            {
                Box = box,
                Bounds = box.Bounds(),
                RampRise = rise,
                Walkable = walkable,
                BlocksSight = blocksSight,
                SolidId = solidId,
                Cx = box.Center.X,
                Cz = box.Center.Z,
                Hx = box.HalfExtents.X,
                Hz = box.HalfExtents.Z,
                Cos = cos,
                Sin = sin,
                RampSign = 1f,
                Bottom = box.Bottom,
                Top = box.Top,
                BaseY = box.Top - rise,
            };
            SetFootprintBounds(ref col);
            _colliderList.Add(col);
            return _colliderList.Count - 1;
        }

        private static void SetFootprintBounds(ref Collider col)
        {
            float ex = MathF.Abs(col.Cos) * col.Hx + MathF.Abs(col.Sin) * col.Hz + BinPadding;
            float ez = MathF.Abs(col.Sin) * col.Hx + MathF.Abs(col.Cos) * col.Hz + BinPadding;
            col.MinX = col.Cx - ex;
            col.MaxX = col.Cx + ex;
            col.MinZ = col.Cz - ez;
            col.MaxZ = col.Cz + ez;
        }

        /// <summary>
        /// Rewrites an image collider's footprint as the exact half-turn image of its partner's (center negated, the
        /// same rotation, ramp direction reversed). A yaw a + 180 recomputes sine and cosine with rounding of its
        /// own, and a column center or edge lying on a face could then fall inside one solid and outside its image.
        /// Pairs that are not images within <see cref="MirrorGeometryTolerance"/> keep their own geometry.
        /// </summary>
        private void AdoptMirrorFootprint(int image, int source)
        {
            Collider a = _colliderList[source];
            Collider b = _colliderList[image];
            const float tol = MirrorGeometryTolerance;
            if (MathF.Abs(a.Cx + b.Cx) > tol || MathF.Abs(a.Cz + b.Cz) > tol || MathF.Abs(a.Hx - b.Hx) > tol || MathF.Abs(a.Hz - b.Hz) > tol
                || MathF.Abs(a.Bottom - b.Bottom) > tol || MathF.Abs(a.Top - b.Top) > tol || MathF.Abs(a.RampRise - b.RampRise) > tol
                || a.Walkable != b.Walkable)
            {
                return;
            }

            float frame;
            if (MathF.Abs(a.Cos - b.Cos) <= tol && MathF.Abs(a.Sin - b.Sin) <= tol)
            {
                frame = 1f;
            }
            else if (MathF.Abs(a.Cos + b.Cos) <= tol && MathF.Abs(a.Sin + b.Sin) <= tol)
            {
                frame = -1f;
            }
            else
            {
                return;
            }

            if (b.IsRamp && frame * a.RampSign > 0f)
            {
                return; // a ramp image rises toward the mirrored direction; this pair does not
            }

            b.Cx = -a.Cx;
            b.Cz = -a.Cz;
            b.Hx = a.Hx;
            b.Hz = a.Hz;
            b.Cos = a.Cos;
            b.Sin = a.Sin;
            b.RampSign = -a.RampSign;
            b.Bottom = a.Bottom;
            b.Top = a.Top;
            b.RampRise = a.RampRise;
            b.BaseY = a.BaseY;
            SetFootprintBounds(ref b);
            _colliderList[image] = b;
        }

        private void BinColliders()
        {
            int columns = _n * _n;
            var counts = new int[columns + 1];
            _columnReach = new float[columns];
            for (int c = 0; c < columns; c++)
            {
                _columnReach[c] = float.NegativeInfinity;
            }

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

                for (int ci = 0; ci < _colliders.Length; ci++)
                {
                    ref Collider col = ref _colliders[ci];
                    float reach = col.Top - MovingAllowance(ref col);
                    ColumnRange(col.MinX, col.MinZ, col.MaxX, col.MaxZ, out int i0, out int j0, out int i1, out int j1);
                    for (int j = j0; j <= j1; j++)
                    {
                        for (int i = i0; i <= i1; i++)
                        {
                            int c = j * _n + i;
                            if (pass == 0)
                            {
                                counts[c]++;
                                _columnReach[c] = MathF.Max(_columnReach[c], reach);
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

        // ---- Footprint geometry ------------------------------------------------------------------
        // Every expression below is odd under negating both the point and the footprint center, so a footprint and
        // its adopted image answer mirrored questions identically (IEEE rounding is symmetric about zero).

        private static void ToLocal(ref Collider col, float x, float z, out float lx, out float lz)
        {
            float dx = x - col.Cx;
            float dz = z - col.Cz;
            lx = dx * col.Cos - dz * col.Sin;
            lz = dx * col.Sin + dz * col.Cos;
        }

        private static bool ContainsLocal(ref Collider col, float lx, float lz) => MathF.Abs(lx) <= col.Hx && MathF.Abs(lz) <= col.Hz;

        /// <summary>Top of the collider's solid at a local footprint point (the walkable surface for ramps).</summary>
        private static float TopAtLocal(ref Collider col, float lz)
        {
            if (col.RampRise <= 0f || col.Hz <= 0f)
            {
                return col.Top;
            }

            float t = CMath.Clamp01((col.RampSign * lz + col.Hz) / (2f * col.Hz));
            return col.BaseY + col.RampRise * t;
        }

        /// <summary>
        /// Height above a layer at which a collider starts to obstruct a capsule standing on that layer: anything
        /// rising past the 0.05 m floor tolerance for plain obstacles, past the 0.35 m step for walkable surfaces the
        /// motor steps onto, and past the 1 m common jump for walkable surfaces that carry nodes (ledges, D-024).
        /// </summary>
        private static float StandingAllowance(ref Collider col)
        {
            if (!col.Walkable)
            {
                return HeadroomFloor;
            }

            return col.SupportsNode ? LedgeClimb : GlobalRules.Movement.StepHeight;
        }

        /// <summary>Allowance for a moving capsule: plain obstacles block above the floor tolerance, surfaces above a step.</summary>
        private static float MovingAllowance(ref Collider col) => col.Walkable ? GlobalRules.Movement.StepHeight : HeadroomFloor;

        // ---- Nodes ---------------------------------------------------------------------------------

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
                    ref Collider col = ref _colliders[ci];
                    if (!col.Walkable)
                    {
                        continue;
                    }

                    ToLocal(ref col, p.X, p.Y, out float lx, out float lz);
                    if (ContainsLocal(ref col, lx, lz))
                    {
                        candidates.Add(new KeyValuePair<float, int>(TopAtLocal(ref col, lz), ci));
                    }
                }

                if (candidates.Count == 0)
                {
                    continue;
                }

                candidates.Sort((a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.Value.CompareTo(b.Value));
                float last = float.NegativeInfinity;
                bool lastIsNode = false;
                for (int k = candidates.Count - 1; k >= 0; k--)
                {
                    // Keep the highest of nearly coincident surfaces; walk downward so dedupe keeps the top one.
                    float h = candidates[k].Key;
                    if (MathF.Abs(h - last) < SurfaceMergeEpsilon)
                    {
                        if (lastIsNode)
                        {
                            _colliders[candidates[k].Value].SupportsNode = true;
                        }

                        continue;
                    }

                    last = h;
                    lastIsNode = HasHeadroom(c, p, h, ArenaRules.Headroom);
                    if (lastIsNode)
                    {
                        heights.Add(h);
                        supports.Add(candidates[k].Value);
                        columnOf.Add(c);
                        _colliders[candidates[k].Value].SupportsNode = true;
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
            _nodeX = new float[_nodeHeight.Length];
            _nodeZ = new float[_nodeHeight.Length];
            for (int a = 0; a < _nodeHeight.Length; a++)
            {
                Vec2 p = ColumnCenter(_nodeColumn[a]);
                _nodeX[a] = p.X;
                _nodeZ[a] = p.Y;
            }
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

        private bool HasHeadroom(int column, Vec2 p, float h, float headroom) => IntervalClear(column, p, h + HeadroomFloor, h + headroom);

        /// <summary>True when no collider occupies the vertical interval (lo, hi) above a ground point of a column.</summary>
        private bool IntervalClear(int column, Vec2 p, float lo, float hi)
        {
            if (hi <= lo)
            {
                return true;
            }

            for (int k = _cellStart[column]; k < _cellStart[column + 1]; k++)
            {
                ref Collider col = ref _colliders[_cellItems[k]];
                if (col.Top <= lo || col.Bottom >= hi)
                {
                    continue;
                }

                ToLocal(ref col, p.X, p.Y, out float lx, out float lz);
                if (ContainsLocal(ref col, lx, lz) && TopAtLocal(ref col, lz) > lo)
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

        // ---- Edges ---------------------------------------------------------------------------------

        private void BuildEdges()
        {
            int nodeCount = _nodeHeight.Length;
            var start = new int[nodeCount + 1];
            var to = new List<int>(nodeCount * 8);
            var cost = new List<float>(nodeCount * 8);
            var kind = new List<NavEdgeKind>(nodeCount * 8);
            _obstructedDirs = new byte[nodeCount];
            _edgeDirs = new byte[nodeCount];
            float diag = _cs * MathF.Sqrt(2f);
            for (int a = 0; a < nodeCount; a++)
            {
                start[a] = to.Count;
                int c = _nodeColumn[a];
                int ci = c % _n;
                int cj = c / _n;
                float h1 = _nodeHeight[a];
                Vec2 pa = ColumnCenter(c);
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
                    Vec2 pb = ColumnCenter(nc);
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

                        if (SegmentObstructed(c, nc, -1, pa, pb, h1, _nodeHeight[walk]))
                        {
                            _obstructedDirs[a] |= (byte)(1 << dir);
                            continue;
                        }

                        to.Add(walk);
                        cost.Add(d);
                        kind.Add(NavEdgeKind.Walk);
                        _edgeDirs[a] |= (byte)(1 << dir);
                        continue;
                    }

                    if (diagonal)
                    {
                        continue;
                    }

                    // No walkable continuation: consider a ledge climb or a safe drop into the neighbor column. The
                    // capsule crosses at the upper height: rising before a climb, stepping off before a drop.
                    int jump = -1;
                    int drop = -1;
                    for (int m = _columnFirst[nc]; m < _columnFirst[nc + 1]; m++)
                    {
                        float dh = _nodeHeight[m] - h1;
                        if (dh > 0f && dh <= GlobalRules.Movement.MaxCommonJumpLedge + 0.01f)
                        {
                            if (jump < 0 && IntervalClear(c, pa, h1 + ArenaRules.Headroom, _nodeHeight[m] + ArenaRules.Headroom)
                                && !SegmentObstructed(c, nc, -1, pa, pb, _nodeHeight[m], _nodeHeight[m]))
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

                    if (drop >= 0 && SegmentObstructed(c, nc, -1, pa, pb, h1, h1))
                    {
                        drop = -1;
                    }

                    if (jump >= 0)
                    {
                        to.Add(jump);
                        cost.Add(d + ArenaRules.JumpEdgePenalty);
                        kind.Add(NavEdgeKind.Jump);
                        _edgeDirs[a] |= (byte)(1 << dir);
                    }

                    if (drop >= 0)
                    {
                        to.Add(drop);
                        cost.Add(d + ArenaRules.DropEdgePenalty);
                        kind.Add(NavEdgeKind.Drop);
                        _edgeDirs[a] |= (byte)(1 << dir);

                        // A falling capsule carries forward: also land one column farther out, clear of the wall the
                        // ledge makes for the floor right below it.
                        int fi = ni + DirX[dir];
                        int fj = nj + DirZ[dir];
                        if (fi >= 0 && fj >= 0 && fi < _n && fj < _n)
                        {
                            int nc2 = fj * _n + fi;
                            Vec2 pc = ColumnCenter(nc2);
                            int far = WalkNodeIn(nc2, _nodeHeight[drop], _cs);
                            if (far >= 0 && IntervalClear(nc2, pc, _nodeHeight[far] + ArenaRules.Headroom, h1 + ArenaRules.Headroom)
                                && !SegmentObstructed(c, nc, nc2, pa, pc, h1, h1)
                                && !SegmentObstructed(nc, nc2, -1, pb, pc, _nodeHeight[far], _nodeHeight[far]))
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

        /// <summary>
        /// True when a capsule moving along the ground segment p to q, its feet rising linearly from f0 to f1, runs into
        /// a collider: some point of the segment lies in a footprint whose solid reaches above the feet by more than
        /// the collider's allowance while its underside is below head height. Colliders are taken from the listed
        /// columns, whose cells contain the segment (binning is conservative).
        /// </summary>
        private bool SegmentObstructed(int c0, int c1, int c2, Vec2 p, Vec2 q, float f0, float f1)
        {
            return ObstructedIn(c0, p, q, f0, f1)
                || (c1 >= 0 && c1 != c0 && ObstructedIn(c1, p, q, f0, f1))
                || (c2 >= 0 && c2 != c0 && c2 != c1 && ObstructedIn(c2, p, q, f0, f1));
        }

        private bool ObstructedIn(int column, Vec2 p, Vec2 q, float f0, float f1)
        {
            float low = MathF.Min(f0, f1);
            if (_columnReach[column] + ReachSlack <= low)
            {
                return false; // nothing binned here rises past its allowance above these feet (open floor)
            }

            float high = MathF.Max(f0, f1);
            for (int k = _cellStart[column]; k < _cellStart[column + 1]; k++)
            {
                ref Collider col = ref _colliders[_cellItems[k]];
                float allowance = MovingAllowance(ref col);
                if (col.Top <= low + allowance || col.Bottom >= high + ArenaRules.Headroom)
                {
                    continue;
                }

                if (SegmentHits(ref col, p, q, f0, f1, allowance))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SegmentHits(ref Collider col, Vec2 p, Vec2 q, float f0, float f1, float allowance)
        {
            ToLocal(ref col, p.X, p.Y, out float ox, out float oz);
            ToLocal(ref col, q.X, q.Y, out float qx, out float qz);
            float dx = qx - ox;
            float dz = qz - oz;
            float t0 = 0f;
            float t1 = 1f;
            if (!ClipSlab(ox, dx, col.Hx, ref t0, ref t1) || !ClipSlab(oz, dz, col.Hz, ref t0, ref t1))
            {
                return false;
            }

            // Along the overlap, the solid's top above the feet and the head's height above its underside are both
            // linear in t, so the overlap is obstructed iff both are positive somewhere on it.
            float fa = f0 + (f1 - f0) * t0;
            float fb = f0 + (f1 - f0) * t1;
            float ga = TopAtLocal(ref col, oz + dz * t0) - fa - allowance;
            float gb = TopAtLocal(ref col, oz + dz * t1) - fb - allowance;
            float ceiling = col.Bottom - ArenaRules.Headroom;
            return BothPositiveSomewhere(ga, gb, fa - ceiling, fb - ceiling);
        }

        /// <summary>Clips [t0, t1] to the parameters where o + d t lies within [-h, h]; false when nothing remains.</summary>
        private static bool ClipSlab(float o, float d, float h, ref float t0, ref float t1)
        {
            if (MathF.Abs(d) < 1e-9f)
            {
                return MathF.Abs(o) <= h;
            }

            float ta = (-h - o) / d;
            float tb = (h - o) / d;
            t0 = MathF.Max(t0, MathF.Min(ta, tb));
            t1 = MathF.Min(t1, MathF.Max(ta, tb));
            return t0 <= t1;
        }

        /// <summary>True if two linear functions on [0, 1] (given by their end values) are positive at a common point.</summary>
        private static bool BothPositiveSomewhere(float a0, float a1, float b0, float b1)
        {
            float lo = 0f;
            float hi = 1f;
            return PositivePart(a0, a1, ref lo, ref hi) && PositivePart(b0, b1, ref lo, ref hi) && (lo < hi || (a0 > 0f && b0 > 0f) || (a1 > 0f && b1 > 0f));
        }

        private static bool PositivePart(float v0, float v1, ref float lo, ref float hi)
        {
            if (v0 > 0f && v1 > 0f)
            {
                return true;
            }

            if (v0 <= 0f && v1 <= 0f)
            {
                return false;
            }

            float s = v0 / (v0 - v1);
            if (v0 > 0f)
            {
                hi = MathF.Min(hi, s);
            }
            else
            {
                lo = MathF.Max(lo, s);
            }

            return lo <= hi;
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

        /// <summary>
        /// Clearance of every node: the distance to the nearest obstacle footprint (exact within
        /// <see cref="ExactClearanceRadius"/>) or surface edge (where the node's surface ends with no walk, ledge, or
        /// drop beyond), propagated outward over walk edges as a distance to the nearest known boundary point.
        /// Every value is a distance to a real boundary point, so it never underestimates by more than the edge
        /// bisection step.
        /// </summary>
        private void ComputeClearance()
        {
            int nodeCount = _nodeHeight.Length;
            _clearance = new float[nodeCount];
            var srcX = new float[nodeCount];
            var srcZ = new float[nodeCount];
            for (int a = 0; a < nodeCount; a++)
            {
                _clearance[a] = float.PositiveInfinity;
            }

            // Squared distances until the end: the order is the same and the propagation takes no square roots.
            SeedObstacleDistances(srcX, srcZ);
            for (int a = 0; a < nodeCount; a++)
            {
                // Directions cut by an obstacle are measured exactly above; the rest end at a surface edge.
                int open = _obstructedDirs[a] | PassableDirs(a);
                if (open == 0xFF)
                {
                    continue;
                }

                int c = _nodeColumn[a];
                int ci = c % _n;
                int cj = c / _n;
                Vec2 p = new Vec2(_nodeX[a], _nodeZ[a]);
                for (int dir = 0; dir < 8; dir++)
                {
                    if ((open & (1 << dir)) != 0)
                    {
                        continue;
                    }

                    Vec2 target = new Vec2(p.X + DirX[dir] * _cs, p.Y + DirZ[dir] * _cs);
                    Vec2 crossing = EdgeCrossing(a, p, target, ci + DirX[dir], cj + DirZ[dir]);
                    float d2 = Vec2.DistanceSquared(p, crossing);
                    if (d2 < _clearance[a])
                    {
                        _clearance[a] = d2;
                        srcX[a] = crossing.X;
                        srcZ[a] = crossing.Y;
                    }
                }
            }

            var heap = new NodeHeap(nodeCount, _rank);
            for (int a = 0; a < nodeCount; a++)
            {
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

                float sx = srcX[a];
                float sz = srcZ[a];
                for (int e = _edgeStart[a]; e < _edgeStart[a + 1]; e++)
                {
                    if (_edgeKind[e] != NavEdgeKind.Walk)
                    {
                        continue;
                    }

                    int b = _edgeTo[e];
                    float dx = _nodeX[b] - sx;
                    float dz = _nodeZ[b] - sz;
                    float candidate = dx * dx + dz * dz;
                    if (candidate < _clearance[b])
                    {
                        _clearance[b] = candidate;
                        srcX[b] = sx;
                        srcZ[b] = sz;
                        heap.Push(candidate, b);
                    }
                }
            }

            for (int a = 0; a < nodeCount; a++)
            {
                _clearance[a] = MathF.Sqrt(_clearance[a]);
            }
        }

        /// <summary>
        /// Exact horizontal distance from each node to the part of every nearby footprint that obstructs a capsule
        /// standing on the node's layer (see <see cref="StandingAllowance"/>; for ramps, the part of the footprint
        /// where the surface rises past the allowance). Walks each collider's footprint grown by the exact radius.
        /// </summary>
        private void SeedObstacleDistances(float[] srcX, float[] srcZ)
        {
            if (_nodeHeight.Length == 0)
            {
                return;
            }

            float minH = float.PositiveInfinity;
            float maxH = float.NegativeInfinity;
            for (int a = 0; a < _nodeHeight.Length; a++)
            {
                minH = MathF.Min(minH, _nodeHeight[a]);
                maxH = MathF.Max(maxH, _nodeHeight[a]);
            }

            const float r = ExactClearanceRadius;
            for (int ci = 0; ci < _colliders.Length; ci++)
            {
                ref Collider col = ref _colliders[ci];
                float allowance = StandingAllowance(ref col);

                // Layers h it can obstruct: Top > h + allowance and Bottom < h + headroom.
                float layerLow = col.Bottom - ArenaRules.Headroom;
                float layerHigh = col.Top - allowance;
                if (layerHigh <= minH || layerLow >= maxH || layerHigh <= layerLow)
                {
                    continue;
                }

                ColumnRange(col.MinX - r, col.MinZ - r, col.MaxX + r, col.MaxZ + r, out int i0, out int j0, out int i1, out int j1);
                for (int j = j0; j <= j1; j++)
                {
                    for (int i = i0; i <= i1; i++)
                    {
                        int c = j * _n + i;
                        int first = _columnFirst[c];
                        int end = _columnFirst[c + 1];
                        if (first == end)
                        {
                            continue;
                        }

                        Vec2 p = ColumnCenter(c);
                        ToLocal(ref col, p.X, p.Y, out float lx, out float lz);
                        float dx = MathF.Max(MathF.Abs(lx) - col.Hx, 0f);
                        if (dx >= r)
                        {
                            continue;
                        }

                        for (int m = first; m < end; m++)
                        {
                            float h = _nodeHeight[m];
                            if (h <= layerLow || h >= layerHigh)
                            {
                                continue;
                            }

                            ObstructedSpan(ref col, h + allowance, out float zLo, out float zHi);
                            float dz = MathF.Max(MathF.Max(zLo - lz, lz - zHi), 0f);
                            float d2 = dx * dx + dz * dz;
                            if (d2 < r * r && d2 < _clearance[m])
                            {
                                // Nearest obstructing point, back in world space (the source for propagation).
                                float qx = CMath.Clamp(lx, -col.Hx, col.Hx);
                                float qz = CMath.Clamp(lz, zLo, zHi);
                                _clearance[m] = d2;
                                srcX[m] = col.Cx + qx * col.Cos + qz * col.Sin;
                                srcZ[m] = col.Cz - qx * col.Sin + qz * col.Cos;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Local Z range of the footprint where the solid reaches above a height: all of it for plain boxes, the
        /// stretch past the height for ramps (the surface is linear along the rise). Called only when the top does.
        /// </summary>
        private static void ObstructedSpan(ref Collider col, float above, out float zLo, out float zHi)
        {
            zLo = -col.Hz;
            zHi = col.Hz;
            if (col.RampRise <= 0f || col.Hz <= 0f)
            {
                return;
            }

            float own = (above - col.BaseY) / col.RampRise * (2f * col.Hz) - col.Hz;
            if (own <= -col.Hz)
            {
                return;
            }

            if (col.RampSign > 0f)
            {
                zLo = MathF.Min(own, col.Hz);
            }
            else
            {
                zHi = -MathF.Min(own, col.Hz);
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
        /// The propagated clearance is the distance to a real boundary point, so it never underestimates; on a
        /// symmetric graph each node and its image measure the same geometry, and the smaller of the two estimates
        /// is the better one for both. Without this, propagation order left mirrored cells apart in clearance (and
        /// so in capsule and route admissibility).
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
        /// Directions (bit d) in which the node connects toward the neighbor column by any movement: walking, a
        /// common-jump ledge, or a safe drop; a diagonal also counts when both of its orthogonal steps do. Clearance
        /// measures distance to real boundaries only, so a capsule beside a jumpable ledge or at the lip of a safe
        /// drop still counts as standing room.
        /// </summary>
        private int PassableDirs(int node)
        {
            int bits = _edgeDirs[node];
            int px = bits & 1;          // +X is direction 0
            int pz = (bits >> 1) & 1;   // +Z is direction 1
            int nx = (bits >> 2) & 1;   // -X is direction 2
            int nz = (bits >> 3) & 1;   // -Z is direction 3
            int diagonals = ((px & pz) << 4) | ((nx & pz) << 5) | ((nx & nz) << 6) | ((px & nz) << 7);
            return bits | diagonals;
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
                // The far column is standable at the center but not walk-connected (a step or slope too steep): the
                // break lies between the two centers.
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
                ref Collider col = ref _colliders[_cellItems[k]];
                if (!col.Walkable)
                {
                    continue;
                }

                ToLocal(ref col, p.X, p.Y, out float lx, out float lz);
                if (!ContainsLocal(ref col, lx, lz))
                {
                    continue;
                }

                float s = TopAtLocal(ref col, lz);
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

        public Vec3 NodePosition(int node) => new Vec3(_nodeX[node], _nodeHeight[node], _nodeZ[node]);

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
            for (int i = 0; i < _colliders.Length; i++)
            {
                ref Collider col = ref _colliders[i];
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
            for (int i = 0; i < _colliders.Length; i++)
            {
                ref Collider col = ref _colliders[i];
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
            for (int i = 0; i < _colliders.Length; i++)
            {
                ref Collider col = ref _colliders[i];
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
