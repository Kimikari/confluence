using Confluence.Core.Time;

namespace Confluence.Casting
{
    /// <summary>Kinds of discrete, client-originated grammar input (spec L365, L369, L490).</summary>
    public enum GrammarInputKind : byte
    {
        /// <summary>A recognized voice syllable: <see cref="GrammarInputEvent.Value"/> indexes VoiceLexicon.Syllables.</summary>
        Syllable = 0,

        /// <summary>A timed rhythm-route press standing in for a syllable or beat (spec L367).</summary>
        RhythmTap = 1,

        /// <summary>One normalized pointer sample of a rune/sigil stroke (spec L369, L490).</summary>
        StrokeSample = 2,

        /// <summary>End of the current stroke (pointer lifted or segment confirmed).</summary>
        StrokeEnd = 3,

        /// <summary>A direction-key trace step: <see cref="GrammarInputEvent.Value"/> is a <see cref="TraceDirection"/>.</summary>
        DirectionTrace = 4,

        /// <summary>A widget choice (junction, tile, socket, branch...): <see cref="GrammarInputEvent.Value"/> is its index.</summary>
        WidgetSelect = 5,
    }

    /// <summary>Eight-way direction for direction-key tracing (spec L369 "same segment count").</summary>
    public enum TraceDirection : byte
    {
        Up = 0,
        UpRight = 1,
        Right = 2,
        DownRight = 3,
        Down = 4,
        DownLeft = 5,
        Left = 6,
        UpLeft = 7,
    }

    /// <summary>
    /// A discrete grammar input with a local timestamp. Inputs are locally timestamped and server-validated under
    /// bounded latency compensation (spec L490); the timestamp is the input tick plus a sub-tick fraction, so voice
    /// syllables and rhythm taps share one time base and identical windows (spec L365, L367).
    /// </summary>
    public struct GrammarInputEvent
    {
        public const int SubTickResolution = 1000;

        public GrammarInputKind Kind;

        /// <summary>Input tick the event was sampled in.</summary>
        public int Tick;

        /// <summary>Position inside that tick in 1/1000 tick (0-999).</summary>
        public int SubTick;

        /// <summary>Syllable index, trace direction, widget index, or rhythm lane, by <see cref="Kind"/>.</summary>
        public int Value;

        /// <summary>Recognition confidence 0-1 for syllables; 1 for keyed input. Never scales a reward (spec L367).</summary>
        public float Confidence;

        /// <summary>Normalized pane coordinates for stroke samples (0-1 across the pane; spec L490).</summary>
        public float X;
        public float Y;

        public GrammarInputEvent(GrammarInputKind kind, int tick, int subTick, int value)
        {
            Kind = kind;
            Tick = tick;
            SubTick = subTick < 0 ? 0 : (subTick >= SubTickResolution ? SubTickResolution - 1 : subTick);
            Value = value;
            Confidence = 1f;
            X = 0f;
            Y = 0f;
        }

        public static GrammarInputEvent Stroke(int tick, int subTick, float x, float y)
        {
            var e = new GrammarInputEvent(GrammarInputKind.StrokeSample, tick, subTick, 0);
            e.X = x;
            e.Y = y;
            return e;
        }

        /// <summary>Timestamp in sub-ticks (tick * 1000 + sub-tick); exact integer ordering.</summary>
        public long Time => (long)Tick * SubTickResolution + SubTick;

        /// <summary>Converts seconds to the sub-tick time base, rounded to nearest.</summary>
        public static long SubTicksFromSeconds(double seconds) => (long)System.Math.Round(seconds * Ticks.PerSecond * SubTickResolution);
    }
}
