using System;
using System.Collections.Generic;

namespace Confluence.Hosting
{
    /// <summary>
    /// Operational log of a match host: content-availability choices, refused starts, and other decisions an operator
    /// should be able to read back. Lines are kept in order and optionally forwarded (console, server log).
    /// Rules never read it.
    /// </summary>
    public sealed class HostLog
    {
        private readonly List<string> _lines = new List<string>();
        private readonly object _gate = new object();

        public HostLog(Action<string> forward = null)
        {
            Forward = forward;
        }

        /// <summary>Optional sink every line is also written to.</summary>
        public Action<string> Forward { get; set; }

        public IReadOnlyList<string> Lines
        {
            get
            {
                lock (_gate) return _lines.ToArray();
            }
        }

        public void Info(string message) => Write("info: " + message);

        public void Warn(string message) => Write("warn: " + message);

        private void Write(string line)
        {
            lock (_gate) _lines.Add(line);
            Forward?.Invoke(line);
        }
    }
}
