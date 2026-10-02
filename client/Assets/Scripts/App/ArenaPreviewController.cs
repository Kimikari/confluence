using System;
using Confluence.Client.Arena;
using Confluence.Client.Cameras;
using Confluence.Client.Input;
using Confluence.Client.Mapping;
using Confluence.Client.Settings;
using Confluence.Client.UI;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using UnityEngine;

namespace Confluence.Client.App
{
    /// <summary>
    /// The ArenaPreview state: renders a layout and lets the player inspect it with the first-person rig (walk
    /// or fly) or the rotatable overview camera, while a combat clock drives the boundary contour and the
    /// final-cover schedule. Owns everything it creates and releases it on destroy.
    /// </summary>
    public sealed class ArenaPreviewController : MonoBehaviour
    {
        public const float ScrubStepSec = 5f;

        private PreviewInput _input;
        private UiShell _ui;
        private ArenaPreviewHud _hud;
        private Action _onExit;
        private bool _captureCursor;
        private bool _overview;
        private float _hudRefresh;

        public ArenaRenderer Arena { get; private set; }
        public FirstPersonRig Rig { get; private set; }
        public ArenaOverviewCamera Overview { get; private set; }
        public float CombatTimeSec { get; private set; }
        public bool ClockPaused { get; private set; }

        public static ArenaPreviewController Create(ArenaLayout layout, Catalogue catalogue, ClientSettings settings, UiShell ui,
            PreviewView view, float startTimeSec, bool captureCursor, Action onExit)
        {
            var go = new GameObject("Arena preview");
            ArenaPreviewController preview = go.AddComponent<ArenaPreviewController>();
            preview._ui = ui;
            preview._onExit = onExit;
            preview._captureCursor = captureCursor;
            preview._input = new PreviewInput();
            preview._input.Enable();

            preview.Arena = ArenaRenderer.Create(layout, catalogue, go.transform);
            preview.Rig = FirstPersonRig.Create(go.transform, layout, preview._input, settings);
            Vector3 center = CoreSpace.ToUnity(layout.Center);
            preview.Overview = ArenaOverviewCamera.Create(go.transform, center, ArenaQueries.OuterRadius(layout), preview._input, settings);

            preview._hud = new ArenaPreviewHud(preview.Arena, catalogue);
            ui?.SetHud(preview._hud.Root);

            preview.CombatTimeSec = Mathf.Clamp(startTimeSec, 0f, GlobalRules.Round.CombatMaxSec);
            preview.Arena.SetCombatTime(preview.CombatTimeSec);
            preview.ApplyStartView(view);
            return preview;
        }

        public void ShowOverview(bool overview)
        {
            _overview = overview;
            Rig.Camera.enabled = !overview;
            Rig.InputEnabled = !overview;
            Overview.Camera.enabled = overview;
            Overview.InputEnabled = overview;
            Rig.Camera.gameObject.tag = overview ? "Untagged" : "MainCamera";
            Overview.gameObject.tag = overview ? "MainCamera" : "Untagged";
            UpdateCursor();
            RefreshHud();
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
            if (_input.Back.WasPressedThisFrame())
            {
                _onExit?.Invoke();
                return;
            }

            if (_input.ToggleOverview.WasPressedThisFrame())
            {
                ShowOverview(!_overview);
            }

            if (!_overview && _input.ToggleFly.WasPressedThisFrame())
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

        private void UpdateClock()
        {
            if (_input.TimePause.WasPressedThisFrame())
            {
                ClockPaused = !ClockPaused;
            }

            float time = CombatTimeSec;
            if (_input.TimeReset.WasPressedThisFrame())
            {
                time = 0f;
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

            // Spec L47: combat lasts at most 90 s; the preview clock loops.
            if (time > GlobalRules.Round.CombatMaxSec)
            {
                time = 0f;
            }

            CombatTimeSec = Mathf.Max(0f, time);
            Arena.SetCombatTime(CombatTimeSec);
        }

        private void RefreshHud()
        {
            if (_hud == null)
            {
                return;
            }

            _hudRefresh = 0.1f;
            string mode = _overview ? "overview (rotatable top-down)" : Rig.Mode == FirstPersonMode.Walk ? "walk (preview motor)" : "free fly";
            _hud.SetMode(mode, !_overview);
            float minRadius = Arena.Layout.Boundary != null ? Arena.Layout.Boundary.MinRadiusAt(CombatTimeSec) : 0f;
            _hud.SetClock(CombatTimeSec, ClockPaused, minRadius);
        }

        private void UpdateCursor()
        {
            bool locked = _captureCursor && !_overview;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void OnDestroy()
        {
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
