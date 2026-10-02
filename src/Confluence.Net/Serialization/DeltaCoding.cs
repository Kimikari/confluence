using System;

namespace Confluence.Net.Serialization
{
    /// <summary>
    /// Bit set of changed fields for delta-encoded records (up to 32 fields). Write the mask first, then only
    /// the fields whose bit is set; the reader rejects bits beyond the record's declared field count.
    /// </summary>
    public struct ChangeMask : IEquatable<ChangeMask>
    {
        public const int MaxFields = 32;

        public ChangeMask(uint bits)
        {
            Bits = bits;
        }

        public uint Bits { get; private set; }

        public bool IsEmpty => Bits == 0;

        public bool this[int field] => (Bits & Bit(field)) != 0;

        /// <summary>Marks <paramref name="field"/> when <paramref name="changed"/> is true.</summary>
        public void Mark(int field, bool changed = true)
        {
            if (changed)
            {
                Bits |= Bit(field);
            }
        }

        public void Write(NetWriter writer) => writer.WriteVarUInt(Bits);

        /// <summary>Reads a mask whose set bits must all be below <paramref name="fieldCount"/>.</summary>
        public static ChangeMask Read(NetReader reader, int fieldCount)
        {
            if (fieldCount < 0 || fieldCount > MaxFields) throw new ArgumentOutOfRangeException(nameof(fieldCount));
            uint bits = reader.ReadVarUInt();
            if (fieldCount < MaxFields && (bits >> fieldCount) != 0)
            {
                throw new NetReadException(NetReadError.InvalidValue, "change mask names an unknown field");
            }

            return new ChangeMask(bits);
        }

        public bool Equals(ChangeMask other) => Bits == other.Bits;

        public override bool Equals(object obj) => obj is ChangeMask other && Equals(other);

        public override int GetHashCode() => (int)Bits;

        private static uint Bit(int field)
        {
            if (field < 0 || field >= MaxFields) throw new ArgumentOutOfRangeException(nameof(field), field, "Field index must be 0-31.");
            return 1u << field;
        }
    }

    /// <summary>
    /// Delta helpers: values are sent as zigzag varint differences from a baseline both ends already hold
    /// (typically the last snapshot the client acknowledged).
    /// </summary>
    public static class DeltaCoding
    {
        public static void WriteIntDelta(this NetWriter writer, int value, int baseline) => writer.WriteVarInt(unchecked(value - baseline));

        public static int ReadIntDelta(this NetReader reader, int baseline) => unchecked(baseline + reader.ReadVarInt());

        /// <summary>Difference of a monotonically increasing counter (ticks, sequences); must not go backwards.</summary>
        public static void WriteForwardDelta(this NetWriter writer, uint value, uint baseline)
        {
            if (value < baseline) throw new ArgumentException("A forward delta cannot decrease.", nameof(value));
            writer.WriteVarUInt(value - baseline);
        }

        public static uint ReadForwardDelta(this NetReader reader, uint baseline)
        {
            uint delta = reader.ReadVarUInt();
            if (delta > uint.MaxValue - baseline)
            {
                throw new NetReadException(NetReadError.InvalidValue, "forward delta overflows");
            }

            return baseline + delta;
        }

        /// <summary>
        /// Float sent as a quantized difference on a fixed grid (<paramref name="scale"/> units per 1.0). Encoding
        /// both value and baseline on the grid keeps the decoded result identical on both ends.
        /// </summary>
        public static void WriteScaledDelta(this NetWriter writer, float value, float baseline, int scale)
        {
            writer.WriteVarInt(unchecked(ToGrid(value, scale) - ToGrid(baseline, scale)));
        }

        public static float ReadScaledDelta(this NetReader reader, float baseline, int scale)
        {
            long units = (long)ToGrid(baseline, scale) + reader.ReadVarInt();
            if (units < int.MinValue || units > int.MaxValue)
            {
                throw new NetReadException(NetReadError.InvalidValue, "scaled delta overflows");
            }

            return units / (float)scale;
        }

        /// <summary>Nearest grid point of <paramref name="value"/> (saturating).</summary>
        public static int ToGrid(float value, int scale)
        {
            if (scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
            double scaled = Math.Round((double)value * scale, MidpointRounding.AwayFromZero);
            if (scaled >= int.MaxValue) return int.MaxValue;
            if (scaled <= int.MinValue) return int.MinValue;
            return (int)scaled;
        }
    }
}
