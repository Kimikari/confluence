using System.Linq;
using Confluence.Casting;
using Confluence.Casting.Grammars;
using Confluence.Casting.Tests.Support;

namespace Confluence.Casting.Tests.Machine
{
    public class PhaseTimingTests
    {
        private static CastDriver Guided(int prepTicks, params CastableSpell[] spells) =>
            new CastDriver(new GuidedNormalGrammar("TEST", GrammarFamily.ButtonTiming, prepTicks), spells);

        [Fact]
        public void Phases_Advance_On_Exact_Ticks()
        {
            // Guided preparation 15 ticks, windup 15 (0.25 s), quick recovery 0.20 s = 12 ticks (spec L92).
            var d = Guided(15, TestSpells.Quick());
            d.Step(InputButtons.Primary);
            Assert.Equal(CastPhase.Preparation, d.Machine.Phase);
            d.Idle(14);
            Assert.Equal(CastPhase.Preparation, d.Machine.Phase); // tick 14
            d.Idle(1);
            Assert.Equal(CastPhase.Windup, d.Machine.Phase);      // tick 15: committed
            d.Idle(14);
            Assert.Equal(CastPhase.Windup, d.Machine.Phase);      // tick 29
            d.Idle(1);
            Assert.Equal(CastPhase.Recovery, d.Machine.Phase);    // tick 30: released
            d.Idle(11);
            Assert.Equal(CastPhase.Recovery, d.Machine.Phase);    // tick 41
            d.Idle(1);
            Assert.Equal(CastPhase.Idle, d.Machine.Phase);        // tick 42

            Assert.Equal(0, d.TickOf(CastEventKind.PreparationStarted));
            Assert.Equal(15, d.TickOf(CastEventKind.Committed));
            Assert.Equal(30, d.TickOf(CastEventKind.Released));
            Assert.Equal(42, d.TickOf(CastEventKind.RecoveryEnded));
            Assert.Equal(15, d.Single(CastEventKind.Committed).Ticks);
        }

        [Fact]
        public void Commitment_Pays_Mana_And_Starts_Cooldown_Exactly_Once()
        {
            var d = Guided(15, TestSpells.Quick(mana: 20f));
            d.Step(InputButtons.Primary).Idle(14);
            Assert.Equal(100f, d.Host.ManaPool);                         // preparation spends nothing (spec L90)
            Assert.Equal(0, d.Host.CooldownRemainingTicks("TEST-QUICK"));
            d.Idle(1);                                                    // tick 15
            Assert.Equal(80f, d.Host.ManaPool);
            Assert.Equal(1, d.Host.Payments);
            Assert.Equal(20f, d.Single(CastEventKind.Committed).Mana);
            Assert.Equal(120, d.Host.CooldownRemainingTicks("TEST-QUICK"));
            d.Idle(60);
            Assert.Equal(1, d.Host.Payments);
        }

        [Fact]
        public void Instant_Spell_Releases_On_The_Commit_Tick_But_Still_Recovers()
        {
            // Spec L92: "Instant means zero windup, not zero recovery."
            var d = Guided(15, TestSpells.Quick(windupTicks: 0));
            d.Step(InputButtons.Primary).Idle(40);
            Assert.Equal(15, d.TickOf(CastEventKind.Committed));
            Assert.Equal(15, d.TickOf(CastEventKind.Released));
            Assert.Equal(27, d.TickOf(CastEventKind.RecoveryEnded));
        }

        [Fact]
        public void A_New_Preparation_Can_Open_On_The_Tick_Recovery_Ends()
        {
            var d = Guided(15, TestSpells.Quick("A"), TestSpells.Quick("B"));
            d.Step(InputButtons.Primary).Idle(20);
            d.Step(InputButtons.None, select: 1);          // tick 21, selection during windup does not cancel
            d.Idle(20);                                    // through tick 41
            Assert.Equal(CastPhase.Recovery, d.Machine.Phase);
            d.Step(InputButtons.Primary);                  // tick 42
            var started = d.OfKind(CastEventKind.PreparationStarted).ToList();
            Assert.Equal(2, started.Count);
            Assert.Equal(42, started[1].Tick);
            Assert.Equal("B", started[1].SpellId);
            Assert.Equal(42, d.TickOf(CastEventKind.RecoveryEnded));
            Assert.False(d.Has(CastEventKind.PreparationCancelled));
        }

        [Fact]
        public void Pressing_During_Recovery_Does_Not_Buffer_A_Cast()
        {
            var d = Guided(15, TestSpells.Quick("A"), TestSpells.Quick("B"));
            d.Step(InputButtons.Primary).Idle(34);
            d.Step(InputButtons.None, select: 1);
            d.Hold(InputButtons.Primary, 20);              // pressed at tick 36 inside recovery, held past its end
            Assert.Single(d.OfKind(CastEventKind.PreparationStarted));
        }

        [Fact]
        public void Recovery_Classes_Follow_Spec_Defaults()
        {
            // Spec L92: quick 0.20 s, heavy and control 0.35 s, major fields or summons 0.45 s.
            Assert.Equal(12, CastWeights.DefaultRecoveryTicks(CastWeight.Quick));
            Assert.Equal(21, CastWeights.DefaultRecoveryTicks(CastWeight.Heavy));
            Assert.Equal(21, CastWeights.DefaultRecoveryTicks(CastWeight.Control));
            Assert.Equal(27, CastWeights.DefaultRecoveryTicks(CastWeight.MajorFieldOrSummon));

            var d = Guided(0, new CastableSpell("FIELD", 10f, 60, 30, CastWeight.MajorFieldOrSummon, false));
            d.Step(InputButtons.Primary).Idle(80);
            Assert.Equal(0, d.TickOf(CastEventKind.Committed));
            Assert.Equal(30, d.TickOf(CastEventKind.Released));
            Assert.Equal(57, d.TickOf(CastEventKind.RecoveryEnded));
        }

        [Fact]
        public void Cast_Event_Ids_Are_Sequential_Per_Player_Including_Cancelled_Attempts()
        {
            var d = Guided(15, TestSpells.Quick("A"), TestSpells.Quick("B"));
            d.Step(InputButtons.Primary).Idle(3);
            d.Step(InputButtons.Dodge).Idle(20);           // cancelled attempt #1
            d.Step(InputButtons.Primary).Idle(60);         // committed attempt #2
            var ids = d.OfKind(CastEventKind.PreparationStarted).Select(e => e.CastId).ToList();
            Assert.Equal(new[] { new CastEventId(1, 1), new CastEventId(1, 2) }, ids);
            Assert.Equal(new CastEventId(1, 2), d.Single(CastEventKind.Committed).CastId);
            Assert.Equal(new CastEventId(1, 2), d.Single(CastEventKind.Released).CastId);
            Assert.Equal("P1#2", d.Single(CastEventKind.Released).CastId.ToString());
        }

        [Fact]
        public void Cooldown_Blocks_A_New_Preparation_With_A_Rejection()
        {
            var d = Guided(15, TestSpells.Quick());
            d.Step(InputButtons.Primary).Idle(60);
            d.Step(InputButtons.Primary);                  // tick 61, cooldown runs until 135
            var reject = d.Single(CastEventKind.Rejected);
            Assert.Equal(RejectReason.CooldownNotReady, reject.Reject);
            Assert.Single(d.OfKind(CastEventKind.PreparationStarted));
            d.Idle(80);                                    // cooldown ready at tick 135
            d.Step(InputButtons.Primary);
            Assert.Equal(2, d.OfKind(CastEventKind.PreparationStarted).Count());
        }

        [Fact]
        public void Guard_Held_Or_A_Common_Action_On_The_Same_Tick_Prevents_Opening()
        {
            var d = Guided(15, TestSpells.Quick());
            d.Step(InputButtons.Guard);
            d.Step(InputButtons.Guard | InputButtons.Primary);
            d.Step(InputButtons.None);
            d.Step(InputButtons.Primary | InputButtons.Dodge);
            Assert.False(d.Has(CastEventKind.PreparationStarted));
            Assert.False(d.Has(CastEventKind.Rejected));
        }
    }
}
