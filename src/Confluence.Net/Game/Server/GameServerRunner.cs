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
