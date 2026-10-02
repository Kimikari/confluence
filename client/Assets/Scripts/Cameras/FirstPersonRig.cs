using Confluence.Client.Input;
using Confluence.Client.Mapping;
using Confluence.Client.Movement;
using Confluence.Client.Settings;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Sim.Movement;
using Confluence.Sim.World;
using UnityEngine;

namespace Confluence.Client.Cameras
{
    public enum FirstPersonMode
    {
        /// <summary>The Sim CharacterMotor against the layout's CollisionWorld, eye at the motor's eye height.</summary>
        Walk,

        /// <summary>Free-fly debug camera without collision.</summary>
        Fly,
    }

    /// <summary>
    /// First-person camera rig for the arena preview. The root sits at the feet; the camera child sits at the motor's
    /// eye height (GlobalRules.Movement.StandingEyeHeight, DECISIONS D-024). Walk mode steps the shared Sim
    /// <see cref="CharacterMotor"/> through <see cref="LocalMotor"/> at 60 ticks per second and interpolates the camera
    /// between ticks, so the preview moves exactly like the server simulates (spec L80-L82, L312). Look angles are kept
    /// in Core convention (yaw clockwise from +Z, positive pitch up) and converted with
    /// <see cref="CoreSpace.ViewRotation"/>. Settings supply the horizontal FOV (80-110, spec L371), mouse sensitivity,
    /// and Y inversion. The preview walker has unlimited stamina: it inspects geometry and is not a duel.
    /// </summary>
    public sealed class FirstPersonRig : MonoBehaviour
    {
        public const float PitchLimit = 89f;
        public const float FlySpeed = 8f;
        public const float FlySprintMultiplier = 3f;

        private readonly TickClock _clock = new TickClock();
        private PreviewInput _input;
        private ClientSettings _settings;
        private LocalMotor _motor;
        private bool _jumpLatched;
        private bool _dodgeLatched;

        public Camera Camera { get; private set; }

        public FirstPersonMode Mode { get; private set; } = FirstPersonMode.Fly;

        /// <summary>Core yaw in degrees.</summary>
        public float Yaw { get; private set; }

        /// <summary>Core pitch in degrees (positive looks up).</summary>
        public float Pitch { get; private set; }

        /// <summary>When false, input is ignored (overview active, menus open).</summary>
        public bool InputEnabled { get; set; } = true;

        /// <summary>The walk-mode motor (the Sim's CharacterMotor over the layout's CollisionWorld).</summary>
        public LocalMotor Motor => _motor;

        public static FirstPersonRig Create(Transform parent, ArenaLayout layout, PreviewInput input, ClientSettings settings)
        {
            var root = new GameObject("First-person rig");
            root.transform.SetParent(parent, false);
            FirstPersonRig rig = root.AddComponent<FirstPersonRig>();
            rig._input = input;
            rig._settings = settings;

            var cameraGo = new GameObject("Eye camera");
            cameraGo.transform.SetParent(root.transform, false);
            cameraGo.transform.localPosition = new Vector3(0f, GlobalRules.Movement.StandingEyeHeight, 0f);
            rig.Camera = cameraGo.AddComponent<Camera>();
            rig.Camera.nearClipPlane = 0.05f;
            rig.Camera.farClipPlane = 400f;
            rig.Camera.clearFlags = CameraClearFlags.Skybox;
            cameraGo.AddComponent<AudioListener>();

            rig.SetLayout(layout);
            rig.ApplySettings();
            return rig;
        }

        /// <summary>Rebuilds the collision world from a (new) layout and stands the walker on spawn side 0.</summary>
        public void SetLayout(ArenaLayout layout)
        {
            SpawnPoint spawn = layout?.Spawns != null && layout.Spawns.Length > 0 ? layout.Spawns[0] : null;
            Vec3 feet = spawn != null ? spawn.Position : Vec3.Zero;
            float yaw = spawn != null ? spawn.YawDeg : 0f;
            _motor = new LocalMotor(CollisionWorld.FromLayout(layout), feet, yaw);
            _clock.Reset();
            transform.position = CoreSpace.ToUnity(_motor.State.Position);
            SetLook(yaw, 0f);
        }

        public void SetMode(FirstPersonMode mode)
        {
            if (mode == FirstPersonMode.Walk && Mode != FirstPersonMode.Walk)
            {
                // Drop from the current position onto whatever is below; a fall out of the map returns to spawn.
                _motor.Teleport(CoreSpace.ToCore(transform.position), Yaw);
                _clock.Reset();
            }

            Mode = mode;
            Camera.transform.localPosition = new Vector3(0f, GlobalRules.Movement.StandingEyeHeight, 0f);
        }

        /// <summary>Places the rig (feet position) and look angles in Core convention.</summary>
        public void SetPose(Vector3 feet, float yawDeg, float pitchDeg)
        {
            transform.position = feet;
            _motor.Teleport(CoreSpace.ToCore(feet), yawDeg);
            SetLook(yawDeg, pitchDeg);
        }

        public void SetLook(float yawDeg, float pitchDeg)
        {
            Yaw = Mathf.Repeat(yawDeg, 360f);
            Pitch = Mathf.Clamp(pitchDeg, -PitchLimit, PitchLimit);
            Camera.transform.rotation = CoreSpace.ViewRotation(Yaw, Pitch);
        }

        public void ApplySettings()
        {
            if (_settings == null || Camera == null)
            {
                return;
            }

            float aspect = Camera.aspect > 0f ? Camera.aspect : 16f / 9f;
            Camera.fieldOfView = Camera.HorizontalToVerticalFieldOfView(_settings.FieldOfView, aspect);
        }

        private void Update()
        {
            ApplySettings(); // aspect can change with the window
            if (!InputEnabled || _input == null)
            {
                _clock.Reset();
                return;
            }

            Vector2 look = _input.LookDelta * _settings.LookDegreesPerCount;
            float pitchSign = _settings.InvertY ? -1f : 1f;
            SetLook(Yaw + look.x, Pitch + look.y * pitchSign);

            if (Mode == FirstPersonMode.Fly)
            {
                Fly(_input.MoveValue, Time.deltaTime);
            }
            else
            {
                Walk(Time.deltaTime);
            }
        }

        private void Fly(Vector2 move, float dt)
        {
            Vector3 forward = Camera.transform.forward;
            Vector3 right = CoreSpace.YawRotation(Yaw) * Vector3.right;
            float vertical = (_input.Jump.IsPressed() ? 1f : 0f) - (_input.Descend.IsPressed() ? 1f : 0f);
            Vector3 velocity = (forward * move.y + right * move.x + Vector3.up * vertical) * FlySpeed;
            if (_input.Sprint.IsPressed())
            {
                velocity *= FlySprintMultiplier;
            }

            transform.position += velocity * dt;
        }

        // Presses are latched until the next tick consumes them, so a tap shorter than a tick still counts.
        private void Walk(float dt)
        {
            _jumpLatched |= _input.Jump.WasPressedThisFrame();
            _dodgeLatched |= _input.Dodge.WasPressedThisFrame();
            int ticks = _clock.Advance(dt);
            for (int i = 0; i < ticks; i++)
            {
                MotorCommand command = MotorCommand.Default; // unlimited stamina, no statuses, no casting
                Vector2 move = _input.MoveValue;
                command.Move = new Vec2(move.x, move.y);
                command.YawDeg = Yaw;
                command.PitchDeg = Pitch;
                command.JumpPressed = _jumpLatched;
                command.DodgePressed = _dodgeLatched;
                command.SprintHeld = _input.Sprint.IsPressed();
                command.CrouchHeld = _input.Descend.IsPressed();
                _motor.Step(command);
                _jumpLatched = false;
                _dodgeLatched = false;
            }

            float alpha = _clock.Alpha;
            transform.position = CoreSpace.ToUnity(_motor.InterpolatedFeet(alpha));
            Camera.transform.localPosition = new Vector3(0f, _motor.InterpolatedEyeHeight(alpha), 0f);
        }
    }
}
