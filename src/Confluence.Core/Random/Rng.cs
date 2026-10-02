using System;
using System.Collections.Generic;

namespace Confluence.Core.Random
{
    /// <summary>Complete xoshiro256** state, for replay snapshots.</summary>
    public struct RngState : IEquatable<RngState>
    {
        public ulong S0;
        public ulong S1;
        public ulong S2;
        public ulong S3;

        public RngState(ulong s0, ulong s1, ulong s2, ulong s3)
        {
            S0 = s0;
            S1 = s1;
            S2 = s2;
            S3 = s3;
        }

        public bool IsZero => (S0 | S1 | S2 | S3) == 0UL;

        public bool Equals(RngState other) => S0 == other.S0 && S1 == other.S1 && S2 == other.S2 && S3 == other.S3;

        public override bool Equals(object obj) => obj is RngState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (int)(SeedHash.Combine(S0, S1, S2, S3) >> 32);
            }
        }

        public override string ToString() => S0.ToString("X16") + ":" + S1.ToString("X16") + ":" + S2.ToString("X16") + ":" + S3.ToString("X16");
    }

    /// <summary>
    /// Deterministic random generator: xoshiro256** (Blackman and Vigna) seeded through SplitMix64.
    /// The only randomness source allowed in rules code. Derive independent instances with
    /// <see cref="RngStreams.For"/> rather than sharing one generator across systems.
    /// </summary>
    public sealed class Rng
    {
        private ulong _s0;
        private ulong _s1;
        private ulong _s2;
        private ulong _s3;

        public Rng(ulong seed)
        {
            ulong x = seed;
            _s0 = SplitMix64(ref x);
            _s1 = SplitMix64(ref x);
            _s2 = SplitMix64(ref x);
            _s3 = SplitMix64(ref x);
            if ((_s0 | _s1 | _s2 | _s3) == 0UL)
            {
                _s0 = 1UL; // unreachable for SplitMix64 output, kept as a guard against the invalid all-zero state
            }
        }

        public Rng(RngState state)
        {
            State = state;
        }

        /// <summary>Independent copy with the same state.</summary>
        public Rng Clone() => new Rng(State);

        /// <summary>Full generator state. Setting the all-zero state is rejected (xoshiro would emit zeros forever).</summary>
        public RngState State
        {
            get => new RngState(_s0, _s1, _s2, _s3);
            set
            {
                if (value.IsZero)
                {
                    throw new ArgumentException("The all-zero xoshiro256** state is invalid.", nameof(value));
                }

                _s0 = value.S0;
                _s1 = value.S1;
                _s2 = value.S2;
                _s3 = value.S3;
            }
        }

        /// <summary>SplitMix64 step: advances x and returns the mixed output.</summary>
        public static ulong SplitMix64(ref ulong x)
        {
            unchecked
            {
                x += 0x9E3779B97F4A7C15UL;
                ulong z = x;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        public ulong NextULong()
        {
            unchecked
            {
                ulong result = RotateLeft(_s1 * 5UL, 7) * 9UL;
                ulong t = _s1 << 17;
                _s2 ^= _s0;
                _s3 ^= _s1;
                _s1 ^= _s2;
                _s0 ^= _s3;
                _s2 ^= t;
                _s3 = RotateLeft(_s3, 45);
                return result;
            }
        }

        /// <summary>Upper 32 bits of the next output (the strongest bits).</summary>
        public uint NextUInt() => (uint)(NextULong() >> 32);

        /// <summary>Unbiased integer in [0, maxExclusive) using Lemire's multiply-and-reject method.</summary>
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "Must be positive.");
            }

            return (int)NextBounded((uint)maxExclusive);
        }

        /// <summary>Unbiased integer in [min, maxExclusive).</summary>
        public int NextInt(int min, int maxExclusive)
        {
            if (maxExclusive <= min)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "Must be greater than min.");
            }

            uint range = (uint)((long)maxExclusive - min);
            return (int)((long)min + NextBounded(range));
        }

        private uint NextBounded(uint range)
        {
            unchecked
            {
                ulong m = (ulong)NextUInt() * range;
                uint low = (uint)m;
                if (low < range)
                {
                    uint threshold = (0u - range) % range;
                    while (low < threshold)
                    {
                        m = (ulong)NextUInt() * range;
                        low = (uint)m;
                    }
                }

                return (uint)(m >> 32);
            }
        }

        /// <summary>Uniform float in [0, 1) with 24 random bits.</summary>
        public float NextFloat() => (NextULong() >> 40) * (1f / 16777216f);

        /// <summary>Uniform double in [0, 1) with 53 random bits.</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);

        public bool NextBool() => (NextULong() >> 63) != 0UL;

        /// <summary>True with the given probability (clamped to [0, 1]).</summary>
        public bool Chance(float probability) => NextFloat() < probability;

        /// <summary>Uniform float in [a, b) (or (b, a] when b &lt; a); returns a when they are equal.</summary>
        public float Range(float a, float b)
        {
            float v = a + (b - a) * NextFloat();
            // Float rounding can land exactly on b for wide ranges; keep the upper bound exclusive.
            return (v == b && a != b) ? a : v;
        }

        /// <summary>In-place Fisher-Yates shuffle.</summary>
        public void Shuffle<T>(IList<T> list)
        {
            if (list == null)
            {
                throw new ArgumentNullException(nameof(list));
            }

            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = NextInt(i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        /// <summary>Uniformly chosen element. Throws on an empty list.</summary>
        public T Pick<T>(IReadOnlyList<T> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            if (items.Count == 0)
            {
                throw new ArgumentException("Cannot pick from an empty list.", nameof(items));
            }

            return items[NextInt(items.Count)];
        }

        private static ulong RotateLeft(ulong x, int k) => (x << k) | (x >> (64 - k));
    }
}
