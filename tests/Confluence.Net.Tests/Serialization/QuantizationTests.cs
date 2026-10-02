using System;
using Confluence.Core.Geometry;
using Confluence.Core.Random;
using Confluence.Net.Serialization;

namespace Confluence.Net.Tests.Serialization
{
    public sealed class QuantizationTests
    {
        [Fact]
        public void Positions_Round_Trip_Within_Half_A_Unit()
        {
            var rng = new Rng(11);
            var w = new NetWriter();
            var originals = new Vec3[200];
            for (int i = 0; i < originals.Length; i++)
            {
                originals[i] = new Vec3(Range(rng, 30f), Range(rng, 8f), Range(rng, 30f));
                w.WritePosition(originals[i]);
            }

            var r = new NetReader(w.ToArray());
            foreach (Vec3 original in originals)
            {
                Vec3 decoded = r.ReadPosition();
                Assert.InRange(Math.Abs(decoded.X - original.X), 0f, Quantization.PositionMaxError + 1e-6f);
                Assert.InRange(Math.Abs(decoded.Y - original.Y), 0f, Quantization.PositionMaxError + 1e-6f);
                Assert.InRange(Math.Abs(decoded.Z - original.Z), 0f, Quantization.PositionMaxError + 1e-6f);
            }

            r.ExpectEnd();

            // Arena-scale coordinates take about three bytes per axis.
            Assert.True(w.Length <= originals.Length * 9, "positions should pack into at most 9 bytes each, got " + w.Length);
        }

        [Fact]
        public void Positions_Are_Clamped_On_Write_And_Bounded_On_Read()
        {
            QuantizedVec3 q = Quantization.QuantizePosition(new Vec3(1e6f, -1e6f, 0f));
            Assert.Equal(Quantization.PositionLimitUnits, q.X);
            Assert.Equal(-Quantization.PositionLimitUnits, q.Y);

            var forged = new NetWriter();
            forged.WriteVarInt(Quantization.PositionLimitUnits + 1);
            forged.WriteVarInt(0);
            forged.WriteVarInt(0);
            Assert.Equal(NetReadError.InvalidValue, Assert.Throws<NetReadException>(() => new NetReader(forged.ToArray()).ReadPosition()).Error);
        }

        [Fact]
        public void Position_Deltas_Reproduce_The_Value_And_Stay_Small()
        {
            QuantizedVec3 baseline = Quantization.QuantizePosition(new Vec3(12.25f, 1.5f, -7f));
            QuantizedVec3 moved = Quantization.QuantizePosition(new Vec3(12.30f, 1.5f, -6.95f));
            var w = new NetWriter();
            w.WritePositionDelta(moved, baseline);
            Assert.True(w.Length <= 5, "a few centimeters of motion should cost a few bytes, got " + w.Length);
            Assert.Equal(moved, new NetReader(w.ToArray()).ReadPositionDelta(baseline));

            var escape = new NetWriter();
            escape.WritePositionDelta(new QuantizedVec3(Quantization.PositionLimitUnits + 50, 0, 0), new QuantizedVec3(0, 0, 0));
            Assert.Throws<NetReadException>(() => new NetReader(escape.ToArray()).ReadPositionDelta(new QuantizedVec3(0, 0, 0)));
        }

        [Theory]
        [InlineData(0f, 0f)]
        [InlineData(90f, 90f)]
        [InlineData(-90f, 270f)]
        [InlineData(360f, 0f)]
        [InlineData(725.5f, 5.5f)]
        [InlineData(359.999f, 0f)]
        public void Yaw_Wraps_Into_Zero_To_Three_Sixty(float degrees, float expected)
        {
            float decoded = Quantization.DequantizeAngle(Quantization.QuantizeAngle(degrees));
            float error = Math.Abs(decoded - expected);
            error = Math.Min(error, 360f - error);
            Assert.InRange(error, 0f, Quantization.AngleMaxErrorDeg + 1e-4f);
        }

        [Fact]
        public void Pitch_Is_Clamped_To_Straight_Up_And_Down()
        {
            var w = new NetWriter();
            w.WritePitch(-120f);
            w.WritePitch(45.3f);
            w.WritePitch(90f);
            var r = new NetReader(w.ToArray());
            Assert.Equal(-90f, r.ReadPitch());
            Assert.InRange(Math.Abs(r.ReadPitch() - 45.3f), 0f, Quantization.RangeMaxError(-90f, 90f, 16) + 1e-5f);
            Assert.Equal(90f, r.ReadPitch());
        }

        [Fact]
        public void Range_Quantizer_Error_Is_Bounded_By_Its_Resolution()
        {
            var rng = new Rng(5);
            float maxError = Quantization.RangeMaxError(0f, 25f, 10);
            for (int i = 0; i < 500; i++)
            {
                float v = (float)(rng.NextDouble() * 25.0);
                uint units = Quantization.QuantizeRange(v, 0f, 25f, 10);
                Assert.InRange(units, 0u, 1023u);
                Assert.InRange(Math.Abs(Quantization.DequantizeRange(units, 0f, 25f, 10) - v), 0f, maxError + 1e-5f);
            }

            Assert.Equal(1023u, Quantization.QuantizeRange(99f, 0f, 25f, 10));
            Assert.Equal(25f, Quantization.DequantizeRange(5000u, 0f, 25f, 10));
            Assert.Throws<ArgumentException>(() => Quantization.QuantizeRange(1f, 5f, 5f, 8));
        }

        private static float Range(Rng rng, float halfExtent) => rng.Range(-halfExtent, halfExtent);
    }
}
