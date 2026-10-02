using Confluence.Core.Rules;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Players;

namespace Confluence.Sim.Tests.Players
{
    /// <summary>Health, mana, and stamina rules (spec L78).</summary>
    public class ResourcePoolsTests
    {
        private static ResourcePools Fresh()
        {
            var pools = new ResourcePools();
            pools.RestoreForDuel(GlobalRules.Resources.BaseHealth);
            return pools;
        }

        private static void Regen(ResourcePools pools, int fromTick, int toTick, float manaRate = GlobalRules.Resources.ManaRegenPerSecond)
        {
            for (int t = fromTick; t <= toTick; t++)
            {
                pools.Regenerate(t, manaRate);
            }
        }

        [Fact]
        public void Duel_Starts_With_300_Health_100_Mana_100_Stamina()
        {
            ResourcePools p = Fresh();
            Assert.Equal(300f, p.Health);
            Assert.Equal(100f, p.Mana);
            Assert.Equal(100f, p.Stamina);
        }

        [Fact]
        public void Mana_Regenerates_10_Per_Second_After_1_5_Seconds_Without_Spending()
        {
            ResourcePools p = Fresh();
            Assert.True(p.TrySpendMana(40f, 0, true));
            Regen(p, 1, 89);
            Assert.Equal(60f, p.Mana, 4);
            Regen(p, 90, 149); // 60 ticks of regeneration
            Assert.Equal(70f, p.Mana, 3);
        }

        [Fact]
        public void Mana_Regeneration_Caps_At_14_Per_Second_After_Modifiers()
        {
            ResourcePools p = Fresh();
            p.TrySpendMana(50f, 0, true);
            Regen(p, 90, 149, 25f);
            Assert.Equal(64f, p.Mana, 3);

            var stats = new StatSheet();
            stats.Add(StatKind.ManaRegenBonus, 9f, "AUG-TEST");
            Assert.Equal(14f, stats.ManaRegenPerSecond, 4);
        }

        [Fact]
        public void Channels_Count_As_Spending()
        {
            ResourcePools p = Fresh();
            p.TrySpendMana(30f, 0, true);
            for (int t = 1; t <= 300; t++)
            {
                p.MarkChanneling(t);
                p.Regenerate(t, 10f);
            }

            Assert.Equal(70f, p.Mana, 4);
        }

        [Fact]
        public void Stamina_Regenerates_20_Per_Second_After_1_Second()
        {
            ResourcePools p = Fresh();
            Assert.True(p.TrySpendStamina(50f, 0));
            Regen(p, 1, 59);
            Assert.Equal(50f, p.Stamina, 4);
            Regen(p, 60, 119);
            Assert.Equal(70f, p.Stamina, 3);
        }

        [Fact]
        public void Nothing_Is_Spent_Without_The_Full_Initial_Cost()
        {
            ResourcePools p = Fresh();
            p.TrySpendMana(75f, 0, true);
            Assert.False(p.TrySpendMana(30f, 1, true));
            Assert.Equal(25f, p.Mana, 4);
            Assert.Equal(0, p.LastManaSpendTick); // a refused payment does not reset the regeneration delay
            p.TrySpendStamina(80f, 0);
            Assert.False(p.TrySpendStamina(25f, 1));
            Assert.Equal(20f, p.Stamina, 4);
        }

        [Fact]
        public void There_Is_No_Passive_Health_Regeneration()
        {
            ResourcePools p = Fresh();
            p.LoseHealth(100f);
            Regen(p, 0, 600);
            Assert.Equal(200f, p.Health, 4);
        }

        [Fact]
        public void Maximum_Health_Caps_At_450()
        {
            DuelPlayerSetup setup = TestWorlds.Setup("A");
            setup.Modifiers.Add(new StatModifier(StatKind.MaxHealthBonus, 250f, "AUG-TEST"));
            DuelWorld w = TestWorlds.Duel(a: setup);
            Assert.Equal(450f, w.Player(0).Resources.MaxHealth);
            Assert.Equal(450f, w.Player(0).Resources.Health);
            Assert.Equal(300f, w.Player(1).Resources.MaxHealth);
        }

        [Fact]
        public void Sprinting_In_The_World_Drains_Stamina_And_Delays_Regeneration()
        {
            DuelWorld w = TestWorlds.Duel(TestWorlds.Flat(boundaryRadius: 30f, floorHalf: 40f));
            PlayerInput sprint = TestWorlds.A.Moving(0f, 1f).With(InputButtons.Sprint);
            TestWorlds.RunSeconds(w, 1f, sprint, TestWorlds.B);
            Assert.Equal(88f, w.Player(0).Resources.Stamina, 2);
            TestWorlds.RunSeconds(w, 1f, TestWorlds.A, TestWorlds.B); // the 1 s delay (regeneration resumes on its last tick)
            Assert.InRange(w.Player(0).Resources.Stamina, 88f, 88.34f);
            TestWorlds.RunSeconds(w, 0.5f, TestWorlds.A, TestWorlds.B);
            Assert.Equal(98.33f, w.Player(0).Resources.Stamina, 1); // 31 regeneration ticks at 20/s
        }
    }
}
