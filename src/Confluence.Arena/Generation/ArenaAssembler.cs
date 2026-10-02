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

        /// <summary>Clear space kept around the center module footprint.</summary>
        public const float CenterGap = 1.0f;

        private const int PlacementTries = 160;
        private const int BastionTuningPasses = 4;
        private const int FillerPasses = 8;

        /// <summary>Half length of the rock closing each end of the designated lane (it also closes the strips beside it).</summary>
        private const float LaneEndHalfLength = 4.5f;

        private static readonly ArenaNavOptions NoProps = new ArenaNavOptions { PropsAsObstacles = false };

        /// <summary>
        /// Free space a filler rock keeps to every other solid: a capsule passage (0.8 m) with margin, so rocks never
        /// seal floor. The skeleton routes are keep-out zones, so the 2.5 m routes (spec L312) are untouched.
        /// </summary>
        private const float FillerGap = 1.25f;

        private const float LaneBreakerGap = FillerGap;

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
        private ModuleGeometry _center;
        private int _sourceBudget = GlobalRules.Arena.MaxNaturalSourcePatches;

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
            ReserveBiomeSources();
            PlaceIslands();
            PlaceLaneEnds();
            PlaceOutcrops(_p.OutcropPairs);

            ArenaNav nav = ArenaNav.Build(_layout, NoProps);
            nav = FillTerrain(nav);

            DressBiome(nav);
            PlaceProps(nav);
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
            _bastion = new SpawnBastion(_w, _p.SpawnDistance, SpawnBastion.WingLengthFor(_p.FrontWallHalfWidth));
            _wingMax = SpawnBastion.WingLengthFor(_p.FrontWallHalfWidth + 2.0f);
            _bastion.Build(_layout);
        }

        /// <summary>Side approach corridor east (sign 1) or west (sign -1) of the bastion, running to the firing lane.</summary>
        private Rect2 Corridor(int sign)
        {
            float inner = _bastion.LateralExtent + 0.25f;
            float outer = inner + CorridorWidth;
            return sign > 0 ? new Rect2(inner, _bastion.SpawnZ, outer, LaneZoneHalf) : new Rect2(-outer, _bastion.SpawnZ, -inner, LaneZoneHalf);
        }

        private void AddSkeletonKeepOuts()
        {
            // Bastion, its exits, and the crossover strip in front of the screen; then the side approach corridors.
            AddKeepOut(_bastion.Zone(CorridorWidth));
            AddKeepOut(Corridor(1));
            AddKeepOut(Corridor(-1));

            // Firing lane zone along X (both halves of it are in the side-0 list after mirroring).
            float laneHalf = _p.LaneHalfLength > 0f ? _p.LaneHalfLength + 1f : _p.Radius + 2f;
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
            if (r.MaxZ > -LaneZoneHalf || !BoundaryBuilder.ContainsRect(_layout.Boundary, r, ArenaRules.ContourStripWidth))
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

            float x = _p.LaneHalfLength + 0.5f;
            YawBox box = Box(-x - 0.5f, -LaneEndHalfLength, -x + 0.5f, LaneEndHalfLength, 0f, ArenaRules.TallWallHeight);
            Solid rock = _w.NewSolid(SolidKind.Wall, box, ArenaMaterials.Rock, 0f);
            _w.AddMirroredPair(rock);
            Rect2 r = Rect2.Around(box);
            _occupied.Add(r);
            _occupied.Add(r.Mirrored());
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
            if (bounds.MaxZ > -LaneZoneHalf || !Clear(bounds, IslandGap))
            {
                return false;
            }

            Solid rock = _w.NewSolid(SolidKind.Wall, box, ArenaMaterials.Rock, 0f);
            _w.AddMirroredPair(rock);
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

                float next = CMath.Clamp(_bastion.WingLength + step, 0.75f, _wingMax);
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
                FiringLane lane = new LaneMeter(nav, _layout).Longest();
                bool longLane = lane.Length > GlobalRules.Arena.FiringLaneMax - 1.5f;
                float missing = (coverTarget - stats.CoverFraction) * stats.UsableArea * 0.5f; // per side
                if (!longLane && missing <= 0f)
                {
                    break;
                }

                List<int> candidates = FillerCandidates(nav, FillerGap);
                var chosen = new List<Vec3>(); // x, z and the clearance radius in Y
                if (longLane)
                {
                    PlaceLaneBreaker(nav, FillerCandidates(nav, LaneBreakerGap), lane, chosen);
                }

                if (missing > 0f)
                {
                    candidates.Sort((x, y) =>
                    {
                        int c = nav.Clearance(y).CompareTo(nav.Clearance(x));
                        return c != 0 ? c : x.CompareTo(y);
                    });
                    for (int i = 0; i < candidates.Count && missing > 0f; i++)
                    {
                        Vec3 p = nav.NodePosition(candidates[i]);
                        float room = nav.Clearance(candidates[i]);
                        if (!FarFromChosen(p, room, chosen))
                        {
                            continue;
                        }

                        float area = PlaceRock(p.XZ(), room - FillerGap, _rng.Range(0f, 180f));
                        if (area > 0f)
                        {
                            missing -= area;
                            chosen.Add(new Vec3(p.X, room, p.Z));
                        }
                    }
                }

                if (chosen.Count == 0)
                {
                    Notes.Add(longLane ? "lane of " + lane.Length + " m could not be broken" : "cover fill found no room");
                    break;
                }

                nav = ArenaNav.Build(_layout, NoProps);
            }

            return nav;
        }

        /// <summary>Side-0 main-floor nodes in open floor outside every skeleton route.</summary>
        private List<int> FillerCandidates(ArenaNav nav, float gap)
        {
            var result = new List<int>();
            float minRoom = gap + FillerMinHalfDiagonal;
            for (int node = 0; node < nav.NodeCount; node++)
            {
                float room = nav.Clearance(node);
                if (room < minRoom)
                {
                    continue;
                }

                Vec3 p = nav.NodePosition(node);
                if (MathF.Abs(p.Y) > 0.05f || p.Z > -(room - gap) - IslandGap * 0.5f)
                {
                    continue;
                }

                float half = Math.Min(room - gap, FillerMaxHalfDiagonal);
                Rect2 r = Rect2.FromCenter(p.XZ(), 2f * half, 2f * half);
                if (_layout.Boundary.RadialMargin(p, 0f) < half + ArenaRules.ContourStripWidth || OverlapsKeepOut(r))
                {
                    continue;
                }

                result.Add(node);
            }

            return result;
        }

        private void PlaceLaneBreaker(ArenaNav nav, List<int> candidates, FiringLane lane, List<Vec3> chosen)
        {
            int best = -1;
            float bestRoom = 0f;
            for (int pass = 0; pass < 2 && best < 0; pass++)
            {
                Vec2 a = pass == 0 ? lane.A : MirrorMath.Point(lane.A);
                Vec2 b = pass == 0 ? lane.B : MirrorMath.Point(lane.B);
                for (int i = 0; i < candidates.Count; i++)
                {
                    Vec2 p = nav.NodePosition(candidates[i]).XZ();
                    Vec2 c = Intersections.ClosestPointOnSegment(p, a, b);
                    float t = Vec2.Distance(a, c) / Math.Max(1e-3f, Vec2.Distance(a, b));
                    float room = nav.Clearance(candidates[i]);
                    float reach = Math.Min(FillerMaxHalfDiagonal, room - LaneBreakerGap) * 0.8f;
                    if (Vec2.Distance(p, c) <= reach && t > 0.12f && t < 0.88f && room > bestRoom)
                    {
                        best = candidates[i];
                        bestRoom = room;
                    }
                }
            }

            if (best < 0)
            {
                return;
            }

            Vec3 q = nav.NodePosition(best);
            float yaw = CMath.YawOf(lane.B - lane.A) + 90f;
            if (PlaceRock(q.XZ(), bestRoom - LaneBreakerGap, yaw, elongate: true) > 0f)
            {
                chosen.Add(new Vec3(q.X, bestRoom, q.Z));
            }
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
            _w.AddMirroredPair(_w.NewSolid(SolidKind.Wall, box, ArenaMaterials.Rock, 0f));
            Rect2 bounds = Rect2.Around(box);
            _occupied.Add(bounds);
            _occupied.Add(bounds.Mirrored());
            return 4f * hx * hz;
        }

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
                float clearance = radius + (shape.BlocksMovement ? 1.0f : 0.25f);
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
            AddLightPair(new Vec3(0f, 6f, _bastion.TipZ + 1.5f), 2f);
            float corridorX = _bastion.LateralExtent + 0.25f + CorridorWidth * 0.5f;
            AddLightPair(new Vec3(-corridorX, 6f, -LaneZoneHalf - 1f), 2f);
            AddLightPair(new Vec3(corridorX, 6f, -LaneZoneHalf - 1f), 2f);
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
