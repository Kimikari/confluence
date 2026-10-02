using System;
using Confluence.Core.Rules;

namespace Confluence.Client.Settings
{
    /// <summary>
    /// Local player options (spec L369, L371). Pure presentation state: none of these values reach the server
    /// or change a rule. Every setter clamps to its legal range and raises <see cref="Changed"/>; nothing is
    /// persisted until <see cref="Save"/>.
    /// </summary>
    public sealed class ClientSettings
    {
        // Spec L371 "field of view 80 through 110". Interpreted as the horizontal FOV (the PC shooter convention);
        // cameras convert it to Unity's vertical FOV for the current aspect (docs/notes/client.md).
        public const float FieldOfViewMin = GlobalRules.Interface.FieldOfViewMin;
        public const float FieldOfViewMax = GlobalRules.Interface.FieldOfViewMax;
        public const float DefaultFieldOfView = 95f;

        // Mouse look multiplies the raw device delta (acceleration off, spec L369) by Sensitivity * DegreesPerCount.
        public const float SensitivityMin = 0.1f;
        public const float SensitivityMax = 10f;
        public const float DefaultSensitivity = 2f;
        public const float DegreesPerCount = 0.022f;

        public const float DefaultVolume = 0.8f;

        // Spec L371 "scalable HUD" and dynamic text size: one factor for every panel (menus, shop, HUD).
        public const float UiScaleMin = 0.75f;
        public const float UiScaleMax = 1.5f;
        public const float DefaultUiScale = 1f;

        private const string Prefix = "confluence.settings.";
        private const string KeyFov = Prefix + "fov";
        private const string KeySensitivity = Prefix + "sensitivity";
        private const string KeyInvertY = Prefix + "invertY";
        private const string KeyMaster = Prefix + "volume.master";
        private const string KeyMusic = Prefix + "volume.music";
        private const string KeyEffects = Prefix + "volume.effects";
        private const string KeyVoice = Prefix + "volume.voice";
        private const string KeyUiScale = Prefix + "ui.scale";

        private readonly ISettingsStore _store;
        private float _fieldOfView = DefaultFieldOfView;
        private float _sensitivity = DefaultSensitivity;
        private bool _invertY;
        private float _masterVolume = DefaultVolume;
        private float _musicVolume = DefaultVolume;
        private float _effectsVolume = DefaultVolume;
        private float _voiceVolume = DefaultVolume;
        private float _uiScale = DefaultUiScale;

        public ClientSettings(ISettingsStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>Raised after any value changes.</summary>
        public event Action Changed;

        /// <summary>Horizontal field of view in degrees, 80-110.</summary>
        public float FieldOfView
        {
            get => _fieldOfView;
            set => Set(ref _fieldOfView, Clamp(value, FieldOfViewMin, FieldOfViewMax, DefaultFieldOfView));
        }

        public float MouseSensitivity
        {
            get => _sensitivity;
            set => Set(ref _sensitivity, Clamp(value, SensitivityMin, SensitivityMax, DefaultSensitivity));
        }

        public bool InvertY
        {
            get => _invertY;
            set
            {
                if (_invertY == value)
                {
                    return;
                }

                _invertY = value;
                Changed?.Invoke();
            }
        }

        public float MasterVolume
        {
            get => _masterVolume;
            set => Set(ref _masterVolume, Clamp(value, 0f, 1f, DefaultVolume));
        }

        // Spec L371: separate voice, music, and effects controls.
        public float MusicVolume
        {
            get => _musicVolume;
            set => Set(ref _musicVolume, Clamp(value, 0f, 1f, DefaultVolume));
        }

        public float EffectsVolume
        {
            get => _effectsVolume;
            set => Set(ref _effectsVolume, Clamp(value, 0f, 1f, DefaultVolume));
        }

        public float VoiceVolume
        {
            get => _voiceVolume;
            set => Set(ref _voiceVolume, Clamp(value, 0f, 1f, DefaultVolume));
        }

        /// <summary>Interface and text scale, 0.75-1.5 (spec L371).</summary>
        public float UiScale
        {
            get => _uiScale;
            set => Set(ref _uiScale, Clamp(value, UiScaleMin, UiScaleMax, DefaultUiScale));
        }

        /// <summary>Mouse-look degrees per raw device count.</summary>
        public float LookDegreesPerCount => _sensitivity * DegreesPerCount;

        public void Load()
        {
            _fieldOfView = Clamp(_store.GetFloat(KeyFov, DefaultFieldOfView), FieldOfViewMin, FieldOfViewMax, DefaultFieldOfView);
            _sensitivity = Clamp(_store.GetFloat(KeySensitivity, DefaultSensitivity), SensitivityMin, SensitivityMax, DefaultSensitivity);
            _invertY = _store.GetInt(KeyInvertY, 0) != 0;
            _masterVolume = Clamp(_store.GetFloat(KeyMaster, DefaultVolume), 0f, 1f, DefaultVolume);
            _musicVolume = Clamp(_store.GetFloat(KeyMusic, DefaultVolume), 0f, 1f, DefaultVolume);
            _effectsVolume = Clamp(_store.GetFloat(KeyEffects, DefaultVolume), 0f, 1f, DefaultVolume);
            _voiceVolume = Clamp(_store.GetFloat(KeyVoice, DefaultVolume), 0f, 1f, DefaultVolume);
            _uiScale = Clamp(_store.GetFloat(KeyUiScale, DefaultUiScale), UiScaleMin, UiScaleMax, DefaultUiScale);
            Changed?.Invoke();
        }

        public void Save()
        {
            _store.SetFloat(KeyFov, _fieldOfView);
            _store.SetFloat(KeySensitivity, _sensitivity);
            _store.SetInt(KeyInvertY, _invertY ? 1 : 0);
            _store.SetFloat(KeyMaster, _masterVolume);
            _store.SetFloat(KeyMusic, _musicVolume);
            _store.SetFloat(KeyEffects, _effectsVolume);
            _store.SetFloat(KeyVoice, _voiceVolume);
            _store.SetFloat(KeyUiScale, _uiScale);
            _store.Flush();
        }

        public void ResetToDefaults()
        {
            _fieldOfView = DefaultFieldOfView;
            _sensitivity = DefaultSensitivity;
            _invertY = false;
            _masterVolume = DefaultVolume;
            _musicVolume = DefaultVolume;
            _effectsVolume = DefaultVolume;
            _voiceVolume = DefaultVolume;
            _uiScale = DefaultUiScale;
            Changed?.Invoke();
        }

        private void Set(ref float field, float value)
        {
            if (field == value)
            {
                return;
            }

            field = value;
            Changed?.Invoke();
        }

        private static float Clamp(float value, float min, float max, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return fallback;
            }

            return value < min ? min : value > max ? max : value;
        }
    }
}
