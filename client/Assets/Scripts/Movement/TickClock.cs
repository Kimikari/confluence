using System;
using Confluence.Core.Time;

namespace Confluence.Client.Movement
{
    /// <summary>
    /// Turns Unity's variable frame time into whole authoritative ticks (60 per second, spec L421). Frame time only
    /// decides how many Sim steps run this frame; the rules themselves only ever see ticks (docs/ARCHITECTURE.md
    /// "Conventions"). <see cref="Alpha"/> is the fraction of the next tick already elapsed, used to interpolate
    /// rendered positions between the last two simulated states. A long hitch drops its backlog instead of running
    /// a burst of catch-up steps.
    /// </summary>
    public sealed class TickClock
    {
        public const int DefaultMaxTicksPerFrame = 8;

        private double _accumulator;

        public TickClock(int maxTicksPerFrame = DefaultMaxTicksPerFrame)
        {
            if (maxTicksPerFrame < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxTicksPerFrame));
            }

            MaxTicksPerFrame = maxTicksPerFrame;
        }

        public int MaxTicksPerFrame { get; }

        /// <summary>Fraction (0-1) of the next tick that has elapsed.</summary>
        public float Alpha => (float)Math.Min(1.0, _accumulator / Ticks.DtDouble);

        /// <summary>Adds frame time and returns how many ticks to simulate now.</summary>
        public int Advance(double deltaSeconds)
        {
            if (!(deltaSeconds > 0.0) || double.IsInfinity(deltaSeconds))
            {
                return 0;
            }

            _accumulator += deltaSeconds;
            int ticks = (int)Math.Floor(_accumulator / Ticks.DtDouble);
            if (ticks > MaxTicksPerFrame)
            {
                _accumulator = 0.0; // drop the backlog of a hitch
                return MaxTicksPerFrame;
            }

            _accumulator -= ticks * Ticks.DtDouble;
            return ticks;
        }

        public void Reset() => _accumulator = 0.0;
    }
}
