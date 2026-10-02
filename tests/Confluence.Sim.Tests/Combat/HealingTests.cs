using Confluence.Core.Rules;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Players;

namespace Confluence.Sim.Tests.Combat
{
    /// <summary>Healing and lifesteal budgets (spec L114).</summary>
    public class HealingTests
    {
        private static DuelWorld Wounded(float damage = 200f)
        {
            DuelWorld w = TestWorlds.Duel();
            w.ApplyDamage(DamageRequest.Single(-1, 0, DamageKind.Environment, "TEST", DamageChannel.Physical, damage));
            return w;
        }

        [Fact]
        public void Direct_Healing_Caps_At_35_Per_Second_And_120_Per_Duel()
        {
            DuelWorld w = Wounded();
            Assert.Equal(35f, w.Heal(0, 50f, "SPL-MEND"), 4);
            Assert.Equal(0f, w.Heal(0, 10f, "SPL-MEND"), 4);
            TestWorlds.Run(w, 30, TestWorlds.A, TestWorlds.B);
            Assert.Equal(0f, w.Heal(0, 10f, "SPL-MEND"), 4); // still inside the rolling second

            float total = 35f;
            for (int second = 0; second < 5; second++)
            {
                TestWorlds.Run(w, 60, TestWorlds.A, TestWorlds.B);
                total += w.Heal(0, 50f, "SPL-MEND");
            }

            Assert.Equal(GlobalRules.Healing.MaxPerDuel, total, 3);
            Assert.Equal(0f, w.Player(0).Healing.RemainingDuel, 3);
            Assert.Equal(220f, w.Player(0).Resources.Health, 3);
            HealEvent last = null;
            foreach (DuelEvent e in w.EventLog)
            {
                last = e as HealEvent ?? last;
            }

            Assert.Equal(0f, last.BudgetRemaining, 3); // the remaining budget is visible
        }

        [Fact]
        public void Healing_At_Full_Health_Spends_No_Budget()
        {
            DuelWorld w = TestWorlds.Duel();
            Assert.Equal(0f, w.Heal(0, 30f, "SPL-MEND"));
            Assert.Equal(GlobalRules.Healing.MaxPerDuel, w.Player(0).Healing.RemainingDuel);
        }

        [Fact]
        public void Lifesteal_Is_Capped_At_10_Percent_And_20_Per_Second()
        {
            DuelWorld w = Wounded();
            w.Player(0).Stats.Add(StatKind.Lifesteal, 0.25f, "EQP-TEST");
            Assert.Equal(0.10f, w.Player(0).Stats.Lifesteal, 5);

            w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 100f));
            Assert.Equal(110f, w.Player(0).Resources.Health, 3);
            w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 150f, DamageKind.Melee));
            Assert.Equal(120f, w.Player(0).Resources.Health, 3); // 15 asked, 10 left in the 20/s lifesteal window
            Assert.Equal(20f, w.Player(0).Healing.LifestealInLastSecond, 3);
        }

        [Fact]
        public void Lifesteal_Comes_Only_From_Direct_Damage_To_The_Opponent()
        {
            DuelWorld w = Wounded();
            w.Player(0).Stats.Add(StatKind.Lifesteal, 0.1f, "EQP-TEST");
            w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 50f, DamageKind.Splash));
            w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 50f, DamageKind.Status));
            w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 50f, DamageKind.Environment));
            DamageRequest proc = TestWorlds.Hit(w, 0, 1, 50f, DamageKind.Secondary);
            proc.Generation = 1;
            w.ApplyDamage(proc);
            w.ApplyDamage(TestWorlds.Hit(w, 0, 0, 20f)); // self-damage never heals
            Assert.Equal(80f, w.Player(0).Resources.Health, 3);
            Assert.Equal(GlobalRules.Healing.MaxPerDuel, w.Player(0).Healing.RemainingDuel, 3);
        }

        [Fact]
        public void Lifesteal_Shares_The_Duel_Budget()
        {
            DuelWorld w = Wounded();
            w.Player(0).Stats.Add(StatKind.Lifesteal, 0.1f, "EQP-TEST");
            for (int i = 0; i < 4; i++)
            {
                w.Heal(0, 35f, "SPL-MEND");
                TestWorlds.Run(w, 60, TestWorlds.A, TestWorlds.B);
            }

            Assert.Equal(0f, w.Player(0).Healing.RemainingDuel, 3);
            float before = w.Player(0).Resources.Health;
            w.ApplyDamage(TestWorlds.Hit(w, 0, 1, 100f));
            Assert.Equal(before, w.Player(0).Resources.Health, 3);
        }
    }
}
