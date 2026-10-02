namespace Confluence.Casting.Grammars
{
    /// <summary>
    /// One player's casting language. A grammar instance lives for the whole duel so it can track state that spans
    /// casts (armed rewards, alternation patterns, anchors). It runs identically on the authoritative server, from
    /// received input frames, and on the client for prediction and HUD (spec L419, L425).
    /// <para>
    /// Call order per tick, from <see cref="CastMachine"/>: <see cref="Observe"/> always; while idle,
    /// <see cref="WantsToBegin"/> and then <see cref="Begin"/>; during preparation (including the opening tick),
    /// <see cref="Advance"/>; and <see cref="End"/> once when the attempt commits or is cancelled.
    /// </para>
    /// </summary>
    public interface IGrammar
    {
        string WizardId { get; }
        GrammarFamily Family { get; }
        InputRoute Route { get; }

        /// <summary>Background tracking every tick in every phase (combat events, movement, expiries).</summary>
        void Observe(in CastInputFrame frame);

        /// <summary>Whether this frame's input opens a preparation for <paramref name="spell"/>. Must not change state.</summary>
        bool WantsToBegin(in CastInputFrame frame, CastableSpell spell);

        /// <summary>Opens a preparation on the current tick.</summary>
        void Begin(in CastInputFrame frame, in GrammarContext context);

        /// <summary>Validates this tick's input and decides whether preparation continues, commits, or is abandoned.</summary>
        GrammarStep Advance(in CastInputFrame frame, in GrammarContext context);

        /// <summary>The attempt ended: committed with <paramref name="mastery"/>, or cancelled before commitment.</summary>
        void End(bool committed, in MasteryResult mastery);

        /// <summary>HUD state for the open preparation.</summary>
        GrammarUiState Ui { get; }
    }
}
