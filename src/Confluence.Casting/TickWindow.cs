using System;
using Confluence.Core.Time;

namespace Confluence.Casting
{
    /// <summary>
    /// A closed interval of elapsed preparation ticks [Open, Close]. Grammar windows are written in seconds in the
    /// catalogue and converted once with <see cref="Ticks.FromSeconds(double)"/>, so the server and the client agree on
    /// exactly which tick qualifies.
    /// </summary>
    public readonly struct TickWindow : IEquatable<TickWindow>
    {
        public TickWindow(int open, int close)
        {
            if (close < open) throw new ArgumentException("Window closes before it opens.");
            Open = open;
            Close = close;
        }

        public int Open { get; }
        public int Close { get; }

        /// <summary>Number of tick samples inside the window.</summary>
        public int Length => Close - Open + 1;

        public bool Contains(int tick) => tick >= Open && tick <= Close;

        /// <summary>Signed distance outside the window: negative before it, positive after it, 0 inside.</summary>
        public int Offset(int tick) => tick < Open ? tick - Open : (tick > Close ? tick - Close : 0);

        /// <summary>[startSec, endSec] inclusive.</summary>
        public static TickWindow Between(double startSec, double endSec) => new TickWindow(Ticks.FromSeconds(startSec), Ticks.FromSeconds(endSec));

        /// <summary>A window of total width <paramref name="widthSec"/> centered on <paramref name="centerSec"/> ("a 100 ms window at 0.45 s").</summary>
        public static TickWindow Centered(double centerSec, double widthSec) => Between(centerSec - widthSec / 2.0, centerSec + widthSec / 2.0);

        /// <summary>Every tick within <paramref name="toleranceSec"/> of <paramref name="centerSec"/> ("within 80 ms of the beat").</summary>
        public static TickWindow Within(double centerSec, double toleranceSec) => Between(centerSec - toleranceSec, centerSec + toleranceSec);

        public bool Equals(TickWindow other) => Open == other.Open && Close == other.Close;

        public override bool Equals(object obj) => obj is TickWindow other && Equals(other);

        public override int GetHashCode() => (Open * 397) ^ Close;

        public override string ToString() => "[" + Open + ", " + Close + "]";
    }
}
