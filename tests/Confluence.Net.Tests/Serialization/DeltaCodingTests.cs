using System;
using Confluence.Net.Serialization;

namespace Confluence.Net.Tests.Serialization
{
    public sealed class DeltaCodingTests
    {
        [Fact]
        public void Int_Deltas_Round_Trip_Including_Wraparound()
        {
            var w = new NetWriter();
            w.WriteIntDelta(105, 100);
            Assert.Equal(1, w.Length); // a small change is one byte
            w.WriteIntDelta(int.MinValue, int.MaxValue);
            w.WriteIntDelta(-7, 0);

            var r = new NetReader(w.ToArray());
            Assert.Equal(105, r.ReadIntDelta(100));
            Assert.Equal(int.MinValue, r.ReadIntDelta(int.MaxValue));
            Assert.Equal(-7, r.ReadIntDelta(0));
            r.ExpectEnd();
        }

        [Fact]
        public void Forward_Deltas_Never_Go_Backwards_Or_Overflow()
        {
            var w = new NetWriter();
            w.WriteForwardDelta(1000, 990);
            Assert.Equal(1000u, new NetReader(w.ToArray()).ReadForwardDelta(990));
            Assert.Throws<ArgumentException>(() => new NetWriter().WriteForwardDelta(5, 6));

            var forged = new NetWriter();
            forged.WriteVarUInt(10);
            Assert.Equal(NetReadError.InvalidValue,
                Assert.Throws<NetReadException>(() => new NetReader(forged.ToArray()).ReadForwardDelta(uint.MaxValue - 5)).Error);
        }

        [Fact]
        public void Scaled_Deltas_Decode_To_The_Same_Grid_Value_On_Both_Ends()
        {
            const int scale = 100; // centi-units
            float baseline = 47.31f;
            float value = 52.987f;
            var w = new NetWriter();
            w.WriteScaledDelta(value, baseline, scale);
            float decoded = new NetReader(w.ToArray()).ReadScaledDelta(baseline, scale);
            Assert.Equal(DeltaCoding.ToGrid(value, scale) / (float)scale, decoded);
            Assert.InRange(Math.Abs(decoded - value), 0f, 0.5f / scale + 1e-4f);

            Assert.Equal(int.MaxValue, DeltaCoding.ToGrid(1e12f, scale));
            Assert.Equal(int.MinValue, DeltaCoding.ToGrid(-1e12f, scale));
            Assert.Throws<ArgumentOutOfRangeException>(() => DeltaCoding.ToGrid(1f, 0));
        }

        [Fact]
        public void Change_Masks_Mark_Fields_And_Reject_Unknown_Ones()
        {
            var mask = new ChangeMask();
            Assert.True(mask.IsEmpty);
            mask.Mark(0);
            mask.Mark(5);
            mask.Mark(3, false);
            Assert.True(mask[0]);
            Assert.True(mask[5]);
            Assert.False(mask[3]);

            var w = new NetWriter();
            mask.Write(w);
            Assert.Equal(mask, ChangeMask.Read(new NetReader(w.ToArray()), 6));
            Assert.Equal(NetReadError.InvalidValue, Assert.Throws<NetReadException>(() => ChangeMask.Read(new NetReader(w.ToArray()), 5)).Error);

            mask.Mark(5, false); // an unchanged field leaves an earlier mark alone
            Assert.True(mask[5]);
            Assert.Throws<ArgumentOutOfRangeException>(() => mask.Mark(32));

            var full = new NetWriter();
            new ChangeMask(uint.MaxValue).Write(full);
            Assert.Equal(uint.MaxValue, ChangeMask.Read(new NetReader(full.ToArray()), ChangeMask.MaxFields).Bits);
        }
    }
}
