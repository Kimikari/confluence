using System;
using Confluence.Net.Game.Wire;
using Confluence.Net.Protocol;
using Confluence.Net.Serialization;
using Confluence.Net.Sync;
using Confluence.Sim.Duel;

namespace Confluence.Net.Game.Snapshots
{
    /// <summary>
    /// Body layout of a duel snapshot after its <see cref="SnapshotHeader"/>: duel id, match tick, phase and combat
    /// tick, both players in full, the six keyed sections as deltas against the baseline (or in full), and the
    /// receiver's private state. A delta can only be decoded on top of the exact baseline frame the server chose, which
    /// is always one the client acknowledged (it keeps the frames it decoded).
    /// </summary>
    public static class SnapshotCodec
    {
        public static void EncodeBody(NetWriter w, DuelFrame frame, DuelFrame baseline, PrivateEntry own)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (baseline != null && baseline.DuelId != frame.DuelId) throw new ArgumentException("A baseline must belong to the same duel.");
            w.WriteVarUInt((uint)frame.DuelId);
            w.WriteVarUInt((uint)frame.MatchTick);
            w.WriteByte((byte)frame.Phase);
            w.WriteVarUInt((uint)Math.Max(0, frame.CombatTick));
            frame.Players[0].Write(w);
            frame.Players[1].Write(w);
            KeyedSection.WriteDelta(w, frame.Entities, baseline?.Entities);
            KeyedSection.WriteDelta(w, frame.Owned, baseline?.Owned);
            KeyedSection.WriteDelta(w, frame.Volumes, baseline?.Volumes);
            KeyedSection.WriteDelta(w, frame.Cells, baseline?.Cells);
            KeyedSection.WriteDelta(w, frame.Props, baseline?.Props);
            KeyedSection.WriteDelta(w, frame.Solids, baseline?.Solids);
            w.WriteBool(own != null);
            own?.Write(w);
        }

        /// <summary>
        /// Decodes a body. <paramref name="baseline"/> must be the frame of <c>header.BaselineTick</c> when the header has
        /// one. Throws <see cref="NetReadException"/> on malformed bytes or a missing or mismatched baseline.
        /// </summary>
        public static DuelFrame DecodeBody(NetReader r, SnapshotHeader header, DuelFrame baseline, out PrivateEntry own)
        {
            if (header.HasBaseline)
            {
                if (baseline == null || baseline.Tick != header.BaselineTick)
                {
                    throw new NetReadException(NetReadError.InvalidValue, "snapshot baseline " + header.BaselineTick + " is not held");
                }
            }
            else
            {
                baseline = null;
            }

            var f = new DuelFrame();
            uint duel = r.ReadVarUInt();
            if (duel > int.MaxValue) throw new NetReadException(NetReadError.InvalidValue, "duel id out of range");
            f.DuelId = (int)duel;
            if (baseline != null && baseline.DuelId != f.DuelId) throw new NetReadException(NetReadError.InvalidValue, "baseline of another duel");
            uint matchTick = r.ReadVarUInt();
            if (matchTick > int.MaxValue) throw new NetReadException(NetReadError.InvalidValue, "match tick out of range");
            f.MatchTick = (int)matchTick;
            f.Tick = header.ServerTick;
            f.Phase = (DuelPhase)r.ReadByteInRange(0, (byte)DuelPhase.Ended);
            uint combat = r.ReadVarUInt();
            if (combat > (uint)f.Tick) throw new NetReadException(NetReadError.InvalidValue, "combat tick after the duel tick");
            f.CombatTick = (int)combat;
            f.Players[0] = PlayerEntry.Read(r);
            f.Players[1] = PlayerEntry.Read(r);
            f.Entities = KeyedSection.ReadDelta(r, baseline?.Entities, WireLimits.MaxEntities, EntityEntry.Read);
            f.Owned = KeyedSection.ReadDelta(r, baseline?.Owned, WireLimits.MaxEntities, OwnedEntry.Read);
            f.Volumes = KeyedSection.ReadDelta(r, baseline?.Volumes, WireLimits.MaxVolumes, VolumeEntry.Read);
            f.Cells = KeyedSection.ReadDelta(r, baseline?.Cells, WireLimits.MaxCells, CellEntry.Read);
            f.Props = KeyedSection.ReadDelta(r, baseline?.Props, WireLimits.MaxProps, PropEntry.Read);
            f.Solids = KeyedSection.ReadDelta(r, baseline?.Solids, WireLimits.MaxSolids, SolidEntry.Read);
            own = r.ReadBool() ? PrivateEntry.Read(r) : null;
            return f;
        }

        /// <summary>True when both frames replicate the same public state (tests, diagnostics).</summary>
        public static bool SameState(DuelFrame a, DuelFrame b)
        {
            return a.DuelId == b.DuelId && a.Tick == b.Tick && a.MatchTick == b.MatchTick && a.Phase == b.Phase && a.CombatTick == b.CombatTick &&
                a.Players[0].SameAs(b.Players[0]) && a.Players[1].SameAs(b.Players[1]) &&
                KeyedSection.SameSection(a.Entities, b.Entities) && KeyedSection.SameSection(a.Owned, b.Owned) &&
                KeyedSection.SameSection(a.Volumes, b.Volumes) && KeyedSection.SameSection(a.Cells, b.Cells) &&
                KeyedSection.SameSection(a.Props, b.Props) && KeyedSection.SameSection(a.Solids, b.Solids);
        }
    }

    /// <summary>
    /// A duel snapshot on the sequenced Snapshot lane: the header (duel tick, last processed input of the receiver,
    /// baseline tick) and the encoded body, decoded by the receiver against its own baseline frames (the registry only
    /// checks the header; <see cref="SnapshotCodec.DecodeBody"/> validates the rest).
    /// </summary>
    public sealed class SnapshotMessage : INetMessage
    {
        public SnapshotHeader Header { get; set; }

        public byte[] Body { get; set; } = new byte[0];

        public int BodyLength { get; set; } = -1;

        /// <summary>
        /// The receiver's server-side input queue depth after this step (commands waiting), capped at 255: the client's
        /// input lead control holds it near one tick (docs/research/netcode.md §2.1.1).
        /// </summary>
        public int InputQueue { get; set; }

        /// <summary>Server steps of this duel that found no new input from the receiver (cumulative).</summary>
        public uint InputStarved { get; set; }

        public void Serialize(NetWriter writer)
        {
            Header.Write(writer);
            writer.WriteByte((byte)Math.Max(0, Math.Min(255, InputQueue)));
            writer.WriteVarUInt(InputStarved);
            writer.WriteBytes(Body, 0, BodyLength >= 0 ? BodyLength : Body.Length);
        }

        public void Deserialize(NetReader reader)
        {
            Header = SnapshotHeader.Read(reader);
            InputQueue = reader.ReadByte();
            InputStarved = reader.ReadVarUInt();
            Body = reader.RemainingToArray();
            BodyLength = Body.Length;
            reader.Skip(reader.Remaining);
        }
    }
}
