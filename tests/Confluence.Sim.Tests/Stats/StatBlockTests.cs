using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Players;
using Confluence.Sim.Stats;
using Confluence.Sim.Tests.World;

namespace Confluence.Sim.Tests.Stats
{
    /// <summary>
    /// Every capped quantity keeps its raw and effective value (spec L78-L124, L355), and the motor, resources,
    /// damage pipeline, guard, melee, dodge, jump, and sprint read their parameters from the stat block.
    /// </summary>
    public class StatBlockTests
    {
        private static StatLine Line(StatBlock s, StatKind kind, DamageChannel channel = DamageChannel.None) =>
            s.InspectAll().Single(l => l.Stat == kind && l.Channel == channel);

        [Fact]
        public void Every_Global_Cap_Binds_Its_Effective_Value_And_The_Inspector_Shows_Raw_And_Effective()
        {
            var s = new StatBlock();
            s.Add(StatKind.MaxHealthBonus, 200f, "EQP-A");                       // 500 raw, 450 cap (L114)
            s.Add(StatKind.Resistance, 0.5f, "EQP-B", DamageChannel.Physical);   // 35% cap (L112)
            s.Add(StatKind.Resistance, -0.3f, "AUG-C", DamageChannel.Thermal);   // -15% floor
            s.Add(StatKind.PassiveReduction, 0.3f, "AUG-D");                     // with 35% resistance: 45% total cap
            s.Add(StatKind.MovementBonus, 0.3f, "EQP-E");                        // +20% cap (L124)
            s.Add(StatKind.CooldownReduction, 0.4f, "AUG-F");                    // 25% cap
            s.Add(StatKind.CostReduction, 0.5f, "AUG-G");                        // 30% reduction cap
            s.Add(StatKind.RefundFraction, 0.5f, "AUG-H");                       // 25% cap
            s.Add(StatKind.ManaRegenPercent, 0.6f, "EQP-I");                     // 16/s raw, 14/s cap (L78)
            s.Add(StatKind.DashCharges, 3f, "EQP-J");                            // 2 charges
            s.Add(StatKind.DashDistance, 6f, "EQP-K");                           // 8 m dash cap
            s.Add(StatKind.TeleportDistance, 9f, "EQP-L");                       // 10 m teleport cap
            s.Add(StatKind.Lifesteal, 0.3f, "AUG-M");                            // 10% cap

            Assert.Equal(450f, s.MaxHealth, 3);
            Assert.Equal(0.35f, s.Resistance(DamageChannel.Physical), 4);
            Assert.Equal(-0.15f, s.Resistance(DamageChannel.Thermal), 4);
            Assert.Equal(1f - GlobalRules.Damage.PassiveReductionCap, s.PassiveMultiplier(DamageChannel.Physical), 4);
            Assert.Equal(0.7f * 1.15f, s.PassiveMultiplier(DamageChannel.Thermal), 4); // vulnerable, but the passive still applies
            Assert.Equal(1.2f, s.MovementMultiplier, 4);
            Assert.Equal(6f, s.EffectiveCooldownSec(8f), 4);
            Assert.Equal(0.5f, s.EffectiveCooldownSec(0.6f), 4); // no recharge below 0.5 s
            Assert.Equal(14f, s.EffectiveCost(20f), 4);
            Assert.Equal(5f, s.CapRefund(20f, 12f), 4);
            Assert.Equal(14f, s.ManaRegenPerSecond, 4);
            Assert.Equal(2, s.DashCharges);
            Assert.Equal(8f, s.EffectiveDashDistance(4f), 4);
            Assert.Equal(10f, s.EffectiveTeleportDistance(6f), 4);
            Assert.Equal(0.1f, s.Lifesteal, 4);

            StatLine health = Line(s, StatKind.MaxHealthBonus);
            Assert.Equal(500f, health.Raw, 3);
            Assert.Equal(450f, health.Effective, 3);
            Assert.Equal(450f, health.Cap, 3);
            Assert.True(health.Capped);
            StatLine regen = Line(s, StatKind.ManaRegenPercent);
            Assert.Equal(16f, regen.Raw, 3);
            Assert.Equal(14f, regen.Effective, 3);
            Assert.True(regen.Capped);
            Assert.True(Line(s, StatKind.Resistance, DamageChannel.Thermal).Capped); // the -15% floor binds
            Assert.True(Line(s, StatKind.PassiveReduction, DamageChannel.Physical).Capped);
            Assert.True(Line(s, StatKind.Lifesteal).Capped);
            Assert.False(Line(s, StatKind.MaxStaminaBonus).Capped);
            Assert.Contains(s.Inspect(), l => l.Stat == StatKind.DashCharges); // compact form lists modified stats only
            Assert.DoesNotContain(s.Inspect(), l => l.Stat == StatKind.GuardStaminaCost);
        }

        [Fact]
        public void Gait_Bonuses_Share_The_Twenty_Percent_Cap_And_Slows_Their_Thirty_Percent_Cap()
        {
            var s = new StatBlock();
            s.Add(StatKind.MovementBonus, 0.1f, "EQP-A");
            s.Add(StatKind.SprintSpeedBonus, 0.15f, "EQP-081");
            s.Add(StatKind.WalkSpeedBonus, -0.02f, "EQP-082");
            Assert.Equal(0.2f, s.SprintBonus, 4);
            Assert.Equal(0.08f, s.WalkBonus, 4);
            Assert.Equal(GlobalRules.Control.SlowCap, StatBlock.EffectiveSlow(0.5f, 0.1f), 4);
            Assert.Equal(0.2f, StatBlock.EffectiveSlow(0.1f, 0.2f), 4); // only the strongest slow applies (L116)
        }

        [Fact]
        public void Timed_Modifiers_Expire_And_Sources_Remove_Together()
        {
            var s = new StatBlock();
            s.Add(new StatModifier(StatKind.MaxManaBonus, 10f, "AUG-A", DamageChannel.None, 100));
            s.Add(StatKind.MaxManaBonus, 5f, "AUG-B");
            Assert.Equal(115f, s.MaxMana, 3);
            int version = s.Version;
            Assert.Equal(0, s.Expire(99));
            Assert.Equal(1, s.Expire(100));
            Assert.Equal(105f, s.MaxMana, 3);
            Assert.True(s.Version > version);
            Assert.Equal(1, s.RemoveSource("AUG-B"));
            Assert.Equal(100f, s.MaxMana, 3);
        }

        // ----- systems read the stat block -----------------------------------------------------------------------

        [Fact]
        public void The_Motor_Reads_Gait_Speeds_And_Stamina_Costs_From_The_Stat_Block()
        {
            DuelWorld world = WorldDuels.Duel();
            DuelPlayer p = world.Player(0);
            p.Stats.Add(StatKind.SprintSpeedBonus, 0.5f, "EQP-TEST");          // capped at +20%
            p.Stats.Add(StatKind.SprintStaminaPerSecond, 2f, "EQP-TEST");      // 14/s
            WorldDuels.Step(world, 60, WorldDuels.Sprint(0f), TestWorlds.B);
            Assert.Equal(GlobalRules.Movement.SprintSpeed * 1.2f, p.Motor.Velocity.Length(), 2);
            Assert.Equal(100f - 14f, p.Resources.Stamina, 1);

            DuelWorld jumpWorld = WorldDuels.Duel();
            DuelPlayer j = jumpWorld.Player(0);
            j.Stats.Add(StatKind.JumpStaminaCost, -2f, "AUG-099");
            j.Stats.Add(StatKind.DodgeStaminaCost, 1f, "AUG-097");
            j.Stats.Add(StatKind.DodgeDistance, 10f, "EQP-TEST");              // a dodge never exceeds the 8 m dash cap
            j.Stats.Add(StatKind.DodgeCooldownSec, -5f, "EQP-TEST");           // never below 0.5 s
            WorldDuels.Idle(jumpWorld, 1);
            jumpWorld.Step(WorldDuels.Jump(0f), TestWorlds.B);
            Assert.Equal(92f, j.Resources.Stamina, 3);
            WorldDuels.Idle(jumpWorld, 50); // landed, before stamina regeneration resumes
            Vec3 before = j.Position;
            jumpWorld.Step(WorldDuels.Dodge(0f), TestWorlds.B);
            Assert.Equal(92f - 26f, j.Resources.Stamina, 3);
            Assert.Equal(Ticks.FromSeconds(GlobalRules.Modifiers.MinRechargeSec), j.Motor.DodgeCooldownTicks);
            WorldDuels.Idle(jumpWorld, Confluence.Sim.Movement.CharacterMotor.DodgeTicks - 1);
            Assert.Equal(GlobalRules.Modifiers.MaxDashDistance, j.Position.Z - before.Z, 2);
        }

        [Fact]
        public void Guard_And_Melee_Read_Their_Costs_Timing_And_Damage_From_The_Stat_Block()
        {
            DuelWorld world = WorldDuels.Duel();
            DuelPlayer guard = world.Player(1);
            guard.Stats.Add(StatKind.GuardStaminaCost, -2f, "AUG-078");
            guard.Stats.Add(StatKind.GuardCooldownSec, 0.2f, "AUG-078");
            guard.Stats.Add(StatKind.GuardMaxHoldSec, 5f, "EQP-TEST"); // the 0.8 s hold is a ceiling
            WorldDuels.Idle(world, 1);
            WorldDuels.Step(world, 55, TestWorlds.A, WorldDuels.Guard(180f));
            Assert.Equal(77f, guard.Resources.Stamina, 3); // stamina regeneration has not resumed yet (1 s delay)
            Assert.False(guard.Guard.Active); // released at 0.8 s even though held
            Assert.Equal(guard.Guard.ReleaseTick + Ticks.FromSeconds(3.2f), guard.Guard.CooldownUntil);

            DuelPlayer striker = world.Player(0);
            striker.Stats.Add(StatKind.MeleeDamage, 1f, "AUG-077");
            striker.Stats.Add(StatKind.MeleeStaminaCost, 1f, "AUG-077");
            striker.Stats.Add(StatKind.WeaponDamageBonus, 0.5f, "EQP-TEST");
            TestWorlds.Place(world, 0, new Vec3(0f, 0f, 4.8f), 0f);
            WorldDuels.Idle(world, 200);
            float health = world.Player(1).Resources.Health;
            world.Step(WorldDuels.Melee(0f), TestWorlds.B);
            Assert.Equal(100f - 9f, striker.Resources.Stamina, 3);
            WorldDuels.Idle(world, 30);
            Assert.Equal((GlobalRules.Melee.FocusDamage + 1f) * 1.5f, health - world.Player(1).Resources.Health, 3);
        }

        [Fact]
        public void Resources_Read_Maxima_Regeneration_And_Delays_From_The_Stat_Block()
        {
            DuelPlayerSetup a = TestWorlds.Setup("A");
            a.Modifiers.Add(new StatModifier(StatKind.MaxManaBonus, 20f, "EQP-TEST"));
            a.Modifiers.Add(new StatModifier(StatKind.MaxStaminaBonus, -10f, "EQP-TEST"));
            a.Modifiers.Add(new StatModifier(StatKind.ManaRegenPercent, 0.2f, "EQP-TEST"));
            a.Modifiers.Add(new StatModifier(StatKind.ManaRegenDelaySec, -0.5f, "AUG-TEST"));
            a.Modifiers.Add(new StatModifier(StatKind.StaminaRegenPercent, 0.5f, "EQP-TEST"));
            DuelWorld world = WorldDuels.Duel(a: a);
            DuelPlayer p = world.Player(0);
            Assert.Equal(120f, p.Resources.MaxMana, 3);
            Assert.Equal(120f, p.Resources.Mana, 3);
            Assert.Equal(90f, p.Resources.MaxStamina, 3);
            Assert.Equal(90f, p.Resources.Stamina, 3);

            WorldDuels.Idle(world, 1);
            Assert.True(world.TryCommitMana(0, 50f, "SPL-009"));
            WorldDuels.Idle(world, 59); // 1 s delay after modifiers, then 12/s
            Assert.Equal(70f, p.Resources.Mana, 1);
            WorldDuels.Idle(world, 60);
            Assert.Equal(82f, p.Resources.Mana, 1);
        }

        [Fact]
        public void Lifesteal_And_Shields_Keep_Their_Caps_In_The_Duel()
        {
            DuelWorld world = WorldDuels.Duel();
            world.Player(0).Stats.Add(StatKind.Lifesteal, 0.5f, "AUG-TEST"); // 10% effective
            WorldDuels.Idle(world, 1);
            world.ApplyDamage(TestWorlds.Hit(world, 1, 0, 100f));
            world.ApplyDamage(TestWorlds.Hit(world, 0, 1, 100f));
            Assert.Equal(210f, world.Player(0).Resources.Health, 3); // healed 10 (10% of 100)

            world.GrantShield(1, "SPL-TEST-A", 70f, 120);
            world.GrantShield(1, "SPL-TEST-B", 70f, 120);
            Assert.Equal(GlobalRules.Shields.Cap, world.Player(1).Shields.Total, 3); // temporary and reserve share 100
        }
    }
}
