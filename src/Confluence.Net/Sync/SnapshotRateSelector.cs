using System;
using Confluence.Net.Transport;

namespace Confluence.Net.Sync
{
    /// <summary>
    /// Per-client snapshot rate between 20 and 30 Hz selected by congestion (spec L421; DECISIONS D-005: 30 Hz
    /// by default, 20 Hz under congestion). The link counts as congested when windowed loss, reliable backlog,
    /// or round-trip inflation over the recent RTT floor crosses its threshold. The rate drops after congestion
    /// persists for <see cref="NetRules.CongestionDowngradeMs"/> and returns only after
    /// <see cref="NetRules.CongestionRecoveryMs"/> of clear signals, so it does not oscillate.
    /// </summary>
    public sealed class SnapshotRateSelector
    {
        private const int FloorBuckets = 10;
        private const long FloorBucketMs = 1000;

        private readonly int[] _floorMinima = new int[FloorBuckets];
        private long _floorBucketStart = -1;
        private int _floorIndex;
        private long _congestedSinceMs = -1;
        private long _clearSinceMs = -1;

        public SnapshotRateSelector(int highRate = NetRules.SnapshotRateMax, int lowRate = NetRules.SnapshotRateMin)
        {
            if (lowRate < NetRules.SnapshotRateMin || highRate > NetRules.SnapshotRateMax || lowRate > highRate)
            {
                throw new ArgumentOutOfRangeException(nameof(lowRate), "Snapshot rates must lie within 20-30 Hz (spec L421).");
            }

            HighRate = highRate;
            LowRate = lowRate;
            CurrentRate = highRate;
        }

        public int HighRate { get; }

        public int LowRate { get; }

        /// <summary>Snapshots per second for this client right now.</summary>
        public int CurrentRate { get; private set; }

        /// <summary>Whether the latest signals were congested.</summary>
        public bool IsCongested { get; private set; }

        /// <summary>Lowest RTT over the last ten seconds (0 until measured).</summary>
        public int RttFloorMs { get; private set; }

        public long Downgrades { get; private set; }

        public long Upgrades { get; private set; }

        /// <summary>Feeds the latest link signals.</summary>
        public void Update(long nowMs, LinkStats stats)
        {
            TrackRttFloor(nowMs, stats.RttMs);
            IsCongested = stats.LossFraction >= NetRules.CongestedLossFraction
                || stats.ReliableQueued >= NetRules.CongestedReliableQueue
                || (RttFloorMs > 0 && stats.RttMs - RttFloorMs >= NetRules.CongestedRttInflationMs);

            if (IsCongested)
            {
                _clearSinceMs = -1;
                if (_congestedSinceMs < 0) _congestedSinceMs = nowMs;
                if (CurrentRate != LowRate && nowMs - _congestedSinceMs >= NetRules.CongestionDowngradeMs)
                {
                    CurrentRate = LowRate;
                    Downgrades++;
                }
            }
            else
            {
                _congestedSinceMs = -1;
                if (_clearSinceMs < 0) _clearSinceMs = nowMs;
                if (CurrentRate != HighRate && nowMs - _clearSinceMs >= NetRules.CongestionRecoveryMs)
                {
                    CurrentRate = HighRate;
                    Upgrades++;
                }
            }
        }

        /// <summary>
        /// Whether a snapshot goes out on <paramref name="serverTick"/>: whenever tick * rate / 60 crosses an integer.
        /// At 30 Hz that is every second tick, at 20 Hz every third.
        /// </summary>
        public bool ShouldSend(int serverTick) => ShouldSend(serverTick, CurrentRate);

        public static bool ShouldSend(int serverTick, int rate)
        {
            long now = FloorDiv((long)serverTick * rate, NetRules.TickRate);
            long before = FloorDiv((long)(serverTick - 1) * rate, NetRules.TickRate);
            return now != before;
        }

        private void TrackRttFloor(long nowMs, int rttMs)
        {
            if (rttMs <= 0) return;
            if (_floorBucketStart < 0)
            {
                _floorBucketStart = nowMs;
                _floorMinima[0] = rttMs;
            }

            while (nowMs - _floorBucketStart >= FloorBucketMs)
            {
                _floorBucketStart += FloorBucketMs;
                _floorIndex = (_floorIndex + 1) % FloorBuckets;
                _floorMinima[_floorIndex] = 0;
                if (nowMs - _floorBucketStart >= FloorBucketMs * FloorBuckets)
                {
                    // Long silence: restart the history rather than spinning through empty buckets.
                    Array.Clear(_floorMinima, 0, FloorBuckets);
                    _floorBucketStart = nowMs;
                }
            }

            if (_floorMinima[_floorIndex] == 0 || rttMs < _floorMinima[_floorIndex])
            {
                _floorMinima[_floorIndex] = rttMs;
            }

            int floor = 0;
            foreach (int m in _floorMinima)
            {
                if (m > 0 && (floor == 0 || m < floor)) floor = m;
            }

            RttFloorMs = floor;
        }

        private static long FloorDiv(long a, long b)
        {
            long q = a / b;
            return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q;
        }
    }
}
