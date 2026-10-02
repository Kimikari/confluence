using System;
using System.Text;

namespace Confluence.Net.Serialization
{
    /// <summary>
    /// Growable little-endian binary writer for network messages. Byte order and encodings are explicit, so the
    /// .NET server and the Unity client produce identical bytes. Integers can be written fixed-width or as
    /// LEB128 varints (zigzag for signed values); strings are UTF-8 with a varint byte length.
    /// </summary>
    public sealed class NetWriter
    {
        public const int DefaultMaxStringBytes = 256;

        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        private byte[] _buffer;
        private int _length;

        public NetWriter(int initialCapacity = 256)
        {
            _buffer = new byte[Math.Max(16, initialCapacity)];
        }

        /// <summary>Bytes written so far.</summary>
        public int Length => _length;

        /// <summary>Backing array; only the first <see cref="Length"/> bytes are meaningful.</summary>
        public byte[] Buffer => _buffer;

        public void Reset() => _length = 0;

        public byte[] ToArray()
        {
            var copy = new byte[_length];
            System.Buffer.BlockCopy(_buffer, 0, copy, 0, _length);
            return copy;
        }

        public ArraySegment<byte> AsSegment() => new ArraySegment<byte>(_buffer, 0, _length);

        public void WriteByte(byte value)
        {
            Ensure(1);
            _buffer[_length++] = value;
        }

        public void WriteSByte(sbyte value) => WriteByte(unchecked((byte)value));

        public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

        public void WriteUShort(ushort value)
        {
            Ensure(2);
            _buffer[_length++] = (byte)value;
            _buffer[_length++] = (byte)(value >> 8);
        }

        public void WriteShort(short value) => WriteUShort(unchecked((ushort)value));

        public void WriteUInt(uint value)
        {
            Ensure(4);
            _buffer[_length++] = (byte)value;
            _buffer[_length++] = (byte)(value >> 8);
            _buffer[_length++] = (byte)(value >> 16);
            _buffer[_length++] = (byte)(value >> 24);
        }

        public void WriteInt(int value) => WriteUInt(unchecked((uint)value));

        public void WriteULong(ulong value)
        {
            WriteUInt((uint)value);
            WriteUInt((uint)(value >> 32));
        }

        public void WriteLong(long value) => WriteULong(unchecked((ulong)value));

        public void WriteFloat(float value) => WriteInt(BitConverter.SingleToInt32Bits(value));

        public void WriteDouble(double value) => WriteLong(BitConverter.DoubleToInt64Bits(value));

        /// <summary>Unsigned LEB128: 1 byte below 128, at most 5 bytes.</summary>
        public void WriteVarUInt(uint value)
        {
            Ensure(5);
            while (value >= 0x80)
            {
                _buffer[_length++] = (byte)(value | 0x80);
                value >>= 7;
            }

            _buffer[_length++] = (byte)value;
        }

        /// <summary>Unsigned LEB128 for 64-bit values: at most 10 bytes.</summary>
        public void WriteVarULong(ulong value)
        {
            Ensure(10);
            while (value >= 0x80)
            {
                _buffer[_length++] = (byte)(value | 0x80);
                value >>= 7;
            }

            _buffer[_length++] = (byte)value;
        }

        /// <summary>Zigzag-encoded signed varint: small magnitudes of either sign stay short.</summary>
        public void WriteVarInt(int value) => WriteVarUInt(ZigZag.Encode(value));

        public void WriteVarLong(long value) => WriteVarULong(ZigZag.Encode(value));

        /// <summary>
        /// UTF-8 string with a varint byte length. Null is written as the empty string. Throws when the encoded
        /// form exceeds <paramref name="maxBytes"/>, because an oversized outgoing string is a programming error.
        /// </summary>
        public void WriteString(string value, int maxBytes = DefaultMaxStringBytes)
        {
            if (string.IsNullOrEmpty(value))
            {
                WriteVarUInt(0);
                return;
            }

            int count = Utf8.GetByteCount(value);
            if (count > maxBytes)
            {
                throw new ArgumentException("String encodes to " + count + " bytes; the limit is " + maxBytes + ".", nameof(value));
            }

            WriteVarUInt((uint)count);
            Ensure(count);
            _length += Utf8.GetBytes(value, 0, value.Length, _buffer, _length);
        }

        /// <summary>Raw bytes without a length prefix (fixed-size fields such as hashes and tokens).</summary>
        public void WriteBytes(byte[] source, int offset, int count)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (offset < 0 || count < 0 || offset + count > source.Length) throw new ArgumentOutOfRangeException(nameof(count));
            Ensure(count);
            System.Buffer.BlockCopy(source, offset, _buffer, _length, count);
            _length += count;
        }

        public void WriteBytes(byte[] source) => WriteBytes(source, 0, source?.Length ?? 0);

        /// <summary>Bytes with a varint length prefix.</summary>
        public void WriteBlob(byte[] source, int offset, int count)
        {
            WriteVarUInt((uint)count);
            WriteBytes(source, offset, count);
        }

        private void Ensure(int extra)
        {
            int needed = _length + extra;
            if (needed <= _buffer.Length)
            {
                return;
            }

            int size = _buffer.Length * 2;
            while (size < needed)
            {
                size *= 2;
            }

            Array.Resize(ref _buffer, size);
        }
    }

    /// <summary>Zigzag mapping between signed and unsigned integers (0, -1, 1, -2, ... -> 0, 1, 2, 3, ...).</summary>
    public static class ZigZag
    {
        public static uint Encode(int value) => unchecked((uint)((value << 1) ^ (value >> 31)));

        public static int Decode(uint value) => unchecked((int)(value >> 1) ^ -(int)(value & 1));

        public static ulong Encode(long value) => unchecked((ulong)((value << 1) ^ (value >> 63)));

        public static long Decode(ulong value) => unchecked((long)(value >> 1) ^ -(long)(value & 1));
    }
}
