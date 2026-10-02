using Confluence.Casting;
using Confluence.Casting.Grammars;

namespace Confluence.Casting.Tests.Support
{
    /// <summary>A test grammar that opens on a primary press and commits a chosen result after a fixed preparation.</summary>
    internal sealed class ScriptedGrammar : GrammarBase
    {
        private readonly int _commitAt;
        private readonly bool _auto;

        public ScriptedGrammar(int commitAt, MasteryResult result, bool auto = false)
            : base("TEST", GrammarFamily.ButtonTiming, InputRoute.Standard)
        {
            _commitAt = commitAt;
            Result = result;
            _auto = auto;
        }

        public MasteryResult Result { get; set; }
        public int Begins { get; private set; }
        public int Commits { get; private set; }
        public int Cancels { get; private set; }
        public int Observed { get; private set; }

        public override void Observe(in CastInputFrame frame) => Observed++;

        public override bool WantsToBegin(in CastInputFrame frame, CastableSpell spell) => frame.WasPressed(InputButtons.Primary);

        public override void Begin(in CastInputFrame frame, in GrammarContext context) => Begins++;

        public override GrammarStep Advance(in CastInputFrame frame, in GrammarContext context)
        {
            if (context.Elapsed < _commitAt) return GrammarStep.Continue;
            return _auto ? GrammarStep.AutoCommit(Result) : GrammarStep.Commit(Result);
        }

        public override void End(bool committed, in MasteryResult mastery)
        {
            if (committed) Commits++;
            else Cancels++;
        }
    }
}
