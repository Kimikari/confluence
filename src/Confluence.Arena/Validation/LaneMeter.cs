using System;
using System.Collections.Generic;
using Confluence.Arena.Navigation;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Arena.Validation
{
    /// <summary>An open firing lane: a straight segment between two standing positions on the main floor.</summary>
    public struct FiringLane
    {
        public Vec2 A;
        public Vec2 B;

        public float Length => Vec2.Distance(A, B);

        public bool IsEmpty => A == B;
    }

    /// <summary>
    /// Measures firing lanes (spec L310 "The longest uninterrupted firing lane is 12 to 28 meters"). A lane is a
    /// straight line along which standing players on the main floor see and shoot each other: no sight-blocking solid
    /// spans the chest-to-eye band (1.2-1.6 m, D-024) anywhere between its two endpoints, and both endpoints are
    /// standable floor inside the initial contour. Low cover (1 m) does not interrupt a lane; props never count (they
    /// move and break). Lines are swept every 7.5 degrees and every 0.5 m across the arena.
    /// </summary>
    public sealed class LaneMeter
    {
        private const float SampleStep = 0.25f;

        private readonly ArenaNav _nav;
        private readonly BoundaryContour _contour;
        private readonly List<YawBox> _blockers;
        private readonly float _reach;

        public LaneMeter(ArenaNav nav, ArenaLayout layout)
        {
            _nav = nav;
            _contour = layout.Boundary;
            _blockers = nav.BandBlockers(ArenaRules.LaneBandLow, ArenaRules.LaneBandHigh);
            _reach = _contour.MaxRadiusAt(0f) + 0.5f;
        }

        /// <summary>Longest open lane over every sampled line (and its endpoints).</summary>
        public FiringLane Longest()
        {
            var best = default(FiringLane);
            float bestLength = -1f;
            foreach (FiringLane lane in Sweep())
            {
                if (lane.Length > bestLength)
                {
                    bestLength = lane.Length;
                    best = lane;
                }
            }

            return best;
        }

        /// <summary>The longest lane of every sampled line that is longer than a threshold, longest first.</summary>
        public List<FiringLane> LongerThan(float threshold)
        {
            var result = new List<FiringLane>();
            foreach (FiringLane lane in Sweep())
            {
                if (lane.Length > threshold)
                {
                    result.Add(lane);
                }
            }

            result.Sort((a, b) => b.Length.CompareTo(a.Length));
            return result;
        }

        private IEnumerable<FiringLane> Sweep()
        {
            var intervals = new List<Vec2>(64);
            for (int k = 0; k < ArenaRules.LaneDirections; k++)
            {
                float yaw = k * 180f / ArenaRules.LaneDirections;
                Vec2 d = Vec2.FromYaw(yaw);
                Vec2 n = new Vec2(d.Y, -d.X);
                for (float s = -_reach; s <= _reach; s += ArenaRules.LaneLineSpacing)
                {
                    FiringLane lane = LongestOnLine(n * s, d, intervals, float.NaN);
                    if (!lane.IsEmpty)
                    {
                        yield return lane;
                    }
                }
            }
        }

        /// <summary>True when a footprint crosses the lane segment, so it interrupts the lane.</summary>
        public static bool Interrupts(FiringLane lane, YawBox box)
        {
            Vec2 d = lane.B - lane.A;
            float length = d.Length();
            if (length < 1e-4f || !LineVsBox(lane.A, d / length, box, out float t0, out float t1))
            {
                return false;
            }

            return t1 >= 0f && t0 <= length;
        }

        /// <summary>
        /// The designated skeleton lane: the open run along the X axis through the arena center (spec L308 "one longer
        /// firing lane"). Empty when the center itself is blocked.
        /// </summary>
        public FiringLane Designated()
        {
            return LongestOnLine(Vec2.Zero, new Vec2(1f, 0f), new List<Vec2>(64), 0f);
        }

        /// <summary>
        /// Longest open run on the line origin + t * d. When mustContain is a number, only the run covering that t
        /// counts.
        /// </summary>
        private FiringLane LongestOnLine(Vec2 origin, Vec2 d, List<Vec2> intervals, float mustContain)
        {
            intervals.Clear();
            for (int i = 0; i < _blockers.Count; i++)
            {
                if (LineVsBox(origin, d, _blockers[i], out float t0, out float t1) && t1 >= -_reach && t0 <= _reach)
                {
                    intervals.Add(new Vec2(t0, t1));
                }
            }

            intervals.Sort((a, b) => a.X.CompareTo(b.X));
            var best = default(FiringLane);
            float bestLength = -1f;
            float cursor = -_reach;
            for (int i = 0; i <= intervals.Count; i++)
            {
                // Open runs are the gaps between merged blocked intervals.
                float runEnd = i < intervals.Count ? intervals[i].X : _reach;
                if (runEnd > cursor && (float.IsNaN(mustContain) || (mustContain >= cursor && mustContain <= runEnd)))
                {
                    FiringLane lane = StandableSpan(origin, d, cursor, Math.Min(runEnd, _reach));
                    if (!lane.IsEmpty && lane.Length > bestLength)
                    {
                        bestLength = lane.Length;
                        best = lane;
                    }
                }

                if (i < intervals.Count)
                {
                    cursor = Math.Max(cursor, intervals[i].Y);
                }
            }

            return best;
        }

        /// <summary>First and last standable main-floor sample inside the contour within [t0, t1].</summary>
        private FiringLane StandableSpan(Vec2 origin, Vec2 d, float t0, float t1)
        {
            float first = float.NaN;
            float last = float.NaN;
            float start = MathF.Ceiling(t0 / SampleStep) * SampleStep;
            for (float t = start; t <= t1; t += SampleStep)
            {
                if (IsStandable(origin + d * t))
                {
                    first = float.IsNaN(first) ? t : first;
                    last = t;
                }
            }

            if (float.IsNaN(first) || last <= first)
            {
                return default(FiringLane);
            }

            return new FiringLane { A = origin + d * first, B = origin + d * last };
        }

        private bool IsStandable(Vec2 p)
        {
            if (_contour.RadialMargin(p.ToXZ(), 0f) < 0f)
            {
                return false;
            }

            int column = _nav.ColumnAt(p);
            if (column < 0)
            {
                return false;
            }

            int node = _nav.NodeInColumnNear(column, 0f, ArenaRules.FloorLevelTolerance);
            return node >= 0 && _nav.Clearance(node) >= ArenaRules.CapsuleClearance;
        }

        /// <summary>Parameter interval where the line origin + t * d crosses a yaw box footprint.</summary>
        public static bool LineVsBox(Vec2 origin, Vec2 d, YawBox box, out float t0, out float t1)
        {
            Vec3 lo = box.ToLocal(origin.ToXZ(box.Center.Y));
            Vec3 ld = box.DirectionToLocal(d.ToXZ());
            t0 = float.NegativeInfinity;
            t1 = float.PositiveInfinity;
            if (!Slab(lo.X, ld.X, box.HalfExtents.X, ref t0, ref t1) || !Slab(lo.Z, ld.Z, box.HalfExtents.Z, ref t0, ref t1))
            {
                return false;
            }

            return t1 >= t0;
        }

        private static bool Slab(float o, float d, float h, ref float t0, ref float t1)
        {
            if (MathF.Abs(d) < 1e-7f)
            {
                return MathF.Abs(o) <= h;
            }

            float a = (-h - o) / d;
            float b = (h - o) / d;
            if (a > b)
            {
                float tmp = a;
                a = b;
                b = tmp;
            }

            t0 = Math.Max(t0, a);
            t1 = Math.Min(t1, b);
            return t0 <= t1;
        }
    }
}
