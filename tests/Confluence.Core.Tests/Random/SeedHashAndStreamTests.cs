using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Random;

namespace Confluence.Core.Tests.Random
{
    public class SeedHashAndStreamTests
    {
        [Theory]
        [InlineData("", 0xCBF29CE484222325UL)]
        [InlineData("a", 0xAF63DC4C8601EC8CUL)]
        [InlineData("foobar", 0x85944171F73967E8UL)]
        [InlineData("WIZ-002", 0x60A9D16239D248EEUL)]
        [InlineData("é€\U0001F600", 0x0A289CA40199FA06UL)] // 2-, 3- and 4-byte UTF-8 sequences
        public void Fnv1a64_Matches_Reference_Vectors(string input, ulong expected)
        {
            Assert.Equal(expected, SeedHash.Fnv1a64(input));
        }

        [Fact]
        public void Fnv1a64_Null_Hashes_Like_Empty()
        {
            Assert.Equal(SeedHash.Fnv1a64(""), SeedHash.Fnv1a64(null));
        }

        [Fact]
        public void Fnv1a64_Lone_Surrogate_Matches_Utf8_Replacement()
        {
            Assert.Equal(SeedHash.Fnv1a64("�"), SeedHash.Fnv1a64("\uD800"));
        }

        [Fact]
        public void Of_And_Combine_Golden_Values()
        {
            Assert.Equal(0xAF555872B3B3F4ADUL, SeedHash.Of("WIZ-002"));
            Assert.Equal(0xD1D582ABD43B2A45UL, SeedHash.Combine(1UL, 2UL));
            Assert.Equal(0xD9733AE19C3005B5UL, SeedHash.Combine(1UL, 2UL, 3UL));
            Assert.Equal(0xE220A8397B1DCDAFUL, SeedHash.Combine(new ulong[0]));
        }

        [Fact]
        public void Combine_Overloads_Agree_And_Depend_On_Order()
        {
            Assert.Equal(SeedHash.Combine(new ulong[] { 5, 6 }), SeedHash.Combine(5UL, 6UL));
            Assert.Equal(SeedHash.Combine(new ulong[] { 5, 6, 7 }), SeedHash.Combine(5UL, 6UL, 7UL));
            Assert.Equal(SeedHash.Combine(new ulong[] { 5, 6, 7, 8 }), SeedHash.Combine(5UL, 6UL, 7UL, 8UL));
            Assert.NotEqual(SeedHash.Combine(5UL, 6UL), SeedHash.Combine(6UL, 5UL));
            Assert.NotEqual(SeedHash.Combine(new ulong[] { 5 }), SeedHash.Combine(5UL, 0UL));
            Assert.NotEqual(SeedHash.Combine(0UL, 0UL), SeedHash.Combine(0UL, 0UL, 0UL));
            Assert.Throws<ArgumentNullException>(() => SeedHash.Combine(null));
        }

        [Fact]
        public void Mix_Has_Avalanche()
        {
            // Flipping one input bit flips about half the output bits on average.
            double total = 0;
            int samples = 0;
            for (ulong x = 1; x < 200; x++)
            {
                for (int bit = 0; bit < 64; bit += 7)
                {
                    ulong diff = SeedHash.Mix(x) ^ SeedHash.Mix(x ^ (1UL << bit));
                    total += System.Numerics.BitOperations.PopCount(diff);
                    samples++;
                }
            }

            Assert.InRange(total / samples, 30.0, 34.0);
        }

        [Fact]
        public void Stream_Seeds_Are_Stable_Golden_Values()
        {
            // Replays rely on these derivations staying fixed (independent BigInt reference).
            Assert.Equal(0x0F0C6FB2599FA709UL, RngStreams.SeedFor(12345, RngStream.Offer, 1));
            Assert.Equal(0x4C8F55640E771E74UL, RngStreams.SeedFor(12345, RngStream.Arena, 3));
            Assert.Equal(0x14B445BE6D8CD8F0UL, RngStreams.SeedFor(12345, RngStream.Cosmetic, 1, 2));
            Assert.Equal(0xC0954E59E542C30AUL, RngStreams.SeedFor(12345, RngStream.Bot, -1));
            Assert.Equal(SeedHash.Of("confluence.rng.offer"), RngStreams.StreamKey(RngStream.Offer));
        }

        [Fact]
        public void Same_Arguments_Reproduce_The_Same_Sequence()
        {
            Rng a = RngStreams.For(777, RngStream.Offer, 4, 1);
            Rng b = RngStreams.For(777, RngStream.Offer, 4, 1);
            for (int i = 0; i < 100; i++)
            {
                Assert.Equal(a.NextULong(), b.NextULong());
            }
        }

        [Fact]
        public void All_Stream_Round_Sub_Combinations_Have_Distinct_Seeds()
        {
            var seeds = new HashSet<ulong>();
            int count = 0;
            foreach (RngStream stream in Enum.GetValues(typeof(RngStream)))
            {
                for (int round = -1; round <= 16; round++)
                {
                    for (int sub = 0; sub < 8; sub++)
                    {
                        seeds.Add(RngStreams.SeedFor(42, stream, round, sub));
                        count++;
                    }
                }
            }

            Assert.Equal(count, seeds.Count);
            Assert.NotEqual(RngStreams.SeedFor(1, RngStream.Offer, 1), RngStreams.SeedFor(2, RngStream.Offer, 1));
        }

        [Fact]
        public void Cosmetic_Consumption_Never_Changes_Later_Offers()
        {
            // Spec L435: rendering an extra particle cannot change a later shop offer.
            const ulong matchSeed = 0xC0FFEE;
            var quiet = RngStreams.For(matchSeed, RngStream.Offer, 5);
            var expected = Enumerable.Range(0, 20).Select(_ => quiet.NextInt(1000)).ToArray();

            var cosmetic = RngStreams.For(matchSeed, RngStream.Cosmetic, 5);
            for (int i = 0; i < 100000; i++)
            {
                cosmetic.NextFloat();
            }

            var bot = RngStreams.For(matchSeed, RngStream.Bot, 5);
            bot.Shuffle(Enumerable.Range(0, 64).ToList());

            var offer = RngStreams.For(matchSeed, RngStream.Offer, 5);
            var actual = Enumerable.Range(0, 20).Select(_ => offer.NextInt(1000)).ToArray();
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void Streams_Are_Statistically_Independent()
        {
            Rng offer = RngStreams.For(2026, RngStream.Offer, 1);
            Rng arena = RngStreams.For(2026, RngStream.Arena, 1);
            Rng nextRound = RngStreams.For(2026, RngStream.Offer, 2);
            const int n = 20000;
            var x = new double[n];
            var y = new double[n];
            var z = new double[n];
            int equalFirst = 0;
            for (int i = 0; i < n; i++)
            {
                x[i] = offer.NextDouble();
                y[i] = arena.NextDouble();
                z[i] = nextRound.NextDouble();
                if (x[i] == y[i])
                {
                    equalFirst++;
                }
            }

            Assert.InRange(Correlation(x, y), -0.03, 0.03);
            Assert.InRange(Correlation(x, z), -0.03, 0.03);
            Assert.Equal(0, equalFirst);
        }

        private static double Correlation(double[] a, double[] b)
        {
            double ma = a.Average();
            double mb = b.Average();
            double cov = 0, va = 0, vb = 0;
            for (int i = 0; i < a.Length; i++)
            {
                cov += (a[i] - ma) * (b[i] - mb);
                va += (a[i] - ma) * (a[i] - ma);
                vb += (b[i] - mb) * (b[i] - mb);
            }

            return cov / Math.Sqrt(va * vb);
        }

        [Fact]
        public void Unknown_Stream_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RngStreams.StreamKey((RngStream)99));
        }
    }
}
