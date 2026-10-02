using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;

namespace Confluence.Sim.Tests.Combat
{
    /// <summary>Common guard (spec L104-L106).</summary>
    public class GuardTests
    {
        private static readonly PlayerInput Hold = TestWorlds.B.With(InputButtons.Guard);

        // A hit whose source lies offDeg away from player 1's facing (yaw 180 toward -Z), 5 m away.
        private static DamageRequest HitFrom(DuelWorld w, float offDeg, DamageKind kind = DamageKind.Direct)
        {
            Vec3 source = w.Player(1).Position + CMath.ForwardFromYaw(180f + offDeg) * 5f;
            return DamageRequest.Single(0, 1, kind, "SPL-TEST", DamageChannel.Physical, 100f).From(source);
        }

        [Fact]
        public void Guard_Costs_25_Stamina_Upfront_And_Needs_The_Full_Cost()
        {
            DuelWorld w = TestWorlds.Duel();
            TestWorlds.StartGuard(w);
            Assert.True(w.Player(1).Guard.Active);
            Assert.Equal(75f, w.Player(1).Resources.Stamina, 3);

            DuelWorld tired = TestWorlds.Duel();
            tired.Player(1).Resources.TrySpendStamina(80f, 0);
            TestWorlds.StartGuard(tired);
            Assert.False(tired.Player(1).Guard.Active);
        }

        [Fact]
        public void Guard_Lasts_At_Most_0_8_Seconds_While_Held()
        {
            DuelWorld w = TestWorlds.Duel();
            int start = TestWorlds.StartGuard(w);
            TestWorlds.Run(w, GuardState.MaxHoldTicks - 1, TestWorlds.A, Hold);
            Assert.True(w.Player(1).Guard.Active);
            w.Step(TestWorlds.A, Hold);
            Assert.False(w.Player(1).Guard.Active);
            Assert.Equal(start + 48, w.Player(1).Guard.ReleaseTick);
            Assert.Contains(w.TickEvents, e => e is GuardEvent g && g.Change == GuardChange.Released);
        }

        [Fact]
        public void Cooldown_Runs_3_Seconds_From_Release()
        {
            DuelWorld w = TestWorlds.Duel();
            TestWorlds.StartGuard(w);
            TestWorlds.Run(w, 10, TestWorlds.A, Hold);
            w.Step(TestWorlds.A, TestWorlds.B); // release
            int release = w.Tick;
            TestWorlds.Run(w, GuardState.CooldownTicks - 2, TestWorlds.A, TestWorlds.B);
            w.Step(TestWorlds.A, Hold); // tick release + 179: still cooling down
            Assert.False(w.Player(1).Guard.Active);
            w.Step(TestWorlds.A, TestWorlds.B);
            Assert.Equal(release + GuardState.CooldownTicks, w.Tick); // cooldown over; the next fresh press starts a guard
            w.Step(TestWorlds.A, Hold);
            Assert.True(w.Player(1).Guard.Active);
        }

        [Fact]
        public void Release_Leaves_A_0_25_Second_Punishable_Window()
        {
            DuelWorld w = TestWorlds.Duel();
            TestWorlds.StartGuard(w);
            TestWorlds.Run(w, 10, TestWorlds.A, Hold);
            w.Step(TestWorlds.A, TestWorlds.B);
            Assert.True(w.Player(1).Guard.InReleaseWindow(w.Tick));
            Assert.Equal(CastBlockReason.GuardRelease, w.CastPermissionsFor(w.Player(1)).Reason);

            PlayerInput dodgeAndStrike = TestWorlds.B.With(InputButtons.Dodge | InputButtons.Melee);
            w.Step(TestWorlds.A, dodgeAndStrike);
            Assert.False(w.Player(1).Motor.IsDodging);
            Assert.False(w.Player(1).Melee.Busy);
            Assert.Equal(GuardOutcome.None, w.ApplyDamage(HitFrom(w, 0f)).Resolution.Guard); // full damage lands

            TestWorlds.Run(w, GuardState.PunishTicks, TestWorlds.A, TestWorlds.B);
            Assert.False(w.Player(1).Guard.InReleaseWindow(w.Tick));
            w.Step(TestWorlds.A, dodgeAndStrike);
            Assert.True(w.Player(1).Motor.IsDodging);
        }

        [Fact]
        public void Only_The_Frontal_120_Degree_Arc_Is_Guarded()
        {
            DuelWorld w = TestWorlds.Duel();
            TestWorlds.StartGuard(w);
            TestWorlds.Run(w, GuardState.PerfectTicks, TestWorlds.A, Hold);
            Assert.Equal(GuardOutcome.Guarded, w.ApplyDamage(HitFrom(w, 0f)).Resolution.Guard);
            Assert.Equal(GuardOutcome.Guarded, w.ApplyDamage(HitFrom(w, 55f)).Resolution.Guard);
            Assert.Equal(GuardOutcome.Guarded, w.ApplyDamage(HitFrom(w, -55f)).Resolution.Guard);
            Assert.Equal(GuardOutcome.None, w.ApplyDamage(HitFrom(w, 70f)).Resolution.Guard);   // flank
            Assert.Equal(GuardOutcome.None, w.ApplyDamage(HitFrom(w, 180f)).Resolution.Guard);  // rear
        }

        [Fact]
        public void Guard_Does_Not_Stop_Fields_Environment_Statuses_Or_Sound_Without_A_Projectile()
        {
            DuelWorld w = TestWorlds.Duel();
            TestWorlds.StartGuard(w);
            TestWorlds.Run(w, GuardState.PerfectTicks, TestWorlds.A, Hold);
            Assert.Equal(GuardOutcome.None, w.ApplyDamage(HitFrom(w, 0f, DamageKind.Splash)).Resolution.Guard);
            Assert.Equal(GuardOutcome.None, w.ApplyDamage(HitFrom(w, 0f, DamageKind.Environment)).Resolution.Guard);
            Assert.Equal(GuardOutcome.None, w.ApplyDamage(HitFrom(w, 0f, DamageKind.Status)).Resolution.Guard);
            DamageRequest sound = HitFrom(w, 0f);
            sound.GuardEligible = false;
            Assert.Equal(GuardOutcome.None, w.ApplyDamage(sound).Resolution.Guard);
            Assert.Equal(GuardOutcome.Guarded, w.ApplyDamage(HitFrom(w, 0f, DamageKind.Melee)).Resolution.Guard);
        }

        [Fact]
        public void Perfect_Guard_Grants_5_Mana_Once_Per_Guard()
        {
            DuelWorld w = TestWorlds.Duel();
            w.TryCommitMana(1, 30f, "SPL-TEST");
            TestWorlds.StartGuard(w);
            w.ApplyDamage(HitFrom(w, 0f));
            w.ApplyDamage(HitFrom(w, 10f));
            Assert.Equal(75f, w.Player(1).Resources.Mana, 3);
            int perfect = 0;
            foreach (DuelEvent e in w.TickEvents)
            {
                if (e is GuardEvent g && g.Change == GuardChange.PerfectBlocked)
                {
                    perfect++;
                    Assert.Equal(perfect == 1 ? GlobalRules.Guard.PerfectManaGain : 0f, g.ManaGained);
                }
            }

            Assert.Equal(2, perfect);
            Assert.Equal(1, w.Player(1).Feats.PerfectGuards);
        }

        [Fact]
        public void Guard_Is_Unavailable_During_The_Alcove_Ward()
        {
            DuelWorld w = TestWorlds.Duel(alcoveTicks: 120);
            TestWorlds.StartGuard(w);
            Assert.False(w.Player(1).Guard.Active);
        }
    }
}
