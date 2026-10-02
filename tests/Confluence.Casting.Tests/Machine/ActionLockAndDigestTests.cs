using System.Linq;
using Confluence.Casting;
using Confluence.Casting.Grammars;
using Confluence.Casting.Tests.Support;

namespace Confluence.Casting.Tests.Machine
{
    /// <summary>Host action locks (melee, guard release window, swap, recall) and the machine state digest.</summary>
    public class ActionLockAndDigestTests
    {
        private static CastDriver Guided(params CastableSpell[] spells) =>
            new CastDriver(new GuidedNormalGrammar("TEST", GrammarFamily.ButtonTiming, 15), spells);

        [Fact]
        public void An_Action_Lock_Refuses_New_Casts_Without_Cancelling_A_Commitment()
        {
            var d = Guided(TestSpells.Heavy());
            d.Host.IsActionLocked = true;
            d.Step(InputButtons.Primary);
            Assert.Equal(RejectReason.ActionLocked, d.Single(CastEventKind.Rejected).Reject);
            Assert.False(d.Has(CastEventKind.PreparationStarted));

            d.Host.IsActionLocked = false;
            d.Idle(1).Step(InputButtons.Primary);
            d.IdleUntil(CastEventKind.Committed);
            d.Host.IsActionLocked = true;               // a lock never interrupts the committed windup
            d.IdleUntil(CastEventKind.Released);
            Assert.False(d.Has(CastEventKind.Interrupted));
            Assert.Equal(70f, d.Host.ManaPool);
        }

        [Fact]
        public void An_Action_Lock_Refuses_A_Recast()
        {
            var spell = TestSpells.Quick().WithRecast(RecastRule.Default);
            var d = Guided(spell);
            d.Step(InputButtons.Primary).IdleUntil(CastEventKind.Released);
            d.IdleUntil(CastEventKind.RecoveryEnded);
            d.Host.IsActionLocked = true;
            d.Step(InputButtons.Recast);
            Assert.Equal(RejectReason.ActionLocked, d.OfKind(CastEventKind.Rejected).Last().Reject);
            Assert.NotNull(d.Machine.PendingRecastFor(0));
        }

        [Fact]
        public void Stationary_And_Stated_Move_Factors_Override_The_Class_Default()
        {
            var quick = TestSpells.Quick();
            Assert.Equal(0.85f, quick.CastingMoveSpeedFactor);
            Assert.Equal(0.8f, quick.WithMoveSpeedFactor(0.8f).CastingMoveSpeedFactor);
            Assert.Equal(0f, quick.WithMoveSpeedFactor(0.8f).WithStationary(true).CastingMoveSpeedFactor);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => quick.WithMoveSpeedFactor(1.2f));
        }

        [Fact]
        public void The_State_Digest_Follows_Phase_And_Payment_And_Is_Reproducible()
        {
            var a = Guided(TestSpells.Quick());
            var b = Guided(TestSpells.Quick());
            Assert.Equal(a.Machine.StateDigest(), b.Machine.StateDigest());
            ulong idle = a.Machine.StateDigest();
            a.Step(InputButtons.Primary);
            b.Step(InputButtons.Primary);
            Assert.NotEqual(idle, a.Machine.StateDigest());
            Assert.Equal(a.Machine.StateDigest(), b.Machine.StateDigest());
            a.IdleUntil(CastEventKind.Committed);
            b.IdleUntil(CastEventKind.Committed);
            Assert.Equal(a.Machine.StateDigest(), b.Machine.StateDigest());
            b.Step(InputButtons.Guard);
            a.Step(InputButtons.None);
            Assert.NotEqual(a.Machine.StateDigest(), b.Machine.StateDigest());
            Assert.Equal(1, b.OfKind(CastEventKind.CommittedCastCancelled).Count());
        }
    }
}
