using System;
using Confluence.Client.ViewModels;
using UnityEngine.UIElements;

namespace Confluence.Client.UI
{
    /// <summary>
    /// Round reveal (spec L44, L329) from an <see cref="IRoundRevealViewModel"/>: the next opponent and their completed
    /// previous-round loadout, remaining Resolve, and the generated arena described in words (diameter, biome,
    /// environment rule, major terrain, props, plain material descriptions). The panel sits on the left so the
    /// rotatable arena overview renders beside it. Continue (Enter) opens the shop once the arena is released.
    /// </summary>
    public sealed class RoundRevealScreen
    {
        private readonly IRoundRevealViewModel _model;
        private readonly VisualElement _content;
        private readonly Button _continue;

        public RoundRevealScreen(IRoundRevealViewModel model, string sessionLabel)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            Root = Ui.Element("cf-screen", "cf-screen--side");
            Root.pickingMode = PickingMode.Ignore;
            VisualElement panel = Ui.Panel("cf-side-panel");
            if (!string.IsNullOrEmpty(sessionLabel))
            {
                panel.Add(Ui.Label(sessionLabel, "cf-lab-label"));
            }

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("cf-grow");
            _content = scroll.contentContainer;
            panel.Add(scroll);
            _continue = Ui.Button("Continue", () => _model.Continue(), "cf-button--primary");
            panel.Add(_continue);
            panel.Add(Ui.Muted("Arena view: right-drag or Q/E rotate, wheel zoom."));
            Root.Add(panel);
            _model.Changed += Refresh;
            Refresh();
        }

        public VisualElement Root { get; }

        public void Detach() => _model.Changed -= Refresh;

        public void FocusContinue() => _continue.Focus();

        public void Refresh()
        {
            _content.Clear();
            _content.Add(Ui.Heading(_model.Title));
            _content.Add(Ui.Label("Opponent: " + _model.OpponentName + " - " + _model.OpponentWizard, "cf-text"));
            _content.Add(Ui.Label("Resolve: you " + _model.PlayerResolve + ", opponent " + _model.OpponentResolve, "cf-text"));
            _content.Add(Ui.Label("Opponent's completed previous-round loadout", "cf-build-section"));
            foreach (string line in _model.OpponentLoadout)
            {
                _content.Add(Ui.Muted(line));
            }

            _content.Add(Ui.Label("Arena", "cf-build-section"));
            _content.Add(Ui.Muted(_model.ArenaStatus));
            if (_model.ArenaReady)
            {
                _content.Add(Ui.Label(_model.Diameter + " m diameter - " + _model.Biome, "cf-build-title"));
                if (!string.IsNullOrEmpty(_model.EnvironmentRule))
                {
                    _content.Add(Ui.Label("Environment rule: " + _model.EnvironmentRule, "cf-text"));
                }

                _content.Add(Ui.Label("Major terrain", "cf-build-section"));
                _content.Add(Ui.Muted(string.Join(", ", _model.MajorTerrain)));
                _content.Add(Ui.Label("Props", "cf-build-section"));
                _content.Add(Ui.Muted(_model.Props.Count > 0 ? string.Join(", ", _model.Props) : "None"));
                _content.Add(Ui.Label("Materials", "cf-build-section"));
                foreach (MaterialDescription material in _model.Materials)
                {
                    _content.Add(Ui.Muted(material.Label + ": " + material.Description));
                }
            }

            _content.Add(Ui.Label("Next: " + _model.NextPhase, "cf-build-section"));
            _continue.SetEnabled(_model.CanContinue);
            _continue.text = _model.CanContinue ? "Continue to the shop" : "Waiting for the arena...";
        }
    }

    /// <summary>
    /// Round results (spec L49, L359-L361) from an <see cref="IRoundResultsViewModel"/>: outcome and reason, each
    /// fighter's Resolve change, the next baseline income and winner bonus, the remaining build, and the decisive
    /// sequence as numeric events (the narrative explanation needs the spell and reaction runtimes).
    /// </summary>
    public sealed class ResultsScreen
    {
        private readonly IRoundResultsViewModel _model;

        public ResultsScreen(IRoundResultsViewModel model, string sessionLabel)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            Root = Ui.Element("cf-screen", "cf-screen--match");
            if (!string.IsNullOrEmpty(sessionLabel))
            {
                Root.Add(Ui.Label(sessionLabel, "cf-lab-label"));
            }

            Root.Add(Ui.Heading(_model.Title));
            VisualElement body = Ui.Element("cf-match-body");
            VisualElement main = Ui.Element("cf-match-main");
            main.Add(Ui.Label(_model.Outcome, "cf-results-outcome"));
            main.Add(Ui.Label(_model.Reason, "cf-text"));
            main.Add(Ui.Label("Resolve", "cf-build-section"));
            foreach (ResolveLine line in _model.Resolve)
            {
                string change = line.Change == 0 ? "no change" : line.Change.ToString("+0;-0");
                main.Add(Ui.Label(line.Name + ": " + line.Before + " -> " + line.After + " (" + change + ")", "cf-text"));
            }

            main.Add(Ui.Label("Economy", "cf-build-section"));
            main.Add(Ui.Label("Next baseline income: " + ContentFormat.Crowns(_model.NextIncome), "cf-text"));
            main.Add(Ui.Label("Winner bonus: " + (_model.WinnerBonus > 0 ? ContentFormat.Crowns(_model.WinnerBonus) : "none (draws and losses grant none, spec L193)"), "cf-text"));
            main.Add(Ui.Label("Remaining build", "cf-build-section"));
            foreach (string line in _model.RemainingBuild)
            {
                main.Add(Ui.Muted(line));
            }

            var button = Ui.Button(_model.ContinueLabel, () => _model.Continue(), "cf-button--primary", "cf-button--wide");
            main.Add(button);
            body.Add(main);

            VisualElement side = Ui.Panel("cf-match-side");
            side.Add(Ui.Label("Decisive sequence (last 5 s, numeric events)", "cf-build-section"));
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("cf-grow");
            if (_model.DecisiveSequence.Count == 0)
            {
                scroll.Add(Ui.Muted("No events in the final seconds."));
            }

            foreach (string line in _model.DecisiveSequence)
            {
                scroll.Add(Ui.Label(line, "cf-build-small"));
            }

            side.Add(scroll);
            side.Add(Ui.Muted("The narrated explanation (attack, reaction, mitigation, control, final damage) arrives with the spell runtime."));
            body.Add(side);
            Root.Add(body);
            Continue = button;
        }

        public VisualElement Root { get; }

        public Button Continue { get; }
    }
}
