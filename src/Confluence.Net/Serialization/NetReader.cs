using System;
using System.Text;

namespace Confluence.Net.Serialization
{
    /// <summary>Why a read from untrusted bytes failed.</summary>
    public enum NetReadError : byte
    {
        EndOfData,
        VarIntOverflow,
        StringTooLong,
        InvalidUtf8,
        CountOutOfRange,
        InvalidValue,
        TrailingBytes,
    }

    /// <summary>
    /// Thrown by <see cref="NetReader"/> when untrusted input is truncated, oversized, or out of range. The
    /// transports catch it, count the packet as malformed, and drop it; it never escapes a Poll call.
    /// </summary>
    public sealed class NetReadException : Exception
    {
        public NetReadException(NetReadError error, string detail)
            : base(error + ": " + detail)
        {
            Error = error;
        }

        public NetReadError Error { get; }
    }

    /// <summary>
    /// Bounds-checked little-endian reader over a byte range, the counterpart of <see cref="NetWriter"/>.
    /// Every read validates against the remaining length, every count and string length has an explicit cap,
    /// and floats must be finite unless read with <see cref="ReadFloatRaw"/>. Violations throw
    /// <see cref="NetReadException"/>, so a handler never acts on a partially decoded message.
    /// </summary>
    public sealed class NetReader
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private byte[] _data;
        private int _start;
        private int _end;
        private int _pos;

        public NetReader()
        {
            _data = new byte[0];
        }

        public NetReader(byte[] data)
            : this()
        {
            Reset(data, 0, data?.Length ?? 0);
        }

        public NetReader(byte[] data, int offset, int count)
            : this()
        {
            Reset(data, offset, count);
        }

        public void Reset(byte[] data, int offset, int count)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (offset < 0 || count < 0 || offset + count > data.Length) throw new ArgumentOutOfRangeException(nameof(count));
            _data = data;
            _start = offset;
            _end = offset + count;
            _pos = offset;
        }

        /// <summary>Bytes consumed since the start of the range.</summary>
        public int Position => _pos - _start;

        public int Length => _end - _start;

        public int Remaining => _end - _pos;

        public bool IsAtEnd => _pos >= _end;

        /// <summary>Copy of the unread bytes (diagnostics and cached forwarding).</summary>
        public byte[] RemainingToArray()
        {
            var copy = new byte[Remaining];
            Buffer.BlockCopy(_data, _pos, copy, 0, copy.Length);
            return copy;
        }

        /// <summary>Rejects the message when bytes are left over after a complete decode.</summary>
        public void ExpectEnd()
        {
            if (_pos != _end)
            {
                throw new NetReadException(NetReadError.TrailingBytes, Remaining + " unread byte(s)");
            }
        }

        public byte ReadByte()
        {
            Need(1);
            return _data[_pos++];
        }

        public sbyte ReadSByte() => unchecked((sbyte)ReadByte());

        /// <summary>Strict boolean: only 0 and 1 are valid encodings.</summary>
        public bool ReadBool()
        {
            byte b = ReadByte();
            if (b > 1)
            {
                throw new NetReadException(NetReadError.InvalidValue, "boolean byte " + b);
            }

            return b == 1;
        }

        public ushort ReadUShort()
        {
            Need(2);
            int v = _data[_pos] | (_data[_pos + 1] << 8);
            _pos += 2;
            return (ushort)v;
        }

        public short ReadShort() => unchecked((short)ReadUShort());

        public uint ReadUInt()
        {
            Need(4);
            uint v = (uint)_data[_pos]
                | ((uint)_data[_pos + 1] << 8)
                | ((uint)_data[_pos + 2] << 16)
                | ((uint)_data[_pos + 3] << 24);
            _pos += 4;
            return v;
        }

        public int ReadInt() => unchecked((int)ReadUInt());

        public ulong ReadULong()
        {
            ulong low = ReadUInt();
            ulong high = ReadUInt();
            return low | (high << 32);
        }

        public long ReadLong() => unchecked((long)ReadULong());

        /// <summary>Finite float; NaN and infinities are rejected as impossible values (spec L429).</summary>
        public float ReadFloat()
        {
            float v = ReadFloatRaw();
            if (float.IsNaN(v) || float.IsInfinity(v))
            {
                throw new NetReadException(NetReadError.InvalidValue, "non-finite float");
            }

            return v;
        }

        /// <summary>Any float bit pattern, including NaN and infinities.</summary>
        public float ReadFloatRaw() => BitConverter.Int32BitsToSingle(ReadInt());

        /// <summary>Finite double; NaN and infinities are rejected.</summary>
        public double ReadDouble()
        {
            double v = BitConverter.Int64BitsToDouble(ReadLong());
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                throw new NetReadException(NetReadError.InvalidValue, "non-finite double");
            }

            return v;
        }

        public uint ReadVarUInt()
        {
            uint result = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                byte b = ReadByte();
                if (shift == 28 && b > 0x0F)
                {
                    throw new NetReadException(NetReadError.VarIntOverflow, "varint exceeds 32 bits");
                }

                result |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    return result;
                }
            }

            throw new NetReadException(NetReadError.VarIntOverflow, "varint longer than 5 bytes");
        }

        public ulong ReadVarULong()
        {
            ulong result = 0;
            for (int shift = 0; shift < 70; shift += 7)
            {
                byte b = ReadByte();
                if (shift == 63 && b > 0x01)
                {
                    throw new NetReadException(NetReadError.VarIntOverflow, "varint exceeds 64 bits");
                }

                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    return result;
                }
            }

            throw new NetReadException(NetReadError.VarIntOverflow, "varint longer than 10 bytes");
        }

        public int ReadVarInt() => ZigZag.Decode(ReadVarUInt());

        public long ReadVarLong() => ZigZag.Decode(ReadVarULong());

        /// <summary>Varint in [min, max]; anything else is an out-of-range value.</summary>
        public int ReadVarIntInRange(int min, int max)
        {
            int v = ReadVarInt();
            if (v < min || v > max)
            {
                throw new NetReadException(NetReadError.InvalidValue, "value " + v + " outside [" + min + ", " + max + "]");
            }

            return v;
        }

        /// <summary>Byte in [min, max] (enum discriminants).</summary>
        public byte ReadByteInRange(byte min, byte max)
        {
            byte v = ReadByte();
            if (v < min || v > max)
            {
                throw new NetReadException(NetReadError.InvalidValue, "byte " + v + " outside [" + min + ", " + max + "]");
            }

            return v;
        }

        /// <summary>
        /// Element count prefix, capped by <paramref name="max"/> and by the bytes left (each element needs at
        /// least <paramref name="minBytesPerElement"/>), so a forged count can never trigger a huge allocation.
        /// </summary>
        public int ReadCount(int max, int minBytesPerElement = 1)
        {
            uint count = ReadVarUInt();
            if (count > (uint)max)
            {
                throw new NetReadException(NetReadError.CountOutOfRange, "count " + count + " exceeds " + max);
            }

            if (minBytesPerElement > 0 && (long)count * minBytesPerElement > Remaining)
            {
                throw new NetReadException(NetReadError.CountOutOfRange, "count " + count + " exceeds the remaining " + Remaining + " bytes");
            }

            return (int)count;
        }

        /// <summary>Strict UTF-8 string of at most <paramref name="maxBytes"/> encoded bytes.</summary>
        public string ReadString(int maxBytes = NetWriter.DefaultMaxStringBytes)
        {
            uint count = ReadVarUInt();
            if (count > (uint)maxBytes)
            {
                throw new NetReadException(NetReadError.StringTooLong, count + " bytes exceeds " + maxBytes);
            }

            int n = (int)count;
            Need(n);
            try
            {
                string s = StrictUtf8.GetString(_data, _pos, n);
                _pos += n;
                return s;
            }
            catch (ArgumentException)
            {
                throw new NetReadException(NetReadError.InvalidUtf8, "string is not valid UTF-8");
            }
        }

        /// <summary>Copies exactly <paramref name="count"/> raw bytes into <paramref name="destination"/>.</summary>
        public void ReadBytes(byte[] destination, int offset, int count)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            Need(count);
            Buffer.BlockCopy(_data, _pos, destination, offset, count);
            _pos += count;
        }

        public byte[] ReadBytes(int count)
        {
            if (count < 0)
            {
                throw new NetReadException(NetReadError.CountOutOfRange, "negative byte count");
            }

            var result = new byte[count];
            ReadBytes(result, 0, count);
            return result;
        }

        /// <summary>Length-prefixed bytes of at most <paramref name="maxBytes"/>.</summary>
        public byte[] ReadBlob(int maxBytes)
        {
            return ReadBytes(ReadCount(maxBytes, 1));
        }

        public void Skip(int count)
        {
            Need(count);
            _pos += count;
        }

        private void Need(int count)
        {
            if (count < 0 || _end - _pos < count)
            {
                throw new NetReadException(NetReadError.EndOfData, "needed " + count + " byte(s), " + Remaining + " left");
            }
        }
    }
}
