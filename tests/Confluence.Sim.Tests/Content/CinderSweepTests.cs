using System.Linq;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions.Grid;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;
using Confluence.Sim.World;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Content
{
    /// <summary>
    /// SPL-010 Cinder Sweep cast through WIZ-002's grammar from real input: "90-degree cone; check cover per target;
    /// caster slows 15% during windup", damage "36", "Heat 1 along the cone", 4 m range.
    /// </summary>
    public class CinderSweepTests
    {
        private static readonly PlayerInput Sweep = TestWorlds.A.Selecting(1);

        private static DuelWorld Duel(Vec3 target, ArenaLayout layout = null, IWorldMaterials materials = null)
        {
            DuelWorld w = ContentTestKit.BramVersusDummy(layout, materials);
            TestWorlds.Place(w, 1, target, 180f);
            w.Step(Sweep, TestWorlds.B); // select slot 1: no swap windup (spec L106)
            return w;
        }

        private static float SweepAt(Vec3 target, ArenaLayout layout = null, int hold = 15)
        {
            DuelWorld w = Duel(target, layout);
            ContentTestKit.HoldAndRelease(w, hold, Sweep);
            SpellTestKit.Run(w, 20, Sweep);
            return SpellTestKit.TotalDealt(w, 1, "SPL-010");
        }

        [Fact]
        [Verifies("SPL-010")]
        public void It_Deals_36_Thermal_Damage_Inside_Its_90_Degree_4_Meter_Cone()
        {
            DuelWorld w = Duel(new Vec3(0f, 0f, -3f));
            ContentTestKit.HoldAndRelease(w, 15, Sweep);
            Assert.Equal("SPL-010", ContentTestKit.LastCommit(w).SpellId);
            SpellTestKit.Run(w, 20, Sweep);
            DamageEvent hit = SpellTestKit.DamageTo(w, 1, "SPL-010").Single();
            Assert.Equal(36f, hit.Dealt, 3);
            Assert.Equal(DamageChannel.Thermal, hit.Resolution.Channels.Single().Channel);

            Assert.Equal(36f, SweepAt(new Vec3(2.3f, 0f, -4.07f)), 3);  // 50 degrees to the body center, its edge inside 45
            Assert.Equal(0f, SweepAt(new Vec3(3f, 0f, -4.5f)));         // ~63 degrees: outside the cone
            Assert.Equal(0f, SweepAt(new Vec3(0f, 0f, -1.4f)));         // beyond the 4 m reach
        }

        [Fact]
        [Verifies("SPL-010")]
        public void Cover_Is_Checked_For_Each_Target()
        {
            ArenaLayout wall = SpellTestKit.FlatWith(SpellTestKit.Wall(120, new Vec3(0f, 0f, -4.5f), new Vec3(1.2f, 2.5f, 0.2f)));
            Assert.Equal(0f, SweepAt(new Vec3(0f, 0f, -3f), wall));
            Assert.Equal(36f, SweepAt(new Vec3(1.6f, 0f, -3.2f), wall), 3);
        }

        [Fact]
        [Verifies("SPL-010")]
        public void The_Caster_Moves_15_Percent_Slower_During_Its_Windup()
        {
            DuelWorld w = Duel(new Vec3(0f, 0f, 6f));
            PlayerInput walk = Sweep.Moving(0f, 1f);
            SpellTestKit.Run(w, 30, walk);
            Assert.Equal(GlobalRules.Movement.WalkSpeed, w.Player(0).Motor.Velocity.LengthXZ(), 2);
            ContentTestKit.HoldAndRelease(w, 15, walk);
            Assert.Equal(CastPhase.Windup, SpellTestKit.Machine(w, 0).Phase);
            Assert.Equal(0.85f, w.Spells.GetStatus(0).MovementFactor, 4);
            SpellTestKit.Run(w, 12, walk);
            Assert.Equal(0.85f * GlobalRules.Movement.WalkSpeed, w.Player(0).Motor.Velocity.LengthXZ(), 2);
        }

        [Fact]
        [Verifies("SPL-010")]
        public void Heat_1_Is_Deposited_Along_The_Cone()
        {
            var materials = new RecordingMaterials();
            DuelWorld w = Duel(new Vec3(0f, 0f, -3f), materials: materials);
            ContentTestKit.HoldAndRelease(w, 15, Sweep);
            SpellTestKit.Run(w, 20, Sweep);
            WorldDeposit heat = materials.Deposits.Single();
            Assert.Equal(DepositKind.Heat, heat.Kind);
            Assert.Equal(1, heat.Strength);
            Assert.Equal(FootprintShape.Cone, heat.Footprint.Shape);
            Assert.Equal(4f, heat.Footprint.Radius, 3);
            Assert.Equal(45f, heat.Footprint.HalfAngleDeg, 3);
            Assert.True(heat.Footprint.Contains(new Vec2(0f, -3f)));
        }

        [Fact]
        [Verifies("SPL-010")]
        public void A_Frontal_Guard_Halves_The_Sweep()
        {
            DuelWorld w = Duel(new Vec3(0f, 0f, -3f));
            ContentTestKit.HoldAndRelease(w, 15, Sweep, TestWorlds.B.With(InputButtons.Guard));
            SpellTestKit.Run(w, 20, Sweep, TestWorlds.B.With(InputButtons.Guard));
            DamageEvent hit = SpellTestKit.DamageTo(w, 1, "SPL-010").Single();
            Assert.Equal(GuardOutcome.Guarded, hit.Resolution.Guard);
            Assert.Equal(18f, hit.Dealt, 3);
        }

        [Fact]
        [Verifies("SPL-010")]
        public void A_Crest_Release_Adds_12_Percent_Damage()
        {
            Assert.Equal(36f * 1.12f, SweepAt(new Vec3(0f, 0f, -3f), hold: 27), 3);
        }

        [Fact]
        [Verifies("SPL-010")]
        public void It_Costs_15_Mana_And_Cools_Down_For_5_Seconds()
        {
            DuelWorld w = Duel(new Vec3(0f, 0f, -3f));
            ContentTestKit.HoldAndRelease(w, 15, Sweep);
            Assert.Equal(85f, w.Player(0).Resources.Mana);
            Assert.Equal(Ticks.FromSeconds(5.0), w.Player(0).Caster.CooldownRemainingTicks("SPL-010"));
            Assert.Equal(0, w.Player(0).Caster.CooldownRemainingTicks("SPL-009")); // cooldowns are per spell
        }
    }
}
