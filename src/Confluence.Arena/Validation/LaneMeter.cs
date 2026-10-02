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
            : this(nav, layout, -1, -1)
        {
        }

        /// <summary>
        /// Meter that ignores up to two solids as blockers while keeping the navigation's standable floor: a lower
        /// bound on the lanes after removing them (their footprints only add standable floor), so a removal it
        /// already finds too open can be refused without rebuilding the navigation.
        /// </summary>
        public LaneMeter(ArenaNav nav, ArenaLayout layout, int ignoredSolidA, int ignoredSolidB)
        {
            _nav = nav;
            _contour = layout.Boundary;
            _blockers = nav.BandBlockers(ArenaRules.LaneBandLow, ArenaRules.LaneBandHigh, ignoredSolidA, ignoredSolidB);
            _reach = _contour.MaxRadiusAt(0f) + 0.5f;
            _centers = new Vec2[_blockers.Count];
            _radii = new float[_blockers.Count];
            for (int i = 0; i < _blockers.Count; i++)
            {
                YawBox b = _blockers[i];
                _centers[i] = b.Center.XZ();
                _radii[i] = MathF.Sqrt(b.HalfExtents.X * b.HalfExtents.X + b.HalfExtents.Z * b.HalfExtents.Z);
                _maxRadius = Math.Max(_maxRadius, _radii[i]);
            }
        }

        private readonly Vec2[] _centers;
        private readonly float[] _radii;
        private float _maxRadius;

        /// <summary>
        /// Blockers sorted by their offset across one sweep direction, so each line tests only the blockers whose
        /// bounding circle it can cross (the result is identical to testing them all).
        /// </summary>
        private sealed class DirectionIndex
        {
            public int[] Order;
            public float[] Offset;
        }

        private DirectionIndex IndexFor(Vec2 n)
        {
            int count = _blockers.Count;
            var order = new int[count];
            var offset = new float[count];
            for (int i = 0; i < count; i++)
            {
                order[i] = i;
                offset[i] = Vec2.Dot(_centers[i], n);
            }

            Array.Sort((float[])offset.Clone(), order);
            var sorted = new float[count];
            for (int i = 0; i < count; i++)
            {
                sorted[i] = offset[order[i]];
            }

            return new DirectionIndex { Order = order, Offset = sorted };
        }

        private void Candidates(DirectionIndex index, float s, List<int> result)
        {
            result.Clear();
            float lo = s - _maxRadius - 1e-3f;
            int start = LowerBound(index.Offset, lo);
            for (int k = start; k < index.Offset.Length && index.Offset[k] <= s + _maxRadius + 1e-3f; k++)
            {
                int i = index.Order[k];
                if (Math.Abs(index.Offset[k] - s) <= _radii[i] + 1e-3f)
                {
                    result.Add(i);
                }
            }

            result.Sort();
        }

        private static int LowerBound(float[] sorted, float value)
        {
            int lo = 0;
            int hi = sorted.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (sorted[mid] < value)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            return lo;
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

        /// <summary>
        /// Longest open lane among the sampled lines that cross a footprint (the lines a removed obstacle could open):
        /// the same lattice as <see cref="Longest"/>, restricted to those lines.
        /// </summary>
        public FiringLane LongestThrough(YawBox region)
        {
            var intervals = new List<Vec2>(64);
            var best = default(FiringLane);
            float bestLength = -1f;
            for (int k = 0; k < ArenaRules.LaneDirections; k++)
            {
                float yaw = k * 180f / ArenaRules.LaneDirections;
                Vec2 d = Vec2.FromYaw(yaw);
                Vec2 n = new Vec2(d.Y, -d.X);
                float lo = float.PositiveInfinity;
                float hi = float.NegativeInfinity;
                for (int c = 0; c < 4; c++)
                {
                    float proj = Vec2.Dot(region.FootprintCorner(c), n);
                    lo = Math.Min(lo, proj);
                    hi = Math.Max(hi, proj);
                }

                DirectionIndex index = IndexFor(n);
                var candidates = new List<int>(32);
                for (float s = -_reach; s <= _reach; s += ArenaRules.LaneLineSpacing)
                {
                    if (s < lo - ArenaRules.LaneLineSpacing || s > hi + ArenaRules.LaneLineSpacing)
                    {
                        continue;
                    }

                    Candidates(index, s, candidates);
                    FiringLane lane = LongestOnLine(n * s, d, intervals, float.NaN, candidates);
                    if (!lane.IsEmpty && lane.Length > bestLength)
                    {
                        bestLength = lane.Length;
                        best = lane;
                    }
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
            var candidates = new List<int>(32);
            for (int k = 0; k < ArenaRules.LaneDirections; k++)
            {
                float yaw = k * 180f / ArenaRules.LaneDirections;
                Vec2 d = Vec2.FromYaw(yaw);
                Vec2 n = new Vec2(d.Y, -d.X);
                DirectionIndex index = IndexFor(n);
                for (float s = -_reach; s <= _reach; s += ArenaRules.LaneLineSpacing)
                {
                    Candidates(index, s, candidates);
                    FiringLane lane = LongestOnLine(n * s, d, intervals, float.NaN, candidates);
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
        private FiringLane LongestOnLine(Vec2 origin, Vec2 d, List<Vec2> intervals, float mustContain, List<int> candidates = null)
        {
            intervals.Clear();
            int count = candidates == null ? _blockers.Count : candidates.Count;
            for (int c = 0; c < count; c++)
            {
                int i = candidates == null ? c : candidates[c];
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
            // The first standable sample from the start and the last from the end (the same pair a full scan finds).
            float first = float.NaN;
            float last = float.NaN;
            float start = MathF.Ceiling(t0 / SampleStep) * SampleStep;
            int samples = 0;
            for (float t = start; t <= t1; t += SampleStep)
            {
                samples++;
            }

            for (int k = 0; k < samples; k++)
            {
                float t = start + k * SampleStep;
                if (IsStandable(origin + d * t))
                {
                    first = t;
                    break;
                }
            }

            for (int k = samples - 1; k >= 0 && !float.IsNaN(first); k--)
            {
                float t = start + k * SampleStep;
                if (IsStandable(origin + d * t))
                {
                    last = t;
                    break;
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
