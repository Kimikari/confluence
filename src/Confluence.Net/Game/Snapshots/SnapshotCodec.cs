using System;
using System.Collections.Generic;
using Confluence.Net.Game.Wire;
using Confluence.Net.Protocol;
using Confluence.Net.Serialization;
using Confluence.Net.Sync;
using Confluence.Sim.Duel;

namespace Confluence.Net.Game.Snapshots
{
    /// <summary>The sections a byte budget may defer (research §2.4.1); everything else is mandatory.</summary>
    public enum BudgetedSection : byte
    {
        Props = 0,
        Volumes = 1,
        Cells = 2,
    }

    /// <summary>
    /// Body layout of a duel snapshot after its <see cref="SnapshotHeader"/>: duel id, match tick, phase and combat
    /// tick, both players in full, the receiver's private state, then the keyed sections as deltas against the baseline
    /// (or in full): the mandatory spell entities, owned fields, and solids (prediction collision depends on solids),
    /// then the budgeted props, obscuration volumes, and reaction cells. A delta can only be decoded on top of the exact
    /// baseline frame the server chose, which is always one the client acknowledged (it keeps the frames it decoded).
    /// <para>
    /// Byte budget (docs/research/netcode.md §2.4.1): a snapshot must fit one unreliable datagram. Removals and all
    /// mandatory parts always go; changed props, volumes, and cells are sent in priority order until the budget is used,
    /// and the rest stay different from the receiver's baseline, so they are offered again in the next snapshot (their
    /// priority grows with every deferral, see GameServer). The frame the encoder returns is exactly what the receiver
    /// decodes, and it becomes the stream's baseline candidate, so partial snapshots never desynchronize deltas. Only a
    /// snapshot whose mandatory part alone exceeds the budget is sent reliably (a true keyframe).
    /// </para>
    /// </summary>
    public static class SnapshotCodec
    {
        /// <summary>Encodes a complete body (no budget); see the budgeted overload.</summary>
        public static void EncodeBody(NetWriter w, DuelFrame frame, DuelFrame baseline, PrivateEntry own)
        {
            EncodeBody(w, frame, baseline, own, int.MaxValue, null, out _, out _);
        }

        /// <summary>
        /// Encodes a body within <paramref name="budgetBytes"/> and returns the frame the receiver will hold after decoding
        /// it (<paramref name="frame"/> itself when nothing was deferred). <paramref name="priority"/> scores a changed
        /// budgeted entry (lower first; null = key order). <paramref name="mandatoryOverBudget"/> reports that even the
        /// mandatory part does not fit: the body is then complete and must travel reliably.
        /// </summary>
        public static DuelFrame EncodeBody(NetWriter w, DuelFrame frame, DuelFrame baseline, PrivateEntry own, int budgetBytes,
            Func<BudgetedSection, FrameEntry, float> priority, out int deferred, out bool mandatoryOverBudget)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (baseline != null && baseline.DuelId != frame.DuelId) throw new ArgumentException("A baseline must belong to the same duel.");
            int start = w.Length;
            w.WriteVarUInt((uint)frame.DuelId);
            w.WriteVarUInt((uint)frame.MatchTick);
            w.WriteByte((byte)frame.Phase);
            w.WriteVarUInt((uint)Math.Max(0, frame.CombatTick));
            for (int i = 0; i < 2; i++)
            {
                if (baseline != null) frame.Players[i].WriteAgainst(w, baseline.Players[i]);
                else frame.Players[i].Write(w);
            }

            w.WriteBool(own != null);
            if (own != null)
            {
                if (baseline?.Own != null) own.WriteAgainst(w, baseline.Own);
                else own.Write(w);
            }

            KeyedSection.WriteDelta(w, frame.Entities, baseline?.Entities);
            KeyedSection.WriteDelta(w, frame.Owned, baseline?.Owned);
            KeyedSection.WriteDelta(w, frame.Solids, baseline?.Solids);

            var propsRemoved = new List<int>();
            var propsUp = new List<PropEntry>();
            var volumesRemoved = new List<int>();
            var volumesUp = new List<VolumeEntry>();
            var cellsRemoved = new List<int>();
            var cellsUp = new List<CellEntry>();
            KeyedSection.Diff(frame.Props, baseline?.Props, propsRemoved, propsUp);
            KeyedSection.Diff(frame.Volumes, baseline?.Volumes, volumesRemoved, volumesUp);
            KeyedSection.Diff(frame.Cells, baseline?.Cells, cellsRemoved, cellsUp);

            // Fixed cost of the three budgeted sections: their removals and both counts (upper bound).
            int fixedBytes = RemovalBytes(propsRemoved) + RemovalBytes(volumesRemoved) + RemovalBytes(cellsRemoved) + 3 * 3;
            int remaining = budgetBytes == int.MaxValue ? int.MaxValue : budgetBytes - (w.Length - start) - fixedBytes;
            deferred = 0;
            mandatoryOverBudget = remaining < 0;
            DuelFrame held = frame;
            if (!mandatoryOverBudget && remaining != int.MaxValue)
            {
                var scratch = new NetWriter(256);
                var candidates = new List<Candidate>(propsUp.Count + volumesUp.Count + cellsUp.Count);
                long total = 0;
                AddCandidates(candidates, BudgetedSection.Props, propsUp, baseline?.Props, scratch, ref total);
                AddCandidates(candidates, BudgetedSection.Volumes, volumesUp, baseline?.Volumes, scratch, ref total);
                AddCandidates(candidates, BudgetedSection.Cells, cellsUp, baseline?.Cells, scratch, ref total);
                if (total > remaining)
                {
                    foreach (Candidate c in candidates) c.Score = priority != null ? priority(c.Section, c.Entry) : c.Entry.Key;
                    candidates.Sort(CompareCandidates);
                    var keep = new HashSet<FrameEntry>();
                    int left = remaining;
                    foreach (Candidate c in candidates)
                    {
                        if (c.Bytes > left) continue;
                        left -= c.Bytes;
                        keep.Add(c.Entry);
                    }

                    deferred = candidates.Count - keep.Count;
                    propsUp.RemoveAll(e => !keep.Contains(e));
                    volumesUp.RemoveAll(e => !keep.Contains(e));
                    cellsUp.RemoveAll(e => !keep.Contains(e));
                    held = frame.ShallowCopy();
                    held.Props = KeyedSection.Apply(baseline?.Props, propsRemoved, propsUp);
                    held.Volumes = KeyedSection.Apply(baseline?.Volumes, volumesRemoved, volumesUp);
                    held.Cells = KeyedSection.Apply(baseline?.Cells, cellsRemoved, cellsUp);
                }
            }

            KeyedSection.Write(w, propsRemoved, propsUp, baseline?.Props);
            KeyedSection.Write(w, volumesRemoved, volumesUp, baseline?.Volumes);
            KeyedSection.Write(w, cellsRemoved, cellsUp, baseline?.Cells);
            if (held == frame) frame.Own = own;
            else held.Own = own;
            return held;
        }

        private sealed class Candidate
        {
            public BudgetedSection Section;
            public FrameEntry Entry;
            public int Bytes;
            public float Score;
        }

        private static int CompareCandidates(Candidate a, Candidate b)
        {
            if (a.Score != b.Score) return a.Score.CompareTo(b.Score);
            if (a.Section != b.Section) return a.Section.CompareTo(b.Section);
            return a.Entry.Key.CompareTo(b.Entry.Key);
        }

        private static void AddCandidates<T>(List<Candidate> into, BudgetedSection section, List<T> upserts, List<T> baseline, NetWriter scratch, ref long total) where T : FrameEntry
        {
            foreach (T e in upserts)
            {
                scratch.Reset();
                T previous = KeyedSection.Find(baseline, e.Key);
                if (previous != null) e.WriteBodyAgainst(scratch, previous);
                else e.WriteBody(scratch);
                int bytes = scratch.Length + KeyedSection.VarUIntSize((uint)e.Key + 1u);
                total += bytes;
                into.Add(new Candidate { Section = section, Entry = e, Bytes = bytes });
            }
        }

        private static int RemovalBytes(List<int> removed)
        {
            int bytes = 0;
            int last = -1;
            foreach (int key in removed)
            {
                bytes += KeyedSection.VarUIntSize((uint)(key - last - 1));
                last = key;
            }

            return bytes;
        }

        /// <summary>
        /// Decodes a body. <paramref name="baseline"/> must be the frame of <c>header.BaselineTick</c> when the header has
        /// one. Throws <see cref="NetReadException"/> on malformed bytes or a missing or mismatched baseline.
        /// </summary>
        /// <summary>The duel id a body starts with, or -1 when it cannot be read (the client routes early snapshots by it).</summary>
        public static int PeekDuelId(byte[] body, int length)
        {
            if (body == null) return -1;
            try
            {
                var r = new NetReader(body, 0, length >= 0 ? Math.Min(length, body.Length) : body.Length);
                uint duel = r.ReadVarUInt();
                return duel > int.MaxValue ? -1 : (int)duel;
            }
            catch (NetReadException)
            {
                return -1;
            }
        }

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
            for (int i = 0; i < 2; i++) f.Players[i] = baseline != null ? PlayerEntry.ReadAgainst(r, baseline.Players[i]) : PlayerEntry.Read(r);
            own = null;
            if (r.ReadBool()) own = baseline?.Own != null ? PrivateEntry.ReadAgainst(r, baseline.Own) : PrivateEntry.Read(r);
            f.Own = own;
            f.Entities = KeyedSection.ReadDelta(r, baseline?.Entities, WireLimits.MaxEntities, EntityEntry.Read, EntityEntry.ReadAgainst);
            f.Owned = KeyedSection.ReadDelta(r, baseline?.Owned, WireLimits.MaxEntities, OwnedEntry.Read, OwnedEntry.ReadAgainst);
            f.Solids = KeyedSection.ReadDelta(r, baseline?.Solids, WireLimits.MaxSolids, SolidEntry.Read);
            f.Props = KeyedSection.ReadDelta(r, baseline?.Props, WireLimits.MaxProps, PropEntry.Read, PropEntry.ReadAgainst);
            f.Volumes = KeyedSection.ReadDelta(r, baseline?.Volumes, WireLimits.MaxVolumes, VolumeEntry.Read, VolumeEntry.ReadAgainst);
            f.Cells = KeyedSection.ReadDelta(r, baseline?.Cells, WireLimits.MaxCells, CellEntry.Read);
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
