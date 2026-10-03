using System;
using Confluence.Client.Arena;
using Confluence.Client.Input;
using Confluence.Client.Laboratory;
using Confluence.Client.Mapping;
using Confluence.Client.Movement;
using Confluence.Client.Settings;
using Confluence.Client.UI;
using Confluence.Client.ViewModels;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Players;
using UnityEngine;
using PlayerInput = Confluence.Sim.Input.PlayerInput;

namespace Confluence.Client.Presentation
{
    /// <summary>
    /// A local, authoritative Sim <see cref="DuelWorld"/> played in first person: the offline Laboratory sandbox. Each
    /// rendered frame applies mouse look, turns frame time into whole 60 Hz ticks (<see cref="TickClock"/>), steps the
    /// world with the player's <see cref="DuelInput"/> command (spec L84 bindings) and the opponent pilot's command,
    /// and presents the result: interpolated fighters, guard and melee state, confirmed-hit damage numbers, owned
    /// fields and summons, destroyed and temporary solids, the boundary clock, and the HUD view model. The camera sits
    /// at the motor's interpolated eye, so walking here is exactly the server's CharacterMotor (DECISIONS D-003).
    /// </summary>
    public sealed class OfflineDuel : MonoBehaviour
    {
        public const int LocalPlayer = 0;
        public const float PitchLimit = 89f;

        private readonly TickClock _clock = new TickClock();
        private readonly Vector3[] _previousFeet = new Vector3[DuelWorld.PlayerCount];
        private readonly Vector3[] _currentFeet = new Vector3[DuelWorld.PlayerCount];
        private readonly float[] _previousEye = new float[DuelWorld.PlayerCount];
        private readonly float[] _currentEye = new float[DuelWorld.PlayerCount];
        private readonly FighterView[] _fighters = new FighterView[DuelWorld.PlayerCount];
        private ArenaRenderer _arena;
        private ClientSettings _settings;
        private DuelInput _input;
        private IDuelPilot _opponent;
        private OwnedEntityViews _entities;
        private DamageNumbers _numbers;
        private Transform _viewFocus;
        private bool _ended;

        public DuelWorld World { get; private set; }
        public Camera Camera { get; private set; }
        public DuelHudModel HudModel { get; private set; }
        public DuelHud Hud { get; private set; }

        /// <summary>When set, drives the local player instead of the keyboard (demonstrations, tests).</summary>
        public IDuelPilot LocalPilot { get; set; }

        /// <summary>Look and input are ignored and the world does not advance (menus open).</summary>
        public bool Paused { get; set; }

        /// <summary>Core yaw/pitch of the local view (client-side look, sent with every command).</summary>
        public float Yaw { get; private set; }

        public float Pitch { get; private set; }

        public int DamageNumbersShown { get; private set; }

        public OwnedEntityViews Entities => _entities;

        public DamageNumbers Numbers => _numbers;

        /// <summary>Text of the most recent damage number, or null before the first confirmed hit.</summary>
        public string LastDamageNumber { get; private set; }

        public FighterView Fighter(int index) => _fighters[index];

        /// <summary>One line of state for smoke logs: clock, health, hits, numbers on screen, guard state.</summary>
        public string Describe()
        {
            DuelPlayer me = World.Player(LocalPlayer);
            DuelPlayer other = World.Player(1 - LocalPlayer);
            return "t=" + World.CombatSeconds.ToString("0.0") + " s, phase " + World.Phase + ", health " + me.Resources.Health.ToString("0") + "/"
                + other.Resources.Health.ToString("0") + ", distance " + Core.Geometry.Vec3.DistanceXZ(me.Position, other.Position).ToString("0.00")
                + " m, damage numbers " + DamageNumbersShown + " (" + _numbers.ActiveCount + " on screen, last '" + LastDamageNumber + "'), opponent guard shown " + _fighters[1 - LocalPlayer].GuardShown
                + ", melee " + me.Melee.Phase;
        }

        /// <summary>Raised once when the duel resolves (spec L49).</summary>
        public event Action<DuelResult> Ended;

        public static OfflineDuel Create(Transform parent, ArenaRenderer arena, DuelWorld world, Catalogue catalogue, ClientSettings settings,
            IDuelPilot opponent, string sessionLabel, string controlsHint)
        {
            var go = new GameObject("Offline duel");
            go.transform.SetParent(parent, false);
            OfflineDuel duel = go.AddComponent<OfflineDuel>();
            duel._arena = arena;
            duel._settings = settings;
            duel._opponent = opponent;
            duel.World = world;
            duel._input = new DuelInput();
            duel._input.Enable();
            duel.HudModel = new DuelHudModel(catalogue, LocalPlayer);
            duel.Hud = new DuelHud(duel.HudModel, sessionLabel, controlsHint);
            duel._numbers = new DamageNumbers(duel.Hud.DamageLayer);
            ArenaMaterialLibrary materials = arena.Materials;
            for (int i = 0; i < DuelWorld.PlayerCount; i++)
            {
                duel._fighters[i] = FighterView.Create(go.transform, i, materials, firstPerson: i == LocalPlayer);
            }

            duel._entities = new OwnedEntityViews(go.transform, materials);
            duel.CreateCamera(materials);
            DuelPlayer local = world.Player(LocalPlayer);
            duel.Yaw = local.Motor.YawDeg;
            duel.Pitch = 0f;
            duel.SnapPresentation();
            duel.Present(1f, 0f);
            return duel;
        }

        /// <summary>Steps the world without rendering (fast-forward for tests and demonstrations).</summary>
        public void FastForward(int ticks)
        {
            for (int i = 0; i < ticks && !World.IsOver; i++)
            {
                StepTick();
                _numbers.Age(Confluence.Core.Time.Ticks.Dt);
            }

            SnapPresentation();
            Present(1f, 0f);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (Paused)
            {
                // The world holds still, but the view keeps presenting it (zero elapsed time) so numbers projected before
                // the HUD had a layout appear, and nothing ages behind the menu.
                _clock.Reset();
                Present(1f, 0f);
                return;
            }

            _input.Update();
            if (LocalPilot == null)
            {
                Vector2 look = _input.LookDelta * _settings.LookDegreesPerCount;
                float pitchSign = _settings.InvertY ? -1f : 1f;
                Yaw = Mathf.Repeat(Yaw + look.x, 360f);
                Pitch = Mathf.Clamp(Pitch + look.y * pitchSign, -PitchLimit, PitchLimit);
            }

            int ticks = _clock.Advance(dt);
            for (int i = 0; i < ticks; i++)
            {
                StepTick();
            }

            Present(_clock.Alpha, dt);
        }

        private void StepTick()
        {
            for (int i = 0; i < DuelWorld.PlayerCount; i++)
            {
                _previousFeet[i] = _currentFeet[i];
                _previousEye[i] = _currentEye[i];
            }

            PlayerInput local;
            if (LocalPilot != null)
            {
                local = LocalPilot.Next(World, LocalPlayer);
                Yaw = local.YawDeg;
                Pitch = local.PitchDeg;
            }
            else
            {
                local = _input.Sample(World.Tick + 1, Yaw, Pitch);
            }

            PlayerInput other = _opponent != null ? _opponent.Next(World, 1 - LocalPlayer) : PlayerInput.Idle(World.Player(1 - LocalPlayer).Motor.YawDeg);
            World.Step(local, other);
            for (int i = 0; i < DuelWorld.PlayerCount; i++)
            {
                DuelPlayer p = World.Player(i);
                _currentFeet[i] = CoreSpace.ToUnity(p.Position);
                _currentEye[i] = p.Motor.EyeHeight;
            }

            for (int i = 0; i < World.TickEvents.Count; i++)
            {
                if (DamageNumbers.IsConfirmedHit(World.TickEvents[i], LocalPlayer, out DamageEvent hit))
                {
                    _numbers.Spawn(hit);
                    DamageNumbersShown++;
                    LastDamageNumber = DamageNumbers.Text(hit);
                }
            }

            if (World.IsOver && !_ended)
            {
                _ended = true;
                Ended?.Invoke(World.Result);
            }
        }

        private void SnapPresentation()
        {
            for (int i = 0; i < DuelWorld.PlayerCount; i++)
            {
                DuelPlayer p = World.Player(i);
                _currentFeet[i] = _previousFeet[i] = CoreSpace.ToUnity(p.Position);
                _currentEye[i] = _previousEye[i] = p.Motor.EyeHeight;
            }
        }

        private void Present(float alpha, float dt)
        {
            for (int i = 0; i < DuelWorld.PlayerCount; i++)
            {
                DuelPlayer p = World.Player(i);
                Vector3 feet = Vector3.Lerp(_previousFeet[i], _currentFeet[i], alpha);
                float yaw = i == LocalPlayer ? Yaw : p.Motor.YawDeg;
                _fighters[i].Present(p, feet, yaw, World.Tick);
            }

            Vector3 localFeet = Vector3.Lerp(_previousFeet[LocalPlayer], _currentFeet[LocalPlayer], alpha);
            float eye = Mathf.Lerp(_previousEye[LocalPlayer], _currentEye[LocalPlayer], alpha);
            Camera.transform.SetPositionAndRotation(localFeet + Vector3.up * eye, CoreSpace.ViewRotation(Yaw, Pitch));
            float aspect = Camera.aspect > 0f ? Camera.aspect : 16f / 9f;
            Camera.fieldOfView = Camera.HorizontalToVerticalFieldOfView(_settings.FieldOfView, aspect);
            PresentViewFocus(World.Player(LocalPlayer));

            // The arena clock: negative during the alcove (wards up), then combat seconds (boundary, final cover).
            float clock = World.Phase == DuelPhase.Alcove
                ? -(World.AlcoveTicks - World.Tick) / (float)Confluence.Core.Time.Ticks.PerSecond
                : World.CombatSeconds;
            _arena.SetCombatTime(clock);
            _arena.SyncSolids(World.Collision);
            _entities.Sync(World);
            _numbers.Update(Camera, dt);
            HudModel.Refresh(World);
            Hud.Refresh();
        }

        // First-person focus: lowered at rest, raised across the view while guarding, thrust forward on a strike.
        private void PresentViewFocus(DuelPlayer p)
        {
            Vector3 position = new Vector3(0.3f, -0.32f, 0.62f);
            Quaternion rotation = Quaternion.Euler(55f, -12f, 0f);
            if (p.Guard.Active)
            {
                position = new Vector3(0.05f, -0.12f, 0.5f);
                rotation = Quaternion.Euler(10f, 0f, 75f);
            }
            else if (p.Melee.Phase == MeleePhase.Windup)
            {
                position = new Vector3(0.3f, -0.22f, 0.38f);
                rotation = Quaternion.Euler(80f, -5f, 0f);
            }
            else if (p.Melee.Phase == MeleePhase.Strike)
            {
                position = new Vector3(0.12f, -0.18f, 0.85f);
                rotation = Quaternion.Euler(88f, 0f, 0f);
            }

            _viewFocus.localPosition = Vector3.Lerp(_viewFocus.localPosition, position, 0.5f);
            _viewFocus.localRotation = Quaternion.Slerp(_viewFocus.localRotation, rotation, 0.5f);
            _viewFocus.gameObject.SetActive(p.Alive);
        }

        private void CreateCamera(ArenaMaterialLibrary materials)
        {
            var cameraGo = new GameObject("Duel camera") { tag = "MainCamera" };
            cameraGo.transform.SetParent(transform, false);
            Camera = cameraGo.AddComponent<Camera>();
            Camera.nearClipPlane = 0.03f;
            Camera.farClipPlane = 400f;
            Camera.clearFlags = CameraClearFlags.Skybox;

            GameObject focus = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            focus.name = "View focus";
            ArenaMaterialLibrary.DestroyObject(focus.GetComponent<Collider>());
            focus.transform.SetParent(cameraGo.transform, false);
            focus.transform.localScale = new Vector3(0.028f, 0.24f, 0.028f);
            focus.GetComponent<MeshRenderer>().sharedMaterial = materials.Flat(new Color(0.45f, 0.3f, 0.18f), SurfaceKind.Wood);
            focus.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _viewFocus = focus.transform;
            _viewFocus.localPosition = new Vector3(0.3f, -0.32f, 0.62f);
            _viewFocus.localRotation = Quaternion.Euler(55f, -12f, 0f);
        }

        private void OnDestroy()
        {
            _input?.Dispose();
            _input = null;
            _numbers?.Clear();
            _entities?.Clear();
        }
    }
}
