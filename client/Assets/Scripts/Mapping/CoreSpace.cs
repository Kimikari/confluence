using Confluence.Core.Geometry;
using UnityEngine;

namespace Confluence.Client.Mapping
{
    /// <summary>
    /// Conversions between Confluence.Core and Unity space. Core already uses Unity's left-handed axes
    /// (docs/ARCHITECTURE.md "Conventions": Y up, +Z forward, +X right; yaw 0 faces +Z and positive yaw turns
    /// right), so positions and yaw map one-to-one. The single sign flip is pitch: Core pitch is elevation
    /// (positive looks up, CMath), while Unity's euler X rotates positive downward.
    /// </summary>
    public static class CoreSpace
    {
        public static Vector3 ToUnity(Vec3 v) => new Vector3(v.X, v.Y, v.Z);

        public static Vec3 ToCore(Vector3 v) => new Vec3(v.x, v.y, v.z);

        /// <summary>Ground-plane point (X, Y = world Z) lifted to height y.</summary>
        public static Vector3 ToUnity(Vec2 xz, float y) => new Vector3(xz.X, y, xz.Y);

        public static Vec2 ToCoreXZ(Vector3 v) => new Vec2(v.x, v.z);

        /// <summary>Rotation about the vertical axis by a Core yaw (identical to Unity's Y euler angle).</summary>
        public static Quaternion YawRotation(float yawDeg) => Quaternion.Euler(0f, yawDeg, 0f);

        /// <summary>Camera rotation for a Core yaw and pitch (positive pitch looks up).</summary>
        public static Quaternion ViewRotation(float yawDeg, float pitchDeg) => Quaternion.Euler(UnityEulerXFromPitch(pitchDeg), yawDeg, 0f);

        /// <summary>Unity euler X for a Core pitch: the negation.</summary>
        public static float UnityEulerXFromPitch(float pitchDeg) => -pitchDeg;

        /// <summary>Core pitch for a Unity euler X angle (accepts 0..360 wrapped values).</summary>
        public static float PitchFromUnityEulerX(float eulerX) => -Mathf.DeltaAngle(0f, eulerX);

        /// <summary>World pose of a Core yaw box as Unity position and rotation (scale is the box size).</summary>
        public static void Pose(YawBox box, out Vector3 position, out Quaternion rotation)
        {
            position = ToUnity(box.Center);
            rotation = YawRotation(box.YawDeg);
        }
    }
}
