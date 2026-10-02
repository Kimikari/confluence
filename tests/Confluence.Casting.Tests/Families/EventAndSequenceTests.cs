using System;
using Confluence.Casting;
using Confluence.Casting.Grammars;

namespace Confluence.Casting.Tests.Families
{
    public class CombatEventAndArmedRewardTests
    {
        [Fact]
        public void Only_The_Declared_Combat_Event_Opens_The_Window_For_Its_Exact_Length()
        {
            // WIZ-006 shape: act within 0.5 s (30 ticks) of a successful parry. Spec L482: a genuine combat event.
            var window = new CombatEventWindow(CombatEventKind.PerfectGuard, 30);
            var buttons = CastInputFrame.FromHeld(99, InputButtons.None, InputButtons.Guard | InputButtons.CastModifier);
            buttons.CombatEvents = new[] { new CombatEvent(CombatEventKind.GuardBlock, 99) };
            window.Observe(buttons);
            Assert.False(window.IsOpen(99));

            window.Observe(new CastInputFrame { Tick = 100, CombatEvents = new[] { new CombatEvent(CombatEventKind.PerfectGuard, 100) } });
            Assert.False(window.IsOpen(99));
            Assert.True(window.IsOpen(100));
            Assert.True(window.IsOpen(130));
            Assert.False(window.IsOpen(131));
            window.Close();
            Assert.False(window.IsOpen(110));
        }

        [Fact]
        public void An_Armed_Reward_Is_Used_Once_Expires_And_Never_Stacks()
        {
            // Spec L478: charges cannot be banked beyond their stated windows; one reward per cast.
            var armed = new ArmedReward();
            var first = MasteryResult.Mastered(MasteryReward.WindupReduction, 0.12f);
            var second = MasteryResult.Mastered(MasteryReward.WindupReduction, 0.10f);
            armed.Arm(first, 10, 120);
            armed.Arm(second, 20, 120);
            Assert.Equal(second, armed.Pending);
            Assert.True(armed.IsArmed(140));
            Assert.False(armed.IsArmed(141));

            Assert.True(armed.TryConsume(50, out var used));
            Assert.Equal(second, used);
            Assert.False(armed.TryConsume(51, out _));

            armed.Arm(first, 0, 10);
            Assert.False(armed.TryConsume(11, out var expired));
            Assert.False(expired.IsMastered);
            Assert.Throws<ArgumentException>(() => armed.Arm(MasteryResult.Normal(MissReason.LateRelease), 0, 10));
        }
    }

    public class TokenSequenceTests
    {
        private static readonly InputButtons[] Alphabet = { InputButtons.ModifierA, InputButtons.ModifierB, InputButtons.Primary };

        private static void Press(TokenSequence sequence, int tick, InputButtons buttons) =>
            sequence.Observe(new CastInputFrame { Tick = tick, Pressed = buttons, Held = buttons });

        [Fact]
        public void Clean_Fast_Entry_Masters()
        {
            // WIZ-005 shape: three tokens, mastery "within 0.45 s without correction".
            var s = new TokenSequence(Alphabet, new[] { 1, 0, 2 }, correctionDelayTicks: 9);
            s.Reset(0);
            Press(s, 5, InputButtons.ModifierB);
            Press(s, 12, InputButtons.ModifierA);
            Assert.Equal(2, s.NextExpected);
            Press(s, 20, InputButtons.Primary);
            Assert.True(s.IsComplete);
            Assert.Equal(20, s.CompletedTick);
            Assert.Equal(MissReason.None, s.MasteryMiss(0, 27));
            Assert.Equal(MissReason.TooSlow, s.MasteryMiss(0, 19));
        }

        [Fact]
        public void A_Wrong_Token_Clears_Only_Itself_And_Adds_A_Visible_Delay()
        {
            // WIZ-005 normal cast: "A wrong token clears only that token and adds a visible 0.15 s correction delay".
            var s = new TokenSequence(Alphabet, new[] { 1, 0, 2 }, correctionDelayTicks: 9);
            s.Reset(0);
            Press(s, 2, InputButtons.ModifierB);
            Press(s, 4, InputButtons.Primary);        // wrong: progress stays at 1
            Assert.Equal(1, s.Progress);
            Assert.Equal(13, s.CorrectionEndsTick);
            Press(s, 8, InputButtons.ModifierA);      // ignored during the delay
            Assert.Equal(1, s.Progress);
            Assert.Equal(1, s.IgnoredPresses);
            Press(s, 13, InputButtons.ModifierA);
            Press(s, 15, InputButtons.Primary);
            Assert.True(s.IsComplete);
            Assert.Equal(1, s.Corrections);
            Assert.Equal(MissReason.Corrected, s.MasteryMiss(0, 27));
        }

        [Fact]
        public void Spam_After_Completion_Is_Ignored_And_Simultaneous_Presses_Read_In_Alphabet_Order()
        {
            var s = new TokenSequence(Alphabet, new[] { 0, 1 }, correctionDelayTicks: 6);
            s.Reset(0);
            Press(s, 3, InputButtons.ModifierA | InputButtons.ModifierB);
            Assert.True(s.IsComplete);
            Press(s, 4, InputButtons.Primary);
            Assert.Equal(1, s.IgnoredPresses);
            Assert.Equal(0, s.Corrections);
            Assert.Throws<ArgumentOutOfRangeException>(() => new TokenSequence(Alphabet, new[] { 3 }, 0));
        }

        [Fact]
        public void Chord_Spread_And_Hold_Decide_Recognition_And_A_Selector_Matches_The_Pair()
        {
            // WIZ-022 shape: both inputs within 0.20 s (12 ticks), held 0.20 s; mastery spread 60 ms.
            var chord = new ChordTracker(InputButtons.ModifierA, InputButtons.ModifierB);
            var previous = InputButtons.None;
            void Step(int tick, InputButtons held)
            {
                chord.Observe(CastInputFrame.FromHeld(tick, previous, held));
                previous = held;
            }

            Step(0, InputButtons.ModifierA);
            Step(3, InputButtons.ModifierA | InputButtons.ModifierB);
            Assert.Equal(3, chord.SpreadTicks);
            Assert.Equal(3, chord.FormedTick);
            Step(14, InputButtons.ModifierA | InputButtons.ModifierB);
            Assert.False(chord.IsRecognized(14, 12, 12));
            Step(15, InputButtons.ModifierA | InputButtons.ModifierB);
            Assert.True(chord.IsRecognized(15, 12, 12));
            Step(16, InputButtons.ModifierB);
            Assert.False(chord.BothHeld);

            // A single selector presses both logical inputs on one tick: spread 0, same hold requirement (spec L490).
            chord.Reset();
            previous = InputButtons.None;
            Step(40, InputButtons.ModifierA | InputButtons.ModifierB);
            Assert.Equal(0, chord.SpreadTicks);
            Assert.False(chord.IsRecognized(51, 12, 12));
            Assert.True(chord.IsRecognized(52, 12, 12));
        }
    }
}
