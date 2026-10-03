using UnityEngine;
using UnityEngine.UIElements;

namespace Confluence.Client.UI
{
    /// <summary>
    /// The single UI Toolkit document. PanelSettings are created in code; the only assets are the runtime theme
    /// (Resources/UI/ConfluenceTheme.tss, which imports Unity's default runtime theme) and the stylesheet.
    /// Three stacked layers: the current screen, a HUD layer used by in-world views, and a modal overlay.
    /// </summary>
    public sealed class UiShell : MonoBehaviour
    {
        public const string ThemeResource = "UI/ConfluenceTheme";
        public const string StyleResource = "UI/Confluence";
        public const int ReferenceWidth = 1920;
        public const int ReferenceHeight = 1080;

        private UIDocument _document;
        private PanelSettings _panelSettings;

        public VisualElement Root { get; private set; }
        public VisualElement ScreenLayer { get; private set; }
        public VisualElement HudLayer { get; private set; }
        public VisualElement OverlayLayer { get; private set; }

        public bool HasOverlay => OverlayLayer != null && OverlayLayer.childCount > 0;

        public static UiShell Create(Transform parent)
        {
            var go = new GameObject("UI");
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            UiShell shell = go.AddComponent<UiShell>();
            shell._panelSettings = CreatePanelSettings();
            shell._document = go.AddComponent<UIDocument>();
            shell._document.panelSettings = shell._panelSettings;
            go.SetActive(true);
            shell.BuildLayers();
            return shell;
        }

        /// <summary>Replaces the current screen.</summary>
        public void ShowScreen(VisualElement screen)
        {
            ScreenLayer.Clear();
            if (screen != null)
            {
                ScreenLayer.Add(screen);
            }
        }

        public void ShowOverlay(VisualElement overlay)
        {
            OverlayLayer.Clear();
            if (overlay != null)
            {
                OverlayLayer.Add(overlay);
            }
        }

        public void CloseOverlay() => OverlayLayer.Clear();

        /// <summary>
        /// Scales every panel (scalable HUD and text, spec L371): the layout stays authored at 1920x1080 and the
        /// reference resolution shrinks as the scale grows.
        /// </summary>
        public void SetScale(float scale)
        {
            float s = Mathf.Clamp(scale, 0.5f, 2f);
            _panelSettings.referenceResolution = new Vector2Int(Mathf.RoundToInt(ReferenceWidth / s), Mathf.RoundToInt(ReferenceHeight / s));
        }

        public float Scale => _panelSettings.referenceResolution.x > 0 ? ReferenceWidth / (float)_panelSettings.referenceResolution.x : 1f;

        public void SetHud(VisualElement hud)
        {
            HudLayer.Clear();
            if (hud != null)
            {
                HudLayer.Add(hud);
            }
        }

        private static PanelSettings CreatePanelSettings()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = "Confluence Runtime Panel";
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>(ThemeResource);
            if (settings.themeStyleSheet == null)
            {
                Debug.LogError("[UI] Missing runtime theme Resources/" + ThemeResource + ".tss");
            }

            // Scalable HUD and menus (spec L371): layout is authored at 1920x1080 and scales with the window.
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(ReferenceWidth, ReferenceHeight);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            settings.sortingOrder = 0;
            return settings;
        }

        private void BuildLayers()
        {
            Root = _document.rootVisualElement;
            StyleSheet sheet = Resources.Load<StyleSheet>(StyleResource);
            if (sheet != null)
            {
                Root.styleSheets.Add(sheet);
            }
            else
            {
                Debug.LogError("[UI] Missing stylesheet Resources/" + StyleResource + ".uss");
            }

            Root.AddToClassList("cf-root");
            ScreenLayer = Layer("cf-layer-screen");
            HudLayer = Layer("cf-layer-hud");
            OverlayLayer = Layer("cf-layer-overlay");
            HudLayer.pickingMode = PickingMode.Ignore;
        }

        private VisualElement Layer(string name)
        {
            var layer = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            layer.style.position = Position.Absolute;
            layer.style.left = 0;
            layer.style.top = 0;
            layer.style.right = 0;
            layer.style.bottom = 0;
            Root.Add(layer);
            return layer;
        }

        private void OnDestroy()
        {
            if (_panelSettings != null)
            {
                Destroy(_panelSettings);
            }
        }
    }
}
