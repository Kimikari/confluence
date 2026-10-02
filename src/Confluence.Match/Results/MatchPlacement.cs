using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using Confluence.Core.Json;
using Confluence.Core.Rules;

namespace Confluence.Match
{
    /// <summary>
    /// Final position of one player (spec L40, L387, L433). Players eliminated in the same round occupy a block of
    /// positions and each receives the block's mean, never an arbitrary statistic.
    /// </summary>
    public sealed class MatchPlacement
    {
        public MatchPlacement(int player, int firstPosition, int lastPosition, int round, int bracketRound)
        {
            if (firstPosition < 1 || lastPosition < firstPosition) throw new ArgumentOutOfRangeException(nameof(firstPosition));
            Player = player;
            FirstPosition = firstPosition;
            LastPosition = lastPosition;
            Round = round;
            BracketRound = bracketRound;
        }

        public int Player { get; }

        /// <summary>Best position of the tied block.</summary>
        public int FirstPosition { get; }

        /// <summary>Worst position of the tied block.</summary>
        public int LastPosition { get; }

        /// <summary>Mean occupied position used for rewards and rating (L40, L387). Halves are exact in binary.</summary>
        public double Position => (FirstPosition + LastPosition) / 2.0;

        public bool IsShared => LastPosition > FirstPosition;

        /// <summary>Round in which the player was eliminated or the match ended for them.</summary>
        public int Round { get; }

        public int BracketRound { get; }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append('p').Append(Player).Append(" pos=").Append(JsonWriter.FormatNumber(Position));
            if (IsShared) sb.Append(" (").Append(FirstPosition).Append('-').Append(LastPosition).Append(')');
            sb.Append(" r").Append(Round);
            if (BracketRound > 0) sb.Append(" b").Append(BracketRound);
            return sb.ToString();
        }
    }

    /// <summary>Final result of a match.</summary>
    public sealed class MatchResult
    {
        public MatchResult(MatchMode mode, IEnumerable<MatchPlacement> placements, string settingsFingerprint, bool countsForRating,
            int duelHalfWins0 = 0, int duelHalfWins1 = 0)
        {
            var list = new List<MatchPlacement>(placements);
            list.Sort((x, y) =>
            {
                int c = x.FirstPosition.CompareTo(y.FirstPosition);
                return c != 0 ? c : x.Player.CompareTo(y.Player);
            });
            Mode = mode;
            Placements = new ReadOnlyCollection<MatchPlacement>(list);
            SettingsFingerprint = settingsFingerprint;
            CountsForRating = countsForRating;
            DuelHalfWins0 = duelHalfWins0;
            DuelHalfWins1 = duelHalfWins1;
            Champion = list.Count > 0 && list[0].FirstPosition == 1 && !list[0].IsShared ? list[0].Player : MatchRules.NoPlayer;
        }

        public MatchMode Mode { get; }

        /// <summary>Ordered by position, then player id.</summary>
        public IReadOnlyList<MatchPlacement> Placements { get; }

        /// <summary>Sole first place, or NoPlayer for a shared finish or a recorded Duel draw (L38, L65).</summary>
        public int Champion { get; }

        /// <summary>True when first place is shared: a twice-drawn bracket final, a simultaneous last elimination, or a Duel draw.</summary>
        public bool SharedFirstPlace => Champion == MatchRules.NoPlayer;

        public string SettingsFingerprint { get; }

        /// <summary>Only standard ranked rules produce rating results (L67, L71).</summary>
        public bool CountsForRating { get; }

        /// <summary>Duel only: player 0's final score in half wins.</summary>
        public int DuelHalfWins0 { get; }

        public int DuelHalfWins1 { get; }

        public MatchPlacement For(int player)
        {
            foreach (var p in Placements)
                if (p.Player == player) return p;
            throw new ArgumentException("No placement for player " + player + ".", nameof(player));
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(Mode).Append(" champion=").Append(Champion);
            if (Mode == MatchMode.Duel)
            {
                sb.Append(" score=").Append(JsonWriter.FormatNumber(DuelHalfWins0 / 2.0)).Append('-').Append(JsonWriter.FormatNumber(DuelHalfWins1 / 2.0));
            }

            sb.Append(" [");
            for (int i = 0; i < Placements.Count; i++) sb.Append(i == 0 ? "" : "; ").Append(Placements[i]);
            return sb.Append(']').ToString();
        }
    }
}
