namespace Confluence.Match
{
    /// <summary>Match-level phase (spec L44-L49). Every active player shares the same phase.</summary>
    public enum MatchPhase
    {
        Lobby,
        /// <summary>Opponent, arena preview, Resolve, and the opponent's completed previous-round loadout (L44).</summary>
        Reveal,
        /// <summary>Equipment shop and spell selection (L45).</summary>
        Shop,
        /// <summary>Free five-card augment draft on draft rounds only (L45, L216-L225).</summary>
        Draft,
        /// <summary>Protected spawn alcoves; duels have started but the ward prevents damage and casting (L46).</summary>
        Alcove,
        /// <summary>Open combat, at most 90 s (L47).</summary>
        Combat,
        /// <summary>8 s results period after the last duel of the pass resolves (L49).</summary>
        Results,
        Finished,
    }

    /// <summary>Why a set of fights is being played.</summary>
    public enum PassKind
    {
        /// <summary>The round's scheduled fights, preceded by a shop.</summary>
        Original,

        /// <summary>
        /// A drawn fight replayed with the same loadouts, full resources, no shop, and a new arena seed
        /// (bracket L38, Duel L65). Replay wins grant no winner bonus (L193).
        /// </summary>
        DrawReplay,

        /// <summary>
        /// A voided pass played again after a server failure (L427): same pairings, sides, and loadouts, a new arena
        /// seed, no shop, and no replay counted. Its results stand in for the voided ones.
        /// </summary>
        VoidRerun,
    }

    /// <summary>A player's result in one pass of a round.</summary>
    public enum RoundOutcome
    {
        Win,
        Loss,
        Draw,
        /// <summary>Regular odd-count bye (L61) or a bracket bye or walkover (L38).</summary>
        Bye,
        /// <summary>Duel: a round drawn three times scores half a win each (L65).</summary>
        HalfWin,
    }

    /// <summary>Recorded preset of a Conclave lobby (spec L66-L67, L71).</summary>
    public enum ConclavePreset
    {
        Ranked,
        Unranked,
        Custom,
    }
}
