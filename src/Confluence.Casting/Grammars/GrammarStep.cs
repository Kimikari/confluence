namespace Confluence.Casting.Grammars
{
    public enum GrammarStepKind : byte
    {
        /// <summary>Preparation continues.</summary>
        Continue = 0,

        /// <summary>The player completed the grammar (release, final token...).</summary>
        Commit,

        /// <summary>The grammar committed on its own (overhold vent, band exit, scheduled launch).</summary>
        AutoCommit,

        /// <summary>The grammar abandoned the attempt; nothing is spent (spec L470, L90).</summary>
        Cancel,
    }

    /// <summary>The grammar's decision for one preparation tick.</summary>
    public readonly struct GrammarStep
    {
        private GrammarStep(GrammarStepKind kind, MasteryResult mastery)
        {
            Kind = kind;
            Mastery = mastery;
        }

        public GrammarStepKind Kind { get; }

        /// <summary>The cast's one mastery result, for Commit and AutoCommit.</summary>
        public MasteryResult Mastery { get; }

        public bool Commits => Kind == GrammarStepKind.Commit || Kind == GrammarStepKind.AutoCommit;

        public static GrammarStep Continue => default;

        public static GrammarStep Commit(MasteryResult mastery) => new GrammarStep(GrammarStepKind.Commit, mastery);

        public static GrammarStep AutoCommit(MasteryResult mastery) => new GrammarStep(GrammarStepKind.AutoCommit, mastery);

        public static GrammarStep Cancel => new GrammarStep(GrammarStepKind.Cancel, default);

        public override string ToString() => Commits ? Kind + " " + Mastery : Kind.ToString();
    }

    /// <summary>Per-tick facts the machine gives a grammar during preparation.</summary>
    public readonly struct GrammarContext
    {
        public GrammarContext(int tick, int preparationStartTick, CastableSpell spell, int slot)
        {
            Tick = tick;
            PreparationStartTick = preparationStartTick;
            Spell = spell;
            Slot = slot;
        }

        public int Tick { get; }
        public int PreparationStartTick { get; }

        /// <summary>Ticks since preparation opened (0 on the opening tick).</summary>
        public int Elapsed => Tick - PreparationStartTick;

        /// <summary>The selected spell; the grammar never changes it (spec L470).</summary>
        public CastableSpell Spell { get; }

        public int Slot { get; }
    }
}
