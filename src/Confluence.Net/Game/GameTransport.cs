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

        /// <summary>
        /// Production defaults: library threads (<c>ManualMode = false</c>) mitigate LiteNetLib 2.1.4 manual-mode receive bug
        /// #616 (an empty or layer-rejected datagram ends a manual receive loop; on Linux/Mono permanently, research §2.8.1)
        /// until a fixed release is pinned. Events are still raised only from Poll, so game state stays on the caller's
        /// thread. Tests on a virtual clock use manual mode (TransportSettings default).
        /// </summary>
        public static TransportSettings Defaults() => new TransportSettings { DisconnectTimeoutMs = DisconnectTimeoutMs, AutoReconnect = true, ManualMode = false };
    }
}
