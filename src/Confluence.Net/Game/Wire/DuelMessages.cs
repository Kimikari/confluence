using System;
using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Net.Protocol;
using Confluence.Net.Serialization;

namespace Confluence.Net.Game.Wire
{
    /// <summary>One side's final duel loadout (public from the alcove on, spec L46).</summary>
    public sealed class DuelSideWire
    {
        public int Seat;
        public string Name;
        public string WizardId;
        public List<string> SpellIds = new List<string>();
        public List<string> EquipmentIds = new List<string>();
        public List<EquipmentSlot> EquipmentSlots = new List<EquipmentSlot>();
        public List<string> AugmentIds = new List<string>();
        public List<int> AugmentRanks = new List<int>();
        public string EnabledActiveId;
        public YawBox Alcove;
    }

    /// <summary>
    /// A duel world was created for the receiver (alcove start, spec L46): duel id, pass, arena, the receiver's spawn
    /// side, phase lengths, the match tick of the world's tick 0, and both sides' final loadouts and alcove boxes.
    /// </summary>
    public sealed class DuelStartMessage : INetMessage
    {
        public int DuelId { get; set; }
        public int Round { get; set; }
        public int Attempt { get; set; }
        public string ArenaId { get; set; }
        public int YourSide { get; set; }
        public int AlcoveTicks { get; set; }
        public int CombatTicks { get; set; }

        /// <summary>Match tick at which the duel world was at tick 0.</summary>
        public int StartMatchTick { get; set; }

        public DuelSideWire[] Sides { get; } = { new DuelSideWire(), new DuelSideWire() };

        public void Serialize(NetWriter w)
        {
            w.WriteVarUInt((uint)DuelId);
            w.WriteVarUInt((uint)Round);
            w.WriteVarUInt((uint)Attempt);
            w.WriteId(ArenaId);
            w.WriteByte((byte)YourSide);
            w.WriteVarUInt((uint)AlcoveTicks);
            w.WriteVarUInt((uint)CombatTicks);
            w.WriteVarUInt((uint)StartMatchTick);
            foreach (DuelSideWire s in Sides)
            {
                w.WriteByte((byte)s.Seat);
                w.WriteText(s.Name);
                w.WriteId(s.WizardId);
                w.WriteIdList(s.SpellIds);
                w.WriteIdList(s.EquipmentIds);
                foreach (EquipmentSlot slot in s.EquipmentSlots) w.WriteByte((byte)slot);
                w.WriteIdList(s.AugmentIds);
                foreach (int rank in s.AugmentRanks) w.WriteByte((byte)rank);
                w.WriteId(s.EnabledActiveId);
                w.WriteYawBox(s.Alcove);
            }
        }

        public void Deserialize(NetReader r)
        {
            DuelId = (int)r.ReadVarUInt();
            Round = (int)r.ReadVarUInt();
            Attempt = (int)r.ReadVarUInt();
            ArenaId = r.ReadId();
            YourSide = r.ReadByteInRange(0, 1);
            AlcoveTicks = (int)r.ReadVarUInt();
            CombatTicks = (int)r.ReadVarUInt();
            StartMatchTick = (int)r.ReadVarUInt();
            foreach (DuelSideWire s in Sides)
            {
                s.Seat = r.ReadByteInRange(0, WireLimits.MaxPlayers - 1);
                s.Name = r.ReadText();
                s.WizardId = r.ReadId();
                s.SpellIds = r.ReadIdList();
                s.EquipmentIds = r.ReadIdList();
                s.EquipmentSlots = new List<EquipmentSlot>();
                for (int i = 0; i < s.EquipmentIds.Count; i++) s.EquipmentSlots.Add((EquipmentSlot)r.ReadByteInRange(0, (byte)EquipmentSlot.Consumable));
                s.AugmentIds = r.ReadIdList();
                s.AugmentRanks = new List<int>();
                for (int i = 0; i < s.AugmentIds.Count; i++) s.AugmentRanks.Add(r.ReadByteInRange(1, 2));
                s.EnabledActiveId = r.ReadId();
                s.Alcove = r.ReadYawBox();
            }
        }
    }

    /// <summary>Wire categories of duel events: the Sim's <c>DuelEventType</c> values plus reaction outcomes.</summary>
    public enum WireEventType : byte
    {
        Phase = 0,
        Damage,
        Heal,
        Shield,
        Status,
        Guard,
        Melee,
        Movement,
        Field,
        Solid,
        Boundary,
        Resource,
        DuelEnd,
        Cast,
        Spell,
        Prop,
        Effect,

        /// <summary>A Reactions outcome (steam, ice, charge, ignition...; spec L134-L161), from the world's reaction step.</summary>
        Reaction,

        /// <summary>
        /// A public world marker a record leaves for both players (Sim <c>MarkerEvent</c>: AUG-043's trail, AUG-105's exit):
        /// Position = its start, Direction = its extent (end = Position + Direction), Extra = its duration in ticks, Text =
        /// its cue name.
        /// </summary>
        Marker,

        /// <summary>A kit's public world cue (Sim <c>IPublicDuelCue</c>, e.g. WIZ-010's bubble, stream and vault cues): Kind = the record's cue kind.</summary>
        Cue,
    }

    /// <summary>
    /// One authoritative duel event on the reliable Events lane (spec L419: casts, telegraphs, damage confirmations for
    /// real hits only, reactions, deaths). A compact union: <see cref="Kind"/> is the event's own change or kind enum,
    /// and the optional fields carry its data. Hidden details are removed per viewer before encoding (see
    /// <c>DuelEventFilter</c>).
    /// </summary>
    public sealed class WireDuelEvent
    {
        private const ushort HasTarget = 1;
        private const ushort HasSource = 2;
        private const ushort HasAmount = 4;
        private const ushort HasAmount2 = 8;
        private const ushort HasPosition = 16;
        private const ushort HasDirection = 32;
        private const ushort HasEntity = 64;
        private const ushort HasExtra = 128;
        private const ushort HasText = 256;

        public int Id;
        public int Tick;
        public WireEventType Type;
        public int Player = -1;
        public byte Kind;
        public int Target = -1;
        public string SourceId;
        public float Amount;
        public float Amount2;
        public bool PositionSet;
        public Vec3 Position;
        public bool DirectionSet;
        public Vec3 Direction;
        public int EntityId;
        public int Extra;
        public string Text;

        public void Write(NetWriter w)
        {
            ushort mask = 0;
            if (Target >= 0) mask |= HasTarget;
            if (!string.IsNullOrEmpty(SourceId)) mask |= HasSource;
            if (Amount != 0f) mask |= HasAmount;
            if (Amount2 != 0f) mask |= HasAmount2;
            if (PositionSet) mask |= HasPosition;
            if (DirectionSet) mask |= HasDirection;
            if (EntityId != 0) mask |= HasEntity;
            if (Extra != 0) mask |= HasExtra;
            if (!string.IsNullOrEmpty(Text)) mask |= HasText;
            w.WriteVarUInt((uint)Id);
            w.WriteVarUInt((uint)Math.Max(0, Tick));
            w.WriteByte((byte)Type);
            w.WriteSeatSide(Player);
            w.WriteByte(Kind);
            w.WriteVarUInt(mask);
            if ((mask & HasTarget) != 0) w.WriteSeatSide(Target);
            if ((mask & HasSource) != 0) w.WriteId(SourceId);
            if ((mask & HasAmount) != 0) w.WriteFloat(Amount);
            if ((mask & HasAmount2) != 0) w.WriteFloat(Amount2);
            if ((mask & HasPosition) != 0) w.WritePosition(Position);
            if ((mask & HasDirection) != 0) w.WritePosition(Direction);
            if ((mask & HasEntity) != 0) w.WriteVarInt(EntityId);
            if ((mask & HasExtra) != 0) w.WriteVarInt(Extra);
            if ((mask & HasText) != 0) w.WriteText(Text);
        }

        public static WireDuelEvent Read(NetReader r)
        {
            var e = new WireDuelEvent
            {
                Id = (int)Math.Min(int.MaxValue, r.ReadVarUInt()),
                Tick = (int)Math.Min(int.MaxValue, r.ReadVarUInt()),
                Type = (WireEventType)r.ReadByteInRange(0, (byte)WireEventType.Cue),
                Player = r.ReadSeatSide(),
                Kind = r.ReadByte(),
            };
            uint mask = r.ReadVarUInt();
            if (mask > 511) throw new NetReadException(NetReadError.InvalidValue, "unknown duel event fields");
            if ((mask & HasTarget) != 0) e.Target = r.ReadSeatSide();
            if ((mask & HasSource) != 0) e.SourceId = r.ReadId();
            if ((mask & HasAmount) != 0) e.Amount = r.ReadFloat();
            if ((mask & HasAmount2) != 0) e.Amount2 = r.ReadFloat();
            if ((mask & HasPosition) != 0)
            {
                e.PositionSet = true;
                e.Position = r.ReadPosition();
            }

            if ((mask & HasDirection) != 0)
            {
                e.DirectionSet = true;
                e.Direction = r.ReadPosition();
            }

            if ((mask & HasEntity) != 0) e.EntityId = r.ReadVarInt();
            if ((mask & HasExtra) != 0) e.Extra = r.ReadVarInt();
            if ((mask & HasText) != 0) e.Text = r.ReadText();
            return e;
        }

        public override string ToString() => "#" + Id + " t" + Tick + " " + Type + ":" + Kind + " p" + Player + (SourceId != null ? " " + SourceId : "");
    }

    internal static class SideWire
    {
        /// <summary>A world side 0/1, or -1.</summary>
        public static void WriteSeatSide(this NetWriter w, int side) => w.WriteVarInt(side < 0 ? -1 : Math.Min(side, 7));

        public static int ReadSeatSide(this NetReader r) => r.ReadVarIntInRange(-1, 7);
    }

    /// <summary>The events of one or more duel steps, in simulation order.</summary>
    public sealed class DuelEventsMessage : INetMessage
    {
        public int DuelId { get; set; }
        public List<WireDuelEvent> Events { get; } = new List<WireDuelEvent>();

        public void Serialize(NetWriter w)
        {
            w.WriteVarUInt((uint)DuelId);
            w.WriteVarUInt((uint)Events.Count);
            foreach (WireDuelEvent e in Events) e.Write(w);
        }

        public void Deserialize(NetReader r)
        {
            DuelId = (int)Math.Min(int.MaxValue, r.ReadVarUInt());
            int n = r.ReadCount(WireLimits.MaxDuelEventsPerMessage, 6);
            for (int i = 0; i < n; i++) Events.Add(WireDuelEvent.Read(r));
        }
    }

    /// <summary>
    /// A duelist's results screen data (spec L49, L359): outcome, reason, health fractions, both sides' verified feats
    /// and decisive sequences (filtered like live events for what the receiver may know).
    /// </summary>
    public sealed class DuelReportMessage : INetMessage
    {
        public int DuelId { get; set; }

        /// <summary>Winning side, or -1 for a draw.</summary>
        public int WinnerSide { get; set; } = -1;

        public byte Reason { get; set; }
        public float Health0 { get; set; }
        public float Health1 { get; set; }
        public int FinalTick { get; set; }
        public List<string>[] Feats { get; } = { new List<string>(), new List<string>() };
        public List<WireDuelEvent>[] Decisive { get; } = { new List<WireDuelEvent>(), new List<WireDuelEvent>() };

        public void Serialize(NetWriter w)
        {
            w.WriteVarUInt((uint)DuelId);
            w.WriteVarInt(WinnerSide);
            w.WriteByte(Reason);
            w.WriteFloat(Health0);
            w.WriteFloat(Health1);
            w.WriteVarUInt((uint)FinalTick);
            for (int s = 0; s < 2; s++)
            {
                w.WriteIdList(Feats[s]);
                w.WriteVarUInt((uint)Decisive[s].Count);
                foreach (WireDuelEvent e in Decisive[s]) e.Write(w);
            }
        }

        public void Deserialize(NetReader r)
        {
            DuelId = (int)Math.Min(int.MaxValue, r.ReadVarUInt());
            WinnerSide = r.ReadVarIntInRange(-1, 1);
            Reason = r.ReadByteInRange(0, 3);
            Health0 = r.ReadFloat();
            Health1 = r.ReadFloat();
            FinalTick = (int)Math.Min(int.MaxValue, r.ReadVarUInt());
            for (int s = 0; s < 2; s++)
            {
                Feats[s] = r.ReadIdList();
                int n = r.ReadCount(WireLimits.MaxDuelEventsPerMessage, 6);
                for (int i = 0; i < n; i++) Decisive[s].Add(WireDuelEvent.Read(r));
            }
        }
    }

    /// <summary>
    /// Delayed spectator data (spec L391: at least 15 s behind while the match is active; L40 eliminated players see
    /// only public loadouts and delayed combat): a full public frame of one duel plus the public events since the
    /// previous spectator frame. Reliable (Bulk lane), so it may exceed one datagram.
    /// </summary>
    public sealed class SpectatorFrameMessage : INetMessage
    {
        public int DuelId { get; set; }
        public int Side0Seat { get; set; }
        public int Side1Seat { get; set; }
        public bool DuelEnded { get; set; }

        /// <summary>Encoded snapshot body (full, no private part) of the delayed tick; see <see cref="Snapshots.SnapshotCodec"/>.</summary>
        public int FrameTick { get; set; }

        public byte[] FrameBody { get; set; } = new byte[0];
        public List<WireDuelEvent> Events { get; } = new List<WireDuelEvent>();

        public void Serialize(NetWriter w)
        {
            w.WriteVarUInt((uint)DuelId);
            w.WriteByte((byte)Side0Seat);
            w.WriteByte((byte)Side1Seat);
            w.WriteBool(DuelEnded);
            w.WriteVarUInt((uint)FrameTick);
            w.WriteVarUInt((uint)Events.Count);
            foreach (WireDuelEvent e in Events) e.Write(w);
            w.WriteBlob(FrameBody, 0, FrameBody.Length);
        }

        public void Deserialize(NetReader r)
        {
            DuelId = (int)Math.Min(int.MaxValue, r.ReadVarUInt());
            Side0Seat = r.ReadByteInRange(0, WireLimits.MaxPlayers - 1);
            Side1Seat = r.ReadByteInRange(0, WireLimits.MaxPlayers - 1);
            DuelEnded = r.ReadBool();
            FrameTick = (int)Math.Min(int.MaxValue, r.ReadVarUInt());
            int n = r.ReadCount(WireLimits.MaxDuelEventsPerMessage * 4, 6);
            for (int i = 0; i < n; i++) Events.Add(WireDuelEvent.Read(r));
            FrameBody = r.ReadBlob(1 << 20);
        }
    }
}
