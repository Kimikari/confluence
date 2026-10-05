using System;

namespace Confluence.Net.Game.Client
{
    /// <summary>
    /// Adaptive interpolation delay (docs/research/netcode.md §2.2.1): remote entities render behind the server clock
    /// estimate by the 95th percentile of measured snapshot lateness plus one snapshot interval, clamped to
    /// [<see cref="MinDelayTicks"/>, <see cref="MaxDelayTicks"/>] (50-100 ms; ARCHITECTURE "about 100 ms" is the ceiling).
    /// The delay never jumps: it moves by at most <see cref="SlewFraction"/> of elapsed time, which time-scales the render
    /// clock by a few percent. A smaller delay leaves more of the 150 ms rewind (spec L423) for network latency.
    /// <para>
    /// Decision (research §5): one interval of headroom, not Gaffer's two lost packets. A lost snapshot then briefly
    /// holds the newest state (<c>SampleKind.Starved</c>) and the client reports the held tick to lag compensation
    /// (<see cref="ClientDuel.DisplayedViewTick"/>), so a starved frame never claims a view it did not show.
    /// </para>
    /// </summary>
    public sealed class InterpolationClock
    {
        public const double MinDelayTicks = 3.0;
        public const double MaxDelayTicks = 6.0;
        public const double SlewFraction = 0.05;
        private const int Window = 64;

        private readonly double[] _lateness = new double[Window];
        private readonly double[] _scratch = new double[Window];
        private readonly int[] _gaps = new int[16];
        private int _count;
        private int _next;
        private int _gapCount;
        private int _gapNext;
        private int _lastTick = -1;

        public InterpolationClock(double maxDelayTicks = MaxDelayTicks, double minDelayTicks = MinDelayTicks)
        {
            if (minDelayTicks <= 0 || maxDelayTicks < minDelayTicks) throw new ArgumentOutOfRangeException(nameof(minDelayTicks));
            Max = maxDelayTicks;
            Min = minDelayTicks;
            DelayTicks = maxDelayTicks;
            TargetTicks = maxDelayTicks;
        }

        public double Min { get; }
        public double Max { get; }

        /// <summary>The current interpolation delay in simulation ticks.</summary>
        public double DelayTicks { get; private set; }

        /// <summary>The delay the clock is slewing towards.</summary>
        public double TargetTicks { get; private set; }

        /// <summary>95th percentile of snapshot lateness over the window (ticks).</summary>
        public double LatenessP95 { get; private set; }

        /// <summary>Median spacing of recent snapshots (ticks): 2 at 30 Hz, 3 at 20 Hz.</summary>
        public int IntervalTicks { get; private set; } = 2;

        public int Samples => _count;

        /// <summary>
        /// Records one snapshot's lateness: the estimated server tick at arrival minus the snapshot's tick (both in the
        /// same clock), and its tick for the interval estimate.
        /// </summary>
        public void OnSnapshot(int tick, double latenessTicks)
        {
            if (double.IsNaN(latenessTicks) || double.IsInfinity(latenessTicks)) return;
            if (_lastTick >= 0 && tick > _lastTick)
            {
                _gaps[_gapNext] = Math.Min(30, tick - _lastTick);
                _gapNext = (_gapNext + 1) % _gaps.Length;
                if (_gapCount < _gaps.Length) _gapCount++;
                var g = new int[_gapCount];
                Array.Copy(_gaps, g, _gapCount);
                Array.Sort(g);
                IntervalTicks = Math.Max(1, g[_gapCount / 2]);
            }

            if (tick > _lastTick) _lastTick = tick;
            _lateness[_next] = Math.Max(0, latenessTicks);
            _next = (_next + 1) % Window;
            if (_count < Window) _count++;
            Array.Copy(_lateness, _scratch, _count);
            Array.Sort(_scratch, 0, _count);
            LatenessP95 = _scratch[Math.Min(_count - 1, (int)Math.Ceiling(_count * 0.95) - 1)];
            if (_count >= 8) TargetTicks = Math.Max(Min, Math.Min(Max, LatenessP95 + IntervalTicks));
        }

        /// <summary>Slews the delay towards the target over <paramref name="elapsedTicks"/> of simulation time.</summary>
        public void Advance(double elapsedTicks)
        {
            if (!(elapsedTicks > 0)) return;
            double step = elapsedTicks * SlewFraction;
            double d = TargetTicks - DelayTicks;
            DelayTicks += Math.Max(-step, Math.Min(step, d));
        }

        public void Reset()
        {
            _count = 0;
            _next = 0;
            _gapCount = 0;
            _gapNext = 0;
            _lastTick = -1;
            DelayTicks = Max;
            TargetTicks = Max;
            LatenessP95 = 0;
        }
    }

    /// <summary>
    /// Closed-loop input lead (docs/research/netcode.md §2.1.1): the server consumes one command per step by sequence and
    /// reports its queue depth and starvation count in every snapshot. Every half second the client looks at the smallest
    /// queue depth of the last second: deeper than <see cref="NetRules.InputQueueTargetTicks"/> means its commands wait
    /// (added latency), so it produces one command less (a 3% dilation); starved ticks with an empty queue mean its
    /// commands arrive late, so it produces one more. The queue therefore returns to about one tick after any hiccup
    /// instead of keeping the slip for the rest of the session.
    /// </summary>
    public sealed class InputLeadControl
    {
        public const int EvaluateEverySnapshots = 15;
        private const int Window = 30;

        private readonly int[] _queue = new int[Window];
        private int _count;
        private int _next;
        private int _sinceEval;
        private long _starvedAtEval = -1;
        private long _lastStarved;

        /// <summary>Lead adjustment in ticks added to the base lead.</summary>
        public int Adjust { get; private set; }

        public int Increases { get; private set; }
        public int Decreases { get; private set; }

        /// <summary>The newest reported server queue depth.</summary>
        public int LastQueue { get; private set; }

        /// <summary>Smallest queue depth over the window at the last evaluation.</summary>
        public int MinQueue { get; private set; }

        public void OnSnapshot(int queueDepth, long starvedTicks, int minAdjust, int maxAdjust)
        {
            LastQueue = queueDepth;
            _queue[_next] = queueDepth;
            _next = (_next + 1) % Window;
            if (_count < Window) _count++;
            _lastStarved = starvedTicks;
            if (_starvedAtEval < 0) _starvedAtEval = starvedTicks;
            if (++_sinceEval < EvaluateEverySnapshots) return;
            _sinceEval = 0;
            int min = int.MaxValue;
            for (int i = 0; i < _count; i++) min = Math.Min(min, _queue[i]);
            MinQueue = min;
            long starved = _lastStarved - _starvedAtEval;
            _starvedAtEval = _lastStarved;
            if (starved > 0 && min == 0)
            {
                if (Adjust < maxAdjust)
                {
                    Adjust++;
                    Increases++;
                    _count = 0; // the window predates the change
                    _next = 0;
                }
            }
            else if (min > NetRules.InputQueueTargetTicks)
            {
                if (Adjust > minAdjust)
                {
                    Adjust--;
                    Decreases++;
                    _count = 0;
                    _next = 0;
                }
            }
        }

        public void Reset()
        {
            _count = 0;
            _next = 0;
            _sinceEval = 0;
            _starvedAtEval = -1;
            Adjust = 0;
        }
    }
}
