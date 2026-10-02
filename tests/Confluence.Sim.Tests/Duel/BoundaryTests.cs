using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Movement;
using Confluence.Sim.Players;

namespace Confluence.Sim.Tests.Duel
{
    /// <summary>Boundary damage, escalation, and legal-edge rules (spec L48-L51).</summary>
    public class BoundaryTests
    {
        private static readonly Vec3 Outside = new Vec3(19f, 0f, 0f); // the default boundary radius is 18 m

        private static DuelWorld WithPlayerAt(Vec3 feet, ArenaLayout layout = null)
        {
            DuelWorld w = TestWorlds.Duel(layout);
            TestWorlds.Place(w, 0, feet, 0f);
            return w;
        }

        [Fact]
        public void Damage_Escalates_15_Then_30_Then_60_Per_Second()
        {
            DuelWorld w = WithPlayerAt(Outside);
            TestWorlds.RunSeconds(w, 2f, TestWorlds.A, TestWorlds.B);
            Assert.Equal(270f, w.Player(0).Resources.Health, 2);
            TestWorlds.RunSeconds(w, 2f, TestWorlds.A, TestWorlds.B);
            Assert.Equal(210f, w.Player(0).Resources.Health, 2);
            TestWorlds.RunSeconds(w, 1f, TestWorlds.A, TestWorlds.B);
            Assert.Equal(150f, w.Player(0).Resources.Health, 2);
            Assert.Equal(300f, w.Player(1).Resources.Health);
        }

        [Fact]
        public void One_Second_Back_Inside_Resets_The_Escalation()
        {
            DuelWorld w = WithPlayerAt(Outside);
            TestWorlds.RunSeconds(w, 3f, TestWorlds.A, TestWorlds.B); // 30 + 30
            TestWorlds.Place(w, 0, new Vec3(10f, 0f, 0f), 0f);
            TestWorlds.RunSeconds(w, 1f, TestWorlds.A, TestWorlds.B);
            Assert.Contains(w.EventLog, e => e is BoundaryEvent b && b.Change == BoundaryChange.EscalationReset);
            Assert.Equal(0, w.Player(0).Boundary.OutsideTicks);
            TestWorlds.Place(w, 0, Outside, 0f);
            float before = w.Player(0).Resources.Health;
            TestWorlds.RunSeconds(w, 1f, TestWorlds.A, TestWorlds.B);
            Assert.Equal(before - 15f, w.Player(0).Resources.Health, 2);
        }

        [Fact]
        public void Leaving_Again_Within_The_Second_Continues_The_Escalation()
        {
            DuelWorld w = WithPlayerAt(Outside);
            TestWorlds.RunSeconds(w, 3f, TestWorlds.A, TestWorlds.B);
            TestWorlds.Place(w, 0, new Vec3(10f, 0f, 0f), 0f);
            TestWorlds.RunSeconds(w, 0.5f, TestWorlds.A, TestWorlds.B);
            TestWorlds.Place(w, 0, Outside, 0f);
            float before = w.Player(0).Resources.Health;
            TestWorlds.RunSeconds(w, 0.5f, TestWorlds.A, TestWorlds.B);
            Assert.Equal(before - 15f, w.Player(0).Resources.Health, 2); // 30 per second, not 15
        }

        [Fact]
        public void Boundary_Damage_Bypasses_Mitigation_And_Triggers_Nothing()
        {
            DuelPlayerSetup setup = TestWorlds.Setup("A");
            setup.Modifiers.Add(new StatModifier(StatKind.PassiveReduction, 0.3f, "EQP-ARMOR"));
            setup.Modifiers.Add(new StatModifier(StatKind.Lifesteal, 0.1f, "EQP-FANG"));
            setup.ReserveShields.Add(new ReserveShieldGrant("AUG-WARD", 60f));
            DuelWorld w = TestWorlds.Duel(a: setup);
            TestWorlds.Place(w, 0, Outside, 0f);
            TestWorlds.RunSeconds(w, 2f, TestWorlds.A, TestWorlds.B);
            Assert.Equal(270f, w.Player(0).Resources.Health, 2);
            Assert.Equal(60f, w.Player(0).Shields.Total, 3);
            Assert.Equal(0f, w.Player(1).Feats.DamageDealt);
            Assert.Equal(0f, w.Player(0).Feats.ShieldAbsorbed);
            foreach (DuelEvent e in w.EventLog)
            {
                if (e is DamageEvent d)
                {
                    Assert.Equal(-1, d.Attacker);
                    Assert.Equal(DamageKind.Boundary, d.Kind);
                }

                Assert.False(e is HealEvent);
            }
        }

        [Fact]
        public void Knockback_Stops_At_The_Edge_With_A_5_Point_Impact()
        {
            DuelWorld w = WithPlayerAt(new Vec3(16f, 0f, 0f));
            Assert.True(w.ApplyKnockback(0, 1, new Vec3(1f, 0f, 0f), 4f, "SPL-PUSH"));
            TestWorlds.RunSeconds(w, 0.5f, TestWorlds.A, TestWorlds.B);
            Assert.InRange(w.Player(0).Position.X, 17.9f, 18.0001f);
            Assert.Equal(295f, w.Player(0).Resources.Health, 3);
            Assert.Contains(w.EventLog, e => e is MovementEvent m && m.Change == MovementChange.EdgeImpact);
            Assert.False(w.Player(0).Motor.IsDisplaced);
        }

        [Fact]
        public void Knockback_Caps_At_4_Meters()
        {
            DuelWorld w = WithPlayerAt(Vec3.Zero);
            w.ApplyKnockback(0, 1, new Vec3(0f, 0f, -1f), 9f, "SPL-PUSH");
            TestWorlds.RunSeconds(w, 1f, TestWorlds.A, TestWorlds.B);
            Assert.Equal(-4f, w.Player(0).Position.Z, 2);
        }

        [Fact]
        public void Dash_Stops_At_The_Edge_Without_Impact_Damage()
        {
            DuelWorld w = WithPlayerAt(new Vec3(14f, 0f, 0f));
            Assert.True(w.TryDash(0, new Vec3(1f, 0f, 0f), 12f, 20, "AUG-DASH"));
            TestWorlds.RunSeconds(w, 0.5f, TestWorlds.A, TestWorlds.B);
            Assert.InRange(w.Player(0).Position.X, 17.9f, 18.0001f);
            Assert.Equal(300f, w.Player(0).Resources.Health);
        }

        [Fact]
        public void Teleports_Never_End_Outside_And_Cap_At_10_Meters()
        {
            DuelWorld w = WithPlayerAt(new Vec3(15f, 0f, 0f));
            Assert.True(w.TryTeleport(0, new Vec3(24f, 0f, 0f), "SPL-BLINK"));
            Assert.True(w.LegalBoundary.Contains(w.Player(0).Position));
            Assert.True(w.Player(0).Position.X <= 18f - CharacterMotor.Radius + 1e-3f);

            DuelWorld v = WithPlayerAt(new Vec3(0f, 0f, -6f));
            Assert.True(v.TryTeleport(0, new Vec3(0f, 0f, 12f), "SPL-BLINK"));
            Assert.Equal(4f, v.Player(0).Position.Z, 3);
        }

        [Fact]
        public void Returning_Inside_The_Closing_Boundary_After_Damage_Counts_For_The_Feat()
        {
            ArenaLayout layout = TestWorlds.Flat();
            layout.Boundary.ContractStartSec = 0.5f;
            layout.Boundary.ContractEndSec = 60f;
            DuelWorld w = WithPlayerAt(Vec3.Zero, layout);
            TestWorlds.RunSeconds(w, 1f, TestWorlds.A, TestWorlds.B);
            for (int i = 0; i < 2; i++)
            {
                TestWorlds.Place(w, 0, Outside, 0f);
                TestWorlds.RunSeconds(w, 0.5f, TestWorlds.A, TestWorlds.B);
                TestWorlds.Place(w, 0, Vec3.Zero, 0f);
                TestWorlds.RunSeconds(w, 1.1f, TestWorlds.A, TestWorlds.B);
            }

            Assert.Equal(2, w.Player(0).Feats.BoundaryReturns);
            Assert.True(w.Player(0).Feats.IsMet(Feats.Get("FEAT-024"), false, 1f, 0f));
        }
    }
}
