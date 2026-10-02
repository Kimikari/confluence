using System.Collections.Generic;
using System.Globalization;
using Confluence.Client.Arena;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using UnityEngine.UIElements;

namespace Confluence.Client.UI
{
    /// <summary>
    /// Overlay for the arena preview: diameter, biome, announced environment rule (spec L329, L335), camera mode,
    /// combat clock with boundary state (spec L48), controls, and a legend that shows each present material's
    /// actual pattern with a plain description (spec L329 "so players do not need to infer all material behavior
    /// from color").
    /// </summary>
    public sealed class ArenaPreviewHud
    {
        private readonly Label _mode;
        private readonly Label _clock;
        private readonly VisualElement _crosshair;

        public ArenaPreviewHud(ArenaRenderer arena, Catalogue catalogue)
        {
            Root = new VisualElement { pickingMode = PickingMode.Ignore };
            Root.style.position = Position.Absolute;
            Root.style.left = 0;
            Root.style.top = 0;
            Root.style.right = 0;
            Root.style.bottom = 0;

            ArenaLayout layout = arena.Layout;
            string biomeName = layout.BiomeId;
            if (catalogue != null && layout.BiomeId != null && catalogue.BiomesById.TryGetValue(layout.BiomeId, out BiomeDef biome))
            {
                biomeName = biome.Name + " (" + biome.Id + ")";
            }

            VisualElement info = Ui.Element("cf-hud");
            info.pickingMode = PickingMode.Ignore;
            info.Add(Ui.Label("Arena preview: " + biomeName + ", " + layout.Diameter + " m", "cf-hud-title"));
            if (!string.IsNullOrEmpty(layout.EnvironmentRule))
            {
                info.Add(Ui.Text("Environment rule: " + layout.EnvironmentRule));
            }

            if (layout.Report != null && !layout.Report.Valid && layout.Report.Failures.Count > 0)
            {
                info.Add(Ui.Muted(layout.Report.Failures[0]));
            }

            _mode = Ui.Text(string.Empty);
            _clock = Ui.Text(string.Empty);
            info.Add(_mode);
            info.Add(_clock);
            info.Add(Ui.Muted("Tab overview | F walk/fly | WASD move | Space jump/up | Ctrl down | Shift fast | "
                + "Q/E or right-drag rotate, wheel zoom | P pause clock, [ ] scrub 5 s, Backspace reset | Esc leave"));
            Root.Add(info);

            VisualElement legend = Ui.Element("cf-hud", "cf-hud--legend");
            legend.pickingMode = PickingMode.Ignore;
            legend.Add(Ui.Label("Materials", "cf-hud-title"));
            IReadOnlyList<SurfaceKind> kinds = arena.SurfacesPresent;
            for (int i = 0; i < kinds.Count; i++)
            {
                SurfaceStyle style = Surfaces.Style(kinds[i]);
                VisualElement row = Ui.Element("cf-legend-row");
                VisualElement swatch = Ui.Element("cf-legend-swatch");
                swatch.style.backgroundImage = new StyleBackground(arena.Materials.SurfaceTexture(kinds[i]));
                var tint = style.Tint;
                tint.a = 1f;
                swatch.style.unityBackgroundImageTintColor = tint;
                row.Add(swatch);
                Label text = Ui.Muted(style.Description);
                text.style.flexShrink = 1;
                row.Add(text);
                legend.Add(row);
            }

            Root.Add(legend);

            _crosshair = Ui.Element("cf-crosshair");
            _crosshair.pickingMode = PickingMode.Ignore;
            Root.Add(_crosshair);
        }

        public VisualElement Root { get; }

        public void SetMode(string mode, bool showCrosshair)
        {
            _mode.text = "Camera: " + mode;
            _crosshair.style.display = showCrosshair ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void SetClock(float timeSec, bool paused, float minRadius)
        {
            string phase;
            if (timeSec < GlobalRules.Boundary.ContractionStartSec)
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
    }
}
