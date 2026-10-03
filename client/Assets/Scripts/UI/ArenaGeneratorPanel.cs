using System;
using System.Collections.Generic;
using System.Globalization;
using Confluence.Arena.Generation;
using Confluence.Client.Arena;
using Confluence.Core.Content;
using Confluence.Core.Random;
using UnityEngine.UIElements;

namespace Confluence.Client.UI
{
    /// <summary>
    /// Pickers for Confluence.Arena's generator: match seed, round, diameter (24/32/40/48 m or the balanced round
    /// schedule, spec L306), and biome (seeded for the round or a forced biome that supports the diameter, spec L316).
    /// Shows how the arena was released (attempt k of 8 or the prevalidated fallback, spec L323), the validator's
    /// metrics (L310-L312), and the first failure of each rejected attempt.
    /// </summary>
    public sealed class ArenaGeneratorPanel
    {
        public const string ScheduleChoice = "Round schedule";
        public const string SeededBiomeChoice = "Seeded for the round";

        /// <summary>Default match seed of the preview (any value works; this one matches the merge-time probes).</summary>
        public const ulong DefaultSeed = 7919;

        private static readonly int[] Diameters = { 24, 32, 40, 48 };

        private readonly Catalogue _catalogue;
        private readonly Action<ArenaRequest> _onGenerate;
        private readonly TextField _seed;
        private readonly IntegerField _round;
        private readonly DropdownField _diameter;
        private readonly DropdownField _biome;
        private readonly Label _status;
        private readonly Label _release;
        private readonly VisualElement _metrics;
        private readonly Button _generate;

        public ArenaGeneratorPanel(Catalogue catalogue, ArenaRequest initial, Action<ArenaRequest> onGenerate)
        {
            _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
            _onGenerate = onGenerate;
            initial = initial ?? new ArenaRequest();

            Root = Ui.Element("cf-hud", "cf-hud--generator");
            Root.Add(Ui.Label("Arena generator (Confluence.Arena)", "cf-hud-title"));

            VisualElement seedRow = Ui.Row();
            _seed = Ui.TextField("Match seed", initial.MatchSeed.ToString(CultureInfo.InvariantCulture), 24, null);
            _seed.AddToClassList("cf-grow");
            seedRow.Add(_seed);
            seedRow.Add(Ui.Button("New seed", NextSeed, "cf-button--small"));
            Root.Add(seedRow);

            _round = Ui.IntegerField("Round", Math.Max(1, initial.Round), null);
            Root.Add(_round);

            var diameterChoices = new List<string> { ScheduleChoice };
            for (int i = 0; i < Diameters.Length; i++)
            {
                diameterChoices.Add(Diameters[i] + " m");
            }

            int diameterIndex = Array.IndexOf(Diameters, initial.Diameter) + 1;
            _diameter = new DropdownField("Diameter", diameterChoices, Math.Max(0, diameterIndex));
            _diameter.AddToClassList("cf-field");
            _diameter.RegisterValueChangedCallback(_ => RefreshBiomes(null));
            Root.Add(_diameter);

            _biome = new DropdownField("Biome", new List<string> { SeededBiomeChoice }, 0);
            _biome.AddToClassList("cf-field");
            Root.Add(_biome);
            RefreshBiomes(initial.BiomeId);

            VisualElement buttons = Ui.Row();
            _generate = Ui.Button("Generate", Generate, "cf-button--small", "cf-button--primary");
            buttons.Add(_generate);
            buttons.Add(Ui.Muted("Enter regenerates; G hides this panel."));
            Root.Add(buttons);

            _status = Ui.Text(string.Empty);
            _release = Ui.Text(string.Empty);
            Root.Add(_status);
            Root.Add(_release);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("cf-metrics");
            _metrics = scroll.contentContainer;
            Root.Add(scroll);

            Root.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == UnityEngine.KeyCode.Return || e.keyCode == UnityEngine.KeyCode.KeypadEnter)
                {
                    Generate();
                    e.StopPropagation();
                }
            });
        }

        public VisualElement Root { get; }

        /// <summary>The request described by the pickers (invalid seed text keeps seed 0 and is reported).</summary>
        public ArenaRequest CurrentRequest()
        {
            ulong.TryParse(_seed.value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong seed);
            var request = new ArenaRequest
            {
                MatchSeed = seed,
                Round = Math.Max(1, Math.Min(ArenaSchedule.ScheduledRoundCapacity, _round.value)),
                Diameter = _diameter.index <= 0 ? 0 : Diameters[_diameter.index - 1],
                BiomeId = BiomeIdFromChoice(_biome.value),
            };
            return request;
        }

        /// <summary>Reflects the generation state: running time, release line, metrics, and rejected attempts.</summary>
        public void SetStatus(ArenaGenerationService service)
        {
            _metrics.Clear();
            if (service == null)
            {
                return;
            }

            switch (service.State)
            {
                case ArenaGenerationState.Running:
                    _status.text = "Generating... (validation runs up to eight seeds, spec L323)";
                    _release.text = string.Empty;
                    _generate.SetEnabled(false);
                    return;
                case ArenaGenerationState.Failed:
                    _status.text = "Generation failed: " + service.Error;
                    _release.text = string.Empty;
                    _generate.SetEnabled(true);
                    return;
                case ArenaGenerationState.Idle:
                    _status.text = string.Empty;
                    _release.text = string.Empty;
                    _generate.SetEnabled(true);
                    return;
            }

            _generate.SetEnabled(true);
            GeneratedArena arena = service.Result;
            ArenaRequest request = service.Request;
            _status.text = "Seed " + request.MatchSeed.ToString(CultureInfo.InvariantCulture) + ", round " + request.Round + ": "
                + arena.Layout.Diameter + " m " + BiomeName(arena.Layout.BiomeId) + " in "
                + service.Seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
            _release.text = (arena.UsedFallback ? "FALLBACK: " : string.Empty) + ArenaReportText.Release(arena)
                + ". Layout hash " + arena.LayoutHash.ToString("X16", CultureInfo.InvariantCulture);
            _release.EnableInClassList("cf-warning", arena.UsedFallback);

            List<MetricLine> lines = ArenaReportText.Metrics(arena.Layout.Report);
            for (int i = 0; i < lines.Count; i++)
            {
                VisualElement row = Ui.Element("cf-metric-row");
                row.Add(Ui.Label(lines[i].Label, "cf-metric-label"));
                row.Add(Ui.Label(lines[i].Value, "cf-metric-value"));
                _metrics.Add(row);
            }

            List<string> rejected = ArenaReportText.Rejections(arena);
            if (rejected.Count > 0)
            {
                _metrics.Add(Ui.Label("Rejected attempts", "cf-codex-key"));
                for (int i = 0; i < rejected.Count; i++)
                {
                    _metrics.Add(Ui.Muted(rejected[i]));
                }
            }
        }

        public void FocusGenerate() => _generate.Focus();

        private void Generate()
        {
            ulong parsed;
            if (!ulong.TryParse(_seed.value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                _status.text = "Match seed must be a whole number from 0 to " + ulong.MaxValue.ToString(CultureInfo.InvariantCulture) + ".";
                return;
            }

            _onGenerate?.Invoke(CurrentRequest());
        }

        // A deterministic successor through Core's Rng (the only randomness source, docs/ARCHITECTURE.md).
        private void NextSeed()
        {
            ArenaRequest current = CurrentRequest();
            ulong next = new Rng(current.MatchSeed ^ 0x9E3779B97F4A7C15UL).NextULong() % 1000000000UL;
            _seed.value = next.ToString(CultureInfo.InvariantCulture);
            RefreshBiomes(BiomeIdFromChoice(_biome.value));
        }

        private int EffectiveDiameter()
        {
            ArenaRequest request = CurrentRequest();
            return request.Diameter != 0 ? request.Diameter : ArenaSchedule.DiameterForRound(request.MatchSeed, request.Round);
        }

        // A forced biome must support the diameter (catalogue sizes, spec L335).
        private void RefreshBiomes(string keepBiomeId)
        {
            string keep = keepBiomeId ?? BiomeIdFromChoice(_biome.value);
            int diameter = EffectiveDiameter();
            var choices = new List<string> { SeededBiomeChoice };
            int selected = 0;
            List<BiomeDef> biomes = ArenaSchedule.CandidateBiomes(_catalogue, diameter, ContentAvailability.All);
            for (int i = 0; i < biomes.Count; i++)
            {
                choices.Add(biomes[i].Id + " " + biomes[i].Name);
                if (biomes[i].Id == keep)
                {
                    selected = choices.Count - 1;
                }
            }

            _biome.choices = choices;
            _biome.index = selected;
            _diameter.label = "Diameter" + (_diameter.index <= 0 ? " (" + diameter + " m)" : string.Empty);
        }

        private static string BiomeIdFromChoice(string choice)
        {
            if (string.IsNullOrEmpty(choice) || choice == SeededBiomeChoice)
            {
                return null;
            }

            int space = choice.IndexOf(' ');
            return space > 0 ? choice.Substring(0, space) : choice;
        }

        private string BiomeName(string biomeId)
        {
            return biomeId != null && _catalogue.BiomesById.TryGetValue(biomeId, out BiomeDef biome) ? biome.Name + " (" + biome.Id + ")" : biomeId;
        }
    }
}
