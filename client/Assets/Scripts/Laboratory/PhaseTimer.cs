using System;
using Confluence.Core.Time;

namespace Confluence.Client.Laboratory
{
    /// <summary>
    /// A match-phase countdown in whole ticks (spec L45: 35 s shop, 30 s draft). The Laboratory host advances it with
    /// the same <see cref="Confluence.Client.Movement.TickClock"/> that drives the duel, so phase deadlines are tick-exact like the
    /// server's and never read wall-clock time in rules. Laboratory sessions may pause it (spec L69 modified sessions).
    /// </summary>
    public sealed class PhaseTimer
    {
        public PhaseTimer(float seconds)
        {
            if (!(seconds > 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(seconds));
            }

            TotalTicks = Ticks.FromSeconds(seconds);
            RemainingTicks = TotalTicks;
        }

        public int TotalTicks { get; }
        public int RemainingTicks { get; private set; }
        public bool Paused { get; set; }
        public bool Expired => RemainingTicks <= 0;
        public float SecondsRemaining => RemainingTicks / (float)Ticks.PerSecond;

        /// <summary>Advances by whole ticks; returns true on the tick the timer expires.</summary>
        public bool Advance(int ticks)
        {
            if (Paused || Expired || ticks <= 0)
            {
                return false;
            }

            RemainingTicks = Math.Max(0, RemainingTicks - ticks);
            return Expired;
        }
    }
}
