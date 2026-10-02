using System;
using System.Collections.Generic;
using System.Globalization;
using Confluence.Arena.Biomes;
using Confluence.Arena.Modules;
using Confluence.Arena.Navigation;
using Confluence.Arena.Props;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Arena.Validation
{
    /// <summary>
    /// Validation of an assembled arena (pipeline steps 6 and 7, spec L321-L322; boundary rules L674-L679). Every
    /// check writes its measurement into the report metrics and a coded failure ("code: detail") when it fails, so
    /// batch tests can tally failure causes. In fail-fast mode the validator stops after the first failing group
    /// (the generator only needs a verdict before retrying a seed).
    /// </summary>
    public sealed class ArenaValidator
    {
        private readonly ModuleLibrary _modules;

        public ArenaValidator(ModuleLibrary modules)
        {
            _modules = modules;
        }

        public bool FailFast { get; set; }

        /// <summary>
        /// Fewest prop definitions an arena must hold (spec L662 "three to eight"); the generator lowers it when the
        /// availability filter offers fewer.
        /// </summary>
        public int MinPropDefinitions { get; set; } = GlobalRules.Arena.PropDefinitionsMin;

        public ArenaValidationReport Validate(ArenaLayout layout, ArenaNav nav = null)
        {
            var report = new ArenaValidationReport { Valid = true };
            var run = new Run(this, layout, nav ?? ArenaNav.Build(layout), report);
            run.Execute();
            return report;
        }

        private sealed class Run
        {
            private readonly ArenaValidator _owner;
            private readonly ArenaLayout _layout;
            private readonly ArenaNav _nav;
            private readonly ArenaSurvey _survey;
            private readonly ArenaValidationReport _report;
            private readonly List<List<int>> _approaches = new List<List<int>>();

            public Run(ArenaValidator owner, ArenaLayout layout, ArenaNav nav, ArenaValidationReport report)
            {
                _owner = owner;
                _layout = layout;
                _nav = nav;
                _survey = new ArenaSurvey(layout, nav);
                _report = report;
            }

            private bool Stop => _owner.FailFast && !_report.Valid;

            public void Execute()
            {
                Action[] groups =
                {
                    CheckContour, CheckComposition, CheckMirrorParity, CheckSpawns, CheckReachability, CheckPaths, CheckApproaches,
                    CheckLanes, CheckCover, CheckElevation, CheckSourcesAndProps, CheckFinalFloor, CheckContraction,
                    CheckFieldPlacements, CheckSingleCover, CheckDestruction,
                };

                for (int i = 0; i < groups.Length; i++)
                {
                    groups[i]();
                    if (Stop)
                    {
                        return;
                    }
                }
            }

            private void Fail(string code, string detail) => _report.AddFailure(code + ": " + detail);

            private void Metric(string name, float value) => _report.SetMetric(name, value);

            private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

            // ---- Contour (spec L306, L48) ----------------------------------------------------------------

            private void CheckContour()
            {
                BoundaryContour b = _layout.Boundary;
                if (b == null)
                {
                    Fail("contour", "missing");
                    return;
                }

                if (!b.IsValid(out string reason))
                {
                    Fail("contour", reason);
                    return;
                }

                int n = b.SampleCount;
                if (n % 2 != 0)
                {
                    Fail("contour", "odd sample count cannot be half-turn symmetric");
                }
                else
                {
                    for (int i = 0; i < n / 2; i++)
                    {
                        if (MathF.Abs(b.InitialRadii[i] - b.InitialRadii[i + n / 2]) > ArenaRules.MirrorTolerance)
                        {
                            Fail("contour", "radius sample " + i + " is not mirrored");
                            break;
                        }
                    }
                }

                if (Math.Abs(b.FinalRadius - _layout.FinalFloorRadius) > 1e-3f || Vec3.DistanceXZ(b.Center, _layout.Center) > 1e-3f)
                {
                    Fail("contour", "does not contract to the central final floor");
                }

                float target = (float)(Math.PI * _layout.Diameter * _layout.Diameter * 0.25);
                float area = b.AreaAt(0f);
                Metric("contour_area", area);
                Metric("target_area", target);
                if (MathF.Abs(area - target) > target * GlobalRules.Arena.UsableAreaTolerance)
                {
                    Fail("area", "contour area " + F(area) + " is not within 10% of " + F(target));
                }
            }

            // ---- Module composition (spec L660) -----------------------------------------------------------

            /// <summary>
            /// "Each size selects one center module ... two to four elevation modules, and three to six cover
            /// modules" (spec L660), counted over the recorded module instances. Skipped without a module library.
            /// </summary>
            private void CheckComposition()
            {
                if (_owner._modules == null)
                {
                    return;
                }

                var counts = new int[Enum.GetValues(typeof(ModuleCategory)).Length];
                foreach (ModuleInstance m in _layout.Modules)
                {
                    if (!_owner._modules.TryGet(m.ModuleId, out ModuleGeometry g))
                    {
                        Fail("composition", "unknown module " + m.ModuleId);
                        return;
                    }

                    counts[(int)g.Category]++;
                }

                int center = counts[(int)ModuleCategory.Center];
                int elevation = counts[(int)ModuleCategory.Elevation];
                int cover = counts[(int)ModuleCategory.Cover];
                Metric("modules", _layout.Modules.Count);
                Metric("elevation_modules", elevation);
                Metric("cover_modules", cover);
                if (center != GlobalRules.Arena.CenterModules)
                {
                    Fail("composition", center + " center modules");
                }

                if (elevation < GlobalRules.Arena.ElevationModulesMin || elevation > GlobalRules.Arena.ElevationModulesMax)
                {
                    Fail("composition", elevation + " elevation modules");
                }

                if (cover < GlobalRules.Arena.CoverModulesMin || cover > GlobalRules.Arena.CoverModulesMax)
                {
                    Fail("composition", cover + " cover modules");
                }
            }

            // ---- Functional symmetry (spec L308) ----------------------------------------------------------

            private void CheckMirrorParity()
            {
                var byId = new Dictionary<int, Solid>();
                foreach (Solid s in _layout.Solids)
                {
                    byId[s.Id] = s;
                }

                foreach (Solid s in _layout.Solids)
                {
                    if (!byId.TryGetValue(s.MirrorId, out Solid m) || !SolidsMirror(s, m))
                    {
                        Fail("parity", "solid " + s.Id + " (" + s.Material + ") has no mirrored counterpart");
                        return;
                    }
                }

                for (int i = 0; i < _layout.Materials.Count; i++)
                {
                    MaterialPatch p = _layout.Materials[i];
                    MaterialPatch m = p.MirrorId >= 0 && p.MirrorId < _layout.Materials.Count ? _layout.Materials[p.MirrorId] : null;
                    if (m == null || !PatchesMirror(p, m))
                    {
                        Fail("parity", "material patch " + i + " has no mirrored counterpart");
                        return;
                    }
                }

                foreach (PropPlacement p in _layout.Props)
                {
                    PropPlacement m = _layout.FindProp(p.MirrorId);
                    if (m == null || m.PropId != p.PropId || Vec3.Distance(MirrorMath.Point(p.Position), m.Position) > ArenaRules.MirrorTolerance
                        || MathF.Abs(CMath.DeltaAngle(MirrorMath.Yaw(p.YawDeg), m.YawDeg)) > 0.01f)
                    {
                        Fail("parity", "prop " + p.Id + " (" + p.PropId + ") has no mirrored counterpart");
                        return;
                    }
                }

                for (int i = 0; i < _layout.LightShafts.Count; i++)
                {
                    LightShaft l = _layout.LightShafts[i];
                    LightShaft m = l.MirrorId >= 0 && l.MirrorId < _layout.LightShafts.Count ? _layout.LightShafts[l.MirrorId] : null;
                    if (m == null || Vec3.Distance(MirrorMath.Point(l.Position), m.Position) > ArenaRules.MirrorTolerance || l.Radius != m.Radius)
                    {
                        Fail("parity", "light shaft " + i + " has no mirrored counterpart");
                        return;
                    }
                }

                SpawnPoint s0 = _layout.Spawns[0];
                SpawnPoint s1 = _layout.Spawns[1];
                if (Vec3.Distance(MirrorMath.Point(s0.Position), s1.Position) > ArenaRules.MirrorTolerance
                    || !MirrorMath.IsPointMirror(s0.Alcove, s1.Alcove, ArenaRules.MirrorTolerance))
                {
                    Fail("parity", "spawns are not mirrored");
                }

                // Functional symmetry of the walkable graph itself: every cell, edge, and clearance of one side has its
                // image on the other, so every distance and route measured from one spawn holds for the other.
                if (!_nav.IsHalfTurnSymmetric)
                {
                    Fail("parity", "the walkable graph is not half-turn symmetric");
                }
            }

            private static bool SolidsMirror(Solid a, Solid b)
            {
                return a.Kind == b.Kind && a.Material == b.Material && a.Durability == b.Durability && a.Anchored == b.Anchored
                    && a.Walkable == b.Walkable && a.BlocksSight == b.BlocksSight && a.RetiresAtFinal == b.RetiresAtFinal
                    && a.Flags == b.Flags && MathF.Abs(a.RampRise - b.RampRise) <= ArenaRules.MirrorTolerance
                    && MirrorMath.IsPointMirror(a.Box, b.Box, ArenaRules.MirrorTolerance, a.IsRamp);
            }

            private static bool PatchesMirror(MaterialPatch a, MaterialPatch b)
            {
                return Vec2.Distance(MirrorMath.Point(a.Center), b.Center) <= ArenaRules.MirrorTolerance
                    && Vec2.Distance(a.HalfSize, b.HalfSize) <= ArenaRules.MirrorTolerance && a.Radius == b.Radius
                    && a.Water == b.Water && a.Heat == b.Heat && a.Frost == b.Frost && a.Dust == b.Dust && a.Sand == b.Sand
                    && a.Growth == b.Growth && a.Flags == b.Flags && a.NaturalSource == b.NaturalSource && a.SurfaceY == b.SurfaceY
                    && a.RefillRule == b.RefillRule;
            }

            // ---- Spawns (spec L46-L47, L310) ---------------------------------------------------------------

            private void CheckSpawns()
            {
                for (int side = 0; side < 2; side++)
                {
                    if (_survey.SpawnNode(side) < 0)
                    {
                        Fail("spawn", "side " + side + " spawn has no standing node");
                        return;
                    }

                    SpawnPoint spawn = _layout.Spawns[side];
                    if (!spawn.Alcove.Contains(spawn.Position + new Vec3(0f, 0.5f, 0f)))
                    {
                        Fail("spawn", "side " + side + " spawn lies outside its alcove");
                    }
                }

                // No direct initial enemy sightline from the exact spawn position (L47, L310): eye to any point of the
                // opponent's standing capsule.
                float[] targets = { GlobalRules.Movement.StandingEyeHeight, 1.2f, 0.9f, 0.4f };
                for (int side = 0; side < 2; side++)
                {
                    Vec3 eye = _layout.Spawns[side].Position + new Vec3(0f, GlobalRules.Movement.StandingEyeHeight, 0f);
                    Vec3 enemy = _layout.Spawns[1 - side].Position;
                    for (int i = 0; i < targets.Length; i++)
                    {
                        if (_nav.HasLineOfSight(eye, enemy + new Vec3(0f, targets[i], 0f)))
                        {
                            Fail("spawn-sightline", "side " + side + " sees the enemy spawn");
                            return;
                        }
                    }
                }
            }

            // ---- Reachability and pockets (spec L312, L676) ------------------------------------------------

            private void CheckReachability()
            {
                IReadOnlyList<int> floor = _survey.FinalFloorNodes;
                Metric("final_floor_nodes", floor.Count);
                if (floor.Count == 0)
                {
                    Fail("final-floor", "no standable final floor");
                    return;
                }

                bool[] canReach = _nav.CanReach(floor, NavQuery.Capsule);
                bool[] reached = _survey.ReachableFromSpawns;
                for (int side = 0; side < 2; side++)
                {
                    if (!canReach[_survey.SpawnNode(side)])
                    {
                        Fail("reachability", "side " + side + " cannot reach the final floor");
                        return;
                    }
                }

                if (!reached[_survey.SpawnNode(1)])
                {
                    Fail("reachability", "the spawns are not connected");
                    return;
                }

                for (int node = 0; node < _nav.NodeCount; node++)
                {
                    if (reached[node] && !canReach[node])
                    {
                        Vec3 p = _nav.NodePosition(node);
                        Fail("pocket", "unescapable pocket at (" + F(p.X) + ", " + F(p.Y) + ", " + F(p.Z) + ")");
                        return;
                    }
                }
            }

            // ---- Path lengths and central reach (spec L310) ------------------------------------------------

            private void CheckPaths()
            {
                float spawnPath = _survey.SpawnPathLength();
                ArenaRules.SpawnPathBand(_layout.Diameter, out float min, out float max);
                Metric("spawn_path", spawnPath);
                if (spawnPath < min || spawnPath > max)
                {
                    Fail("spawn-path", F(spawnPath) + " m is outside " + F(min) + "-" + F(max) + " m");
                }

                float c0 = _survey.CenterPathLength(0);
                float c1 = _survey.CenterPathLength(1);
                Metric("center_path_0", c0);
                Metric("center_path_1", c1);
                if (MathF.Abs(c0 - c1) > ArenaRules.ParityDistanceTolerance)
                {
                    Fail("parity", "center paths differ: " + F(c0) + " vs " + F(c1));
                }

                float r0 = _survey.CentralReachSeconds(0);
                float r1 = _survey.CentralReachSeconds(1);
                Metric("central_reach_0", r0);
                Metric("central_reach_1", r1);
                for (int side = 0; side < 2; side++)
                {
                    float r = side == 0 ? r0 : r1;
                    if (!(r >= GlobalRules.Arena.CentralReachMinSec && r <= GlobalRules.Arena.CentralReachMaxSec))
                    {
                        Fail("central-reach", "side " + side + " reaches useful central cover in " + F(r) + " s");
                        return;
                    }
                }

                if (MathF.Abs(r0 - r1) * ArenaRules.CentralReachReferenceSpeed > ArenaRules.ParityDistanceTolerance)
                {
                    Fail("parity", "central reach differs: " + F(r0) + " vs " + F(r1));
                }
            }

            // ---- Route redundancy and width (spec L308, L312) ----------------------------------------------

            private void CheckApproaches()
            {
                for (int side = 0; side < 2; side++)
                {
                    List<List<int>> paths = _survey.DisjointApproaches(side);
                    Metric("approaches_" + side, paths.Count);
                    if (paths.Count == 0)
                    {
                        Fail("route-width", "side " + side + " has no 2.5 m route to the final floor");
                        return;
                    }

                    if (paths.Count < 2)
                    {
                        Fail("route-redundancy", "side " + side + " has a single approach");
                        return;
                    }

                    _approaches.AddRange(paths);
                }

                float[] route = _nav.Distances(new[] { _survey.SpawnNode(0) }, NavQuery.Route);
                if (float.IsPositiveInfinity(route[_survey.SpawnNode(1)]))
                {
                    Fail("route-width", "no 2.5 m route connects the spawns");
                }
            }

            // ---- Firing lanes (spec L308, L310) ------------------------------------------------------------

            private void CheckLanes()
            {
                var meter = new LaneMeter(_nav, _layout);
                FiringLane longest = meter.Longest();
                FiringLane designated = meter.Designated();
                Metric("longest_lane", longest.Length);
                Metric("designated_lane", designated.Length);
                if (longest.Length < GlobalRules.Arena.FiringLaneMin || longest.Length > GlobalRules.Arena.FiringLaneMax)
                {
                    Fail("lane-length", "longest lane " + F(longest.Length) + " m from (" + F(longest.A.X) + ", " + F(longest.A.Y)
                        + ") to (" + F(longest.B.X) + ", " + F(longest.B.Y) + ")");
                    return;
                }

                if (designated.Length < GlobalRules.Arena.FiringLaneMin)
                {
                    Fail("lane-length", "the designated firing lane is " + F(designated.Length) + " m");
                    return;
                }

                // "At least two approaches interrupt a ranged firing lane": disjoint 2.5 m approaches from the spawns
                // that reach the designated lane's corridor, where a player steps into and across the lane.
                List<int> corridor = _survey.LaneCorridorNodes(designated, ArenaRules.LaneCorridorHalfWidth);
                int interrupting = 0;
                for (int side = 0; side < 2; side++)
                {
                    interrupting += _survey.DisjointApproaches(side, corridor).Count;
                }

                Metric("lane_interrupting_approaches", interrupting);
                if (interrupting < GlobalRules.Arena.MinApproachesInterruptingLane)
                {
                    Fail("lane-interrupt", interrupting + " approach(es) reach the firing lane");
                }
            }

            // ---- Cover and usable area (spec L306, L310) ---------------------------------------------------

            private void CheckCover()
            {
                CoverStats stats = _survey.Cover();
                float target = (float)(Math.PI * _layout.Diameter * _layout.Diameter * 0.25);
                Metric("usable_area", stats.UsableArea);
                Metric("cover_area", stats.CoverArea);
                Metric("cover_fraction", stats.CoverFraction);
                if (MathF.Abs(stats.UsableArea - target) > target * GlobalRules.Arena.UsableAreaTolerance)
                {
                    Fail("area", "usable area " + F(stats.UsableArea) + " is not within 10% of " + F(target));
                }

                if (stats.CoverFraction < GlobalRules.Arena.CoverFractionMin || stats.CoverFraction > GlobalRules.Arena.CoverFractionMax)
                {
                    Fail("cover-ratio", "cover is " + F(stats.CoverFraction * 100f) + "% of usable floor");
                }

                float coverScale = Math.Max(1f, Math.Max(stats.CoverAreaSide0, stats.CoverAreaSide1));
                if (MathF.Abs(stats.CoverAreaSide0 - stats.CoverAreaSide1) > coverScale * ArenaRules.ParityAreaTolerance)
                {
                    Fail("parity", "cover per side " + F(stats.CoverAreaSide0) + " vs " + F(stats.CoverAreaSide1));
                }

                float elevScale = Math.Max(1f, Math.Max(stats.ElevatedAreaSide0, stats.ElevatedAreaSide1));
                if (MathF.Abs(stats.ElevatedAreaSide0 - stats.ElevatedAreaSide1) > elevScale * ArenaRules.ParityAreaTolerance)
                {
                    Fail("parity", "elevation per side " + F(stats.ElevatedAreaSide0) + " vs " + F(stats.ElevatedAreaSide1));
                }
            }

            // ---- Elevation (spec L312, L175) ---------------------------------------------------------------

            private void CheckElevation()
            {
                bool[] reached = _survey.ReachableFromSpawns;
                float max = float.NegativeInfinity;
                float min = float.PositiveInfinity;
                for (int node = 0; node < _nav.NodeCount; node++)
                {
                    if (!reached[node])
                    {
                        continue;
                    }

                    float h = _nav.NodeHeight(node) - _layout.Center.Y;
                    max = Math.Max(max, h);
                    min = Math.Min(min, h);
                    float limit = IsVantageNode(node) ? GlobalRules.Arena.VantageHeight : GlobalRules.Arena.ElevationMax;
                    if (h > limit + 0.05f || h < ArenaRules.MinWalkableHeight - 0.05f)
                    {
                        Fail("elevation", "walkable height " + F(h) + " m");
                        return;
                    }
                }

                Metric("max_height", max);
                Metric("min_height", min);
                foreach (Solid s in _layout.Solids)
                {
                    if (s.IsRamp && s.Ramp.SlopeDeg > GlobalRules.Arena.RampMaxSlopeDeg + 1e-3f)
                    {
                        Fail("elevation", "ramp " + s.Id + " is " + F(s.Ramp.SlopeDeg) + " degrees");
                        return;
                    }

                    if (s.Walkable && s.IsDestructible)
                    {
                        Fail("destruction", "walkable solid " + s.Id + " is destructible");
                        return;
                    }
                }
            }

            private bool IsVantageNode(int node)
            {
                int id = _nav.NodeSupportSolidId(node);
                Solid s = id >= 0 ? _layout.FindSolid(id) : null;
                if (s == null || s.ModuleIndex < 0 || s.ModuleIndex >= _layout.Modules.Count || _owner._modules == null)
                {
                    return false;
                }

                return _owner._modules.TryGet(_layout.Modules[s.ModuleIndex].ModuleId, out ModuleGeometry g) && g.IsVantage;
            }

            // ---- Natural sources and props (spec L662, L668) -----------------------------------------------

            private void CheckSourcesAndProps()
            {
                int sources = _layout.NaturalSourceCount();
                Metric("natural_sources", sources);
                if (sources > GlobalRules.Arena.MaxNaturalSourcePatches)
                {
                    Fail("sources", sources + " natural source patches at combat start");
                }

                foreach (MaterialPatch p in _layout.Materials)
                {
                    if (!p.HasValidUnits)
                    {
                        Fail("sources", "a material patch exceeds four units per cell");
                        return;
                    }
                }

                var definitions = new SortedSet<string>(StringComparer.Ordinal);
                foreach (PropPlacement p in _layout.Props)
                {
                    definitions.Add(p.PropId);
                    if (!PropShapes.TryGet(p.PropId, out PropShape shape))
                    {
                        Fail("props", p.PropId + " has no shape");
                        return;
                    }

                    // Never the spawn floor or the final-circle support (prop placement text, spec L662).
                    float r = shape.PlacementRadius;
                    for (int side = 0; side < 2; side++)
                    {
                        YawBox alcove = _layout.Spawns[side].Alcove;
                        if (Vec2.Distance(alcove.ClosestPointXZ(p.Position.XZ()), p.Position.XZ()) < r + 0.5f)
                        {
                            Fail("props", p.PropId + " stands on a spawn floor");
                            return;
                        }
                    }

                    if (Vec3.DistanceXZ(p.Position, _layout.Center) < _layout.FinalFloorRadius + r + ArenaRules.PropFinalCircleMargin)
                    {
                        Fail("props", p.PropId + " stands on the final circle");
                        return;
                    }
                }

                Metric("prop_definitions", definitions.Count);
                Metric("prop_instances", _layout.Props.Count);
                if (_layout.Props.Count > GlobalRules.Arena.MaxPropInstances || definitions.Count > GlobalRules.Arena.PropDefinitionsMax
                    || definitions.Count < _owner.MinPropDefinitions)
                {
                    Fail("props", _layout.Props.Count + " instances of " + definitions.Count + " definitions");
                }

                CheckBiomeFeatures();
            }

            /// <summary>The biome's announced rule has its mirrored sources and objects in place (spec L335, L668).</summary>
            private void CheckBiomeFeatures()
            {
                if (_layout.BiomeId == null || !BiomeFeatures.TryGet(_layout.BiomeId, out BiomeFeature f))
                {
                    return;
                }

                int expected = 2 * f.Pairs;
                int found;
                switch (f.Kind)
                {
                    case BiomeFeatureKind.Solid:
                        found = 0;
                        foreach (Solid s in _layout.Solids)
                        {
                            if (s.Material == f.Material && s.ModuleIndex < 0)
                            {
                                found++;
                            }
                        }

                        break;
                    case BiomeFeatureKind.Gusts:
                        found = _layout.Gusts.Count > 0 ? expected : 0;
                        break;
                    default:
                        found = 0;
                        foreach (MaterialPatch m in _layout.Materials)
                        {
                            if (m.RefillRule == f.Rule)
                            {
                                found++;
                            }
                        }

                        break;
                }

                Metric("biome_features", found);
                if (found < expected)
                {
                    Fail("biome", f.BiomeId + " has " + found + " of " + expected + " rule features");
                }
            }

            // ---- Final floor (spec L171, L678) -------------------------------------------------------------

            private void CheckFinalFloor()
            {
                Vec2 c = _layout.Center.XZ();
                float r = _layout.FinalFloorRadius;
                foreach (Solid s in _layout.Solids)
                {
                    if (s.Walkable || s.Kind == SolidKind.Floor || s.RetiresAtFinal)
                    {
                        if (s.RetiresAtFinal && s.Anchored)
                        {
                            Fail("final-floor", "retiring cover " + s.Id + " is anchored");
                            return;
                        }

                        continue;
                    }

                    bool inPlayerBand = s.Box.Bottom < AboveFloor(GlobalRules.Movement.StandingHeight) && s.Box.Top > 0.05f;
                    if (inPlayerBand && ArenaSurvey.FootprintDistance(s.Box, c) < r - 0.05f)
                    {
                        Fail("final-floor", s.Material + " solid " + s.Id + " intrudes on the clear final floor");
                        return;
                    }
                }

                // The anchored floor must remain: anchored walkable support across the disc. Retiring soft cover may
                // stand on part of it until 85 s, so its footprint is not expected to carry a node yet.
                var underRetiring = new bool[_nav.ColumnCount];
                foreach (Solid s in _layout.Solids)
                {
                    if (s.RetiresAtFinal)
                    {
                        YawBox grown = new YawBox(s.Box.Center, s.Box.HalfExtents + new Vec3(0.5f, 0f, 0.5f), s.Box.YawDeg);
                        _survey.Rect2Columns(grown, (column, center) =>
                        {
                            if (grown.ContainsXZ(center))
                            {
                                underRetiring[column] = true;
                            }
                        });
                    }
                }

                int expected = 0;
                int present = 0;
                for (int col = 0; col < _nav.ColumnCount; col++)
                {
                    Vec2 p = _nav.ColumnCenter(col);
                    if (Vec2.Distance(p, c) > r - 0.5f || underRetiring[col])
                    {
                        continue;
                    }

                    expected++;
                    for (int m = _nav.FirstNodeInColumn(col), end = m + _nav.NodeCountInColumn(col); m < end; m++)
                    {
                        Solid support = _layout.FindSolid(_nav.NodeSupportSolidId(m));
                        if (MathF.Abs(_nav.NodeHeight(m)) <= 0.05f && support != null && support.Anchored)
                        {
                            present++;
                            break;
                        }
                    }
                }

                // Retiring cover may stand on part of the floor until 85 s; its footprint has no node yet.
                if (present < expected * 0.95f)
                {
                    Fail("final-floor", "anchored floor covers " + present + " of " + expected + " cells");
                }
            }

            private float AboveFloor(float height) => _layout.Center.Y + height;

            // ---- Contraction (spec L48, L676) ---------------------------------------------------------------

            private void CheckContraction()
            {
                if (_survey.FindContractionFailure(out float t, out int node))
                {
                    Vec3 p = _nav.NodePosition(node);
                    Fail("contraction", "at " + F(t) + " s the cell at (" + F(p.X) + ", " + F(p.Z) + ") loses its route to the final floor");
                }
            }

            // ---- Representative field placements (spec L312, L322, L676) ------------------------------------

            private void CheckFieldPlacements()
            {
                float radius = ArenaRules.RepresentativeFieldRadius + ArenaRules.CapsuleClearance;
                int tested = 0;
                for (int i = 0; i < _approaches.Count; i++)
                {
                    List<int> path = _approaches[i];
                    float along = 0f;
                    float nextSample = 0f;
                    for (int k = 1; k < path.Count; k++)
                    {
                        Vec2 p = _nav.NodePosition(path[k]).XZ();
                        along += Vec2.Distance(_nav.NodePosition(path[k - 1]).XZ(), p);
                        if (along < nextSample || IsExempt(p))
                        {
                            continue;
                        }

                        nextSample = along + ArenaRules.FieldSampleSpacing;
                        tested++;
                        var blocked = new bool[_nav.NodeCount];
                        _survey.MarkDisc(blocked, p, radius, new Vec2(float.MaxValue, float.MaxValue), 0f, 0f);
                        if (!BothSpawnsReachFloor(NavQuery.Capsule.WithBlocked(blocked)))
                        {
                            Fail("field-placement", "a 3 m wall at (" + F(p.X) + ", " + F(p.Y) + ") seals the final floor");
                            return;
                        }
                    }
                }

                Metric("field_placements", tested);
            }

            private bool IsExempt(Vec2 p)
            {
                for (int side = 0; side < 2; side++)
                {
                    if (Vec2.Distance(p, _layout.Spawns[side].Position.XZ()) <= ArenaRules.AlcoveExemptRadius)
                    {
                        return true;
                    }
                }

                return Vec2.Distance(p, _layout.Center.XZ()) <= _layout.FinalFloorRadius + 1f;
            }

            private bool BothSpawnsReachFloor(NavQuery query)
            {
                bool[] canReach = _nav.CanReach(_survey.FinalFloorNodes, query);
                return canReach[_survey.SpawnNode(0)] && canReach[_survey.SpawnNode(1)];
            }

            // ---- No single cover piece blocks every approach (spec L310) ------------------------------------

            private void CheckSingleCover()
            {
                float radius = ArenaRules.RepresentativeFieldRadius + ArenaRules.CapsuleClearance;
                int tested = 0;
                foreach (Solid s in _layout.Solids)
                {
                    if ((s.Kind != SolidKind.Cover && s.Kind != SolidKind.Pillar) || !NearApproach(s.Box, 2f))
                    {
                        continue;
                    }

                    tested++;
                    var blocked = new bool[_nav.NodeCount];
                    for (int node = 0; node < _nav.NodeCount; node++)
                    {
                        blocked[node] = ArenaSurvey.FootprintDistance(s.Box, _nav.NodePosition(node).XZ()) <= radius
                            && !IsSpawnFloor(_nav.NodePosition(node).XZ());
                    }

                    if (!BothSpawnsReachFloor(NavQuery.Capsule.WithBlocked(blocked)))
                    {
                        Fail("single-cover", s.Material + " cover " + s.Id + " blocks every approach");
                        return;
                    }
                }

                Metric("single_cover_tests", tested);
            }

            private bool IsSpawnFloor(Vec2 p)
            {
                return Vec2.Distance(p, _layout.Spawns[0].Position.XZ()) <= ArenaRules.AlcoveExemptRadius
                    || Vec2.Distance(p, _layout.Spawns[1].Position.XZ()) <= ArenaRules.AlcoveExemptRadius;
            }

            private bool NearApproach(YawBox box, float distance)
            {
                for (int i = 0; i < _approaches.Count; i++)
                {
                    List<int> path = _approaches[i];
                    for (int k = 0; k < path.Count; k += 2)
                    {
                        if (ArenaSurvey.FootprintDistance(box, _nav.NodePosition(path[k]).XZ()) <= distance)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            // ---- Destruction and toggled states (spec L171, L322, L676) -------------------------------------

            private void CheckDestruction()
            {
                // Intact: nothing walkable a player can reach rests on a destructible solid.
                bool[] reached = _survey.ReachableFromSpawns;
                for (int node = 0; node < _nav.NodeCount; node++)
                {
                    if (!reached[node])
                    {
                        continue;
                    }

                    Solid support = _layout.FindSolid(_nav.NodeSupportSolidId(node));
                    if (support != null && support.IsDestructible)
                    {
                        Fail("destruction", "floor at node " + node + " rests on destructible solid " + support.Id);
                        return;
                    }
                }

                // Every destructible piece broken, every shutter open, and every prop gone.
                var removed = new List<Solid>();
                foreach (Solid s in _layout.Solids)
                {
                    if (IsRemovable(s))
                    {
                        removed.Add(s);
                    }
                }

                Metric("destructible_solids", removed.Count);
                var options = new ArenaNavOptions { IncludeSolid = s => !IsRemovable(s), PropsAsObstacles = false };
                ArenaNav broken = ArenaNav.Build(_layout, options);
                var brokenSurvey = new ArenaSurvey(_layout, broken);
                if (brokenSurvey.SpawnNode(0) < 0 || brokenSurvey.SpawnNode(1) < 0 || brokenSurvey.FinalFloorNodes.Count == 0)
                {
                    Fail("destruction", "destruction removes a spawn or the final floor");
                    return;
                }

                bool[] canReach = broken.CanReach(brokenSurvey.FinalFloorNodes, NavQuery.Capsule);
                if (!canReach[brokenSurvey.SpawnNode(0)] || !canReach[brokenSurvey.SpawnNode(1)])
                {
                    Fail("destruction", "destruction seals a spawn from the final floor");
                    return;
                }

                // No hole into an inaccessible region: newly reachable floor must lie where a removed piece stood.
                var intactColumn = new bool[_nav.ColumnCount];
                for (int node = 0; node < _nav.NodeCount; node++)
                {
                    if (reached[node])
                    {
                        intactColumn[_nav.NodeColumn(node)] = true;
                    }
                }

                var nearRemoved = new bool[_nav.ColumnCount];
                foreach (Solid s in removed)
                {
                    YawBox grown = new YawBox(s.Box.Center, s.Box.HalfExtents + new Vec3(1f, 0f, 1f), s.Box.YawDeg);
                    _survey.Rect2Columns(grown, (column, center) =>
                    {
                        if (grown.ContainsXZ(center))
                        {
                            nearRemoved[column] = true;
                        }
                    });
                }

                foreach (PropPlacement p in _layout.Props)
                {
                    if (PropShapes.TryGetCollisionBox(p, out YawBox box))
                    {
                        YawBox grown = new YawBox(box.Center, box.HalfExtents + new Vec3(1f, 0f, 1f), box.YawDeg);
                        _survey.Rect2Columns(grown, (column, center) =>
                        {
                            if (grown.ContainsXZ(center))
                            {
                                nearRemoved[column] = true;
                            }
                        });
                    }
                }

                bool[] brokenReached = brokenSurvey.ReachableFromSpawns;
                for (int node = 0; node < broken.NodeCount; node++)
                {
                    if (!brokenReached[node])
                    {
                        continue;
                    }

                    int column = broken.NodeColumn(node); // both grids share size and origin
                    Vec3 p = broken.NodePosition(node);
                    if (!intactColumn[column] && !nearRemoved[column] && _layout.Boundary.RadialMargin(p, 0f) >= 0f)
                    {
                        Fail("destruction", "destruction opens a hole into an inaccessible region at (" + F(p.X) + ", " + F(p.Z) + ")");
                        return;
                    }
                }
            }

            private static bool IsRemovable(Solid s)
            {
                return s.IsDestructible || ArenaMaterials.IsMaterial(s.Material, ArenaMaterials.Shutter);
            }
        }
    }
}
