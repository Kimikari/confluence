using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using InputButtons = Confluence.Casting.InputButtons;
using Confluence.Sim.Players;

namespace Confluence.Sim.Tests.Movement
{
    /// <summary>Movement rules as the duel world applies them (spec L53, L80-L82).</summary>
    public class WorldMovementTests
    {
        [Fact]
        public void Dodge_Grants_No_Invulnerability()
        {
            DuelWorld w = TestWorlds.Duel();
            w.Step(TestWorlds.A.Moving(1f, 0f).With(InputButtons.Dodge), TestWorlds.B);
            Assert.True(w.Player(0).Motor.IsDodging);
            Assert.Equal(75f, w.Player(0).Resources.Stamina, 3);
            Assert.Equal(30f, w.ApplyDamage(TestWorlds.Hit(w, 1, 0, 30f)).Resolution.HealthLost, 4);
            Assert.False(GlobalRules.Movement.DodgeGrantsInvulnerability);
        }

        [Fact]
        public void Jumping_In_The_World_Costs_10_Stamina_Once_Per_Press()
        {
            DuelWorld w = TestWorlds.Duel();
            PlayerInput jump = TestWorlds.A.With(InputButtons.Jump);
            TestWorlds.Run(w, 30, jump, TestWorlds.B); // held: one jump, no auto-repeat while airborne
            Assert.Equal(90f, w.Player(0).Resources.Stamina, 3);
            Assert.Contains(w.EventLog, e => e is MovementEvent m && m.Change == MovementChange.Jumped);
        }

        [Fact]
        public void Movement_Bonus_Is_Capped_In_The_World()
        {
            DuelPlayerSetup fast = TestWorlds.Setup("A");
            fast.Modifiers.Add(new StatModifier(StatKind.MovementBonus, 0.5f, "EQP-BOOTS"));
            DuelWorld w = TestWorlds.Duel(a: fast);
            TestWorlds.RunSeconds(w, 1f, TestWorlds.A.Moving(0f, 1f), TestWorlds.B);
            Assert.Equal(GlobalRules.Movement.WalkSpeed * 1.2f, w.Player(0).Motor.Velocity.LengthXZ(), 3);
        }

        [Fact]
        public void Judgement_Indicator_Shows_In_The_Final_15_Seconds()
        {
            DuelWorld w = TestWorlds.Duel();
            w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 30f));
            Assert.False(w.JudgementIndicatorVisible);
            Assert.Equal(1, w.JudgementIndicator(0));
            Assert.Equal(-1, w.JudgementIndicator(1));
            TestWorlds.RunSeconds(w, GlobalRules.Round.JudgementIndicatorStartSec, TestWorlds.A, TestWorlds.B);
            Assert.True(w.JudgementIndicatorVisible);
        }

        [Fact]
        public void Knockback_Is_Forced_Movement_That_Locks_Deliberate_Input_For_At_Most_0_75_Seconds()
        {
            DuelWorld w = TestWorlds.Duel();
            Vec3 start = w.Player(0).Position;
            Assert.True(w.ApplyKnockback(0, 1, new Vec3(-1f, 0f, 0f), 4f, "SPL-PUSH"));
            Assert.True(w.Player(0).Statuses.IsDisplaced);
            TestWorlds.Run(w, 45, TestWorlds.A.Moving(0f, 1f), TestWorlds.B);
            Assert.False(w.Player(0).Statuses.IsDisplaced);
            Assert.Equal(start.X - 4f, w.Player(0).Position.X, 2);
            Assert.True(w.Player(0).Statuses.IsControlImmune(w.Tick));
        }
    }
}
