namespace Confluence.Net.Transport
{
    /// <summary>
    /// Socket-level tuning shared by both transports. Defaults suit a LAN or internet match; tests shorten the
    /// timeouts.
    /// </summary>
    public sealed class TransportSettings
    {
        /// <summary>
        /// When true (default) LiteNetLib runs in manual mode: no library threads at all; sockets are read and
        /// timers advanced only inside Poll. When false, LiteNetLib's own threads handle sockets and resends,
        /// but events are still delivered only from Poll, so game state is never touched off the caller's thread.
        /// </summary>
        public bool ManualMode { get; set; } = true;

        /// <summary>A connection with no packets for this long is closed.</summary>
        public int DisconnectTimeoutMs { get; set; } = 5000;

        /// <summary>LiteNetLib keep-alive and RTT probe interval.</summary>
        public int PingIntervalMs { get; set; } = 1000;

        /// <summary>
        /// Largest elapsed time fed to one LiteNetLib update. A long frame hitch (or a test jumping the clock)
        /// must not look like a silent peer, so each Poll advances library timers by at most this much.
        /// </summary>
        public int MaxUpdateStepMs { get; set; } = 100;

        /// <summary>LiteNetLib connect attempts before giving up (one attempt per ReconnectDelay, 500 ms by default).</summary>
        public int MaxConnectAttempts { get; set; } = 10;

        /// <summary>Malformed or illegal messages tolerated per connection before it is closed (0 disables).</summary>
        public int MalformedKickThreshold { get; set; } = NetRules.MalformedKickThreshold;

        /// <summary>
        /// Rate-limited plus handler-faulting messages tolerated per connection before it is closed as a flood (0
        /// disables). Honest clients stay far below it: their bursts fit the limits (research §2.6.7).
        /// </summary>
        public int AbuseKickThreshold { get; set; } = NetRules.AbuseKickThreshold;

        /// <summary>When true, exceptions thrown by message handlers propagate out of Poll (useful in tests).</summary>
        public bool RethrowHandlerExceptions { get; set; }

        /// <summary>Collect LiteNetLib statistics (packet loss feeds the snapshot rate selector).</summary>
        public bool EnableStatistics { get; set; } = true;

        /// <summary>Client only: automatically retry within the reconnect window after an unexpected disconnect.</summary>
        public bool AutoReconnect { get; set; }

        /// <summary>Client only: delay between automatic reconnect attempts.</summary>
        public int ReconnectRetryMs { get; set; } = 1000;

        /// <summary>Server only: reconnect window (spec L55, 90 s).</summary>
        public int ReconnectWindowMs { get; set; } = NetRules.ReconnectWindowMs;

        /// <summary>Client only: clock-sync probe periods.</summary>
        public int ClockPingFastIntervalMs { get; set; } = NetRules.ClockPingFastIntervalMs;

        public int ClockPingFastCount { get; set; } = NetRules.ClockPingFastCount;

        public int ClockPingIntervalMs { get; set; } = NetRules.ClockPingIntervalMs;

        public TransportSettings Clone() => (TransportSettings)MemberwiseClone();
    }
}
