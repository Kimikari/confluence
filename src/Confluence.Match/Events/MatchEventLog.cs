using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Confluence.Core.Random;

namespace Confluence.Match
{
    /// <summary>Append-only, ordered match event stream.</summary>
    public sealed class MatchEventLog : IReadOnlyList<MatchEvent>
    {
        private readonly List<MatchEvent> _events = new List<MatchEvent>();

        /// <summary>Raised after each append (server broadcast, replay recorder).</summary>
        public event Action<MatchEvent> Appended;

        public int Count => _events.Count;

        public MatchEvent this[int index] => _events[index];

        internal void Append(MatchEvent e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            if (_events.Count > 0 && e.Tick < _events[_events.Count - 1].Tick)
            {
                throw new InvalidOperationException("Match events must be appended in tick order.");
            }

            _events.Add(e);
            Appended?.Invoke(e);
        }

        /// <summary>Events from <paramref name="startIndex"/> on, for incremental client delivery.</summary>
        public IReadOnlyList<MatchEvent> Since(int startIndex)
        {
            if (startIndex < 0 || startIndex > _events.Count) throw new ArgumentOutOfRangeException(nameof(startIndex));
            return _events.GetRange(startIndex, _events.Count - startIndex);
        }

        /// <summary>
        /// Events a spectator may see at <paramref name="currentTick"/> under a delay (spec L391: at least 15 s while the
        /// match is active).
        /// </summary>
        public IReadOnlyList<MatchEvent> DelayedView(int currentTick, int delayTicks)
        {
            if (delayTicks < 0) throw new ArgumentOutOfRangeException(nameof(delayTicks));
            var visible = new List<MatchEvent>();
            foreach (var e in _events)
            {
                if (e.Tick > currentTick - delayTicks) break;
                visible.Add(e);
            }

            return visible;
        }

        public List<T> OfType<T>() where T : MatchEvent
        {
            var result = new List<T>();
            foreach (var e in _events)
                if (e is T typed) result.Add(typed);
            return result;
        }

        /// <summary>Canonical text of the whole stream, one event per line.</summary>
        public string ToCanonicalText()
        {
            var sb = new StringBuilder();
            foreach (var e in _events) sb.Append(e).Append('\n');
            return sb.ToString();
        }

        /// <summary>Stable 64-bit hash of the canonical text (identical across runtimes).</summary>
        public ulong Fingerprint() => SeedHash.Of(ToCanonicalText());

        public IEnumerator<MatchEvent> GetEnumerator() => _events.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
