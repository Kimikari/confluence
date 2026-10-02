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

        /// <summary>Gap a required cover module may keep to other solids when nothing else fits (a capsule passage, 0.8 m, with margin).</summary>
        public const float CoverIslandGap = 1.0f;

        /// <summary>Half width of the firing lane zone along the X axis (a 3.5 m route).</summary>
        public const float LaneZoneHalf = 1.75f;

        /// <summary>Width of the guaranteed approach corridors and bastion exits (2.5 m routes plus quantization).</summary>
        public const float CorridorWidth = 3.0f;

        /// <summary>Width of the bastion exits and crossover kept clear beside and in front of the screen (a 2.5 m route plus margin).</summary>
        public const float ExitWidth = 2.75f;

        /// <summary>Clear space kept around the center module footprint.</summary>
        public const float CenterGap = 1.0f;

        private const int PlacementTries = 160;

        /// <summary>Smallest footprint of a landmark module (MOD-002 Dogleg Lane and up: 8 x 4 m); cover and material pieces are smaller.</summary>
        private const float LandmarkMinFootprintArea = 32f;

        /// <summary>Module picks tried for an optional island before the category gives up.</summary>
        private const int IslandAttempts = 3;

        /// <summary>Best-scoring poses re-measured on the navigation grid per module pick.</summary>
        private const int IslandSiteChecks = 3;

        /// <summary>Score penalty for a pose whose pieces stand in a bastion exit or crossover strip.</summary>
        private const float IslandRoutePenalty = 1.5f;

        /// <summary>Poses re-measured for a required module, and the lattice step of its fallback scan.</summary>
        private const int IslandRequiredSiteChecks = 8;

        private const float IslandScanStep = 0.5f;

        /// <summary>Distance from a witness path within which an addition may change a route guarantee (route clearance plus a cell).</summary>
        private const float WitnessMargin = ArenaRules.RouteClearance + ArenaRules.NavCellSize;

        /// <summary>Additions this close to the center may change the useful central cover, so they are always measured.</summary>
        private const float WitnessCenterRadius = ArenaRules.CentralCoverRadius + ArenaRules.UsefulCoverAdjacency + 0.5f;

        /// <summary>Score penalty per open bulwark ray an elevation pose takes the room from (2-degree rays).</summary>
        private const float BulwarkConflictPenalty = 0.5f;

        /// <summary>Clear floor every module piece keeps inside the contour: a capsule passage with margin (spec L676).</summary>
        private const float IslandContourMargin = 1.25f;
        private const int BastionTuningPasses = 4;
        private const int FillerPasses = 12;

        private const int AccretionTries = 400;

        /// <summary>Lane breakers may add a second attachment to an obstacle (contraction repair removes any that trap floor).</summary>
        private const int LaneBreakerAttachLimit = 2;

        private const int ContractionRepairs = 12;

        /// <summary>Obstacles near a stranded cell considered per repair, nearest first.</summary>
        private const int ContractionRepairCandidates = 8;

        /// <summary>Only an obstacle this close to a stranded cell is removed to repair it.</summary>
        private const float ContractionRepairReach = 6f;

        /// <summary>Rocks near a stranded cell that may be trimmed when none can be removed.</summary>
        private const int ContractionTrimCandidates = 4;

        /// <summary>Depth trimmed off a rock face at a pinch: a sub-capsule gap (under 0.8 m) becomes a passage.</summary>
        private const float RockTrimDepth = 0.75f;

        /// <summary>Smallest rock side a trim may leave.</summary>
        private const float RockTrimMinSize = 0.6f;

        /// <summary>Center of the bulwark walls at most this far out: their inner faces keep lanes through the center under 28 m.</summary>
        private const float BulwarkRadius = 14.5f;

        private const float BulwarkMinRadius = 8f;

        /// <summary>Nearest a bulwark piece may come to the center when a module holds its planned radius.</summary>
        private const float BulwarkInnermostRadius = 6.5f;
        private const float BulwarkThickness = 1f;
        private const float BulwarkPieceLength = 2.4f;

        private const int SkeletonPostPasses = 3;
        private const float SkeletonPostLength = 2.0f;

        /// <summary>Free space a skeleton post keeps to other solids, so it never pinches a passage into a pocket.</summary>
        private const float SkeletonPostGap = 2.0f;

        /// <summary>Skeleton posts close only lanes with at least this many meters inside the skeleton zones.</summary>
        private const float SkeletonPostMinZoneRun = 12f;

        /// <summary>Opening between inner-row bulwark pieces (a capsule passage with margin).</summary>
        private const float BulwarkGate = 1.6f;

        /// <summary>Radial spacing between the bulwark rows (center to center).</summary>
        private const float BulwarkRowSpacing = 2.75f;

        /// <summary>Lines through a pivot are closed when longer than this (a margin under the 28 m lane limit).</summary>
        private const float BulwarkLaneTarget = 27f;

        /// <summary>Each bulwark arc extends this far past its open rays, so slightly off-center lanes meet it too.</summary>
        private const float BulwarkPaddingDeg = 2f;

        /// <summary>Sites tried per prop definition (a blocking prop that would narrow a route is withdrawn).</summary>
        private const int PropSiteAttempts = 2;

        /// <summary>Rock pairs a terrain stage may withdraw to keep the skeleton guarantees before it is undone.</summary>
        private const int StageCulprits = 3;

        /// <summary>Free space around a movement-blocking prop: a capsule passage the navigation grid always resolves.</summary>
        private const float PropPassage = 1.25f;

        /// <summary>Spur geometry for lane breaking: thin, reaching at most 3 m from an obstacle, ending 0.4 m past the lane.</summary>
        private const float SpurThickness = 0.6f;

        private const float SpurMaxReach = 3f;
        private const float SpurOvershoot = 0.4f;
        private const float SpurEmbed = 0.2f;


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

        /// <summary>Open floor a rock keeps around a material patch (beds, pools, and tracks stay open floor).</summary>
        private const float RockPatchMargin = 0.5f;

        private const float FillerMinHalfDiagonal = 0.6f;

        /// <summary>Route-zone lane breakers and filler rocks tried per arena (each costs a navigation rebuild).</summary>
        private const int LaneRouteChecks = 12;

        private const int FillRouteChecks = 8;
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
        private readonly List<Rect2> _islands = new List<Rect2>();
        private readonly List<Rect2> _hardZones = new List<Rect2>();
        private readonly List<Rect2> _softZones = new List<Rect2>();
        private bool[] _routeBaseline;
        private List<Vec2> _witness;
        private ArenaNav _repairNav;
        private bool _witnessWithProps;
        private Rect2 _laneZone;
        private List<Vec3> _openRays;
        private Rect2 _centerFootprint;
        private int _laneRouteChecks;
        private int _lastTouching;
        private int _fillRouteChecks;
        private bool _conflictsStrict;

        /// <summary>True once a landmark pair stands (it replaces the arena's other topology modules).</summary>
        private bool _landmark;
        private readonly List<Vec3> _sites = new List<Vec3>(); // placed features and props (side 0), x/z plus radius in Y
        private SpawnBastion _bastion;
        private float _wingMax;
        private float _wingMin;
        private ModuleGeometry _center;
        private int _sourceBudget = GlobalRules.Arena.MaxNaturalSourcePatches;
        private readonly HashSet<int> _attached = new HashSet<int>();
        private readonly List<Rect2> _cutouts = new List<Rect2>();
        private Solid _floor;
        private float _floorHalf;
        private readonly HashSet<int> _removable = new HashSet<int>();
        private readonly HashSet<int> _bulwarks = new HashSet<int>();
        private readonly Dictionary<int, int> _attachments = new Dictionary<int, int>();

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

            // On the small arenas the required elevation modules take the open floor first, where they leave every
            // bulwark its room: their decks and ramps are the hardest pieces to fit (spec L660), and the bulwark rings
            // would leave none. Larger arenas keep open floor beyond the rings, so there they come with the others; the
            // 48 m arena first sets one large landmark module.
            ReserveBiomeSources();
            if (_p.Diameter <= GlobalRules.Arena.DiameterMedium)
            {
                PlaceRequiredElevation();
            }
            else if (_p.Diameter >= GlobalRules.Arena.DiameterHuge && !_conservative)
            {
                PlaceLandmark();
            }

            PlaceBulwarks();
            PlaceSkeletonLanePosts();

            // The biome rule's sources and objects come next, then the remaining catalogue modules.
            DressBiome(ArenaNav.Build(_layout, NoProps));
            PlaceIslands();
            CutFloor();
            PlaceOutcrops(_p.OutcropPairs);

            ArenaNav nav = ArenaNav.Build(_layout, NoProps);
            nav = FillTerrain(nav);

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
            _floor = _w.AddSelfMirrored(_w.NewSolid(SolidKind.Floor, SlabBox(new Rect2(-half, -half, half, half)), ArenaMaterials.Floor, 0f, walkable: true));
            _floorHalf = half;
        }

        private static YawBox SlabBox(Rect2 r)
        {
            return new YawBox(new Vec3(r.Center.X, -ArenaRules.FloorSlabThickness * 0.5f, r.Center.Y),
                new Vec3(r.Width * 0.5f, ArenaRules.FloorSlabThickness * 0.5f, r.Depth * 0.5f), 0f);
        }

        /// <summary>Replaces the single floor slab with anchored pieces around sunken modules' cutouts (mirrored pairs).</summary>
        private void CutFloor()
        {
            if (_cutouts.Count == 0)
            {
                return;
            }

            _w.RemoveSolidPair(_floor);
            List<Rect2> pieces = FloorSlabs.Pieces(_floorHalf, _cutouts);
            var done = new bool[pieces.Count];
            for (int i = 0; i < pieces.Count; i++)
            {
                if (done[i])
                {
                    continue;
                }

                done[i] = true;
                Rect2 image = pieces[i].Mirrored();
                int match = -1;
                for (int j = 0; j < pieces.Count && match < 0; j++)
                {
                    if (Math.Abs(pieces[j].MinX - image.MinX) < 1e-3f && Math.Abs(pieces[j].MinZ - image.MinZ) < 1e-3f
                        && Math.Abs(pieces[j].MaxX - image.MaxX) < 1e-3f && Math.Abs(pieces[j].MaxZ - image.MaxZ) < 1e-3f)
                    {
                        match = j;
                    }
                }

                Solid slab = _w.NewSolid(SolidKind.Floor, SlabBox(pieces[i]), ArenaMaterials.Floor, 0f, walkable: true);
                if (match == i)
                {
                    _w.AddSelfMirrored(slab);
                }
                else
                {
                    _w.AddMirroredPair(slab);
                    if (match >= 0)
                    {
                        done[match] = true;
                    }
                }
            }
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
            _hardZones.Add(footprint);
            _centerFootprint = footprint;

            // The gap ring around the center is a route zone: pieces and rocks may stand there only when the routes
            // into the center survive on the navigation grid.
            Rect2 ring = footprint.Expanded(CenterGap);
            _softZones.Add(new Rect2(ring.MinX, ring.MinZ, ring.MaxX, footprint.MinZ));
            _softZones.Add(new Rect2(ring.MinX, footprint.MaxZ, ring.MaxX, ring.MaxZ));
            _softZones.Add(new Rect2(ring.MinX, footprint.MinZ, footprint.MinX, footprint.MaxZ));
            _softZones.Add(new Rect2(footprint.MaxX, footprint.MinZ, ring.MaxX, footprint.MaxZ));

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
            Rect2[] zones = _bastion.Zones(ExitWidth);
            for (int i = 0; i < zones.Length; i++)
            {
                AddKeepOut(zones[i]);
                if (i == 0)
                {
                    // The alcove and its screen are hard; the porch between the wings in front of the screen is a
                    // route zone like the exits and the crossover strip.
                    Rect2 core = zones[i];
                    float screenFront = _bastion.ScreenZ + SpawnBastion.Thickness;
                    var alcove = new Rect2(core.MinX, core.MinZ, core.MaxX, screenFront);
                    var porch = new Rect2(core.MinX, screenFront, core.MaxX, core.MaxZ);
                    _hardZones.Add(alcove);
                    _hardZones.Add(alcove.Mirrored());
                    _softZones.Add(porch);
                    _softZones.Add(porch.Mirrored());
                }
                else
                {
                    _softZones.Add(zones[i]);
                    _softZones.Add(zones[i].Mirrored());
                }
            }

            // Firing lane zone along X (both halves of it are in the side-0 list after mirroring).
            float laneHalf = _p.LaneHalfLength > 0f ? _p.LaneHalfLength + LaneEndThickness : _p.Radius + 2f;
            AddKeepOut(new Rect2(-laneHalf, -LaneZoneHalf, laneHalf, LaneZoneHalf));
            _laneZone = new Rect2(-laneHalf, -LaneZoneHalf, laneHalf, LaneZoneHalf);
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

        /// <summary>
        /// Catalogue module pairs (spec L660 "two to four elevation modules, and three to six cover modules"; L318).
        /// The required categories come first so they get the open floor; approach, topology, and material modules
        /// then dress what is left. The central soft cover pair, when present, counts toward the cover modules.
        /// </summary>
        /// <summary>
        /// The 48 m arena takes one landmark pair before the bulwark rings: a topology, elevation, or approach module with
        /// a footprint of at least 8 x 4 m. Once the rings and lane posts stand, the big route pieces (up to 10 x 10 m)
        /// find no room; placed first, their own walls close rays the rings would otherwise have to (spec L317 skeleton
        /// "from validated modules, matching sockets and heights", L660 composition). The landmark takes the arena's
        /// topology budget.
        /// </summary>
        private void PlaceLandmark()
        {
            _routeBaseline = RouteConditions();
            _openRays = OpenBulwarkRays();
            _conflictsStrict = true;
            ModuleCategory[] kinds = { ModuleCategory.Topology, ModuleCategory.Elevation, ModuleCategory.Approach };
            ModuleCategory category = kinds[_rng.NextInt(kinds.Length)];
            _landmark = PlaceOneIsland(category, true, LandmarkMinFootprintArea);
            _conflictsStrict = false;
            _openRays = null;
        }

        private void PlaceRequiredElevation()
        {
            _routeBaseline = RouteConditions();
            _openRays = OpenBulwarkRays();
            int pairs = (GlobalRules.Arena.ElevationModulesMin + 1) / 2;
            for (int i = 0; i < pairs; i++)
            {
                // First a pose that leaves every bulwark its room; failing that, the one that takes the least.
                _conflictsStrict = true;
                if (!PlaceOneIsland(ModuleCategory.Elevation, true))
                {
                    _conflictsStrict = false;
                    PlaceOneIsland(ModuleCategory.Elevation, true);
                }
            }

            _openRays = null;
        }

        /// <summary>
        /// Rays from the center that the bulwarks will have to close (as in <see cref="PlaceBulwarks"/>): direction in
        /// X/Y and the radius where the bulwark band starts in Z. Empty for the smallest arena.
        /// </summary>
        private List<Vec3> OpenBulwarkRays()
        {
            var rays = new List<Vec3>();
            if (_p.Diameter <= GlobalRules.Arena.DiameterSmall)
            {
                return rays;
            }

            for (int i = 0; i < 90; i++)
            {
                Vec2 dir = Vec2.FromYaw(RayAngle(i));
                if (FirstTallHit(Vec2.Zero, dir) > BulwarkRadius - BulwarkThickness * 0.5f)
                {
                    rays.Add(new Vec3(dir.X, dir.Y, Math.Max(BulwarkMinRadius, KeepOutExit(Vec2.Zero, dir) + BulwarkThickness * 0.5f + 0.25f)));
                }
            }

            return rays;
        }

        /// <summary>
        /// Open bulwark rays a module pose would take the bulwark room from: a piece too low to close the lane band
        /// itself, within a bulwark's gap of the ray inside the band. Elevation decks there would leave the lane open.
        /// </summary>
        private int BulwarkConflicts(ModuleGeometry g, Vec2 c, int q)
        {
            if (_openRays == null || _openRays.Count == 0)
            {
                return 0;
            }

            List<YawBox> boxes = LayoutWriter.PieceBoxes(g, c, q);
            float bandEnd = BulwarkRadius + 0.5f;
            int conflicts = 0;
            var blocked = new List<Vec2>();
            for (int r = 0; r < _openRays.Count; r++)
            {
                Vec3 ray = _openRays[r];
                var dir = new Vec2(ray.X, ray.Y);
                blocked.Clear();
                bool closes = false;
                for (int i = 0; i < boxes.Count && !closes; i++)
                {
                    ModulePiece piece = g.Pieces[i];
                    YawBox b = boxes[i];
                    if (piece.Kind == SolidKind.Floor)
                    {
                        continue;
                    }

                    // A tall piece inside the band closes the ray by itself.
                    bool tall = piece.BlocksSight && b.Bottom <= ArenaRules.LaneBandLow && b.Top >= ArenaRules.LaneBandHigh;
                    if (tall && LaneMeter.LineVsBox(Vec2.Zero, dir, b, out float h0, out float _) && h0 >= 0f && h0 <= BulwarkRadius)
                    {
                        closes = true;
                        continue;
                    }

                    var grown = new YawBox(b.Center, b.HalfExtents + new Vec3(IslandGap + BulwarkThickness * 0.5f, 0f, IslandGap + BulwarkThickness * 0.5f), b.YawDeg);
                    if (LaneMeter.LineVsBox(Vec2.Zero, dir, grown, out float t0, out float t1) && t1 >= ray.Z && t0 <= bandEnd)
                    {
                        blocked.Add(new Vec2(t0, t1));
                    }
                }

                if (!closes && blocked.Count > 0 && !HasFreeStretch(ray.Z, bandEnd, blocked, BulwarkThickness))
                {
                    conflicts++;
                }
            }

            return conflicts;
        }

        /// <summary>True when [from, to] minus the blocked intervals keeps a stretch at least the given length.</summary>
        private static bool HasFreeStretch(float from, float to, List<Vec2> blocked, float length)
        {
            blocked.Sort((a, b) => a.X.CompareTo(b.X));
            float cursor = from;
            for (int i = 0; i < blocked.Count; i++)
            {
                if (blocked[i].X - cursor >= length)
                {
                    return true;
                }

                cursor = Math.Max(cursor, blocked[i].Y);
            }

            return to - cursor >= length;
        }

        private void PlaceIslands()
        {
            _routeBaseline = RouteConditions();
            int softCover = CountModules(ModuleCategory.Cover);
            int coverPairsMin = Math.Max(0, (GlobalRules.Arena.CoverModulesMin - softCover + 1) / 2);
            int coverPairsMax = Math.Max(0, (GlobalRules.Arena.CoverModulesMax - softCover) / 2);
            int elevationPairsMin = (GlobalRules.Arena.ElevationModulesMin + 1) / 2;
            int elevationPairsMax = GlobalRules.Arena.ElevationModulesMax / 2;

            ModuleCategory[] order = { ModuleCategory.Elevation, ModuleCategory.Cover, ModuleCategory.Approach, ModuleCategory.Topology, ModuleCategory.Material };
            for (int k = 0; k < order.Length; k++)
            {
                ModuleCategory category = order[k];
                int quota = _p.QuotaFor(category);
                int required = 0;
                if (category == ModuleCategory.Elevation)
                {
                    quota = Math.Max(elevationPairsMin, Math.Min(quota, elevationPairsMax));
                    if (quota > elevationPairsMin)
                    {
                        quota = elevationPairsMin + _rng.NextInt(quota - elevationPairsMin + 1);
                    }

                    if (_conservative)
                    {
                        quota = elevationPairsMin;
                    }

                    // Pairs that already stood before the bulwarks (PlaceRequiredElevation) count toward both numbers.
                    int placedPairs = CountModules(ModuleCategory.Elevation) / 2;
                    required = Math.Max(0, elevationPairsMin - placedPairs);
                    quota -= placedPairs;
                }
                else if (category == ModuleCategory.Cover)
                {
                    quota = Math.Max(coverPairsMin, Math.Min(quota, coverPairsMax));
                    required = coverPairsMin;
                }
                else if (category == ModuleCategory.Topology)
                {
                    quota = _landmark ? 0 : quota; // a landmark pair takes the topology budget
                }
                else if (category == ModuleCategory.Approach)
                {
                    quota -= CountModules(ModuleCategory.Approach) / 2; // an approach landmark counts
                }

                else if (_conservative)
                {
                    quota = Math.Min(quota, category == ModuleCategory.Approach ? 1 : 0);
                }

                for (int i = 0; i < quota; i++)
                {
                    if (!PlaceOneIsland(category, i < required))
                    {
                        Notes.Add("no room for " + category);
                    }
                }
            }

            if (CountModules(ModuleCategory.Elevation) < GlobalRules.Arena.ElevationModulesMin
                || CountModules(ModuleCategory.Cover) < GlobalRules.Arena.CoverModulesMin)
            {
                Notes.Add("module composition short");
            }
        }

        private int CountModules(ModuleCategory category)
        {
            int n = 0;
            foreach (ModuleInstance m in _layout.Modules)
            {
                if (_content.Modules.Get(m.ModuleId).Category == category)
                {
                    n++;
                }
            }

            return n;
        }

        private bool PlaceOneIsland(ModuleCategory category, bool required, float minFootprintArea = 0f)
        {
            IReadOnlyList<ModuleGeometry> all = _content.ModulesOf(category);
            var candidates = new List<ModuleGeometry>(all.Count);
            var weights = new List<float>(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                ModuleGeometry g = all[i];
                int sources = CountSources(g);
                if (sources * 2 > _sourceBudget || (_conservative && g.IsVantage) || g.Width * g.Depth < minFootprintArea)
                {
                    continue;
                }

                candidates.Add(g);
                weights.Add(ArenaContent.ModuleWeight(g, _content.Catalogue.Module(g.ModuleId), _biome));
            }

            // A required module keeps trying the whole category: the composition is a validation rule (spec L660).
            int attempts = required ? candidates.Count : IslandAttempts;
            for (int attempt = 0; attempt < attempts && candidates.Count > 0; attempt++)
            {
                int pick = ArenaContent.WeightedPick(_rng, weights);
                ModuleGeometry g = candidates[pick];
                if (TryPlaceIsland(g, category, required))
                {
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

        private struct IslandSite
        {
            public Vec2 Position;
            public int Turns;
            public float Score;
            public bool OnRoute;
        }

        /// <summary>
        /// Samples poses for one module pair and commits the best that keeps the skeleton routes: pieces stay out of
        /// the bastion cores, the firing lane, and the center; open module floor may overlap the bastion exits and
        /// crossover strip, and then the routes are re-measured on the navigation grid before the pair is kept.
        /// </summary>
        private bool TryPlaceIsland(ModuleGeometry g, ModuleCategory category, bool required)
        {
            var sites = new List<IslandSite>();
            float reach = _p.Radius;
            for (int i = 0; i < PlacementTries; i++)
            {
                int q = _rng.NextInt(4);
                Vec2 c = new Vec2(_rng.Range(-reach, reach), _rng.Range(-reach, 0f));
                float noise = _rng.NextFloat() * 0.5f;
                if (!IslandFits(g, c, q, IslandGap, out bool onRoute))
                {
                    continue;
                }

                int conflicts = BulwarkConflicts(g, c, q);
                if (_conflictsStrict && conflicts > 0)
                {
                    continue;
                }

                Rect2 r = LayoutWriter.Footprint(g, c, q);
                float score = Preference(category, r) + noise - (onRoute ? IslandRoutePenalty : 0f) - conflicts * BulwarkConflictPenalty;
                sites.Add(new IslandSite { Position = c, Turns = q, Score = score, OnRoute = onRoute });
            }

            if ((required || category == ModuleCategory.Cover) && sites.Count < IslandSiteChecks)
            {
                // Required modules (spec L660) and cover modules fall back to a scan of every pose on a fine lattice:
                // the smallest arenas leave only slivers of floor beside the skeleton. A required cover module that
                // still finds none may keep only a capsule passage (with margin) to its neighbors.
                ScanIslandSites(g, category, IslandGap, sites);
                if (required && category == ModuleCategory.Cover && sites.Count == 0)
                {
                    ScanIslandSites(g, category, CoverIslandGap, sites);
                }
            }

            // Best first; equal scores keep sampling order (List.Sort is unstable, so sort indices explicitly).
            var order = new List<int>(sites.Count);
            for (int i = 0; i < sites.Count; i++)
            {
                order.Add(i);
            }

            order.Sort((a, b) => sites[a].Score != sites[b].Score ? sites[b].Score.CompareTo(sites[a].Score) : a.CompareTo(b));
            int checks = required ? IslandRequiredSiteChecks : IslandSiteChecks;
            for (int k = 0; k < order.Count && k < checks; k++)
            {
                IslandSite site = sites[order[k]];
                LayoutWriter.Checkpoint mark = _w.Mark();
                _w.PlaceModulePair(g, site.Position, site.Turns);
                if (!RoutesIntact(mark))
                {
                    _w.Rollback(mark);
                    continue;
                }

                foreach (LocalRect cut in g.FloorCutouts)
                {
                    Rect2 world = CutoutRect(cut, site.Position, site.Turns);
                    _cutouts.Add(world);
                    _cutouts.Add(world.Mirrored());
                    _occupied.Add(world);
                    _occupied.Add(world.Mirrored());
                }

                // Later terrain keeps clear of the module's pieces (outcrops test these rectangles); its open floor
                // stays available, as the footprint only spaces modules from each other.
                foreach (YawBox box in LayoutWriter.PieceBoxes(g, site.Position, site.Turns))
                {
                    Rect2 bounds = Rect2.Around(box);
                    _occupied.Add(bounds);
                    _occupied.Add(bounds.Mirrored());
                }

                Rect2 footprint = LayoutWriter.Footprint(g, site.Position, site.Turns);
                _islands.Add(footprint);
                _islands.Add(footprint.Mirrored());
                _sourceBudget -= 2 * CountSources(g);
                return true;
            }

            return false;
        }

        private void ScanIslandSites(ModuleGeometry g, ModuleCategory category, float gap, List<IslandSite> sites)
        {
            float reach = _p.Radius;
            for (float x = -reach; x <= reach; x += IslandScanStep)
            {
                for (float z = -reach; z <= 0f; z += IslandScanStep)
                {
                    for (int q = 0; q < 4; q++)
                    {
                        var c = new Vec2(x, z);
                        if (!IslandFits(g, c, q, gap, out bool onRoute))
                        {
                            continue;
                        }

                        int conflicts = BulwarkConflicts(g, c, q);
                        if (!_conflictsStrict || conflicts == 0)
                        {
                            float score = Preference(category, LayoutWriter.Footprint(g, c, q)) - (onRoute ? IslandRoutePenalty : 0f) - conflicts * BulwarkConflictPenalty;
                            sites.Add(new IslandSite { Position = c, Turns = q, Score = score, OnRoute = onRoute });
                        }
                    }
                }
            }
        }

        private static Rect2 CutoutRect(LocalRect cut, Vec2 position, int turns)
        {
            Vec3 a = LayoutWriter.ToWorld(new Vec3(cut.MinX, 0f, cut.MinZ), position, turns);
            Vec3 b = LayoutWriter.ToWorld(new Vec3(cut.MaxX, 0f, cut.MaxZ), position, turns);
            return new Rect2(a.X, a.Z, b.X, b.Z);
        }

        /// <summary>
        /// Piece-level fit of a module pose. Footprints keep <see cref="IslandGap"/> from other modules and their own
        /// image; every piece keeps the gap from all other solids and the contour strip; no piece, cutout, or patch
        /// enters a hard skeleton zone. onRoute reports pieces standing in the soft zones (exits and crossovers).
        /// </summary>
        private bool IslandFits(ModuleGeometry g, Vec2 c, int q, float gap, out bool onRoute)
        {
            onRoute = false;
            Rect2 r = LayoutWriter.Footprint(g, c, q);
            if (!InAuthoredHalf(r.Center) || r.DistanceTo(r.Mirrored()) < IslandGap || _layout.Boundary.RadialMargin(r.Center.ToXZ(), 0f) < 0f
                || r.Overlaps(_centerFootprint))
            {
                return false;
            }

            for (int i = 0; i < _islands.Count; i++)
            {
                if (r.DistanceTo(_islands[i]) < IslandGap)
                {
                    return false;
                }
            }

            List<YawBox> boxes = LayoutWriter.PieceBoxes(g, c, q);
            for (int i = 0; i < boxes.Count; i++)
            {
                if (g.Pieces[i].Kind == SolidKind.Floor)
                {
                    continue; // sunken floors are tested through their cutouts
                }

                YawBox box = boxes[i];
                for (int k = 0; k < 4; k++)
                {
                    if (_layout.Boundary.RadialMargin(box.FootprintCorner(k).ToXZ(), 0f) < IslandContourMargin)
                    {
                        return false;
                    }
                }

                // Low walkable pieces (ramps and 1 m decks) may cross the lane zone: they block neither the lane band nor the route.
                if (Footprint2D.Overlap(box, ZoneBox(_laneZone)) && !(g.Pieces[i].Walkable && box.Top <= ArenaRules.LaneBandLow - 0.2f))
                {
                    return false;
                }

                if (OverlapsZone(box, _hardZones))
                {
                    return false;
                }

                onRoute |= OverlapsZone(box, _softZones);
                foreach (Solid s in _layout.Solids)
                {
                    if (s.Kind != SolidKind.Floor && Footprint2D.Within(box, s.Box, gap))
                    {
                        return false;
                    }
                }
            }

            foreach (LocalRect cut in g.FloorCutouts)
            {
                Rect2 world = CutoutRect(cut, c, q);
                if (OverlapsAny(world, _hardZones) || OverlapsAny(world, _softZones) || world.Overlaps(_laneZone))
                {
                    return false;
                }
            }

            foreach (ModulePatch patch in g.Patches)
            {
                Vec3 p = LayoutWriter.ToWorld(patch.Center.ToXZ(), c, q);
                float extent = patch.Radius > 0f ? patch.Radius : patch.HalfSize.Length();
                Rect2 bounds = Rect2.FromCenter(new Vec2(p.X, p.Z), extent * 2f, extent * 2f);
                if (OverlapsAny(bounds, _hardZones) || !BoundaryBuilder.ContainsRect(_layout.Boundary, bounds, 0f))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool OverlapsZone(YawBox box, List<Rect2> zones)
        {
            for (int i = 0; i < zones.Count; i++)
            {
                if (Footprint2D.Overlap(box, ZoneBox(zones[i])))
                {
                    return true;
                }
            }

            return false;
        }

        private static YawBox ZoneBox(Rect2 k)
        {
            return new YawBox(new Vec3(k.Center.X, 1f, k.Center.Y), new Vec3(k.Width * 0.5f, 1f, k.Depth * 0.5f), 0f);
        }

        private static bool OverlapsAny(Rect2 r, List<Rect2> zones)
        {
            for (int i = 0; i < zones.Count; i++)
            {
                if (r.Overlaps(zones[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The skeleton guarantees on the bare-terrain grid (spec L308-L312): a 2.5 m route from the spawn to the final
        /// floor and to the other spawn, two disjoint approaches, the spawn path band, and the central reach window.
        /// Mirroring makes side 0 sufficient.
        /// </summary>
        private bool[] RouteConditions(bool withProps = false)
        {
            var result = new bool[5];
            ArenaNav nav = withProps ? ArenaNav.Build(_layout) : ArenaNav.Build(_layout, NoProps);
            var survey = new ArenaSurvey(_layout, nav);
            int s0 = survey.SpawnNode(0);
            int s1 = survey.SpawnNode(1);
            if (s0 < 0 || s1 < 0)
            {
                return result;
            }

            // The witness paths: one route to the final floor and one to the other spawn, the two approaches, the
            // shortest spawn path, and the path to the nearest useful central cover.
            var witness = new List<int>();
            var parents = new int[nav.NodeCount];
            float[] route = nav.Distances(new[] { s0 }, NavQuery.Route, parents);
            int floorNode = ArenaSurvey.ArgMin(route, survey.FinalFloorNodes);
            result[0] = floorNode >= 0 && !float.IsPositiveInfinity(route[floorNode]);
            result[1] = !float.IsPositiveInfinity(route[s1]);
            if (result[0])
            {
                witness.AddRange(ArenaNav.TracePath(parents, floorNode));
            }

            if (result[1])
            {
                witness.AddRange(ArenaNav.TracePath(parents, s1));
            }

            List<List<int>> approaches = survey.DisjointApproaches(0);
            result[2] = approaches.Count >= GlobalRules.Arena.MinCrossoverRoutes;
            foreach (List<int> approach in approaches)
            {
                witness.AddRange(approach);
            }

            float[] walk = nav.Distances(new[] { s0 }, NavQuery.Capsule, parents);
            ArenaRules.SpawnPathBand(_p.Diameter, out float pathMin, out float pathMax);
            float path = walk[s1];
            result[3] = path >= pathMin && path <= pathMax;
            int coverNode = ArenaSurvey.ArgMin(walk, survey.UsefulCoverNodes(0));
            float reach = coverNode < 0 ? float.PositiveInfinity : walk[coverNode] / ArenaRules.CentralReachReferenceSpeed;
            result[4] = reach >= GlobalRules.Arena.CentralReachMinSec && reach <= GlobalRules.Arena.CentralReachMaxSec;
            if (!float.IsPositiveInfinity(path))
            {
                witness.AddRange(ArenaNav.TracePath(parents, s1));
            }

            if (coverNode >= 0 && !float.IsPositiveInfinity(walk[coverNode]))
            {
                witness.AddRange(ArenaNav.TracePath(parents, coverNode));
            }

            _witness = new List<Vec2>(witness.Count);
            foreach (int node in witness)
            {
                _witness.Add(nav.NodePosition(node).XZ());
            }

            _witnessWithProps = withProps;
            return result;
        }

        /// <summary>
        /// True when something added since the mark could change a guarantee: a walkable piece (it may open a
        /// shortcut), anything near the center (useful central cover), or a solid or blocking prop within
        /// <see cref="WitnessMargin"/> of a witness path. Anything else leaves every witness path and its clearance
        /// unchanged, so the guarantees hold without a new measurement.
        /// </summary>
        private bool TouchesWitness(LayoutWriter.Checkpoint since, bool withProps)
        {
            if (_witness == null || _witnessWithProps != withProps)
            {
                return true;
            }

            for (int i = since.Solids; i < _layout.Solids.Count; i++)
            {
                Solid s = _layout.Solids[i];
                if (s.Kind == SolidKind.Floor || s.Box.Bottom > ArenaRules.Headroom)
                {
                    continue;
                }

                if (s.Walkable || NearWitness(s.Box))
                {
                    return true;
                }
            }

            for (int i = since.Props; i < _layout.Props.Count; i++)
            {
                if (withProps && _layout.Props[i].HasCollision && NearWitness(_layout.Props[i].CollisionBox))
                {
                    return true;
                }
            }

            return false;
        }

        private bool NearWitness(YawBox box)
        {
            if (ArenaSurvey.FootprintDistance(box, Vec2.Zero) < WitnessCenterRadius)
            {
                return true;
            }

            Rect2 near = Rect2.Around(box).Expanded(WitnessMargin);
            for (int i = 0; i < _witness.Count; i++)
            {
                Vec2 w = _witness[i];
                if (near.Contains(w) && ArenaSurvey.FootprintDistance(box, w) < WitnessMargin)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when no guarantee that held at the baseline has been lost by what was added since the mark. Additions
        /// clear of the witness paths need no new measurement (see <see cref="TouchesWitness"/>).
        /// </summary>
        private bool RoutesIntact(LayoutWriter.Checkpoint since, bool withProps = false)
        {
            if (!TouchesWitness(since, withProps))
            {
                return true;
            }

            List<Vec2> previous = _witness;
            bool previousWithProps = _witnessWithProps;
            bool[] now = RouteConditions(withProps);
            for (int i = 0; i < now.Length; i++)
            {
                if (_routeBaseline[i] && !now[i])
                {
                    _witness = previous; // the caller undoes the addition
                    _witnessWithProps = previousWithProps;
                    return false;
                }
            }

            return true;
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
            if (!staggered && inner - BulwarkRowSpacing >= BulwarkInnermostRadius)
            {
                // No room outside: the gate covers stand inside the gated row instead, so the floor beyond the
                // bulwark still has ways in every few meters (spec L676 contraction routes).
                staggered = true;
                outer = inner - BulwarkRowSpacing;
            }

            if (!staggered)
            {
                // No room for a second row: one continuous arc of short pieces at the inner radius.
                float r = Math.Min(inner, BulwarkRadius);
                float stepDeg = BulwarkPieceLength / r * CMath.Rad2Deg;
                for (float a = a0; a < a1; a += stepDeg)
                {
                    PlaceArc(a, Math.Min(a + stepDeg, a1), r);
                }

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
            // Outward from the planned radius first; where a module holds that stretch, inward toward the center
            // (a nearer wall only shortens the lanes it closes).
            var radii = new List<float>();
            for (float r = radius; r <= BulwarkRadius + 0.51f; r += 0.25f)
            {
                radii.Add(r);
            }

            for (float r = radius - 0.25f; r >= BulwarkInnermostRadius; r -= 0.25f)
            {
                radii.Add(r);
            }

            foreach (float r in radii)
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

        /// <summary>
        /// Skeleton lane posts (spec L310). Some lanes run most of their length through the skeleton zones, where no
        /// terrain may stand, so the later filler cannot shorten them enough. On the bare skeleton each such lane gets a
        /// short anchored post across it at the first point outside the zones nearest its middle.
        /// </summary>
        private void PlaceSkeletonLanePosts()
        {
            if (_p.Diameter <= GlobalRules.Arena.DiameterSmall)
            {
                return;
            }

            for (int pass = 0; pass < SkeletonPostPasses; pass++)
            {
                ArenaNav nav = ArenaNav.Build(_layout, NoProps);
                List<FiringLane> lanes = new LaneMeter(nav, _layout).LongerThan(BulwarkLaneTarget);
                var posts = new List<YawBox>();
                for (int i = 0; i < lanes.Count; i++)
                {
                    FiringLane lane = lanes[i];
                    if (InterruptedBy(lane, posts) || KeepOutShare(lane) < SkeletonPostMinZoneRun)
                    {
                        continue;
                    }

                    if (TrySkeletonPost(lane, out YawBox post))
                    {
                        posts.Add(post);
                        posts.Add(MirrorMath.Box(post));
                    }
                }

                if (posts.Count == 0)
                {
                    return;
                }
            }
        }

        /// <summary>Meters of a lane that lie inside the skeleton zones.</summary>
        private float KeepOutShare(FiringLane lane)
        {
            float inside = 0f;
            float length = lane.Length;
            for (float s = 0f; s <= length; s += 0.25f)
            {
                if (InKeepOut(Vec2.Lerp(lane.A, lane.B, s / Math.Max(length, 1e-3f))))
                {
                    inside += 0.25f;
                }
            }

            return inside;
        }

        private bool InKeepOut(Vec2 p)
        {
            for (int k = 0; k < _keepOut.Count; k++)
            {
                if (_keepOut[k].Contains(p))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TrySkeletonPost(FiringLane lane, out YawBox placed)
        {
            placed = default(YawBox);
            float length = lane.Length;
            Vec2 dir = (lane.B - lane.A).Normalized();
            for (float offset = 0f; offset <= length * 0.5f; offset += 0.25f)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    float at = length * 0.5f + s * offset;
                    Vec2 p = lane.A + dir * at;
                    if (InKeepOut(p))
                    {
                        continue;
                    }

                    float yaw = CMath.YawOf(dir);
                    if (!InAuthoredHalf(p))
                    {
                        p = MirrorMath.Point(p);
                        yaw = MirrorMath.Yaw(yaw);
                    }

                    var box = new YawBox(new Vec3(p.X, ArenaRules.TallWallHeight * 0.5f, p.Y),
                        new Vec3(SkeletonPostLength * 0.5f, ArenaRules.TallWallHeight * 0.5f, BulwarkThickness * 0.5f), yaw + 90f);
                    if (BulwarkFits(box, SkeletonPostGap, ArenaRules.ContourStripWidth + 1f))
                    {
                        Solid wall = _w.AddMirroredPair(_w.NewSolid(SolidKind.Wall, box, ArenaMaterials.Rock, 0f));
                        _bulwarks.Add(wall.Id);
                        _bulwarks.Add(wall.MirrorId);
                        Rect2 bounds = Rect2.Around(box);
                        _occupied.Add(bounds);
                        _occupied.Add(bounds.Mirrored());
                        placed = box;
                        return true;
                    }
                }
            }

            return false;
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

        private bool BulwarkFits(YawBox box, float solidGap = IslandGap, float contourMargin = ArenaRules.ContourKeepInMargin)
        {
            Rect2 bounds = Rect2.Around(box);
            if (bounds.MaxZ > -IslandGap * 0.5f || Footprint2D.Distance(box, MirrorMath.Box(box)) < IslandGap)
            {
                return false;
            }

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
                    && Footprint2D.Within(box, s.Box, solidGap))
                {
                    return false;
                }
            }


            for (int i = 0; i < 4; i++)
            {
                if (_layout.Boundary.RadialMargin(box.FootprintCorner(i).ToXZ(), 0f) < contourMargin)
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
            bool routeRocksTried = false;
            bool breakLanes = true;
            bool fillFloor = true;
            _routeBaseline = RouteConditions(); // after the islands and outcrops
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
                AssemblyMark laneMark = MarkAll();
                for (int i = 0; i < lanes.Count && breakLanes; i++)
                {
                    if (InterruptedBy(lanes[i], placedBoxes))
                    {
                        continue;
                    }

                    if (TryBreakLane(lanes[i], out YawBox rock) || TryBreakLaneInRouteZone(lanes[i], out rock))
                    {
                        placedBoxes.Add(rock);
                        placedBoxes.Add(MirrorMath.Box(rock));
                        placed++;
                    }
                    else if (i == 0)
                    {
                        Notes.Add("lane of " + lanes[i].Length + " m could not be broken");
                    }
                }

                // Each stage must keep the skeleton guarantees (spec L308-L312); a stage that loses one is undone and
                // not run again, and validation judges what is left.
                if (placed > 0 && !StageKeepsRoutes(laneMark))
                {
                    placed = 0;
                    breakLanes = false;
                }

                if (missing > 0f && fillFloor)
                {
                    AssemblyMark fillMark = MarkAll();
                    int filled = FillOpenFloor(nav, ref missing);
                    filled += Accrete(ref missing);
                    if (filled > 0 && !StageKeepsRoutes(fillMark))
                    {
                        filled = 0;
                        fillFloor = false;
                    }

                    placed += filled;
                }

                if (missing > 0f && placed == 0 && !routeRocksTried)
                {
                    routeRocksTried = true;
                    placed += FillRouteZones(nav, ref missing);
                }

                if (placed == 0)
                {
                    break;
                }

                nav = ArenaNav.Build(_layout, NoProps);
            }

            return nav;
        }

        /// <summary>Generator state at a moment, so a whole terrain stage can be undone.</summary>
        private struct AssemblyMark
        {
            public LayoutWriter.Checkpoint Writer;
            public int Occupied;
            public HashSet<int> Removable;
            public HashSet<int> Attached;
            public Dictionary<int, int> Attachments;
        }

        private AssemblyMark MarkAll()
        {
            return new AssemblyMark
            {
                Writer = _w.Mark(),
                Occupied = _occupied.Count,
                Removable = new HashSet<int>(_removable),
                Attached = new HashSet<int>(_attached),
                Attachments = new Dictionary<int, int>(_attachments),
            };
        }

        /// <summary>
        /// True when the stage since the mark keeps every skeleton guarantee, after withdrawing at most
        /// <see cref="StageCulprits"/> of its rock pairs: each culprit is the first pair whose addition loses a
        /// guarantee (a binary search over the stage's pairs in placement order). Otherwise the stage is undone.
        /// </summary>
        private bool StageKeepsRoutes(AssemblyMark mark)
        {
            if (RoutesIntact(mark.Writer))
            {
                return true;
            }

            // The search below measures every state it tries (and restores the witness when it gives up).
            List<Vec2> witness = _witness;
            bool witnessWithProps = _witnessWithProps;
            _witness = null;

            int baseCount = mark.Writer.Solids;
            List<Solid> stage = _layout.Solids.GetRange(baseCount, _layout.Solids.Count - baseCount);
            int pairs = stage.Count / 2;
            var keep = new bool[pairs];
            for (int i = 0; i < pairs; i++)
            {
                keep[i] = true;
            }

            for (int round = 0; round < StageCulprits && pairs > 0; round++)
            {
                int lo = 0;
                int hi = pairs; // the kept pairs before lo keep the guarantees; those before hi do not
                while (hi - lo > 1)
                {
                    int mid = (lo + hi) / 2;
                    ApplyStage(baseCount, stage, keep, mid);
                    if (RoutesIntact(mark.Writer))
                    {
                        lo = mid;
                    }
                    else
                    {
                        hi = mid;
                    }
                }

                keep[hi - 1] = false;
                ApplyStage(baseCount, stage, keep, pairs);
                if (RoutesIntact(mark.Writer))
                {
                    return true;
                }

                _witness = null;
            }

            _witness = witness;
            _witnessWithProps = witnessWithProps;
            _w.Rollback(mark.Writer);
            _occupied.RemoveRange(mark.Occupied, _occupied.Count - mark.Occupied);
            _removable.Clear();
            _removable.UnionWith(mark.Removable);
            _attached.Clear();
            _attached.UnionWith(mark.Attached);
            _attachments.Clear();
            foreach (KeyValuePair<int, int> kv in mark.Attachments)
            {
                _attachments[kv.Key] = kv.Value;
            }

            return false;
        }

        /// <summary>Rewrites the layout's solids as the base plus the kept stage pairs among the first count pairs.</summary>
        private void ApplyStage(int baseCount, List<Solid> stage, bool[] keep, int count)
        {
            _layout.Solids.RemoveRange(baseCount, _layout.Solids.Count - baseCount);
            for (int i = 0; i < count; i++)
            {
                if (keep[i])
                {
                    _layout.Solids.Add(stage[2 * i]);
                    _layout.Solids.Add(stage[2 * i + 1]);
                }
            }
        }

        /// <summary>
        /// When the open floor is full but cover is still short (the small arenas), rocks may stand in the bastion exits
        /// and crossover strips with only the filler gap around them, each kept only if the skeleton guarantees
        /// survive on the navigation grid (spec L310 cover ratio, L308-L312 routes).
        /// </summary>
        private int FillRouteZones(ArenaNav nav, ref float missing)
        {
            var spots = new List<FillerSpot>();
            for (int node = 0; node < nav.NodeCount; node++)
            {
                float clearance = nav.Clearance(node);
                Vec3 p = nav.NodePosition(node);
                Vec2 q = p.XZ();
                if (clearance < FillerGap + FillerMinHalfDiagonal || MathF.Abs(p.Y) > 0.05f || !InAuthoredHalf(q) || !InAny(q, _softZones))
                {
                    continue;
                }

                float room = Math.Min(clearance - FillerGap, FillerMaxHalfDiagonal);
                Rect2 r = Rect2.FromCenter(q, 2f * room, 2f * room);
                if (OverlapsAny(r, _hardZones) || r.Overlaps(_laneZone) || _layout.Boundary.RadialMargin(p, 0f) < room + ArenaRules.ContourStripWidth
                    || OnPatch(q, room) || NearDeck(q, room + 0.5f))
                {
                    continue;
                }

                spots.Add(new FillerSpot { Node = node, Room = room });
            }

            spots.Sort((x, y) =>
            {
                int c = y.Room.CompareTo(x.Room);
                return c != 0 ? c : x.Node.CompareTo(y.Node);
            });

            _routeBaseline = RouteConditions();
            var chosen = new List<Vec3>();
            for (int i = 0; i < spots.Count && missing > 0f && _fillRouteChecks < FillRouteChecks; i++)
            {
                Vec3 p = nav.NodePosition(spots[i].Node);
                if (!FarFromChosen(p, spots[i].Room, chosen))
                {
                    continue;
                }

                _fillRouteChecks++;
                LayoutWriter.Checkpoint mark = _w.Mark();
                int occupied = _occupied.Count;
                float area = PlaceRock(p.XZ(), spots[i].Room, _rng.Range(0f, 180f));
                if (area <= 0f)
                {
                    continue;
                }

                if (!RoutesIntact(mark))
                {
                    _w.Rollback(mark);
                    _occupied.RemoveRange(occupied, _occupied.Count - occupied);
                    _removable.RemoveWhere(id => id >= mark.NextSolidId);
                    continue;
                }

                missing -= area;
                chosen.Add(new Vec3(p.X, nav.Clearance(spots[i].Node), p.Z));
                for (int id = mark.NextSolidId; id < mark.NextSolidId + 2; id++)
                {
                    _attached.Add(id); // nothing grows from a rock standing in a route zone
                }
            }

            return chosen.Count;
        }

        /// <summary>
        /// A post across a long lane where it crosses a bastion exit or crossover strip (the only floor
        /// some lanes cross), kept only if the skeleton guarantees survive on the navigation grid.
        /// </summary>
        private bool TryBreakLaneInRouteZone(FiringLane lane, out YawBox placed)
        {
            placed = default(YawBox);
            Vec2 dir = (lane.B - lane.A).Normalized();
            Vec2 across = new Vec2(dir.Y, -dir.X);
            // Interior points every 0.5 m, from the middle of the lane outward.
            var fractions = new List<float> { 0.5f };
            float step = 0.5f / Math.Max(1f, lane.Length);
            for (float f = 0.5f - step; f >= 0.03f; f -= step)
            {
                fractions.Add(f);
                fractions.Add(1f - f);
            }

            var options = new List<YawBox>(3);
            for (int i = 0; i < fractions.Count && _laneRouteChecks < LaneRouteChecks; i++)
            {
                Vec2 p = Vec2.Lerp(lane.A, lane.B, fractions[i]);
                Vec2 side = across;
                if (!MirrorMath.IsSideZeroHalf(p))
                {
                    p = MirrorMath.Point(p);
                    side = -across;
                }

                if (!InAny(p, _softZones))
                {
                    continue;
                }

                // A spur from the nearest obstacle on either side, else a post across the lane.
                options.Clear();
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    if (SpurBox(p, side * sgn, out YawBox spur))
                    {
                        options.Add(spur);
                    }
                }

                float height = ArenaRules.TallWallHeight;
                options.Add(new YawBox(new Vec3(p.X, height * 0.5f, p.Y), new Vec3(SpurThickness * 0.5f, height * 0.5f, 0.6f), CMath.YawOf(side)));
                for (int o = 0; o < options.Count && _laneRouteChecks < LaneRouteChecks; o++)
                {
                    // In a route zone a breaker may bridge two obstacles: it is then kept only if the contracting boundary
                    // strands no floor behind it (spec L676), besides the route guarantees.
                    YawBox post = options[o];
                    if (AttachedRockFits(post, out int touched, LaneBreakerAttachLimit, inRouteZone: true, touchLimit: 2)
                        && TryRouteRock(post, touched, checkContraction: _lastTouching > 1))
                    {
                        placed = post;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>True when some accessible cell loses its route to the final floor as the boundary contracts.</summary>
        private bool StrandsFloor()
        {
            ArenaNav nav = ArenaNav.Build(_layout, NoProps);
            var survey = new ArenaSurvey(_layout, nav);
            return survey.SpawnNode(0) >= 0 && survey.FinalFloorNodes.Count > 0 && survey.FindContractionFailure(out float _, out int _);
        }

        /// <summary>Writes a rock pair in a route zone and keeps it only if the skeleton guarantees survive.</summary>
        private bool TryRouteRock(YawBox post, int touched, bool checkContraction = false)
        {
            {
                _routeBaseline = _routeBaseline ?? RouteConditions();
                _laneRouteChecks++;
                LayoutWriter.Checkpoint mark = _w.Mark();
                Solid rock = _w.AddMirroredPair(_w.NewSolid(SolidKind.Wall, post, ArenaMaterials.Rock, 0f));
                if (!RoutesIntact(mark) || (checkContraction && StrandsFloor()))
                {
                    _w.Rollback(mark);
                    return false;
                }

                _removable.Add(rock.Id);
                _removable.Add(rock.MirrorId);
                _attached.Add(rock.Id); // nothing grows from a rock standing in a route zone
                _attached.Add(rock.MirrorId);
                if (touched >= 0)
                {
                    _attachments[touched] = AttachmentsOf(touched) + 1;
                    Solid touchedSolid = _layout.FindSolid(touched);
                    if (touchedSolid != null)
                    {
                        _attachments[touchedSolid.MirrorId] = AttachmentsOf(touchedSolid.MirrorId) + 1;
                    }
                }
                Rect2 bounds = Rect2.Around(post);
                _occupied.Add(bounds);
                _occupied.Add(bounds.Mirrored());
                return true;
            }
        }

        private static bool InAny(Vec2 p, List<Rect2> zones)
        {
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i].Contains(p))
                {
                    return true;
                }
            }

            return false;
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
                if (!FarFromChosen(p, clearance, chosen) || NearDeck(p.XZ(), candidates[i].Room + 0.5f))
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
            for (float f = 0.5f - step; f >= 0.03f; f -= step)
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

                // A free-standing post across the lane first: touching nothing, it can never close a cup that the
                // contracting boundary would strand (spec L676). Otherwise a spur from the nearest obstacle, or a post
                // against one.
                float height = _rng.Range(2.2f, 3.0f);
                var post = new YawBox(new Vec3(p.X, height * 0.5f, p.Y), new Vec3(SpurThickness * 0.5f, height * 0.5f, 0.6f), CMath.YawOf(side));
                bool postFits = AttachedRockFits(post, out int touched, LaneBreakerAttachLimit);
                if (postFits && touched < 0)
                {
                    AddAttachedPair(post, touched);
                    placed = post;
                    return true;
                }

                for (int s = -1; s <= 1; s += 2)
                {
                    if (TrySpur(p, side * s, along, out placed))
                    {
                        return true;
                    }
                }

                if (postFits)
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
            if (!SpurBox(p, toward, out YawBox box) || !AttachedRockFits(box, out int touched, LaneBreakerAttachLimit))
            {
                return false;
            }

            AddAttachedPair(box, touched);
            placed = box;
            return true;
        }

        /// <summary>The spur from the nearest lane-band obstacle within reach of p along toward, if any.</summary>
        private bool SpurBox(Vec2 p, Vec2 toward, out YawBox box)
        {
            box = default(YawBox);
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
            box = new YawBox(new Vec3(mid.X, height * 0.5f, mid.Y), new Vec3(SpurThickness * 0.5f, height * 0.5f, (end - start) * 0.5f), CMath.YawOf(toward));
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
                    && !_attached.Contains(s.Id) && AttachmentsOf(s.Id) == 0)
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
        private int AttachmentsOf(int solidId) => _attachments.TryGetValue(solidId, out int n) ? n : 0;

        private bool AttachedRockFits(YawBox box, out int touchedId, int attachLimit = 1, bool inRouteZone = false, int touchLimit = 1)
        {
            touchedId = -1;
            Rect2 bounds = Rect2.Around(box);
            if (!InAuthoredHalf(box.Center.XZ()))
            {
                return false;
            }

            // A rock in a route zone (checked on the navigation grid by the caller) still keeps out of the hard zones.
            if (inRouteZone
                ? OverlapsZone(box, _hardZones) || Footprint2D.Overlap(box, ZoneBox(_laneZone))
                : OverlapsKeepOut(bounds))
            {
                return false;
            }

            for (int i = 0; i < 4; i++)
            {
                if (_layout.Boundary.RadialMargin(box.FootprintCorner(i).ToXZ(), 0f) < ArenaRules.ContourStripWidth)
                {
                    return false;
                }
            }

            if (Footprint2D.Distance(box, MirrorMath.Box(box)) < FillerGap)
            {
                return false;
            }

            int touching = 0;
            int touchedModule = -1;
            foreach (Solid s in _layout.Solids)
            {
                if (s.Kind == SolidKind.Floor || s.Box.Bottom > ArenaRules.Headroom)
                {
                    continue;
                }

                if (!Footprint2D.Within(box, s.Box, FillerGap))
                {
                    continue;
                }

                float d = Footprint2D.Distance(box, s.Box);

                // One attachment per original obstacle, never to another attached rock: an obstacle grows into an L or
                // T at most, never a cup that could trap floor as the boundary contracts (spec L676). The pieces of
                // one module instance are one obstacle. A rock may stand against a deck or ramp side but never on or into it.
                if (s.Walkable && d <= 0.05f)
                {
                    return false;
                }

                if (s.ModuleIndex >= 0 && s.ModuleIndex == touchedModule)
                {
                    continue;
                }

                if (++touching > touchLimit || _attached.Contains(s.Id) || AttachmentsOf(s.Id) >= attachLimit)
                {
                    return false;
                }

                touchedId = s.Id;
                touchedModule = s.ModuleIndex;
            }

            _lastTouching = touching;

            foreach (MaterialPatch m in _layout.Materials)
            {
                if (BoxTouchesPatch(box, m, RockPatchMargin))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>True when a footprint comes within the margin of a material patch's actual shape.</summary>
        private static bool BoxTouchesPatch(YawBox box, MaterialPatch m, float margin)
        {
            if (m.IsCircle)
            {
                return Vec2.Distance(box.ClosestPointXZ(m.Center), m.Center) < m.Radius + margin;
            }

            var patch = new YawBox(new Vec3(m.Center.X, 0.5f, m.Center.Y), new Vec3(m.HalfSize.X, 0.5f, m.HalfSize.Y), m.YawDeg);
            return Footprint2D.Distance(box, patch) < margin;
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
                _attachments[touchedId] = AttachmentsOf(touchedId) + 1;
                Solid touched = _layout.FindSolid(touchedId);
                if (touched != null)
                {
                    _attachments[touched.MirrorId] = AttachmentsOf(touched.MirrorId) + 1;
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

                if (OnPatch(p.XZ(), room))
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

        /// <summary>Bastion cores (both sides) and the firing lane zone: nothing generated stands there.</summary>
        private List<Rect2> ProtectedZones()
        {
            float laneHalf = _p.LaneHalfLength > 0f ? _p.LaneHalfLength : _p.Radius + 2f;
            Rect2 core = _bastion.Core();
            return new List<Rect2> { core, core.Mirrored(), new Rect2(-laneHalf, -LaneZoneHalf, laneHalf, LaneZoneHalf) };
        }

        /// <summary>True when a disc would cover part of a material patch (beds, pools, and tracks stay open floor).</summary>
        private bool OnPatch(Vec2 center, float radius)
        {
            foreach (MaterialPatch m in _layout.Materials)
            {
                if (m.SurfaceY == 0f && DistanceToPatch(m, center) < radius)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Horizontal distance from a point to a material patch's shape (0 inside).</summary>
        private static float DistanceToPatch(MaterialPatch m, Vec2 p)
        {
            if (m.IsCircle)
            {
                return Math.Max(0f, Vec2.Distance(m.Center, p) - m.Radius);
            }

            Vec2 local = (p - m.Center).RotatedByYaw(-m.YawDeg);
            float dx = Math.Max(0f, Math.Abs(local.X) - m.HalfSize.X);
            float dz = Math.Max(0f, Math.Abs(local.Y) - m.HalfSize.Y);
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>True when a walkable deck, ramp, or bridge lies within the distance (rocks never block a climb).</summary>
        private bool NearDeck(Vec2 p, float distance)
        {
            foreach (Solid s in _layout.Solids)
            {
                if (s.Walkable && s.Kind != SolidKind.Floor && ArenaSurvey.FootprintDistance(s.Box, p) < distance)
                {
                    return true;
                }
            }

            return false;
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

        /// <summary>
        /// Contraction repair (spec L676): while some accessible cell would lose its route to the final floor as the
        /// boundary contracts, change the filler nearest that cell: remove a rock or prop pair, or trim a rock that
        /// must stay. A change is kept only if the stranded cell regains its route at the failing time and no lane
        /// reopens over the limit, so repairs never strip cover that does not help. Skeleton terrain, modules, and
        /// biome sources stay; anything left failing is caught by validation and the seed is retried.
        /// </summary>
        private void RepairContraction()
        {
            _repairNav = null;
            for (int iteration = 0; iteration < ContractionRepairs; iteration++)
            {
                ArenaNav nav = _repairNav ?? ArenaNav.Build(_layout);
                _repairNav = null;
                var survey = new ArenaSurvey(_layout, nav);
                if (survey.SpawnNode(0) < 0 || survey.FinalFloorNodes.Count == 0 || !survey.FindContractionFailure(out float time, out int node))
                {
                    return;
                }

                // Candidates nearest first: removable rocks, bulwark pieces, and props within reach of the stranded cell.
                var stranded = new StrandedCell { Position = nav.NodePosition(node), TimeSec = time };
                Vec2 cell = stranded.Position.XZ();
                var candidates = new List<KeyValuePair<float, int>>(); // distance, solid index or ~prop index
                for (int i = 0; i < _layout.Solids.Count; i++)
                {
                    Solid s = _layout.Solids[i];
                    float d = ArenaSurvey.FootprintDistance(s.Box, cell);
                    if ((_removable.Contains(s.Id) || _bulwarks.Contains(s.Id)) && d < ContractionRepairReach)
                    {
                        candidates.Add(new KeyValuePair<float, int>(d, i));
                    }
                }

                for (int i = 0; i < _layout.Props.Count; i++)
                {
                    float d = Vec2.Distance(_layout.Props[i].Position.XZ(), cell);
                    if (d < ContractionRepairReach)
                    {
                        candidates.Add(new KeyValuePair<float, int>(d, ~i));
                    }
                }

                candidates.Sort((a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.Value.CompareTo(b.Value));
                bool repaired = false;
                Solid opener = null;
                ArenaNav openerNav = null;
                for (int c = 0; c < candidates.Count && c < ContractionRepairCandidates && !repaired; c++)
                {
                    int index = candidates[c].Value;
                    if (index < 0)
                    {
                        repaired = TryRemoveProp(_layout.Props[~index], stranded);
                        continue;
                    }

                    Solid rock = _layout.Solids[index];
                    repaired = TryRemoveRock(rock, nav, stranded, out ArenaNav removedNav);
                    if (!repaired && removedNav != null && opener == null)
                    {
                        opener = rock;
                        openerNav = removedNav;
                    }
                }

                // A rock that must stay to keep a lane closed can still give way at the pinch: trim its face toward
                // the stranded cell so the gap beside it becomes a capsule passage.
                for (int c = 0; c < candidates.Count && c < ContractionTrimCandidates && !repaired; c++)
                {
                    if (candidates[c].Value >= 0)
                    {
                        repaired = TryTrimRock(_layout.Solids[candidates[c].Value], stranded);
                    }
                }

                // No single change rescues the cell: a pinch between two rocks may need both gone. Remove the nearest
                // rock whose removal keeps every lane closed and look again.
                if (!repaired && opener != null)
                {
                    RemoveRock(opener);
                    _repairNav = openerNav; // built for exactly this removal
                    repaired = true;
                }

                if (!repaired)
                {
                    Notes.Add("contraction failure without a removable obstacle nearby");
                    return;
                }
            }
        }

        /// <summary>A cell found cut off from the final floor, and when.</summary>
        private struct StrandedCell
        {
            public Vec3 Position;
            public float TimeSec;
        }

        /// <summary>True when the stranded cell keeps a route to the final floor at its failing time on this navigation.</summary>
        private bool Rescues(ArenaNav nav, StrandedCell stranded)
        {
            var survey = new ArenaSurvey(_layout, nav);
            int node = nav.FindNode(stranded.Position, ArenaRules.FloorLevelTolerance, ArenaRules.NavCellSize, ArenaRules.CapsuleClearance);
            return node < 0 || survey.CanReachFinalFloorAt(stranded.TimeSec)[node];
        }

        /// <summary>Removes a prop pair if that rescues the stranded cell (otherwise both go back where they were).</summary>
        private bool TryRemoveProp(PropPlacement prop, StrandedCell stranded)
        {
            PropPlacement image = _layout.FindProp(prop.MirrorId);
            int a = _layout.Props.IndexOf(prop);
            int b = image == null || image == prop ? -1 : _layout.Props.IndexOf(image);
            _layout.Props.Remove(prop);
            if (b >= 0)
            {
                _layout.Props.Remove(image);
            }

            ArenaNav nav = ArenaNav.Build(_layout);
            if (!Rescues(nav, stranded))
            {
                ReinsertPair(_layout.Props, prop, a, image, b);
                return false;
            }

            _repairNav = nav; // the next repair iteration measures this layout
            return true;
        }

        /// <summary>
        /// Shortens a rock pair by <see cref="RockTrimDepth"/> on the face toward a stranded cell; kept only if that
        /// rescues the cell without reopening a firing lane over the limit (otherwise both boxes are restored). The
        /// authored (side 0) rock is trimmed and its image rewritten, so the pair stays an exact half turn.
        /// </summary>
        private bool TryTrimRock(Solid rock, StrandedCell stranded)
        {
            Solid image = _layout.FindSolid(rock.MirrorId);
            if (image == null || image == rock || rock.IsRamp || rock.Walkable)
            {
                return false;
            }

            Solid authored = rock.Id < image.Id ? rock : image;
            Solid other = authored == rock ? image : rock;
            Vec2 cell = stranded.Position.XZ();
            Vec2 target = authored == rock ? cell : MirrorMath.Point(cell);
            YawBox box = authored.Box;
            Vec3 h = box.HalfExtents;
            Vec3 local = box.ToLocal(new Vec3(target.X, box.Center.Y, target.Y));
            bool alongX = MathF.Abs(local.X) / h.X >= MathF.Abs(local.Z) / h.Z;
            float half = alongX ? h.X : h.Z;
            float trim = Math.Min(RockTrimDepth, 2f * half - RockTrimMinSize);
            if (trim < ArenaRules.NavCellSize)
            {
                return false;
            }

            float away = -0.5f * trim * ((alongX ? local.X : local.Z) >= 0f ? 1f : -1f);
            Vec3 center = box.ToWorld(alongX ? new Vec3(away, 0f, 0f) : new Vec3(0f, 0f, away));
            Vec3 extents = alongX ? new Vec3(h.X - 0.5f * trim, h.Y, h.Z) : new Vec3(h.X, h.Y, h.Z - 0.5f * trim);
            YawBox originalAuthored = authored.Box;
            YawBox originalOther = other.Box;
            authored.Box = new YawBox(center, extents, box.YawDeg);
            other.Box = MirrorMath.Box(authored.Box);

            ArenaNav nav = ArenaNav.Build(_layout);
            if (!Rescues(nav, stranded) || ReopensLane(nav, originalAuthored))
            {
                authored.Box = originalAuthored;
                other.Box = originalOther;
                return false;
            }

            _repairNav = nav; // the next repair iteration measures this layout
            return true;
        }

        /// <summary>
        /// Removes a rock pair if that rescues the stranded cell without reopening a firing lane over the limit
        /// (otherwise it is put back). The current navigation, with the pair ignored as blockers, gives a lower bound
        /// on the reopened lanes, so most refusals need no rebuild. A removal that keeps every lane closed but does
        /// not rescue the cell is undone too; its navigation comes back in openedNav so the caller can still apply it.
        /// </summary>
        private bool TryRemoveRock(Solid rock, ArenaNav current, StrandedCell stranded, out ArenaNav openedNav)
        {
            openedNav = null;
            if (ReopensLane(new LaneMeter(current, _layout, rock.Id, rock.MirrorId), rock.Box))
            {
                return false;
            }

            Solid image = _layout.FindSolid(rock.MirrorId);
            int a = _layout.Solids.IndexOf(rock);
            int b = image == null || image == rock ? -1 : _layout.Solids.IndexOf(image);
            _w.RemoveSolidPair(rock);
            ArenaNav nav = ArenaNav.Build(_layout);
            bool lanesClosed = !ReopensLane(nav, rock.Box);
            if (!lanesClosed || !Rescues(nav, stranded))
            {
                ReinsertPair(_layout.Solids, rock, a, image, b);
                openedNav = lanesClosed ? nav : null;
                return false;
            }

            ForgetRock(rock);
            _repairNav = nav; // the next repair iteration measures this layout
            return true;
        }

        /// <summary>Removes a rock pair already measured to keep every lane closed.</summary>
        private void RemoveRock(Solid rock)
        {
            _w.RemoveSolidPair(rock);
            ForgetRock(rock);
        }

        private void ForgetRock(Solid rock)
        {
            _removable.Remove(rock.Id);
            _removable.Remove(rock.MirrorId);
            _bulwarks.Remove(rock.Id);
            _bulwarks.Remove(rock.MirrorId);
        }

        private bool ReopensLane(ArenaNav nav, YawBox freed) => ReopensLane(new LaneMeter(nav, _layout), freed);

        /// <summary>True when a line through the freed footprint or its image is open for more than 28 m (spec L310).</summary>
        private static bool ReopensLane(LaneMeter meter, YawBox freed)
        {
            return Math.Max(meter.LongestThrough(freed).Length, meter.LongestThrough(MirrorMath.Box(freed)).Length) > GlobalRules.Arena.FiringLaneMax;
        }

        /// <summary>Puts a removed pair back at its original indices, so ids and order stay exactly as they were.</summary>
        private static void ReinsertPair<T>(List<T> list, T item, int a, T image, int b)
        {
            if (b >= 0 && b < a)
            {
                list.Insert(b, image);
                list.Insert(a, item);
            }
            else
            {
                list.Insert(a, item);
                if (b >= 0)
                {
                    list.Insert(b, image);
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

            var finder = new SiteFinder(nav, _layout, _sites, _keepOut, ProtectedZones());
            for (int pair = 0; pair < f.Pairs; pair++)
            {
                bool solid = f.Kind == BiomeFeatureKind.Solid;
                float radius = f.FootprintRadius;
                float clearance = solid ? radius + PropPassage : radius + 0.25f;
                if (!finder.TryFind(_rng, clearance, radius, solid, out Vec3 site))
                {
                    Notes.Add("no site for " + f.BiomeId);
                    return;
                }

                float yaw = 90f * _rng.NextInt(4);
                _sites.Add(new Vec3(site.X, radius, site.Z));
                Rect2 claim = Rect2.FromCenter(site.XZ(), 2f * radius, 2f * radius);
                _occupied.Add(claim);
                _occupied.Add(claim.Mirrored());
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
            _routeBaseline = RouteConditions(withProps: true);
            var finder = new SiteFinder(nav, _layout, _sites, _keepOut, ProtectedZones());
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
                for (int attempt = 0; attempt < PropSiteAttempts; attempt++)
                {
                    if (!finder.TryFind(_rng, clearance, radius, shape.BlocksMovement, out Vec3 site))
                    {
                        break;
                    }

                    float yaw = 90f * _rng.NextInt(4);
                    LayoutWriter.Checkpoint mark = _w.Mark();
                    _w.AddPropPair(def.Id, site, yaw);
                    _sites.Add(new Vec3(site.X, radius, site.Z));

                    // A prop that blocks movement must not narrow a skeleton route below 2.5 m (spec L312) or merge
                    // the two approaches (L308); the site stays claimed either way.
                    if (shape.BlocksMovement && !RoutesIntact(mark, withProps: true))
                    {
                        _w.Rollback(mark);
                        continue;
                    }

                    placed++;
                    break;
                }
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
