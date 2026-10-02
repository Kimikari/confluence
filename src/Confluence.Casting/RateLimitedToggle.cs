using System;

namespace Confluence.Casting
{
    /// <summary>
    /// The effective state of a held grammar input under an accepted input interval (spec L478 "Input faster than
    /// the stated accepted interval provides no additional benefit"; L425 rate limits; WIZ-010 "0.1 s accepted input
    /// interval"). The effective state follows the physical key, but it can change at most once per interval: a flip
    /// requested sooner waits until the interval has passed, and a flip undone before then is dropped. Spamming the
    /// key therefore produces at most one effective change per interval, which a deliberate player gets anyway.
    /// </summary>
    public sealed class RateLimitedToggle
    {
        private readonly int _intervalTicks;
        private int _lastFlipTick;
        private bool _hasFlipped;

        public RateLimitedToggle(int intervalTicks)
        {
            if (intervalTicks < 1) throw new ArgumentOutOfRangeException(nameof(intervalTicks));
            _intervalTicks = intervalTicks;
        }

        public int IntervalTicks => _intervalTicks;

        /// <summary>The state rules code acts on.</summary>
        public bool Effective { get; private set; }

        /// <summary>Physical edges that arrived faster than the accepted interval (deferred or dropped).</summary>
        public int ThrottledChanges { get; private set; }

        public void Reset(bool state)
        {
            Effective = state;
            _hasFlipped = false;
            _lastFlipTick = 0;
            ThrottledChanges = 0;
        }

        /// <summary>
        /// Samples the physical state at <paramref name="tick"/>. <paramref name="edgesThisTick"/> counts physical edges
        /// seen this tick (a press and release inside one tick is two). Returns true when the effective state flipped.
        /// </summary>
        public bool Sample(int tick, bool physical, int edgesThisTick)
        {
            bool ready = !_hasFlipped || tick - _lastFlipTick >= _intervalTicks;
            bool flipped = false;
            if (ready && physical != Effective)
            {
                Effective = physical;
                _hasFlipped = true;
                _lastFlipTick = tick;
                flipped = true;
            }

            int accepted = flipped && edgesThisTick > 0 ? 1 : 0;
            ThrottledChanges += ready ? edgesThisTick - accepted : edgesThisTick;
            return flipped;
        }
    }
}
