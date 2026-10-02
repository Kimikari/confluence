using System;
using Confluence.Arena.Generation;
using Confluence.Client.App;
using Confluence.Client.Arena;
using Confluence.Client.Cameras;
using Confluence.Client.Input;
using Confluence.Client.Mapping;
using Confluence.Client.Movement;
using Confluence.Client.Presentation;
using Confluence.Client.UI;
using Confluence.Client.ViewModels;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Time;
using Confluence.Economy.Shop;
using Confluence.Sim.Duel;
using UnityEngine;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

namespace Confluence.Client.Laboratory
{
    /// <summary>What the Laboratory currently shows.</summary>
    public enum LabView
    {
        Setup,
        Generating,
        Reveal,
        Shop,
        Draft,
        Duel,
        Results,
    }

    /// <summary>
    /// Unity host of the offline Laboratory (spec L69): the setup screen, then either a sandbox duel against the
    /// training dummy at sparring range or full local rounds driven by <see cref="LaboratorySession"/> — reveal with the
    /// generated arena in a rotatable overview (spec L44, L329), shop and draft screens over local Economy objects
    /// with tick-counted phase timers (L45), the first-person duel (<see cref="OfflineDuel"/>, L46-L53), and results
    /// (L49, L359-L361). Esc opens the Laboratory menu (the offline duel pauses; online duels never do); Tab shows the
    /// build panel (loadout inspection, L46). Every screen carries the "modified, unranked" label.
    /// </summary>
    public sealed class LaboratoryController : MonoBehaviour
    {
        public const float DuelEndPauseSec = 2.5f;
        public const string ControlsHint = "WASD move | mouse aim | LMB cast | RMB guard | F melee | Shift sprint | Space jump | Ctrl crouch | Alt dodge | "
            + "1-6 or wheel slot | Q signature | E active | X consumable | T recast | R modifier | Z/C mods A/B | Tab build | Esc menu";

        private readonly TickClock _phaseClock = new TickClock();
        private ClientContext _context;
        private Catalogue _catalogue;
        private Action _onExit;
        private LaboratoryConfig _config;
        private LaboratorySession _session;
        private ArenaGenerationService _generation;
        private GeneratedArena _arena;
        private string _arenaKey;
        private string _pendingKey;
        private Action _whenArenaReady;
        private ArenaRenderer _renderer;
        private ArenaLayout _renderedLayout;
        private ArenaOverviewCamera _overview;
        private PreviewInput _previewInput;
        private Camera _backdrop;
        private OfflineDuel _duel;
        private bool _sandbox;
        private Action _detachScreen;
        private EconomyShopModel _shopModel;
        private EconomyDraftModel _draftModel;
        private IDisposable _smokeHold;
        private DuelResult _pendingResult;
        private float _endDelay = -1f;
        private bool _menuOpen;
        private bool _inspectOpen;
        private bool _captureCursor;

        public LabView View { get; private set; }
        public LaboratorySession Session => _session;
        public OfflineDuel Duel => _duel;
        public ArenaRenderer Arena => _renderer;
        public LaboratoryConfig Config => _config;

        public static LaboratoryController Create(ClientContext context, Action onExit)
        {
            var go = new GameObject("Laboratory");
            LaboratoryController lab = go.AddComponent<LaboratoryController>();
            lab._context = context;
            lab._catalogue = context.Catalogue.Catalogue;
            lab._onExit = onExit;
            lab._captureCursor = !context.Options.IsSmokeTest;
            lab._config = ConfigFrom(context.Options, lab._catalogue);
            lab._generation = new ArenaGenerationService(lab._catalogue);
            lab._generation.Completed += lab.OnGenerated;
            lab._previewInput = new PreviewInput();
            lab._previewInput.Enable();
            lab.CreateBackdrop();
            lab.ApplyStartOptions(context.Options);
            return lab;
        }

        /// <summary>The configuration the command line asks for (-labWizard, -labOpponent, -arenaSeed, -labRound).</summary>
        public static LaboratoryConfig ConfigFrom(CommandLineOptions options, Catalogue catalogue)
        {
            var config = new LaboratoryConfig { MatchSeed = options.ArenaSeed, StartRound = options.LabRound };
            if (options.LabWizard != null && catalogue.WizardsById.ContainsKey(options.LabWizard))
            {
                config.PlayerWizardId = options.LabWizard;
            }

            if (options.LabOpponent != null && catalogue.WizardsById.ContainsKey(options.LabOpponent))
            {
                config.OpponentWizardId = options.LabOpponent;
            }

            if (options.LabDemo)
            {
                config.Dummy = DummyMode.Guard; // the demonstration shows guarded and clean hits
            }

            return config;
        }

        // ---- entry points --------------------------------------------------------------------------------------

        public void ShowSetup()
        {
            DestroyDuel();
            _session = null;
            View = LabView.Setup;
            ShowOverview(false);
            ShowScreen(LaboratorySetupScreen.Build(_catalogue, _config, BeginSandbox, BeginRounds, () => _onExit?.Invoke()), null);
        }

        /// <summary>Sandbox: spar with the dummy at once (no shop or draft), restarting after each duel.</summary>
        public void BeginSandbox()
        {
            _sandbox = true;
            _session = new LaboratorySession(_catalogue, _config);
            _session.SkipToDuel();
            StartDuel();
        }

        /// <summary>Full Laboratory rounds from the configured starting round.</summary>
        public void BeginRounds()
        {
            _sandbox = false;
            _session = new LaboratorySession(_catalogue, _config);
            ShowReveal();
        }

        // ---- round flow ----------------------------------------------------------------------------------------

        private void ShowReveal()
        {
            DestroyDuel();
            View = LabView.Reveal;
            RoundRevealModel model = _session.CreateReveal(OnRevealContinue);
            var screen = new RoundRevealScreen(model, LaboratorySession.Label);
            ShowScreen(screen.Root, screen.Detach);
            RequireArena(() =>
            {
                model.SetArena(_arena);
                ShowArenaOverview(_arena.Layout);
                screen.FocusContinue();
            });
        }

        private void OnRevealContinue()
        {
            if (View != LabView.Reveal)
            {
                return;
            }

            ShowShop(_session.OpenShop());
        }

        private void ShowShop(EconomyShopModel model)
        {
            View = LabView.Shop;
            ShowOverview(false);
            _shopModel = model;
            _phaseClock.Reset();
            var screen = new ShopScreen(model, LaboratorySession.Label);
            model.ReadyChanged += OnShopReady;
            ShowScreen(screen.Root, () =>
            {
                model.ReadyChanged -= OnShopReady;
                screen.Detach();
            });
            screen.FocusFirstCard();
        }

        private void OnShopReady(bool ready)
        {
            if (ready)
            {
                EndShop(); // the dummy is always ready, so everyone is (spec L45)
            }
        }

        private void EndShop()
        {
            if (View != LabView.Shop)
            {
                return;
            }

            _session.CloseShop();
            _shopModel = null;
            if (_session.Phase == LabPhase.Draft)
            {
                ShowDraft(_session.DraftModel);
            }
            else
            {
                StartDuel();
            }
        }

        private void ShowDraft(EconomyDraftModel model)
        {
            View = LabView.Draft;
            _draftModel = model;
            _phaseClock.Reset();
            var screen = new DraftScreen(model, LaboratorySession.Label);
            model.Closed += EndDraft;
            ShowScreen(screen.Root, () =>
            {
                model.Closed -= EndDraft;
                screen.Detach();
            });
            screen.Root.Focus();
        }

        private void EndDraft()
        {
            if (View != LabView.Draft)
            {
                return;
            }

            _draftModel.Closed -= EndDraft;
            _session.CloseDraft();
            _draftModel = null;
            StartDuel();
        }

        // ---- duel ----------------------------------------------------------------------------------------------

        private void StartDuel()
        {
            RequireArena(() =>
            {
                DestroyDuel();
                ArenaLayout layout = _sandbox ? SparringLayout.Create(_arena.Layout) : _arena.Layout;
                EnsureRenderer(layout);
                ShowOverview(false);
                var world = new DuelWorld(layout, _session.PlayerSetup(), _session.OpponentSetup());
                _duel = OfflineDuel.Create(transform, _renderer, world, _catalogue, _context.Settings, new TrainingDummyPilot(_config.Dummy),
                    LaboratorySession.Label + (_sandbox ? "  |  sandbox sparring" : "  |  " + _session.RoundTitle), ControlsHint);
                _duel.Ended += OnDuelEnded;
                View = LabView.Duel;
                ShowScreen(null, null);
                _context.Ui.SetHud(_duel.Hud.Root);
                UpdateCursor();
                ApplyDuelOptions(_context.Options);
            });
        }

        private void OnDuelEnded(DuelResult result)
        {
            _pendingResult = result;
            _endDelay = DuelEndPauseSec; // show the final state briefly (spec L49 results period)
        }

        private void FinishDuel()
        {
            _endDelay = -1f;
            DuelResult result = _pendingResult;
            _pendingResult = null;
            if (_sandbox)
            {
                _session = new LaboratorySession(_catalogue, _config);
                _session.SkipToDuel();
                StartDuel(); // sparring restarts with full resources
                return;
            }

            int alcoveTicks = _duel != null ? _duel.World.AlcoveTicks : 0;
            RoundResultsModel results = _session.CompleteDuel(result, alcoveTicks, OnResultsContinue);
            DestroyDuel();
            View = LabView.Results;
            var screen = new ResultsScreen(results, LaboratorySession.Label);
            ShowScreen(screen.Root, null);
            screen.Continue.Focus();
            _smokeHold?.Dispose();
            _smokeHold = null;
        }

        private void OnResultsContinue()
        {
            _session.NextRound();
            if (_session.Phase == LabPhase.MatchOver)
            {
                ShowSetup();
            }
            else
            {
                ShowReveal();
            }
        }

        private void DestroyDuel()
        {
            if (_duel == null)
            {
                return;
            }

            _duel.Ended -= OnDuelEnded;
            Destroy(_duel.gameObject);
            _duel = null;
            _context.Ui.SetHud(null);
            _context.Ui.CloseOverlay();
            _menuOpen = false;
            _inspectOpen = false;
            UpdateCursor();
        }

        // ---- arena ---------------------------------------------------------------------------------------------

        private void RequireArena(Action then)
        {
            ArenaRequest request = _session.ArenaRequest;
            string key = request.MatchSeed + "/" + request.Round;
            if (_arena != null && _arenaKey == key)
            {
                then();
                return;
            }

            _whenArenaReady = then;
            if (_pendingKey == key && _generation.IsRunning)
            {
                return;
            }

            _pendingKey = key;
            _smokeHold?.Dispose();
            _smokeHold = SmokeTestDriver.Hold("Laboratory arena generation");
            _generation.Begin(request);
            if (View == LabView.Setup || View == LabView.Results || View == LabView.Duel)
            {
                View = LabView.Generating;
                ShowScreen(GeneratingScreen(request), null);
            }
        }

        private void OnGenerated(ArenaGenerationService service)
        {
            _smokeHold?.Dispose();
            _smokeHold = null;
            if (service.State != ArenaGenerationState.Done)
            {
                Debug.LogError("[Laboratory] Arena generation failed: " + service.Error);
                return;
            }

            _arena = service.Result;
            _arenaKey = _pendingKey;
            _pendingKey = null;
            Debug.Log("[Laboratory] " + ArenaReportText.Release(_arena.Layout) + ": " + _arena.Layout.Diameter + " m " + _arena.Layout.BiomeId
                + " in " + service.Seconds.ToString("0.0") + " s");
            Action then = _whenArenaReady;
            _whenArenaReady = null;
            then?.Invoke();
        }

        private void EnsureRenderer(ArenaLayout layout)
        {
            if (_renderer == null)
            {
                _renderer = ArenaRenderer.Create(layout, _catalogue, transform);
            }
            else if (!ReferenceEquals(_renderedLayout, layout))
            {
                _renderer.Build(layout, _catalogue);
            }

            _renderedLayout = layout;
        }

        private void ShowArenaOverview(ArenaLayout layout)
        {
            EnsureRenderer(layout);
            _renderer.SetCombatTime(0f);
            Vector3 center = CoreSpace.ToUnity(layout.Center);
            float radius = ArenaQueries.OuterRadius(layout);
            if (_overview == null)
            {
                _overview = ArenaOverviewCamera.Create(transform, center, radius, _previewInput, _context.Settings);
            }
            else
            {
                _overview.Retarget(center, radius);
            }

            // The reveal panel covers the left third; the arena renders in the rest (spec L329 rotatable preview).
            _overview.Camera.rect = new Rect(0.34f, 0f, 0.66f, 1f);
            ShowOverview(true);
        }

        private void ShowOverview(bool show)
        {
            if (_overview == null)
            {
                return;
            }

            _overview.Camera.enabled = show;
            _overview.InputEnabled = show;
            _overview.gameObject.tag = show ? "MainCamera" : "Untagged";
        }

        // ---- per frame -----------------------------------------------------------------------------------------

        private void Update()
        {
            _generation.Poll();
            if (_endDelay >= 0f)
            {
                _endDelay -= Time.unscaledDeltaTime;
                if (_endDelay < 0f)
                {
                    FinishDuel();
                }

                return;
            }

            UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
            bool escape = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
            bool tab = keyboard != null && keyboard.tabKey.wasPressedThisFrame;
            if (View == LabView.Duel)
            {
                if (escape)
                {
                    ToggleMenu();
                }
                else if (tab && !_menuOpen)
                {
                    ToggleInspect();
                }

                return;
            }

            if (escape && !(View == LabView.Draft && _draftModel != null && _draftModel.Step != DraftStep.Choosing))
            {
                ToggleMenu();
                return;
            }

            if (View == LabView.Shop || View == LabView.Draft)
            {
                int ticks = _phaseClock.Advance(Time.unscaledDeltaTime);
                if (_session.AdvanceTimer(ticks))
                {
                    if (View == LabView.Shop)
                    {
                        EndShop(); // spec L355: a timeout commits only completed purchases
                    }
                    else
                    {
                        EndDraft(); // spec L229: the timer expiring chooses nothing
                    }
                }
            }
        }

        // ---- menu and inspection -------------------------------------------------------------------------------

        private void ToggleMenu()
        {
            if (_menuOpen)
            {
                CloseOverlays();
                return;
            }

            _menuOpen = true;
            _inspectOpen = false;
            SetDuelPaused(true);
            VisualElement overlay = Ui.Element("cf-overlay");
            VisualElement panel = Ui.Panel("cf-panel--dialog");
            panel.Add(Ui.Heading("Laboratory menu"));
            panel.Add(Ui.Label(LaboratorySession.Label, "cf-lab-label"));
            if (_duel != null)
            {
                panel.Add(Ui.Muted("The offline duel is paused while this menu is open (online duels never pause, spec L55)."));
            }

            Button resume = Ui.Button("Resume", CloseOverlays, "cf-button--primary");
            panel.Add(resume);
            if (_duel != null)
            {
                panel.Add(Ui.Button("Restart this duel", () =>
                {
                    CloseOverlays();
                    StartDuel();
                }));
                panel.Add(Ui.Button("Dummy behavior: " + _config.Dummy + " (change)", () =>
                {
                    _config.Dummy = (DummyMode)(((int)_config.Dummy + 1) % Enum.GetValues(typeof(DummyMode)).Length);
                    CloseOverlays();
                    StartDuel();
                }));
                if (!_sandbox)
                {
                    panel.Add(Ui.Button("Surrender this round (a normal loss, spec L53)", () =>
                    {
                        _duel.World.Surrender(OfflineDuel.LocalPlayer);
                        CloseOverlays();
                    }));
                }

                panel.Add(Ui.Button("Inspect build (Tab)", () =>
                {
                    CloseOverlays();
                    ToggleInspect();
                }));
            }

            if (_session != null && (View == LabView.Shop || View == LabView.Draft))
            {
                bool paused = _session.ShopTimer != null && _session.ShopTimer.Paused || _session.DraftTimer != null && _session.DraftTimer.Paused;
                panel.Add(Ui.Button(paused ? "Resume phase timers" : "Pause phase timers (Laboratory)", () =>
                {
                    _session.SetTimersPaused(!paused);
                    CloseOverlays();
                }));
            }

            panel.Add(Ui.Button("Back to Laboratory setup", () =>
            {
                CloseOverlays();
                ShowSetup();
            }));
            panel.Add(Ui.Button("Leave the Laboratory", () =>
            {
                CloseOverlays();
                _onExit?.Invoke();
            }));
            overlay.Add(panel);
            _context.Ui.ShowOverlay(overlay);
            resume.Focus();
            UpdateCursor();
        }

        /// <summary>Shows the build panel over the duel (spec L46: players may inspect their loadout in the alcove).</summary>
        public void ToggleInspect()
        {
            if (_inspectOpen)
            {
                CloseOverlays();
                return;
            }

            if (_session == null)
            {
                return;
            }

            _inspectOpen = true;
            SetDuelPaused(true);
            var build = new BuildPanel(new LoadoutBuildModel(_session.Player));
            VisualElement overlay = Ui.Element("cf-overlay");
            VisualElement panel = Ui.Panel("cf-panel--dialog", "cf-panel--inspect");
            panel.Add(Ui.Heading("Build (Tab to close)"));
            panel.Add(build.Root);
            overlay.Add(panel);
            _context.Ui.ShowOverlay(overlay);
            UpdateCursor();
        }

        private void CloseOverlays()
        {
            _menuOpen = false;
            _inspectOpen = false;
            _context.Ui.CloseOverlay();
            SetDuelPaused(false);
            UpdateCursor();
        }

        private void SetDuelPaused(bool paused)
        {
            if (_duel != null)
            {
                _duel.Paused = paused;
            }
        }

        private void UpdateCursor()
        {
            bool locked = _captureCursor && _duel != null && !_menuOpen && !_inspectOpen;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        // ---- screens -------------------------------------------------------------------------------------------

        private void ShowScreen(VisualElement screen, Action detach)
        {
            _detachScreen?.Invoke();
            _detachScreen = detach;
            _context.Ui.ShowScreen(screen);
        }

        private static VisualElement GeneratingScreen(ArenaRequest request)
        {
            VisualElement screen = Ui.Element("cf-screen", "cf-screen--menu", "cf-column");
            screen.Add(Ui.Label(LaboratorySession.Label, "cf-lab-label"));
            screen.Add(Ui.Heading("Generating the round arena"));
            screen.Add(Ui.Muted("Seed " + request.MatchSeed + ", round " + request.Round
                + ": Confluence.Arena validates up to eight attempts, then releases a prevalidated fallback (spec L323)."));
            return screen;
        }

        private void CreateBackdrop()
        {
            var go = new GameObject("Laboratory backdrop camera");
            go.transform.SetParent(transform, false);
            _backdrop = go.AddComponent<Camera>();
            _backdrop.clearFlags = CameraClearFlags.SolidColor;
            _backdrop.backgroundColor = new Color(0.04f, 0.045f, 0.07f);
            _backdrop.cullingMask = 0;
            _backdrop.depth = -10f;
            go.AddComponent<AudioListener>(); // the one listener while the Laboratory runs
        }

        // ---- command-line start (smoke screenshots) ------------------------------------------------------------

        private void ApplyStartOptions(CommandLineOptions options)
        {
            if (!options.Laboratory)
            {
                ShowSetup();
                return;
            }

            switch (options.LabScreen)
            {
                case LabScreen.Reveal:
                    BeginRounds();
                    break;
                case LabScreen.Shop:
                    BeginRounds();
                    RequireArena(() =>
                    {
                        OnRevealContinue();
                        if (options.LabDemo)
                        {
                            DemoPurchase();
                        }
                    });
                    break;
                case LabScreen.Draft:
                    BeginRounds();
                    RequireArena(() =>
                    {
                        OnRevealContinue();
                        if (options.LabDemo)
                        {
                            DemoPurchase();
                        }

                        EndShop();
                    });
                    break;
                case LabScreen.Build:
                case LabScreen.Duel:
                    BeginSandbox();
                    break;
                case LabScreen.Results:
                    _sandbox = false;
                    _session = new LaboratorySession(_catalogue, _config);
                    _session.SkipToDuel();
                    StartDuel();
                    break;
                default:
                    ShowSetup();
                    break;
            }
        }

        // Runs once the duel exists: the demonstration pilot, fast-forward, and the inspection overlay.
        private void ApplyDuelOptions(CommandLineOptions options)
        {
            if (!options.Laboratory || _duel == null)
            {
                return;
            }

            if (options.LabScreen == LabScreen.Results)
            {
                _duel.LocalPilot = new DemoPilot();
                _duel.FastForward(_duel.World.AlcoveTicks + _duel.World.CombatTicks + Ticks.PerSecond);
                _pendingResult = _duel.World.Result;
                FinishDuel();
                return;
            }

            if (options.LabDemo)
            {
                _duel.LocalPilot = new DemoPilot(options.LabDuelTimeSec >= 60f ? 6 : int.MaxValue);
            }

            if (options.LabDuelTimeSec >= 0f)
            {
                _duel.FastForward(_duel.World.AlcoveTicks + Ticks.FromSeconds(options.LabDuelTimeSec));
            }

            if (options.LabScreen == LabScreen.Build)
            {
                ToggleInspect();
            }
        }

        // Buys the first affordable offered card so the screenshot shows a purchase, a filled slot, and folding.
        private void DemoPurchase()
        {
            if (_shopModel == null)
            {
                return;
            }

            for (int i = 0; i < _shopModel.Cards.Count; i++)
            {
                if (_shopModel.Cards[i].Status == ShopCardStatus.Offered && _shopModel.Cards[i].CanBuy)
                {
                    _shopModel.Buy(i);
                    return;
                }
            }
        }

        private void OnDestroy()
        {
            _detachScreen?.Invoke();
            _detachScreen = null;
            _smokeHold?.Dispose();
            _previewInput?.Dispose();
            _previewInput = null;
            if (_context != null)
            {
                _context.Ui.SetHud(null);
                _context.Ui.CloseOverlay();
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
