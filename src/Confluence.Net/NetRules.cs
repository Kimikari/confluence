using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Net.RateLimiting;

namespace Confluence.Net
{
    /// <summary>
    /// Network constants. Spec-derived numbers forward to <see cref="GlobalRules.Network"/>; everything else is a
    /// transport tuning choice the spec leaves open (L417-L429 give no packet-level numbers) and is recorded in
    /// docs/notes/net.md.
    /// </summary>
    public static class NetRules
    {
        /// <summary>
        /// Wire-format version of this protocol. Bump whenever any message layout changes; a mismatch is rejected
        /// at the handshake with the unsupported-version state (spec L427).
        /// </summary>
        public const ushort ProtocolVersion = 1;

        /// <summary>
        /// Version string of the rule code (GlobalRules, Schedules, Sim behaviors). It is hashed together with the
        /// catalogue bytes into the ruleset fingerprint, so bump it whenever a rule changes without a catalogue
        /// change (spec L415: gameplay changes enter the ruleset for all owners simultaneously).
        /// </summary>
        public const string RulesVersion = "confluence-rules-0.1";

        public const int TickRate = GlobalRules.Network.TicksPerSecond; // spec L421
        public const int SnapshotRateMin = GlobalRules.Network.SnapshotRateMin; // spec L421
        public const int SnapshotRateMax = GlobalRules.Network.SnapshotRateMax; // spec L421
        public const int DefaultSnapshotRate = GlobalRules.Network.DefaultSnapshotRate; // DECISIONS D-005

        /// <summary>Reconnect window in milliseconds (spec L55: a reconnect is allowed for 90 seconds).</summary>
        public const int ReconnectWindowMs = (int)(GlobalRules.Network.ReconnectWindowSec * 1000f);

        /// <summary>Spectator delay floor while the match is active (spec L391).</summary>
        public static readonly int SpectatorDelayMinTicks = Ticks.FromSeconds(GlobalRules.Match.SpectatorDelayMinSec);

        /// <summary>Remote entities render this far behind the estimated server time (ARCHITECTURE: about 100 ms).</summary>
        public const int InterpolationDelayMs = 100;

        // ---- Inputs ----------------------------------------------------------------------------

        /// <summary>Earlier unacknowledged inputs repeated in every input packet (recovers bursts of 4 lost packets).</summary>
        public const int InputRedundancy = 4;

        /// <summary>Upper bound of commands in one input packet; larger counts are malformed.</summary>
        public const int MaxInputsPerPacket = 16;

        /// <summary>Ticks the server repeats the last input while starved before falling back to a neutral input (100 ms).</summary>
        public const int InputMaxRepeatTicks = 6;

        /// <summary>Most inputs the server queues ahead of consumption (200 ms); older surplus is skipped.</summary>
        public const int InputMaxBufferedTicks = 12;

        // ---- Handshake and sessions ------------------------------------------------------------

        public const int DisplayNameMinChars = 1;
        public const int DisplayNameMaxChars = 24;
        public const int ClientBuildMaxChars = 64;
        public const int RulesVersionMaxChars = 64;
        public const int RejectDetailMaxChars = 200;

        /// <summary>Default session capacity: a full Conclave lobby (spec L34).</summary>
        public const int DefaultMaxSessions = GlobalRules.Match.LobbySize;

        /// <summary>A connection that sends this many malformed or illegal messages is closed.</summary>
        public const int MalformedKickThreshold = 64;

        /// <summary>Messages queued on the client between the transport connect and the server welcome.</summary>
        public const int MaxPreWelcomeMessages = 256;

        // ---- Requests --------------------------------------------------------------------------

        /// <summary>Responses remembered per player for duplicate detection; older request ids are stale.</summary>
        public const int RequestDedupeWindow = 64;

        // ---- Rate limits (spec L425, L429: rerolls and casts are rate-limited; numbers are tuning) ----

        /// <summary>Shop and draft rerolls: burst 3, then one per 250 ms.</summary>
        public static readonly RateLimit Rerolls = new RateLimit(3, 250);

        /// <summary>Purchases, sales, and draft selections: burst 5, then one per 200 ms.</summary>
        public static readonly RateLimit Transactions = new RateLimit(5, 200);

        /// <summary>Chat-like social messages: burst 4, then one per second.</summary>
        public static readonly RateLimit Chat = new RateLimit(4, 1000);

        /// <summary>Clock-sync pings: burst 8, then one per 50 ms.</summary>
        public static readonly RateLimit ClockPings = new RateLimit(8, 50);

        // ---- Clock sync ------------------------------------------------------------------------

        /// <summary>Ping period while the clock estimate is still settling, and the number of such fast pings.</summary>
        public const int ClockPingFastIntervalMs = 100;
        public const int ClockPingFastCount = 10;

        /// <summary>Steady-state ping period.</summary>
        public const int ClockPingIntervalMs = 1000;

        // ---- Congestion (spec L421: snapshot rate "selected by congestion") ---------------------

        /// <summary>Windowed reliable loss at or above this fraction counts as congestion.</summary>
        public const float CongestedLossFraction = 0.05f;

        /// <summary>Round-trip inflation over the best observed RTT that counts as queueing delay.</summary>
        public const int CongestedRttInflationMs = 80;

        /// <summary>Reliable packets waiting for acknowledgement that count as a backed-up link.</summary>
        public const int CongestedReliableQueue = 32;

        /// <summary>Congestion must persist this long before the rate drops to 20 Hz.</summary>
        public const int CongestionDowngradeMs = 500;

        /// <summary>The link must stay clear this long before the rate returns to 30 Hz.</summary>
        public const int CongestionRecoveryMs = 3000;
    }
}
