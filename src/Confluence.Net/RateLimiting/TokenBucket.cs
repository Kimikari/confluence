using System;
using System.Collections.Generic;

namespace Confluence.Net.RateLimiting
{
    /// <summary>
    /// Token-bucket parameters: up to <see cref="Burst"/> actions at once, refilled by one token every
    /// <see cref="RefillIntervalMs"/>. <see cref="Unlimited"/> (burst 0) disables limiting.
    /// </summary>
    public struct RateLimit : IEquatable<RateLimit>
    {
        public RateLimit(int burst, int refillIntervalMs)
        {
            if (burst < 0) throw new ArgumentOutOfRangeException(nameof(burst));
            if (burst > 0 && refillIntervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(refillIntervalMs));
            Burst = burst;
            RefillIntervalMs = refillIntervalMs;
        }

        public static RateLimit Unlimited => default;

        public int Burst { get; }

        public int RefillIntervalMs { get; }

        public bool IsLimited => Burst > 0;

        public bool Equals(RateLimit other) => Burst == other.Burst && RefillIntervalMs == other.RefillIntervalMs;

        public override bool Equals(object obj) => obj is RateLimit other && Equals(other);

        public override int GetHashCode() => unchecked(Burst * 397 ^ RefillIntervalMs);

        public override string ToString() => IsLimited ? Burst + " burst, 1 per " + RefillIntervalMs + " ms" : "unlimited";
    }

    /// <summary>
    /// Integer token bucket. Credit is kept in milliseconds of refill time, so refill and consumption are exact
    /// integer arithmetic: a token costs <see cref="RateLimit.RefillIntervalMs"/> of credit and credit never
    /// exceeds Burst times that interval. Time comes from the caller (a monotonic network clock).
    /// </summary>
    public sealed class TokenBucket
    {
        private readonly RateLimit _limit;
        private readonly long _maxCredit;
        private long _credit;
        private long _lastMs;

        public TokenBucket(RateLimit limit, long nowMs)
        {
            _limit = limit;
            _maxCredit = (long)limit.Burst * limit.RefillIntervalMs;
            _credit = _maxCredit; // starts full
            _lastMs = nowMs;
        }

        public RateLimit Limit => _limit;

        /// <summary>Whole tokens available at <paramref name="nowMs"/>.</summary>
        public int Available(long nowMs)
        {
            if (!_limit.IsLimited) return int.MaxValue;
            Refill(nowMs);
            return (int)(_credit / _limit.RefillIntervalMs);
        }

        /// <summary>Consumes one token if available.</summary>
        public bool TryTake(long nowMs)
        {
            if (!_limit.IsLimited)
            {
                return true;
            }

            Refill(nowMs);
            if (_credit < _limit.RefillIntervalMs)
            {
                return false;
            }

            _credit -= _limit.RefillIntervalMs;
            return true;
        }

        private void Refill(long nowMs)
        {
            long elapsed = nowMs - _lastMs;
            if (elapsed <= 0)
            {
                return; // a clock that stands still or steps back never mints tokens
            }

            _lastMs = nowMs;
            _credit = Math.Min(_maxCredit, _credit + elapsed);
        }
    }

    /// <summary>
    /// Per-sender, per-key token buckets with rejection counters (spec L429: duplicate purchases are
    /// prevented and rerolls rate-limited). Keys are message or request ids. Buckets are created lazily at
    /// first use and removed with <see cref="Forget"/> when a session ends.
    /// </summary>
    public sealed class RateLimiter
    {
        private readonly Dictionary<int, Dictionary<ushort, TokenBucket>> _bySender = new Dictionary<int, Dictionary<ushort, TokenBucket>>();
        private readonly Dictionary<ushort, long> _rejectedByKey = new Dictionary<ushort, long>();

        public long TotalRejected { get; private set; }

        /// <summary>True when <paramref name="sender"/> may perform the action <paramref name="key"/> now.</summary>
        public bool TryAcquire(int sender, ushort key, RateLimit limit, long nowMs)
        {
            if (!limit.IsLimited)
            {
                return true;
            }

            if (!_bySender.TryGetValue(sender, out Dictionary<ushort, TokenBucket> buckets))
            {
                buckets = new Dictionary<ushort, TokenBucket>();
                _bySender[sender] = buckets;
            }

            if (!buckets.TryGetValue(key, out TokenBucket bucket) || !bucket.Limit.Equals(limit))
            {
                bucket = new TokenBucket(limit, nowMs);
                buckets[key] = bucket;
            }

            if (bucket.TryTake(nowMs))
            {
                return true;
            }

            TotalRejected++;
            _rejectedByKey.TryGetValue(key, out long count);
            _rejectedByKey[key] = count + 1;
            return false;
        }

        public long RejectedFor(ushort key) => _rejectedByKey.TryGetValue(key, out long count) ? count : 0;

        /// <summary>Drops every bucket of <paramref name="sender"/> (rejection counters are kept).</summary>
        public void Forget(int sender) => _bySender.Remove(sender);

        /// <summary>Drops every bucket of every sender (rejection counters are kept).</summary>
        public void Clear() => _bySender.Clear();
    }
}
