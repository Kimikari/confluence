using System.Collections.Generic;
using System.Globalization;
using Confluence.Arena.Generation;
using Confluence.Client.Arena;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using UnityEngine.UIElements;

namespace Confluence.Client.UI
{
    /// <summary>
    /// Overlay for the arena preview: diameter, biome, announced environment rule (spec L329, L335), how the
    /// generator released the layout (L323), camera mode, combat clock with boundary state (L48), controls, the
    /// generator panel (<see cref="ArenaGeneratorPanel"/>), and a legend that shows each present material's actual
    /// pattern with a plain description (L329 "so players do not need to infer all material behavior from color").
    /// </summary>
    public sealed class ArenaPreviewHud
    {
        private readonly Catalogue _catalogue;
        private readonly Label _title;
        private readonly Label _release;
        private readonly Label _rule;
        private readonly Label _mode;
        private readonly Label _clock;
        private readonly VisualElement _legend;
        private readonly VisualElement _crosshair;

        public ArenaPreviewHud(Catalogue catalogue, ArenaGeneratorPanel generator)
        {
            _catalogue = catalogue;
            Root = new VisualElement { pickingMode = PickingMode.Ignore };
            Root.style.position = Position.Absolute;
            Root.style.left = 0;
            Root.style.top = 0;
            Root.style.right = 0;
            Root.style.bottom = 0;

            VisualElement info = Ui.Element("cf-hud");
            info.pickingMode = PickingMode.Ignore;
            _title = Ui.Label("Arena preview: generating...", "cf-hud-title");
            _release = Ui.Muted(string.Empty);
            _rule = Ui.Text(string.Empty);
            _mode = Ui.Text(string.Empty);
            _clock = Ui.Text(string.Empty);
            info.Add(_title);
            info.Add(_release);
            info.Add(_rule);
            info.Add(_mode);
            info.Add(_clock);
            info.Add(Ui.Muted("Tab overview | F walk/fly | WASD move | Space jump/up | Ctrl crouch/down | Alt dodge | Shift sprint/fast | "
                + "Q/E or right-drag rotate, wheel zoom | P pause clock, [ ] scrub 5 s, Backspace reset | G generator | Esc leave"));
            SetPickingIgnore(info);
            Root.Add(info);

            Generator = generator;
            if (generator != null)
            {
                Root.Add(generator.Root);
            }

            _legend = Ui.Element("cf-hud", "cf-hud--legend");
            _legend.pickingMode = PickingMode.Ignore;
            Root.Add(_legend);

            _crosshair = Ui.Element("cf-crosshair");
            _crosshair.pickingMode = PickingMode.Ignore;
            Root.Add(_crosshair);
        }

        public VisualElement Root { get; }

        public ArenaGeneratorPanel Generator { get; }

        public bool GeneratorVisible => Generator != null && Generator.Root.style.display != DisplayStyle.None;

        /// <summary>Shows the description and material legend of a (new) arena.</summary>
        public void Bind(ArenaRenderer arena, GeneratedArena generated)
        {
            ArenaLayout layout = arena.Layout;
            _title.text = "Arena preview: " + BiomeName(layout.BiomeId) + ", " + layout.Diameter + " m"
                + (layout.IsFallback ? "  [FALLBACK]" : string.Empty);
            _release.text = ArenaReportText.Release(layout) + ", seed " + layout.Seed.ToString(CultureInfo.InvariantCulture);
            _rule.text = string.IsNullOrEmpty(layout.EnvironmentRule) ? string.Empty : "Environment rule: " + layout.EnvironmentRule;

            _legend.Clear();
            _legend.Add(Ui.Label("Materials", "cf-hud-title"));
            ArenaLegend.Fill(_legend, arena);
        }

        public void ShowGenerator(bool visible)
        {
            if (Generator != null)
            {
                Generator.Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        public void SetMode(string mode, bool showCrosshair)
        {
            _mode.text = "Camera: " + mode;
            _crosshair.style.display = showCrosshair ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void SetClock(float timeSec, bool paused, float minRadius)
        {
            string phase;
            if (timeSec < 0f)
            {
                phase = "alcove ward, opens at 0 s"; // spec L46-L47
            }
            else if (timeSec < GlobalRules.Boundary.ContractionStartSec)
            {
                phase = "boundary steady until 55 s";
            }
            else if (timeSec < GlobalRules.Boundary.ContractionEndSec)
            {
                phase = "boundary contracting";
            }
            else
            {
                phase = "final floor";
            }

            _clock.text = "Combat clock " + timeSec.ToString("0.0", CultureInfo.InvariantCulture) + " s" + (paused ? " (paused)" : string.Empty)
                + ": " + phase + ", min radius " + minRadius.ToString("0.0", CultureInfo.InvariantCulture) + " m";
        }

        private string BiomeName(string biomeId)
        {
            if (_catalogue != null && biomeId != null && _catalogue.BiomesById.TryGetValue(biomeId, out BiomeDef biome))
            {
                return biome.Name + " (" + biome.Id + ")";
            }

            return biomeId;
        }

        private static void SetPickingIgnore(VisualElement element)
        {
            element.pickingMode = PickingMode.Ignore;
            foreach (VisualElement child in element.Children())
            {
                SetPickingIgnore(child);
            }
        }
    }

    /// <summary>The material legend shared by the preview, round reveal, and shop (spec L329).</summary>
    public static class ArenaLegend
    {
        /// <summary>Adds one row per present surface: its actual pattern swatch and plain description.</summary>
        public static void Fill(VisualElement container, ArenaRenderer arena)
        {
            IReadOnlyList<SurfaceKind> kinds = arena.SurfacesPresent;
            for (int i = 0; i < kinds.Count; i++)
            {
                SurfaceStyle style = Surfaces.Style(kinds[i]);
                VisualElement row = Ui.Element("cf-legend-row");
                row.pickingMode = PickingMode.Ignore;
                VisualElement swatch = Ui.Element("cf-legend-swatch");
                swatch.style.backgroundImage = new StyleBackground(arena.Materials.SurfaceTexture(kinds[i]));
                var tint = style.Tint;
                tint.a = 1f;
                swatch.style.unityBackgroundImageTintColor = tint;
                row.Add(swatch);
                Label text = Ui.Muted(style.Description);
                text.style.flexShrink = 1;
                text.pickingMode = PickingMode.Ignore;
                row.Add(text);
                container.Add(row);
            }
        }
    }
}
