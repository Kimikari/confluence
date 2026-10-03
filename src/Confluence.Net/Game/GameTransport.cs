using Confluence.Net.Transport;

namespace Confluence.Net.Game
{
    /// <summary>Transport defaults of game sessions.</summary>
    public static class GameTransport
    {
        /// <summary>
        /// A generous 15 s disconnect timeout: a server generating an unpredicted arena (a draw replay, 0.3-8 s) cannot
        /// poll meanwhile, and a session must not drop for that. Real outages still resume within the 90 s window (L55).
        /// </summary>
        public const int DisconnectTimeoutMs = 15000;

        public static TransportSettings Defaults() => new TransportSettings { DisconnectTimeoutMs = DisconnectTimeoutMs, AutoReconnect = true };
    }
}
