using System;
using System.Collections.Generic;
using Confluence.Arena.Generation;
using Confluence.Client.Arena;
using Confluence.Core.Arena;
using Confluence.Core.Content;

namespace Confluence.Client.ViewModels
{
    /// <summary>
    /// <see cref="IRoundRevealViewModel"/> filled by its host: the opponent and their previous loadout are set at
    /// construction, the arena once generation releases it (spec L44: diameter, biome, major terrain, props, and
    /// remaining Resolve; L329 material descriptions).
    /// </summary>
    public sealed class RoundRevealModel : IRoundRevealViewModel
    {
        private readonly Catalogue _catalogue;
        private readonly Action _onContinue;
        private readonly List<string> _terrain = new List<string>();
        private readonly List<string> _props = new List<string>();
        private readonly List<MaterialDescription> _materials = new List<MaterialDescription>();

        public RoundRevealModel(Catalogue catalogue, string title, string opponentName, string opponentWizard, IReadOnlyList<string> opponentLoadout,
            int playerResolve, int opponentResolve, string nextPhase, Action onContinue)
        {
            _catalogue = catalogue;
            Title = title;
            OpponentName = opponentName;
            OpponentWizard = opponentWizard;
            OpponentLoadout = opponentLoadout ?? Array.Empty<string>();
            PlayerResolve = playerResolve;
            OpponentResolve = opponentResolve;
            NextPhase = nextPhase;
            _onContinue = onContinue;
            ArenaStatus = "Generating the round arena...";
        }

        public event Action Changed;

        public string Title { get; }
        public string OpponentName { get; }
        public string OpponentWizard { get; }
        public IReadOnlyList<string> OpponentLoadout { get; }
        public int PlayerResolve { get; }
        public int OpponentResolve { get; }
        public bool ArenaReady { get; private set; }
        public string ArenaStatus { get; private set; }
        public int Diameter { get; private set; }
        public string Biome { get; private set; }
        public string EnvironmentRule { get; private set; }
        public IReadOnlyList<string> MajorTerrain => _terrain;
        public IReadOnlyList<string> Props => _props;
        public IReadOnlyList<MaterialDescription> Materials => _materials;
        public string NextPhase { get; }
        public bool CanContinue => ArenaReady;

        public void Continue()
        {
            if (CanContinue)
            {
                _onContinue?.Invoke();
            }
        }

        public void SetArenaFailed(string error)
        {
            ArenaReady = false;
            ArenaStatus = "Arena generation failed: " + error;
            Changed?.Invoke();
        }

        /// <summary>Describes a released arena.</summary>
        public void SetArena(GeneratedArena arena)
        {
            ArenaLayout layout = arena.Layout;
            Diameter = layout.Diameter;
            Biome = _catalogue != null && layout.BiomeId != null && _catalogue.BiomesById.TryGetValue(layout.BiomeId, out BiomeDef biome)
                ? biome.Name + " (" + biome.Id + ")"
                : layout.BiomeId;
            EnvironmentRule = layout.EnvironmentRule;
            ArenaStatus = ArenaReportText.Release(layout);
            FillTerrain(layout);
            FillProps(layout);
            _materials.Clear();
            foreach (SurfaceKind kind in Surfaces.PresentIn(layout))
            {
                SurfaceStyle style = Surfaces.Style(kind);
                _materials.Add(new MaterialDescription { Label = style.Label, Description = style.Description });
            }

            ArenaReady = true;
            Changed?.Invoke();
        }

        // Distinct module names in placement order, mirrored pairs counted once (spec L44 "major terrain").
        private void FillTerrain(ArenaLayout layout)
        {
            _terrain.Clear();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ModuleInstance module in layout.Modules)
            {
                if (module == null || module.ModuleId == null || !seen.Add(module.ModuleId))
                {
                    continue;
                }

                _terrain.Add(_catalogue != null && _catalogue.ModulesById.TryGetValue(module.ModuleId, out ModuleDef def) ? def.Name : module.ModuleId);
            }
        }

        private void FillProps(ArenaLayout layout)
        {
            _props.Clear();
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (PropPlacement prop in layout.Props)
            {
                if (prop == null || prop.PropId == null)
                {
                    continue;
                }

                if (!counts.ContainsKey(prop.PropId))
                {
                    counts[prop.PropId] = 0;
                    order.Add(prop.PropId);
                }

                counts[prop.PropId]++;
            }

            foreach (string id in order)
            {
                string name = _catalogue != null && _catalogue.PropsById.TryGetValue(id, out PropDef def) ? def.Name : id;
                _props.Add(counts[id] + " x " + name);
            }
        }
    }

    /// <summary><see cref="IRoundResultsViewModel"/> filled once by its host after a duel (spec L49, L359-L361).</summary>
    public sealed class RoundResultsModel : IRoundResultsViewModel
    {
        private readonly Action _onContinue;

        public RoundResultsModel(string title, string outcome, string reason, IReadOnlyList<ResolveLine> resolve, int nextIncome, int winnerBonus,
            string decisiveHeading, IReadOnlyList<string> decisiveSequence, IReadOnlyList<string> remainingBuild, bool matchOver, Action onContinue)
        {
            Title = title;
            Outcome = outcome;
            Reason = reason;
            Resolve = resolve ?? Array.Empty<ResolveLine>();
            NextIncome = nextIncome;
            WinnerBonus = winnerBonus;
            DecisiveHeading = decisiveHeading;
            DecisiveSequence = decisiveSequence ?? Array.Empty<string>();
            RemainingBuild = remainingBuild ?? Array.Empty<string>();
            MatchOver = matchOver;
            ContinueLabel = matchOver ? "Finish Laboratory session" : "Next round";
            _onContinue = onContinue;
        }

        public event Action Changed
        {
            add { }
            remove { }
        }

        public string Title { get; }
        public string Outcome { get; }
        public string Reason { get; }
        public IReadOnlyList<ResolveLine> Resolve { get; }
        public int NextIncome { get; }
        public int WinnerBonus { get; }
        public string DecisiveHeading { get; }
        public IReadOnlyList<string> DecisiveSequence { get; }
        public IReadOnlyList<string> RemainingBuild { get; }
        public string ContinueLabel { get; }
        public bool MatchOver { get; }

        public void Continue() => _onContinue?.Invoke();
    }
}
