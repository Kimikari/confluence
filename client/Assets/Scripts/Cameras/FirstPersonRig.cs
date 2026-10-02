using Confluence.Client.Input;
using Confluence.Client.Mapping;
using Confluence.Client.Settings;
using Confluence.Core.Arena;
using Confluence.Core.Rules;
using UnityEngine;

namespace Confluence.Client.Cameras
{
    public enum FirstPersonMode
    {
        /// <summary>Gravity and solids via <see cref="PreviewWalker"/>, eye at the standing eye height.</summary>
        Walk,

        /// <summary>Free-fly debug camera without collision.</summary>
        Fly,
    }

    /// <summary>
    /// First-person camera rig for the arena preview. The root sits at the feet; the camera child sits at
    /// GlobalRules.Movement.StandingEyeHeight (DECISIONS D-024). Look angles are kept in Core convention (yaw
    /// clockwise from +Z, positive pitch up) and converted with <see cref="CoreSpace.ViewRotation"/>, which
    /// negates pitch for Unity's euler X. Settings supply the horizontal FOV (80-110, spec L371), mouse
    /// sensitivity, and Y inversion.
    /// </summary>
    public sealed class FirstPersonRig : MonoBehaviour
    {
        public const float PitchLimit = 89f;
        public const float FlySpeed = 8f;
        public const float FlySprintMultiplier = 3f;

        private PreviewInput _input;
        private ClientSettings _settings;
        private PreviewWalker _walker;

        public Camera Camera { get; private set; }

        public FirstPersonMode Mode { get; private set; } = FirstPersonMode.Fly;

        /// <summary>Core yaw in degrees.</summary>
        public float Yaw { get; private set; }

        /// <summary>Core pitch in degrees (positive looks up).</summary>
        public float Pitch { get; private set; }

        /// <summary>When false, input is ignored (overview active, menus open).</summary>
        public bool InputEnabled { get; set; } = true;

        public PreviewWalker Walker => _walker;

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

            SpawnPoint spawn = layout.Spawns != null && layout.Spawns.Length > 0 ? layout.Spawns[0] : null;
            Vector3 feet = spawn != null ? CoreSpace.ToUnity(spawn.Position) : Vector3.zero;
            rig._walker = new PreviewWalker(layout, feet);
            rig.transform.position = feet;
            rig.SetLook(spawn != null ? spawn.YawDeg : 0f, 0f);
            rig.ApplySettings();
            return rig;
        }

        public void SetMode(FirstPersonMode mode)
        {
            if (mode == FirstPersonMode.Walk && Mode != FirstPersonMode.Walk)
            {
                // Drop from the current position onto whatever is below.
                _walker.Teleport(transform.position);
            }

            Mode = mode;
        }

        /// <summary>Places the rig (feet position) and look angles in Core convention.</summary>
        public void SetPose(Vector3 feet, float yawDeg, float pitchDeg)
        {
            transform.position = feet;
            _walker.Teleport(feet);
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
                return;
            }

            Vector2 look = _input.LookDelta * _settings.LookDegreesPerCount;
            float pitchSign = _settings.InvertY ? -1f : 1f;
            SetLook(Yaw + look.x, Pitch + look.y * pitchSign);

            Vector2 move = _input.MoveValue;
            Quaternion yaw = CoreSpace.YawRotation(Yaw);
            float dt = Time.deltaTime;
            if (Mode == FirstPersonMode.Fly)
            {
                Vector3 forward = Camera.transform.forward;
                Vector3 right = yaw * Vector3.right;
                float vertical = (_input.Jump.IsPressed() ? 1f : 0f) - (_input.Descend.IsPressed() ? 1f : 0f);
                Vector3 velocity = (forward * move.y + right * move.x + Vector3.up * vertical) * FlySpeed;
                if (_input.Sprint.IsPressed())
                {
                    velocity *= FlySprintMultiplier;
                }

                transform.position += velocity * dt;
            }
            else
            {
                Vector3 wish = yaw * new Vector3(move.x, 0f, move.y);
                _walker.Step(wish, _input.Sprint.IsPressed(), _input.Jump.WasPressedThisFrame(), dt);
                transform.position = _walker.Feet;
            }
        }
    }
}
