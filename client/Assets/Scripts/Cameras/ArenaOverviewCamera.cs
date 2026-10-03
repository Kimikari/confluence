using Confluence.Client.Input;
using Confluence.Client.Mapping;
using Confluence.Client.Settings;
using UnityEngine;

namespace Confluence.Client.Cameras
{
    /// <summary>
    /// Rotatable top-down arena preview (spec L329): orbits the arena center at a steep pitch. Q/E or a right-drag
    /// rotate it, a vertical right-drag tilts it between <see cref="MinPitch"/> and straight down, the mouse wheel
    /// zooms. Angles use the Core convention; the camera looks down, so its Core pitch is negative.
    /// </summary>
    public sealed class ArenaOverviewCamera : MonoBehaviour
    {
        public const float MinPitch = 35f;
        public const float MaxPitch = 89.5f;
        public const float RotateSpeedDegPerSec = 90f;
        public const float VerticalFieldOfView = 50f;

        private PreviewInput _input;
        private ClientSettings _settings;
        private Vector3 _target;
        private float _minDistance;
        private float _maxDistance;

        public Camera Camera { get; private set; }

        /// <summary>Orbit yaw (Core convention): the camera sits behind the target along this yaw.</summary>
        public float Yaw { get; private set; }

        /// <summary>Downward tilt in degrees (90 = straight down).</summary>
        public float Tilt { get; private set; } = 72f;

        public float Distance { get; private set; }

        public bool InputEnabled { get; set; } = true;

        public static ArenaOverviewCamera Create(Transform parent, Vector3 target, float arenaRadius, PreviewInput input, ClientSettings settings)
        {
            var go = new GameObject("Overview camera");
            go.transform.SetParent(parent, false);
            ArenaOverviewCamera overview = go.AddComponent<ArenaOverviewCamera>();
            overview._input = input;
            overview._settings = settings;
            overview.Camera = go.AddComponent<Camera>();
            overview.Camera.fieldOfView = VerticalFieldOfView;
            overview.Camera.nearClipPlane = 0.3f;
            overview.Camera.farClipPlane = 600f;
            overview.Camera.clearFlags = CameraClearFlags.Skybox;
            overview.Retarget(target, arenaRadius);
            return overview;
        }

        /// <summary>Frames a (new) arena: orbit center and the zoom range follow its radius.</summary>
        public void Retarget(Vector3 target, float arenaRadius)
        {
            _target = target;
            _minDistance = Mathf.Max(6f, arenaRadius * 0.6f);
            _maxDistance = Mathf.Max(20f, arenaRadius * 5f);
            Distance = Mathf.Clamp(arenaRadius * 2.6f, _minDistance, _maxDistance);
            Apply();
        }

        public void SetView(float yawDeg, float tiltDeg, float distance)
        {
            Yaw = Mathf.Repeat(yawDeg, 360f);
            Tilt = Mathf.Clamp(tiltDeg, MinPitch, MaxPitch);
            Distance = Mathf.Clamp(distance, _minDistance, _maxDistance);
            Apply();
        }

        private void Update()
        {
            if (!InputEnabled || _input == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            float yaw = Yaw;
            float tilt = Tilt;
            float distance = Distance;
            if (_input.RotateLeft.IsPressed())
            {
                yaw -= RotateSpeedDegPerSec * dt;
            }

            if (_input.RotateRight.IsPressed())
            {
                yaw += RotateSpeedDegPerSec * dt;
            }

            if (_input.Orbit.IsPressed())
            {
                Vector2 delta = _input.LookDelta * _settings.LookDegreesPerCount * 2f;
                yaw += delta.x;
                tilt -= delta.y * (_settings.InvertY ? -1f : 1f);
            }

            float scroll = _input.Zoom.ReadValue<float>();
            if (Mathf.Abs(scroll) > 0.01f)
            {
                distance *= Mathf.Pow(0.9f, Mathf.Sign(scroll));
            }

            SetView(yaw, tilt, distance);
        }

        private void Apply()
        {
            // Core pitch is -Tilt (looking down); the camera sits opposite its view direction.
            Quaternion rotation = CoreSpace.ViewRotation(Yaw, -Tilt);
            transform.SetPositionAndRotation(_target - rotation * Vector3.forward * Distance, rotation);
        }
    }
}
