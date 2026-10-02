using Confluence.Casting;

namespace Confluence.Casting.Tests.Mastery
{
    public class MasteryClampTests
    {
        [Fact]
        public void Rewards_Clamp_To_Their_Category()
        {
            // Spec L124, L478: efficiency up to 20%, one attribute up to 15%.
            Assert.Equal(0.20f, MasteryResult.Mastered(MasteryReward.ManaEfficiency, 0.35f).Magnitude);
            Assert.Equal(0.20f, MasteryResult.Mastered(MasteryReward.StaminaEfficiency, 0.25f).Magnitude);
            Assert.Equal(0.15f, MasteryResult.Mastered(MasteryReward.DirectDamage, 0.30f).Magnitude);
            Assert.Equal(0.15f, MasteryResult.Mastered(MasteryReward.ProjectileSpeed, 0.16f).Magnitude);
            Assert.Equal(0.15f, MasteryResult.Mastered(MasteryReward.WindupReduction, 0.50f).Magnitude);
            Assert.Equal(0.12f, MasteryResult.Mastered(MasteryReward.DirectDamage, 0.12f).Magnitude);
        }

        [Fact]
        public void Linked_Effects_Clamp_Together_With_The_Reward_In_Its_Category()
        {
            var eff = MasteryResult.Mastered(MasteryReward.ManaEfficiency, 0.15f);
            Assert.Equal(0.20f, MasteryClamp.Combined(eff, 0.10f));
            var dmg = MasteryResult.Mastered(MasteryReward.DirectDamage, 0.12f);
            Assert.Equal(0.15f, MasteryClamp.Combined(dmg, 0.05f));
            Assert.Equal(0f, MasteryClamp.Combined(MasteryResult.Normal(MissReason.LateRelease), 0.10f));

            // Linked efficiency never applies to a damage-rewarded cast: never both (spec L124).
            Assert.Equal(0f, MasteryClamp.ManaEfficiency(dmg, new CastModifiers { MasteryLinkedEfficiency = 0.10f }));
            Assert.Equal(0.20f, MasteryClamp.ManaEfficiency(eff, new CastModifiers { MasteryLinkedEfficiency = 0.10f }));
        }

        [Fact]
        public void Paid_Cost_Never_Falls_Below_70_Percent()
        {
            Assert.Equal(17f, MasteryClamp.PaidCost(20f, 0f, 0.15f), 4);
            Assert.Equal(14f, MasteryClamp.PaidCost(20f, 0.25f, 0.20f), 4);
            Assert.Equal(14f, MasteryClamp.PaidCost(20f, 0.90f, 0f), 4);
            Assert.Equal(24f, MasteryClamp.PaidCost(20f, -0.20f, 0f), 4); // surcharges are not floored
            Assert.Equal(0f, MasteryClamp.PaidCost(0f, 0.1f, 0.1f));
        }

        [Fact]
        public void Refunds_Cap_At_A_Quarter_Of_The_Payment()
        {
            Assert.Equal(5f, MasteryClamp.MaxRefund(20f));
            Assert.Equal(0f, MasteryClamp.MaxRefund(-3f));
        }

        [Fact]
        public void Windup_Reduction_Rounds_Down_Respects_Caps_And_Never_Removes_The_Windup()
        {
            Assert.Equal(19, MasteryClamp.ReducedWindupTicks(21, 0.12f, 0)); // 2.52 -> 2 ticks
            Assert.Equal(36, MasteryClamp.ReducedWindupTicks(40, 0.10f, 4)); // WIZ-005 0.06 s cap
            Assert.Equal(47, MasteryClamp.ReducedWindupTicks(51, 0.10f, 4)); // 5.1 ticks capped at 4
            Assert.Equal(34, MasteryClamp.ReducedWindupTicks(40, 0.50f, 0)); // clamps to 15%
            Assert.Equal(1, MasteryClamp.ReducedWindupTicks(1, 0.15f, 0));
            Assert.Equal(0, MasteryClamp.ReducedWindupTicks(0, 0.15f, 0));    // instant stays instant
        }

        [Fact]
        public void Results_State_Exactly_Why_Mastery_Was_Missed()
        {
            Assert.Throws<System.ArgumentException>(() => MasteryResult.Normal(MissReason.None));
            Assert.Throws<System.ArgumentException>(() => MasteryResult.Mastered(MasteryReward.None, 0.1f));
            Assert.Equal("guided cast: no mastery reward", MasteryResult.Guided.Describe());
            Assert.Equal("ManaEfficiency +15%", MasteryResult.Mastered(MasteryReward.ManaEfficiency, 0.15f).Describe());
            foreach (MissReason reason in System.Enum.GetValues(typeof(MissReason)))
            {
                string text = MissReasons.Describe(reason);
                Assert.False(string.IsNullOrWhiteSpace(text));
                Assert.DoesNotContain("bad cast", text); // spec L500
            }
        }
    }
}
