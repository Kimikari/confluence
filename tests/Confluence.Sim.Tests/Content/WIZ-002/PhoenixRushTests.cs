using System.Linq;
using Confluence.Casting;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Combat;
using Confluence.Sim.Content.Wizards.Wiz002;
using Confluence.Sim.Duel;
using Confluence.Sim.Fields;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Content.Wiz002
{
    /// <summary>SPL-013 Phoenix Rush and its transformations AUG-313 Rush Return and AUG-314 Rush Brake.</summary>
    public class PhoenixRushTests
    {
        private const string Rush = "SPL-013";

        private static DuelWorld Duel(ArenaLayout layout = null, params ActiveAugment[] augments) =>
            BramKit.Duel(BramKit.Bram(null, augments), layout, BramKit.Caster(BramKit.Bolt("SPL-T90", 30f)));

        /// <summary>Casts the rush facing +Z and runs until the outbound dash has ended.</summary>
        private static SpellEvent Dash(DuelWorld w, int hold = BramKit.NormalHold, PlayerInput? other = null)
        {
            int slot = BramKit.SlotOf(w, Rush);
            BramKit.Cast(w, slot, hold, TestWorlds.A.Selecting(slot), other);
            BramKit.RunUntil(w, () => BramKit.Spell(w, SpellEventKind.MovementEnded).Any(), 80, TestWorlds.A.Selecting(slot), other);
            return BramKit.Spell(w, SpellEventKind.MovementEnded).FirstOrDefault();
        }

        private static ArenaLayout WallAt(float z) => SpellTestKit.FlatWith(SpellTestKit.Wall(50, new Vec3(0f, 0f, z + 0.5f), new Vec3(6f, 3f, 1f)));

        [Fact]
        [Verifies("SPL-013")]
        public void It_Dashes_Up_To_8_Meters_Along_The_Ground_For_28_Mana_With_A_13_Second_Cooldown()
        {
            DuelWorld w = Duel();
            SpellEvent end = Dash(w);
            Assert.NotNull(end);
            Assert.InRange(end.Amount, 7.8f, 8.05f);
            Assert.InRange(w.Player(0).Position.Z, 1.8f, 2.05f);
            Assert.Equal(100f - 28f, BramKit.Mana(w), 3);
            Assert.True(w.Player(0).Caster.CooldownRemainingTicks(Rush) > Ticks.FromSeconds(12.0));
            SpellEvent start = BramKit.Spell(w, SpellEventKind.MovementStarted).Single();
            Assert.Equal(BramKit.Casts(w, CastEventKind.Committed).Single().Tick + Ticks.FromSeconds(0.35), start.Tick); // 0.35 s windup
        }

        [Fact]
        [Verifies("SPL-013")]
        public void It_Stops_At_Solid_Geometry_So_The_Endpoint_Can_Be_Blocked()
        {
            DuelWorld w = Duel(WallAt(-1f));
            SpellEvent end = Dash(w);
            Assert.InRange(w.Player(0).Position.Z, -1.5f, -1.35f); // capsule radius short of the wall face
            Assert.InRange(end.Amount, 4.5f, 4.7f);
        }

        [Fact]
        [Verifies("SPL-013")]
        public void Its_Path_Is_Telegraphed_At_Commitment_And_Fixed_For_The_Windup()
        {
            DuelWorld w = Duel();
            int slot = BramKit.SlotOf(w, Rush);
            BramKit.Cast(w, slot, BramKit.NormalHold, TestWorlds.A.Selecting(slot));
            SpellEvent marker = BramKit.Spell(w, SpellEventKind.Marker).Single(e => e.SpellId == Rush);
            Assert.Equal(BramKit.Casts(w, CastEventKind.Committed).Single().Tick, marker.Tick);
            Assert.Equal(8f, marker.Amount, 3);
            Assert.Equal(1f, marker.Direction.Z, 3);
            // Turning during the windup does not move the telegraphed path.
            BramKit.RunUntil(w, () => BramKit.Spell(w, SpellEventKind.MovementEnded).Any(), 80, PlayerInput.Idle(90f).Selecting(slot));
            Assert.InRange(w.Player(0).Position.X, -0.05f, 0.05f);
            Assert.InRange(w.Player(0).Position.Z, 1.8f, 2.05f);
        }

        [Fact]
        [Verifies("SPL-013")]
        public void Its_1_Meter_Endpoint_Burst_Deals_30_Thermal_Unguarded_And_Takes_The_Crest_Bonus()
        {
            DuelWorld w = Duel();
            TestWorlds.Place(w, 1, new Vec3(1.2f, 0f, 2f), 270f); // 0.8 m from the arrival point's body surface
            PlayerInput guard = PlayerInput.Idle(270f).With(InputButtons.Guard);
            Dash(w);
            BramKit.Run(w, 2);
            DamageEvent burst = BramKit.DamageTo(w, 1, Rush).Single();
            Assert.Equal(DamageKind.Splash, burst.Kind);
            Assert.Equal(30f, burst.Dealt, 3);
            Assert.Equal(DamageChannel.Thermal, burst.Resolution.Channels[0].Channel);
            Assert.Empty(BramKit.DamageTo(w, 0, Rush)); // the caster is not hit

            DuelWorld far = Duel();
            TestWorlds.Place(far, 1, new Vec3(2.6f, 0f, 2f), 270f);
            Dash(far);
            BramKit.Run(far, 2);
            Assert.Empty(BramKit.DamageTo(far, 1, Rush));

            DuelWorld crest = Duel();
            TestWorlds.Place(crest, 1, new Vec3(1.2f, 0f, 2f), 270f);
            Dash(crest, BramKit.CrestHold);
            BramKit.Run(crest, 2);
            Assert.Equal(30f * 1.12f, BramKit.DamageTo(crest, 1, Rush).Single().Dealt, 3); // primary payload (spec L480)

            DuelWorld guarded = Duel();
            TestWorlds.Place(guarded, 1, new Vec3(1.2f, 0f, 2f), 270f);
            int slot = BramKit.SlotOf(guarded, Rush);
            BramKit.Cast(guarded, slot, BramKit.NormalHold, TestWorlds.A.Selecting(slot), PlayerInput.Idle(270f));
            BramKit.RunUntil(guarded, () => BramKit.DamageTo(guarded, 1, Rush).Any(), 60, TestWorlds.A.Selecting(slot), guard);
            Assert.Equal(30f, BramKit.DamageTo(guarded, 1, Rush).Single().Dealt, 3); // a burst is not guarded (spec L104)
        }

        [Fact]
        [Verifies("SPL-013")]
        public void It_Leaves_A_1_Meter_Heat_1_Trail_For_3_Seconds_That_Deals_6_Per_Second_To_Entrants()
        {
            DuelWorld w = Duel();
            Dash(w);
            BramKit.Run(w, 1);
            HeatStripEntity trail = BramKit.Entity<HeatStripEntity>(w);
            Assert.NotNull(trail);
            Assert.Equal(1, w.Owned.Count(0, OwnedEntityKind.Field)); // one field of the caster
            Assert.Equal(1, BramKit.Reactions(w).UnitsAt(new Vec3(0f, 0f, -2f)).Heat);
            Assert.Equal(0, BramKit.Reactions(w).UnitsAt(new Vec3(1f, 0f, -2f)).Heat); // 1 m wide
            Assert.Empty(BramKit.DamageTo(w, 0, Rush)); // the caster stands at the burst, beyond the trail

            TestWorlds.Place(w, 1, new Vec3(0f, 0f, -2f), 0f);
            BramKit.Run(w, 2);
            DamageEvent first = BramKit.DamageTo(w, 1, Rush).Single();
            Assert.Equal(6f, first.Dealt, 3);
            Assert.Equal(DamageKind.Splash, first.Kind); // area contact
            BramKit.Run(w, Ticks.PerSecond);
            Assert.Equal(2, BramKit.DamageTo(w, 1, Rush).Count); // "6/s"

            int created = trail.Owned.CreatedTick;
            BramKit.RunUntil(w, () => !trail.Alive, 240);
            Assert.InRange(w.Tick - created, Ticks.FromSeconds(3.0) - 1, Ticks.FromSeconds(3.0) + 2); // "persists 3 s"
            int hits = BramKit.DamageTo(w, 1, Rush).Count;
            BramKit.Run(w, 2 * Ticks.PerSecond);
            Assert.Equal(hits, BramKit.DamageTo(w, 1, Rush).Count);
        }

        [Fact]
        [Verifies("SPL-013")]
        public void The_Trail_Is_Neutral_And_The_Dash_Grants_No_Invulnerability()
        {
            DuelWorld w = Duel();
            int slot = BramKit.SlotOf(w, Rush);
            BramKit.Cast(w, slot, BramKit.NormalHold, TestWorlds.A.Selecting(slot));
            BramKit.RunUntil(w, () => BramKit.Spell(w, SpellEventKind.MovementStarted).Any(), 40, TestWorlds.A.Selecting(slot));
            BramKit.Run(w, 3, TestWorlds.A.Selecting(slot));
            Assert.True(w.Player(0).Motor.IsDisplaced);
            DamageEvent hit = w.ApplyDamage(DamageRequest.Single(1, 0, DamageKind.Direct, "SPL-T99", DamageChannel.Physical, 10f).From(w.Player(1).Position));
            Assert.Equal(10f, hit.Dealt, 3); // "no invulnerability" (spec L82)

            BramKit.RunUntil(w, () => BramKit.Spell(w, SpellEventKind.MovementEnded).Any(), 60);
            BramKit.Run(w, 1);
            TestWorlds.Place(w, 0, new Vec3(0f, 0f, -3f), 0f); // the caster steps back onto his own trail
            BramKit.Run(w, 2);
            Assert.Equal(6f, BramKit.DamageTo(w, 0, Rush).Single().Dealt, 3); // neutral field (spec L496)
            Assert.Equal(0f, w.Player(0).Shields.Total, 3); // and self-damage does not charge Tempered Hands
        }

        [Fact]
        [Verifies("SPL-013")]
        public void Dash_Bonuses_Never_Exceed_The_8_Meter_Cap()
        {
            DuelWorld w = Duel(TestWorlds.Flat(boundaryRadius: 30f, floorHalf: 30f));
            w.Player(0).Stats.Add(StatKind.DashDistance, 3f, "TEST");
            SpellEvent end = Dash(w);
            Assert.InRange(end.Amount, 7.8f, 8.05f); // spec L124
        }

        // ----- AUG-313 Rush Return ------------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 6f, 3f)]
        [InlineData(2, 7f, 4f)]
        [Verifies("AUG-313")]
        public void Rush_Return_Leaves_A_25_Health_Ember_And_The_Recast_Dashes_Back_Toward_It(int rank, float back, float emberSeconds)
        {
            DuelWorld w = Duel(null, BramKit.Aug("AUG-313", rank));
            int slot = BramKit.SlotOf(w, Rush);
            Dash(w);
            SpellEvent placed = BramKit.Spell(w, SpellEventKind.DeployablePlaced).Single();
            Assert.Equal(25f, placed.Amount);
            Assert.Equal(Ticks.FromSeconds(emberSeconds), placed.Ticks);
            Assert.InRange(placed.Position.Z, -6.05f, -5.95f); // the departure point

            BramKit.Recast(w, slot, TestWorlds.A.Selecting(slot));
            BramKit.RunUntil(w, () => BramKit.Spell(w, SpellEventKind.MovementEnded).Count() >= 2, 60, TestWorlds.A.Selecting(slot));
            SpellEvent back2 = BramKit.Spell(w, SpellEventKind.MovementEnded).ElementAt(1);
            Assert.InRange(back2.Amount, back - 0.2f, back + 0.05f);
            Assert.InRange(w.Player(0).Position.Z, 2f - back - 0.1f, 2f - back + 0.25f);
            Assert.Equal(2, BramKit.Spell(w, SpellEventKind.MovementStarted).Count()); // "at most 2 dash charges total"
            BramKit.Recast(w, slot, TestWorlds.A.Selecting(slot));
            BramKit.Run(w, 30, TestWorlds.A.Selecting(slot));
            Assert.Equal(2, BramKit.Spell(w, SpellEventKind.MovementStarted).Count());
        }

        [Fact]
        [Verifies("AUG-313")]
        public void Rush_Return_Burst_Drops_To_22_And_Destroying_The_Ember_Cancels_The_Return()
        {
            DuelWorld w = Duel(null, BramKit.Aug("AUG-313"));
            int slot = BramKit.SlotOf(w, Rush);
            TestWorlds.Place(w, 1, new Vec3(1.2f, 0f, 2f), 270f);
            Dash(w);
            BramKit.Run(w, 1);
            Assert.Equal(22f, BramKit.DamageTo(w, 1, Rush).Single().Dealt, 3); // "Initial burst 30 becomes 22"

            EmberEntity ember = BramKit.Entity<EmberEntity>(w);
            TestWorlds.Place(w, 1, new Vec3(3f, 0f, -6f), 270f);
            BramKit.OpponentFires(w, 0, ember.Position, TestWorlds.A.Selecting(slot)); // "Destroy ember"
            BramKit.RunUntil(w, () => !ember.Alive, 30, TestWorlds.A.Selecting(slot));
            Assert.Contains(BramKit.Spell(w, SpellEventKind.DeployableDestroyed), e => e.EntityId == ember.Id);
            BramKit.Recast(w, slot, TestWorlds.A.Selecting(slot));
            BramKit.Run(w, 30, TestWorlds.A.Selecting(slot));
            Assert.Single(BramKit.Spell(w, SpellEventKind.MovementStarted));
        }

        [Fact]
        [Verifies("AUG-313")]
        public void Rush_Return_Is_Stopped_By_A_Blocked_Route_And_Waits_For_An_Early_Recast()
        {
            DuelWorld w = Duel(null, BramKit.Aug("AUG-313"));
            int slot = BramKit.SlotOf(w, Rush);
            BramKit.Cast(w, slot, BramKit.NormalHold, TestWorlds.A.Selecting(slot));
            BramKit.RunUntil(w, () => BramKit.Spell(w, SpellEventKind.MovementStarted).Any(), 40, TestWorlds.A.Selecting(slot));
            BramKit.Recast(w, slot, TestWorlds.A.Selecting(slot)); // during the outbound dash: queued
            BramKit.RunUntil(w, () => BramKit.Spell(w, SpellEventKind.MovementEnded).Count() >= 2, 80, TestWorlds.A.Selecting(slot));
            Assert.InRange(BramKit.Spell(w, SpellEventKind.MovementEnded).First().Amount, 7.8f, 8.05f); // the outbound dash ran fully
            Assert.InRange(w.Player(0).Position.Z, -4.1f, -3.75f);

            // "block return route": geometry on the way back stops the return dash.
            DuelWorld v = Duel(null, BramKit.Aug("AUG-313"));
            Dash(v);
            Assert.True(v.AddTemporarySolid(new Confluence.Sim.World.TemporarySolidSpec
            {
                Box = YawBox.OnFloor(new Vec3(0f, 0f, -0.5f), new Vec3(4f, 3f, 1f), 0f),
                Kind = SolidKind.Wall,
                BlocksSight = true,
                LifetimeTicks = 600,
                OwnerPlayer = 1,
                SourceId = "SPL-T96",
            }) >= 0);
            BramKit.Recast(v, BramKit.SlotOf(v, Rush), TestWorlds.A.Selecting(BramKit.SlotOf(v, Rush)));
            BramKit.RunUntil(v, () => BramKit.Spell(v, SpellEventKind.MovementEnded).Count() >= 2, 60);
            Assert.InRange(v.Player(0).Position.Z, 0.3f, 0.5f);
        }

        // ----- AUG-314 Rush Brake -------------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 24f)]
        [InlineData(2, 28f)]
        [Verifies("AUG-314")]
        public void Rush_Brake_Recast_During_The_Dash_Stops_Early_With_A_1_2_Meter_Burst(int rank, float damage)
        {
            DuelWorld w = Duel(null, BramKit.Aug("AUG-314", rank));
            int slot = BramKit.SlotOf(w, Rush);
            Assert.Equal(28f + 5f, BramKit.Form(w, Rush).Mana); // "Mana+5"
            BramKit.Cast(w, slot, BramKit.NormalHold, TestWorlds.A.Selecting(slot));
            BramKit.RunUntil(w, () => BramKit.Spell(w, SpellEventKind.MovementStarted).Any(), 40, TestWorlds.A.Selecting(slot));
            BramKit.Run(w, 6, TestWorlds.A.Selecting(slot));
            float mana = BramKit.Mana(w);
            Vec3 at = w.Player(0).Position;
            TestWorlds.Place(w, 1, at + new Vec3(1.3f, 0f, 0.6f), 270f);
            BramKit.Recast(w, slot, TestWorlds.A.Selecting(slot));
            BramKit.Run(w, 2, TestWorlds.A.Selecting(slot));
            SpellEvent end = BramKit.Spell(w, SpellEventKind.MovementEnded).Single();
            Assert.InRange(end.Amount, 1.5f, 4f); // stopped early
            SpellEvent burst = BramKit.Spell(w, SpellEventKind.AreaBurst).Single();
            Assert.Equal(1.2f, burst.Amount, 3);
            Assert.Equal(damage, BramKit.DamageTo(w, 1, Rush).Single().Dealt, 3); // "burst of 24 damage once" / 28
            Assert.Equal(mana, BramKit.Mana(w), 3); // "never grants another dash or refund"
            Assert.Single(BramKit.Spell(w, SpellEventKind.MovementStarted));
        }

        [Fact]
        [Verifies("AUG-314")]
        public void Rush_Brake_Keeps_The_Full_30_Endpoint_Burst_And_Combines_With_Rush_Return()
        {
            DuelWorld w = Duel(null, BramKit.Aug("AUG-314", 2));
            TestWorlds.Place(w, 1, new Vec3(1.2f, 0f, 2f), 270f);
            Dash(w);
            BramKit.Run(w, 1);
            Assert.Equal(30f, BramKit.DamageTo(w, 1, Rush).Single().Dealt, 3); // "full endpoint burst remains 30"

            // With AUG-313: the first recast brakes, the second returns (two recasts, two dashes).
            DuelWorld both = Duel(null, BramKit.Aug("AUG-313"), BramKit.Aug("AUG-314"));
            int slot = BramKit.SlotOf(both, Rush);
            BramKit.Cast(both, slot, BramKit.NormalHold, TestWorlds.A.Selecting(slot));
            BramKit.RunUntil(both, () => BramKit.Spell(both, SpellEventKind.MovementStarted).Any(), 40, TestWorlds.A.Selecting(slot));
            BramKit.Run(both, 6, TestWorlds.A.Selecting(slot));
            BramKit.Recast(both, slot, TestWorlds.A.Selecting(slot));
            Assert.Single(BramKit.Spell(both, SpellEventKind.MovementEnded));
            BramKit.Recast(both, slot, TestWorlds.A.Selecting(slot));
            BramKit.RunUntil(both, () => BramKit.Spell(both, SpellEventKind.MovementEnded).Count() >= 2, 60, TestWorlds.A.Selecting(slot));
            Assert.Equal(2, BramKit.Spell(both, SpellEventKind.MovementStarted).Count());
            Assert.InRange(both.Player(0).Position.Z, -6.1f, -5.6f); // back at the ember
        }
    }
}
