using Confluence.Core.Rules;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using InputButtons = Confluence.Casting.InputButtons;
using Confluence.Sim.Players;
using Confluence.Sim.Status;

namespace Confluence.Sim.Tests.Combat
{
    /// <summary>The spec L112 resolution order with exact numbers.</summary>
    public class DamagePipelineTests
    {
        [Fact]
        public void Unmitigated_Hit_Removes_Health()
        {
            DuelWorld w = TestWorlds.Duel();
            DamageEvent e = w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 40f));
            Assert.Equal(40f, e.Resolution.HealthLost, 4);
            Assert.Equal(260f, w.Player(1).Resources.Health, 4);
        }

        [Fact]
        public void Precision_Applies_Only_To_Tagged_Spells_And_Caps_At_1_25()
        {
            DuelWorld w = TestWorlds.Duel();
            DamageRequest untagged = TestWorlds.Hit(w, 0, 1, 40f);
            untagged.PrecisionMultiplier = 1.5f;
            Assert.Equal(40f, w.ApplyDamage(untagged).Resolution.AfterPrecision, 4);

            DamageRequest tagged = TestWorlds.Hit(w, 0, 1, 40f);
            tagged.PrecisionTagged = true;
            tagged.PrecisionMultiplier = 1.5f; // stated above the global maximum
            Assert.Equal(50f, w.ApplyDamage(tagged).Resolution.HealthLost, 4);
        }

        [Fact]
        public void Guard_Halves_Frontal_Direct_Damage_And_Perfect_Guard_Removes_80_Percent()
        {
            DuelWorld w = TestWorlds.Duel();
            int start = TestWorlds.StartGuard(w);
            Assert.True(w.Player(1).Guard.IsPerfect(w.Tick));
            DamageEvent perfect = w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 100f));
            Assert.Equal(GuardOutcome.PerfectGuard, perfect.Resolution.Guard);
            Assert.Equal(20f, perfect.Resolution.HealthLost, 4);

            TestWorlds.Run(w, GuardState.PerfectTicks, TestWorlds.A, TestWorlds.B.With(InputButtons.Guard));
            Assert.Equal(start + GuardState.PerfectTicks, w.Tick);
            DamageEvent normal = w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 100f));
            Assert.Equal(GuardOutcome.Guarded, normal.Resolution.Guard);
            Assert.Equal(50f, normal.Resolution.HealthLost, 4);
        }

        [Fact]
        public void Guard_Applies_Before_Resistance_And_Is_Exempt_From_The_Passive_Cap()
        {
            DuelWorld w = TestWorlds.Duel();
            w.Player(1).Stats.Add(StatKind.Resistance, 0.35f, "TEST", DamageChannel.Physical);
            w.Player(1).Stats.Add(StatKind.PassiveReduction, 0.2f, "TEST");
            TestWorlds.StartGuard(w);
            TestWorlds.Run(w, GuardState.PerfectTicks, TestWorlds.A, TestWorlds.B.With(InputButtons.Guard));
            DamageResolution r = w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 100f)).Resolution;
            Assert.Equal(50f, r.AfterGuard, 4);
            Assert.Equal(32.5f, r.AfterResistance, 4);
            Assert.Equal(27.5f, r.Mitigated, 4); // passive total 48% capped at 45%: 50 x 0.55
        }

        [Fact]
        public void Channel_Resistance_Caps_At_35_And_Floors_At_Minus_15()
        {
            DuelWorld w = TestWorlds.Duel();
            w.Player(1).Stats.Add(StatKind.Resistance, 0.5f, "TEST", DamageChannel.Thermal);
            w.Player(1).Stats.Add(StatKind.Resistance, -0.4f, "TEST", DamageChannel.Electrical);
            Assert.Equal(65f, w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 100f, channel: DamageChannel.Thermal)).Resolution.HealthLost, 3);
            Assert.Equal(115f, w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 100f, channel: DamageChannel.Electrical)).Resolution.HealthLost, 3);
            Assert.Equal(100f, w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 100f, channel: DamageChannel.Arcane)).Resolution.HealthLost, 3);
        }

        [Fact]
        public void Passive_Reductions_Combine_Multiplicatively_Up_To_45_Percent()
        {
            DuelWorld w = TestWorlds.Duel();
            StatSheet stats = w.Player(1).Stats;
            stats.Add(StatKind.PassiveReduction, 0.2f, "ARMOR");
            stats.Add(StatKind.PassiveReduction, 0.1f, "TRINKET");
            Assert.Equal(72f, w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 100f)).Resolution.HealthLost, 3); // 0.8 x 0.9
            stats.Add(StatKind.Resistance, 0.3f, "CLOAK", DamageChannel.Physical);
            Assert.Equal(55f, w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 100f)).Resolution.HealthLost, 3); // 49.6% capped at 45%
        }

        [Fact]
        public void Shields_Absorb_Earliest_Expiring_First_And_Share_A_100_Cap()
        {
            DuelWorld w = TestWorlds.Duel();
            Assert.Equal(50f, w.GrantShield(1, "AUG-RESERVE", 50f, 0, reserve: true));
            Assert.Equal(30f, w.GrantShield(1, "SPL-WARD", 30f, 240));
            Assert.Equal(20f, w.GrantShield(1, "SPL-OTHER", 60f, 120)); // only 20 room under the cap
            ShieldPool shields = w.Player(1).Shields;
            Assert.Equal(100f, shields.Total, 4);

            DamageResolution r = w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 40f)).Resolution;
            Assert.Equal(40f, r.ShieldAbsorbed, 4);
            Assert.Equal(0f, r.HealthLost);
            Assert.Equal(50f, shields.ReserveTotal, 4); // the 2 s and 4 s shields went first
            Assert.Equal(10f, shields.TemporaryTotal, 4);

            r = w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 80f)).Resolution;
            Assert.Equal(60f, r.ShieldAbsorbed, 4);
            Assert.Equal(20f, r.HealthLost, 4);
            Assert.Equal(0f, shields.Total);
        }

        [Fact]
        public void Timed_Shields_Expire_Within_8_Seconds_And_Reserve_Shields_Persist()
        {
            DuelWorld w = TestWorlds.Duel();
            w.GrantShield(1, "AUG-RESERVE", 30f, 0, reserve: true);
            w.GrantShield(1, "SPL-WARD", 40f, 20 * 60); // asks for 20 s
            Assert.Equal(ShieldPool.MaxTimedTicks, w.Player(1).Shields.Entries[0].ExpireTick);
            TestWorlds.RunSeconds(w, 8.1f, TestWorlds.A, TestWorlds.B);
            Assert.Equal(30f, w.Player(1).Shields.Total, 4);
            Assert.Equal(30f, w.Player(1).Shields.ReserveTotal, 4);
        }

        [Fact]
        public void Larger_Shield_From_The_Same_Source_Tops_Up_Instead_Of_Stacking()
        {
            DuelWorld w = TestWorlds.Duel();
            w.GrantShield(1, "SPL-WARD", 30f, 300);
            Assert.Equal(15f, w.GrantShield(1, "SPL-WARD", 45f, 300));
            Assert.Equal(45f, w.Player(1).Shields.Total, 4);
            Assert.Single(w.Player(1).Shields.Entries);
        }

        [Fact]
        public void Split_Channel_Attack_Resolves_Per_Channel_And_Rejoins_As_One_Hit()
        {
            DuelWorld w = TestWorlds.Duel();
            w.Player(1).Stats.Add(StatKind.Resistance, 0.2f, "TEST", DamageChannel.Thermal);
            DamageRequest split = DamageRequest.Split(0, 1, DamageKind.Direct, "SPL-TEST",
                new DamagePart(DamageChannel.Physical, 30f), new DamagePart(DamageChannel.Thermal, 30f)).From(w.Player(0).Position);
            int before = w.EventLog.Count;
            DamageEvent e = w.ApplyDamage(split);
            Assert.Equal(54f, e.Resolution.HealthLost, 3);
            Assert.Equal(30f, e.Resolution.Channels[0].Mitigated, 3);
            Assert.Equal(24f, e.Resolution.Channels[1].Mitigated, 3);
            int damageEvents = 0;
            for (int i = before; i < w.EventLog.Count; i++)
            {
                damageEvents += w.EventLog[i] is DamageEvent ? 1 : 0;
            }

            Assert.Equal(1, damageEvents);
            Assert.Equal(e.Id, e.OriginEventId);
        }

        [Fact]
        public void Guard_Reduces_Associated_Knockback_By_The_Same_Fraction()
        {
            DuelWorld w = TestWorlds.Duel();
            TestWorlds.StartGuard(w);
            TestWorlds.Run(w, GuardState.PerfectTicks, TestWorlds.A, TestWorlds.B.With(InputButtons.Guard));
            DamageRequest hit = TestWorlds.Hit(w, 0, 1, 20f);
            hit.Knockback = 3f;
            Assert.Equal(1.5f, w.ApplyDamage(hit).Resolution.Knockback, 4);
            Assert.True(w.Player(1).Motor.IsDisplaced);
        }

        [Fact]
        public void Guard_Never_Grants_Full_Immunity()
        {
            Assert.True(GlobalRules.Guard.PerfectDamageReduction < 1f);
            DuelWorld w = TestWorlds.Duel();
            TestWorlds.StartGuard(w);
            Assert.True(w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 10f)).Resolution.HealthLost > 0f);
        }

        [Fact]
        public void Boundary_And_Recall_Damage_Bypass_Guard_Resistance_And_Shields()
        {
            DuelWorld w = TestWorlds.Duel();
            w.Player(1).Stats.Add(StatKind.Resistance, 0.35f, "TEST", DamageChannel.Physical);
            w.Player(1).Stats.Add(StatKind.PassiveReduction, 0.3f, "TEST");
            w.GrantShield(1, "AUG-RESERVE", 100f, 0, reserve: true);
            TestWorlds.StartGuard(w);
            foreach (DamageKind kind in new[] { DamageKind.Boundary, DamageKind.Recall })
            {
                DamageRequest rule = DamageRequest.Single(-1, 1, kind, DamageSources.Boundary, DamageChannel.Physical, 15f).From(w.Player(0).Position);
                rule.GuardEligible = true;
                DamageResolution r = w.ApplyDamage(rule).Resolution;
                Assert.Equal(GuardOutcome.None, r.Guard);
                Assert.Equal(15f, r.HealthLost, 4);
                Assert.Equal(0f, r.ShieldAbsorbed);
            }

            Assert.Equal(100f, w.Player(1).Shields.Total, 4);
            Assert.Equal(270f, w.Player(1).Resources.Health, 4);
        }

        [Fact]
        public void Secondary_Damage_Beyond_One_Generation_Is_Refused()
        {
            DuelWorld w = TestWorlds.Duel();
            DamageRequest proc = TestWorlds.Hit(w, 0, 1, 10f, DamageKind.Secondary);
            proc.Generation = 1;
            Assert.NotNull(w.ApplyDamage(proc));
            Assert.False(proc.CanTriggerEffects);
            proc.Generation = 2;
            Assert.Null(w.ApplyDamage(proc));
        }

        [Fact]
        public void Wet_Adds_No_Damage_Multiplier()
        {
            DuelWorld w = TestWorlds.Duel();
            w.ApplyStatus(1, StatusApplication.Of(StatusKind.Wet, "SPL-RAIN", 0, 300));
            Assert.True(w.Player(1).Statuses.IsWet);
            Assert.Equal(40f, w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 40f)).Resolution.HealthLost, 4);
            Assert.Equal(1f, GlobalRules.Statuses.WetDamageMultiplier);
        }

        [Fact]
        public void Health_Fraction_Excludes_Shields()
        {
            DuelWorld w = TestWorlds.Duel();
            w.GrantShield(1, "AUG-RESERVE", 50f, 0, reserve: true);
            w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 80f));
            Assert.Equal(270f / 300f, w.Player(1).Resources.HealthFraction, 4);
        }
    }
}
