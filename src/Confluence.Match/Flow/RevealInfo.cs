namespace Confluence.Match
{
    /// <summary>
    /// What one player sees at reveal and through the shop (spec L44): the next opponent, the generated arena, both
    /// players' remaining Resolve, and the opponent's completed previous-round loadout. Live purchases stay hidden
    /// until the alcove, where final loadouts become visible (L46).
    /// </summary>
    public sealed class RevealInfo
    {
        internal RevealInfo(int player, int round, int bracketRound, int attempt, PassKind passKind, int opponent, int side,
            ArenaPreview arena, int ownResolve, int opponentResolve, PublicLoadout opponentLoadout)
        {
            Player = player;
            Round = round;
            BracketRound = bracketRound;
            Attempt = attempt;
            PassKind = passKind;
            Opponent = opponent;
            Side = side;
            Arena = arena;
            OwnResolve = ownResolve;
            OpponentResolve = opponentResolve;
            OpponentLoadout = opponentLoadout;
        }

        public int Player { get; }
        public int Round { get; }
        public int BracketRound { get; }
        public int Attempt { get; }
        public PassKind PassKind { get; }

        /// <summary>Opponent, or NoPlayer for a bye.</summary>
        public int Opponent { get; }

        public bool IsBye => Opponent == MatchRules.NoPlayer;

        /// <summary>Spawn side 0 or 1, or -1 for a bye.</summary>
        public int Side { get; }

        /// <summary>The pass's shared arena (bye players may still inspect it).</summary>
        public ArenaPreview Arena { get; }

        public int OwnResolve { get; }
        public int OpponentResolve { get; }

        /// <summary>
        /// Original passes: the opponent's completed previous-round loadout (null in their first round). Replays and
        /// reruns: the opponent's final loadout for this round, already public.
        /// </summary>
        public PublicLoadout OpponentLoadout { get; }
    }
}
