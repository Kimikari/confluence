using System;
using System.Collections.Generic;
using Confluence.Arena.Biomes;
using Confluence.Arena.Modules;
using Confluence.Arena.Navigation;
using Confluence.Arena.Props;
using Confluence.Arena.Validation;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Random;
using Confluence.Core.Rules;

namespace Confluence.Arena.Generation
{
    /// <summary>
    /// Builds one arena attempt from a recorded seed (pipeline steps 2-5, spec L317-L320). The route skeleton is a
    /// mirrored set of open corridors that no module may occupy: each spawn bastion's channel and its two side exits,
    /// the side approach corridors running from the exits to the firing lane, the crossover strip in front of each
    /// bastion, and the firing lane along the X axis through the central final floor. Catalogue modules then fill
    /// the remaining floor as islands whose footprints keep at least <see cref="IslandGap"/> from each other, so every
    /// module socket opens onto connected floor at its own level (module sockets text: "attach only matching level").
    /// Everything is authored on side 0 (z &lt; 0) and written with its half-turn image.
    /// </summary>
    internal sealed class ArenaAssembler
    {
        /// <summary>Minimum gap between island footprints: a capsule passage, never a sealed seam.</summary>
        public const float IslandGap = 1.5f;

        /// <summary>Half width of the firing lane zone along the X axis (a 3.5 m route).</summary>
        public const float LaneZoneHalf = 1.75f;

        /// <summary>Width of the guaranteed approach corridors and bastion exits (2.5 m routes plus quantization).</summary>
        public const float CorridorWidth = 3.0f;

        /// <summary>Width of the bastion exits and crossover kept clear beside and in front of the screen (a 2.5 m route plus margin).</summary>
        public const float ExitWidth = 2.75f;

        /// <summary>Clear space kept around the center module footprint.</summary>
        public const float CenterGap = 1.0f;

        private const int PlacementTries = 160;
        private const int BastionTuningPasses = 4;
        private const int FillerPasses = 12;

        private const int AccretionTries = 400;

        private const int ContractionRepairs = 8;

        /// <summary>Only an obstacle this close to a stranded cell is removed to repair it.</summary>
        private const float ContractionRepairReach = 4f;

        /// <summary>Center of the bulwark walls at most this far out: their inner faces keep lanes through the center under 28 m.</summary>
        private const float BulwarkRadius = 14.5f;

        private const float BulwarkMinRadius = 8f;
        private const float BulwarkThickness = 1f;
        private const float BulwarkPieceLength = 2.4f;

        /// <summary>Opening between inner-row bulwark pieces (a capsule passage with margin).</summary>
        private const float BulwarkGate = 1.6f;

        /// <summary>Radial spacing between the bulwark rows (center to center).</summary>
        private const float BulwarkRowSpacing = 2.75f;

        /// <summary>Lines through a pivot are closed when longer than this (a margin under the 28 m lane limit).</summary>
        private const float BulwarkLaneTarget = 27f;

        /// <summary>Each bulwark arc extends this far past its open rays, so slightly off-center lanes meet it too.</summary>
        private const float BulwarkPaddingDeg = 2f;

        /// <summary>Free space around a movement-blocking prop: a capsule passage the navigation grid always resolves.</summary>
        private const float PropPassage = 1.3f;

        /// <summary>Spur geometry for lane breaking: thin, reaching at most 3 m from an obstacle, ending 0.4 m past the lane.</summary>
        private const float SpurThickness = 0.6f;

        private const float SpurMaxReach = 3f;
        private const float SpurOvershoot = 0.4f;
        private const float SpurEmbed = 0.2f;

        private static readonly bool UseCorridors = System.Environment.GetEnvironmentVariable("ARENA_CORRIDORS") == "1";

        /// <summary>Half length of the rock closing each end of the designated lane (it also closes the strips beside it).</summary>
        private const float LaneEndHalfLength = 2.5f;

        /// <summary>Radial thickness of each lane-end block; it also closes the sightlines past the center module.</summary>
        private const float LaneEndThickness = 3f;

        /// <summary>Inner end of the lane-end nose: just outside the 10 m center module.</summary>
        private const float LaneNoseInnerX = 5.25f;

        /// <summary>Smallest lateral half extent of the bastion screen beyond the 24 m arena.</summary>
        private const float WingTipMinExtent = 5.5f;


        private static readonly ArenaNavOptions NoProps = new ArenaNavOptions { PropsAsObstacles = false };

        /// <summary>
        /// Free space a filler rock keeps to every other solid: a capsule passage (0.8 m) with margin, so rocks never
        /// seal floor. The skeleton routes are keep-out zones, so the 2.5 m routes (spec L312) are untouched.
        /// </summary>
        private const float FillerGap = 1.25f;

        /// <summary>Free space a rock inside a skeleton zone keeps around itself: a full 2.5 m route plus quantization.</summary>
        private const float RouteGap = 2.75f;

        private const float FillerMinHalfDiagonal = 0.6f;
        private const float FillerMaxHalfDiagonal = 3.0f;

        private readonly ArenaContent _content;
        private readonly SizeProfile _p;
        private readonly BiomeDef _biome;
        private readonly IContentAvailability _propFilter;
        private readonly bool _conservative;
        private readonly Rng _rng;
        private readonly ArenaLayout _layout;
        private readonly LayoutWriter _w;

        private readonly List<Rect2> _keepOut = new List<Rect2>();
        private readonly List<Rect2> _occupied = new List<Rect2>();
        private readonly List<Vec3> _sites = new List<Vec3>(); // placed features and props (side 0), x/z plus radius in Y
        private SpawnBastion _bastion;
        private float _wingMax;
        private float _wingMin;
        private ModuleGeometry _center;
        private int _sourceBudget = GlobalRules.Arena.MaxNaturalSourcePatches;
        private readonly HashSet<int> _attached = new HashSet<int>();
        private readonly HashSet<int> _removable = new HashSet<int>();
        private readonly HashSet<int> _bulwarks = new HashSet<int>();
        private string _why;
        private readonly HashSet<int> _hasAttachment = new HashSet<int>();

        public ArenaAssembler(ArenaContent content, ulong seed, int diameter, BiomeDef biome, IContentAvailability props, bool conservative)
        {
            _content = content;
            _p = SizeProfile.For(diameter);
            _biome = biome;
            _propFilter = props ?? ContentAvailability.All;
            _conservative = conservative;
            _rng = new Rng(seed);
            _layout = new ArenaLayout
            {
                Seed = seed,
                Diameter = diameter,
                BiomeId = biome?.Id,
                EnvironmentRule = biome?.EnvironmentRule,
                Center = Vec3.Zero,
            };
            _w = new LayoutWriter(_layout);
        }

        /// <summary>Names of the skeleton stages that failed to place everything they wanted (diagnostics only).</summary>
        public List<string> Notes { get; } = new List<string>();

        public ArenaLayout Build()
        {
            _layout.Boundary = BoundaryBuilder.Build(_rng, _p);

            AddFloor();
            PlaceCenter();
            PlaceBastions();

            // Reach and spawn path run through the bastions, crossover strips, and center, which stay keep-out zones
            // from here on, so tuning on the bare skeleton fixes them for the finished arena.
            TuneBastions();
            AddSkeletonKeepOuts();
            PlaceLaneEnds();
            PlaceBulwarks();
            ReserveBiomeSources();
            PlaceIslands();
            PlaceOutcrops(_p.OutcropPairs);

            ArenaNav nav = ArenaNav.Build(_layout, NoProps);
            nav = FillTerrain(nav);

            DressBiome(nav);
            PlaceProps(nav);
            RepairContraction();
            AddLighting();
            AddGusts();
            return _layout;
        }

        // ---- Floor, center, bastions ----------------------------------------------------------------------

        private void AddFloor()
        {
            float half = _layout.Boundary.MaxRadiusAt(0f) + ArenaRules.FloorMarginBeyondContour;
            var box = new YawBox(new Vec3(0f, -ArenaRules.FloorSlabThickness * 0.5f, 0f), new Vec3(half, ArenaRules.FloorSlabThickness * 0.5f, half), 0f);
            _w.AddSelfMirrored(_w.NewSolid(SolidKind.Floor, box, ArenaMaterials.Floor, 0f, walkable: true));
        }

        private void PlaceCenter()
        {
            IReadOnlyList<ModuleGeometry> centers = _content.ModulesOf(ModuleCategory.Center);
            var weights = new List<float>(centers.Count);
            for (int i = 0; i < centers.Count; i++)
            {
                weights.Add(ArenaContent.ModuleWeight(centers[i], _content.Catalogue.Module(centers[i].ModuleId), _biome));
            }

            _center = centers[ArenaContent.WeightedPick(_rng, weights)];

            // Keep the designated firing lane along X open through the center: pick a yaw whose pieces leave the
            // chest-to-eye band clear on the lane line (spec L308 "one longer firing lane").
            var turns = new List<int>();
            for (int q = 0; q < 2; q++)
            {
                if (LaneClear(_center, q))
                {
                    turns.Add(q);
                }
            }

            int quarter = turns.Count == 0 ? 0 : turns[_rng.NextInt(turns.Count)];
            _w.PlaceCenterModule(_center, quarter);
            Rect2 footprint = LayoutWriter.Footprint(_center, Vec2.Zero, quarter);
            _keepOut.Add(footprint.Expanded(CenterGap));

            for (int i = 0; i < _center.Patches.Count; i++)
            {
                if (_center.Patches[i].NaturalSource)
                {
                    _sourceBudget--;
                }
            }

            if (!HasCentralCover(_center))
            {
                PlaceCentralSoftCover();
            }
        }

        /// <summary>True when the center module itself offers cover near the final floor (spec L310 "useful central cover").</summary>
        private static bool HasCentralCover(ModuleGeometry g)
        {
            for (int i = 0; i < g.Pieces.Count; i++)
            {
                ModulePiece p = g.Pieces[i];
                if (!p.Walkable && p.BlocksSight && p.Box.Top - p.Box.Bottom >= ArenaRules.CoverMinHeight
                    && ArenaSurvey.FootprintDistance(p.Box, Vec2.Zero) <= ArenaRules.CentralCoverRadius - 0.5f)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Center modules whose geometry is all floor (beds, strips, gutters) receive a mirrored pair of low cover
        /// modules on the final floor edge, as map-owned soft cover that retires at 85 s (spec L678). Their pieces
        /// sit on the spawn axis, clear of the designated lane and of the center's patches.
        /// </summary>
        private void PlaceCentralSoftCover()
        {
            string[] lowCover = { "MOD-013", "MOD-024" };
            ModuleGeometry g = _content.Modules.Get(lowCover[_rng.NextInt(lowCover.Length)]);
            int firstSolid = _layout.Solids.Count;
            _w.PlaceModulePair(g, new Vec2(0f, -3.0f), 0);
            for (int i = firstSolid; i < _layout.Solids.Count; i++)
            {
                _layout.Solids[i].RetiresAtFinal = true;
            }
        }

        private static bool LaneClear(ModuleGeometry g, int quarterTurns)
        {
            List<YawBox> boxes = LayoutWriter.PieceBoxes(g, Vec2.Zero, quarterTurns);
            for (int i = 0; i < boxes.Count; i++)
            {
                YawBox b = boxes[i];
                if (g.Pieces[i].Walkable || !g.Pieces[i].BlocksSight || b.Top < ArenaRules.LaneBandLow || b.Bottom > ArenaRules.LaneBandHigh)
                {
                    continue;
                }

                if (LaneMeter.LineVsBox(new Vec2(0f, -0.5f), new Vec2(1f, 0f), b, out float _, out float _)
                    || LaneMeter.LineVsBox(new Vec2(0f, 0f), new Vec2(1f, 0f), b, out float _, out float _)
                    || LaneMeter.LineVsBox(new Vec2(0f, 0.5f), new Vec2(1f, 0f), b, out float _, out float _))
                {
                    return false;
                }
            }

            return true;
        }

        private void PlaceBastions()
        {
            float offset = _p.BastionOffset * (_rng.NextBool() ? 1f : -1f);
            _bastion = new SpawnBastion(_w, _p.SpawnDistance, offset, SpawnBastion.WingLengthFor(_p.FrontWallHalfWidth));
            _wingMax = SpawnBastion.WingLengthFor(_p.FrontWallHalfWidth + 4.0f);

            // Beyond the smallest arena the wing tips reach past the center module (half width 5 m); with the lane-end
            // noses, every straight line parallel to the spawn axis meets a screen or a block (spec L310 lane limit).
            _wingMin = _p.Diameter > GlobalRules.Arena.DiameterSmall ? SpawnBastion.WingLengthFor(WingTipMinExtent) : 0.75f;
            _bastion.Build(_layout);
            if (_bastion.WingLength < _wingMin)
            {
                _bastion.SetWingLength(_layout, _wingMin);
            }
        }

        /// <summary>Side approach corridor east (sign 1) or west (sign -1) of the bastion, running to the firing lane.</summary>
        private Rect2 Corridor(int sign)
        {
            float inner = _bastion.LateralExtent + 0.25f;
            float outer = inner + CorridorWidth;
            float x0 = _bastion.OffsetX;
            return sign > 0 ? new Rect2(x0 + inner, _bastion.SpawnZ, x0 + outer, LaneZoneHalf) : new Rect2(x0 - outer, _bastion.SpawnZ, x0 - inner, LaneZoneHalf);
        }

        private void AddSkeletonKeepOuts()
        {
            // Bastion, its exits, and the crossover strip in front of the screen.
            foreach (Rect2 zone in _bastion.Zones(ExitWidth))
            {
                AddKeepOut(zone);
            }
            if (UseCorridors)
            {
                AddKeepOut(Corridor(1));
                AddKeepOut(Corridor(-1));
            }

            // Firing lane zone along X (both halves of it are in the side-0 list after mirroring).
            float laneHalf = _p.LaneHalfLength > 0f ? _p.LaneHalfLength + LaneEndThickness : _p.Radius + 2f;
            AddKeepOut(new Rect2(-laneHalf, -LaneZoneHalf, laneHalf, LaneZoneHalf));
        }

        private void AddKeepOut(Rect2 r)
        {
            _keepOut.Add(r);
            _keepOut.Add(r.Mirrored());
        }

        // ---- Islands ----------------------------------------------------------------------------------------

        private void ReserveBiomeSources()
        {
            if (_biome != null && BiomeFeatures.TryGet(_biome.Id, out BiomeFeature f))
            {
                _sourceBudget -= f.SourcePatches;
            }
        }

        private void PlaceIslands()
        {
            ModuleCategory[] order = { ModuleCategory.Topology, ModuleCategory.Elevation, ModuleCategory.Approach, ModuleCategory.Material, ModuleCategory.Cover };
            for (int k = 0; k < order.Length; k++)
            {
                ModuleCategory category = order[k];
                int quota = _p.QuotaFor(category);
                if (_conservative && category != ModuleCategory.Cover)
                {
                    quota = Math.Min(quota, category == ModuleCategory.Approach ? 1 : 0);
                }

                if (category == ModuleCategory.Elevation && quota > 1)
                {
                    quota = 1 + _rng.NextInt(quota); // 1..quota per half: 2-4 elevation modules in all (spec L660)
                }

                for (int i = 0; i < quota; i++)
                {
                    if (!PlaceOneIsland(category))
                    {
                        Notes.Add("no room for " + category);
                    }
                }
            }
        }

        private bool PlaceOneIsland(ModuleCategory category)
        {
            IReadOnlyList<ModuleGeometry> all = _content.ModulesOf(category);
            var candidates = new List<ModuleGeometry>(all.Count);
            var weights = new List<float>(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                ModuleGeometry g = all[i];
                int sources = CountSources(g);
                if (sources * 2 > _sourceBudget || (_conservative && g.IsVantage))
                {
                    continue;
                }

                candidates.Add(g);
                weights.Add(ArenaContent.ModuleWeight(g, _content.Catalogue.Module(g.ModuleId), _biome));
            }

            for (int attempt = 0; attempt < 3 && candidates.Count > 0; attempt++)
            {
                int pick = ArenaContent.WeightedPick(_rng, weights);
                ModuleGeometry g = candidates[pick];
                if (TryPlaceIsland(g, category, out Vec2 position, out int turns))
                {
                    _w.PlaceModulePair(g, position, turns);
                    Rect2 r = LayoutWriter.Footprint(g, position, turns);
                    _occupied.Add(r);
                    _occupied.Add(r.Mirrored());
                    _sourceBudget -= 2 * CountSources(g);
                    return true;
                }

                candidates.RemoveAt(pick);
                weights.RemoveAt(pick);
            }

            return false;
        }

        private static int CountSources(ModuleGeometry g)
        {
            int n = 0;
            for (int i = 0; i < g.Patches.Count; i++)
            {
                if (g.Patches[i].NaturalSource)
                {
                    n++;
                }
            }

            return n;
        }

        private bool TryPlaceIsland(ModuleGeometry g, ModuleCategory category, out Vec2 position, out int turns)
        {
            position = default(Vec2);
            turns = 0;
            float bestScore = float.NegativeInfinity;
            float reach = _p.Radius;
            for (int i = 0; i < PlacementTries; i++)
            {
                int q = _rng.NextInt(4);
                Vec2 c = new Vec2(_rng.Range(-reach, reach), _rng.Range(-reach, -LaneZoneHalf));
                Rect2 r = LayoutWriter.Footprint(g, c, q);
                if (!IslandFits(r))
                {
                    continue;
                }

                float score = Preference(category, r) + _rng.NextFloat() * 0.5f;
                if (score > bestScore)
                {
                    bestScore = score;
                    position = c;
                    turns = q;
                }
            }

            return !float.IsNegativeInfinity(bestScore);
        }

        private bool IslandFits(Rect2 r)
        {
            if (!InAuthoredHalf(r.Center) || !BoundaryBuilder.ContainsRect(_layout.Boundary, r, ArenaRules.ContourStripWidth))
            {
                return false;
            }

            for (int i = 0; i < _keepOut.Count; i++)
            {
                if (r.Overlaps(_keepOut[i]))
                {
                    return false;
                }
            }

            for (int i = 0; i < _occupied.Count; i++)
            {
                if (r.DistanceTo(_occupied[i]) < IslandGap)
                {
                    return false;
                }
            }

            return r.DistanceTo(r.Mirrored()) >= IslandGap;
        }

        /// <summary>
        /// Where each category prefers to sit: approach and topology pieces line the side corridors, cover gathers
        /// near the center and the lane, elevation and material modules take the outer floor.
        /// </summary>
        private float Preference(ModuleCategory category, Rect2 r)
        {
            float toCorridor = Math.Min(r.DistanceTo(Corridor(-1)), r.DistanceTo(Corridor(1)));
            float toCenter = r.Center.Length();
            switch (category)
            {
                case ModuleCategory.Approach:
                case ModuleCategory.Topology:
                    return -toCorridor;
                case ModuleCategory.Cover:
                    return -Math.Min(toCorridor, Math.Max(0f, toCenter - 6f));
                case ModuleCategory.Elevation:
                    return toCenter * 0.25f - toCorridor * 0.5f;
                default:
                    return -toCorridor * 0.5f;
            }
        }

        // ---- Terrain outcrops -------------------------------------------------------------------------------

        /// <summary>
        /// Anchored rock terminating the designated lane at its planned length (spec L310 "longest uninterrupted firing
        /// lane is 12 to 28 meters").
        /// </summary>
        private void PlaceLaneEnds()
        {
            if (_p.LaneHalfLength <= 0f)
            {
                return;
            }

            // Near the contour the block reaches past it, like an outcrop, so no seam opens behind it (spec L676).
            float x = _p.LaneHalfLength;
            float outer = x + LaneEndThickness;
            float contour = Math.Min(_layout.Boundary.InitialRadiusAt(270f - 8f), Math.Min(_layout.Boundary.InitialRadiusAt(270f), _layout.Boundary.InitialRadiusAt(270f + 8f)));
            if (outer > contour - ArenaRules.ContourStripWidth)
            {
                outer = Math.Max(outer, contour + 1f);
            }

            YawBox box = Box(-outer, -LaneEndHalfLength, -x, LaneEndHalfLength, 0f, ArenaRules.TallWallHeight);
            Solid rock = _w.NewSolid(SolidKind.Wall, box, ArenaMaterials.Rock, 0f);
            _w.AddMirroredPair(rock);
            Rect2 r = Rect2.Around(box);
            _occupied.Add(r);
            _occupied.Add(r.Mirrored());

            if (_p.Diameter > GlobalRules.Arena.DiameterSmall)
            {
                // A nose beside the lane zone closes the strip between the block and the center module, so no line
                // along the spawn axis passes between them; on the far side the mirrored block's nose does the same.
                YawBox nose = Box(-x, LaneZoneHalf + 0.25f, -LaneNoseInnerX, LaneZoneHalf + 1.25f, 0f, ArenaRules.TallWallHeight);
                _w.AddMirroredPair(_w.NewSolid(SolidKind.Wall, nose, ArenaMaterials.Rock, 0f));
                Rect2 n = Rect2.Around(nose);
                _occupied.Add(n);
                _occupied.Add(n.Mirrored());
            }
        }

        /// <summary>
        /// Central bulwarks (spec L310 lane limit). The final floor, crossovers, and bastion exits leave the middle of
        /// the arena open, so a sightline through the center runs until it meets a tall solid. For every direction in
        /// which nothing tall stands within <see cref="BulwarkRadius"/> of the center, an anchored wall arc is raised
        /// just outside the skeleton zones, so lanes through the middle stay under the 28 m limit.
        /// </summary>
        private void PlaceBulwarks()
        {
            if (_p.Diameter <= GlobalRules.Arena.DiameterSmall)
            {
                return; // every chord of the 24 m arena is shorter than the lane limit
            }

            const int Steps = 90; // 2-degree rays over the side-0 half
            var open = new bool[Steps];
            var exit = new float[Steps];
            for (int i = 0; i < Steps; i++)
            {
                Vec2 dir = Vec2.FromYaw(RayAngle(i));
                open[i] = FirstTallHit(Vec2.Zero, dir) > BulwarkRadius - BulwarkThickness * 0.5f;
                exit[i] = KeepOutExit(Vec2.Zero, dir);
            }

            int start = -1;
            for (int i = 0; i <= Steps; i++)
            {
                bool isOpen = i < Steps && open[i];
                if (isOpen && start < 0)
                {
                    start = i;
                }
                else if (!isOpen && start >= 0)
                {
                    RaiseBulwark(start, i - 1, exit);
                    start = -1;
                }
            }
        }

        private static float RayAngle(int i) => 90f + i * 2f + 1f;

        /// <summary>
        /// Closes the rays [first, last] with two staggered rows: inner pieces separated by gates a player walks
        /// through, and outer pieces behind each gate, so every line from the center meets a wall within the lane
        /// radius while the floor behind the rows keeps nearby ways in (spec L310 lanes, L676 contraction routes).
        /// </summary>
        private void RaiseBulwark(int first, int last, float[] exit)
        {
            float a0 = RayAngle(first) - 1f - BulwarkPaddingDeg;
            float a1 = RayAngle(last) + 1f + BulwarkPaddingDeg;
            float inner = BulwarkMinRadius;
            for (int i = first; i <= last; i++)
            {
                inner = Math.Max(inner, exit[i] + BulwarkThickness * 0.5f + 0.25f);
            }

            float outer = inner + BulwarkRowSpacing;
            bool staggered = outer <= BulwarkRadius;
            if (!staggered)
            {
                // No room for a second row: one continuous arc at the inner radius.
                PlaceArc(a0, a1, Math.Min(inner, BulwarkRadius));
                return;
            }

            // Inner row: pieces of BulwarkPieceLength with BulwarkGate openings; the outer row covers each opening.
            float pitchDeg = (BulwarkPieceLength + BulwarkGate) / inner * CMath.Rad2Deg;
            float pieceDeg = BulwarkPieceLength / inner * CMath.Rad2Deg;
            for (float a = a0; a < a1; a += pitchDeg)
            {
                PlaceArc(a, Math.Min(a + pieceDeg, a1), inner);
                float gate0 = a + pieceDeg;
                if (gate0 < a1)
                {
                    float gateMid = gate0 + (pitchDeg - pieceDeg) * 0.5f;
                    float coverDeg = (BulwarkGate + 1.2f) / outer * CMath.Rad2Deg;
                    PlaceArc(gateMid - coverDeg * 0.5f, gateMid + coverDeg * 0.5f, outer);
                }
            }
        }

        /// <summary>One straight bulwark piece spanning an arc of a circle (angles in degrees).</summary>
        private void PlaceArc(float b0, float b1, float radius)
        {
            for (float r = radius; r <= BulwarkRadius + 0.01f; r += 0.5f)
            {
                Vec2 p0 = Vec2.FromYaw(b0) * r;
                Vec2 p1 = Vec2.FromYaw(b1) * r;
                Vec2 mid = (p0 + p1) * 0.5f;
                float length = Vec2.Distance(p0, p1) + BulwarkThickness * 0.5f;
                var box = new YawBox(new Vec3(mid.X, ArenaRules.TallWallHeight * 0.5f, mid.Y),
                    new Vec3(length * 0.5f, ArenaRules.TallWallHeight * 0.5f, BulwarkThickness * 0.5f), CMath.YawOf(mid));
                if (BulwarkFits(box))
                {
                    Solid wall = _w.AddMirroredPair(_w.NewSolid(SolidKind.Wall, box, ArenaMaterials.Rock, 0f));
                    _bulwarks.Add(wall.Id);
                    _bulwarks.Add(wall.MirrorId);
                    Rect2 bounds = Rect2.Around(box);
                    _occupied.Add(bounds);
                    _occupied.Add(bounds.Mirrored());
                    return;
                }
            }
        }

        /// <summary>Distance along a ray to the first solid tall enough to block a standing lane.</summary>
        private float FirstTallHit(Vec2 origin, Vec2 dir)
        {
            float hit = float.PositiveInfinity;
            foreach (Solid s in _layout.Solids)
            {
                if (s.Kind == SolidKind.Floor || !s.BlocksSight || s.Box.Bottom > ArenaRules.LaneBandLow || s.Box.Top < ArenaRules.LaneBandHigh)
                {
                    continue;
                }

                if (LaneMeter.LineVsBox(origin, dir, s.Box, out float t0, out float t1) && t1 >= 0f)
                {
                    hit = Math.Min(hit, Math.Max(0f, t0));
                }
            }

            return hit;
        }

        /// <summary>Distance at which a ray leaves the last skeleton zone it crosses (searched up to the lane target).</summary>
        private float KeepOutExit(Vec2 origin, Vec2 dir)
        {
            float last = 0f;
            for (float r = 0f; r <= BulwarkLaneTarget; r += 0.25f)
            {
                Vec2 p = origin + dir * r;
                for (int i = 0; i < _keepOut.Count; i++)
                {
                    if (_keepOut[i].Contains(p))
                    {
                        last = r;
                        break;
                    }
                }
            }

            return last;
        }

        private bool BulwarkFits(YawBox box)
        {
            Rect2 bounds = Rect2.Around(box);
            _why = "half";
            if (bounds.MaxZ > -IslandGap * 0.5f || Footprint2D.Distance(box, MirrorMath.Box(box)) < IslandGap)
            {
                return false;
            }

            _why = "keepout";
            for (int i = 0; i < _keepOut.Count; i++)
            {
                Rect2 k = _keepOut[i];
                var zone = new YawBox(new Vec3(k.Center.X, 1f, k.Center.Y), new Vec3(k.Width * 0.5f, 1f, k.Depth * 0.5f), 0f);
                if (Footprint2D.Overlap(box, zone))
                {
                    return false;
                }
            }

            foreach (Solid s in _layout.Solids)
            {
                if (s.Kind != SolidKind.Floor && s.Box.Bottom < ArenaRules.Headroom && !_bulwarks.Contains(s.Id)
                    && Footprint2D.Distance(box, s.Box) < IslandGap)
                {
                    _why = "solid " + s.Id + " " + s.Material;
                    return false;
                }
            }

            _why = "contour";

            for (int i = 0; i < 4; i++)
            {
                if (_layout.Boundary.RadialMargin(box.FootprintCorner(i).ToXZ(), 0f) < ArenaRules.ContourKeepInMargin)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Rock outcrops straddling the irregular contour: they dress the boundary, break long perimeter sightlines,
        /// and supply anchored cover (spec L171 "Permanent arena terrain is anchored").
        /// </summary>
        private int PlaceOutcrops(int pairs)
        {
            int placed = 0;
            for (int i = 0; i < pairs; i++)
            {
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    float angle = _rng.Range(95f, 265f); // the side-0 half of the contour
                    float length = _rng.Range(2.5f, 4.5f);
                    float inside = _rng.Range(1.2f, 2.4f);
                    if (TryOutcrop(angle, length, inside))
                    {
                        placed++;
                        break;
                    }
                }
            }

            return placed;
        }

        private bool TryOutcrop(float angle, float length, float inside)
        {
            BoundaryContour b = _layout.Boundary;
            float r = b.InitialRadiusAt(angle);
            float depth = inside + 0.75f;
            float centerR = r - inside + depth * 0.5f;
            Vec2 c = Vec2.FromYaw(angle) * centerR;
            var box = new YawBox(new Vec3(c.X, ArenaRules.TallWallHeight * 0.5f, c.Y), new Vec3(length * 0.5f, ArenaRules.TallWallHeight * 0.5f, depth * 0.5f), angle);
            Rect2 bounds = Rect2.Around(box);
            if (!InAuthoredHalf(bounds.Center) || bounds.DistanceTo(bounds.Mirrored()) < IslandGap || !Clear(bounds, IslandGap))
            {
                return false;
            }

            Solid rock = _w.AddMirroredPair(_w.NewSolid(SolidKind.Wall, box, ArenaMaterials.Rock, 0f));
            _removable.Add(rock.Id);
            _removable.Add(rock.MirrorId);
            _occupied.Add(bounds);
            _occupied.Add(bounds.Mirrored());
            return true;
        }

        private bool Clear(Rect2 r, float gap)
        {
            for (int i = 0; i < _keepOut.Count; i++)
            {
                if (r.Overlaps(_keepOut[i]))
                {
                    return false;
                }
            }

            for (int i = 0; i < _occupied.Count; i++)
            {
                if (r.DistanceTo(_occupied[i]) < gap)
                {
                    return false;
                }
            }

            return true;
        }

        // ---- Tuning passes ----------------------------------------------------------------------------------

        /// <summary>
        /// Adjusts each bastion's front screen so useful central cover lies 2.0-3.5 s away while the spawn path stays
        /// inside its band (spec L310). Both screens change together, so the two sides stay mirrored.
        /// </summary>
        private void TuneBastions()
        {
            ArenaRules.SpawnPathBand(_p.Diameter, out float pathMin, out float pathMax);
            float reachMin = GlobalRules.Arena.CentralReachMinSec * ArenaRules.CentralReachReferenceSpeed + 0.4f;
            float reachMax = GlobalRules.Arena.CentralReachMaxSec * ArenaRules.CentralReachReferenceSpeed - 0.4f;
            ArenaNav nav = null;
            for (int pass = 0; pass < BastionTuningPasses; pass++)
            {
                nav = ArenaNav.Build(_layout, NoProps);
                var survey = new ArenaSurvey(_layout, nav);
                float reach = survey.CentralReachSeconds(0) * ArenaRules.CentralReachReferenceSpeed;
                float path = survey.SpawnPathLength();
                float step = 0f;
                if (reach < reachMin && path < pathMax - 2.5f)
                {
                    step = 0.75f;
                }
                else if (path > pathMax - 0.5f || reach > reachMax)
                {
                    step = -0.75f;
                }

                float next = CMath.Clamp(_bastion.WingLength + step, _wingMin, _wingMax);
                if (step == 0f || next == _bastion.WingLength)
                {
                    break;
                }

                _bastion.SetWingLength(_layout, next);
            }
        }

        /// <summary>
        /// Terrain filler (spec L310 cover and lane limits): anchored rock outcrops dropped into open floor away from
        /// the skeleton routes. A rock goes only where navigation clearance exceeds its half diagonal plus
        /// <see cref="FillerGap"/>, so every passage around it stays wider than a 2.5 m route and nothing is sealed.
        /// Lanes longer than the limit get a rock across their middle first; then rocks fill the largest open areas
        /// until cover reaches the lower part of the 18-30 percent band. Rocks are written in mirrored pairs.
        /// </summary>
        private ArenaNav FillTerrain(ArenaNav nav)
        {
            const float coverTarget = GlobalRules.Arena.CoverFractionMin + 0.025f;
            for (int pass = 0; pass < FillerPasses; pass++)
            {
                var survey = new ArenaSurvey(_layout, nav);
                CoverStats stats = survey.Cover();
                List<FiringLane> lanes = new LaneMeter(nav, _layout).LongerThan(GlobalRules.Arena.FiringLaneMax - 1.5f);
                float missing = (coverTarget - stats.CoverFraction) * stats.UsableArea * 0.5f; // per side
                if (lanes.Count == 0 && missing <= 0f)
                {
                    break;
                }

                // Break every long lane that no rock placed in this pass already interrupts.
                int placed = 0;
                var placedBoxes = new List<YawBox>();
                for (int i = 0; i < lanes.Count; i++)
                {
                    if (InterruptedBy(lanes[i], placedBoxes))
                    {
                        continue;
                    }

                    Rejects.Clear();
                    if (TryBreakLane(lanes[i], out YawBox rock))
                    {
                        placedBoxes.Add(rock);
                        placedBoxes.Add(MirrorMath.Box(rock));
                        placed++;
                    }
                    else if (i == 0)
                    {
                        var parts = new List<string>(); foreach (var kv in Rejects) parts.Add(kv.Key + "=" + kv.Value);
                        Notes.Add("lane of " + lanes[i].Length + " m could not be broken [" + string.Join(",", parts) + "] " + lanes[i].A + " " + lanes[i].B);
                    }
                }

                Notes.Add("DEBUG pass " + pass + " lanes " + lanes.Count + " (max " + (lanes.Count > 0 ? lanes[0].Length : 0f) + ") broken " + placed + " cover " + stats.CoverFraction);
                if (missing > 0f)
                {
                    placed += FillOpenFloor(nav, ref missing);
                    placed += Accrete(ref missing);
                }

                if (placed == 0)
                {
                    break;
                }

                nav = ArenaNav.Build(_layout, NoProps);
            }

            return nav;
        }

        /// <summary>Rocks in the largest open spaces, each centered where clearance leaves room around it.</summary>
        private int FillOpenFloor(ArenaNav nav, ref float missing)
        {
            List<FillerSpot> candidates = FillerCandidates(nav);
            candidates.Sort((x, y) =>
            {
                int c = y.Room.CompareTo(x.Room);
                return c != 0 ? c : x.Node.CompareTo(y.Node);
            });

            var chosen = new List<Vec3>(); // x, z and the clearance radius in Y
            for (int i = 0; i < candidates.Count && missing > 0f; i++)
            {
                Vec3 p = nav.NodePosition(candidates[i].Node);
                float clearance = nav.Clearance(candidates[i].Node);
                if (!FarFromChosen(p, clearance, chosen))
                {
                    continue;
                }

                float area = PlaceRock(p.XZ(), candidates[i].Room, _rng.Range(0f, 180f));
                if (area > 0f)
                {
                    missing -= area;
                    chosen.Add(new Vec3(p.X, clearance, p.Z));
                }
            }

            return chosen.Count;
        }

        /// <summary>
        /// Breaks a lane with a thin rock across it at an interior point (or across its mirror image). The rock may
        /// extend one existing obstacle but keeps a capsule passage to every other solid, so no passage closes.
        /// </summary>
        private static bool InterruptedBy(FiringLane lane, List<YawBox> boxes)
        {
            for (int i = 0; i < boxes.Count; i++)
            {
                if (LaneMeter.Interrupts(lane, boxes[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryBreakLane(FiringLane lane, out YawBox placed)
        {
            placed = default(YawBox);
            Vec2 dir = (lane.B - lane.A).Normalized();
            Vec2 across = new Vec2(dir.Y, -dir.X);

            // Interior points every 0.25 m, from the middle of the lane outward.
            var fractions = new List<float> { 0.5f };
            float step = 0.25f / Math.Max(1f, lane.Length);
            for (float f = 0.5f - step; f >= 0.1f; f -= step)
            {
                fractions.Add(f);
                fractions.Add(1f - f);
            }

            for (int i = 0; i < fractions.Count; i++)
            {
                Vec2 p = Vec2.Lerp(lane.A, lane.B, fractions[i]);
                Vec2 side = across;
                Vec2 along = dir;
                if (!MirrorMath.IsSideZeroHalf(p))
                {
                    // Break the mirror image of the lane instead: it is just as long, and its rock mirrors back here.
                    p = MirrorMath.Point(p);
                    side = -across;
                    along = -dir;
                }

                for (int s = -1; s <= 1; s += 2)
                {
                    if (TrySpur(p, side * s, along, out placed))
                    {
                        return true;
                    }
                }

                // Where no obstacle is near enough to grow a spur from, a free-standing post across the lane.
                float height = _rng.Range(2.2f, 3.0f);
                var post = new YawBox(new Vec3(p.X, height * 0.5f, p.Y), new Vec3(SpurThickness * 0.5f, height * 0.5f, 0.6f), CMath.YawOf(side));
                if (AttachedRockFits(post, out int touched))
                {
                    AddAttachedPair(post, touched);
                    placed = post;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// A thin spur grown from the nearest obstacle across the lane point p: it starts inside that obstacle's
        /// face and ends just past the lane, so it interrupts the lane while extending only one obstacle.
        /// </summary>
        private bool TrySpur(Vec2 p, Vec2 toward, Vec2 along, out YawBox placed)
        {
            placed = default(YawBox);
            float hit = float.PositiveInfinity;
            foreach (Solid s in _layout.Solids)
            {
                if (s.Kind == SolidKind.Floor || s.Walkable || s.Box.Bottom > ArenaRules.Headroom || s.Box.Top < ArenaRules.LaneBandHigh)
                {
                    continue;
                }

                if (LaneMeter.LineVsBox(p, toward, s.Box, out float t0, out float t1) && t1 >= 0f && t0 >= 0f)
                {
                    hit = Math.Min(hit, t0);
                }
            }

            if (hit > SpurMaxReach)
            {
                return false;
            }

            float start = -SpurOvershoot;
            float end = hit + SpurEmbed;
            Vec2 mid = p + toward * ((start + end) * 0.5f);
            float height = _rng.Range(2.2f, 3.0f);
            var box = new YawBox(new Vec3(mid.X, height * 0.5f, mid.Y), new Vec3(SpurThickness * 0.5f, height * 0.5f, (end - start) * 0.5f), CMath.YawOf(toward));
            if (!AttachedRockFits(box, out int touched))
            {
                return false;
            }

            AddAttachedPair(box, touched);
            placed = box;
            return true;
        }

        /// <summary>
        /// Grows existing obstacles with small attached rocks until cover reaches the target: each rock touches one
        /// solid and keeps a capsule passage to all others, so obstacle clusters grow without sealing floor.
        /// </summary>
        private int Accrete(ref float missing)
        {
            var anchors = new List<Solid>();
            foreach (Solid s in _layout.Solids)
            {
                if (!s.Walkable && s.Kind != SolidKind.Floor && s.Kind != SolidKind.AlcoveWall && s.Box.Bottom <= 0.05f
                    && s.Box.Top >= ArenaRules.CoverMinHeight && MirrorMath.IsSideZeroHalf(s.Box.Center.XZ())
                    && !_attached.Contains(s.Id) && !_hasAttachment.Contains(s.Id))
                {
                    anchors.Add(s);
                }
            }

            int placed = 0;
            for (int attempt = 0; attempt < AccretionTries && missing > 0f && anchors.Count > 0; attempt++)
            {
                Solid anchor = anchors[_rng.NextInt(anchors.Count)];
                float angle = _rng.Range(0f, 360f);
                Vec2 outward = Vec2.FromYaw(angle);
                Vec2 surface = anchor.Box.ClosestPointXZ(anchor.Box.Center.XZ() + outward * 20f);
                float sx = _rng.Range(0.8f, 2.4f);
                float sz = _rng.Range(0.8f, 1.6f);
                Vec2 c = surface + outward * (Math.Min(sx, sz) * 0.5f - 0.1f);
                float height = _rng.Range(1.6f, 3.0f);
                var box = new YawBox(new Vec3(c.X, height * 0.5f, c.Y), new Vec3(sx * 0.5f, height * 0.5f, sz * 0.5f), _rng.Range(0f, 180f));
                if (!AttachedRockFits(box, out int touched) || (touched >= 0 && touched != anchor.Id))
                {
                    continue;
                }

                AddAttachedPair(box, touched);
                anchors.Remove(anchor);
                missing -= sx * sz;
                placed++;
            }

            return placed;
        }

        /// <summary>
        /// A rock fits when it stays in side 0 clear of its own image, inside the contour strip, outside the skeleton
        /// zones and patches, and within <see cref="FillerGap"/> of at most one non-walkable solid.
        /// </summary>
        public readonly Dictionary<string, int> Rejects = new Dictionary<string, int>();

        private void Rej(string r) { Rejects.TryGetValue(r, out int n); Rejects[r] = n + 1; }

        private bool AttachedRockFits(YawBox box, out int touchedId)
        {
            touchedId = -1;
            Rect2 bounds = Rect2.Around(box);
            if (!InAuthoredHalf(box.Center.XZ())) { Rej("half"); return false; }
            if (OverlapsKeepOut(bounds)) { Rej("keepout"); return false; }

            for (int i = 0; i < 4; i++)
            {
                if (_layout.Boundary.RadialMargin(box.FootprintCorner(i).ToXZ(), 0f) < ArenaRules.ContourStripWidth)
                {
                    Rej("contour"); return false;
                }
            }

            if (Footprint2D.Distance(box, MirrorMath.Box(box)) < FillerGap)
            {
                return false;
            }

            int touching = 0;
            foreach (Solid s in _layout.Solids)
            {
                if (s.Kind == SolidKind.Floor || s.Box.Bottom > ArenaRules.Headroom)
                {
                    continue;
                }

                float d = Footprint2D.Distance(box, s.Box);
                if (d >= FillerGap)
                {
                    continue;
                }

                // One attachment per original obstacle, never to another attached rock: an obstacle grows into an L or
                // T at most, never a cup that could trap floor as the boundary contracts (spec L676).
                // A rock may stand against a deck or ramp side but never on or into it.
                if ((s.Walkable && d <= 0.05f) || ++touching > 1 || _attached.Contains(s.Id) || _hasAttachment.Contains(s.Id))
                {
                    Rej(s.Walkable && d <= 0.05f ? "walk" : touching > 1 ? "touch2" : _attached.Contains(s.Id) ? "attached" : "hasattach"); return false;
                }

                touchedId = s.Id;
            }

            Vec2 center = box.Center.XZ();
            float radius = box.HalfExtents.X + box.HalfExtents.Z;
            foreach (MaterialPatch m in _layout.Materials)
            {
                float extent = m.IsCircle ? m.Radius : m.HalfSize.Length();
                if (Vec2.Distance(m.Center, center) < extent + radius)
                {
                    Rej("patch"); return false;
                }
            }

            return true;
        }

        private void AddAttachedPair(YawBox box, int touchedId)
        {
            Solid rock = _w.AddMirroredPair(_w.NewSolid(SolidKind.Wall, box, ArenaMaterials.Rock, 0f));
            _attached.Add(rock.Id);
            _attached.Add(rock.MirrorId);
            _removable.Add(rock.Id);
            _removable.Add(rock.MirrorId);
            if (touchedId >= 0)
            {
                _hasAttachment.Add(touchedId);
                Solid touched = _layout.FindSolid(touchedId);
                if (touched != null)
                {
                    _hasAttachment.Add(touched.MirrorId);
                }
            }

            Rect2 bounds = Rect2.Around(box);
            _occupied.Add(bounds);
            _occupied.Add(bounds.Mirrored());
        }

        private void AddRockPair(YawBox box)
        {
            _w.AddMirroredPair(_w.NewSolid(SolidKind.Wall, box, ArenaMaterials.Rock, 0f));
            Rect2 bounds = Rect2.Around(box);
            _occupied.Add(bounds);
            _occupied.Add(bounds.Mirrored());
        }

        /// <summary>A node where a filler rock fits, with the half diagonal the rock may take there.</summary>
        private struct FillerSpot
        {
            public int Node;
            public float Room;
        }

        /// <summary>
        /// Side-0 main-floor spots for filler rocks. Outside the skeleton a rock keeps a capsule passage
        /// (<see cref="FillerGap"/>) to everything; inside a skeleton zone it must keep a full route
        /// (<see cref="RouteGap"/>) around itself and stay off the protected cores: the designated lane, the final floor
        /// ring, and the bastion between its back wall and its screen.
        /// </summary>
        private List<FillerSpot> FillerCandidates(ArenaNav nav)
        {
            var result = new List<FillerSpot>();
            float laneHalf = _p.LaneHalfLength > 0f ? _p.LaneHalfLength : _p.Radius + 2f;
            Rect2 core = _bastion.Core();
            for (int node = 0; node < nav.NodeCount; node++)
            {
                float clearance = nav.Clearance(node);
                if (clearance < FillerGap + FillerMinHalfDiagonal)
                {
                    continue;
                }

                Vec3 p = nav.NodePosition(node);
                if (MathF.Abs(p.Y) > 0.05f)
                {
                    continue;
                }

                float room = Math.Min(clearance - FillerGap, FillerMaxHalfDiagonal);
                Rect2 r = Rect2.FromCenter(p.XZ(), 2f * room, 2f * room);
                if (OverlapsKeepOut(r))
                {
                    room = Math.Min(clearance - RouteGap, FillerMaxHalfDiagonal);
                    if (room < FillerMinHalfDiagonal)
                    {
                        continue;
                    }

                    Vec2 q = p.XZ();
                    bool inLane = MathF.Abs(q.Y) <= LaneZoneHalf + room && MathF.Abs(q.X) <= laneHalf + room;
                    bool nearFloor = q.Length() <= _layout.FinalFloorRadius + 1.5f + room;
                    if (inLane || nearFloor || core.Expanded(room).Contains(q) || core.Mirrored().Expanded(room).Contains(q))
                    {
                        continue;
                    }
                }

                if (!InAuthoredHalf(p.XZ()) || p.XZ().Length() < room + IslandGap * 0.5f || _layout.Boundary.RadialMargin(p, 0f) < room + ArenaRules.ContourStripWidth)
                {
                    continue;
                }

                result.Add(new FillerSpot { Node = node, Room = room });
            }

            return result;
        }

        private static bool FarFromChosen(Vec3 p, float room, List<Vec3> chosen)
        {
            for (int i = 0; i < chosen.Count; i++)
            {
                Vec3 c = chosen[i];
                float need = room + c.Y;
                if (Vec2.DistanceSquared(new Vec2(c.X, c.Z), p.XZ()) < need * need
                    || Vec2.DistanceSquared(new Vec2(-c.X, -c.Z), p.XZ()) < need * need)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Writes a mirrored rock pair inside a circle of the given half diagonal; returns the area of one rock.</summary>
        private float PlaceRock(Vec2 center, float halfDiagonal, float yaw, bool elongate = false)
        {
            halfDiagonal = Math.Min(halfDiagonal, FillerMaxHalfDiagonal);
            if (halfDiagonal < FillerMinHalfDiagonal)
            {
                return 0f;
            }

            float angle = elongate ? 20f : _rng.Range(25f, 65f);
            CMath.SinCosDeg(angle, out float sin, out float cos);
            float hx = halfDiagonal * cos;
            float hz = halfDiagonal * sin;
            float height = _rng.Range(2.2f, 3.2f);
            var box = new YawBox(new Vec3(center.X, height * 0.5f, center.Y), new Vec3(hx, height * 0.5f, hz), CMath.WrapAngle360(yaw));
            Solid placed = _w.AddMirroredPair(_w.NewSolid(SolidKind.Wall, box, ArenaMaterials.Rock, 0f));
            _removable.Add(placed.Id);
            _removable.Add(placed.MirrorId);
            Rect2 bounds = Rect2.Around(box);
            _occupied.Add(bounds);
            _occupied.Add(bounds.Mirrored());
            return 4f * hx * hz;
        }

        /// <summary>
        /// Generator choices are authored on side 0 and written with their half-turn image: the half-plane z &lt; 0 plus
        /// the x &lt; 0 side of the X axis, so terrain may straddle the axis wherever it stays clear of its own image.
        /// </summary>
        private static bool InAuthoredHalf(Vec2 p) => p.Y < 0f || (p.X < 0f && p.Y < SpawnBastion.InteriorHalfDepth);

        private bool OverlapsKeepOut(Rect2 r)
        {
            for (int i = 0; i < _keepOut.Count; i++)
            {
                if (r.Overlaps(_keepOut[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Contraction repair (spec L676): while some accessible cell would lose its route to the final floor as the
        /// boundary contracts, remove the filler rock or prop pair nearest that cell. Skeleton terrain, modules, and
        /// biome sources stay; anything left failing is caught by validation and the seed is retried.
        /// </summary>
        private void RepairContraction()
        {
            for (int iteration = 0; iteration < ContractionRepairs; iteration++)
            {
                ArenaNav nav = ArenaNav.Build(_layout);
                var survey = new ArenaSurvey(_layout, nav);
                if (survey.SpawnNode(0) < 0 || survey.FinalFloorNodes.Count == 0 || !survey.FindContractionFailure(out float _, out int node))
                {
                    return;
                }

                Vec2 cell = nav.NodePosition(node).XZ();
                Solid rock = null;
                PropPlacement prop = null;
                float best = ContractionRepairReach;
                foreach (Solid s in _layout.Solids)
                {
                    if (_removable.Contains(s.Id))
                    {
                        float d = ArenaSurvey.FootprintDistance(s.Box, cell);
                        if (d < best)
                        {
                            best = d;
                            rock = s;
                        }
                    }
                }

                foreach (PropPlacement p in _layout.Props)
                {
                    float d = Vec2.Distance(p.Position.XZ(), cell);
                    if (d < best)
                    {
                        best = d;
                        prop = p;
                        rock = null;
                    }
                }

                if (prop != null)
                {
                    PropPlacement image = _layout.FindProp(prop.MirrorId);
                    _layout.Props.Remove(prop);
                    _layout.Props.Remove(image);
                }
                else if (rock != null)
                {
                    _removable.Remove(rock.Id);
                    _removable.Remove(rock.MirrorId);
                    _w.RemoveSolidPair(rock);
                }
                else
                {
                    Notes.Add("contraction failure without a removable obstacle nearby");
                    return;
                }
            }
        }

        // ---- Biome dressing and props -----------------------------------------------------------------------

        private void DressBiome(ArenaNav nav)
        {
            if (_biome == null || !BiomeFeatures.TryGet(_biome.Id, out BiomeFeature f) || f.Kind == BiomeFeatureKind.Gusts)
            {
                return;
            }

            var finder = new SiteFinder(nav, _layout, _sites, _keepOut);
            for (int pair = 0; pair < f.Pairs; pair++)
            {
                bool solid = f.Kind == BiomeFeatureKind.Solid;
                float radius = f.FootprintRadius;
                float clearance = solid ? radius + 2.0f : radius + 0.25f;
                if (!finder.TryFind(_rng, clearance, radius, out Vec3 site))
                {
                    Notes.Add("no site for " + f.BiomeId);
                    return;
                }

                float yaw = 90f * _rng.NextInt(4);
                _sites.Add(new Vec3(site.X, radius, site.Z));
                switch (f.Kind)
                {
                    case BiomeFeatureKind.Patch:
                        _w.AddPatchPair(FeaturePatch(f, site.XZ(), yaw, 0f));
                        break;
                    case BiomeFeatureKind.LightShaft:
                        _w.AddPatchPair(FeaturePatch(f, site.XZ(), yaw, 0f));
                        AddLightPair(new Vec3(site.X, 6f, site.Z), f.Radius);
                        break;
                    default:
                        var box = YawBox.OnFloor(new Vec3(site.X, 0f, site.Z), new Vec3(f.SizeX, f.Height, f.SizeZ), yaw);
                        _w.AddMirroredPair(_w.NewSolid(f.SolidKind, box, f.Material, f.Durability));
                        if (f.PatchOnTop)
                        {
                            _w.AddPatchPair(FeaturePatch(f, site.XZ(), yaw, f.Height));
                        }

                        break;
                }
            }
        }

        private static MaterialPatch FeaturePatch(BiomeFeature f, Vec2 center, float yaw, float surfaceY)
        {
            return new MaterialPatch
            {
                Center = center,
                HalfSize = new Vec2(f.SizeX * 0.5f, f.SizeZ * 0.5f),
                Radius = f.Kind == BiomeFeatureKind.LightShaft || f.Radius > 0f ? f.Radius : 0f,
                YawDeg = yaw,
                SurfaceY = surfaceY,
                Water = f.Water,
                Heat = f.Heat,
                Frost = f.Frost,
                Dust = f.Dust,
                Sand = f.Sand,
                Growth = f.Growth,
                Flags = f.Flags,
                NaturalSource = f.NaturalSource || f.PatchOnTop,
                RefillRule = f.Rule,
            };
        }

        /// <summary>
        /// Three to eight prop definitions, one mirrored pair each (spec L662 "ordinarily two instances of each for
        /// parity"), drawn by biome affinity from the available definitions. Props stand on open main floor with room
        /// around them, never on a spawn floor or the final circle.
        /// </summary>
        private void PlaceProps(ArenaNav nav)
        {
            var pool = new List<PropDef>();
            var weights = new List<float>();
            for (int i = 0; i < _content.Props.Count; i++)
            {
                PropDef def = _content.Props[i];
                if (_propFilter.IsAvailable(def.Id))
                {
                    pool.Add(def);
                    weights.Add(ArenaContent.PropWeight(def, _biome));
                }
            }

            int wanted = PropDefinitionCount();
            var finder = new SiteFinder(nav, _layout, _sites, _keepOut);
            int placed = 0;
            while (placed < wanted && pool.Count > 0)
            {
                int pick = ArenaContent.WeightedPick(_rng, weights);
                PropDef def = pool[pick];
                pool.RemoveAt(pick);
                weights.RemoveAt(pick);
                PropShape shape = PropShapes.For(def.Id);
                float radius = shape.PlacementRadius;
                float clearance = radius + (shape.BlocksMovement ? PropPassage : 0.25f);
                if (!finder.TryFind(_rng, clearance, radius, out Vec3 site))
                {
                    continue;
                }

                float yaw = 90f * _rng.NextInt(4);
                _w.AddPropPair(def.Id, site, yaw);
                _sites.Add(new Vec3(site.X, radius, site.Z));
                placed++;
            }

            if (placed < GlobalRules.Arena.PropDefinitionsMin)
            {
                Notes.Add("only " + placed + " prop definitions placed");
            }
        }

        private int PropDefinitionCount()
        {
            int min = GlobalRules.Arena.PropDefinitionsMin;
            int max;
            switch (_p.Diameter)
            {
                case GlobalRules.Arena.DiameterSmall: max = 4; break;
                case GlobalRules.Arena.DiameterMedium: max = 6; break;
                case GlobalRules.Arena.DiameterLarge: max = 7; break;
                default: max = GlobalRules.Arena.PropDefinitionsMax; break;
            }

            if (_conservative)
            {
                return min;
            }

            return _rng.NextInt(min, max + 1);
        }

        // ---- Lighting and gusts (spec L320, L664) -------------------------------------------------------------

        /// <summary>Standardized overhead lighting: one shaft over the final floor, one over each bastion and each corridor mouth.</summary>
        private void AddLighting()
        {
            var center = new LightShaft { Position = new Vec3(0f, 8f, 0f), Radius = _layout.FinalFloorRadius };
            center.MirrorId = _layout.LightShafts.Count;
            _layout.LightShafts.Add(center);
            AddLightPair(new Vec3(_bastion.OffsetX, 6f, _bastion.TipZ + 1.5f), 2f);
            float corridorX = _bastion.LateralExtent + 0.25f + CorridorWidth * 0.5f;
            AddLightPair(new Vec3(_bastion.OffsetX - corridorX, 6f, -LaneZoneHalf - 1f), 2f);
            AddLightPair(new Vec3(_bastion.OffsetX + corridorX, 6f, -LaneZoneHalf - 1f), 2f);
        }

        private void AddLightPair(Vec3 position, float radius)
        {
            int a = _layout.LightShafts.Count;
            _layout.LightShafts.Add(new LightShaft { Position = position, Radius = radius, MirrorId = a + 1 });
            _layout.LightShafts.Add(new LightShaft { Position = MirrorMath.Point(position), Radius = radius, MirrorId = a });
        }

        /// <summary>BIO-011 Windmill Yard: a 2 s strength-1 gust every 12 s along one previewed compass direction.</summary>
        private void AddGusts()
        {
            if (_biome == null || !BiomeFeatures.TryGet(_biome.Id, out BiomeFeature f) || f.Kind != BiomeFeatureKind.Gusts)
            {
                return;
            }

            // Across the spawn axis, so the push favors neither side.
            Vec2 direction = _rng.NextBool() ? new Vec2(1f, 0f) : new Vec2(-1f, 0f);
            for (float t = 12f; t < GlobalRules.Round.CombatMaxSec; t += 12f)
            {
                _layout.Gusts.Add(new GustEvent { TimeSec = t, DurationSec = 2f, Direction = direction, Strength = 1, Center = Vec2.Zero, Radius = 0f });
            }
        }

        // ---- Helpers ----------------------------------------------------------------------------------------

        private static YawBox Box(float x0, float z0, float x1, float z1, float y0, float y1)
        {
            return new YawBox(
                new Vec3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f),
                new Vec3(Math.Abs(x1 - x0) * 0.5f, Math.Abs(y1 - y0) * 0.5f, Math.Abs(z1 - z0) * 0.5f),
                0f);
        }
    }
}
