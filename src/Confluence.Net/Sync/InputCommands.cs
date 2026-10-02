using System;
using System.Collections.Generic;
using Confluence.Net.Serialization;

namespace Confluence.Net.Sync
{
    /// <summary>
    /// Wire codec for one input payload. <paramref name="previous"/> is the preceding command in the same packet
    /// (default for the first), so codecs may delta-encode consecutive inputs.
    /// </summary>
    public interface IInputCodec<T>
    {
        void Write(NetWriter writer, T value, T previous);

        /// <summary>Reads one input from untrusted bytes; throws <see cref="NetReadException"/> on violations.</summary>
        T Read(NetReader reader, T previous);
    }

    /// <summary>One client tick of input with its sequence number (1, 2, 3, ... per input stream).</summary>
    public struct InputCommand<T>
    {
        public InputCommand(uint sequence, T input)
        {
            Sequence = sequence;
            Input = input;
        }

        public uint Sequence { get; }

        public T Input { get; }
    }

    /// <summary>
    /// Packet layout shared by both buffers: varint newest sequence, count (1..<see cref="NetRules.MaxInputsPerPacket"/>),
    /// then the inputs oldest to newest, each coded against its predecessor. Sequences are contiguous, so the
    /// packet states only the newest one.
    /// </summary>
    public static class InputPacket
    {
        public static void Write<T>(NetWriter writer, IReadOnlyList<InputCommand<T>> oldestToNewest, IInputCodec<T> codec)
        {
            if (oldestToNewest == null || oldestToNewest.Count == 0) throw new ArgumentException("An input packet carries at least one command.");
            if (oldestToNewest.Count > NetRules.MaxInputsPerPacket) throw new ArgumentException("Too many commands for one packet.");
            uint newest = oldestToNewest[oldestToNewest.Count - 1].Sequence;
            writer.WriteVarUInt(newest);
            writer.WriteVarUInt((uint)oldestToNewest.Count);
            T previous = default;
            for (int i = 0; i < oldestToNewest.Count; i++)
            {
                InputCommand<T> command = oldestToNewest[i];
                if (command.Sequence != newest - (uint)(oldestToNewest.Count - 1 - i))
                {
                    throw new ArgumentException("Input packet sequences must be contiguous.");
                }

                codec.Write(writer, command.Input, previous);
                previous = command.Input;
            }
        }

        /// <summary>Decodes a packet into <paramref name="output"/> (cleared first), oldest to newest.</summary>
        public static void Read<T>(NetReader reader, IInputCodec<T> codec, List<InputCommand<T>> output)
        {
            output.Clear();
            uint newest = reader.ReadVarUInt();
            int count = reader.ReadCount(NetRules.MaxInputsPerPacket, 1);
            if (count == 0 || newest < (uint)count)
            {
                throw new NetReadException(NetReadError.InvalidValue, "input packet sequences out of range");
            }

            uint first = newest - (uint)count + 1;
            T previous = default;
            for (int i = 0; i < count; i++)
            {
                T input = codec.Read(reader, previous);
                output.Add(new InputCommand<T>(first + (uint)i, input));
                previous = input;
            }
        }
    }

    /// <summary>
    /// Message body carrying one input packet (see <see cref="InputPacket"/>), for use with a
    /// <see cref="Protocol.MessageType{T}"/> on <see cref="Protocol.NetChannel.Input"/>.
    /// </summary>
    public sealed class InputPacketMessage<T, TCodec> : Protocol.INetMessage
        where TCodec : IInputCodec<T>, new()
    {
        private static readonly TCodec Codec = new TCodec();

        /// <summary>Commands oldest to newest with contiguous sequences.</summary>
        public List<InputCommand<T>> Commands { get; } = new List<InputCommand<T>>();

        public void Serialize(NetWriter writer) => InputPacket.Write(writer, Commands, Codec);

        public void Deserialize(NetReader reader) => InputPacket.Read(reader, Codec, Commands);
    }

    /// <summary>
    /// Client side of the input stream. Every tick's input gets the next sequence number and is kept until the
    /// server acknowledges it (snapshot ack), which serves both redundancy and reconciliation replay. Each packet
    /// carries the newest input plus up to <see cref="Redundancy"/> earlier unacknowledged inputs, so isolated
    /// or short bursts of lost packets are recovered by the next packet.
    /// </summary>
    public sealed class InputSendBuffer<T>
    {
        private readonly List<InputCommand<T>> _unacked = new List<InputCommand<T>>();
        private readonly List<InputCommand<T>> _packet = new List<InputCommand<T>>();
        private readonly int _capacity;
        private uint _nextSequence = 1;

        public InputSendBuffer(int redundancy = NetRules.InputRedundancy, int capacity = 128)
        {
            if (redundancy < 0 || redundancy >= NetRules.MaxInputsPerPacket) throw new ArgumentOutOfRangeException(nameof(redundancy));
            if (capacity <= redundancy) throw new ArgumentOutOfRangeException(nameof(capacity));
            Redundancy = redundancy;
            _capacity = capacity;
        }

        public int Redundancy { get; }

        /// <summary>Sequence of the newest pushed input (0 before the first).</summary>
        public uint NewestSequence => _nextSequence - 1;

        /// <summary>Highest sequence the server confirmed as processed.</summary>
        public uint AckedSequence { get; private set; }

        public int UnacknowledgedCount => _unacked.Count;

        /// <summary>Inputs dropped from the replay history because the server stopped acknowledging.</summary>
        public long Overflowed { get; private set; }

        /// <summary>Unacknowledged inputs, oldest first (the reconciliation replay list).</summary>
        public IReadOnlyList<InputCommand<T>> Unacknowledged => _unacked;

        /// <summary>Records this tick's input and returns its sequence.</summary>
        public uint Push(T input)
        {
            uint sequence = _nextSequence++;
            _unacked.Add(new InputCommand<T>(sequence, input));
            if (_unacked.Count > _capacity)
            {
                _unacked.RemoveAt(0);
                Overflowed++;
            }

            return sequence;
        }

        /// <summary>Drops inputs up to and including <paramref name="sequence"/>. Older or repeated acks are ignored.</summary>
        public void Acknowledge(uint sequence)
        {
            if (sequence <= AckedSequence) return;
            if (sequence > NewestSequence) sequence = NewestSequence;
            AckedSequence = sequence;
            int remove = 0;
            while (remove < _unacked.Count && _unacked[remove].Sequence <= sequence)
            {
                remove++;
            }

            _unacked.RemoveRange(0, remove);
        }

        public bool TryGet(uint sequence, out T input)
        {
            foreach (InputCommand<T> c in _unacked)
            {
                if (c.Sequence == sequence)
                {
                    input = c.Input;
                    return true;
                }
            }

            input = default;
            return false;
        }

        /// <summary>Writes the newest input plus up to <see cref="Redundancy"/> earlier unacknowledged ones.</summary>
        public void WritePacket(NetWriter writer, IInputCodec<T> codec)
        {
            FillPacket(_packet);
            InputPacket.Write(writer, _packet, codec);
        }

        /// <summary>Fills <paramref name="commands"/> (cleared first) with the next packet's commands, oldest first.</summary>
        public void FillPacket(List<InputCommand<T>> commands)
        {
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            if (_unacked.Count == 0) throw new InvalidOperationException("No unacknowledged input to send.");
            int count = Math.Min(_unacked.Count, Redundancy + 1);
            commands.Clear();
            for (int i = _unacked.Count - count; i < _unacked.Count; i++)
            {
                commands.Add(_unacked[i]);
            }
        }

        /// <summary>Starts a new stream (for example after a reconnect).</summary>
        public void Reset()
        {
            _unacked.Clear();
            _nextSequence = 1;
            AckedSequence = 0;
        }
    }

    /// <summary>Where the input a server tick used came from.</summary>
    public enum InputSource : byte
    {
        /// <summary>No input has ever arrived; the neutral input was used.</summary>
        None,

        /// <summary>The client's input for the next sequence.</summary>
        Received,

        /// <summary>The next sequence is lost for good (a newer packet arrived without it); the last input stood in.</summary>
        Lost,

        /// <summary>Nothing new has arrived yet; the last input was repeated without consuming a sequence.</summary>
        Repeated,

        /// <summary>Starved longer than the repeat bound; the neutral input was used.</summary>
        Neutral,
    }

    /// <summary>The input a server tick consumed.</summary>
    public struct ConsumedInput<T>
    {
        public ConsumedInput(uint sequence, T input, InputSource source)
        {
            Sequence = sequence;
            Input = input;
            Source = source;
        }

        /// <summary>Sequence this tick consumed, or 0 when it consumed none (None, Repeated, Neutral).</summary>
        public uint Sequence { get; }

        public T Input { get; }

        public InputSource Source { get; }
    }

    /// <summary>
    /// Server side of one player's input stream. Accepts redundant packets, drops duplicates and inputs already
    /// consumed, keeps the rest ordered by sequence, and hands out exactly one input per server tick:
    /// <list type="bullet">
    /// <item>the next sequence when present;</item>
    /// <item>when the next sequence is missing but a newer one exists, that input is lost for good (packets are
    /// sequenced and each repeats its predecessors), so the last known input stands in and the sequence is
    /// consumed;</item>
    /// <item>when nothing newer exists (the client is late), the last known input is repeated without consuming a
    /// sequence, for at most <see cref="MaxRepeatTicks"/> ticks, then the neutral input. The real input is still
    /// used when it arrives, so a late client gains a tick of buffering instead of losing actions.</item>
    /// </list>
    /// At most <see cref="MaxBufferedTicks"/> inputs wait ahead of consumption; surplus oldest inputs are skipped
    /// so a client running fast cannot bank latency.
    /// </summary>
    public sealed class InputReceiveBuffer<T>
    {
        private readonly SortedDictionary<uint, T> _pending = new SortedDictionary<uint, T>();
        private readonly List<InputCommand<T>> _scratch = new List<InputCommand<T>>();
        private readonly T _neutral;
        private uint _nextSequence;
        private uint _newestReceived;
        private T _lastKnown;
        private int _starvedRun;

        public InputReceiveBuffer(T neutral = default, int maxRepeatTicks = NetRules.InputMaxRepeatTicks, int maxBufferedTicks = NetRules.InputMaxBufferedTicks)
        {
            if (maxRepeatTicks < 0) throw new ArgumentOutOfRangeException(nameof(maxRepeatTicks));
            if (maxBufferedTicks < 1) throw new ArgumentOutOfRangeException(nameof(maxBufferedTicks));
            _neutral = neutral;
            _lastKnown = neutral;
            MaxRepeatTicks = maxRepeatTicks;
            MaxBufferedTicks = maxBufferedTicks;
        }

        public int MaxRepeatTicks { get; }

        public int MaxBufferedTicks { get; }

        public bool HasStarted => _nextSequence != 0;

        /// <summary>
        /// Last sequence the server has finished with (consumed or written off). Snapshots echo it so the client
        /// replays only later inputs during reconciliation.
        /// </summary>
        public uint LastProcessedSequence => _nextSequence == 0 ? 0 : _nextSequence - 1;

        public uint NewestReceivedSequence => _newestReceived;

        public int BufferedCount => _pending.Count;

        public long Accepted { get; private set; }

        public long Duplicates { get; private set; }

        /// <summary>Inputs that arrived after their sequence was already consumed or written off.</summary>
        public long Late { get; private set; }

        public long LostSubstituted { get; private set; }

        public long RepeatedTicks { get; private set; }

        public long NeutralTicks { get; private set; }

        public long Skipped { get; private set; }

        /// <summary>Decodes one input packet and accepts its new commands; returns how many were new.</summary>
        public int ReadPacket(NetReader reader, IInputCodec<T> codec)
        {
            InputPacket.Read(reader, codec, _scratch);
            return Accept(_scratch);
        }

        /// <summary>Accepts decoded commands (oldest first); returns how many were new.</summary>
        public int Accept(IReadOnlyList<InputCommand<T>> commands)
        {
            int added = 0;
            for (int i = 0; i < commands.Count; i++)
            {
                if (Accept(commands[i].Sequence, commands[i].Input))
                {
                    added++;
                }
            }

            return added;
        }

        /// <summary>Accepts one command; false for duplicates and sequences already consumed.</summary>
        public bool Accept(uint sequence, T input)
        {
            if (sequence == 0) return false;
            if (_nextSequence == 0)
            {
                _nextSequence = sequence; // the stream starts at the first sequence seen
            }

            if (sequence < _nextSequence)
            {
                Late++;
                return false;
            }

            if (_pending.ContainsKey(sequence))
            {
                Duplicates++;
                return false;
            }

            _pending.Add(sequence, input);
            Accepted++;
            if (sequence > _newestReceived)
            {
                _newestReceived = sequence;
                TrimSurplus(); // keeps the pending set within MaxBufferedTicks between server ticks
            }

            return true;
        }

        /// <summary>Input for the current server tick.</summary>
        public ConsumedInput<T> ConsumeNext()
        {
            if (_nextSequence == 0)
            {
                return new ConsumedInput<T>(0, _neutral, InputSource.None);
            }

            TrimSurplus();
            if (_pending.TryGetValue(_nextSequence, out T input))
            {
                _pending.Remove(_nextSequence);
                _lastKnown = input;
                _starvedRun = 0;
                return new ConsumedInput<T>(_nextSequence++, input, InputSource.Received);
            }

            if (_newestReceived > _nextSequence)
            {
                LostSubstituted++;
                _starvedRun = 0;
                return new ConsumedInput<T>(_nextSequence++, _lastKnown, InputSource.Lost);
            }

            _starvedRun++;
            if (_starvedRun <= MaxRepeatTicks)
            {
                RepeatedTicks++;
                return new ConsumedInput<T>(0, _lastKnown, InputSource.Repeated);
            }

            NeutralTicks++;
            return new ConsumedInput<T>(0, _neutral, InputSource.Neutral);
        }

        /// <summary>Starts a new stream (for example after the client reset its sequences on reconnect).</summary>
        public void Reset()
        {
            _pending.Clear();
            _nextSequence = 0;
            _newestReceived = 0;
            _lastKnown = _neutral;
            _starvedRun = 0;
        }

        private void TrimSurplus()
        {
            // Inputs ahead of consumption span the next sequence through the newest received. Jump straight to the
            // newest MaxBufferedTicks of them: the cost depends on the pending count, never on how far a (possibly
            // forged) newest sequence lies ahead.
            if (_newestReceived < _nextSequence || _newestReceived - _nextSequence < (uint)MaxBufferedTicks)
            {
                return;
            }

            uint keepFrom = _newestReceived - (uint)MaxBufferedTicks + 1;
            while (_pending.Count > 0)
            {
                KeyValuePair<uint, T> oldest = Oldest();
                if (oldest.Key >= keepFrom) break;
                _pending.Remove(oldest.Key);
                _lastKnown = oldest.Value;
            }

            Skipped += keepFrom - _nextSequence;
            _nextSequence = keepFrom;
        }

        private KeyValuePair<uint, T> Oldest()
        {
            using (SortedDictionary<uint, T>.Enumerator e = _pending.GetEnumerator())
            {
                e.MoveNext();
                return e.Current;
            }
        }
    }
}
