using System;
using Confluence.Arena.Generation;
using Confluence.Client.Arena;
using Confluence.Client.Cameras;
using Confluence.Client.Input;
using Confluence.Client.Mapping;
using Confluence.Client.Presentation;
using Confluence.Client.Settings;
using Confluence.Client.UI;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using UnityEngine;

namespace Confluence.Client.App
{
    /// <summary>
    /// The ArenaPreview state: generates an arena with Confluence.Arena's seeded generator (seed, round, diameter,
    /// and biome pickers; spec §7), renders it, and lets the player inspect it with the first-person rig (walking
    /// with the Sim CharacterMotor, or flying) or the rotatable overview camera, while a combat clock drives the
    /// boundary contour, the final-cover schedule, and the arena's own environment (<see cref="ArenaEnvironment"/>, drawn
    /// by <see cref="ReactionViews"/>). Regenerating rebuilds the renderer, the walker's collision world, and the
    /// environment. Owns everything it creates and releases it on destroy.
    /// </summary>
    public sealed class ArenaPreviewController : MonoBehaviour
    {
        public const float ScrubStepSec = 5f;

        private PreviewInput _input;
        private UiShell _ui;
        private ArenaPreviewHud _hud;
        private ArenaGenerationService _generation;
        private Catalogue _catalogue;
        private ClientSettings _settings;
        private Action _onExit;
        private PreviewView _startView;
        private IDisposable _smokeHold;
        private bool _captureCursor;
        private bool _overview;
        private bool _generatorOpen;
        private float _hudRefresh;
        private ReactionViews _reactionViews;

        public ArenaRenderer Arena { get; private set; }
        public FirstPersonRig Rig { get; private set; }
        public ArenaOverviewCamera Overview { get; private set; }
        public GeneratedArena Generated { get; private set; }
        public float CombatTimeSec { get; private set; }
        public bool ClockPaused { get; private set; }

        /// <summary>The generated arena's material world over the preview clock, or null before the first arena.</summary>
        public ArenaEnvironment Environment { get; private set; }

        public ReactionViews ReactionViews => _reactionViews;

        public static ArenaPreviewController Create(Catalogue catalogue, ClientSettings settings, UiShell ui, ArenaRequest request,
            PreviewView view, float startTimeSec, bool captureCursor, Action onExit)
        {
            var go = new GameObject("Arena preview");
            ArenaPreviewController preview = go.AddComponent<ArenaPreviewController>();
            preview._catalogue = catalogue;
            preview._settings = settings;
            preview._ui = ui;
            preview._onExit = onExit;
            preview._captureCursor = captureCursor;
            preview._startView = view;
            preview._input = new PreviewInput();
            preview._input.Enable();
            preview.CombatTimeSec = Mathf.Clamp(startTimeSec, ArenaRenderer.AlcoveStartSec, GlobalRules.Round.CombatMaxSec);

            var generator = new ArenaGeneratorPanel(catalogue, request, preview.Regenerate);
            preview._hud = new ArenaPreviewHud(catalogue, generator);
            ui?.SetHud(preview._hud.Root);
            preview._generation = new ArenaGenerationService(catalogue);
            preview._generation.Completed += preview.OnGenerated;
            preview.Regenerate(request);
            return preview;
        }

        /// <summary>Starts generating a new arena; the current one stays until the new one is released.</summary>
        public void Regenerate(ArenaRequest request)
        {
            _smokeHold?.Dispose();
            _smokeHold = SmokeTestDriver.Hold("arena generation");
            _generation.Begin(request);
            _hud.Generator.SetStatus(_generation);
        }

        public void ShowOverview(bool overview)
        {
            _overview = overview;
            if (Rig == null)
            {
                return;
            }

            Rig.Camera.enabled = !overview;
            Overview.Camera.enabled = overview;
            Overview.InputEnabled = overview;
            Rig.Camera.gameObject.tag = overview ? "Untagged" : "MainCamera";
            Overview.gameObject.tag = overview ? "MainCamera" : "Untagged";
            UpdateInteraction();
        }

        private void OnGenerated(ArenaGenerationService service)
        {
            _hud.Generator.SetStatus(service);
            if (service.State != ArenaGenerationState.Done)
            {
                Debug.LogError("[Preview] Arena generation failed: " + service.Error);
                _smokeHold?.Dispose();
                return;
            }

            Generated = service.Result;
            ArenaLayout layout = Generated.Layout;
            Debug.Log("[Preview] " + ArenaReportText.Release(layout) + ": " + layout.Diameter + " m " + layout.BiomeId + " in "
                + service.Seconds.ToString("0.0") + " s");
            bool first = Arena == null;
            if (first)
            {
                Arena = ArenaRenderer.Create(layout, _catalogue, transform);
                Rig = FirstPersonRig.Create(transform, layout, _input, _settings);
                Overview = ArenaOverviewCamera.Create(transform, CoreSpace.ToUnity(layout.Center), ArenaQueries.OuterRadius(layout), _input, _settings);
            }
            else
            {
                Arena.Build(layout, _catalogue);
                Rig.SetLayout(layout);
                Overview.Retarget(CoreSpace.ToUnity(layout.Center), ArenaQueries.OuterRadius(layout));
            }

            Arena.SetCombatTime(CombatTimeSec);
            Environment = new ArenaEnvironment(layout);
            _reactionViews?.Clear();
            _reactionViews = new ReactionViews(transform, Arena.Materials, layout);
            SyncEnvironment();
            _hud.Bind(Arena, Generated);
            if (first)
            {
                ApplyStartView(_startView);
            }
            else if (Rig.Mode == FirstPersonMode.Fly)
            {
                PlaceEstablishingShot();
            }

            RefreshHud();
            _smokeHold?.Dispose();
            _smokeHold = null;
        }

        private void ApplyStartView(PreviewView view)
        {
            switch (view)
            {
                case PreviewView.Overview:
                    ShowOverview(true);
                    break;
                case PreviewView.Walk:
                    Rig.SetMode(FirstPersonMode.Walk);
                    ShowOverview(false);
                    break;
                default:
                    PlaceEstablishingShot();
                    Rig.SetMode(FirstPersonMode.Fly);
                    ShowOverview(false);
                    break;
            }
        }

        // Fly camera above and behind spawn side 0, looking at the center floor.
        private void PlaceEstablishingShot()
        {
            ArenaLayout layout = Arena.Layout;
            SpawnPoint spawn = layout.Spawns != null && layout.Spawns.Length > 0 ? layout.Spawns[0] : null;
            Vector3 center = CoreSpace.ToUnity(layout.Center);
            Vector3 spawnFeet = spawn != null ? CoreSpace.ToUnity(spawn.Position) : center + Vector3.back * 10f;
            Vector3 away = spawnFeet - center;
            away.y = 0f;
            away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.back;
            Vector3 feet = spawnFeet + away * 2f + Vector3.up * 8.5f;
            Vector3 eye = feet + Vector3.up * GlobalRules.Movement.StandingEyeHeight;
            Vector3 look = center + Vector3.up * 0.5f - eye;
            float yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg;
            Rig.SetPose(feet, yaw, pitch);
        }

        private void Update()
        {
            _generation.Poll();
            if (_input.Back.WasPressedThisFrame())
            {
                if (_generatorOpen && !_overview)
                {
                    SetGeneratorOpen(false);
                    return;
                }

                _onExit?.Invoke();
                return;
            }

            if (_input.ToggleGenerator.WasPressedThisFrame())
            {
                SetGeneratorOpen(!_generatorOpen);
            }

            if (Arena == null)
            {
                return;
            }

            if (_input.ToggleOverview.WasPressedThisFrame())
            {
                ShowOverview(!_overview);
            }

            if (!_overview && !_generatorOpen && _input.ToggleFly.WasPressedThisFrame())
            {
                Rig.SetMode(Rig.Mode == FirstPersonMode.Fly ? FirstPersonMode.Walk : FirstPersonMode.Fly);
                RefreshHud();
            }

            UpdateClock();
            _hudRefresh -= Time.unscaledDeltaTime;
            if (_hudRefresh <= 0f)
            {
                RefreshHud();
            }
        }

        private void SetGeneratorOpen(bool open)
        {
            _generatorOpen = open;
            UpdateInteraction();
            if (open)
            {
                _hud.Generator.FocusGenerate();
            }
        }

        // The generator panel is always shown with the free-cursor overview, and on demand (G) in first person.
        private void UpdateInteraction()
        {
            _hud.ShowGenerator(_overview || _generatorOpen);
            if (Rig != null)
            {
                Rig.InputEnabled = !_overview && !_generatorOpen;
            }

            bool locked = _captureCursor && !_overview && !_generatorOpen;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
            RefreshHud();
        }

        private void UpdateClock()
        {
            if (_input.TimePause.WasPressedThisFrame())
            {
                ClockPaused = !ClockPaused;
            }

            float time = CombatTimeSec;
            if (_input.TimeReset.WasPressedThisFrame())
            {
                time = ArenaRenderer.AlcoveStartSec;
            }

            if (_input.TimeBack.WasPressedThisFrame())
            {
                time -= ScrubStepSec;
            }

            if (_input.TimeForward.WasPressedThisFrame())
            {
                time += ScrubStepSec;
            }

            if (!ClockPaused)
            {
                time += Time.deltaTime;
            }

            // Spec L46-L47: a 5 s alcove phase (negative clock), then at most 90 s of combat; the preview clock loops.
            if (time > GlobalRules.Round.CombatMaxSec)
            {
                time = ArenaRenderer.AlcoveStartSec;
            }

            CombatTimeSec = Mathf.Max(ArenaRenderer.AlcoveStartSec, time);
            Arena.SetCombatTime(CombatTimeSec);
            SyncEnvironment();
        }

        private void SyncEnvironment()
        {
            if (Environment == null)
            {
                return;
            }

            Environment.AdvanceTo(CombatTimeSec);
            _reactionViews.Sync(Environment.World);
        }

        private void RefreshHud()
        {
            if (_hud == null || Arena == null)
            {
                return;
            }

            _hudRefresh = 0.1f;
            string mode = _overview ? "overview (rotatable top-down)"
                : Rig.Mode == FirstPersonMode.Walk ? "walk (Sim CharacterMotor, unlimited stamina)" : "free fly";
            _hud.SetMode(mode, !_overview && !_generatorOpen);
            float minRadius = Arena.Layout.Boundary != null ? Arena.Layout.Boundary.MinRadiusAt(CombatTimeSec) : 0f;
            _hud.SetClock(CombatTimeSec, ClockPaused, minRadius);
            _hud.SetEnvironment(Environment != null ? Environment.Summary() : string.Empty);
        }

        private void OnDestroy()
        {
            _reactionViews?.Clear();
            _smokeHold?.Dispose();
            _input?.Dispose();
            _input = null;
            if (_ui != null)
            {
                _ui.SetHud(null);
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
