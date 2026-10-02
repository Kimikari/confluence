using System;
using Confluence.Net.Serialization;

namespace Confluence.Net.Tests.Serialization
{
    public sealed class SerializationTests
    {
        [Fact]
        public void Primitives_Round_Trip_Little_Endian()
        {
            var w = new NetWriter(16);
            w.WriteByte(0xAB);
            w.WriteSByte(-5);
            w.WriteBool(true);
            w.WriteUShort(0xBEEF);
            w.WriteShort(-12345);
            w.WriteUInt(0xDEADBEEF);
            w.WriteInt(int.MinValue);
            w.WriteULong(0x0123456789ABCDEFUL);
            w.WriteLong(long.MinValue);
            w.WriteFloat(-1.5f);
            w.WriteDouble(Math.PI);
            w.WriteString("Ünïcode wizard");
            w.WriteBlob(new byte[] { 9, 8, 7 }, 0, 3);

            byte[] bytes = w.ToArray();
            Assert.Equal(0xEF, bytes[3]); // ushort low byte first
            Assert.Equal(0xBE, bytes[4]);

            var r = new NetReader(bytes);
            Assert.Equal(0xAB, r.ReadByte());
            Assert.Equal(-5, r.ReadSByte());
            Assert.True(r.ReadBool());
            Assert.Equal(0xBEEF, r.ReadUShort());
            Assert.Equal(-12345, r.ReadShort());
            Assert.Equal(0xDEADBEEF, r.ReadUInt());
            Assert.Equal(int.MinValue, r.ReadInt());
            Assert.Equal(0x0123456789ABCDEFUL, r.ReadULong());
            Assert.Equal(long.MinValue, r.ReadLong());
            Assert.Equal(-1.5f, r.ReadFloat());
            Assert.Equal(Math.PI, r.ReadDouble());
            Assert.Equal("Ünïcode wizard", r.ReadString());
            Assert.Equal(new byte[] { 9, 8, 7 }, r.ReadBlob(16));
            r.ExpectEnd();
        }

        [Theory]
        [InlineData(0u, 1)]
        [InlineData(127u, 1)]
        [InlineData(128u, 2)]
        [InlineData(16383u, 2)]
        [InlineData(16384u, 3)]
        [InlineData(uint.MaxValue, 5)]
        public void VarUInt_Uses_Seven_Bits_Per_Byte(uint value, int expectedBytes)
        {
            var w = new NetWriter();
            w.WriteVarUInt(value);
            Assert.Equal(expectedBytes, w.Length);
            Assert.Equal(value, new NetReader(w.ToArray()).ReadVarUInt());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(1)]
        [InlineData(-64)]
        [InlineData(63)]
        [InlineData(int.MaxValue)]
        [InlineData(int.MinValue)]
        public void Zigzag_Varints_Round_Trip_And_Keep_Small_Magnitudes_Short(int value)
        {
            var w = new NetWriter();
            w.WriteVarInt(value);
            w.WriteVarLong(value * 3L);
            var r = new NetReader(w.ToArray());
            Assert.Equal(value, r.ReadVarInt());
            Assert.Equal(value * 3L, r.ReadVarLong());
            r.ExpectEnd();

            var small = new NetWriter();
            small.WriteVarInt(Math.Max(-64, Math.Min(63, value)));
            Assert.Equal(1, small.Length);
        }

        [Fact]
        public void Long_Varints_Round_Trip_At_The_Extremes()
        {
            var w = new NetWriter();
            w.WriteVarULong(ulong.MaxValue);
            w.WriteVarLong(long.MinValue);
            w.WriteVarLong(long.MaxValue);
            Assert.Equal(30, w.Length);
            var r = new NetReader(w.ToArray());
            Assert.Equal(ulong.MaxValue, r.ReadVarULong());
            Assert.Equal(long.MinValue, r.ReadVarLong());
            Assert.Equal(long.MaxValue, r.ReadVarLong());
        }

        [Fact]
        public void Truncated_Data_Throws_End_Of_Data_At_Every_Cut()
        {
            var w = new NetWriter();
            w.WriteUInt(7);
            w.WriteString("abc");
            w.WriteDouble(2.0);
            byte[] full = w.ToArray();
            for (int cut = 0; cut < full.Length; cut++)
            {
                var r = new NetReader(full, 0, cut);
                NetReadException e = Assert.Throws<NetReadException>(() =>
                {
                    r.ReadUInt();
                    r.ReadString();
                    r.ReadDouble();
                });
                Assert.Equal(NetReadError.EndOfData, e.Error);
            }
        }

        [Fact]
        public void Reader_Respects_The_Range_It_Was_Given()
        {
            byte[] data = { 1, 2, 3, 4, 5, 6 };
            var r = new NetReader(data, 2, 2);
            Assert.Equal(2, r.Length);
            Assert.Equal(0x0403, r.ReadUShort());
            Assert.True(r.IsAtEnd);
            Assert.Equal(NetReadError.EndOfData, Assert.Throws<NetReadException>(() => r.ReadByte()).Error);
            Assert.Throws<ArgumentOutOfRangeException>(() => new NetReader(data, 4, 3));
        }

        [Fact]
        public void Overlong_And_Overflowing_Varints_Are_Rejected()
        {
            byte[] sixBytes = { 0x80, 0x80, 0x80, 0x80, 0x80, 0x01 };
            Assert.Equal(NetReadError.VarIntOverflow, Assert.Throws<NetReadException>(() => new NetReader(sixBytes).ReadVarUInt()).Error);

            byte[] over32 = { 0xFF, 0xFF, 0xFF, 0xFF, 0x10 };
            Assert.Equal(NetReadError.VarIntOverflow, Assert.Throws<NetReadException>(() => new NetReader(over32).ReadVarUInt()).Error);

            byte[] over64 = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x02 };
            Assert.Equal(NetReadError.VarIntOverflow, Assert.Throws<NetReadException>(() => new NetReader(over64).ReadVarULong()).Error);
        }

        [Fact]
        public void Strings_Are_Capped_And_Strictly_Utf8()
        {
            var w = new NetWriter();
            w.WriteString(new string('x', 40), 64);
            Assert.Equal(NetReadError.StringTooLong, Assert.Throws<NetReadException>(() => new NetReader(w.ToArray()).ReadString(32)).Error);
            Assert.Throws<ArgumentException>(() => new NetWriter().WriteString(new string('x', 40), 32));

            byte[] invalid = { 2, 0xC3, 0x28 };
            Assert.Equal(NetReadError.InvalidUtf8, Assert.Throws<NetReadException>(() => new NetReader(invalid).ReadString()).Error);

            var empty = new NetWriter();
            empty.WriteString(null);
            Assert.Equal("", new NetReader(empty.ToArray()).ReadString());
        }

        [Fact]
        public void Forged_Counts_Cannot_Exceed_Their_Cap_Or_The_Remaining_Bytes()
        {
            var w = new NetWriter();
            w.WriteVarUInt(1_000_000);
            Assert.Equal(NetReadError.CountOutOfRange, Assert.Throws<NetReadException>(() => new NetReader(w.ToArray()).ReadCount(100)).Error);

            var w2 = new NetWriter();
            w2.WriteVarUInt(10);
            w2.WriteBytes(new byte[] { 1, 2, 3 });
            Assert.Equal(NetReadError.CountOutOfRange, Assert.Throws<NetReadException>(() => new NetReader(w2.ToArray()).ReadCount(100, 1)).Error);

            var w3 = new NetWriter();
            w3.WriteVarUInt(3);
            w3.WriteBytes(new byte[] { 1, 2, 3 });
            Assert.Equal(3, new NetReader(w3.ToArray()).ReadCount(100, 1));
        }

        [Fact]
        public void Impossible_Values_Are_Rejected()
        {
            var nan = new NetWriter();
            nan.WriteFloat(float.NaN);
            nan.WriteFloat(float.PositiveInfinity);
            byte[] bytes = nan.ToArray();
            Assert.Equal(NetReadError.InvalidValue, Assert.Throws<NetReadException>(() => new NetReader(bytes).ReadFloat()).Error);
            var raw = new NetReader(bytes);
            Assert.True(float.IsNaN(raw.ReadFloatRaw()));
            Assert.True(float.IsPositiveInfinity(raw.ReadFloatRaw()));

            var inf = new NetWriter();
            inf.WriteDouble(double.NegativeInfinity);
            Assert.Throws<NetReadException>(() => new NetReader(inf.ToArray()).ReadDouble());

            Assert.Equal(NetReadError.InvalidValue, Assert.Throws<NetReadException>(() => new NetReader(new byte[] { 2 }).ReadBool()).Error);
            Assert.Throws<NetReadException>(() => new NetReader(new byte[] { 9 }).ReadByteInRange(0, 4));

            var ranged = new NetWriter();
            ranged.WriteVarInt(-3);
            Assert.Throws<NetReadException>(() => new NetReader(ranged.ToArray()).ReadVarIntInRange(0, 10));
        }

        [Fact]
        public void Trailing_Bytes_Fail_Expect_End()
        {
            var r = new NetReader(new byte[] { 1, 2 });
            r.ReadByte();
            Assert.Equal(NetReadError.TrailingBytes, Assert.Throws<NetReadException>(() => r.ExpectEnd()).Error);
            Assert.Equal(new byte[] { 2 }, r.RemainingToArray());
        }

        [Fact]
        public void Writer_Grows_And_Resets()
        {
            var w = new NetWriter(16);
            var big = new byte[1000];
            big[999] = 42;
            w.WriteBytes(big);
            Assert.Equal(1000, w.Length);
            Assert.True(w.Buffer.Length >= 1000);
            Assert.Equal(42, w.ToArray()[999]);
            w.Reset();
            Assert.Equal(0, w.Length);
            Assert.Empty(w.ToArray());
        }
    }
}
