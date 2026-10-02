using System;
using Confluence.Core.Geometry;

namespace Confluence.Net.Serialization
{
    /// <summary>
    /// Fixed-point position: integer millimeter-scale units per axis. Snapshots carry these instead of floats so
    /// deltas against a baseline compress to a byte or two per axis.
    /// </summary>
    public struct QuantizedVec3 : IEquatable<QuantizedVec3>
    {
        public int X;
        public int Y;
        public int Z;

        public QuantizedVec3(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public bool Equals(QuantizedVec3 other) => X == other.X && Y == other.Y && Z == other.Z;

        public override bool Equals(object obj) => obj is QuantizedVec3 other && Equals(other);

        public override int GetHashCode() => unchecked((X * 73856093) ^ (Y * 19349663) ^ (Z * 83492791));

        public static bool operator ==(QuantizedVec3 a, QuantizedVec3 b) => a.Equals(b);

        public static bool operator !=(QuantizedVec3 a, QuantizedVec3 b) => !a.Equals(b);

        public override string ToString() => "(" + X + ", " + Y + ", " + Z + ")";
    }

    /// <summary>
    /// Quantization for positions, angles, and bounded scalars. Resolutions are chosen so that quantization
    /// error stays far below anything gameplay can see: 1/1024 m for positions (under 0.5 mm per axis) and
    /// 360/65536 degrees for yaw (about 0.0055 degrees, under 0.1 mm of aim drift at 1 m).
    /// </summary>
    public static class Quantization
    {
        /// <summary>Position units per meter.</summary>
        public const int PositionScale = 1024;

        /// <summary>
        /// Largest accepted coordinate magnitude in meters. Arenas are at most 48 m across (spec L306) and routes
        /// rise at most a few meters (L312); a generous bound still rejects forged far-away positions.
        /// </summary>
        public const float PositionLimit = 512f;

        public const int PositionLimitUnits = (int)(PositionLimit * PositionScale);

        /// <summary>Worst-case per-axis error of a quantized position.</summary>
        public const float PositionMaxError = 0.5f / PositionScale;

        public const float AngleMaxErrorDeg = 180f / 65536f;

        public static int QuantizeCoordinate(float meters)
        {
            float clamped = CMath.Clamp(meters, -PositionLimit, PositionLimit);
            return (int)MathF.Round(clamped * PositionScale, MidpointRounding.AwayFromZero);
        }

        public static float DequantizeCoordinate(int units) => units / (float)PositionScale;

        public static QuantizedVec3 QuantizePosition(Vec3 position)
        {
            return new QuantizedVec3(QuantizeCoordinate(position.X), QuantizeCoordinate(position.Y), QuantizeCoordinate(position.Z));
        }

        public static Vec3 DequantizePosition(QuantizedVec3 q)
        {
            return new Vec3(DequantizeCoordinate(q.X), DequantizeCoordinate(q.Y), DequantizeCoordinate(q.Z));
        }

        /// <summary>Yaw or any wrapping angle in degrees to 16 bits (0 = 0 degrees, 65535 just below 360).</summary>
        public static ushort QuantizeAngle(float degrees)
        {
            double wrapped = CMath.WrapAngle360(degrees);
            long units = (long)Math.Round(wrapped / 360.0 * 65536.0, MidpointRounding.AwayFromZero);
            return (ushort)(units & 0xFFFF);
        }

        /// <summary>Angle in [0, 360).</summary>
        public static float DequantizeAngle(ushort units) => (float)(units * (360.0 / 65536.0));

        /// <summary>Pitch clamped to [-90, 90] degrees, 16 bits.</summary>
        public static ushort QuantizePitch(float degrees)
        {
            return (ushort)QuantizeRange(degrees, -90f, 90f, 16);
        }

        public static float DequantizePitch(ushort units) => DequantizeRange(units, -90f, 90f, 16);

        /// <summary>Maps <paramref name="value"/> clamped to [min, max] onto 0 .. 2^bits - 1.</summary>
        public static uint QuantizeRange(float value, float min, float max, int bits)
        {
            ValidateRange(min, max, bits);
            uint steps = MaxUnits(bits);
            float t = CMath.InverseLerp(min, max, CMath.Clamp(value, min, max));
            return (uint)Math.Round(t * (double)steps, MidpointRounding.AwayFromZero);
        }

        public static float DequantizeRange(uint units, float min, float max, int bits)
        {
            ValidateRange(min, max, bits);
            uint steps = MaxUnits(bits);
            if (units > steps)
            {
                units = steps;
            }

            return (float)(min + (max - min) * (units / (double)steps));
        }

        /// <summary>Largest error of <see cref="QuantizeRange"/> for a given range and width.</summary>
        public static float RangeMaxError(float min, float max, int bits) => (max - min) / MaxUnits(bits) * 0.5f;

        // ---- Reader and writer helpers ---------------------------------------------------------

        /// <summary>Absolute position: three zigzag varints (about 3 bytes per axis inside an arena).</summary>
        public static void WritePosition(this NetWriter writer, Vec3 position) => writer.WriteQuantizedPosition(QuantizePosition(position));

        public static void WriteQuantizedPosition(this NetWriter writer, QuantizedVec3 q)
        {
            writer.WriteVarInt(q.X);
            writer.WriteVarInt(q.Y);
            writer.WriteVarInt(q.Z);
        }

        public static QuantizedVec3 ReadQuantizedPosition(this NetReader reader)
        {
            return new QuantizedVec3(ReadAxis(reader), ReadAxis(reader), ReadAxis(reader));
        }

        public static Vec3 ReadPosition(this NetReader reader) => DequantizePosition(reader.ReadQuantizedPosition());

        /// <summary>Position as a per-axis difference from <paramref name="baseline"/>.</summary>
        public static void WritePositionDelta(this NetWriter writer, QuantizedVec3 value, QuantizedVec3 baseline)
        {
            writer.WriteVarInt(unchecked(value.X - baseline.X));
            writer.WriteVarInt(unchecked(value.Y - baseline.Y));
            writer.WriteVarInt(unchecked(value.Z - baseline.Z));
        }

        public static QuantizedVec3 ReadPositionDelta(this NetReader reader, QuantizedVec3 baseline)
        {
            int x = CheckAxis(unchecked(baseline.X + reader.ReadVarInt()));
            int y = CheckAxis(unchecked(baseline.Y + reader.ReadVarInt()));
            int z = CheckAxis(unchecked(baseline.Z + reader.ReadVarInt()));
            return new QuantizedVec3(x, y, z);
        }

        public static void WriteAngle(this NetWriter writer, float degrees) => writer.WriteUShort(QuantizeAngle(degrees));

        public static float ReadAngle(this NetReader reader) => DequantizeAngle(reader.ReadUShort());

        public static void WritePitch(this NetWriter writer, float degrees) => writer.WriteUShort(QuantizePitch(degrees));

        public static float ReadPitch(this NetReader reader) => DequantizePitch(reader.ReadUShort());

        private static int ReadAxis(NetReader reader) => CheckAxis(reader.ReadVarInt());

        private static int CheckAxis(int units)
        {
            if (units < -PositionLimitUnits || units > PositionLimitUnits)
            {
                throw new NetReadException(NetReadError.InvalidValue, "position coordinate outside the accepted range");
            }

            return units;
        }

        private static uint MaxUnits(int bits) => bits >= 32 ? uint.MaxValue : (1u << bits) - 1u;

        private static void ValidateRange(float min, float max, int bits)
        {
            if (!(max > min)) throw new ArgumentException("max must exceed min.");
            if (bits < 1 || bits > 32) throw new ArgumentOutOfRangeException(nameof(bits), bits, "Must be 1-32.");
        }
    }
}
