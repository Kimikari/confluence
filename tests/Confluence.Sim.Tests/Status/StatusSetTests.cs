using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Status;

namespace Confluence.Sim.Tests.Status
{
    /// <summary>Status rules (spec L116-L120).</summary>
    public class StatusSetTests
    {
        private static StatusApplication Of(StatusKind kind, string def, float seconds, float intensity = 0f)
        {
            return StatusApplication.Of(kind, def, 0, Ticks.FromSeconds(seconds), intensity);
        }

        private static void TickTo(StatusSet set, int from, int to, List<DotPulse> pulses = null)
        {
            for (int t = from; t <= to; t++)
            {
                set.Tick(t, pulses, null);
            }
        }

        [Fact]
        public void Hard_Control_Application_Caps_At_0_75_Seconds_And_Silence_At_0_6()
        {
            var set = new StatusSet();
            Assert.Equal(45, set.Apply(Of(StatusKind.Stun, "SPL-A", 2f), 0).Instance.ExpireTick);
            var other = new StatusSet();
            Assert.Equal(36, other.Apply(Of(StatusKind.Silence, "SPL-B", 2f), 0).Instance.ExpireTick);
            Assert.True(StatusRules.IsHardControl(StatusKind.Root));
            Assert.True(StatusRules.IsHardControl(StatusKind.Displaced));
        }

        [Fact]
        public void Control_Immunity_Lasts_2_Seconds_After_Control_Ends_And_Nothing_Is_Queued()
        {
            var set = new StatusSet();
            set.Apply(Of(StatusKind.Root, "SPL-A", 0.5f), 0);
            TickTo(set, 1, 30);
            Assert.False(set.HasHardControl());
            Assert.Equal(30 + 120, set.ControlImmuneUntil);

            Assert.Equal(StatusOutcome.BlockedByImmunity, set.Apply(Of(StatusKind.Stun, "SPL-B", 0.5f), 100).Outcome);
            TickTo(set, 100, 160);
            Assert.False(set.IsStunned); // the refused stun did not wait for the immunity to end
            Assert.Equal(StatusOutcome.Applied, set.Apply(Of(StatusKind.Stun, "SPL-B", 0.5f), 160).Outcome);
        }

        [Fact]
        public void Overlapping_Hard_Control_Cannot_Extend_Past_One_Window()
        {
            var set = new StatusSet();
            set.Apply(Of(StatusKind.Stun, "SPL-A", 0.75f), 0);
            StatusApplyResult root = set.Apply(Of(StatusKind.Root, "SPL-B", 0.75f), 30);
            Assert.Equal(45, root.Instance.ExpireTick);
            TickTo(set, 1, 45);
            Assert.False(set.HasHardControl());
            Assert.True(set.IsControlImmune(46));
        }

        [Fact]
        public void Only_The_Strongest_Slow_Applies_Capped_At_30_Percent_And_3_Seconds()
        {
            var set = new StatusSet();
            set.Apply(Of(StatusKind.Slow, "SPL-A", 2f, 0.2f), 0);
            StatusApplyResult strong = set.Apply(Of(StatusKind.Slow, "SPL-B", 6f, 0.5f), 0);
            Assert.Equal(0.3f, strong.Instance.Intensity, 5);
            Assert.Equal(180, strong.Instance.ExpireTick);
            Assert.Equal(0.3f, set.StrongestSlow, 5);
            TickTo(set, 1, 180);
            Assert.Equal(0f, set.StrongestSlow);
        }

        [Fact]
        public void Same_Definition_DoT_Refreshes_And_Keeps_The_Strongest_Intensity()
        {
            var set = new StatusSet();
            set.Apply(Of(StatusKind.Burn, "SPL-FIRE", 2f, 10f), 0);
            StatusApplyResult again = set.Apply(Of(StatusKind.Burn, "SPL-FIRE", 3f, 6f), 60);
            Assert.Equal(StatusOutcome.Refreshed, again.Outcome);
            Assert.Equal(10f, again.Instance.Intensity);
            Assert.Equal(240, again.Instance.ExpireTick);
            Assert.Single(set.Active);

            set.Apply(Of(StatusKind.Burn, "SPL-OTHER", 2f, 4f), 60);
            Assert.Equal(2, set.Active.Count); // different definitions coexist
        }

        [Fact]
        public void DoT_Pulses_Its_Per_Second_Intensity_In_Half_Second_Pulses()
        {
            var set = new StatusSet();
            set.Apply(Of(StatusKind.Venom, "SPL-VENOM", 2f, 8f), 0);
            var pulses = new List<DotPulse>();
            TickTo(set, 1, 200, pulses);
            float total = 0f;
            foreach (DotPulse p in pulses)
            {
                total += p.Amount;
            }

            Assert.Equal(4, pulses.Count);
            Assert.Equal(16f, total, 4);
        }

        [Fact]
        public void Dispel_Removes_The_Oldest_Removable_Hostile_Status_Or_The_Selected_One()
        {
            var set = new StatusSet();
            set.Apply(Of(StatusKind.Invisibility, "SPL-VEIL", 1f), 0);
            set.Apply(Of(StatusKind.Burn, "SPL-FIRE", 3f, 5f), 1);
            set.Apply(Of(StatusKind.Slow, "SPL-ICE", 3f, 0.2f), 2);
            set.Apply(Of(StatusKind.Marked, "SPL-MARK", 3f), 3);
            Assert.Equal(StatusKind.Burn, set.Dispel(4).Kind);
            Assert.Equal(StatusKind.Marked, set.Dispel(4, StatusKind.Marked).Kind);
            Assert.Equal(StatusKind.Slow, set.Dispel(4).Kind);
            Assert.Null(set.Dispel(4)); // invisibility is not hostile
            Assert.True(set.IsInvisible);
        }

        [Fact]
        public void Dispel_Never_Removes_Control_Immunity()
        {
            var set = new StatusSet();
            set.Apply(Of(StatusKind.Stun, "SPL-A", 0.5f), 0);
            Assert.Equal(StatusKind.Stun, set.Dispel(10).Kind);
            Assert.True(set.IsControlImmune(11));
            Assert.Null(set.Dispel(11));
            Assert.True(set.IsControlImmune(11));
        }

        [Fact]
        public void Reveal_Lasts_At_Most_2_Seconds_And_Invisibility_1_5()
        {
            var set = new StatusSet();
            Assert.Equal(120, set.Apply(Of(StatusKind.Reveal, "SPL-EYE", 5f), 0).Instance.ExpireTick);
            Assert.Equal(90, set.Apply(Of(StatusKind.Invisibility, "SPL-VEIL", 5f), 0).Instance.ExpireTick);
        }

        [Fact]
        public void Control_During_Immunity_Still_Deals_Damage_In_The_World()
        {
            DuelWorld w = TestWorlds.Duel();
            w.ApplyStatus(1, StatusApplication.Of(StatusKind.Stun, "SPL-A", 0, 30));
            TestWorlds.Run(w, 31, TestWorlds.A, TestWorlds.B);
            Assert.False(w.Player(1).Statuses.IsStunned);

            DamageEvent hit = w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 25f));
            StatusApplyResult blocked = w.ApplyStatus(1, StatusApplication.Of(StatusKind.Root, "SPL-B", 0, 30));
            Assert.Equal(25f, hit.Resolution.HealthLost, 4);
            Assert.Equal(StatusOutcome.BlockedByImmunity, blocked.Outcome);
            Assert.Contains(w.TickEvents, e => e is StatusEvent s && s.Change == StatusChange.BlockedByImmunity);

            // Hostile knockback is hard control too: blocked, with the damage still applied.
            DamageRequest push = TestWorlds.Hit(w, 0, 1, 10f);
            push.Knockback = 4f;
            Assert.Equal(10f, w.ApplyDamage(push).Resolution.HealthLost, 4);
            Assert.False(w.Player(1).Motor.IsDisplaced);
        }

        [Fact]
        public void Stun_Locks_Movement_And_Actions_In_The_World()
        {
            DuelWorld w = TestWorlds.Duel();
            w.ApplyStatus(0, StatusApplication.Of(StatusKind.Stun, "SPL-A", 1, 45));
            Vec3 before = w.Player(0).Position;
            TestWorlds.Run(w, 20, TestWorlds.A.Moving(0f, 1f).With(InputButtons.Melee), TestWorlds.B);
            Assert.Equal(before.Z, w.Player(0).Position.Z, 3);
            Assert.False(w.Player(0).Melee.Busy);
            Assert.False(w.CastPermissionsFor(w.Player(0)).CanCast);
        }

        [Fact]
        public void Slow_Reduces_Movement_Speed_In_The_World()
        {
            DuelWorld w = TestWorlds.Duel();
            w.ApplyStatus(0, StatusApplication.Of(StatusKind.Slow, "SPL-ICE", 1, 180, 0.5f));
            TestWorlds.Run(w, 30, TestWorlds.A.Moving(0f, 1f), TestWorlds.B);
            Assert.Equal(GlobalRules.Movement.WalkSpeed * 0.7f, w.Player(0).Motor.Velocity.LengthXZ(), 3);
        }

        [Fact]
        public void Invisibility_Ends_On_Damage_And_On_Attack_And_Keeps_A_4_Meter_Silhouette()
        {
            DuelWorld w = TestWorlds.Duel();
            w.ApplyStatus(1, StatusApplication.Of(StatusKind.Invisibility, "SPL-VEIL", 1, 90));
            Assert.False(w.IsVisibleTo(0, 1)); // 12 m apart
            TestWorlds.Place(w, 0, new Vec3(0f, 0f, 2.5f), 0f);
            Assert.True(w.IsVisibleTo(0, 1)); // 3.5 m: the nearby silhouette
            w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 5f));
            Assert.False(w.Player(1).Statuses.IsInvisible);

            w.ApplyStatus(0, StatusApplication.Of(StatusKind.Invisibility, "SPL-VEIL", 0, 90));
            w.Step(TestWorlds.A.With(InputButtons.Melee), TestWorlds.B);
            Assert.False(w.Player(0).Statuses.IsInvisible);
            Assert.Contains(w.TickEvents, e => e is StatusEvent s && s.Change == StatusChange.Broken);
        }

        [Fact]
        public void Hidden_Players_Cannot_Be_Acquired()
        {
            DuelWorld w = TestWorlds.Duel();
            Assert.True(w.CanAcquire(0, 1));
            w.ApplyStatus(1, StatusApplication.Of(StatusKind.Invisibility, "SPL-VEIL", 1, 90));
            Assert.False(w.CanAcquire(0, 1));
        }
    }
}
