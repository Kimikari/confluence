using System;
using System.Diagnostics;
using System.Threading;
using Confluence.Net.Transport;

namespace Confluence.Net.Game.Server
{
    /// <summary>
    /// Real-time pacing of a <see cref="GameServer"/>: authoritative ticks at the configured pace (60 per second in real
    /// time, spec L421; a faster labeled pace in the accelerated test mode), the transport polled continuously in between,
    /// and a bounded catch-up after a stall (late ticks run back to back, at most <see cref="MaxCatchUpTicks"/> per
    /// iteration; beyond a quarter second behind the schedule restarts and the stall is logged).
    /// </summary>
    public sealed class GameServerRunner
    {
        public const int MaxCatchUpTicks = 8;
        private const double MaxLagMs = 250.0;

        private readonly GameServer _server;
        private readonly INetClock _clock;
        private readonly Action<string> _log;
        private double _nextTickMs = double.NaN;

        public GameServerRunner(GameServer server, INetClock clock = null, Action<string> log = null)
        {
            _server = server ?? throw new ArgumentNullException(nameof(server));
            _clock = clock ?? SystemNetClock.Instance;
            _log = log;
        }

        public double TickIntervalMs => 1000.0 / _server.Settings.TicksPerWallSecond;

        /// <summary>Times the schedule was reset after falling more than 250 ms behind.</summary>
        public int Stalls { get; private set; }

        private readonly double[] _lateness = new double[4096];
        private int _latenessCount;
        private int _latenessNext;

        /// <summary>Ticks run so far by this runner.</summary>
        public long TicksRun { get; private set; }

        /// <summary>
        /// How late ticks ran against their schedule over the last 4096 ticks (ms): the timer-resolution check of research
        /// §2.8.6 (p99 within about 2 ms with a 1 ms system timer).
        /// </summary>
        public string LatenessSummary()
        {
            if (_latenessCount == 0) return "no ticks";
            var sorted = new double[_latenessCount];
            Array.Copy(_lateness, sorted, _latenessCount);
            Array.Sort(sorted);
            double P(double q) => sorted[Math.Min(_latenessCount - 1, (int)Math.Ceiling(_latenessCount * q) - 1)];
            return "tick lateness p50 " + P(0.5).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " ms, p99 " +
                P(0.99).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " ms, max " +
                sorted[_latenessCount - 1].ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " ms over " + _latenessCount + " ticks";
        }

        /// <summary>One loop iteration: poll, run the ticks that are due, flush. Returns the ticks run.</summary>
        public int Step()
        {
            _server.Poll();
            double now = _clock.NowMs;
            if (double.IsNaN(_nextTickMs) || _server.Status != Wire.LobbyStatus.Running)
            {
                _nextTickMs = now + TickIntervalMs;
                _server.Flush();
                return 0;
            }

            int ran = 0;
            while (now >= _nextTickMs && ran < MaxCatchUpTicks)
            {
                _lateness[_latenessNext] = now - _nextTickMs;
                _latenessNext = (_latenessNext + 1) % _lateness.Length;
                if (_latenessCount < _lateness.Length) _latenessCount++;
                TicksRun++;
                _server.Tick();
                _nextTickMs += TickIntervalMs;
                ran++;
                if (ran % 2 == 0) _server.Poll();
            }

            if (now - _nextTickMs > MaxLagMs)
            {
                Stalls++;
                _log?.Invoke("Server fell " + (int)(now - _nextTickMs) + " ms behind its tick schedule; resynchronizing.");
                _nextTickMs = now + TickIntervalMs;
            }

            _server.Flush();
            return ran;
        }

        /// <summary>Runs until <paramref name="keepRunning"/> returns false or the match is over (plus a drain period).</summary>
        public void Run(Func<bool> keepRunning, int drainMs = 3000)
        {
            var finishedAt = Stopwatch.StartNew();
            bool finished = false;
            while (keepRunning == null || keepRunning())
            {
                Step();
                if (_server.IsFinished)
                {
                    if (!finished)
                    {
                        finished = true;
                        finishedAt.Restart();
                    }
                    else if (finishedAt.ElapsedMilliseconds > drainMs)
                    {
                        break;
                    }
                }

                double wait = _nextTickMs - _clock.NowMs;
                if (wait > 3) Thread.Sleep(1);
                else Thread.Yield();
            }
        }
    }
}
