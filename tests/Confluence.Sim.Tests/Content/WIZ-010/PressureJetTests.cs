using System.Linq;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions;
using Confluence.Reactions.Grid;
using Confluence.Sim.Casting;
using Confluence.Sim.Combat;
using Confluence.Sim.Content.Wizards.HalenTide;
using Confluence.Sim.Duel;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Content.Halen
{
    /// <summary>
    /// SPL-073 Pressure Jet through WIZ-010's pressure-metering grammar: "35 m/s stream projectile pushes hit target 1.5 m;
    /// collision push damage is never added." "32" physical; "Water 1 wets the first impact surface"; 16 mana, 5 s, 0.25 s,
    /// 20 m.
    /// </summary>
    public class PressureJetTests
    {
        private static DamageEvent HitOn(HalenDuel d, int target = 1) => d.DamageTo(target, PressureJet.Id).SingleOrDefault();

        [Fact]
        [Verifies("SPL-073")]
        public void A_Direct_Hit_Deals_32_Physical_And_Pushes_1_5_Meters_Along_The_Stream()
        {
            HalenDuel d = HalenTestKit.Duel();
            d.AimAtOther();
            CastDuelEvent commit = d.Cast(0);
            Assert.Equal(PressureJet.Id, commit.SpellId);
            d.RunUntil(() => HitOn(d) != null, 90);
            DamageEvent hit = HitOn(d);
            Assert.Equal(DamageKind.Direct, hit.Kind);
            Assert.Equal(32f, hit.Dealt, 3);
            Assert.Equal(DamageChannel.Physical, hit.Resolution.Channels.Single().Channel);
            d.Run(40);
            Assert.Equal(6f + 1.5f, d.P1.Position.Z, 1); // pushed along the stream (+Z), at most the 4 m cap
            Assert.Single(d.DamageTo(1));                // the push adds no damage of its own
        }

        [Fact]
        [Verifies("SPL-073")]
        public void Pushing_A_Target_Into_A_Wall_Adds_No_Collision_Damage()
        {
            HalenDuel d = HalenTestKit.Duel(layout: HalenTestKit.Layout(SpellTestKit.Wall(20, new Vec3(0f, 0f, 7.25f), new Vec3(6f, 3f, 0.5f))));
            d.AimAtOther();
            d.Cast(0);
            d.RunUntil(() => HitOn(d) != null, 90);
            d.Run(40);
            Assert.InRange(d.P1.Position.Z, 6.3f, 6.61f); // stopped by the wall's face (0.6 m of room)
            Assert.Equal(32f, d.DamageTo(1).Sum(e => e.Dealt), 3);
        }

        [Fact]
        [Verifies("SPL-073")]
        public void It_Flies_At_35_Meters_Per_Second_After_A_Quarter_Second_Windup()
        {
            HalenDuel d = HalenTestKit.Duel();
            d.AimAtOther();
            CastDuelEvent commit = d.Cast(0);
            d.RunUntil(() => d.SpellEvents(SpellEventKind.ProjectileLaunched).Any(), 30);
            SpellEvent launch = d.SpellEvents(SpellEventKind.ProjectileLaunched).Single();
            Assert.Equal(commit.Tick + Ticks.FromSeconds(0.25), launch.Tick);
            Assert.Equal(35f, launch.Amount, 3);
            TideProjectileEntity jet = d.Runtime.Entities.OfType<TideProjectileEntity>().Single();
            float before = jet.Travelled;
            d.Run(6);
            Assert.Equal(35f * 6f / 60f, jet.Travelled - before, 3);
        }

        [Fact]
        [Verifies("SPL-073")]
        public void Its_Range_Ends_The_Stream_At_20_Meters()
        {
            HalenDuel d = HalenTestKit.Duel(layout: TestWorlds.Flat(boundaryRadius: 40f, floorHalf: 40f));
            TestWorlds.Place(d.World, 1, new Vec3(0f, 0f, 24f), 180f);
            d.Halen = TestWorlds.A.Facing(0f, 0f);
            d.Cast(0);
            d.Run(90);
            SpellEvent expired = d.SpellEvents(SpellEventKind.ProjectileExpired).Single();
            Assert.Equal(20f, expired.Amount, 2);
            Assert.Empty(d.DamageTo(1));
        }

        [Fact]
        [Verifies("SPL-073")]
        public void Water_1_Wets_The_First_Surface_It_Strikes_For_Three_Seconds()
        {
            HalenDuel d = HalenTestKit.Duel();
            d.AimAt(new Vec3(0f, 0f, 0f));
            d.Cast(0);
            d.RunUntil(() => d.SpellEvents(SpellEventKind.ProjectileImpact).Any(), 60);
            SpellEvent impact = d.SpellEvents(SpellEventKind.ProjectileImpact).Single();
            Assert.Equal(TestWorlds.FloorId, impact.SolidId);
            Vec3 spot = new Vec3(impact.Position.X, 0f, impact.Position.Z);
            Assert.Equal(1, d.Units(spot).Water);              // the struck cell only (one local unit, spec L163)
            Assert.Equal(1, d.Total(Material.Water, Footprint.Circle(spot.XZ(), 2f)));
            d.RunSeconds(HalenRules.ImpactWaterLifetimeSec - 0.2);
            Assert.Equal(1, d.Units(spot).Water);
            d.RunSeconds(0.4);
            Assert.Equal(0, d.Units(spot).Water);
        }

        [Fact]
        [Verifies("SPL-073")]
        public void A_Struck_Target_Is_Wetted_At_Its_Feet()
        {
            HalenDuel d = HalenTestKit.Duel();
            d.AimAtOther();
            d.Cast(0);
            d.RunUntil(() => HitOn(d) != null, 90);
            // The struck body is the first surface: the water lands in the ground cell under the contact (D-138), not in
            // the air, so it is a touchable wet cell (spec L118).
            Vec3 under = new Vec3(0f, 0f, 6f);
            Assert.Equal(1, d.Total(Material.Water, Footprint.Circle(under.XZ(), 1f)));
            Assert.Equal(1, d.Total(Material.Water, Footprint.Circle(under.XZ(), 4f)));
            Vec3 wet = d.Reactions.Grid.CellCenter(FirstWetCell(d, under)).ToXZ();
            Assert.Equal(0f, d.Reactions.SurfaceAt(wet).SurfaceY, 3);
        }

        private static int FirstWetCell(HalenDuel d, Vec3 near)
        {
            var cells = new System.Collections.Generic.List<int>();
            d.Reactions.Grid.Rasterize(Footprint.Circle(near.XZ(), 1f), cells);
            return cells.First(c => d.Reactions.UnitsAtCell(c).Water > 0);
        }

        [Fact]
        [Verifies("SPL-073")]
        public void It_Costs_16_Mana_At_Commitment_And_Cools_Down_5_Seconds()
        {
            HalenDuel d = HalenTestKit.Duel();
            d.AimAtOther();
            d.Step(InputButtons.Primary);
            Assert.Equal(100f, d.P0.Resources.Mana); // preparation is free (spec L90)
            d.Slot = 0;
            for (int k = 1; k < HalenDuel.NormalRelease; k++) d.Step(InputButtons.Primary);
            d.Step();
            Assert.Equal(84f, d.P0.Resources.Mana, 3);
            Assert.Equal(Ticks.FromSeconds(5.0), d.P0.Caster.CooldownRemainingTicks(PressureJet.Id));
            Assert.False(d.P0.Caster.Loadout.FormFor(PressureJet.Id).Castable.RequiresAnticipatoryCue); // 32 <= 70 (spec L96)
        }

        [Fact]
        [Verifies("SPL-073")]
        public void A_Band_Held_Release_Pays_15_Percent_Less_Mana()
        {
            HalenDuel d = HalenTestKit.Duel();
            d.AimAtOther();
            CastDuelEvent commit = d.Cast(0, mastery: true);
            Assert.True(commit.Cast.Mastery.IsMastered, commit.Cast.Mastery.Miss.ToString());
            Assert.Equal(MasteryReward.ManaEfficiency, commit.Cast.Mastery.Reward);
            Assert.Equal(16f * 0.85f, commit.Cast.Mana, 3);
            d.RunUntil(() => HitOn(d) != null, 90);
            Assert.Equal(32f, HitOn(d).Dealt, 3); // the reward is efficiency, never damage
        }

        [Fact]
        [Verifies("SPL-073")]
        public void A_Guard_Halves_The_Stream_And_Its_Push()
        {
            HalenDuel d = HalenTestKit.Duel();
            d.AimAtOther();
            d.Cast(0);
            d.RunUntil(() => d.SpellEvents(SpellEventKind.ProjectileLaunched).Any(), 30);
            d.Other = TestWorlds.B.With(InputButtons.Guard); // pressed now, held until the stream arrives (past the perfect window)
            d.RunUntil(() => HitOn(d) != null, 60);
            DamageEvent hit = HitOn(d);
            Assert.Equal(GuardOutcome.Guarded, hit.Resolution.Guard); // a physical projectile direct hit (spec L104)
            Assert.Equal(16f, hit.Dealt, 3);
            Assert.Equal(0.75f, hit.Resolution.Knockback, 3);           // the same fraction off the push
        }

        [Fact]
        [Verifies("SPL-073")]
        public void Cover_Stops_The_Narrow_Stream()
        {
            // Counterplay "anchor behind cover": a wall between the players takes the stream (and its water).
            HalenDuel d = HalenTestKit.Duel(layout: HalenTestKit.Layout(SpellTestKit.Wall(21, new Vec3(0f, 0f, 1f), new Vec3(4f, 3f, 0.5f))));
            d.AimAtOther();
            d.Cast(0);
            d.Run(60);
            SpellEvent impact = d.SpellEvents(SpellEventKind.ProjectileImpact).Single();
            Assert.Equal(21, impact.SolidId);
            Assert.Empty(d.DamageTo(1));
            Assert.Equal(1, d.Total(Material.Water, Footprint.Circle(new Vec2(0f, 0.6f), 0.6f)));
        }

        [Fact]
        [Verifies("SPL-073")]
        public void Sidestepping_Its_Narrow_Line_Avoids_It()
        {
            HalenDuel d = HalenTestKit.Duel();
            d.AimAtOther();
            d.Cast(0);
            d.Other = TestWorlds.B.Moving(1f, 0f); // strafe out of the line during the windup and flight
            d.Run(60);
            Assert.Empty(d.DamageTo(1));
        }
    }
}
