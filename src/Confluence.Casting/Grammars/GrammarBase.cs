using System;

namespace Confluence.Casting.Grammars
{
    /// <summary>Shared plumbing for grammar implementations: identity, route, and the HUD state slot.</summary>
    public abstract class GrammarBase : IGrammar
    {
        protected GrammarBase(string wizardId, GrammarFamily family, InputRoute route)
        {
            if (string.IsNullOrEmpty(wizardId)) throw new ArgumentException("Wizard id is required.", nameof(wizardId));
            WizardId = wizardId;
            Family = family;
            Route = route;
        }

        public string WizardId { get; }
        public GrammarFamily Family { get; }
        public InputRoute Route { get; }

        public GrammarUiState Ui => UiState;

        /// <summary>Updated by implementations every preparation tick.</summary>
        protected GrammarUiState UiState;

        public virtual void Observe(in CastInputFrame frame)
        {
        }

        public abstract bool WantsToBegin(in CastInputFrame frame, CastableSpell spell);

        public abstract void Begin(in CastInputFrame frame, in GrammarContext context);

        public abstract GrammarStep Advance(in CastInputFrame frame, in GrammarContext context);

        public virtual void End(bool committed, in MasteryResult mastery)
        {
            UiState = GrammarUiState.Inactive;
        }

        /// <summary>Press edges seen this tick for <paramref name="button"/> (0, 1, or 2 for a sub-tick tap).</summary>
        protected static int Edges(in CastInputFrame frame, InputButtons button) =>
            ((frame.Pressed & button) != 0 ? 1 : 0) + ((frame.Released & button) != 0 ? 1 : 0);
    }
}
