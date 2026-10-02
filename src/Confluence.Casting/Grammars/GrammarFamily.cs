using System;

namespace Confluence.Casting.Grammars
{
    /// <summary>
    /// Mechanical families of the 36 casting languages (spec L468). Each family shares input handling and
    /// validation primitives; individual grammars supply their windows and rewards from the wizard record.
    /// </summary>
    public enum GrammarFamily : byte
    {
        /// <summary>Release or confirm a held input inside a timing window (crest, sector, socket, cue).</summary>
        ButtonTiming = 0,

        /// <summary>Keep a continuously driven gauge or widget value inside a band (pressure, dial, drifting mote).</summary>
        HeldGauge,

        /// <summary>Enter an ordered or simultaneous set of logical keys (tokens, chords).</summary>
        KeySequence,

        /// <summary>Trace normalized strokes or vertices, with an equivalent direction-key route (spec L369).</summary>
        GestureStroke,

        /// <summary>Spoken syllables or beats on a cadence, with an equivalent timed-key route (spec L365-L367).</summary>
        VoiceRhythm,

        /// <summary>Opened or rewarded by a genuine combat event such as a parry or melee hit (spec L472, L482).</summary>
        CombatEvent,

        /// <summary>Requires real travel, velocity, support, or position (spec L482).</summary>
        Movement,

        /// <summary>Solve a small fixed widget: circuit, tiles, packets, ratio, branch, crystal, stance.</summary>
        WidgetPuzzle,

        /// <summary>Reproduce a remembered motif or keep a scheduled release pending.</summary>
        SchedulingMemory,

        /// <summary>Move the real camera aim through a planned path or cone; guides never aim for the player (spec L474).</summary>
        AimPath,
    }

    /// <summary>Input routes a grammar accepts (spec L365-L369, L490). A grammar instance runs one route at a time.</summary>
    [Flags]
    public enum InputRoute : byte
    {
        None = 0,

        /// <summary>The grammar's primary keyboard/mouse route.</summary>
        Standard = 1 << 0,

        /// <summary>Recognized voice syllables processed locally (spec L365).</summary>
        Voice = 1 << 1,

        /// <summary>Timed directional or modifier presses equivalent to a voice/rhythm phrase (spec L367).</summary>
        Rhythm = 1 << 2,

        /// <summary>Direction-key tracing equivalent to a mouse gesture (spec L369, L474).</summary>
        DirectionKeys = 1 << 3,

        /// <summary>One-input selector confirmation equivalent to a simultaneous chord (spec L490).</summary>
        SingleSelector = 1 << 4,

        /// <summary>Permanently visible guide (memory, motif) for normal output (spec L490).</summary>
        StaticGuide = 1 << 5,

        /// <summary>Guided normal preparation: always a normal cast, never a reward (spec L470).</summary>
        Guided = 1 << 6,
    }
}
