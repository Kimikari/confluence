using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Sim.Combat;

namespace Confluence.Sim.Tests.Combat
{
    /// <summary>Trigger budgets and chain targeting (spec L126).</summary>
    public class ProcBudgetTests
    {
        [Fact]
        public void An_Effect_Triggers_Once_Per_Original_Hit_Event()
        {
            var budget = new ProcBudget();
            Assert.True(budget.TryTrigger(0, "EQP-TEST", 10, 1, 0));
            Assert.False(budget.TryTrigger(0, "EQP-TEST", 10, 1, 100)); // the same hit event, much later
            Assert.True(budget.TryTrigger(0, "EQP-TEST", 11, 1, 100));
        }

        [Fact]
        public void An_Effect_Triggers_At_Most_Once_Per_Half_Second()
        {
            var budget = new ProcBudget();
            Assert.True(budget.TryTrigger(0, "EQP-TEST", 1, 1, 0));
            Assert.False(budget.TryTrigger(0, "EQP-TEST", 2, 1, 29));
            Assert.True(budget.TryTrigger(0, "EQP-TEST", 3, 1, 30));
            Assert.True(budget.TryTrigger(0, "AUG-OTHER", 3, 1, 30)); // a different effect has its own budget
            Assert.True(budget.TryTrigger(1, "EQP-TEST", 3, 0, 30));  // and so does the other player
            Assert.Equal(2, budget.TriggerCount(0, "EQP-TEST"));
        }

        [Fact]
        public void Beam_Ticks_Sharing_One_Origin_Never_Cascade()
        {
            var budget = new ProcBudget();
            int triggers = 0;
            for (int tick = 0; tick < 180; tick += 6) // a 3 s beam ticking ten times a second
            {
                DamageRequest beamTick = DamageRequest.Single(0, 1, DamageKind.Direct, "SPL-BEAM", DamageChannel.Thermal, 4f);
                beamTick.OriginEventId = 77;
                triggers += budget.TryTrigger(0, "EQP-TEST", beamTick, beamTick.OriginEventId, tick) ? 1 : 0;
            }

            Assert.Equal(1, triggers);
        }

        [Fact]
        public void Secondary_And_Rule_Damage_Cannot_Trigger()
        {
            var budget = new ProcBudget();
            DamageRequest proc = DamageRequest.Single(0, 1, DamageKind.Secondary, "EQP-TEST", DamageChannel.Physical, 5f);
            proc.Generation = 1;
            Assert.False(budget.TryTrigger(0, "AUG-X", proc, 5, 0));
            Assert.False(budget.TryTrigger(0, "AUG-X", DamageRequest.Single(-1, 1, DamageKind.Boundary, DamageSources.Boundary, DamageChannel.None, 5f), 6, 0));
            Assert.False(budget.TryTrigger(0, "AUG-X", DamageRequest.Single(0, 1, DamageKind.Environment, "SPL-X", DamageChannel.Electrical, 5f), 7, 0));
            Assert.True(budget.TryTrigger(0, "AUG-X", DamageRequest.Single(0, 1, DamageKind.Melee, "EQP-031", DamageChannel.Physical, 5f), 8, 0));
        }

        [Fact]
        public void Chains_Jump_At_Most_Three_Times_Up_To_6_Meters_And_Never_Repeat_A_Target()
        {
            var candidates = new List<ChainCandidate>
            {
                new ChainCandidate { Id = 1, Position = new Vec3(5f, 0f, 0f), IsOpponent = true },
                new ChainCandidate { Id = 2, Position = new Vec3(10f, 0f, 0f) },
                new ChainCandidate { Id = 3, Position = new Vec3(15f, 0f, 0f) },
                new ChainCandidate { Id = 4, Position = new Vec3(20f, 0f, 0f) },
            };
            List<ChainCandidate> path = ChainTargeting.Select(Vec3.Zero, candidates, maxJumps: 10, maxJumpDistance: 20f);
            Assert.Equal(GlobalRules.Triggers.ChainMaxJumps, path.Count);
            Assert.Equal(new[] { 1, 2, 3 }, path.ConvertAll(c => c.Id).ToArray());

            var gap = new List<ChainCandidate>
            {
                new ChainCandidate { Id = 1, Position = new Vec3(5f, 0f, 0f), IsOpponent = true },
                new ChainCandidate { Id = 2, Position = new Vec3(11.5f, 0f, 0f) }, // 6.5 m past the opponent
            };
            Assert.Single(ChainTargeting.Select(Vec3.Zero, gap));

            // The opponent the cast already hit is never chosen again.
            Assert.Empty(ChainTargeting.Select(Vec3.Zero, new List<ChainCandidate> { gap[0] }, alreadyHit: new[] { 1 }));
        }
    }
}
