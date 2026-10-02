using System;

namespace Confluence.Casting.Grammars
{
    /// <summary>
    /// Guided normal preparation (spec L470 "Guided normal preparation is available for the complex modes. Guidance
    /// completes a normal action and gives no mastery reward"). Pressing the opening input starts a fixed
    /// preparation; when it completes the selected spell commits normally. Releasing early does not abort it, so a
    /// tap is enough. Used as the accessibility route of a wizard (with that wizard's stated normal preparation, see
    /// <see cref="GrammarRegistry.CreateGuided"/>) and by tests and novice bots (spec L381 "uses normal casts").
    /// </summary>
    public sealed class GuidedNormalGrammar : GrammarBase
    {
        private readonly int _preparationTicks;
        private readonly InputButtons _opening;

        public GuidedNormalGrammar(string wizardId, GrammarFamily family, int preparationTicks, InputButtons opening = InputButtons.Primary)
            : base(wizardId, family, InputRoute.Guided)
        {
            if (preparationTicks < 0) throw new ArgumentOutOfRangeException(nameof(preparationTicks));
            if (opening == InputButtons.None) throw new ArgumentException("An opening input is required.", nameof(opening));
            _preparationTicks = preparationTicks;
            _opening = opening;
        }

        public int PreparationTicks => _preparationTicks;

        public override bool WantsToBegin(in CastInputFrame frame, CastableSpell spell) => frame.WasPressed(_opening);

        public override void Begin(in CastInputFrame frame, in GrammarContext context)
        {
            UiState = new GrammarUiState
            {
                Active = true,
                HasReleaseWindow = true,
                ReleaseWindow = new TickWindow(_preparationTicks, _preparationTicks),
                RequiredTicks = _preparationTicks,
                ReleaseNow = MissReason.Guided,
            };
        }

        public override GrammarStep Advance(in CastInputFrame frame, in GrammarContext context)
        {
            int elapsed = context.Elapsed;
            UiState.ElapsedTicks = elapsed;
            UiState.ProgressTicks = Math.Min(elapsed, _preparationTicks);
            UiState.Gauge = _preparationTicks == 0 ? 1f : Math.Min(1f, elapsed / (float)_preparationTicks);
            UiState.GaugeValue = UiState.Gauge * 100f;
            return elapsed >= _preparationTicks ? GrammarStep.Commit(MasteryResult.Guided) : GrammarStep.Continue;
        }
    }
}
