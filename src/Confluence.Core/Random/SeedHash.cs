using System;

namespace Confluence.Core.Random
{
    /// <summary>
    /// Deterministic 64-bit hashing for seeds and stable identifiers. Unlike string.GetHashCode, results are
    /// identical across processes, runtimes, and platforms.
    /// </summary>
    public static class SeedHash
    {
        public const ulong FnvOffsetBasis = 0xCBF29CE484222325UL;
        public const ulong FnvPrime = 0x00000100000001B3UL;

        private const ulong Golden = 0x9E3779B97F4A7C15UL;

        /// <summary>FNV-1a over the UTF-8 encoding of s (allocation-free). Null hashes like the empty string.</summary>
        public static ulong Fnv1a64(string s)
        {
            ulong hash = FnvOffsetBasis;
            if (s == null)
            {
                return hash;
            }

            unchecked
            {
                for (int i = 0; i < s.Length; i++)
                {
                    int cp = s[i];
                    if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                    {
                        cp = char.ConvertToUtf32(s[i], s[i + 1]);
                        i++;
                    }
                    else if (char.IsSurrogate(s[i]))
                    {
                        cp = 0xFFFD; // lone surrogate encodes as U+FFFD, matching UTF8Encoding's replacement
                    }

                    if (cp < 0x80)
                    {
                        hash = (hash ^ (uint)cp) * FnvPrime;
                    }
                    else if (cp < 0x800)
                    {
                        hash = (hash ^ (uint)(0xC0 | (cp >> 6))) * FnvPrime;
                        hash = (hash ^ (uint)(0x80 | (cp & 0x3F))) * FnvPrime;
                    }
                    else if (cp < 0x10000)
                    {
                        hash = (hash ^ (uint)(0xE0 | (cp >> 12))) * FnvPrime;
                        hash = (hash ^ (uint)(0x80 | ((cp >> 6) & 0x3F))) * FnvPrime;
                        hash = (hash ^ (uint)(0x80 | (cp & 0x3F))) * FnvPrime;
                    }
                    else
                    {
                        hash = (hash ^ (uint)(0xF0 | (cp >> 18))) * FnvPrime;
                        hash = (hash ^ (uint)(0x80 | ((cp >> 12) & 0x3F))) * FnvPrime;
                        hash = (hash ^ (uint)(0x80 | ((cp >> 6) & 0x3F))) * FnvPrime;
                        hash = (hash ^ (uint)(0x80 | (cp & 0x3F))) * FnvPrime;
                    }
                }
            }

            return hash;
        }

        /// <summary>Strong 64-bit finalizer (the SplitMix64 / Stafford variant 13 mixer). Bijective.</summary>
        public static ulong Mix(ulong x)
        {
            unchecked
            {
                x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
                x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
                return x ^ (x >> 31);
            }
        }

        /// <summary>Well-mixed 64-bit hash of a string: Mix(FNV-1a(UTF-8)).</summary>
        public static ulong Of(string s) => Mix(Fnv1a64(s));

        /// <summary>Order-dependent combination of two values. Equal to Combine(new[] { a, b }).</summary>
        public static ulong Combine(ulong a, ulong b) => Step(Step(Start(2), a), b);

        public static ulong Combine(ulong a, ulong b, ulong c) => Step(Step(Step(Start(3), a), b), c);

        public static ulong Combine(ulong a, ulong b, ulong c, ulong d) => Step(Step(Step(Step(Start(4), a), b), c), d);

        /// <summary>
        /// Order-dependent combination of any number of values. The count is mixed in first, so a sequence and
        /// its prefix never collide trivially. The fixed-arity overloads give identical results.
        /// </summary>
        public static ulong Combine(params ulong[] values)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            ulong hash = Start(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                hash = Step(hash, values[i]);
            }

            return hash;
        }

        private static ulong Start(int count) => Mix(Golden ^ (ulong)count);

        private static ulong Step(ulong hash, ulong value)
        {
            unchecked
            {
                return Mix(hash + Golden + Mix(value ^ (hash << 6) ^ (hash >> 2)));
            }
        }
    }
}
