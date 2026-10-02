using System;
using System.Collections.Generic;
using Confluence.Core.Time;

namespace Confluence.Net.Sync
{
    /// <summary>
    /// Holds spectator-bound items (snapshots, combat events) until they are at least the spectator delay old
    /// (spec L391: a minimum 15 second delay while a match remains active; private hosts may choose longer).
    /// Shorter requested delays are raised to the minimum. Items must arrive in non-decreasing tick order.
    /// </summary>
    public sealed class SpectatorDelayBuffer<T>
    {
        private readonly Queue<KeyValuePair<int, T>> _queue = new Queue<KeyValuePair<int, T>>();
        private int _lastTick = int.MinValue;

        public SpectatorDelayBuffer(int delayTicks)
        {
            DelayTicks = Math.Max(delayTicks, NetRules.SpectatorDelayMinTicks);
        }

        public static SpectatorDelayBuffer<T> ForSeconds(float delaySeconds) => new SpectatorDelayBuffer<T>(Ticks.FromSeconds(delaySeconds));

        public int DelayTicks { get; }

        public int Count => _queue.Count;

        public void Push(int tick, T item)
        {
            if (tick < _lastTick) throw new ArgumentException("Spectator items must arrive in tick order.", nameof(tick));
            _lastTick = tick;
            _queue.Enqueue(new KeyValuePair<int, T>(tick, item));
        }

        /// <summary>Moves every item at least <see cref="DelayTicks"/> old at <paramref name="currentTick"/> to <paramref name="output"/>.</summary>
        public int Release(int currentTick, List<T> output)
        {
            long releaseThrough = (long)currentTick - DelayTicks;
            int released = 0;
            while (_queue.Count > 0 && _queue.Peek().Key <= releaseThrough)
            {
                output.Add(_queue.Dequeue().Value);
                released++;
            }

            return released;
        }

        /// <summary>Releases everything; only valid once the match is over and the delay no longer applies.</summary>
        public int ReleaseAll(List<T> output)
        {
            int released = _queue.Count;
            while (_queue.Count > 0)
            {
                output.Add(_queue.Dequeue().Value);
            }

            return released;
        }
    }
}
