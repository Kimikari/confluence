using System;
using System.Collections.Generic;

namespace Confluence.Hosting.Commands
{
    /// <summary>
    /// Rate-limit hook consulted before a command runs (spec L429 "rate-limits rerolls"). A refused command answers
    /// <see cref="CommandCode.RateLimited"/> and is not recorded, so the client may retry the same request id.
    /// </summary>
    public interface ICommandRateLimiter
    {
        bool TryAcquire(int player, CommandKind kind, int tick);
    }

    /// <summary>Never limits (bots in tests, transports that already limit).</summary>
    public sealed class UnlimitedCommands : ICommandRateLimiter
    {
        public static readonly UnlimitedCommands Instance = new UnlimitedCommands();

        public bool TryAcquire(int player, CommandKind kind, int tick) => true;
    }

    /// <summary>
    /// Per-player integer token buckets on the host tick: rerolls (shop and draft) burst 3 then one per 15 ticks
    /// (250 ms), other economy transactions burst 5 then one per 12 ticks (200 ms). The numbers mirror the transport
    /// limits (D-116; the spec only requires limits). Ready and surrender are never limited.
    /// </summary>
    public sealed class TickRateLimiter : ICommandRateLimiter
    {
        public const int RerollBurst = 3;
        public const int RerollRefillTicks = 15;
        public const int TransactionBurst = 5;
        public const int TransactionRefillTicks = 12;

        private readonly Dictionary<long, Bucket> _buckets = new Dictionary<long, Bucket>();

        public bool TryAcquire(int player, CommandKind kind, int tick)
        {
            int group;
            int burst;
            int refill;
            switch (kind)
            {
                case CommandKind.ShopReroll:
                case CommandKind.DraftReroll:
                    group = 0;
                    burst = RerollBurst;
                    refill = RerollRefillTicks;
                    break;
                case CommandKind.ShopReady:
                case CommandKind.DraftPass:
                case CommandKind.Surrender:
                    return true;
                default:
                    group = 1;
                    burst = TransactionBurst;
                    refill = TransactionRefillTicks;
                    break;
            }

            long key = ((long)player << 8) | (uint)group;
            if (!_buckets.TryGetValue(key, out Bucket bucket))
            {
                bucket = new Bucket { Tokens = burst, LastTick = tick };
                _buckets.Add(key, bucket);
            }

            int elapsed = Math.Max(0, tick - bucket.LastTick);
            int gained = elapsed / refill;
            if (gained > 0)
            {
                bucket.Tokens = Math.Min(burst, bucket.Tokens + gained);
                bucket.LastTick += gained * refill;
            }

            if (bucket.Tokens >= burst) bucket.LastTick = tick; // a full bucket does not bank refill time
            if (bucket.Tokens <= 0) return false;
            bucket.Tokens--;
            return true;
        }

        private sealed class Bucket
        {
            public int Tokens;
            public int LastTick;
        }
    }
}
