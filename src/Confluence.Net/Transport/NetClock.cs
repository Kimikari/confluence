using System.Diagnostics;

namespace Confluence.Net.Transport
{
    /// <summary>
    /// Monotonic millisecond clock for transport timing: LiteNetLib updates, the reconnect window, rate limits,
    /// and clock synchronization. Rule code never reads it; the simulation advances in integer ticks.
    /// Injectable so tests can jump time (for example past the 90 s reconnect window).
    /// </summary>
    public interface INetClock
    {
        long NowMs { get; }
    }

    /// <summary>Process-monotonic clock based on the high-resolution performance counter.</summary>
    public sealed class SystemNetClock : INetClock
    {
        public static readonly SystemNetClock Instance = new SystemNetClock();

        private static readonly double MsPerTimestamp = 1000.0 / Stopwatch.Frequency;

        public long NowMs => (long)(Stopwatch.GetTimestamp() * MsPerTimestamp);
    }

    /// <summary>A clock that follows another clock plus an adjustable offset (tests skip time forward).</summary>
    public sealed class OffsetNetClock : INetClock
    {
        private readonly INetClock _inner;
        private long _offsetMs;

        public OffsetNetClock(INetClock inner = null)
        {
            _inner = inner ?? SystemNetClock.Instance;
        }

        public long NowMs => _inner.NowMs + _offsetMs;

        /// <summary>Moves time forward instantly; negative values are ignored to keep the clock monotonic.</summary>
        public void Advance(long milliseconds)
        {
            if (milliseconds > 0)
            {
                _offsetMs += milliseconds;
            }
        }
    }

    /// <summary>Fully manual clock for logic tests that involve no sockets.</summary>
    public sealed class ManualNetClock : INetClock
    {
        public ManualNetClock(long startMs = 0)
        {
            NowMs = startMs;
        }

        public long NowMs { get; private set; }

        public void Advance(long milliseconds)
        {
            if (milliseconds > 0)
            {
                NowMs += milliseconds;
            }
        }
    }

    /// <summary>Source of the server's current simulation time in fractional ticks.</summary>
    public interface ITickClock
    {
        double CurrentTick { get; }
    }

    /// <summary>
    /// Default tick clock: ticks elapsed on a network clock since construction at <see cref="NetRules.TickRate"/>.
    /// The match server replaces it with its simulation's tick counter.
    /// </summary>
    public sealed class ElapsedTickClock : ITickClock
    {
        private readonly INetClock _clock;
        private readonly long _startMs;

        public ElapsedTickClock(INetClock clock)
        {
            _clock = clock;
            _startMs = clock.NowMs;
        }

        public double CurrentTick => (_clock.NowMs - _startMs) * (NetRules.TickRate / 1000.0);
    }
}
