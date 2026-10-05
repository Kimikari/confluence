using System;
using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Core.Geometry;
using Confluence.Net.Protocol;
using Confluence.Net.Serialization;
using Confluence.Net.Sync;
using Confluence.Sim.Input;

namespace Confluence.Net.Game.Wire
{
    /// <summary>
    /// Wire codec of one <see cref="PlayerInput"/> (spec L419-L425): the intended tick (delta to the previous command
    /// of the packet), movement axes (8 bits each), view yaw and pitch (16 bits each), the unified
    /// <see cref="InputButtons"/> held and tapped sets by value (bits outside <see cref="InputButtons.All"/> are
    /// malformed), the selected slot, the viewed tick for lag compensation (as a lag behind the command tick, L423), and
    /// the client-originated grammar events (syllables, rhythm taps, strokes, trace steps, widget picks; L365-L369,
    /// L490). Combat events are never read from clients (the server generates them).
    /// <para>
    /// The client predicts with <see cref="Canonicalize"/>d inputs, the exact values the server will decode, so the
    /// shared motor steps identically on both sides.
    /// </para>
    /// </summary>
    public sealed class PlayerInputCodec : IInputCodec<PlayerInput>
    {
        /// <summary>A command may view at most this far into the past (10 s); the Sim further clamps to 150 ms (L423).</summary>
        public const int MaxViewLagTicks = 600;

        /// <summary>Grammar events may be stamped at most this far from their command's tick.</summary>
        public const int MaxEventTickOffset = 600;

        private const byte FlagView = 1;
        private const byte FlagEvents = 2;
        private const byte FlagTapped = 4;
        private const float StrokeMin = -0.5f;
        private const float StrokeMax = 1.5f;

        public static readonly PlayerInputCodec Instance = new PlayerInputCodec();

        [ThreadStatic] private static NetWriter _scratchWriter;
        [ThreadStatic] private static NetReader _scratchReader;

        public void Write(NetWriter writer, PlayerInput value, PlayerInput previous)
        {
            writer.WriteVarInt(unchecked(value.Tick - previous.Tick));
            writer.WriteSByte(QuantizeAxis(value.MoveX));
            writer.WriteSByte(QuantizeAxis(value.MoveY));
            writer.WriteUShort(Quantization.QuantizeAngle(Finite(value.YawDeg)));
            writer.WriteUShort(Quantization.QuantizePitch(Finite(value.PitchDeg)));
            writer.WriteVarUInt((uint)(value.Buttons & InputButtons.All));

            int lag = value.ViewTick > 0 ? value.Tick - value.ViewTick : -1;
            bool hasView = value.ViewTick > 0 && lag >= 0 && lag <= MaxViewLagTicks;
            int events = value.GrammarEvents == null ? 0 : Math.Min(value.GrammarEvents.Count, WireLimits.MaxGrammarEventsPerInput);
            InputButtons tapped = value.Tapped & InputButtons.All;
            byte flags = (byte)((hasView ? FlagView : 0) | (events > 0 ? FlagEvents : 0) | (tapped != InputButtons.None ? FlagTapped : 0));
            writer.WriteByte(flags);
            writer.WriteByte((byte)(value.SelectedSlot >= 0 && value.SelectedSlot < PlayerInput.SlotCount ? value.SelectedSlot : 0));
            if (tapped != InputButtons.None) writer.WriteVarUInt((uint)tapped);
            if (hasView) writer.WriteVarUInt((uint)lag);
            if (events > 0)
            {
                writer.WriteVarUInt((uint)events);
                for (int i = 0; i < events; i++) WriteEvent(writer, value.GrammarEvents[i], value.Tick);
            }
        }

        public PlayerInput Read(NetReader reader, PlayerInput previous)
        {
            var input = new PlayerInput();
            long tick = (long)previous.Tick + reader.ReadVarInt();
            if (tick < 0 || tick > int.MaxValue) throw new NetReadException(NetReadError.InvalidValue, "input tick out of range");
            input.Tick = (int)tick;
            input.MoveX = ReadAxis(reader);
            input.MoveY = ReadAxis(reader);
            input.YawDeg = Quantization.DequantizeAngle(reader.ReadUShort());
            input.PitchDeg = Quantization.DequantizePitch(reader.ReadUShort());
            input.Buttons = ReadButtons(reader);
            byte flags = reader.ReadByte();
            if ((flags & ~(FlagView | FlagEvents | FlagTapped)) != 0) throw new NetReadException(NetReadError.InvalidValue, "unknown input flags");
            input.SelectedSlot = reader.ReadByteInRange(0, PlayerInput.SlotCount - 1);
            if ((flags & FlagTapped) != 0)
            {
                input.Tapped = ReadButtons(reader);
                if (input.Tapped == InputButtons.None) throw new NetReadException(NetReadError.InvalidValue, "empty tapped set");
            }

            if ((flags & FlagView) != 0)
            {
                uint lag = reader.ReadVarUInt();
                if (lag > MaxViewLagTicks || lag > (uint)input.Tick) throw new NetReadException(NetReadError.InvalidValue, "view tick out of range");
                input.ViewTick = input.Tick - (int)lag;
                if (input.ViewTick <= 0) input.ViewTick = 0;
            }

            if ((flags & FlagEvents) != 0)
            {
                int count = reader.ReadCount(WireLimits.MaxGrammarEventsPerInput, 6);
                if (count == 0) throw new NetReadException(NetReadError.InvalidValue, "empty grammar event list");
                var list = new GrammarInputEvent[count];
                for (int i = 0; i < count; i++) list[i] = ReadEvent(reader, input.Tick);
                input.GrammarEvents = list;
            }

            return input;
        }

        /// <summary>The input exactly as the server will decode it (quantized axes, angles, and grammar samples).</summary>
        public static PlayerInput Canonicalize(PlayerInput input)
        {
            NetWriter w = _scratchWriter ?? (_scratchWriter = new NetWriter(128));
            NetReader r = _scratchReader ?? (_scratchReader = new NetReader());
            w.Reset();
            Instance.Write(w, input, default);
            r.Reset(w.Buffer, 0, w.Length);
            PlayerInput canonical = Instance.Read(r, default);
            canonical.Sequence = input.Sequence;
            return canonical;
        }

        /// <summary>The sanitization DuelWorld applies in tick-order step 1, for client prediction (same clamps and wraps).</summary>
        public static PlayerInput SanitizeLikeServer(PlayerInput input, int previousSlot)
        {
            input.MoveX = CMath.Clamp(Finite(input.MoveX), -1f, 1f);
            input.MoveY = CMath.Clamp(Finite(input.MoveY), -1f, 1f);
            input.YawDeg = CMath.WrapAngle(Finite(input.YawDeg));
            input.PitchDeg = CMath.Clamp(Finite(input.PitchDeg), -89f, 89f);
            if (input.SelectedSlot < 0 || input.SelectedSlot >= PlayerInput.SlotCount) input.SelectedSlot = previousSlot;
            return input;
        }

        private static void WriteEvent(NetWriter writer, GrammarInputEvent e, int commandTick)
        {
            byte kind = (byte)e.Kind;
            if (kind > (byte)GrammarInputKind.WidgetSelect) kind = (byte)GrammarInputKind.WidgetSelect;
            writer.WriteByte(kind);
            long offset = (long)e.Tick - commandTick;
            if (offset < -MaxEventTickOffset) offset = -MaxEventTickOffset;
            if (offset > MaxEventTickOffset) offset = MaxEventTickOffset;
            writer.WriteVarInt((int)offset);
            writer.WriteVarUInt((uint)Math.Max(0, Math.Min(GrammarInputEvent.SubTickResolution - 1, e.SubTick)));
            writer.WriteVarInt(e.Value);
            writer.WriteByte((byte)Quantization.QuantizeRange(Finite(e.Confidence), 0f, 1f, 8));
            writer.WriteUShort((ushort)Quantization.QuantizeRange(Finite(e.X), StrokeMin, StrokeMax, 16));
            writer.WriteUShort((ushort)Quantization.QuantizeRange(Finite(e.Y), StrokeMin, StrokeMax, 16));
        }

        private static GrammarInputEvent ReadEvent(NetReader reader, int commandTick)
        {
            var kind = (GrammarInputKind)reader.ReadByteInRange(0, (byte)GrammarInputKind.WidgetSelect);
            int offset = reader.ReadVarIntInRange(-MaxEventTickOffset, MaxEventTickOffset);
            long tick = (long)commandTick + offset;
            if (tick < 0) tick = 0;
            uint sub = reader.ReadVarUInt();
            if (sub >= GrammarInputEvent.SubTickResolution) throw new NetReadException(NetReadError.InvalidValue, "sub-tick out of range");
            int value = reader.ReadVarInt();
            if (kind == GrammarInputKind.DirectionTrace && (value < 0 || value > (int)TraceDirection.UpLeft))
            {
                throw new NetReadException(NetReadError.InvalidValue, "trace direction out of range");
            }

            var e = new GrammarInputEvent(kind, (int)tick, (int)sub, value);
            e.Confidence = Quantization.DequantizeRange(reader.ReadByte(), 0f, 1f, 8);
            e.X = Quantization.DequantizeRange(reader.ReadUShort(), StrokeMin, StrokeMax, 16);
            e.Y = Quantization.DequantizeRange(reader.ReadUShort(), StrokeMin, StrokeMax, 16);
            return e;
        }

        private static InputButtons ReadButtons(NetReader reader)
        {
            uint bits = reader.ReadVarUInt();
            if ((bits & ~(uint)InputButtons.All) != 0) throw new NetReadException(NetReadError.InvalidValue, "undefined input buttons");
            return (InputButtons)bits;
        }

        private static sbyte QuantizeAxis(float v)
        {
            v = CMath.Clamp(Finite(v), -1f, 1f);
            return (sbyte)Math.Round(v * 127f, MidpointRounding.AwayFromZero);
        }

        private static float ReadAxis(NetReader reader)
        {
            sbyte q = reader.ReadSByte();
            if (q == sbyte.MinValue) throw new NetReadException(NetReadError.InvalidValue, "movement axis out of range");
            return q / 127f;
        }

        private static float Finite(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 0f : v;
    }

    /// <summary>
    /// The per-tick input packet of a duel (Input lane, sequenced): the duel it belongs to, the newest snapshot tick the
    /// client has decoded (the server's delta baseline acknowledgement), and the newest input with up to
    /// <see cref="NetRules.InputRedundancy"/> earlier unacknowledged ones (packet loss recovery).
    /// </summary>
    public sealed class DuelInputMessage : INetMessage
    {
        public int DuelId { get; set; }

        /// <summary>Newest snapshot duel tick decoded by the client, or -1.</summary>
        public int AckSnapshotTick { get; set; } = -1;

        /// <summary>
        /// The client's current interpolation delay in whole ticks (its declared view lag; the server's expected-view
        /// check uses it, docs/research/netcode.md §2.2.3). At most <see cref="MaxInterpolationTicks"/>.
        /// </summary>
        public int InterpolationTicks { get; set; }

        /// <summary>Snapshots of this duel the client received so far (the server measures snapshot loss from it).</summary>
        public uint SnapshotsReceived { get; set; }

        public List<InputCommand<PlayerInput>> Commands { get; } = new List<InputCommand<PlayerInput>>();

        /// <summary>Largest declarable interpolation delay (500 ms); lag compensation still rewinds at most 150 ms (L423).</summary>
        public const int MaxInterpolationTicks = 30;

        public void Serialize(NetWriter writer)
        {
            writer.WriteVarUInt((uint)DuelId);
            writer.WriteVarInt(AckSnapshotTick);
            writer.WriteByte((byte)Math.Max(0, Math.Min(MaxInterpolationTicks, InterpolationTicks)));
            writer.WriteVarUInt(SnapshotsReceived);
            InputPacket.Write(writer, Commands, PlayerInputCodec.Instance);
        }

        public void Deserialize(NetReader reader)
        {
            uint duel = reader.ReadVarUInt();
            if (duel > int.MaxValue) throw new NetReadException(NetReadError.InvalidValue, "duel id out of range");
            DuelId = (int)duel;
            AckSnapshotTick = reader.ReadVarIntInRange(-1, int.MaxValue);
            InterpolationTicks = reader.ReadByteInRange(0, MaxInterpolationTicks);
            SnapshotsReceived = reader.ReadVarUInt();
            InputPacket.Read(reader, PlayerInputCodec.Instance, Commands);
        }
    }
}
