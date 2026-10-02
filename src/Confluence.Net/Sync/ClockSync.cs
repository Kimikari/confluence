using System;

namespace Confluence.Net.Sync
{
    /// <summary>
    /// Client estimate of the server's simulation tick. Each probe yields a round-trip time and an offset
    /// between the client clock and the server tick assuming symmetric paths. Following NTP's clock filter,
    /// the offset of the lowest-RTT probe in a recent window is the target (queueing delay inflates RTT and
    /// skews offsets, so the fastest probe is the most trustworthy), and the applied offset eases toward it so
    /// the estimate does not jump. RTT is smoothed as in RFC 6298 (SRTT and RTTVAR).
    /// </summary>
    public sealed class ClockSync
    {
        /// <summary>Weight of each new target in the applied offset.</summary>
        public const double OffsetGain = 0.25;

        /// <summary>Errors larger than this snap the offset instead of easing (in milliseconds).</summary>
        public const double SnapThresholdMs = 250.0;

        private readonly double _ticksPerMs;
        private readonly double[] _offsets;
        private readonly double[] _rtts;
        private int _count;
        private int _next;
        private double _appliedOffsetTicks;

        public ClockSync(int tickRate = NetRules.TickRate, int window = 16)
        {
            if (tickRate <= 0) throw new ArgumentOutOfRangeException(nameof(tickRate));
            if (window <= 0) throw new ArgumentOutOfRangeException(nameof(window));
            TickRate = tickRate;
            _ticksPerMs = tickRate / 1000.0;
            _offsets = new double[window];
            _rtts = new double[window];
        }

        public int TickRate { get; }

        public bool HasEstimate { get; private set; }

        /// <summary>Probes accepted since the last reset.</summary>
        public int SampleCount { get; private set; }

        public double SmoothedRttMs { get; private set; }

        public double RttVarianceMs { get; private set; }

        /// <summary>Lowest RTT in the current window.</summary>
        public double MinRttMs { get; private set; }

        /// <summary>Most recent probe RTT.</summary>
        public double LastRttMs { get; private set; }

        /// <summary>Applied offset: server tick minus client time expressed in ticks.</summary>
        public double OffsetTicks => _appliedOffsetTicks;

        public void Reset()
        {
            _count = 0;
            _next = 0;
            HasEstimate = false;
            SampleCount = 0;
            SmoothedRttMs = 0;
            RttVarianceMs = 0;
            MinRttMs = 0;
            LastRttMs = 0;
            _appliedOffsetTicks = 0;
        }

        /// <summary>
        /// Coarse initial estimate before any probe completes (from the welcome message and the transport's own
        /// one-way latency estimate). Ignored once probes exist.
        /// </summary>
        public void Seed(double serverTick, long clientNowMs, double oneWayMs)
        {
            if (SampleCount > 0) return;
            _appliedOffsetTicks = serverTick + Math.Max(0, oneWayMs) * _ticksPerMs - clientNowMs * _ticksPerMs;
            HasEstimate = true;
        }

        /// <summary>
        /// Adds a completed probe: the client sent at <paramref name="clientSendMs"/>, the server stamped
        /// <paramref name="serverTick"/>, and the reply arrived at <paramref name="clientReceiveMs"/>.
        /// Returns false for impossible samples (negative RTT).
        /// </summary>
        public bool AddSample(long clientSendMs, long clientReceiveMs, double serverTick)
        {
            long rtt = clientReceiveMs - clientSendMs;
            if (rtt < 0 || double.IsNaN(serverTick) || double.IsInfinity(serverTick))
            {
                return false;
            }

            LastRttMs = rtt;
            double offset = serverTick + rtt * 0.5 * _ticksPerMs - clientReceiveMs * _ticksPerMs;
            _offsets[_next] = offset;
            _rtts[_next] = rtt;
            _next = (_next + 1) % _offsets.Length;
            if (_count < _offsets.Length) _count++;

            UpdateRtt(rtt);
            double target = BestOffset();
            if (SampleCount == 0 || Math.Abs(target - _appliedOffsetTicks) > SnapThresholdMs * _ticksPerMs)
            {
                _appliedOffsetTicks = target;
            }
            else
            {
                _appliedOffsetTicks += (target - _appliedOffsetTicks) * OffsetGain;
            }

            SampleCount++;
            HasEstimate = true;
            return true;
        }

        /// <summary>Estimated current server tick (fractional) at client time <paramref name="clientNowMs"/>.</summary>
        public double EstimateServerTick(long clientNowMs) => clientNowMs * _ticksPerMs + _appliedOffsetTicks;

        /// <summary>Render time for remote entities: the server estimate minus the interpolation delay.</summary>
        public double InterpolationTick(long clientNowMs, int delayMs = NetRules.InterpolationDelayMs)
        {
            return EstimateServerTick(clientNowMs) - delayMs * _ticksPerMs;
        }

        /// <summary>
        /// Ticks ahead of the server estimate the client should stamp its inputs so they arrive before the server
        /// simulates them: one-way delay plus two RTT deviations of jitter margin, plus one tick.
        /// </summary>
        public int RecommendedInputLeadTicks
        {
            get
            {
                double ms = SmoothedRttMs * 0.5 + 2.0 * RttVarianceMs;
                return (int)Math.Ceiling(ms * _ticksPerMs) + 1;
            }
        }

        private void UpdateRtt(double rtt)
        {
            if (SampleCount == 0)
            {
                SmoothedRttMs = rtt;
                RttVarianceMs = rtt * 0.5;
            }
            else
            {
                RttVarianceMs = 0.75 * RttVarianceMs + 0.25 * Math.Abs(SmoothedRttMs - rtt);
                SmoothedRttMs = 0.875 * SmoothedRttMs + 0.125 * rtt;
            }
        }

        private double BestOffset()
        {
            // Newest first, strict comparison: among equal RTTs the most recent probe wins, so a slowly drifting
            // server clock is tracked rather than pinned to an old sample.
            int best = -1;
            for (int k = 0; k < _count; k++)
            {
                int i = (_next - 1 - k + _offsets.Length) % _offsets.Length;
                if (best < 0 || _rtts[i] < _rtts[best])
                {
                    best = i;
                }
            }

            MinRttMs = _rtts[best];
            return _offsets[best];
        }
    }
}
