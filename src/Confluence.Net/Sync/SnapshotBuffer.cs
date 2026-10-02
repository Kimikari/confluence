using System;
using System.Collections.Generic;
using Confluence.Net.Serialization;

namespace Confluence.Net.Sync
{
    /// <summary>
    /// Common prefix of every world snapshot: the server tick it describes, the last input sequence the server
    /// processed for the receiving player (the reconciliation acknowledgement), and the baseline tick a delta
    /// body was encoded against (none for a full snapshot).
    /// </summary>
    public struct SnapshotHeader
    {
        public const int NoBaseline = -1;

        public SnapshotHeader(int serverTick, uint lastProcessedInput, int baselineTick = NoBaseline)
        {
            if (serverTick < 0) throw new ArgumentOutOfRangeException(nameof(serverTick));
            if (baselineTick != NoBaseline && (baselineTick < 0 || baselineTick >= serverTick))
            {
                throw new ArgumentOutOfRangeException(nameof(baselineTick), "A baseline precedes its snapshot.");
            }

            ServerTick = serverTick;
            LastProcessedInput = lastProcessedInput;
            BaselineTick = baselineTick;
        }

        public int ServerTick { get; }

        public uint LastProcessedInput { get; }

        public int BaselineTick { get; }

        public bool HasBaseline => BaselineTick != NoBaseline;

        public void Write(NetWriter writer)
        {
            writer.WriteVarUInt((uint)ServerTick);
            writer.WriteVarUInt(LastProcessedInput);
            writer.WriteVarUInt(HasBaseline ? (uint)(ServerTick - BaselineTick) : 0u); // 0 = full snapshot
        }

        public static SnapshotHeader Read(NetReader reader)
        {
            uint tick = reader.ReadVarUInt();
            if (tick > int.MaxValue) throw new NetReadException(NetReadError.InvalidValue, "snapshot tick out of range");
            uint ack = reader.ReadVarUInt();
            uint back = reader.ReadVarUInt();
            if (back > tick) throw new NetReadException(NetReadError.InvalidValue, "snapshot baseline before tick 0");
            return new SnapshotHeader((int)tick, ack, back == 0 ? NoBaseline : (int)(tick - back));
        }
    }

    public enum SampleKind : byte
    {
        /// <summary>No snapshot yet.</summary>
        Empty,

        /// <summary>Render time lies between two snapshots (or exactly on one).</summary>
        Interpolated,

        /// <summary>Render time precedes the oldest snapshot; the oldest is held.</summary>
        BeforeOldest,

        /// <summary>Render time is past the newest snapshot (late or lost packets); the newest is held.</summary>
        Starved,
    }

    /// <summary>Two bracketing snapshots and the blend factor for a render time.</summary>
    public struct SnapshotSample<T>
    {
        public SnapshotSample(SampleKind kind, int fromTick, T from, int toTick, T to, float alpha)
        {
            Kind = kind;
            FromTick = fromTick;
            From = from;
            ToTick = toTick;
            To = to;
            Alpha = alpha;
        }

        public SampleKind Kind { get; }

        public int FromTick { get; }

        public T From { get; }

        public int ToTick { get; }

        public T To { get; }

        /// <summary>0 at <see cref="From"/>, approaching 1 at <see cref="To"/>.</summary>
        public float Alpha { get; }
    }

    /// <summary>
    /// Snapshots ordered by server tick, for remote-entity interpolation on the client (render about
    /// <see cref="NetRules.InterpolationDelayMs"/> behind the server estimate) and for delta baselines on the
    /// server. Out-of-order arrivals are inserted in place; duplicates and snapshots older than a full buffer
    /// are ignored.
    /// </summary>
    public sealed class SnapshotBuffer<T>
    {
        private readonly List<int> _ticks = new List<int>();
        private readonly List<T> _items = new List<T>();

        public SnapshotBuffer(int capacity = 32)
        {
            if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity), "Interpolation needs at least two snapshots.");
            Capacity = capacity;
        }

        public int Capacity { get; }

        public int Count => _ticks.Count;

        public int NewestTick => _ticks.Count > 0 ? _ticks[_ticks.Count - 1] : -1;

        public int OldestTick => _ticks.Count > 0 ? _ticks[0] : -1;

        public long Duplicates { get; private set; }

        public long TooOld { get; private set; }

        /// <summary>Inserts a snapshot; false when its tick is already held or older than a full buffer.</summary>
        public bool Add(int tick, T snapshot)
        {
            int index = _ticks.BinarySearch(tick);
            if (index >= 0)
            {
                Duplicates++;
                return false;
            }

            index = ~index;
            if (_ticks.Count >= Capacity && index == 0)
            {
                TooOld++;
                return false;
            }

            _ticks.Insert(index, tick);
            _items.Insert(index, snapshot);
            if (_ticks.Count > Capacity)
            {
                _ticks.RemoveAt(0);
                _items.RemoveAt(0);
            }

            return true;
        }

        public bool TryGet(int tick, out T snapshot)
        {
            int index = _ticks.BinarySearch(tick);
            if (index >= 0)
            {
                snapshot = _items[index];
                return true;
            }

            snapshot = default;
            return false;
        }

        public bool TryGetNewest(out int tick, out T snapshot)
        {
            if (_ticks.Count == 0)
            {
                tick = -1;
                snapshot = default;
                return false;
            }

            tick = _ticks[_ticks.Count - 1];
            snapshot = _items[_items.Count - 1];
            return true;
        }

        /// <summary>The snapshots bracketing <paramref name="renderTick"/>.</summary>
        public SnapshotSample<T> Sample(double renderTick)
        {
            int n = _ticks.Count;
            if (n == 0)
            {
                return new SnapshotSample<T>(SampleKind.Empty, -1, default, -1, default, 0f);
            }

            if (renderTick < _ticks[0])
            {
                return new SnapshotSample<T>(SampleKind.BeforeOldest, _ticks[0], _items[0], _ticks[0], _items[0], 0f);
            }

            int last = n - 1;
            if (renderTick >= _ticks[last])
            {
                SampleKind kind = renderTick > _ticks[last] ? SampleKind.Starved : SampleKind.Interpolated;
                return new SnapshotSample<T>(kind, _ticks[last], _items[last], _ticks[last], _items[last], 0f);
            }

            // Largest index whose tick is at or before the render time; it has a successor here.
            int lo = 0;
            int hi = last;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (_ticks[mid] <= renderTick) lo = mid;
                else hi = mid;
            }

            int from = _ticks[lo];
            int to = _ticks[lo + 1];
            float alpha = (float)((renderTick - from) / (to - from));
            return new SnapshotSample<T>(SampleKind.Interpolated, from, _items[lo], to, _items[lo + 1], alpha);
        }

        /// <summary>
        /// Interpolated value at <paramref name="renderTick"/> using <paramref name="lerp"/>; holds the nearest end
        /// outside the buffered range. False only when the buffer is empty.
        /// </summary>
        public bool TrySample(double renderTick, Func<T, T, float, T> lerp, out T value)
        {
            if (lerp == null) throw new ArgumentNullException(nameof(lerp));
            SnapshotSample<T> sample = Sample(renderTick);
            if (sample.Kind == SampleKind.Empty)
            {
                value = default;
                return false;
            }

            value = sample.Kind == SampleKind.Interpolated && sample.FromTick != sample.ToTick
                ? lerp(sample.From, sample.To, sample.Alpha)
                : sample.From;
            return true;
        }

        /// <summary>
        /// Drops snapshots older than <paramref name="tick"/> except the newest one at or before it, which is still
        /// needed as the start of an interpolation span.
        /// </summary>
        public void DiscardBefore(int tick)
        {
            int keepFrom = 0;
            while (keepFrom + 1 < _ticks.Count && _ticks[keepFrom + 1] <= tick)
            {
                keepFrom++;
            }

            if (keepFrom > 0)
            {
                _ticks.RemoveRange(0, keepFrom);
                _items.RemoveRange(0, keepFrom);
            }
        }

        public void Clear()
        {
            _ticks.Clear();
            _items.Clear();
        }
    }

    /// <summary>Client-side prediction replay.</summary>
    public static class Reconciliation
    {
        /// <summary>
        /// Acknowledges inputs through <paramref name="lastProcessedInput"/> and re-applies every later input to the
        /// authoritative state with the shared step function (the same motor code the server runs), producing
        /// the corrected predicted state.
        /// </summary>
        public static TState Replay<TState, TInput>(
            TState authoritative,
            uint lastProcessedInput,
            InputSendBuffer<TInput> inputs,
            Func<TState, TInput, TState> step)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            if (step == null) throw new ArgumentNullException(nameof(step));
            inputs.Acknowledge(lastProcessedInput);
            TState state = authoritative;
            foreach (InputCommand<TInput> command in inputs.Unacknowledged)
            {
                if (command.Sequence > lastProcessedInput)
                {
                    state = step(state, command.Input);
                }
            }

            return state;
        }
    }
}
