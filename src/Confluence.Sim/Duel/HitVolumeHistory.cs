using System;
using Confluence.Core.Geometry;

namespace Confluence.Sim.Duel
{
    /// <summary>
    /// Lag compensation (spec L423): a bounded ring of player hit volumes for the last 150 ms (9 ticks). Hitscan and
    /// melee validation may rewind a player's capsule to the tick the attacker was viewing; projectiles, fields, and
    /// props are never rewound.
    /// </summary>
    public sealed class HitVolumeHistory
    {
        private readonly int _depth;
        private readonly int[] _ticks;
        private readonly Capsule[][] _volumes;
        private readonly int _players;

        public HitVolumeHistory(int players, int maxRewindTicks)
        {
            if (players <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(players));
            }

            _players = players;
            _depth = Math.Max(0, maxRewindTicks) + 1;
            _ticks = new int[_depth];
            _volumes = new Capsule[_depth][];
            for (int i = 0; i < _depth; i++)
            {
                _ticks[i] = int.MinValue;
                _volumes[i] = new Capsule[players];
            }
        }

        public int MaxRewindTicks => _depth - 1;

        public int LatestTick { get; private set; } = int.MinValue;

        /// <summary>Stores every player's hit volume for a tick (after movement, tick-order step 2).</summary>
        public void Record(int tick, Capsule[] volumes)
        {
            if (volumes == null || volumes.Length != _players)
            {
                throw new ArgumentException("One volume per player is required.", nameof(volumes));
            }

            int slot = Slot(tick);
            _ticks[slot] = tick;
            Array.Copy(volumes, _volumes[slot], _players);
            LatestTick = tick;
        }

        /// <summary>
        /// The hit volume of a player as of requestedTick, clamped to the 150 ms window (older requests use the
        /// oldest stored tick; future ones the latest). Returns false when nothing is recorded.
        /// </summary>
        public bool TryGet(int player, int requestedTick, out Capsule volume, out int usedTick)
        {
            volume = default;
            usedTick = int.MinValue;
            if (LatestTick == int.MinValue || player < 0 || player >= _players)
            {
                return false;
            }

            int tick = Math.Min(requestedTick, LatestTick);
            tick = Math.Max(tick, LatestTick - MaxRewindTicks);
            for (; tick <= LatestTick; tick++)
            {
                int slot = Slot(tick);
                if (_ticks[slot] == tick)
                {
                    volume = _volumes[slot][player];
                    usedTick = tick;
                    return true;
                }
            }

            return false;
        }

        private int Slot(int tick) => ((tick % _depth) + _depth) % _depth;
    }
}
