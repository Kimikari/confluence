using System;

namespace Confluence.Casting
{
    /// <summary>
    /// Stable identity of one cast attempt: the player's index and a per-player sequence that starts at 1 each duel
    /// and increments at every preparation. Server and predicting client derive the same ids from the same inputs.
    /// Commitment keeps the id; recasts, splits, and delayed portions reuse it (spec L94, L480, L508).
    /// </summary>
    public readonly struct CastEventId : IEquatable<CastEventId>, IComparable<CastEventId>
    {
        public CastEventId(int player, int sequence)
        {
            Player = player;
            Sequence = sequence;
        }

        public int Player { get; }
        public int Sequence { get; }

        public bool IsValid => Sequence > 0;

        public static CastEventId None => default;

        /// <summary>Packed form for logs and network messages.</summary>
        public long Packed => ((long)Player << 32) | (uint)Sequence;

        public bool Equals(CastEventId other) => Player == other.Player && Sequence == other.Sequence;

        public override bool Equals(object obj) => obj is CastEventId other && Equals(other);

        public override int GetHashCode() => Player * 7919 + Sequence;

        public int CompareTo(CastEventId other) => Player != other.Player ? Player.CompareTo(other.Player) : Sequence.CompareTo(other.Sequence);

        public static bool operator ==(CastEventId a, CastEventId b) => a.Equals(b);

        public static bool operator !=(CastEventId a, CastEventId b) => !a.Equals(b);

        public override string ToString() => IsValid ? "P" + Player + "#" + Sequence : "none";
    }
}
