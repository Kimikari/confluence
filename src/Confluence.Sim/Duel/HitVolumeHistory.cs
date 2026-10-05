using System;
using Confluence.Core.Geometry;

namespace Confluence.Sim.Duel
{
    /// <summary>
    /// Lag compensation (spec L423): a bounded ring of player hit volumes for the last 150 ms (9 ticks). Hitscan and
    /// melee validation may rewind a player's capsule to the tick the attacker was viewing; projectiles, fields, and
    /// props are never rewound. A rewind never crosses a discontinuity (Source's "lost track" rule; Net research
    /// section 2.2.4 of docs/research/netcode.md): when a volume jumps more than <see cref="DiscontinuityDistance"/> in one tick (teleport, recall,
    /// alcove placement), older records of that player are no longer used.
    /// </summary>
    public sealed class HitVolumeHistory
    {
        /// <summary>
        /// A move this far in one tick (90 m/s) cannot be locomotion, dodge, or dash (spec L80-L82, L124: at most 8 m
        /// dashes over their duration), only a teleport or placement (L124: up to 10 m).
        /// </summary>
        public const float DiscontinuityDistance = 1.5f;

        private readonly int _depth;
        private readonly int[] _ticks;
        private readonly Capsule[][] _volumes;
        private readonly int _players;
        private readonly int[] _breakTick;

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
            _breakTick = new int[players];
            for (int p = 0; p < players; p++) _breakTick[p] = int.MinValue;
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

            int previous = Slot(tick - 1);
            if (_ticks[previous] == tick - 1)
            {
                for (int p = 0; p < _players; p++)
                {
                    if (Vec3.Distance(_volumes[previous][p].A, volumes[p].A) > DiscontinuityDistance) _breakTick[p] = tick;
                }
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
            tick = Math.Max(tick, _breakTick[player]);
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

        /// <summary>The newest tick at which the player's volume jumped (int.MinValue when never).</summary>
        public int DiscontinuityTick(int player) => player >= 0 && player < _players ? _breakTick[player] : int.MinValue;

        private int Slot(int tick) => ((tick % _depth) + _depth) % _depth;
    }
}
