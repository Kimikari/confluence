using System;
using System.Collections.Generic;

namespace Confluence.Casting.Grammars
{
    /// <summary>
    /// Ordered entry of logical tokens for the KeySequence, WidgetPuzzle and SchedulingMemory families (WIZ-005
    /// token syntax, WIZ-017 motif reproduction, WIZ-008/-033 choices). Tokens are logical buttons (remappable,
    /// spec L474), each press is one token, and the expected order is fixed per spell (WIZ-008, WIZ-017).
    /// <para>
    /// A wrong token clears only itself and starts a visible correction delay during which presses are ignored
    /// (WIZ-005 "clears only that token and adds a visible 0.15 s correction delay"; WIZ-017 "0.20 s correction").
    /// Presses during the delay are counted, so spam is reported but never helps (spec L373). Presses after the
    /// sequence completes are ignored and flagged as extra. Several tokens pressed in one tick are taken in alphabet
    /// order so every runtime reads them identically.
    /// </para>
    /// </summary>
    public sealed class TokenSequence
    {
        private readonly InputButtons[] _alphabet;
        private readonly int[] _expected;
        private readonly int _correctionDelayTicks;

        public TokenSequence(IReadOnlyList<InputButtons> alphabet, IReadOnlyList<int> expected, int correctionDelayTicks)
        {
            if (alphabet == null || alphabet.Count == 0) throw new ArgumentException("A token alphabet is required.", nameof(alphabet));
            if (expected == null || expected.Count == 0) throw new ArgumentException("An expected sequence is required.", nameof(expected));
            if (correctionDelayTicks < 0) throw new ArgumentOutOfRangeException(nameof(correctionDelayTicks));
            _alphabet = new InputButtons[alphabet.Count];
            for (int i = 0; i < _alphabet.Length; i++) _alphabet[i] = alphabet[i];
            _expected = new int[expected.Count];
            for (int i = 0; i < _expected.Length; i++)
            {
                if (expected[i] < 0 || expected[i] >= _alphabet.Length) throw new ArgumentOutOfRangeException(nameof(expected));
                _expected[i] = expected[i];
            }

            _correctionDelayTicks = correctionDelayTicks;
            Reset(0);
        }

        public int Length => _expected.Length;

        /// <summary>Correct tokens entered so far.</summary>
        public int Progress { get; private set; }

        public bool IsComplete => Progress == _expected.Length;

        /// <summary>Wrong tokens entered (each cleared with a correction delay).</summary>
        public int Corrections { get; private set; }

        /// <summary>Presses ignored during a correction delay or after completion.</summary>
        public int IgnoredPresses { get; private set; }

        public int StartTick { get; private set; }

        /// <summary>Tick of the first accepted token, or -1.</summary>
        public int FirstTokenTick { get; private set; }

        /// <summary>Tick the final token was entered, or -1.</summary>
        public int CompletedTick { get; private set; }

        /// <summary>Presses are ignored before this tick (the visible correction delay).</summary>
        public int CorrectionEndsTick { get; private set; }

        /// <summary>The token the HUD should highlight next (training display), or -1 once complete.</summary>
        public int NextExpected => IsComplete ? -1 : _expected[Progress];

        public void Reset(int tick)
        {
            Progress = 0;
            Corrections = 0;
            IgnoredPresses = 0;
            StartTick = tick;
            FirstTokenTick = -1;
            CompletedTick = -1;
            CorrectionEndsTick = int.MinValue;
        }

        /// <summary>Reads this tick's token presses.</summary>
        public void Observe(in CastInputFrame frame)
        {
            for (int t = 0; t < _alphabet.Length; t++)
            {
                if (!frame.WasPressed(_alphabet[t])) continue;
                if (IsComplete || frame.Tick < CorrectionEndsTick)
                {
                    IgnoredPresses++;
                    continue;
                }

                if (t == _expected[Progress])
                {
                    if (FirstTokenTick < 0) FirstTokenTick = frame.Tick;
                    Progress++;
                    if (IsComplete) CompletedTick = frame.Tick;
                }
                else
                {
                    Corrections++;
                    CorrectionEndsTick = frame.Tick + _correctionDelayTicks;
                }
            }
        }

        /// <summary>
        /// Why the completed sequence earns no reward, or <see cref="MissReason.None"/>: it must be complete, free of
        /// corrections, and finished within <paramref name="masteryTicks"/> of <paramref name="fromTick"/>
        /// (WIZ-005 "all three tokens within 0.45 s without correction").
        /// </summary>
        public MissReason MasteryMiss(int fromTick, int masteryTicks)
        {
            if (!IsComplete) return MissReason.MissingInput;
            if (Corrections > 0) return MissReason.Corrected;
            if (CompletedTick - fromTick > masteryTicks) return MissReason.TooSlow;
            return MissReason.None;
        }
    }

    /// <summary>
    /// Two logical inputs pressed as a chord (WIZ-022 hand chords): recognized when both start within the normal
    /// spread and stay held together; mastered only within the tighter spread. Spread is measured in ticks between
    /// the two press ticks, so a single remappable selector that presses both on one tick has spread 0 and the same
    /// hold requirement (spec L490 "a single-selector confirmation mode with the same timing requirement").
    /// </summary>
    public sealed class ChordTracker
    {
        private readonly InputButtons _first;
        private readonly InputButtons _second;
        private int _firstDown = -1;
        private int _secondDown = -1;

        public ChordTracker(InputButtons first, InputButtons second)
        {
            if (first == InputButtons.None || second == InputButtons.None || first == second) throw new ArgumentException("A chord needs two distinct inputs.");
            _first = first;
            _second = second;
        }

        public bool BothHeld { get; private set; }

        /// <summary>Ticks between the two presses, or -1 until both are down.</summary>
        public int SpreadTicks => _firstDown >= 0 && _secondDown >= 0 ? Math.Abs(_firstDown - _secondDown) : -1;

        /// <summary>Tick both inputs became held together, or -1.</summary>
        public int FormedTick { get; private set; } = -1;

        public void Reset()
        {
            _firstDown = -1;
            _secondDown = -1;
            BothHeld = false;
            FormedTick = -1;
        }

        public void Observe(in CastInputFrame frame)
        {
            if (frame.WasPressed(_first)) _firstDown = frame.Tick;
            if (frame.WasPressed(_second)) _secondDown = frame.Tick;
            if (!frame.IsHeld(_first)) _firstDown = -1;
            if (!frame.IsHeld(_second)) _secondDown = -1;
            bool held = _firstDown >= 0 && _secondDown >= 0;
            if (held && !BothHeld) FormedTick = frame.Tick;
            if (!held) FormedTick = -1;
            BothHeld = held;
        }

        /// <summary>Recognized chord held for at least <paramref name="holdTicks"/> (WIZ-022 normal: 0.20 s spread, 0.20 s hold).</summary>
        public bool IsRecognized(int tick, int maxSpreadTicks, int holdTicks) =>
            BothHeld && SpreadTicks <= maxSpreadTicks && tick - FormedTick >= holdTicks;
    }
}
