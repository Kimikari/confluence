using UnityEngine;
using UnityEngine.Rendering;

namespace Confluence.Client.Arena
{
    /// <summary>
    /// Standardized overhead lighting (spec L320). Every arena uses the same steep key light and flat ambient so
    /// biome art never changes visibility (spec L684 "cosmetic darkness ... never secretly changes enemy
    /// visibility"); light shafts are additive spots over their recorded positions.
    /// </summary>
    public static class ArenaLighting
    {
        /// <summary>Steep elevation keeps shadows short and readable under cover.</summary>
        public const float OverheadPitchDeg = 68f;
        public const float OverheadYawDeg = 35f;
        public const float OverheadIntensity = 1.35f;

        /// <summary>Height above the shaft's floor point where its spot light sits.</summary>
        public const float ShaftHeight = 9f;
        public const float ShaftIntensity = 6f;

        public static readonly Color OverheadColor = new Color(1f, 0.96f, 0.9f);

        public static void ConfigureOverhead(Light light)
        {
            light.type = LightType.Directional;
            light.color = OverheadColor;
            light.intensity = OverheadIntensity;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.85f;
            light.renderMode = LightRenderMode.ForcePixel;
        }

        public static void ConfigureShaft(Light light, float radius)
        {
            light.type = LightType.Spot;
            light.color = new Color(1f, 0.95f, 0.82f);
            light.intensity = ShaftIntensity;
            light.range = ShaftHeight * 1.6f;

            // Cone that covers the shaft radius on the floor.
            light.spotAngle = Mathf.Clamp(2f * Mathf.Atan2(Mathf.Max(0.5f, radius), ShaftHeight) * Mathf.Rad2Deg, 10f, 120f);
            light.innerSpotAngle = light.spotAngle * 0.6f;
            light.shadows = LightShadows.None;
        }

        /// <summary>Flat three-color ambient, identical on every graphics preset (spec L138).</summary>
        public static void ApplyAmbient()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.52f, 0.58f, 0.68f);
            RenderSettings.ambientEquatorColor = new Color(0.4f, 0.41f, 0.43f);
            RenderSettings.ambientGroundColor = new Color(0.24f, 0.23f, 0.21f);
            RenderSettings.fog = false;
        }
    }
}
