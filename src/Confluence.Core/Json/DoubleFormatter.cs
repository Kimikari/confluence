using System;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Confluence.Core.Json
{
    /// <summary>
    /// Shortest round-trip decimal text for a double, computed with exact integer arithmetic so the result never
    /// depends on the runtime's number formatting. .NET Core 3.0+ "R" and Mono / .NET Framework "R" (G15, else G17)
    /// disagree on values such as 0.1 + 0.7; this produces the .NET Core text on every runtime, including Unity.
    /// The layout follows .NET Core: plain notation for decimal exponents -4 through 16, otherwise "d.dddE+XX".
    /// </summary>
    internal static class DoubleFormatter
    {
        private const int MaxSignificantDigits = 17;
        private const int MinPlainExponent = -4;
        private const int MaxPlainExponent = 16;

        public static string ShortestRoundTrip(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Only finite values have decimal text.");
            }

            if (value == 0d)
            {
                return BitConverter.DoubleToInt64Bits(value) < 0 ? "-0" : "0";
            }

            bool negative = value < 0d;
            long bits = BitConverter.DoubleToInt64Bits(value) & long.MaxValue;
            int exponentBits = (int)(bits >> 52);
            long fraction = bits & ((1L << 52) - 1);

            // value = m * 2^e exactly.
            long m = exponentBits == 0 ? fraction : fraction | (1L << 52);
            int e = (exponentBits == 0 ? 1 : exponentBits) - 1075;

            // Rounding interval in units of 2^(e-2): the midpoints to the neighbouring doubles. The gap below a
            // power of two is half the gap above it. Midpoints parse back to value only when m is even.
            BigInteger center = new BigInteger(m) * 4;
            BigInteger low = center - (fraction == 0 && exponentBits > 1 ? 1 : 2);
            BigInteger high = center + 2;
            bool inclusive = (m & 1) == 0;

            int firstDigitExponent = DecimalExponent(m, e);
            for (int precision = 1; precision <= MaxSignificantDigits; precision++)
            {
                int k = firstDigitExponent - precision + 1; // candidate = digits * 10^k
                BigInteger floor = FloorDiv(m, e, k);
                bool floorFits = InInterval(floor, k, low, high, e, inclusive);
                bool ceilFits = InInterval(floor + 1, k, low, high, e, inclusive);
                if (!floorFits && !ceilFits)
                {
                    continue;
                }

                BigInteger digits;
                if (floorFits && ceilFits)
                {
                    // Both neighbours round-trip: take the closer one; an exact tie keeps the even last digit.
                    int cmp = CompareToMidpoint(m, e, floor, k);
                    digits = cmp < 0 || (cmp == 0 && floor.IsEven) ? floor : floor + 1;
                }
                else
                {
                    digits = floorFits ? floor : floor + 1;
                }

                return Layout(negative, digits, k);
            }

            // Unreachable: 17 significant digits always identify a double.
            return value.ToString("G17", CultureInfo.InvariantCulture);
        }

        // floor(log10(m * 2^e)), corrected exactly after a floating-point estimate.
        private static int DecimalExponent(long m, int e)
        {
            double estimate = Math.Log10(m) + e * Math.Log10(2d);
            int x = (int)Math.Floor(estimate);
            while (ComparePow10(m, e, x) < 0) x--;      // value < 10^x
            while (ComparePow10(m, e, x + 1) >= 0) x++; // value >= 10^(x+1)
            return x;
        }

        // Sign of (m * 2^e) - 10^x.
        private static int ComparePow10(long m, int e, int x)
        {
            BigInteger left = new BigInteger(m) * Pow2(Math.Max(e, 0)) * BigInteger.Pow(10, Math.Max(-x, 0));
            BigInteger right = BigInteger.Pow(10, Math.Max(x, 0)) * Pow2(Math.Max(-e, 0));
            return left.CompareTo(right);
        }

        // floor(m * 2^e / 10^k).
        private static BigInteger FloorDiv(long m, int e, int k)
        {
            BigInteger num = new BigInteger(m) * Pow2(Math.Max(e, 0)) * BigInteger.Pow(10, Math.Max(-k, 0));
            BigInteger den = Pow2(Math.Max(-e, 0)) * BigInteger.Pow(10, Math.Max(k, 0));
            return BigInteger.Divide(num, den);
        }

        // Whether digits * 10^k lies in [low, high] * 2^(e-2) (open at the ends unless inclusive).
        private static bool InInterval(BigInteger digits, int k, BigInteger low, BigInteger high, int e, bool inclusive)
        {
            Scale(e - 2, k, out BigInteger boundScale, out BigInteger decimalScale);
            BigInteger candidate = digits * decimalScale;
            int lowCmp = candidate.CompareTo(low * boundScale);
            int highCmp = candidate.CompareTo(high * boundScale);
            return inclusive ? lowCmp >= 0 && highCmp <= 0 : lowCmp > 0 && highCmp < 0;
        }

        // Sign of value - (floor + 1/2) * 10^k: negative means floor is the closer candidate.
        private static int CompareToMidpoint(long m, int e, BigInteger floor, int k)
        {
            // Compare 2 * m * 2^e with (2 * floor + 1) * 10^k, both scaled to integers.
            Scale(e, k, out BigInteger valueScale, out BigInteger decimalScale);
            BigInteger left = new BigInteger(m) * 2 * valueScale;
            BigInteger right = (floor * 2 + 1) * decimalScale;
            return left.CompareTo(right);
        }

        // Integer multipliers that bring x * 2^binaryExponent and y * 10^decimalExponent to a common integer scale.
        private static void Scale(int binaryExponent, int decimalExponent, out BigInteger binaryScale, out BigInteger decimalScale)
        {
            int shift2 = Math.Max(0, -binaryExponent);
            int shift10 = Math.Max(0, -decimalExponent);
            binaryScale = Pow2(binaryExponent + shift2) * BigInteger.Pow(10, shift10);
            decimalScale = BigInteger.Pow(10, decimalExponent + shift10) * Pow2(shift2);
        }

        private static BigInteger Pow2(int exponent) => BigInteger.One << exponent;

        private static string Layout(bool negative, BigInteger digitsValue, int k)
        {
            string digits = digitsValue.ToString(CultureInfo.InvariantCulture);
            int trailing = 0;
            while (trailing < digits.Length - 1 && digits[digits.Length - 1 - trailing] == '0') trailing++;
            digits = digits.Substring(0, digits.Length - trailing);
            int exponent = k + trailing + digits.Length - 1; // decimal exponent of the first digit

            var sb = new StringBuilder(digits.Length + 8);
            if (negative) sb.Append('-');
            if (exponent < MinPlainExponent || exponent > MaxPlainExponent)
            {
                sb.Append(digits[0]);
                if (digits.Length > 1) sb.Append('.').Append(digits, 1, digits.Length - 1);
                sb.Append('E').Append(exponent < 0 ? '-' : '+');
                sb.Append(Math.Abs(exponent).ToString("00", CultureInfo.InvariantCulture));
            }
            else if (exponent >= 0)
            {
                if (digits.Length <= exponent + 1)
                {
                    sb.Append(digits).Append('0', exponent + 1 - digits.Length);
                }
                else
                {
                    sb.Append(digits, 0, exponent + 1).Append('.').Append(digits, exponent + 1, digits.Length - exponent - 1);
                }
            }
            else
            {
                sb.Append("0.").Append('0', -exponent - 1).Append(digits);
            }

            return sb.ToString();
        }
    }
}
