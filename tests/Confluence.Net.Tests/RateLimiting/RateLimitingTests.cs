using System;
using Confluence.Net.RateLimiting;

namespace Confluence.Net.Tests.RateLimiting
{
    public sealed class RateLimitingTests
    {
        [Fact]
        public void Bucket_Allows_A_Burst_Then_Refills_One_Token_Per_Interval()
        {
            var bucket = new TokenBucket(new RateLimit(3, 250), 1000);
            Assert.Equal(3, bucket.Available(1000));
            Assert.True(bucket.TryTake(1000));
            Assert.True(bucket.TryTake(1000));
            Assert.True(bucket.TryTake(1000));
            Assert.False(bucket.TryTake(1000));

            Assert.False(bucket.TryTake(1249));
            Assert.True(bucket.TryTake(1250)); // exactly one interval later
            Assert.False(bucket.TryTake(1250));

            // Long idle periods refill only up to the burst.
            Assert.Equal(3, bucket.Available(100_000));
        }

        [Fact]
        public void Partial_Refill_Credit_Is_Kept_Exactly()
        {
            var bucket = new TokenBucket(new RateLimit(1, 100), 0);
            Assert.True(bucket.TryTake(0));
            Assert.False(bucket.TryTake(60));
            Assert.True(bucket.TryTake(100)); // 60 + 40 ms of credit add up to one token
        }

        [Fact]
        public void A_Clock_Stepping_Back_Never_Mints_Tokens()
        {
            var bucket = new TokenBucket(new RateLimit(1, 100), 500);
            Assert.True(bucket.TryTake(500));
            Assert.False(bucket.TryTake(100));
            Assert.False(bucket.TryTake(550));
            Assert.True(bucket.TryTake(600));
        }

        [Fact]
        public void Unlimited_Never_Rejects()
        {
            var bucket = new TokenBucket(RateLimit.Unlimited, 0);
            for (int i = 0; i < 1000; i++)
            {
                Assert.True(bucket.TryTake(0));
            }

            Assert.False(RateLimit.Unlimited.IsLimited);
            Assert.Throws<ArgumentOutOfRangeException>(() => new RateLimit(2, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RateLimit(-1, 10));
        }

        [Fact]
        public void Limiter_Keeps_Separate_Buckets_Per_Sender_And_Key_And_Counts_Rejections()
        {
            var limiter = new RateLimiter();
            var limit = new RateLimit(2, 1000);
            const ushort Reroll = 70;
            const ushort Purchase = 71;

            Assert.True(limiter.TryAcquire(1, Reroll, limit, 0));
            Assert.True(limiter.TryAcquire(1, Reroll, limit, 0));
            Assert.False(limiter.TryAcquire(1, Reroll, limit, 0));
            Assert.False(limiter.TryAcquire(1, Reroll, limit, 10));

            // Another key and another sender are untouched.
            Assert.True(limiter.TryAcquire(1, Purchase, limit, 10));
            Assert.True(limiter.TryAcquire(2, Reroll, limit, 10));

            Assert.Equal(2, limiter.TotalRejected);
            Assert.Equal(2, limiter.RejectedFor(Reroll));
            Assert.Equal(0, limiter.RejectedFor(Purchase));

            // Forgetting a sender (session expired) gives a fresh bucket but keeps the counters.
            limiter.Forget(1);
            Assert.True(limiter.TryAcquire(1, Reroll, limit, 20));
            Assert.Equal(2, limiter.RejectedFor(Reroll));
        }

        [Fact]
        public void Limiter_Ignores_Unlimited_Keys()
        {
            var limiter = new RateLimiter();
            for (int i = 0; i < 100; i++)
            {
                Assert.True(limiter.TryAcquire(1, 5, RateLimit.Unlimited, 0));
            }

            Assert.Equal(0, limiter.TotalRejected);
        }

        [Fact]
        public void Shipped_Limits_Allow_Normal_Play_And_Stop_Spam()
        {
            // Spec L429 requires rate-limited rerolls; a player rerolling by hand (one click per 400 ms) is never
            // refused, while a scripted burst is capped at the configured burst plus the refill rate.
            var limiter = new RateLimiter();
            for (long t = 0; t < 10_000; t += 400)
            {
                Assert.True(limiter.TryAcquire(1, 80, NetRules.Rerolls, t));
            }

            int accepted = 0;
            for (long t = 0; t < 1000; t++)
            {
                if (limiter.TryAcquire(2, 80, NetRules.Rerolls, t)) accepted++;
            }

            Assert.Equal(NetRules.Rerolls.Burst + 999 / NetRules.Rerolls.RefillIntervalMs, accepted); // 3 + 3
        }
    }
}
