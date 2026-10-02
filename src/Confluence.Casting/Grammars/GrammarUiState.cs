namespace Confluence.Casting.Grammars
{
    /// <summary>
    /// What the client HUD draws for an open preparation (spec L472 "the game shows the selected spell, preparation
    /// progress, and exact input state"; L500 exact miss reasons). Values are plain data so the same grammar code
    /// drives the server, prediction, and the HUD. Unused bands are NaN-free zero ranges with the Has* flags false.
    /// </summary>
    public struct GrammarUiState
    {
        public bool Active;
        public int ElapsedTicks;

        /// <summary>Gauge position normalized 0-1 for drawing.</summary>
        public float Gauge;

        /// <summary>Gauge value in the grammar's own units (pressure 0-100, percent of crest...).</summary>
        public float GaugeValue;

        public bool HasSafeBand;
        public float SafeBandLow;
        public float SafeBandHigh;

        public bool HasMasteryBand;
        public float MasteryBandLow;
        public float MasteryBandHigh;

        /// <summary>Elapsed ticks at which release casts (normal output).</summary>
        public bool HasReleaseWindow;
        public TickWindow ReleaseWindow;

        /// <summary>Elapsed ticks at which release earns the reward (the marked window).</summary>
        public bool HasMasteryWindow;
        public TickWindow MasteryWindow;

        /// <summary>Progress toward a continuous requirement (e.g. ticks held inside the mastery band).</summary>
        public int ProgressTicks;
        public int RequiredTicks;

        /// <summary>A normal cast is queued and will commit at <see cref="QueuedCommitTick"/> elapsed.</summary>
        public bool Queued;
        public int QueuedCommitTick;

        /// <summary>The gauge is auto-venting; a normal cast follows in <see cref="VentRemainingTicks"/>.</summary>
        public bool Venting;
        public int VentRemainingTicks;

        /// <summary>The input the grammar is currently applying (vent valve open, rate-limited state).</summary>
        public bool ModifierEngaged;

        /// <summary>The result a release on this tick would produce: None when it would be mastered.</summary>
        public MissReason ReleaseNow;

        /// <summary>Inputs that arrived faster than the accepted interval during this preparation.</summary>
        public int ThrottledInputs;

        public static GrammarUiState Inactive => default;
    }
}
