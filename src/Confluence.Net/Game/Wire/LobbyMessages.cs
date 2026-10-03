using System.Collections.Generic;
using Confluence.Casting.Grammars;
using Confluence.Core.Rules;
using Confluence.Net.Protocol;
using Confluence.Net.Serialization;

namespace Confluence.Net.Game.Wire
{
    /// <summary>Server lobby lifecycle.</summary>
    public enum LobbyStatus : byte
    {
        /// <summary>Seats are open; the match starts once every human seat has joined.</summary>
        Waiting = 0,

        Running = 1,
        Finished = 2,

        /// <summary>The host refused to start (economy floors, D-010); see the notice.</summary>
        Refused = 3,
    }

    /// <summary>Reason codes of a refused lobby join (the response's reason field).</summary>
    public enum JoinCode : ushort
    {
        Accepted = 0,
        UnknownWizard = 1,

        /// <summary>The wizard's kit is not implemented under the current rules (choice limited to implemented kits).</summary>
        WizardNotAvailable = 2,

        /// <summary>The wizard's grammar does not accept the requested input alternative (spec L365-L369, L490).</summary>
        RouteNotSupported = 3,

        LobbyFull = 4,
        MatchRunning = 5,
    }

    /// <summary>
    /// Lobby join (spec L292 one wizard for the whole match, L343 the controls of the selected wizard and an input
    /// alternative are settled in the lobby). The display name comes from the transport handshake.
    /// </summary>
    public sealed class JoinLobbyRequest : INetMessage
    {
        public string WizardId { get; set; }

        /// <summary>The input alternative (exactly one InputRoute bit).</summary>
        public InputRoute Route { get; set; } = InputRoute.Standard;

        public void Serialize(NetWriter writer)
        {
            writer.WriteId(WizardId);
            writer.WriteByte((byte)Route);
        }

        public void Deserialize(NetReader reader)
        {
            WizardId = reader.ReadId();
            byte route = reader.ReadByte();
            if (route == 0 || (route & (route - 1)) != 0 || route > (byte)InputRoute.Guided)
            {
                throw new NetReadException(NetReadError.InvalidValue, "an input alternative is exactly one route");
            }

            Route = (InputRoute)route;
        }
    }

    public sealed class JoinLobbyResponse : INetMessage
    {
        /// <summary>The match seat (player id) assigned, or -1.</summary>
        public int Seat { get; set; } = -1;

        public string Detail { get; set; } = string.Empty;

        public void Serialize(NetWriter writer)
        {
            writer.WriteSeat(Seat);
            writer.WriteText(Detail);
        }

        public void Deserialize(NetReader reader)
        {
            Seat = reader.ReadSeat();
            Detail = reader.ReadText();
        }
    }

    /// <summary>A wizard the lobby offers, with the input routes its grammar accepts.</summary>
    public sealed class WizardChoiceWire
    {
        public string WizardId;
        public string Name;
        public InputRoute Routes;

        /// <summary>The kit carries a registered behavior class; false for Laboratory-only grammar-route wizards.</summary>
        public bool KitImplemented;
    }

    public sealed class LobbySeatWire
    {
        public int Seat;
        public string Name;
        public string WizardId;
        public bool IsBot;
        public bool Joined;
        public bool Connected;
    }

    /// <summary>
    /// Public lobby view: mode, seats, the wizards that may be chosen (limited to implemented kits, or the Laboratory
    /// allow-list), the Laboratory label and rating flag (spec L69, L71), and a notice line.
    /// </summary>
    public sealed class LobbyStateMessage : INetMessage
    {
        public MatchMode Mode { get; set; }
        public int PlayerCount { get; set; }
        public LobbyStatus Status { get; set; }

        /// <summary>Empty for standard rules; otherwise the canonical Laboratory override text (L69).</summary>
        public string LaboratoryLabel { get; set; } = string.Empty;

        public bool CountsForRating { get; set; }

        /// <summary>
        /// The server's settings fingerprint (ruleset, mode, seats, bots, overrides, Conclave settings, spectator delay,
        /// pace, allowed wizards, implemented content), visible before joining (spec L71).
        /// </summary>
        public string SettingsFingerprint { get; set; } = string.Empty;

        public string Notice { get; set; } = string.Empty;
        public List<LobbySeatWire> Seats { get; } = new List<LobbySeatWire>();
        public List<WizardChoiceWire> Wizards { get; } = new List<WizardChoiceWire>();

        public void Serialize(NetWriter w)
        {
            w.WriteByte((byte)Mode);
            w.WriteByte((byte)PlayerCount);
            w.WriteByte((byte)Status);
            w.WriteText(LaboratoryLabel);
            w.WriteBool(CountsForRating);
            w.WriteId(SettingsFingerprint);
            w.WriteText(Notice);
            w.WriteVarUInt((uint)Seats.Count);
            foreach (LobbySeatWire s in Seats)
            {
                w.WriteByte((byte)s.Seat);
                w.WriteText(s.Name);
                w.WriteId(s.WizardId);
                w.WriteByte((byte)((s.IsBot ? 1 : 0) | (s.Joined ? 2 : 0) | (s.Connected ? 4 : 0)));
            }

            w.WriteVarUInt((uint)Wizards.Count);
            foreach (WizardChoiceWire c in Wizards)
            {
                w.WriteId(c.WizardId);
                w.WriteText(c.Name);
                w.WriteByte((byte)c.Routes);
                w.WriteBool(c.KitImplemented);
            }
        }

        public void Deserialize(NetReader r)
        {
            Mode = (MatchMode)r.ReadByteInRange(0, 1);
            PlayerCount = r.ReadByteInRange(2, WireLimits.MaxPlayers);
            Status = (LobbyStatus)r.ReadByteInRange(0, (byte)LobbyStatus.Refused);
            LaboratoryLabel = r.ReadText();
            CountsForRating = r.ReadBool();
            SettingsFingerprint = r.ReadId() ?? string.Empty;
            Notice = r.ReadText();
            int seats = r.ReadCount(WireLimits.MaxPlayers, 4);
            for (int i = 0; i < seats; i++)
            {
                var s = new LobbySeatWire { Seat = r.ReadByteInRange(0, WireLimits.MaxPlayers - 1), Name = r.ReadText(), WizardId = r.ReadId() };
                byte bits = r.ReadByteInRange(0, 7);
                s.IsBot = (bits & 1) != 0;
                s.Joined = (bits & 2) != 0;
                s.Connected = (bits & 4) != 0;
                Seats.Add(s);
            }

            int wizards = r.ReadCount(64, 4);
            for (int i = 0; i < wizards; i++)
            {
                Wizards.Add(new WizardChoiceWire
                {
                    WizardId = r.ReadId(),
                    Name = r.ReadText(),
                    Routes = (InputRoute)r.ReadByteInRange(0, 127),
                    KitImplemented = r.ReadBool(),
                });
            }
        }
    }

    /// <summary>Public seat summary inside the match settings.</summary>
    public sealed class SeatWire
    {
        public int Seat;
        public string Name;
        public string WizardId;
        public bool IsBot;
    }

    /// <summary>
    /// The match settings every client sees before play (spec L71 "a settings fingerprint is visible before joining"):
    /// mode, the receiver's seat, the host and match fingerprints, the Laboratory label, the rating flag, phase lengths,
    /// and the server pace (ticks per wall second; 60 except in the labeled accelerated test mode). The match seed is
    /// never sent: offers derive from it (spec L435).
    /// </summary>
    public sealed class MatchSettingsMessage : INetMessage
    {
        public int YourSeat { get; set; }
        public MatchMode Mode { get; set; }
        public string HostFingerprint { get; set; } = string.Empty;
        public string MatchFingerprint { get; set; } = string.Empty;
        public string RulesetShortHex { get; set; } = string.Empty;
        public string LaboratoryLabel { get; set; } = string.Empty;
        public bool CountsForRating { get; set; }
        public int ShopTicks { get; set; }
        public int DraftTicks { get; set; }
        public int AlcoveTicks { get; set; }
        public int CombatTicks { get; set; }
        public int ResultsTicks { get; set; }
        public int TicksPerWallSecond { get; set; } = 60;
        public bool Accelerated { get; set; }
        public List<SeatWire> Seats { get; } = new List<SeatWire>();

        public void Serialize(NetWriter w)
        {
            w.WriteByte((byte)YourSeat);
            w.WriteByte((byte)Mode);
            w.WriteText(HostFingerprint);
            w.WriteText(MatchFingerprint);
            w.WriteText(RulesetShortHex);
            w.WriteText(LaboratoryLabel);
            w.WriteBool(CountsForRating);
            w.WriteVarUInt((uint)ShopTicks);
            w.WriteVarUInt((uint)DraftTicks);
            w.WriteVarUInt((uint)AlcoveTicks);
            w.WriteVarUInt((uint)CombatTicks);
            w.WriteVarUInt((uint)ResultsTicks);
            w.WriteVarUInt((uint)TicksPerWallSecond);
            w.WriteBool(Accelerated);
            w.WriteVarUInt((uint)Seats.Count);
            foreach (SeatWire s in Seats)
            {
                w.WriteByte((byte)s.Seat);
                w.WriteText(s.Name);
                w.WriteId(s.WizardId);
                w.WriteBool(s.IsBot);
            }
        }

        public void Deserialize(NetReader r)
        {
            YourSeat = r.ReadByteInRange(0, WireLimits.MaxPlayers - 1);
            Mode = (MatchMode)r.ReadByteInRange(0, 1);
            HostFingerprint = r.ReadText();
            MatchFingerprint = r.ReadText();
            RulesetShortHex = r.ReadText();
            LaboratoryLabel = r.ReadText();
            CountsForRating = r.ReadBool();
            ShopTicks = (int)r.ReadVarUInt();
            DraftTicks = (int)r.ReadVarUInt();
            AlcoveTicks = (int)r.ReadVarUInt();
            CombatTicks = (int)r.ReadVarUInt();
            ResultsTicks = (int)r.ReadVarUInt();
            uint pace = r.ReadVarUInt();
            if (pace < 1 || pace > 60 * 64) throw new NetReadException(NetReadError.InvalidValue, "server pace out of range");
            TicksPerWallSecond = (int)pace;
            Accelerated = r.ReadBool();
            int n = r.ReadCount(WireLimits.MaxPlayers, 4);
            for (int i = 0; i < n; i++)
            {
                Seats.Add(new SeatWire { Seat = r.ReadByteInRange(0, WireLimits.MaxPlayers - 1), Name = r.ReadText(), WizardId = r.ReadId(), IsBot = r.ReadBool() });
            }
        }
    }

    /// <summary>A spectator asks to follow a duel (-1 = any running one). Only players outside a live duel may (spec L49, L391).</summary>
    public sealed class SpectateRequest : INetMessage
    {
        public int DuelId { get; set; } = -1;

        public void Serialize(NetWriter writer) => writer.WriteVarInt(DuelId);

        public void Deserialize(NetReader reader) => DuelId = reader.ReadVarIntInRange(-1, int.MaxValue);
    }

    /// <summary>Empty body (surrender request; the request id carries the identity).</summary>
    public sealed class EmptyBody : INetMessage
    {
        public void Serialize(NetWriter writer)
        {
            writer.WriteByte(0);
        }

        public void Deserialize(NetReader reader)
        {
            reader.ReadByteInRange(0, 0);
        }
    }
}
