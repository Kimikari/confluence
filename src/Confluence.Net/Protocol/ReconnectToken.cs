using System;
using System.Security.Cryptography;
using Confluence.Core.Random;
using Confluence.Net.Serialization;

namespace Confluence.Net.Protocol
{
    /// <summary>
    /// 128-bit secret that lets a disconnected client resume its player identity within the reconnect window
    /// (spec L55). The server issues it in the welcome; the all-zero value means "no token".
    /// </summary>
    public struct ReconnectToken : IEquatable<ReconnectToken>
    {
        public const int Size = 16;

        public ReconnectToken(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public static ReconnectToken None => default;

        public ulong High { get; }

        public ulong Low { get; }

        public bool IsNone => High == 0 && Low == 0;

        public void Write(NetWriter writer)
        {
            writer.WriteULong(High);
            writer.WriteULong(Low);
        }

        public static ReconnectToken Read(NetReader reader)
        {
            ulong high = reader.ReadULong();
            ulong low = reader.ReadULong();
            return new ReconnectToken(high, low);
        }

        public bool Equals(ReconnectToken other) => High == other.High && Low == other.Low;

        public override bool Equals(object obj) => obj is ReconnectToken other && Equals(other);

        public override int GetHashCode() => unchecked((int)(High ^ (High >> 32) ^ Low ^ (Low >> 32)));

        public static bool operator ==(ReconnectToken a, ReconnectToken b) => a.Equals(b);

        public static bool operator !=(ReconnectToken a, ReconnectToken b) => !a.Equals(b);

        /// <summary>Short prefix for logs; never log the whole secret.</summary>
        public override string ToString() => IsNone ? "none" : (High >> 40).ToString("x6") + "...";
    }

    /// <summary>Source of reconnect tokens.</summary>
    public interface ITokenSource
    {
        /// <summary>A fresh token that is never <see cref="ReconnectToken.None"/>.</summary>
        ReconnectToken Next();
    }

    /// <summary>
    /// Cryptographically random tokens. Tokens are credentials, not gameplay randomness: they must be
    /// unpredictable, and they never influence a rule outcome, so they intentionally do not come from the
    /// seeded <see cref="Rng"/> streams.
    /// </summary>
    public sealed class CryptoTokenSource : ITokenSource
    {
        private readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();
        private readonly byte[] _buffer = new byte[ReconnectToken.Size];

        public ReconnectToken Next()
        {
            while (true)
            {
                _rng.GetBytes(_buffer);
                var token = new ReconnectToken(BitConverter.ToUInt64(_buffer, 0), BitConverter.ToUInt64(_buffer, 8));
                if (!token.IsNone)
                {
                    return token;
                }
            }
        }
    }

    /// <summary>Deterministic tokens from a seeded <see cref="Rng"/>, for tests and replays only.</summary>
    public sealed class SeededTokenSource : ITokenSource
    {
        private readonly Rng _rng;

        public SeededTokenSource(ulong seed)
        {
            _rng = new Rng(seed);
        }

        public ReconnectToken Next()
        {
            while (true)
            {
                var token = new ReconnectToken(_rng.NextULong(), _rng.NextULong());
                if (!token.IsNone)
                {
                    return token;
                }
            }
        }
    }
}
