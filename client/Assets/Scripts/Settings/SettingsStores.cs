using System.Collections.Generic;
using UnityEngine;

namespace Confluence.Client.Settings
{
    /// <summary>Key-value persistence behind <see cref="ClientSettings"/> and <see cref="PlayProfile"/>.</summary>
    public interface ISettingsStore
    {
        float GetFloat(string key, float defaultValue);
        int GetInt(string key, int defaultValue);
        string GetString(string key, string defaultValue);
        void SetFloat(string key, float value);
        void SetInt(string key, int value);
        void SetString(string key, string value);
        void Flush();
    }

    /// <summary>Unity PlayerPrefs (the per-user registry on Windows).</summary>
    public sealed class PlayerPrefsSettingsStore : ISettingsStore
    {
        public float GetFloat(string key, float defaultValue) => PlayerPrefs.GetFloat(key, defaultValue);
        public int GetInt(string key, int defaultValue) => PlayerPrefs.GetInt(key, defaultValue);
        public string GetString(string key, string defaultValue) => PlayerPrefs.GetString(key, defaultValue);
        public void SetFloat(string key, float value) => PlayerPrefs.SetFloat(key, value);
        public void SetInt(string key, int value) => PlayerPrefs.SetInt(key, value);
        public void SetString(string key, string value) => PlayerPrefs.SetString(key, value ?? string.Empty);
        public void Flush() => PlayerPrefs.Save();
    }

    /// <summary>In-memory store for tests and smoke runs that must not touch the user's saved settings.</summary>
    public sealed class MemorySettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, float> _floats = new Dictionary<string, float>();
        private readonly Dictionary<string, int> _ints = new Dictionary<string, int>();
        private readonly Dictionary<string, string> _strings = new Dictionary<string, string>();

        public int FlushCount { get; private set; }

        public float GetFloat(string key, float defaultValue) => _floats.TryGetValue(key, out float v) ? v : defaultValue;
        public int GetInt(string key, int defaultValue) => _ints.TryGetValue(key, out int v) ? v : defaultValue;
        public string GetString(string key, string defaultValue) => _strings.TryGetValue(key, out string v) ? v : defaultValue;
        public void SetFloat(string key, float value) => _floats[key] = value;
        public void SetInt(string key, int value) => _ints[key] = value;
        public void SetString(string key, string value) => _strings[key] = value ?? string.Empty;
        public void Flush() => FlushCount++;
    }
}
