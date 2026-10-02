using System;

namespace Confluence.Client.Settings
{
    /// <summary>
    /// Connection fields remembered by the Play card: server host, UDP port, and display name. The default port
    /// is a client-side placeholder until Confluence.Net defines the server's listening port.
    /// </summary>
    public sealed class PlayProfile
    {
        public const string DefaultHost = "127.0.0.1";
        public const int DefaultPort = 7777;
        public const string DefaultName = "Wizard";
        public const int MaxNameLength = 24;
        public const int MaxHostLength = 253;

        private const string KeyHost = "confluence.play.host";
        private const string KeyPort = "confluence.play.port";
        private const string KeyName = "confluence.play.name";

        private readonly ISettingsStore _store;
        private string _host = DefaultHost;
        private int _port = DefaultPort;
        private string _name = DefaultName;

        public PlayProfile(ISettingsStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public string Host
        {
            get => _host;
            set => _host = Clean(value, MaxHostLength, DefaultHost);
        }

        public int Port
        {
            get => _port;
            set => _port = IsValidPort(value) ? value : DefaultPort;
        }

        public string PlayerName
        {
            get => _name;
            set => _name = Clean(value, MaxNameLength, DefaultName);
        }

        public static bool IsValidPort(int port) => port >= 1 && port <= 65535;

        /// <summary>Null when the fields are usable, else a short reason for the Play card.</summary>
        public static string Validate(string host, int port, string name)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return "Enter a server host.";
            }

            if (!IsValidPort(port))
            {
                return "Port must be between 1 and 65535.";
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                return "Enter a display name.";
            }

            return null;
        }

        public void Load()
        {
            Host = _store.GetString(KeyHost, DefaultHost);
            Port = _store.GetInt(KeyPort, DefaultPort);
            PlayerName = _store.GetString(KeyName, DefaultName);
        }

        public void Save()
        {
            _store.SetString(KeyHost, _host);
            _store.SetInt(KeyPort, _port);
            _store.SetString(KeyName, _name);
            _store.Flush();
        }

        private static string Clean(string value, int maxLength, string fallback)
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return fallback;
            }

            return trimmed.Length > maxLength ? trimmed.Substring(0, maxLength) : trimmed;
        }
    }
}
