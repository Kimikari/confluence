using System.Linq;
using Confluence.Casting;
using Confluence.Casting.Grammars;
using Confluence.Casting.Tests.Support;
using Confluence.Core.Rules;

namespace Confluence.Casting.Tests.Machine
{
    public class ChannelCueMovementTests
    {
        private static CastDriver Guided(int prep, params CastableSpell[] spells) =>
            new CastDriver(new GuidedNormalGrammar("TEST", GrammarFamily.ButtonTiming, prep), spells);

        private static CastableSpell Beam(int channelTicks) =>
            new CastableSpell("TEST-BEAM", 20f, 300, 15, CastWeight.Heavy, true).WithChannel(channelTicks);

        [Fact]
        public void Channel_Pulses_Every_Tick_After_Release_Then_Recovers()
        {
            var d = Guided(15, Beam(60));
            d.Step(InputButtons.Primary).Idle(120);
            Assert.Equal(30, d.TickOf(CastEventKind.Released));
            var pulses = d.OfKind(CastEventKind.ChannelPulse).ToList();
            Assert.Equal(60, pulses.Count);
            Assert.Equal(31, pulses.First().Tick);
            Assert.Equal(90, pulses.Last().Tick);
            var end = d.Single(CastEventKind.ChannelEnded);
            Assert.Equal(90, end.Tick);
            Assert.Equal(ChannelEndReason.Completed, end.ChannelEnd);
            Assert.Equal(90 + 21, d.TickOf(CastEventKind.RecoveryEnded));
        }

        [Fact]
        public void Channel_Can_Be_Cancelled_On_Any_Tick_And_Remaining_Ticks_Stop()
        {
            var d = Guided(15, Beam(60));
            d.Step(InputButtons.Primary).Idle(49);         // ticks 1-49
            Assert.True(d.Machine.IsChanneling);
            d.Step(InputButtons.Primary);                  // tick 50
            var end = d.Single(CastEventKind.ChannelEnded);
            Assert.Equal(50, end.Tick);
            Assert.Equal(ChannelEndReason.Cancelled, end.ChannelEnd);
            d.Idle(60);
            Assert.Equal(19, d.OfKind(CastEventKind.ChannelPulse).Count()); // ticks 31-49 only
            Assert.Equal(50 + 21, d.TickOf(CastEventKind.RecoveryEnded));
            Assert.Equal(80f, d.Host.ManaPool);
        }

        [Fact]
        public void Channel_Interrupt_Ends_The_Channel_And_Applies_The_Refund_Rule()
        {
            var d = Guided(15, Beam(60).WithInterruptRefund(true));
            d.Step(InputButtons.Primary).Idle(39);
            d.Host.IsHardControlled = true;
            d.Idle(1);                                     // tick 40
            Assert.Equal(5f, d.Single(CastEventKind.Interrupted).Mana);
            Assert.Equal(ChannelEndReason.Interrupted, d.Single(CastEventKind.ChannelEnded).ChannelEnd);
            Assert.Equal(CastPhase.Recovery, d.Machine.Phase);
        }

        [Fact]
        public void Channel_Length_Is_Capped_At_Three_Seconds()
        {
            Assert.Equal(180, CastingRules.MaxChannelTicks);
            Assert.Equal(180, Beam(600).ChannelMaxTicks);
            var d = Guided(0, Beam(600));
            d.Step(InputButtons.Primary).Idle(260);
            Assert.Equal(180, d.OfKind(CastEventKind.ChannelPulse).Count());
            Assert.Equal(15 + 180, d.TickOf(CastEventKind.ChannelEnded));
        }

        [Fact]
        public void Pulse_Interval_Spaces_Channel_Events()
        {
            var spell = new CastableSpell("TEST-BEAM", 20f, 300, 0, CastWeight.Heavy, true).WithChannel(60, 6);
            var d = Guided(0, spell);
            d.Step(InputButtons.Primary).Idle(100);
            var ticks = d.OfKind(CastEventKind.ChannelPulse).Select(e => e.Tick).ToArray();
            Assert.Equal(new[] { 6, 12, 18, 24, 30, 36, 42, 48, 54, 60 }, ticks);
        }

        [Fact]
        public void Anticipatory_Cue_Holds_Commitment_Inside_Preparation_Until_Release_Is_0_25_s_After_The_Cue()
        {
            // Spec L96: the 0.25 s cue is part of the listed preparation, not a secret delay after commitment.
            var spell = new CastableSpell("TEST-STUN", 20f, 300, 6, CastWeight.Control, false).WithAnticipatoryCue(true);
            var d = Guided(3, spell);
            d.Step(InputButtons.Primary).Idle(40);
            var cue = d.Single(CastEventKind.CueStarted);
            Assert.Equal(0, cue.Tick);
            Assert.Equal(15, cue.Ticks);
            Assert.Equal(9, d.TickOf(CastEventKind.Committed));   // grammar finished at 3, held until 15 - 6
            Assert.Equal(15, d.TickOf(CastEventKind.Released));
            Assert.Equal(6, d.Single(CastEventKind.Committed).Ticks); // intrinsic windup unchanged
        }

        [Fact]
        public void A_Held_Cue_Commit_Is_Still_Free_To_Cancel()
        {
            var spell = new CastableSpell("TEST-STUN", 20f, 300, 6, CastWeight.Control, false).WithAnticipatoryCue(true);
            var d = Guided(3, spell);
            d.Step(InputButtons.Primary).Idle(5);
            Assert.True(d.Machine.GrammarUi.Queued);
            Assert.Equal(9, d.Machine.GrammarUi.QueuedCommitTick);
            d.Step(InputButtons.Guard);                    // tick 6
            Assert.Equal(CancelReason.CommonAction, d.Single(CastEventKind.PreparationCancelled).Cancel);
            Assert.Equal(100f, d.Host.ManaPool);
        }

        [Fact]
        public void Long_Preparations_Need_No_Cue_Hold()
        {
            var spell = new CastableSpell("TEST-STUN", 20f, 300, 6, CastWeight.Control, false).WithAnticipatoryCue(true);
            var d = Guided(12, spell);
            d.Step(InputButtons.Primary).Idle(40);
            Assert.Equal(12, d.TickOf(CastEventKind.Committed));
            Assert.Equal(18, d.TickOf(CastEventKind.Released));
        }

        [Fact]
        public void Windup_Mastery_Never_Shortens_The_Cue()
        {
            var spell = new CastableSpell("TEST-STUN", 20f, 300, 10, CastWeight.Control, false).WithAnticipatoryCue(true);
            var grammar = new ScriptedGrammar(2, MasteryResult.Mastered(MasteryReward.WindupReduction, 0.15f));
            var d = new CastDriver(grammar, spell);
            d.Step(InputButtons.Primary).Idle(40);
            var committed = d.Single(CastEventKind.Committed);
            Assert.Equal(9, committed.Ticks);              // floor(10 * 0.15) = 1 tick shorter
            Assert.Equal(6, committed.Tick);               // held until 15 - 9
            Assert.Equal(15, d.TickOf(CastEventKind.Released));
        }

        [Fact]
        public void Catalogue_Cue_Spells_Are_Detected()
        {
            Assert.True(TestCatalogue.Spell("SPL-079").RequiresAnticipatoryCue); // root
            Assert.True(TestCatalogue.Spell("SPL-005").RequiresAnticipatoryCue); // teleport destination
            Assert.True(TestCatalogue.Spell("SPL-007").RequiresAnticipatoryCue); // "68; weak-point hit 85" > 70
            Assert.True(TestCatalogue.Spell("SPL-032").RequiresAnticipatoryCue); // silence
            Assert.True(TestCatalogue.Spell("SPL-048").RequiresAnticipatoryCue); // "70; weak-point hit 87.5"
            Assert.False(TestCatalogue.Spell("SPL-009").RequiresAnticipatoryCue); // 48 direct
            Assert.False(TestCatalogue.Spell("SPL-075").RequiresAnticipatoryCue); // shield
        }

        [Fact]
        public void Movement_Factor_Applies_From_Commitment_Through_Channel()
        {
            var d = Guided(15, Beam(30), TestSpells.Quick());
            Assert.Equal(1f, d.Machine.MoveSpeedFactor);
            d.Step(InputButtons.Primary);
            Assert.Equal(1f, d.Machine.MoveSpeedFactor);   // preparation keeps aim and movement live
            Assert.True(d.Machine.SuppressesSprint);       // heavy cast: sprint disallowed (spec L80)
            d.Idle(15);
            Assert.Equal(CastPhase.Windup, d.Machine.Phase);
            Assert.Equal(GlobalRules.Casting.MoveSpeedFactorHeavyCast, d.Machine.MoveSpeedFactor);
            d.Idle(15);
            Assert.Equal(CastPhase.Channel, d.Machine.Phase);
            Assert.Equal(0.70f, d.Machine.MoveSpeedFactor);
            d.Idle(30);
            Assert.Equal(CastPhase.Recovery, d.Machine.Phase);
            Assert.Equal(1f, d.Machine.MoveSpeedFactor);
            Assert.False(d.Machine.SuppressesSprint);
        }

        [Fact]
        public void Quick_Casts_Allow_Sprint_At_85_Percent_And_Stationary_Overrides()
        {
            var d = Guided(0, TestSpells.Quick());
            d.Step(InputButtons.Primary);
            Assert.Equal(CastPhase.Windup, d.Machine.Phase);
            Assert.Equal(0.85f, d.Machine.MoveSpeedFactor);
            Assert.False(d.Machine.SuppressesSprint);

            var still = Guided(0, TestSpells.Quick().WithStationary(true));
            still.Step(InputButtons.Primary);
            Assert.Equal(0f, still.Machine.MoveSpeedFactor);
        }
    }
}
