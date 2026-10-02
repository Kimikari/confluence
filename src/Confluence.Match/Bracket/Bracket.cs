using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Confluence.Core.Rules;

namespace Confluence.Match
{
    /// <summary>A contender's bracket seeding inputs (spec L38).</summary>
    public sealed class BracketSeedEntry
    {
        public BracketSeedEntry(int player, int resolve, int combatWins, int regularByes, ulong lobbySeed)
        {
            Player = player;
            Resolve = resolve;
            CombatWins = combatWins;
            RegularByes = regularByes;
            LobbySeed = lobbySeed;
        }

        public int Player { get; }
        public int Resolve { get; }
        public int CombatWins { get; }
        public int RegularByes { get; }

        /// <summary>Recorded at lobby creation; breaks exact ties.</summary>
        public ulong LobbySeed { get; }
    }

    /// <summary>One bracket match between two slots. Either side may be empty (a bye or walkover).</summary>
    public readonly struct BracketMatch
    {
        public BracketMatch(int index, int higherSeedPlayer, int lowerSeedPlayer)
        {
            Index = index;
            HigherSeedPlayer = higherSeedPlayer;
            LowerSeedPlayer = lowerSeedPlayer;
        }

        /// <summary>Position in the current bracket round, top to bottom.</summary>
        public int Index { get; }

        public int HigherSeedPlayer { get; }
        public int LowerSeedPlayer { get; }

        public bool IsContested => HigherSeedPlayer != MatchRules.NoPlayer && LowerSeedPlayer != MatchRules.NoPlayer;

        /// <summary>The only present player of an uncontested match, or NoPlayer when both slots are empty.</summary>
        public int UncontestedPlayer => HigherSeedPlayer != MatchRules.NoPlayer ? HigherSeedPlayer : LowerSeedPlayer;
    }

    /// <summary>
    /// Seeded single-elimination bracket after round 12 (spec L38). Seeds order by Resolve, then combat wins, then fewer
    /// regular byes, then the recorded lobby seed. The bracket grows to the next power of two, and the highest seeds take
    /// the empty slots as byes. Standard seeding places 1 and 2 in opposite halves. A double-drawn match advances nobody
    /// (both players share a finish), so its next opponent advances by walkover.
    /// </summary>
    public sealed class Bracket
    {
        private int[] _slots;

        public Bracket(IReadOnlyList<int> playersBySeed)
        {
            if (playersBySeed == null) throw new ArgumentNullException(nameof(playersBySeed));
            if (playersBySeed.Count < 2) throw new ArgumentException("A bracket needs at least two contenders.", nameof(playersBySeed));
            Size = SizeFor(playersBySeed.Count);
            TotalRounds = Log2(Size);
            if (TotalRounds > GlobalRules.Match.MaxBracketRounds)
            {
                throw new ArgumentException("At most " + GlobalRules.Match.MaxBracketRounds + " bracket rounds are allowed (L38).", nameof(playersBySeed));
            }

            Seeds = new ReadOnlyCollection<int>(new List<int>(playersBySeed));
            int[] order = SlotOrder(Size);
            _slots = new int[Size];
            for (int i = 0; i < Size; i++)
            {
                int seed = order[i];
                _slots[i] = seed <= playersBySeed.Count ? playersBySeed[seed - 1] : MatchRules.NoPlayer;
            }
        }

        /// <summary>Players in seed order (seed 1 first).</summary>
        public IReadOnlyList<int> Seeds { get; }

        /// <summary>Power-of-two slot count.</summary>
        public int Size { get; }

        public int TotalRounds { get; }

        /// <summary>Bracket rounds completed so far.</summary>
        public int RoundsPlayed { get; private set; }

        /// <summary>1-based bracket round to play next.</summary>
        public int CurrentRound => RoundsPlayed + 1;

        /// <summary>Entrants of the current round in slot order; empty slots are NoPlayer.</summary>
        public IReadOnlyList<int> Slots => Array.AsReadOnly(_slots);

        public int RemainingPlayers
        {
            get
            {
                int count = 0;
                foreach (int p in _slots)
                    if (p != MatchRules.NoPlayer) count++;
                return count;
            }
        }

        /// <summary>True once at most one entrant remains (champion decided, or a shared finish left nobody).</summary>
        public bool IsComplete => RemainingPlayers <= 1 || _slots.Length < 2;

        /// <summary>Seed number (1-based) of a player.</summary>
        public int SeedOf(int player)
        {
            int index = -1;
            for (int i = 0; i < Seeds.Count; i++)
                if (Seeds[i] == player) index = i;
            if (index < 0) throw new ArgumentException("Player " + player + " is not in the bracket.", nameof(player));
            return index + 1;
        }

        /// <summary>Matches of the current round: adjacent slot pairs, the higher seed first.</summary>
        public IReadOnlyList<BracketMatch> CurrentMatches()
        {
            if (IsComplete) throw new InvalidOperationException("The bracket is complete.");
            var matches = new List<BracketMatch>(_slots.Length / 2);
            for (int i = 0; i < _slots.Length; i += 2)
            {
                int x = _slots[i], y = _slots[i + 1];
                bool xHigher = y == MatchRules.NoPlayer || (x != MatchRules.NoPlayer && SeedOf(x) < SeedOf(y));
                matches.Add(xHigher ? new BracketMatch(i / 2, x, y) : new BracketMatch(i / 2, y, x));
            }

            return matches;
        }

        /// <summary>
        /// Completes the current round. <paramref name="advancing"/> holds, per match index, the advancing player or NoPlayer
        /// when nobody advances (an empty match or a double-drawn shared finish).
        /// </summary>
        public void CompleteRound(IReadOnlyList<int> advancing)
        {
            if (advancing == null) throw new ArgumentNullException(nameof(advancing));
            var matches = CurrentMatches();
            if (advancing.Count != matches.Count) throw new ArgumentException("One entry per bracket match is required.", nameof(advancing));
            var next = new int[matches.Count];
            for (int i = 0; i < matches.Count; i++)
            {
                int p = advancing[i];
                if (p != MatchRules.NoPlayer && p != matches[i].HigherSeedPlayer && p != matches[i].LowerSeedPlayer)
                {
                    throw new ArgumentException("Player " + p + " was not in bracket match " + i + ".", nameof(advancing));
                }

                next[i] = p;
            }

            _slots = next;
            RoundsPlayed++;
        }

        /// <summary>Order of contenders for seeding: Resolve, combat wins, fewer byes, lobby seed (L38).</summary>
        public static IReadOnlyList<int> SeedOrder(IReadOnlyList<BracketSeedEntry> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            var sorted = new List<BracketSeedEntry>(entries);
            sorted.Sort(CompareSeeds);
            var players = new List<int>(sorted.Count);
            foreach (var e in sorted) players.Add(e.Player);
            return players;
        }

        /// <summary>Negative when x seeds above y.</summary>
        public static int CompareSeeds(BracketSeedEntry x, BracketSeedEntry y)
        {
            int c = y.Resolve.CompareTo(x.Resolve);
            if (c != 0) return c;
            c = y.CombatWins.CompareTo(x.CombatWins);
            if (c != 0) return c;
            c = x.RegularByes.CompareTo(y.RegularByes);
            if (c != 0) return c;
            c = x.LobbySeed.CompareTo(y.LobbySeed);
            return c != 0 ? c : x.Player.CompareTo(y.Player);
        }

        /// <summary>Smallest power of two holding <paramref name="entrants"/>.</summary>
        public static int SizeFor(int entrants)
        {
            if (entrants < 1) throw new ArgumentOutOfRangeException(nameof(entrants));
            int size = 1;
            while (size < entrants) size *= 2;
            return Math.Max(2, size);
        }

        /// <summary>
        /// Seed numbers in slot order for a standard bracket: 2 -> [1,2]; 4 -> [1,4,2,3]; 8 -> [1,8,4,5,2,7,3,6].
        /// Seed s meets seed size+1-s in the first round, and the top two seeds can only meet in the final.
        /// </summary>
        public static int[] SlotOrder(int size)
        {
            if (size < 1 || (size & (size - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(size), size, "Bracket sizes are powers of two.");
            var order = new List<int> { 1 };
            while (order.Count < size)
            {
                int doubled = order.Count * 2;
                var next = new List<int>(doubled);
                foreach (int s in order)
                {
                    next.Add(s);
                    next.Add(doubled + 1 - s);
                }

                order = next;
            }

            return order.ToArray();
        }

        private static int Log2(int powerOfTwo)
        {
            int rounds = 0;
            while ((1 << rounds) < powerOfTwo) rounds++;
            return rounds;
        }
    }
}
