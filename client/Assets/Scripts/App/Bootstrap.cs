using Confluence.Client.Arena;
using Confluence.Client.Content;
using Confluence.Client.Laboratory;
using Confluence.Client.Settings;
using Confluence.Client.UI;
using Confluence.Core.Rules;
using UnityEngine;

namespace Confluence.Client.App
{
    /// <summary>
    /// Builds the whole client at runtime: settings, the catalogue, the state machine, the UI shell, and the
    /// screen for each state. The generated Assets/Scenes/Bootstrap.unity holds one instance; pressing Play in
    /// any other scene creates one automatically (<see cref="EnsureBootstrap"/>). There are no prefabs.
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        private ClientContext _context;
        private Camera _menuCamera;
        private ArenaPreviewController _preview;
        private LaboratoryController _laboratory;

        public static Bootstrap Instance { get; private set; }

        public ClientContext Context => _context;

        public ArenaPreviewController Preview => _preview;

        public LaboratoryController Laboratory => _laboratory;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureBootstrap()
        {
            if (Instance == null && FindAnyObjectByType<Bootstrap>() == null)
            {
                new GameObject("Bootstrap").AddComponent<Bootstrap>();
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            CommandLineOptions options = CommandLineOptions.FromEnvironment();
            if (options.IsSmokeTest)
            {
                SmokeTestDriver.Attach(gameObject, options);
            }

            if (options.Warnings.Length > 0)
            {
                Debug.LogWarning("[Boot] " + options.Warnings);
            }

            Application.targetFrameRate = GlobalRules.Network.TargetFrameRate; // spec L421: 120 fps target
            Application.runInBackground = true;

            // Smoke runs use in-memory settings so they neither depend on nor change the user's saved options.
            ISettingsStore store = options.IsSmokeTest ? (ISettingsStore)new MemorySettingsStore() : new PlayerPrefsSettingsStore();
            var settings = new ClientSettings(store);
            settings.Load();
            var profile = new PlayProfile(store);
            profile.Load();
            settings.Changed += () => ApplyAudio(settings);
            ApplyAudio(settings);

            CreateMenuCamera();
            UiShell ui = UiShell.Create(transform);
            ui.SetScale(settings.UiScale);
            settings.Changed += () => ui.SetScale(settings.UiScale);
            var states = new AppStateMachine();
            var catalogue = new CatalogueService();
            _context = new ClientContext(states, settings, profile, catalogue, ui, options, Quit);

            states.StateChanged += OnStateChanged;
            catalogue.Completed += OnCatalogueCompleted;
            ShowScreen(AppState.Boot);
            catalogue.BeginLoad();
        }

        private void Update()
        {
            _context?.Catalogue.Poll();
            if (_context != null && _context.Ui.HasOverlay && UnityEngine.InputSystem.Keyboard.current != null
                && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame && _preview == null && _laboratory == null)
            {
                _context.Ui.CloseOverlay();
            }
        }

        private void OnCatalogueCompleted(CatalogueService service)
        {
            AppState next = _context.Options.ArenaPreview ? AppState.ArenaPreview
                : _context.Options.Laboratory ? AppState.Laboratory
                : AppState.MainMenu;
            _context.States.TransitionTo(next);
            if (next == AppState.MainMenu)
            {
                OpenStartPanel(_context.Options.MenuPanel);
            }
        }

        // -menuPanel: the Play card is opened by MainMenuScreen itself.
        private void OpenStartPanel(MenuPanel panel)
        {
            switch (panel)
            {
                case MenuPanel.Settings:
                    _context.OpenSettings();
                    break;
                case MenuPanel.Codex:
                    _context.States.TransitionTo(AppState.Codex);
                    break;
                case MenuPanel.Training:
                    _context.States.TransitionTo(AppState.Training);
                    break;
            }
        }

        private void OnStateChanged(AppState from, AppState to)
        {
            Debug.Log("[App] " + from + " -> " + to);
            if (from == AppState.ArenaPreview)
            {
                ExitPreview();
            }

            if (from == AppState.Laboratory)
            {
                ExitLaboratory();
            }

            _context.Ui.CloseOverlay();
            ShowScreen(to);
        }

        private void ShowScreen(AppState state)
        {
            UiShell ui = _context.Ui;
            switch (state)
            {
                case AppState.Boot:
                    ui.ShowScreen(BootScreen.Build(_context));
                    break;
                case AppState.MainMenu:
                    ui.ShowScreen(MainMenuScreen.Build(_context));
                    break;
                case AppState.Codex:
                    ui.ShowScreen(CodexScreen.Build(_context));
                    break;
                case AppState.ArenaPreview:
                    ui.ShowScreen(null);
                    EnterPreview();
                    break;
                case AppState.Laboratory:
                    EnterLaboratory();
                    break;
                default:
                    ui.ShowScreen(PlaceholderScreen.Build(_context, state));
                    break;
            }
        }

        private void EnterPreview()
        {
            CommandLineOptions options = _context.Options;
            _menuCamera.gameObject.SetActive(false);
            _preview = ArenaPreviewController.Create(_context.Catalogue.Catalogue, _context.Settings, _context.Ui, options.ArenaRequest,
                options.PreviewView, options.PreviewTimeSec, captureCursor: !options.IsSmokeTest, onExit: LeavePreview);
        }

        private void LeavePreview()
        {
            AppState back = _context.States.Previous == AppState.Training ? AppState.Training : AppState.MainMenu;
            _context.States.TryTransitionTo(back);
        }

        private void ExitPreview()
        {
            if (_preview != null)
            {
                Destroy(_preview.gameObject);
                _preview = null;
            }

            _context.Ui.SetHud(null);
            _menuCamera.gameObject.SetActive(true);
        }

        private void EnterLaboratory()
        {
            _menuCamera.gameObject.SetActive(false);
            _laboratory = LaboratoryController.Create(_context, LeaveLaboratory);
        }

        private void LeaveLaboratory()
        {
            AppState back = _context.States.Previous == AppState.Training ? AppState.Training : AppState.MainMenu;
            _context.States.TryTransitionTo(back);
        }

        private void ExitLaboratory()
        {
            if (_laboratory != null)
            {
                Destroy(_laboratory.gameObject);
                _laboratory = null;
            }

            _context.Ui.SetHud(null);
            _menuCamera.gameObject.SetActive(true);
        }

        private void CreateMenuCamera()
        {
            var go = new GameObject("Menu camera") { tag = "MainCamera" };
            go.transform.SetParent(transform, false);
            _menuCamera = go.AddComponent<Camera>();
            _menuCamera.clearFlags = CameraClearFlags.SolidColor;
            _menuCamera.backgroundColor = new Color(0.04f, 0.045f, 0.07f);
            _menuCamera.cullingMask = 0;
            go.AddComponent<AudioListener>();
        }

        private static void ApplyAudio(ClientSettings settings)
        {
            AudioListener.volume = settings.MasterVolume;
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
