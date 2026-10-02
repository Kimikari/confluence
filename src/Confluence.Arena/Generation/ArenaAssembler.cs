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

        private const int PlacementTries = 40;
        private const int BastionTuningPasses = 4;
        private const int LaneBreakPasses = 8;

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
        private float _frontHalf;
        private float _frontHalfMax;
        private Solid _frontWall;
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
            _frontHalf = _p.FrontWallHalfWidth;
            _frontHalfMax = _p.FrontWallHalfWidth + 1.5f;

            AddFloor();
            PlaceCenter();
            PlaceBastions();
            AddSkeletonKeepOuts();
            ReserveBiomeSources();
            PlaceIslands();
            PlaceLaneEnds();
            PlaceOutcrops(_p.OutcropPairs);

            ArenaNav nav = TuneBastions();
            nav = BreakLongLanes(nav);
            nav = FillCover(nav);

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

            // Contributing natural sources of the center (both ends are part of the same module).
            for (int i = 0; i < _center.Patches.Count; i++)
            {
                if (_center.Patches[i].NaturalSource)
                {
                    _sourceBudget--;
                }
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

        /// <summary>
        /// Protected spawn alcove (spec L46-L47): a 5 by 3 m channel between an anchored back wall and a wider
        /// front screen, open at both ends. The screen hides the spawn from the opponent (L310) and its width sets the
        /// walking detour to the center; the two exits give each spawn two disjoint ways out.
        /// </summary>
        private void PlaceBastions()
        {
            float z = -_p.SpawnDistance;
            const float h = ArenaRules.TallWallHeight;
            const float t = ArenaRules.WallThickness;
            Solid back = _w.NewSolid(SolidKind.AlcoveWall, Box(-3f, z - 1.5f - t, 3f, z - 1.5f, 0f, h), ArenaMaterials.Alcove, 0f);
            _w.AddMirroredPair(back);
            _frontWall = _w.NewSolid(SolidKind.AlcoveWall, Box(-_frontHalf, z + 1.5f, _frontHalf, z + 1.5f + t, 0f, h), ArenaMaterials.Alcove, 0f);
            _w.AddMirroredPair(_frontWall);

            var alcove = new YawBox(new Vec3(0f, 1.5f, z), new Vec3(2.5f, 1.5f, 1.5f), 0f);
            _layout.Spawns[0] = new SpawnPoint { Side = 0, Position = new Vec3(0f, 0f, z), YawDeg = 0f, Alcove = alcove };
            _layout.Spawns[1] = new SpawnPoint
            {
                Side = 1,
                Position = MirrorMath.Point(new Vec3(0f, 0f, z)),
                YawDeg = 180f,
                Alcove = MirrorMath.Box(alcove),
            };
        }

        private void SetFrontHalf(float half)
        {
            _frontHalf = half;
            float z = -_p.SpawnDistance;
            YawBox box = Box(-half, z + 1.5f, half, z + 1.5f + ArenaRules.WallThickness, 0f, ArenaRules.TallWallHeight);
            _frontWall.Box = box;
            _layout.FindSolid(_frontWall.MirrorId).Box = MirrorMath.Box(box);
        }

        private void AddSkeletonKeepOuts()
        {
            float z = -_p.SpawnDistance;
            float wing = _frontHalfMax + CorridorWidth;

            // Bastion, its exits, and the crossover strip in front of it.
            AddKeepOut(new Rect2(-wing, z - 2.0f - 0.5f, wing, z + 2.0f + CorridorWidth));

            // Side approach corridors from the exits to the firing lane.
            AddKeepOut(new Rect2(_frontHalfMax, z, wing, LaneZoneHalf));
            AddKeepOut(new Rect2(-wing, z, -_frontHalfMax, LaneZoneHalf));

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
            ModuleCategory[] order = { ModuleCategory.Elevation, ModuleCategory.Approach, ModuleCategory.Topology, ModuleCategory.Material, ModuleCategory.Cover };
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
            if (r.MaxZ > -LaneZoneHalf || !BoundaryBuilder.ContainsRect(_layout.Boundary, r, ArenaRules.ContourKeepInMargin))
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
            float z = -_p.SpawnDistance;
            var corridorL = new Rect2(-_frontHalfMax - CorridorWidth, z, -_frontHalfMax, LaneZoneHalf);
            var corridorR = new Rect2(_frontHalfMax, z, _frontHalfMax + CorridorWidth, LaneZoneHalf);
            float toCorridor = Math.Min(r.DistanceTo(corridorL), r.DistanceTo(corridorR));
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
            YawBox box = Box(-x - 0.5f, -2.75f, -x + 0.5f, 2.75f, 0f, ArenaRules.TallWallHeight);
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
        private ArenaNav TuneBastions()
        {
            ArenaRules.SpawnPathBand(_p.Diameter, out float pathMin, out float pathMax);
            float reachMin = GlobalRules.Arena.CentralReachMinSec * ArenaRules.CentralReachReferenceSpeed + 0.4f;
            float reachMax = GlobalRules.Arena.CentralReachMaxSec * ArenaRules.CentralReachReferenceSpeed - 0.4f;
            ArenaNav nav = null;
            for (int pass = 0; pass < BastionTuningPasses; pass++)
            {
                nav = ArenaNav.Build(_layout, new ArenaNavOptions { PropsAsObstacles = false });
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

                float next = CMath.Clamp(_frontHalf + step, 2.75f, _frontHalfMax);
                if (step == 0f || next == _frontHalf)
                {
                    break;
                }

                SetFrontHalf(next);
            }

            return nav;
        }

        /// <summary>Breaks lanes longer than the limit with anchored rock, mirrored (spec L310).</summary>
        private ArenaNav BreakLongLanes(ArenaNav nav)
        {
            for (int pass = 0; pass < LaneBreakPasses; pass++)
            {
                var meter = new LaneMeter(nav, _layout);
                FiringLane lane = meter.Longest();
                if (lane.Length <= GlobalRules.Arena.FiringLaneMax - 1.5f)
                {
                    break;
                }

                if (!TryBreak(lane))
                {
                    Notes.Add("lane of " + lane.Length + " m could not be broken");
                    break;
                }

                nav = ArenaNav.Build(_layout, new ArenaNavOptions { PropsAsObstacles = false });
            }

            return nav;
        }

        private bool TryBreak(FiringLane lane)
        {
            float[] fractions = { 0.5f, 0.4f, 0.6f, 0.3f, 0.7f, 0.2f, 0.8f, 0.15f, 0.85f };
            Vec2 dir = (lane.B - lane.A).Normalized();
            float yaw = CMath.YawOf(dir) + 90f;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < fractions.Length; i++)
                {
                    Vec2 p = Vec2.Lerp(lane.A, lane.B, fractions[i]);
                    if (pass == 1)
                    {
                        p = MirrorMath.Point(p); // the lane's mirror image is just as long; try breaking it there
                    }

                    if (!MirrorMath.IsSideZeroHalf(p))
                    {
                        continue;
                    }

                    var box = new YawBox(new Vec3(p.X, ArenaRules.TallWallHeight * 0.5f, p.Y), new Vec3(1.25f, ArenaRules.TallWallHeight * 0.5f, 0.5f), pass == 1 ? MirrorMath.Yaw(yaw) : yaw);
                    Rect2 bounds = Rect2.Around(box);
                    if (bounds.MaxZ > -LaneZoneHalf || !Clear(bounds, IslandGap)
                        || !BoundaryBuilder.ContainsRect(_layout.Boundary, bounds, ArenaRules.ContourKeepInMargin))
                    {
                        continue;
                    }

                    _w.AddMirroredPair(_w.NewSolid(SolidKind.Wall, box, ArenaMaterials.Rock, 0f));
                    _occupied.Add(bounds);
                    _occupied.Add(bounds.Mirrored());
                    return true;
                }
            }

            return false;
        }

        /// <summary>Adds boundary outcrops until cover reaches the band's lower part (spec L310: 18-30 percent).</summary>
        private ArenaNav FillCover(ArenaNav nav)
        {
            const float target = GlobalRules.Arena.CoverFractionMin + 0.02f;
            for (int round = 0; round < 3; round++)
            {
                var survey = new ArenaSurvey(_layout, nav);
                CoverStats stats = survey.Cover();
                if (stats.CoverFraction >= target)
                {
                    break;
                }

                // Each outcrop pair adds roughly 2 * length * inside of cover; aim slightly past the target.
                float missing = (target - stats.CoverFraction) * stats.UsableArea;
                int pairs = Math.Max(1, (int)Math.Ceiling(missing / 2f / 5.0f));
                if (PlaceOutcrops(pairs) == 0)
                {
                    break;
                }

                nav = ArenaNav.Build(_layout, new ArenaNavOptions { PropsAsObstacles = false });
            }

            return nav;
        }

        // ---- Biome dressing and props -----------------------------------------------------------------------

        private void DressBiome(ArenaNav nav)
        {
            if (_biome == null || !BiomeFeatures.TryGet(_biome.Id, out BiomeFeature f) || f.Kind == BiomeFeatureKind.Gusts)
            {
                return;
            }

            var finder = new SiteFinder(nav, _layout, _sites);
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
            var finder = new SiteFinder(nav, _layout, _sites);
            int placed = 0;
            while (placed < wanted && pool.Count > 0)
            {
                int pick = ArenaContent.WeightedPick(_rng, weights);
                PropDef def = pool[pick];
                pool.RemoveAt(pick);
                weights.RemoveAt(pick);
                PropShape shape = PropShapes.For(def.Id);
                float radius = shape.PlacementRadius;
                float clearance = radius + (shape.BlocksMovement ? 1.75f : 0.25f);
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
            AddLightPair(new Vec3(0f, 6f, -_p.SpawnDistance + 3.5f), 2f);
            AddLightPair(new Vec3(-(_frontHalfMax + CorridorWidth * 0.5f), 6f, -LaneZoneHalf - 1f), 2f);
            AddLightPair(new Vec3(_frontHalfMax + CorridorWidth * 0.5f, 6f, -LaneZoneHalf - 1f), 2f);
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
