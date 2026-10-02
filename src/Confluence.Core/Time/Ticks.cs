using System;

namespace Confluence.Core.Time
{
    /// <summary>
    /// Fixed simulation time base: 60 authoritative steps per second (spec L421). Gameplay timers are integer
    /// ticks; content durations in seconds convert with <see cref="FromSeconds(double)"/>.
    /// </summary>
    public static class Ticks
    {
        public const int PerSecond = 60; // spec L421
        public const float Dt = 1f / PerSecond;
        public const double DtDouble = 1.0 / PerSecond;

        // Relative slack that absorbs binary representation error (including float-sourced durations), so a
        // decimal half-tick duration such as 0.175 s (10.5 ticks) rounds up as written.
        private const double RelativeSlack = 1e-6;

        /// <summary>
        /// Converts seconds to ticks, rounding to the nearest tick (halves round up). Any positive duration
        /// lasts at least one tick; zero, negative, and NaN durations are zero ticks. Saturates at int.MaxValue.
        /// </summary>
        public static int FromSeconds(double seconds)
        {
            if (!(seconds > 0.0))
            {
                return 0;
            }

            double exact = seconds * PerSecond;
            double ticks = Math.Floor(exact + 0.5 + Math.Max(1e-9, exact * RelativeSlack));
            if (ticks >= int.MaxValue)
            {
                return int.MaxValue;
            }

            return ticks < 1.0 ? 1 : (int)ticks;
        }

        public static int FromSeconds(float seconds) => FromSeconds((double)seconds);

        public static int FromMilliseconds(int milliseconds) => FromSeconds(milliseconds / 1000.0);

        public static float ToSeconds(int ticks) => ticks / (float)PerSecond; // division keeps whole seconds exact

        public static double ToSecondsDouble(int ticks) => ticks / (double)PerSecond;
    }
}
