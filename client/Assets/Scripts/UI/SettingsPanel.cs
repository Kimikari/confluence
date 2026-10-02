using System;
using Confluence.Client.App;
using Confluence.Client.Settings;
using UnityEngine;
using UnityEngine.UIElements;

namespace Confluence.Client.UI
{
    /// <summary>
    /// Settings dialog (spec L369, L371): field of view 80-110, mouse sensitivity and Y inversion, and separate
    /// master, music, effects, and voice volumes. Changes apply immediately; Done saves them.
    /// </summary>
    public static class SettingsPanel
    {
        public static VisualElement Build(ClientContext context, Action onDone)
        {
            ClientSettings s = context.Settings;
            VisualElement overlay = Ui.Element("cf-overlay");
            VisualElement panel = Ui.Panel("cf-panel--dialog");
            overlay.Add(panel);

            panel.Add(Ui.Heading("Settings"));
            panel.Add(Ui.Label("Camera and controls", "cf-codex-key"));
            panel.Add(Ui.SliderInt("Field of view (horizontal)", (int)ClientSettings.FieldOfViewMin, (int)ClientSettings.FieldOfViewMax,
                Mathf.RoundToInt(s.FieldOfView), v => s.FieldOfView = v));
            panel.Add(Ui.Slider("Mouse sensitivity", ClientSettings.SensitivityMin, ClientSettings.SensitivityMax, s.MouseSensitivity,
                v => s.MouseSensitivity = v));
            panel.Add(Ui.Toggle("Invert vertical look", s.InvertY, v => s.InvertY = v));

            panel.Add(Ui.Label("Audio", "cf-codex-key"));
            panel.Add(Volume("Master volume", s.MasterVolume, v => s.MasterVolume = v));
            panel.Add(Volume("Music volume", s.MusicVolume, v => s.MusicVolume = v));
            panel.Add(Volume("Effects volume", s.EffectsVolume, v => s.EffectsVolume = v));
            panel.Add(Volume("Voice volume", s.VoiceVolume, v => s.VoiceVolume = v));
            panel.Add(Ui.Muted("Music, effects, and voice levels are stored now and drive the mixer once audio exists."));

            VisualElement buttons = Ui.Row();
            buttons.style.marginTop = 16;
            buttons.Add(Ui.Button("Done", onDone, "cf-button--small", "cf-button--primary"));
            buttons.Add(Ui.Button("Reset to defaults", () =>
            {
                s.ResetToDefaults();
                context.OpenSettings();
            }, "cf-button--small"));
            panel.Add(buttons);
            return overlay;
        }

        private static SliderInt Volume(string label, float value, Action<float> set)
        {
            return Ui.SliderInt(label, 0, 100, Mathf.RoundToInt(value * 100f), v => set(v / 100f));
        }
    }
}
