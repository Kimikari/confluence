using System;
using Confluence.Net.Serialization;

namespace Confluence.Net.Protocol
{
    /// <summary>Why the server refused a connection.</summary>
    public enum RejectReason : byte
    {
        /// <summary>The connection data was not a readable Confluence handshake.</summary>
        MalformedHandshake = 1,

        /// <summary>Different wire protocol version (unsupported-version state, spec L427).</summary>
        UnsupportedProtocol = 2,

        /// <summary>Different ruleset fingerprint: catalogue or rules version differ (spec L427).</summary>
        RulesetMismatch = 3,

        /// <summary>The server requires a specific client build.</summary>
        UnsupportedClientBuild = 4,

        InvalidDisplayName = 5,

        ServerFull = 6,

        /// <summary>The server takes no new players (for example a match is in progress); reconnects still work.</summary>
        NotAcceptingPlayers = 7,

        /// <summary>The reconnect token belongs to a session whose 90 s window has passed (spec L55).</summary>
        ReconnectExpired = 8,

        /// <summary>The reconnect token is not known to this server.</summary>
        UnknownSession = 9,

        /// <summary>The server rejected the connection without readable details.</summary>
        Unspecified = 255,
    }

    /// <summary>Why the server closed an established connection.</summary>
    public enum DisconnectNoticeReason : byte
    {
        Kicked = 1,

        /// <summary>Too many malformed or illegal messages.</summary>
        ProtocolViolation = 2,

        /// <summary>The same player reconnected from another connection.</summary>
        SessionTakenOver = 3,

        ServerShutdown = 4,
    }

    /// <summary>Validation of player-chosen display names.</summary>
    public static class DisplayNames
    {
        /// <summary>
        /// Trimmed name when it has 1-24 characters and no control characters; otherwise null. Content
        /// moderation (offensive names, spec L393) is a separate service concern.
        /// </summary>
        public static string Normalize(string name)
        {
            if (name == null) return null;
            string trimmed = name.Trim();
            if (trimmed.Length < NetRules.DisplayNameMinChars || trimmed.Length > NetRules.DisplayNameMaxChars) return null;
            foreach (char c in trimmed)
            {
                if (char.IsControl(c)) return null;
            }

            return trimmed;
        }
    }

    /// <summary>
    /// Connection data the client sends with its connect request: protocol version, ruleset fingerprint,
    /// build id, display name, and an optional reconnect token. The magic and protocol version lead every
    /// protocol version's layout (frozen), so a server can name a version mismatch even when the rest of a
    /// newer or older handshake does not decode. <see cref="ConnectionRejection"/> is frozen the same way and
    /// readers ignore fields appended after it.
    /// </summary>
    public sealed class HandshakeRequest : INetMessage
    {
        public const uint Magic = 0x4E464C43; // "CLFN" little-endian

        /// <summary>
        /// Reads only the frozen preamble (magic and protocol version). False when the bytes are not a Confluence
        /// handshake at all.
        /// </summary>
        public static bool TryReadProtocolVersion(NetReader reader, out ushort protocolVersion)
        {
            protocolVersion = 0;
            if (reader.Remaining < 6 || reader.ReadUInt() != Magic)
            {
                return false;
            }

            protocolVersion = reader.ReadUShort();
            return true;
        }

        public ushort ProtocolVersion { get; set; } = NetRules.ProtocolVersion;

        public RulesetFingerprint Ruleset { get; set; } = RulesetFingerprint.Zero;

        public string RulesVersion { get; set; } = NetRules.RulesVersion;

        public string ClientBuild { get; set; } = "";

        public string DisplayName { get; set; } = "";

        public ReconnectToken ReconnectToken { get; set; }

        public void Serialize(NetWriter writer)
        {
            writer.WriteUInt(Magic);
            writer.WriteUShort(ProtocolVersion);
            (Ruleset ?? RulesetFingerprint.Zero).Write(writer);
            writer.WriteString(RulesVersion, NetRules.RulesVersionMaxChars * 3);
            writer.WriteString(ClientBuild, NetRules.ClientBuildMaxChars * 3);
            writer.WriteString(DisplayName, NetRules.DisplayNameMaxChars * 3 + 32);
            ReconnectToken.Write(writer);
        }

        public void Deserialize(NetReader reader)
        {
            if (reader.ReadUInt() != Magic)
            {
                throw new NetReadException(NetReadError.InvalidValue, "not a Confluence handshake");
            }

            ProtocolVersion = reader.ReadUShort();
            Ruleset = RulesetFingerprint.Read(reader);
            RulesVersion = reader.ReadString(NetRules.RulesVersionMaxChars * 3);
            ClientBuild = reader.ReadString(NetRules.ClientBuildMaxChars * 3);
            DisplayName = reader.ReadString(NetRules.DisplayNameMaxChars * 3 + 32);
            ReconnectToken = ReconnectToken.Read(reader);
        }
    }

    /// <summary>
    /// Reject payload. It always carries the server's protocol and ruleset identity so the client can show a
    /// precise unsupported-version state rather than a generic failure (spec L427).
    /// </summary>
    public sealed class ConnectionRejection : INetMessage
    {
        public const uint Magic = 0x4A524C43; // "CLRJ"

        public RejectReason Reason { get; set; } = RejectReason.Unspecified;

        public ushort ServerProtocolVersion { get; set; }

        public string ServerRulesVersion { get; set; } = "";

        public RulesetFingerprint ServerRuleset { get; set; } = RulesetFingerprint.Zero;

        public string Detail { get; set; } = "";

        /// <summary>True when the client build cannot play on this server at all (protocol, ruleset, or build).</summary>
        public bool IsVersionMismatch =>
            Reason == RejectReason.UnsupportedProtocol
            || Reason == RejectReason.RulesetMismatch
            || Reason == RejectReason.UnsupportedClientBuild;

        public void Serialize(NetWriter writer)
        {
            writer.WriteUInt(Magic);
            writer.WriteByte((byte)Reason);
            writer.WriteUShort(ServerProtocolVersion);
            writer.WriteString(ServerRulesVersion, NetRules.RulesVersionMaxChars * 3);
            (ServerRuleset ?? RulesetFingerprint.Zero).Write(writer);
            writer.WriteString(Truncate(Detail, NetRules.RejectDetailMaxChars), NetRules.RejectDetailMaxChars * 3);
        }

        public void Deserialize(NetReader reader)
        {
            if (reader.ReadUInt() != Magic)
            {
                throw new NetReadException(NetReadError.InvalidValue, "not a Confluence rejection");
            }

            byte reason = reader.ReadByte();
            Reason = Enum.IsDefined(typeof(RejectReason), reason) ? (RejectReason)reason : RejectReason.Unspecified;
            ServerProtocolVersion = reader.ReadUShort();
            ServerRulesVersion = reader.ReadString(NetRules.RulesVersionMaxChars * 3);
            ServerRuleset = RulesetFingerprint.Read(reader);
            Detail = reader.ReadString(NetRules.RejectDetailMaxChars * 3);
        }

        public override string ToString() => Reason + (string.IsNullOrEmpty(Detail) ? "" : ": " + Detail);

        internal static string Truncate(string s, int max) => s == null ? "" : (s.Length <= max ? s : s.Substring(0, max));
    }

    /// <summary>Payload of a server-initiated disconnect of an established connection.</summary>
    public sealed class DisconnectNotice : INetMessage
    {
        public const uint Magic = 0x43444C43; // "CLDC"

        public DisconnectNoticeReason Reason { get; set; } = DisconnectNoticeReason.Kicked;

        public string Detail { get; set; } = "";

        public void Serialize(NetWriter writer)
        {
            writer.WriteUInt(Magic);
            writer.WriteByte((byte)Reason);
            writer.WriteString(ConnectionRejection.Truncate(Detail, NetRules.RejectDetailMaxChars), NetRules.RejectDetailMaxChars * 3);
        }

        public void Deserialize(NetReader reader)
        {
            if (reader.ReadUInt() != Magic)
            {
                throw new NetReadException(NetReadError.InvalidValue, "not a Confluence disconnect notice");
            }

            Reason = (DisconnectNoticeReason)reader.ReadByteInRange(1, 4);
            Detail = reader.ReadString(NetRules.RejectDetailMaxChars * 3);
        }
    }

    /// <summary>First message after the server accepts a connection: the player identity and initial clock.</summary>
    public sealed class Welcome : INetMessage
    {
        public int PlayerId { get; set; }

        public ReconnectToken ReconnectToken { get; set; }

        /// <summary>True when this connection resumed an existing session within the reconnect window.</summary>
        public bool Resumed { get; set; }

        public int TickRate { get; set; } = NetRules.TickRate;

        public int SnapshotRate { get; set; } = NetRules.DefaultSnapshotRate;

        public int ReconnectWindowMs { get; set; } = NetRules.ReconnectWindowMs;

        /// <summary>Server tick (with fraction) when the welcome was sent.</summary>
        public double ServerTick { get; set; }

        public void Serialize(NetWriter writer)
        {
            writer.WriteVarInt(PlayerId);
            ReconnectToken.Write(writer);
            writer.WriteBool(Resumed);
            writer.WriteVarInt(TickRate);
            writer.WriteVarInt(SnapshotRate);
            writer.WriteVarInt(ReconnectWindowMs);
            TickTime.Write(writer, ServerTick);
        }

        public void Deserialize(NetReader reader)
        {
            PlayerId = reader.ReadVarIntInRange(1, int.MaxValue);
            ReconnectToken = ReconnectToken.Read(reader);
            Resumed = reader.ReadBool();
            TickRate = reader.ReadVarIntInRange(1, 1000);
            SnapshotRate = reader.ReadVarIntInRange(1, 1000);
            ReconnectWindowMs = reader.ReadVarIntInRange(0, int.MaxValue);
            ServerTick = TickTime.Read(reader);
        }
    }

    /// <summary>Client clock probe (unreliable). The server echoes the client time with its own tick.</summary>
    public sealed class ClockPing : INetMessage
    {
        public long ClientTimeMs { get; set; }

        public void Serialize(NetWriter writer) => writer.WriteVarLong(ClientTimeMs);

        public void Deserialize(NetReader reader) => ClientTimeMs = reader.ReadVarLong();
    }

    /// <summary>Server reply to <see cref="ClockPing"/>.</summary>
    public sealed class ClockPong : INetMessage
    {
        public long ClientTimeMs { get; set; }

        public double ServerTick { get; set; }

        public void Serialize(NetWriter writer)
        {
            writer.WriteVarLong(ClientTimeMs);
            TickTime.Write(writer, ServerTick);
        }

        public void Deserialize(NetReader reader)
        {
            ClientTimeMs = reader.ReadVarLong();
            ServerTick = TickTime.Read(reader);
        }
    }

    /// <summary>Wire form of a fractional tick: whole tick as a varint plus a 16-bit fraction.</summary>
    public static class TickTime
    {
        public static void Write(NetWriter writer, double tick)
        {
            if (!(tick >= 0)) tick = 0; // also maps NaN to 0
            double whole = Math.Floor(tick);
            if (whole > int.MaxValue) whole = int.MaxValue;
            int fraction = (int)Math.Round((tick - whole) * 65536.0);
            if (fraction >= 65536)
            {
                fraction = 0;
                whole = Math.Min(int.MaxValue, whole + 1);
            }

            writer.WriteVarUInt((uint)whole);
            writer.WriteUShort((ushort)fraction);
        }

        public static double Read(NetReader reader)
        {
            uint whole = reader.ReadVarUInt();
            if (whole > int.MaxValue)
            {
                throw new NetReadException(NetReadError.InvalidValue, "tick out of range");
            }

            return whole + reader.ReadUShort() / 65536.0;
        }
    }

    /// <summary>Transport-level message types (ids below <see cref="MessageType.FirstGameMessageId"/>).</summary>
    public static class ProtocolMessages
    {
        public static readonly MessageType<Welcome> Welcome =
            new MessageType<Welcome>(1, "net.welcome", NetChannel.Control, MessageDirection.ServerToClient);

        public static readonly MessageType<ClockPing> ClockPing =
            new MessageType<ClockPing>(2, "net.clock_ping", NetChannel.Time, MessageDirection.ClientToServer, NetRules.ClockPings);

        public static readonly MessageType<ClockPong> ClockPong =
            new MessageType<ClockPong>(3, "net.clock_pong", NetChannel.Time, MessageDirection.ServerToClient);
    }
}
