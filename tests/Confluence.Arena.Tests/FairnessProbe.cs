using System;
using System.Collections.Generic;
using Confluence.Arena.Generation;
using Confluence.Arena.Navigation;
using Confluence.Arena.Props;
using Confluence.Arena.Validation;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Arena.Tests
{
    /// <summary>
    /// Independent re-checks of a released layout. Nothing here trusts the validator's verdict: geometry, parity,
    /// sightlines, props, and mirrored distances are recomputed from the layout, and the validator's recorded metrics
    /// are compared with the spec ranges (spec L306-L312, L321-L323, L662, L674-L679).
    /// </summary>
    public static class FairnessProbe
    {
        /// <summary>
        /// Largest |d0(n) - d1(mirror n)| over every node reachable from spawn 0 (spec L310 "mirrored distances"). The
        /// geometry and its navigation graph are exactly half-turn symmetric, so only float summation order remains.
        /// </summary>
        public const float MirroredDistanceTolerance = 0.01f;

        public sealed class Result
        {
            public List<string> Problems { get; } = new List<string>();
            public float MaxMirrorDistanceError { get; set; }
            public float AsymmetricNodeFraction { get; set; }
        }

        public static Result Check(ArenaLayout layout, bool contraction)
        {
            var result = new Result();
            List<string> problems = result.Problems;
            if (!layout.Report.Valid)
            {
                problems.Add("released an invalid layout: " + string.Join("; ", layout.Report.Failures));
                return result;
            }

            CheckContour(layout, problems);
            CheckMirrorPairs(layout, problems);
            CheckProps(layout, problems);
            CheckSightline(layout, problems);
            CheckMetrics(layout, problems);
            ArenaNav nav = ArenaNav.Build(layout);
            var survey = new ArenaSurvey(layout, nav);
            CheckMirroredDistances(nav, survey, result);
            CheckRoutes(nav, survey, problems);
            if (contraction)
            {
                CheckContraction(layout, nav, survey, problems);
            }

            return result;
        }

        private static void CheckContour(ArenaLayout layout, List<string> problems)
        {
            BoundaryContour b = layout.Boundary;
            if (!b.IsValid(out string reason))
            {
                problems.Add("contour: " + reason);
                return;
            }

            int n = b.SampleCount;
            for (int i = 0; i < n / 2; i++)
            {
                if (b.InitialRadii[i] != b.InitialRadii[i + n / 2])
                {
                    problems.Add("contour: sample " + i + " is not half-turn symmetric");
                }
            }

            float target = SizeProfile.For(layout.Diameter).TargetArea;
            float area = b.AreaAt(0f);
            if (Math.Abs(area - target) > target * GlobalRules.Arena.UsableAreaTolerance)
            {
                problems.Add("contour: area " + area + " vs target " + target);
            }

            for (float angle = 0f; angle < 360f; angle += 7.5f)
            {
                float last = float.PositiveInfinity;
                for (float t = 0f; t <= b.ContractEndSec + 5f; t += 5f)
                {
                    float r = b.RadiusAt(angle, t);
                    if (r > last + 1e-4f)
                    {
                        problems.Add("contour: expands at " + angle + " deg, " + t + " s");
                    }

                    last = r;
                }

                if (Math.Abs(b.RadiusAt(angle, b.ContractEndSec) - GlobalRules.Boundary.FinalFloorRadius) > 1e-3f)
                {
                    problems.Add("contour: does not end on the 4 m final floor at " + angle + " deg");
                }
            }
        }

        private static void CheckMirrorPairs(ArenaLayout layout, List<string> problems)
        {
            foreach (Solid s in layout.Solids)
            {
                Solid m = layout.FindSolid(s.MirrorId);
                if (m == null || m.MirrorId != s.Id || m.Kind != s.Kind || m.Material != s.Material || m.Anchored != s.Anchored
                    || m.Durability != s.Durability || m.Walkable != s.Walkable || m.BlocksSight != s.BlocksSight
                    || !MirrorMath.IsPointMirror(s.Box, m.Box, ArenaRules.MirrorTolerance, s.IsRamp))
                {
                    problems.Add("parity: solid " + s.Id + " (" + s.Kind + ", " + s.Material + ")");
                }
            }

            for (int i = 0; i < layout.Materials.Count; i++)
            {
                MaterialPatch p = layout.Materials[i];
                MaterialPatch q = p.MirrorId >= 0 && p.MirrorId < layout.Materials.Count ? layout.Materials[p.MirrorId] : null;
                if (q == null || Vec2.Distance(MirrorMath.Point(p.Center), q.Center) > ArenaRules.MirrorTolerance || q.Water != p.Water
                    || q.Heat != p.Heat || q.Frost != p.Frost || q.Sand != p.Sand || q.Dust != p.Dust || q.Growth != p.Growth
                    || q.NaturalSource != p.NaturalSource || q.Flags != p.Flags)
                {
                    problems.Add("parity: patch " + i);
                }
            }

            SpawnPoint s0 = layout.Spawns[0];
            SpawnPoint s1 = layout.Spawns[1];
            if (Vec3.Distance(MirrorMath.Point(s0.Position), s1.Position) > ArenaRules.MirrorTolerance
                || Math.Abs(CMath.DeltaAngle(MirrorMath.Yaw(s0.YawDeg), s1.YawDeg)) > 0.01f || s0.Position.Z >= 0f)
            {
                problems.Add("parity: spawns");
            }
        }

        private static void CheckProps(ArenaLayout layout, List<string> problems)
        {
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (PropPlacement p in layout.Props)
            {
                counts[p.PropId] = counts.TryGetValue(p.PropId, out int c) ? c + 1 : 1;
                PropPlacement m = layout.FindProp(p.MirrorId);
                if (m == null || m.MirrorId != p.Id || m.PropId != p.PropId || m == p
                    || Vec3.Distance(MirrorMath.Point(p.Position), m.Position) > ArenaRules.MirrorTolerance
                    || Math.Abs(CMath.DeltaAngle(MirrorMath.Yaw(p.YawDeg), m.YawDeg)) > 0.01f)
                {
                    problems.Add("props: " + p.PropId + " #" + p.Id + " has no mirrored twin");
                }

                PropShape shape = PropShapes.For(p.PropId);
                if (Vec3.DistanceXZ(p.Position, layout.Center) < layout.FinalFloorRadius + shape.PlacementRadius)
                {
                    problems.Add("props: " + p.PropId + " on the final circle");
                }

                for (int side = 0; side < 2; side++)
                {
                    if (layout.Spawns[side].Alcove.ContainsXZ(p.Position.XZ()))
                    {
                        problems.Add("props: " + p.PropId + " in a spawn alcove");
                    }
                }
            }

            if (layout.Props.Count > GlobalRules.Arena.MaxPropInstances || counts.Count > GlobalRules.Arena.PropDefinitionsMax
                || counts.Count < GlobalRules.Arena.PropDefinitionsMin)
            {
                problems.Add("props: " + layout.Props.Count + " instances of " + counts.Count + " definitions");
            }

            foreach (KeyValuePair<string, int> kv in counts)
            {
                if (kv.Value % GlobalRules.Arena.PropInstancesPerDefinition != 0)
                {
                    problems.Add("props: " + kv.Key + " has an odd count " + kv.Value);
                }
            }
        }

        /// <summary>Eye to the opponent's standing capsule, against the raw solids (not the navigation colliders).</summary>
        private static void CheckSightline(ArenaLayout layout, List<string> problems)
        {
            float[] targets = { GlobalRules.Movement.StandingEyeHeight, 1.2f, 0.9f, 0.4f };
            for (int side = 0; side < 2; side++)
            {
                Vec3 eye = layout.Spawns[side].Position + new Vec3(0f, GlobalRules.Movement.StandingEyeHeight, 0f);
                for (int i = 0; i < targets.Length; i++)
                {
                    Vec3 target = layout.Spawns[1 - side].Position + new Vec3(0f, targets[i], 0f);
                    bool blocked = false;
                    foreach (Solid s in layout.Solids)
                    {
                        if (!s.BlocksSight)
                        {
                            continue;
                        }

                        bool hit = s.IsRamp
                            ? Intersections.SegmentVsRamp(eye, target, s.Ramp, out RayHit _)
                            : Intersections.SegmentVsYawBox(eye, target, s.Box, out RayHit _);
                        if (hit)
                        {
                            blocked = true;
                            break;
                        }
                    }

                    if (!blocked)
                    {
                        problems.Add("sightline: side " + side + " sees the enemy at " + targets[i] + " m");
                        return;
                    }
                }
            }
        }

        private static void CheckMetrics(ArenaLayout layout, List<string> problems)
        {
            ArenaValidationReport r = layout.Report;
            ArenaRules.SpawnPathBand(layout.Diameter, out float min, out float max);
            InRange(problems, r, "spawn_path", min, max);
            InRange(problems, r, "central_reach_0", GlobalRules.Arena.CentralReachMinSec, GlobalRules.Arena.CentralReachMaxSec);
            InRange(problems, r, "central_reach_1", GlobalRules.Arena.CentralReachMinSec, GlobalRules.Arena.CentralReachMaxSec);
            InRange(problems, r, "longest_lane", GlobalRules.Arena.FiringLaneMin, GlobalRules.Arena.FiringLaneMax);
            InRange(problems, r, "designated_lane", GlobalRules.Arena.FiringLaneMin, GlobalRules.Arena.FiringLaneMax);
            InRange(problems, r, "cover_fraction", GlobalRules.Arena.CoverFractionMin, GlobalRules.Arena.CoverFractionMax);
            InRange(problems, r, "approaches_0", GlobalRules.Arena.MinCrossoverRoutes, 99f);
            InRange(problems, r, "approaches_1", GlobalRules.Arena.MinCrossoverRoutes, 99f);
            InRange(problems, r, "lane_interrupting_approaches", GlobalRules.Arena.MinApproachesInterruptingLane, 99f);
            InRange(problems, r, "max_height", 0f, GlobalRules.Arena.VantageHeight + 0.01f);
            InRange(problems, r, "natural_sources", 0f, GlobalRules.Arena.MaxNaturalSourcePatches);
            float target = SizeProfile.For(layout.Diameter).TargetArea;
            InRange(problems, r, "usable_area", target * (1f - GlobalRules.Arena.UsableAreaTolerance), target * (1f + GlobalRules.Arena.UsableAreaTolerance));
            r.TryGetMetric("center_path_0", out float c0);
            r.TryGetMetric("center_path_1", out float c1);
            if (Math.Abs(c0 - c1) > ArenaRules.ParityDistanceTolerance)
            {
                problems.Add("metric: center paths " + c0 + " vs " + c1);
            }
        }

        private static void InRange(List<string> problems, ArenaValidationReport r, string metric, float min, float max)
        {
            if (!r.TryGetMetric(metric, out float v))
            {
                problems.Add("metric: " + metric + " missing");
            }
            else if (v < min - 1e-4f || v > max + 1e-4f)
            {
                problems.Add("metric: " + metric + " = " + v + " outside " + min + "-" + max);
            }
        }

        /// <summary>Distances from each spawn agree on mirrored nodes everywhere on the reachable floor.</summary>
        private static void CheckMirroredDistances(ArenaNav nav, ArenaSurvey survey, Result result)
        {
            int s0 = survey.SpawnNode(0);
            int s1 = survey.SpawnNode(1);
            if (s0 < 0 || s1 < 0 || nav.MirrorNode(s0) != s1)
            {
                result.Problems.Add("distances: spawn nodes are missing or not mirrored");
                return;
            }

            if (!nav.IsHalfTurnSymmetric)
            {
                result.Problems.Add("distances: the walkable graph is not half-turn symmetric");
            }

            float[] d0 = survey.DistancesFromSpawn(0);
            float[] d1 = survey.DistancesFromSpawn(1);
            int reachable = 0;
            int asymmetric = 0;
            float worst = 0f;
            for (int node = 0; node < nav.NodeCount; node++)
            {
                if (float.IsPositiveInfinity(d0[node]))
                {
                    continue;
                }

                reachable++;
                int m = nav.MirrorNode(node);
                if (m < 0 || float.IsPositiveInfinity(d1[m]))
                {
                    asymmetric++;
                    continue;
                }

                worst = Math.Max(worst, Math.Abs(d0[node] - d1[m]));
            }

            result.MaxMirrorDistanceError = worst;
            result.AsymmetricNodeFraction = reachable == 0 ? 1f : asymmetric / (float)reachable;
            if (worst > MirroredDistanceTolerance)
            {
                result.Problems.Add("distances: mirrored nodes differ by " + worst + " m");
            }

            if (asymmetric > 0)
            {
                result.Problems.Add("distances: " + asymmetric + " reachable node(s) have no reachable mirror");
            }
        }

        private static void CheckRoutes(ArenaNav nav, ArenaSurvey survey, List<string> problems)
        {
            for (int side = 0; side < 2; side++)
            {
                bool[] route = nav.Reachable(new[] { survey.SpawnNode(side) }, NavQuery.Route);
                bool reachesFloor = false;
                foreach (int node in survey.FinalFloorNodes)
                {
                    reachesFloor |= route[node];
                }

                if (!reachesFloor)
                {
                    problems.Add("route: side " + side + " has no 2.5 m route to the final floor");
                }

                if (!route[survey.SpawnNode(1 - side)])
                {
                    problems.Add("route: no 2.5 m route between the spawns");
                }
            }
        }

        /// <summary>
        /// Every 5 s of the contraction, offset from the validator's own samples, every spawn-reachable cell comfortably inside the contour can still walk to
        /// the final floor using only cells a capsule fits inside (the validator measure, at times between its samples) (spec L48, L674).
        /// </summary>
        private static void CheckContraction(ArenaLayout layout, ArenaNav nav, ArenaSurvey survey, List<string> problems)
        {
            BoundaryContour b = layout.Boundary;
            bool[] reached = survey.ReachableFromSpawns;
            var allowed = new bool[nav.NodeCount];
            for (float t = b.ContractStartSec + 1.25f; t <= b.ContractEndSec; t += 5f)
            {
                for (int node = 0; node < nav.NodeCount; node++)
                {
                    allowed[node] = b.RadialMargin(nav.NodePosition(node), t) >= ArenaRules.CapsuleClearance;
                }

                bool[] canReach = nav.CanReach(survey.FinalFloorNodes, NavQuery.Capsule.WithAllowed(allowed));
                for (int node = 0; node < nav.NodeCount; node++)
                {
                    if (reached[node] && !canReach[node] && nav.Clearance(node) >= ArenaRules.CapsuleClearance
                        && b.RadialMargin(nav.NodePosition(node), t) >= ArenaRules.ContractionEdgeTolerance)
                    {
                        problems.Add("contraction: node " + node + " at " + nav.NodePosition(node) + " is cut off at " + t + " s");
                        return;
                    }
                }
            }
        }
    }
}
