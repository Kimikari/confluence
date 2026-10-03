using System.Linq;
using Confluence.Casting;
using Confluence.Core.Arena;
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
    /// SPL-074 Water Hammer through WIZ-010's grammar: "A 0.8 m-wide thrust; hit pushes 2 m; a miss leaves 0.3 s recovery."
    /// "48" physical; "Water 1 sprays only the impact spot"; 20 mana, 8 s, 0.4 s, 3 m.
    /// </summary>
    public class WaterHammerTests
    {
        private const int Slot = 1; // the default kit's second spell

        private static HalenDuel Close(float z = -3.5f, float x = 0f, ArenaLayout layout = null)
        {
            HalenDuel d = HalenTestKit.Duel(layout: layout);
            TestWorlds.Place(d.World, 1, new Vec3(x, 0f, z), 180f);
            d.Halen = TestWorlds.A; // a level thrust from the focus along +Z
            return d;
        }

        private static DamageEvent HitOn(HalenDuel d) => d.DamageTo(1, WaterHammer.Id).SingleOrDefault();

        [Fact]
        [Verifies("SPL-074")]
        public void A_Thrust_Hit_Deals_48_Physical_And_Pushes_2_Meters_After_The_04_Second_Windup()
        {
            HalenDuel d = Close();
            CastDuelEvent commit = d.Cast(Slot);
            Assert.Equal(WaterHammer.Id, commit.SpellId);
            d.RunUntil(() => HitOn(d) != null, 40);
            SpellEvent strike = d.SpellEvents(SpellEventKind.ArcReleased).Single();
            Assert.Equal(commit.Tick + Ticks.FromSeconds(0.4), strike.Tick);
            DamageEvent hit = HitOn(d);
            Assert.Equal(DamageKind.Direct, hit.Kind);
            Assert.Equal(48f, hit.Dealt, 3);
            Assert.Equal(DamageChannel.Physical, hit.Resolution.Channels.Single().Channel);
            d.Run(30);
            Assert.Equal(-3.5f + 2f, d.P1.Position.Z, 1);
        }

        [Theory]
        [InlineData(0.75f, true)]
        [InlineData(1.0f, false)]
        [Verifies("SPL-074")]
        public void The_Thrust_Is_08_Meters_Wide(float lateral, bool hits)
        {
            HalenDuel d = Close(x: lateral);
            d.Cast(Slot);
            d.Run(40);
            Assert.Equal(hits, HitOn(d) != null);
        }

        // The thrust line runs 3 m from the focus; its 0.8 m swept width adds half a width around the line, so a body whose
        // surface is within 3.4 m of the caster's center line is struck (framework strike volume, spec L102).
        [Theory]
        [InlineData(3.7f, true)]
        [InlineData(4.0f, false)]
        [Verifies("SPL-074")]
        public void It_Reaches_3_Meters(float distance, bool hits)
        {
            HalenDuel d = Close(z: -6f + distance);
            d.Cast(Slot);
            d.Run(40);
            Assert.Equal(hits, HitOn(d) != null);
        }

        [Fact]
        [Verifies("SPL-074")]
        public void A_Miss_Leaves_03_Seconds_More_Recovery_To_Punish()
        {
            int RecoveryAfterRelease(bool hit)
            {
                HalenDuel d = Close(z: hit ? -3.5f : 0f);
                d.Cast(Slot);
                d.RunUntil(() => d.SpellEvents(SpellEventKind.ArcReleased).Any(), 40);
                int release = d.SpellEvents(SpellEventKind.ArcReleased).Single().Tick;
                d.RunUntil(() => SpellTestKit.CastEvents(d.World, CastEventKind.RecoveryEnded).Any(), 80);
                return SpellTestKit.CastEvents(d.World, CastEventKind.RecoveryEnded).Single().Tick - release;
            }

            int heavy = Ticks.FromSeconds(GlobalRules.Casting.RecoveryHeavyProjectileSec); // 0.4 s windup: heavy (D-047)
            Assert.Equal(heavy, RecoveryAfterRelease(true));
            Assert.Equal(heavy + Ticks.FromSeconds(0.3), RecoveryAfterRelease(false));
        }

        [Fact]
        [Verifies("SPL-074")]
        public void Water_1_Sprays_Only_The_Impact_Spot()
        {
            HalenDuel d = Close();
            d.Cast(Slot);
            d.RunUntil(() => HitOn(d) != null, 40);
            Vec2 spot = new Vec2(0f, -3.5f);
            Assert.Equal(1, d.Units(spot.ToXZ()).Water);
            Assert.Equal(1, d.Total(Material.Water, Footprint.Circle(spot, 3f))); // one cell, not an area
        }

        [Fact]
        [Verifies("SPL-074")]
        public void A_Thrust_Into_A_Wall_Sprays_There_And_Counts_As_A_Miss()
        {
            HalenDuel d = Close(z: -1f, layout: HalenTestKit.Layout(SpellTestKit.Wall(30, new Vec3(0f, 0f, -4.25f), new Vec3(4f, 3f, 0.5f))));
            d.Cast(Slot);
            d.Run(40);
            Assert.Null(HitOn(d));
            Assert.Equal(1, d.Total(Material.Water, Footprint.Circle(new Vec2(0f, -4.6f), 0.8f)));
            Assert.Single(d.Tide(TideEventKind.MissRecovery));
        }

        [Fact]
        [Verifies("SPL-074")]
        public void A_Guard_Or_Parry_Halves_The_Thrust_And_Its_Push()
        {
            HalenDuel d = Close();
            d.Slot = Slot;
            for (int k = 0; k < HalenDuel.NormalRelease; k++) d.Step(InputButtons.Primary);
            d.Step();
            d.Run(10);
            d.Other = TestWorlds.B.With(InputButtons.Guard); // raised during the windup, past its perfect window at the strike
            d.RunUntil(() => HitOn(d) != null, 40);
            DamageEvent hit = HitOn(d);
            Assert.Equal(GuardOutcome.Guarded, hit.Resolution.Guard); // an offensive melee spell (spec L104, L535)
            Assert.Equal(24f, hit.Dealt, 3);
            Assert.Equal(1f, hit.Resolution.Knockback, 3);
        }

        [Fact]
        [Verifies("SPL-074")]
        public void It_Costs_20_Mana_And_Cools_Down_8_Seconds()
        {
            HalenDuel d = Close();
            CastDuelEvent commit = d.Cast(Slot);
            Assert.Equal(20f, commit.Cast.Mana, 3);
            Assert.Equal(80f, d.P0.Resources.Mana, 3);
            Assert.Equal(Ticks.FromSeconds(8.0), d.P0.Caster.CooldownRemainingTicks(WaterHammer.Id));
        }
    }
}
