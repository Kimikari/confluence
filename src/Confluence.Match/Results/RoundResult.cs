using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace Confluence.Match
{
    /// <summary>
    /// One player's line of the results screen (spec L361): Resolve change, income received, next baseline income,
    /// winner bonus, and how the pass ended. Ties and byes are explicit outcomes.
    /// </summary>
    public sealed class RoundResultEntry
    {
        internal RoundResultEntry(int player, int opponent, RoundOutcome outcome)
        {
            Player = player;
            Opponent = opponent;
            Outcome = outcome;
        }

        public int Player { get; }

        /// <summary>Opponent, or NoPlayer for a bye or walkover.</summary>
        public int Opponent { get; }

        public RoundOutcome Outcome { get; internal set; }

        /// <summary>Conclave: Resolve before and after this result (L36). Equal for byes and bracket rounds.</summary>
        public int ResolveBefore { get; internal set; }

        public int ResolveAfter { get; internal set; }

        public int ResolveDelta => ResolveAfter - ResolveBefore;

        /// <summary>Baseline income credited at this round's shop (0 on replay passes, which have no shop).</summary>
        public int BaselineIncome { get; internal set; }

        /// <summary>Winner bonus credited at this round's shop.</summary>
        public int WinnerBonus { get; internal set; }

        /// <summary>Baseline income of the next shop, 0 when the player has no next shop.</summary>
        public int NextBaselineIncome { get; internal set; }

        /// <summary>Winner bonus earned for the next shop (L193): only for a combat win outside a draw replay.</summary>
        public int NextWinnerBonus { get; internal set; }

        /// <summary>Drawn and scheduled to replay (bracket L38, Duel L65).</summary>
        public bool ReplayPending { get; internal set; }

        public bool Eliminated { get; internal set; }

        /// <summary>Final placement when it is decided by this result, otherwise null.</summary>
        public MatchPlacement Placement { get; internal set; }

        /// <summary>Duel only: the player's score in half wins after this result.</summary>
        public int DuelHalfWins { get; internal set; }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append('p').Append(Player).Append(' ').Append(Outcome);
            if (Opponent != MatchRules.NoPlayer) sb.Append(" vs p").Append(Opponent);
            sb.Append(" resolve ").Append(ResolveBefore).Append("->").Append(ResolveAfter);
            sb.Append(" income ").Append(BaselineIncome).Append('+').Append(WinnerBonus);
            sb.Append(" next ").Append(NextBaselineIncome).Append('+').Append(NextWinnerBonus);
            if (ReplayPending) sb.Append(" replay");
            if (Eliminated) sb.Append(" eliminated");
            return sb.ToString();
        }
    }

    /// <summary>Results of one pass of a round, produced when its last duel resolves.</summary>
    public sealed class RoundResult
    {
        internal RoundResult(int tick, int round, int bracketRound, int attempt, PassKind passKind, List<RoundResultEntry> entries)
        {
            Tick = tick;
            Round = round;
            BracketRound = bracketRound;
            Attempt = attempt;
            PassKind = passKind;
            entries.Sort((x, y) => x.Player.CompareTo(y.Player));
            Entries = new ReadOnlyCollection<RoundResultEntry>(entries);
        }

        public int Tick { get; }
        public int Round { get; }
        public int BracketRound { get; }
        public int Attempt { get; }
        public PassKind PassKind { get; }

        /// <summary>False while drawn fights still have to replay.</summary>
        public bool RoundComplete { get; internal set; }

        /// <summary>Ordered by player id.</summary>
        public IReadOnlyList<RoundResultEntry> Entries { get; }

        public RoundResultEntry For(int player)
        {
            foreach (var e in Entries)
                if (e.Player == player) return e;
            return null;
        }

        public override string ToString() => "round " + Round + (BracketRound > 0 ? " bracket " + BracketRound : "") +
            (Attempt > 0 ? " attempt " + Attempt : "") + ": " + string.Join("; ", Entries);
    }
}
