using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Random;

namespace Confluence.Core.Tests.Random
{
    public class RngTests
    {
        // Chi-square critical value for 9 degrees of freedom at p = 0.0001 is about 33.7; 40 is generous.
        private const double ChiSquareLimit9 = 40.0;

        private static double ChiSquare(int[] counts, double expectedEach)
        {
            double chi = 0.0;
            foreach (int c in counts)
            {
                double d = c - expectedEach;
                chi += d * d / expectedEach;
            }

            return chi;
        }

        [Fact]
        public void SplitMix64_Matches_Reference_Output()
        {
            ulong state = 0;
            Assert.Equal(0xE220A8397B1DCDAFUL, Rng.SplitMix64(ref state));
            Assert.Equal(0x6E789E6AA1B965F4UL, Rng.SplitMix64(ref state));
        }

        [Fact]
        public void Xoshiro_Matches_Reference_Vectors_From_State_1234()
        {
            var rng = new Rng(new RngState(1, 2, 3, 4));
            Assert.Equal(11520UL, rng.NextULong());
            Assert.Equal(0UL, rng.NextULong());
            Assert.Equal(1509978240UL, rng.NextULong());
            Assert.Equal(1215971899390074240UL, rng.NextULong());
        }

        [Theory]
        [InlineData(0UL, 0x99EC5F36CB75F2B4UL, 0xBF6E1F784956452AUL, 0x1A5F849D4933E6E0UL, 0x6AA594F1262D2D2CUL)]
        [InlineData(42UL, 0x15780B2E0C2EC716UL, 0x6104D9866D113A7EUL, 0xAE17533239E499A1UL, 0xECB8AD4703B360A1UL)]
        [InlineData(0xDEADBEEFUL, 0xC5555444A74D7E83UL, 0x65C30D37B4B16E38UL, 0x54F773200A4EFA23UL, 0x429AED75FB958AF7UL)]
        public void Seeded_Golden_Values(ulong seed, ulong v0, ulong v1, ulong v2, ulong v3)
        {
            // Values computed by an independent BigInt implementation of SplitMix64-seeded xoshiro256**.
            var rng = new Rng(seed);
            Assert.Equal(v0, rng.NextULong());
            Assert.Equal(v1, rng.NextULong());
            Assert.Equal(v2, rng.NextULong());
            Assert.Equal(v3, rng.NextULong());
        }

        [Fact]
        public void Derived_Values_Use_The_High_Bits()
        {
            var a = new Rng(42);
            var b = new Rng(42);
            Assert.Equal((uint)(0x15780B2E0C2EC716UL >> 32), a.NextUInt());
            Assert.Equal((0x6104D9866D113A7EUL >> 40) / 16777216f, (a.NextFloat()));
            Assert.Equal((0xAE17533239E499A1UL >> 11) / 9007199254740992.0, a.NextDouble());
            Assert.True(a.NextBool()); // 0xECB8... has its top bit set
            b.NextULong();
            b.NextULong();
            b.NextULong();
            b.NextULong();
            Assert.Equal(b.State, a.State);
        }

        [Fact]
        public void State_Round_Trips_For_Replay()
        {
            var rng = new Rng(123456789);
            for (int i = 0; i < 10; i++)
            {
                rng.NextULong();
            }

            RngState saved = rng.State;
            var first = Enumerable.Range(0, 8).Select(_ => rng.NextULong()).ToArray();
            rng.State = saved;
            var second = Enumerable.Range(0, 8).Select(_ => rng.NextULong()).ToArray();
            Assert.Equal(first, second);

            var restored = new Rng(saved);
            Assert.Equal(first[0], restored.NextULong());
            Assert.Equal(saved, new Rng(saved).State);
            Assert.Equal(saved.GetHashCode(), new RngState(saved.S0, saved.S1, saved.S2, saved.S3).GetHashCode());
        }

        [Fact]
        public void Zero_State_Is_Rejected()
        {
            var rng = new Rng(1);
            Assert.Throws<ArgumentException>(() => rng.State = default);
            Assert.Throws<ArgumentException>(() => new Rng(new RngState(0, 0, 0, 0)));
        }

        [Fact]
        public void Clone_Is_Independent()
        {
            var rng = new Rng(77);
            rng.NextULong();
            Rng copy = rng.Clone();
            Assert.Equal(rng.NextULong(), copy.NextULong());
            copy.NextULong();
            Assert.NotEqual(rng.State, copy.State);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(7)]
        [InlineData(10)]
        [InlineData(1000)]
        [InlineData(1 << 30)]
        [InlineData(int.MaxValue)]
        public void NextInt_Stays_In_Bounds(int maxExclusive)
        {
            var rng = new Rng((ulong)maxExclusive);
            for (int i = 0; i < 5000; i++)
            {
                int v = rng.NextInt(maxExclusive);
                Assert.InRange(v, 0, maxExclusive - 1);
            }
        }

        [Fact]
        public void NextInt_Range_Handles_Extremes()
        {
            var rng = new Rng(9);
            bool sawNegative = false;
            bool sawPositive = false;
            for (int i = 0; i < 2000; i++)
            {
                int v = rng.NextInt(int.MinValue, int.MaxValue);
                Assert.True(v < int.MaxValue);
                sawNegative |= v < 0;
                sawPositive |= v > 0;

                int w = rng.NextInt(-5, 5);
                Assert.InRange(w, -5, 4);

                Assert.Equal(int.MaxValue - 1, rng.NextInt(int.MaxValue - 1, int.MaxValue));
            }

            Assert.True(sawNegative && sawPositive);
        }

        [Fact]
        public void NextInt_Rejects_Empty_Ranges()
        {
            var rng = new Rng(1);
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(-3));
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(5, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(6, 5));
        }

        [Fact]
        public void NextInt_Is_Roughly_Uniform()
        {
            var rng = new Rng(31337);
            var counts = new int[10];
            const int n = 100000;
            for (int i = 0; i < n; i++)
            {
                counts[rng.NextInt(10)]++;
            }

            Assert.True(ChiSquare(counts, n / 10.0) < ChiSquareLimit9, string.Join(",", counts));

            // A range that does not divide 2^32 exercises the rejection path.
            var counts3 = new int[3];
            for (int i = 0; i < 30000; i++)
            {
                counts3[rng.NextInt(3)]++;
            }

            Assert.True(ChiSquare(counts3, 10000.0) < 25.0, string.Join(",", counts3));
        }

        [Fact]
        public void NextInt_With_Huge_Range_Is_Unbiased_Across_Halves()
        {
            // For range 1.5 * 2^30 a naive modulo would put 56% of draws in the lower half; Lemire's method must not.
            var rng = new Rng(4242);
            const int range = 1610612736;
            int low = 0;
            const int n = 60000;
            for (int i = 0; i < n; i++)
            {
                if (rng.NextInt(range) < range / 2)
                {
                    low++;
                }
            }

            Assert.InRange(low, n / 2 - 1000, n / 2 + 1000);
        }

        [Fact]
        public void Float_Double_Bool_And_Range()
        {
            var rng = new Rng(2024);
            var buckets = new int[10];
            int trues = 0;
            const int n = 50000;
            for (int i = 0; i < n; i++)
            {
                float f = rng.NextFloat();
                Assert.InRange(f, 0f, 0.99999994f);
                buckets[(int)(f * 10f)]++;

                double d = rng.NextDouble();
                Assert.True(d >= 0.0 && d < 1.0);

                if (rng.NextBool())
                {
                    trues++;
                }

                float r = rng.Range(-3f, 5f);
                Assert.True(r >= -3f && r < 5f);
            }

            Assert.True(ChiSquare(buckets, n / 10.0) < ChiSquareLimit9, string.Join(",", buckets));
            Assert.InRange(trues, n / 2 - 800, n / 2 + 800);
            Assert.Equal(2f, rng.Range(2f, 2f));
            Assert.False(rng.Chance(0f));
            Assert.True(rng.Chance(1f));
        }

        [Fact]
        public void Shuffle_Produces_Valid_Uniform_Permutations()
        {
            var rng = new Rng(555);
            var source = Enumerable.Range(0, 20).ToList();
            for (int trial = 0; trial < 200; trial++)
            {
                var list = new List<int>(source);
                rng.Shuffle(list);
                Assert.Equal(source, list.OrderBy(x => x).ToList());
            }

            // All 6 orders of three elements appear about equally often.
            var counts = new Dictionary<string, int>();
            const int n = 60000;
            for (int i = 0; i < n; i++)
            {
                var arr = new[] { 'a', 'b', 'c' };
                rng.Shuffle(arr);
                string key = new string(arr);
                counts[key] = counts.TryGetValue(key, out int c) ? c + 1 : 1;
            }

            Assert.Equal(6, counts.Count);
            double chi = counts.Values.Sum(c => (c - n / 6.0) * (c - n / 6.0) / (n / 6.0));
            Assert.True(chi < 30.0, string.Join(",", counts.Select(kv => kv.Key + "=" + kv.Value)));

            rng.Shuffle(new List<int>());
            rng.Shuffle(new List<int> { 1 });
            Assert.Throws<ArgumentNullException>(() => rng.Shuffle<int>(null));
        }

        [Fact]
        public void Shuffle_Is_Deterministic_Per_Seed()
        {
            var a = Enumerable.Range(0, 50).ToArray();
            var b = Enumerable.Range(0, 50).ToArray();
            new Rng(8).Shuffle(a);
            new Rng(8).Shuffle(b);
            Assert.Equal(a, b);
            var c = Enumerable.Range(0, 50).ToArray();
            new Rng(9).Shuffle(c);
            Assert.NotEqual(a, c);
        }

        [Fact]
        public void Pick_Chooses_Members_And_Rejects_Empty()
        {
            var rng = new Rng(3);
            var items = new[] { "WIZ-002", "WIZ-010", "WIZ-021" };
            var seen = new HashSet<string>();
            for (int i = 0; i < 300; i++)
            {
                string picked = rng.Pick(items);
                Assert.Contains(picked, items);
                seen.Add(picked);
            }

            Assert.Equal(3, seen.Count);
            Assert.Throws<ArgumentException>(() => rng.Pick(new string[0]));
            Assert.Throws<ArgumentNullException>(() => rng.Pick<string>(null));
        }
    }
}
