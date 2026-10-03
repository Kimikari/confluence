using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Sim.Casting;
using Confluence.Sim.Combat;
using Confluence.Sim.Content.Wizards.HalenTide;
using Confluence.Sim.Duel;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Content.Halen
{
    /// <summary>AUG-435 Hammer Wake and AUG-436 Hammer Fan on SPL-074 Water Hammer, cast through WIZ-010's grammar.</summary>
    public class HammerTransformTests
    {
        private const int Slot = 1; // the default kit's second spell

        private static HalenDuel Hammer(string augment, int rank, Vec3 target)
        {
            HalenDuel d = HalenTestKit.Duel(HalenTestKit.Halen(null, HalenTestKit.Aug(augment, rank)));
            TestWorlds.Place(d.World, 1, target, 180f);
            d.Halen = TestWorlds.A; // a level strike from (0, 0, -6) along +Z
            return d;
        }

        /// <summary>A point at a distance and a yaw off Halen's facing (+Z) from his spawn.</summary>
        private static Vec3 At(float distance, float yawDeg) =>
            new Vec3(0f, 0f, -6f) + CMath.ForwardFromYaw(yawDeg) * distance;

        private static DamageEvent HitOn(HalenDuel d) => d.DamageTo(1, WaterHammer.Id).SingleOrDefault();

        private static int CellsIn(HalenDuel d, Footprint footprint)
        {
            var cells = new List<int>();
            d.Reactions.Grid.Rasterize(footprint, cells);
            return cells.Count;
        }

        // ---- AUG-435 Hammer Wake -------------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 1f, 3f)]
        [InlineData(2, 1.5f, 4f)]
        [Verifies("AUG-435")]
        public void A_Hit_Leaves_A_Water_1_Puddle_Under_The_Target_For_Its_Lifetime(int rank, float radius, float lifetime)
        {
            HalenDuel d = Hammer(HammerWake.Id, rank, new Vec3(0f, 0f, -3.5f));
            d.Cast(Slot);
            d.RunUntil(() => HitOn(d) != null, 40);
            DamageEvent hit = HitOn(d);
            Assert.Equal(40f, hit.Dealt, 3);                                 // drawback "Damage 48 becomes 40"
            Assert.Equal(2f, hit.Resolution.Knockback, 3);                    // the push is unchanged
            d.Step();
            Vec2 spot = new Vec2(0f, -3.5f);
            int cells = CellsIn(d, Footprint.Circle(spot, radius));
            Assert.Equal(cells, d.Total(Material.Water, Footprint.Circle(spot, radius + 1f))); // Water 1 over the puddle only
            Assert.Equal(1, d.Units(spot.ToXZ()).Water);
            Assert.True(cells > 1);                                                            // a puddle, not the one-cell spray

            // "no damage": the puddle itself never damages anyone, and it dries at its stated lifetime.
            int hitTick = hit.Tick;
            d.RunUntil(() => d.World.Tick >= hitTick + Ticks.FromSeconds(lifetime) - 3, Ticks.FromSeconds(lifetime));
            Assert.Equal(1, d.Units(spot.ToXZ()).Water);
            d.Run(6);
            Assert.Equal(0, d.Total(Material.Water, Footprint.Circle(spot, radius + 1f)));
            Assert.Single(d.DamageTo(1));
        }

        [Fact]
        [Verifies("AUG-435")]
        public void The_Puddle_Is_A_Neutral_Electricity_Risk_That_A_Dry_Step_Avoids()
        {
            // Drawback "neutral electricity risk" and counterplay "Avoid or electrify puddle": any charge in the puddle reaches
            // whoever stands in it, its caster included, and nobody on the dry floor beside it.
            HalenDuel d = Hammer(HammerWake.Id, 2, new Vec3(0f, 0f, -3.5f));
            d.Cast(Slot);
            d.RunUntil(() => HitOn(d) != null, 40);
            d.Run(30);
            TestWorlds.Place(d.World, 0, new Vec3(0.5f, 0f, -3.5f), 0f);  // Halen steps into his own puddle
            TestWorlds.Place(d.World, 1, new Vec3(3.5f, 0f, -3.5f), 180f); // the opponent stays dry
            d.Halen = TestWorlds.A;
            d.Other = TestWorlds.B;
            d.Step();
            d.Reactions.Discharge(new ElectricDischarge { Point = new Vec3(-0.5f, 0f, -3.5f), ReactionDamage = 10f, Owner = 1, SourceEventId = 0 });
            d.Run(3);
            Assert.Contains(d.DamageTo(0), e => e.Kind == DamageKind.Environment);
            Assert.DoesNotContain(d.DamageTo(1), e => e.Kind == DamageKind.Environment);
        }

        [Fact]
        [Verifies("AUG-435")]
        public void A_Miss_Leaves_No_Puddle_And_Keeps_The_Miss_Recovery()
        {
            HalenDuel d = Hammer(HammerWake.Id, 1, new Vec3(0f, 0f, 6f));
            d.Cast(Slot);
            d.Run(40);
            Assert.Null(HitOn(d));
            Assert.Equal(0, d.Total(Material.Water, Footprint.Circle(new Vec2(0f, -3f), 6f)));
            Assert.Single(d.Tide(TideEventKind.MissRecovery));
        }

        // ---- AUG-436 Hammer Fan --------------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 40f, 37.5f)]
        [InlineData(2, 44f, 45f)]
        [Verifies("AUG-436")]
        public void The_Thrust_Becomes_A_Cone_With_No_Push(int rank, float damage, float halfAngle)
        {
            HalenDuel d = Hammer(HammerFan.Id, rank, At(2.5f, 30f));
            SpellForm form = d.P0.Caster.Loadout.FormFor(WaterHammer.Id);
            var cone = Assert.IsType<ConeDelivery>(form.Delivery);
            Assert.Equal(halfAngle, cone.HalfAngleDeg, 3); // "75-degree" / "90 degrees"
            Assert.Equal(3f, cone.ReachFor(form), 3);       // the record's 3 m reach is inherited (spec L508)
            Assert.True(form.HasTag("cone"));
            Assert.False(form.HasTag("push"));              // structural delta (D-012)
            Vec3 before = d.P1.Position;
            d.Cast(Slot);
            d.RunUntil(() => HitOn(d) != null, 50);
            DamageEvent hit = HitOn(d);
            Assert.NotNull(hit);
            Assert.Equal(damage, hit.Dealt, 3);
            Assert.Equal(0f, hit.Resolution.Knockback, 3); // "with no push"
            d.Run(20);
            Assert.Equal(0f, Vec3.DistanceXZ(before, d.P1.Position), 2);
            Assert.Equal(1, d.Units(before).Water);         // the record's impact spray is kept
        }

        [Theory]
        [InlineData(1, false)]
        [InlineData(2, true)]
        [Verifies("AUG-436")]
        public void Rank_2_Widens_The_Cone_To_90_Degrees(int rank, bool hits)
        {
            // A body 52 degrees off the aim at 2.5 m: its near edge is about 43 degrees off, outside 37.5 and inside 45.
            HalenDuel d = Hammer(HammerFan.Id, rank, At(2.5f, 52f));
            d.Cast(Slot);
            d.Run(50);
            Assert.Equal(hits, HitOn(d) != null);
        }

        [Fact]
        [Verifies("AUG-436")]
        public void It_Costs_4_More_Mana_And_Winds_Up_01_Second_Longer()
        {
            HalenDuel d = Hammer(HammerFan.Id, 1, At(2.5f, 0f));
            CastDuelEvent commit = d.Cast(Slot);
            Assert.Equal(24f, commit.Cast.Mana, 3);                        // "Mana +4"
            d.RunUntil(() => d.SpellEvents(SpellEventKind.ConeReleased).Any(), 50);
            SpellEvent cone = d.SpellEvents(SpellEventKind.ConeReleased).Single();
            Assert.Equal(commit.Tick + Ticks.FromSeconds(0.5), cone.Tick); // "windup +0.1 s"
            Assert.Equal(Ticks.FromSeconds(8.0), d.P0.Caster.CooldownRemainingTicks(WaterHammer.Id) + (cone.Tick - commit.Tick));
        }

        [Fact]
        [Verifies("AUG-436")]
        public void Flanking_The_Cone_Or_Backing_Out_Avoids_It()
        {
            HalenDuel flank = Hammer(HammerFan.Id, 2, At(1.5f, 100f));
            flank.Cast(Slot);
            flank.Run(50);
            Assert.Null(HitOn(flank));

            HalenDuel back = Hammer(HammerFan.Id, 2, At(2.5f, 0f));
            back.Cast(Slot);
            back.Other = TestWorlds.B.Moving(0f, -1f); // walk away from Halen during the 0.5 s windup
            back.Run(50);
            Assert.Null(HitOn(back));
            Assert.Single(back.Tide(TideEventKind.MissRecovery)); // the whiff is still punishable
        }

        [Fact]
        [Verifies("AUG-436")]
        public void A_Guard_Halves_The_Cone()
        {
            HalenDuel d = Hammer(HammerFan.Id, 1, At(2.5f, 0f));
            d.Slot = Slot;
            for (int k = 0; k < HalenDuel.NormalRelease; k++) d.Step(InputButtons.Primary);
            d.Step();
            d.Run(10);
            d.Other = TestWorlds.B.With(InputButtons.Guard);
            d.RunUntil(() => HitOn(d) != null, 50);
            DamageEvent hit = HitOn(d);
            Assert.Equal(GuardOutcome.Guarded, hit.Resolution.Guard);
            Assert.Equal(20f, hit.Dealt, 3);
        }

        // ---- The SPL-074-delivery exclusive group --------------------------------------------------------------

        [Fact]
        [Verifies("AUG-435", "AUG-436")]
        public void Wake_And_Fan_Share_The_Delivery_Group_And_Cannot_Coexist()
        {
            Catalogue cat = HalenTestKit.Catalogue;
            Assert.Equal("SPL-074-delivery", cat.Augment(HammerWake.Id).ExclusiveGroup);
            Assert.Equal("SPL-074-delivery", cat.Augment(HammerFan.Id).ExclusiveGroup);
            Assert.ThrowsAny<InvalidOperationException>(() => HalenTestKit.Form(WaterHammer.Id, HalenTestKit.Aug(HammerWake.Id), HalenTestKit.Aug(HammerFan.Id)));
        }
    }
}
