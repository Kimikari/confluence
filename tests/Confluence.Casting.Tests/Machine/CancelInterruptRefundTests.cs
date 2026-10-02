using System.Linq;
using Confluence.Casting;
using Confluence.Casting.Grammars;
using Confluence.Casting.Tests.Support;

namespace Confluence.Casting.Tests.Machine
{
    public class CancelInterruptRefundTests
    {
        private static CastDriver Guided(params CastableSpell[] spells) =>
            new CastDriver(new GuidedNormalGrammar("TEST", GrammarFamily.ButtonTiming, 15), spells);

        [Fact]
        public void Cancel_Before_Commitment_Spends_Nothing_Keeps_Exposure_And_Locks_Recasting_For_0_15_s()
        {
            var d = Guided(TestSpells.Quick());
            d.Step(InputButtons.Primary).Idle(4);
            d.Step(InputButtons.Guard);                    // tick 5
            var cancel = d.Single(CastEventKind.PreparationCancelled);
            Assert.Equal(5, cancel.Tick);
            Assert.Equal(CancelReason.CommonAction, cancel.Cancel);
            Assert.Equal(5, cancel.Ticks);                 // the exposed preparation stays spent (spec L90)
            Assert.Equal(5, d.Machine.TotalExposedPreparationTicks);
            Assert.Equal(100f, d.Host.ManaPool);
            Assert.Equal(0, d.Host.CooldownRemainingTicks("TEST-QUICK"));
            Assert.Equal(9, CastingRules.RecastLockTicks);
            Assert.True(d.Machine.IsRecastLocked(13));
            Assert.False(d.Machine.IsRecastLocked(14));
        }

        [Theory]
        [InlineData(13, false)]
        [InlineData(14, true)]
        public void Re_Cast_Lock_Ends_Exactly_Nine_Ticks_After_The_Cancel(int pressTick, bool opens)
        {
            var d = Guided(TestSpells.Quick());
            d.Step(InputButtons.Primary).Idle(4);
            d.Step(InputButtons.Guard);                    // cancel at tick 5
            d.Idle(pressTick - d.Tick);
            d.Step(InputButtons.Primary);
            Assert.Equal(opens ? 2 : 1, d.OfKind(CastEventKind.PreparationStarted).Count());
            if (!opens) Assert.Equal(RejectReason.RecastLocked, d.Single(CastEventKind.Rejected).Reject);
        }

        [Fact]
        public void Changing_The_Selected_Spell_Cancels_An_Open_Preparation()
        {
            var d = Guided(TestSpells.Quick("A"), TestSpells.Quick("B"));
            d.Step(InputButtons.Primary).Idle(5);
            d.Step(InputButtons.None, select: 1);
            var cancel = d.Single(CastEventKind.PreparationCancelled);
            Assert.Equal(CancelReason.SelectionChanged, cancel.Cancel);
            Assert.Equal("A", cancel.SpellId);
            Assert.Equal(1, d.Machine.SelectedSlot);
            Assert.Equal(100f, d.Host.ManaPool);
        }

        [Fact]
        public void Silence_Hard_Control_And_Ward_Cancel_Preparation_And_Refuse_New_Ones()
        {
            var d = Guided(TestSpells.Quick());
            d.Step(InputButtons.Primary).Idle(3);
            d.Host.IsSilenced = true;
            d.Idle(1);
            Assert.Equal(CancelReason.Silenced, d.Single(CastEventKind.PreparationCancelled).Cancel);
            d.Idle(20);
            d.Step(InputButtons.Primary);
            Assert.Equal(RejectReason.Silenced, d.OfKind(CastEventKind.Rejected).Last().Reject);

            d.Host.IsSilenced = false;
            d.Host.IsWarded = true;
            d.Idle(1);
            d.Step(InputButtons.Primary);
            Assert.Equal(RejectReason.Warded, d.OfKind(CastEventKind.Rejected).Last().Reject);

            d.Host.IsWarded = false;
            d.Host.IsHardControlled = true;
            d.Idle(1);
            d.Step(InputButtons.Primary);
            Assert.Equal(RejectReason.HardControlled, d.OfKind(CastEventKind.Rejected).Last().Reject);
            Assert.Equal(100f, d.Host.ManaPool);
        }

        [Fact]
        public void Holding_Primary_Through_The_Ward_Needs_A_Fresh_Press()
        {
            var d = Guided(TestSpells.Quick());
            d.Host.IsWarded = true;
            d.Hold(InputButtons.Primary, 10);
            d.Host.IsWarded = false;
            d.Hold(InputButtons.Primary, 10);
            Assert.False(d.Has(CastEventKind.PreparationStarted));
            d.Idle(1).Step(InputButtons.Primary);
            Assert.True(d.Has(CastEventKind.PreparationStarted));
        }

        [Fact]
        public void Insufficient_Mana_At_Commitment_Cancels_Without_Spending()
        {
            var d = Guided(TestSpells.Quick(mana: 20f));
            d.Host.ManaPool = 19f;
            d.Step(InputButtons.Primary).Idle(20);
            Assert.Equal(CancelReason.InsufficientMana, d.Single(CastEventKind.PreparationCancelled).Cancel);
            Assert.Equal(15, d.TickOf(CastEventKind.PreparationCancelled));
            Assert.Equal(19f, d.Host.ManaPool);
            Assert.False(d.Has(CastEventKind.Committed));
            Assert.Equal(0, d.Host.CooldownRemainingTicks("TEST-QUICK"));
        }

        [Fact]
        public void Interrupting_A_Committed_Cast_Spends_Mana_Keeps_Cooldown_And_Refunds_Nothing_Without_The_Tag()
        {
            var d = Guided(TestSpells.Quick(mana: 40f));
            d.Step(InputButtons.Primary).Idle(19);         // committed at 15, windup until 30
            d.Host.IsHardControlled = true;
            d.Idle(1);                                     // tick 20
            var interrupted = d.Single(CastEventKind.Interrupted);
            Assert.Equal(20, interrupted.Tick);
            Assert.Equal(InterruptCause.HardControl, interrupted.Interrupt);
            Assert.Equal(0f, interrupted.Mana);
            Assert.Equal(60f, d.Host.ManaPool);
            Assert.True(d.Host.CooldownRemainingTicks("TEST-QUICK") > 0);
            Assert.False(d.Has(CastEventKind.Released));
            Assert.Equal(CastPhase.Idle, d.Machine.Phase);
        }

        [Fact]
        public void Interrupt_Refund_Tag_Returns_25_Percent_Once()
        {
            var spell = TestSpells.Quick(mana: 40f).WithInterruptRefund(true);
            var d = Guided(spell);
            d.Step(InputButtons.Primary).Idle(19);
            d.Host.IsSilenced = true;
            d.Idle(1);
            var interrupted = d.Single(CastEventKind.Interrupted);
            Assert.Equal(10f, interrupted.Mana);           // 25% of 40 (spec L90)
            Assert.Equal(70f, d.Host.ManaPool);
            var id = interrupted.CastId;

            // The cap is spent: no further refund effect can return mana for this cast (spec L90, L124).
            var events = new System.Collections.Generic.List<CastEvent>();
            Assert.Equal(0f, d.Machine.TryRefund(id, "AUG-TEST", 5f, d.Tick, events));
            Assert.Empty(events);
            Assert.Equal(10f, d.Host.TotalRefunded);
        }

        [Fact]
        public void Interrupt_Refund_Uses_The_Paid_Amount_After_Reductions()
        {
            var d = Guided(TestSpells.Quick(mana: 40f).WithInterruptRefund(true));
            d.Host.Modifiers = new CastModifiers { CostReduction = 0.25f };
            d.Step(InputButtons.Primary).Idle(19);
            Assert.Equal(30f, d.Single(CastEventKind.Committed).Mana);
            d.Machine.Interrupt(InterruptCause.SpellRule, d.Tick, d.Events);
            Assert.Equal(7.5f, d.Single(CastEventKind.Interrupted).Mana);
        }

        [Fact]
        public void Voluntary_Cancel_After_Commitment_Gives_No_Refund_Even_With_The_Tag()
        {
            var d = Guided(TestSpells.Quick(mana: 40f).WithInterruptRefund(true));
            d.Step(InputButtons.Primary).Idle(19);
            d.Step(InputButtons.Dodge);                    // tick 20, inside the windup
            var cancel = d.Single(CastEventKind.CommittedCastCancelled);
            Assert.Equal(40f, cancel.Mana);
            Assert.Equal(60f, d.Host.ManaPool);
            Assert.Equal(0f, d.Host.TotalRefunded);
            Assert.True(d.Host.CooldownRemainingTicks("TEST-QUICK") > 0);
            Assert.False(d.Has(CastEventKind.Released));
            Assert.Equal(CastPhase.Idle, d.Machine.Phase);
            Assert.False(d.Machine.IsRecastLocked(d.Tick));  // the 0.15 s lock is a pre-commit rule only
        }

        [Fact]
        public void Refund_Effects_Share_A_25_Percent_Cap_And_Fire_Once_Per_Cast()
        {
            var d = Guided(TestSpells.Quick(mana: 40f));
            d.Step(InputButtons.Primary).Idle(40);
            var id = d.Single(CastEventKind.Committed).CastId;
            var events = new System.Collections.Generic.List<CastEvent>();
            Assert.Equal(6f, d.Machine.TryRefund(id, "AUG-A", 6f, d.Tick, events));
            Assert.Equal(0f, d.Machine.TryRefund(id, "AUG-A", 1f, d.Tick, events));   // once per cast
            Assert.Equal(4f, d.Machine.TryRefund(id, "AUG-B", 10f, d.Tick, events));  // shared cap 10
            Assert.Equal(0f, d.Machine.TryRefund(id, "AUG-C", 1f, d.Tick, events));
            Assert.Equal(0f, d.Machine.TryRefund(new CastEventId(1, 99), "AUG-D", 1f, d.Tick, events));
            Assert.Equal(10f, d.Host.TotalRefunded);
            Assert.Equal(2, events.Count(e => e.Kind == CastEventKind.Refunded));
            Assert.Equal(0f, d.Machine.RefundRemaining(id));
        }

        [Fact]
        public void External_Interrupt_Of_An_Open_Preparation_Is_A_Free_Cancel()
        {
            var d = Guided(TestSpells.Quick());
            d.Step(InputButtons.Primary).Idle(3);
            Assert.True(d.Machine.Interrupt(InterruptCause.SpellRule, d.Tick, d.Events));
            Assert.Equal(CancelReason.External, d.Single(CastEventKind.PreparationCancelled).Cancel);
            Assert.Equal(100f, d.Host.ManaPool);
            Assert.False(d.Machine.Interrupt(InterruptCause.SpellRule, d.Tick, d.Events));
        }

        [Fact]
        public void Recovery_Cannot_Be_Cancelled_Or_Interrupted()
        {
            var d = Guided(TestSpells.Quick());
            d.Step(InputButtons.Primary).Idle(32);         // released at 30, recovery until 42
            d.Step(InputButtons.Dodge);
            d.Host.IsHardControlled = true;
            d.Idle(1);
            Assert.Equal(CastPhase.Recovery, d.Machine.Phase);
            Assert.False(d.Has(CastEventKind.Interrupted));
            Assert.False(d.Has(CastEventKind.CommittedCastCancelled));
            d.Host.IsHardControlled = false;
            d.Idle(10);
            Assert.Equal(42, d.TickOf(CastEventKind.RecoveryEnded));
        }
    }
}
