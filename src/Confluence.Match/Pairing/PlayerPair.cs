using System;

namespace Confluence.Match
{
    /// <summary>Two players facing each other, without spawn sides. Stored with the lower id first.</summary>
    public readonly struct PlayerPair : IEquatable<PlayerPair>
    {
        public PlayerPair(int first, int second)
        {
            if (first == second) throw new ArgumentException("A pair needs two different players.");
            A = Math.Min(first, second);
            B = Math.Max(first, second);
        }

        /// <summary>Lower player id.</summary>
        public int A { get; }

        /// <summary>Higher player id.</summary>
        public int B { get; }

        public bool Contains(int player) => player == A || player == B;

        public int Other(int player)
        {
            if (player == A) return B;
            if (player == B) return A;
            throw new ArgumentException("Player " + player + " is not in pair " + this + ".", nameof(player));
        }

        public bool Equals(PlayerPair other) => A == other.A && B == other.B;
        public override bool Equals(object obj) => obj is PlayerPair other && Equals(other);
        public override int GetHashCode() => unchecked(A * 397 ^ B);
        public override string ToString() => A + "v" + B;
    }

    /// <summary>A pair with spawn sides assigned (spec L59).</summary>
    public readonly struct SeatedPair : IEquatable<SeatedPair>
    {
        public SeatedPair(int side0Player, int side1Player)
        {
            if (side0Player == side1Player) throw new ArgumentException("A pair needs two different players.");
            Side0Player = side0Player;
            Side1Player = side1Player;
        }

        public int Side0Player { get; }
        public int Side1Player { get; }

        public PlayerPair Pair => new PlayerPair(Side0Player, Side1Player);

        public bool Contains(int player) => player == Side0Player || player == Side1Player;

        public int SideOf(int player)
        {
            if (player == Side0Player) return 0;
            if (player == Side1Player) return 1;
            throw new ArgumentException("Player " + player + " is not seated in " + this + ".", nameof(player));
        }

        public int Other(int player) => SideOf(player) == 0 ? Side1Player : Side0Player;

        public bool Equals(SeatedPair other) => Side0Player == other.Side0Player && Side1Player == other.Side1Player;
        public override bool Equals(object obj) => obj is SeatedPair other && Equals(other);
        public override int GetHashCode() => unchecked(Side0Player * 397 ^ Side1Player);
        public override string ToString() => Side0Player + ">" + Side1Player;
    }
}
