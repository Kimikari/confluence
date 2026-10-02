using System.Collections.Generic;
using Confluence.Core.Rules;
using Confluence.Sim.Players;

namespace Confluence.Sim.Tests.Players
{
    /// <summary>Global modifier caps and the raw/effective inspector (spec L124, L355).</summary>
    public class StatSheetTests
    {
        [Fact]
        public void Movement_Bonus_Caps_At_20_Percent()
        {
            var s = new StatSheet();
            s.Add(StatKind.MovementBonus, 0.15f, "EQP-BOOTS");
            s.Add(StatKind.MovementBonus, 0.15f, "AUG-SWIFT");
            Assert.Equal(0.30f, s.Raw(StatKind.MovementBonus), 5);
            Assert.Equal(1.20f, s.MovementMultiplier, 5);
        }

        [Fact]
        public void Cooldown_Reduction_Caps_At_25_Percent_With_A_Half_Second_Floor()
        {
            var s = new StatSheet();
            s.Add(StatKind.CooldownReduction, 0.4f, "AUG-HASTE");
            Assert.Equal(0.25f, s.CooldownReduction, 5);
            Assert.Equal(6f, s.EffectiveCooldownSec(8f), 4);
            Assert.Equal(0.5f, s.EffectiveCooldownSec(0.6f), 4);
            Assert.Equal(0.4f, s.EffectiveCooldownSec(0.4f), 4); // a base already below the floor is not raised
        }

        [Fact]
        public void At_Least_70_Percent_Of_A_Cost_Is_Paid_And_Refunds_Cap_At_25_Percent()
        {
            var s = new StatSheet();
            s.Add(StatKind.CostReduction, 0.2f, "EQP-A");
            s.Add(StatKind.CostReduction, 0.2f, "AUG-B");
            Assert.Equal(28f, s.EffectiveCost(40f), 4);
            Assert.Equal(7f, s.CapRefund(28f, 20f), 4);
            Assert.Equal(3f, s.CapRefund(28f, 3f), 4);
        }

        [Fact]
        public void Dash_And_Teleport_Limits()
        {
            var s = new StatSheet();
            s.Add(StatKind.DashCharges, 3f, "AUG-A");
            s.Add(StatKind.DashDistance, 4f, "AUG-B");
            s.Add(StatKind.TeleportDistance, 6f, "AUG-C");
            Assert.Equal(GlobalRules.Modifiers.MaxDashCharges, s.DashCharges);
            Assert.Equal(8f, s.EffectiveDashDistance(6f), 4);
            Assert.Equal(10f, s.EffectiveTeleportDistance(8f), 4);
        }

        [Fact]
        public void Inspector_Shows_Raw_And_Effective_Values()
        {
            var s = new StatSheet();
            s.Add(StatKind.MovementBonus, 0.3f, "EQP-BOOTS");
            s.Add(StatKind.Lifesteal, 0.05f, "EQP-FANG");
            s.Add(StatKind.Resistance, 0.5f, "EQP-CLOAK", DamageChannel.Thermal);
            List<StatLine> lines = s.Inspect();
            StatLine move = lines.Find(l => l.Stat == StatKind.MovementBonus);
            Assert.Equal(0.3f, move.Raw, 5);
            Assert.Equal(0.2f, move.Effective, 5);
            Assert.True(move.Capped);
            StatLine steal = lines.Find(l => l.Stat == StatKind.Lifesteal);
            Assert.False(steal.Capped);
            StatLine resist = lines.Find(l => l.Stat == StatKind.Resistance && l.Channel == DamageChannel.Thermal);
            Assert.Equal(0.35f, resist.Effective, 5);
            Assert.True(resist.Capped);
        }

        [Fact]
        public void Removing_A_Source_Removes_Its_Modifiers()
        {
            var s = new StatSheet();
            s.Add(StatKind.MovementBonus, 0.1f, "EQP-BOOTS");
            s.Add(StatKind.Lifesteal, 0.05f, "EQP-BOOTS");
            s.Add(StatKind.Lifesteal, 0.02f, "AUG-OTHER");
            Assert.Equal(2, s.RemoveSource("EQP-BOOTS"));
            Assert.Equal(0f, s.MovementBonus);
            Assert.Equal(0.02f, s.Lifesteal, 5);
        }
    }
}
