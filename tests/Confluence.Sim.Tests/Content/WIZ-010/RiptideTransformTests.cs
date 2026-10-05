using System.Linq;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Time;
using Confluence.Reactions;
using Confluence.Reactions.Grid;
using Confluence.Sim.Casting;
using Confluence.Sim.Content.Wizards.HalenTide;
using Confluence.Sim.Duel;
using Confluence.Sim.Fields;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;

namespace Confluence.Sim.Tests.Content.Halen
{
    /// <summary>AUG-443 Riptide Fork and AUG-444 Riptide Valve on SPL-078 Riptide Lane, cast through WIZ-010's grammar.</summary>
    public class RiptideTransformTests
    {
        private static HalenDuel With(params ActiveAugment[] augments) => HalenTestKit.Duel(HalenTestKit.Halen(null, augments));

        /// <summary>A stream from (0, -3) along +Z with player 1 parked far away.</summary>
        private static HalenDuel Stream(params ActiveAugment[] augments)
        {
            HalenDuel d = RiptideLaneTests.Stream(new Vec3(8f, 0f, 8f), With(augments));
            return d;
        }

        private static Vec3 Along(Vec2 arrow, float meters) => new Vec3(arrow.X * meters, 0f, -3f + arrow.Y * meters);

        // ---- AUG-443 Riptide Fork ------------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 6f)]
        [InlineData(2, 8f)]
        [Verifies("AUG-443")]
        public void The_Stream_Splits_Into_Two_Lanes_From_One_Junction_With_Water_Shared(int rank, float length)
        {
            HalenDuel d = Stream(HalenTestKit.Aug(RiptideFork.Id, rank));
            StreamCurrentEntity current = RiptideLaneTests.Current(d);
            Assert.Equal(2, current.Lanes.Count);
            Assert.Equal(2, d.World.Owned.Entities.Count(e => e.DefinitionId == RiptideLane.Id)); // each lane its own field
            Vec2 left = current.Arrow(0);
            Vec2 right = current.Arrow(1);
            Assert.Equal(2f * HalenRules.ForkHalfAngleDeg, CMath.AngleBetweenDeg(left, right), 1);
            Assert.Equal(HalenRules.ForkHalfAngleDeg, CMath.AngleBetweenDeg(left, new Vec2(0f, 1f)), 1);

            foreach (Vec2 arrow in new[] { left, right })
            {
                Assert.Equal(1, d.Units(Along(arrow, 3f)).Water);            // "Water 2 shared": 1 per lane
                Assert.Equal(1, d.Units(Along(arrow, length - 0.4f)).Water);
                Assert.Equal(0, d.Units(Along(arrow, length + 0.5f)).Water);
            }

            Assert.Equal(0, d.Units(new Vec3(0f, 0f, 3f)).Water);            // nothing straight down the old 10 m line
        }

        [Fact]
        [Verifies("AUG-443")]
        public void Each_Lane_Gives_10_Percent_With_Its_Arrow_And_Never_15()
        {
            foreach (int lane in new[] { 0, 1 })
            {
                HalenDuel d = Stream(HalenTestKit.Aug(RiptideFork.Id, 1));
                Vec2 arrow = RiptideLaneTests.Current(d).Arrow(lane);
                float yaw = CMath.YawOf(arrow.ToXZ());
                TestWorlds.Place(d.World, 1, Along(arrow, 1.5f), yaw);
                d.Other = TestWorlds.Face(yaw).Moving(0f, 0.6f);
                d.Run(20);
                Assert.True(RiptideLaneTests.Current(d).IsBoosted(1));
                Assert.Equal(0.10f, d.P1.Stats.MovementBonus, 3); // drawback "Original +15% bonus removed"
            }

            // The straight-ahead route of the unforked stream gives nothing (it is dry between the lanes).
            HalenDuel ahead = Stream(HalenTestKit.Aug(RiptideFork.Id, 2));
            TestWorlds.Place(ahead.World, 1, new Vec3(0f, 0f, 3f), 0f);
            ahead.Other = TestWorlds.Face(0f).Moving(0f, 0.6f);
            ahead.Run(20);
            Assert.False(RiptideLaneTests.Current(ahead).IsBoosted(1));
        }

        [Fact]
        [Verifies("AUG-443")]
        public void Freezing_The_Junction_Cuts_The_Bonus_There()
        {
            // Counterplay "Use either lane or freeze junction".
            HalenDuel d = Stream(HalenTestKit.Aug(RiptideFork.Id, 1));
            d.Deposit(Material.Frost, 2, Footprint.Circle(new Vec2(0f, -3f), 1.6f));
            d.Step();
            Assert.True(d.Reactions.HasIceAt(new Vec3(0f, 0f, -2.5f)));
            Vec2 arrow = RiptideLaneTests.Current(d).Arrow(0);
            float yaw = CMath.YawOf(arrow.ToXZ());
            TestWorlds.Place(d.World, 1, Along(arrow, 0.3f), yaw);
            d.Other = TestWorlds.Face(yaw).Moving(0f, 0.6f);
            d.Run(15);
            Assert.False(RiptideLaneTests.Current(d).IsBoosted(1)); // the frozen junction carries nobody

            // The far part of the lane is still liquid: the bonus resumes there.
            d.RunUntil(() => RiptideLaneTests.Current(d).IsBoosted(1), 180);
            Assert.True(RiptideLaneTests.Current(d).IsBoosted(1));
            Assert.True(Vec3.DistanceXZ(d.P1.Position, new Vec3(0f, 0f, -3f)) > 1.5f);
        }

        // ---- AUG-444 Riptide Valve -----------------------------------------------------------------------------

        [Fact]
        [Verifies("AUG-444")]
        public void Lane_Length_10_Becomes_8_And_The_Recast_Is_A_Pending_State()
        {
            HalenDuel d = Stream(HalenTestKit.Aug(RiptideValve.Id, 1));
            Assert.True(d.P0.Caster.Loadout.FormFor(RiptideLane.Id).HasTag("recast"));
            Assert.Equal(2, d.Units(new Vec3(0f, 0f, 4.6f)).Water);
            Assert.Equal(0, d.Units(new Vec3(0f, 0f, 5.6f)).Water);
            Assert.True(RiptideLaneTests.Current(d).AcceptsRecast);
        }

        [Fact]
        [Verifies("AUG-444")]
        public void The_Recast_Reverses_The_Arrow_Once_For_5_Mana_Without_Extending_Lifetime()
        {
            HalenDuel d = Stream(HalenTestKit.Aug(RiptideValve.Id, 1));
            SpellEvent created = d.SpellEvents(SpellEventKind.FieldCreated).Single();
            float mana = d.P0.Resources.Mana;
            d.Halen = TestWorlds.Face(90f); // rank 1 always reverses, whatever the aim
            d.Recast();
            Assert.Equal(mana - 5f, d.P0.Resources.Mana, 1); // "recast 5 mana"
            StreamCurrentEntity current = RiptideLaneTests.Current(d);
            Assert.Equal(0f, Vec2.Distance(new Vec2(0f, -1f), current.Arrow(0)), 3);
            Assert.Equal(180f, d.Tide(TideEventKind.StreamTurned).Single().Amount);
            Assert.False(current.AcceptsRecast);

            // Counterplay "Read new arrow and claim route": downstream is now -Z.
            TestWorlds.Place(d.World, 1, new Vec3(0f, 0f, 3f), 180f);
            d.Other = TestWorlds.Face(180f).Moving(0f, 0.6f);
            d.Run(20);
            Assert.True(current.IsBoosted(1));
            Assert.Equal(0.15f, d.P1.Stats.MovementBonus, 3);

            // Once only, and no lifetime extension.
            float after = d.P0.Resources.Mana;
            d.Recast();
            Assert.Single(d.Tide(TideEventKind.StreamTurned));
            Assert.True(d.P0.Resources.Mana >= after - 1e-3f);
            d.RunUntil(() => d.SpellEvents(SpellEventKind.FieldEnded).Any(), Ticks.FromSeconds(7.0));
            Assert.InRange(d.SpellEvents(SpellEventKind.FieldEnded).Single().Tick, created.Tick + created.Ticks - 1, created.Tick + created.Ticks + 1);
        }

        [Theory]
        [InlineData(90f, 90f)]
        [InlineData(270f, 90f)]
        [InlineData(180f, 180f)]
        [Verifies("AUG-444")]
        public void Rank_2_May_Turn_The_Arrow_90_Degrees_Toward_The_Aim_Instead(float aimYaw, float turned)
        {
            HalenDuel d = Stream(HalenTestKit.Aug(RiptideValve.Id, 2));
            d.Halen = TestWorlds.Face(aimYaw);
            d.Recast();
            Assert.Equal(turned, d.Tide(TideEventKind.StreamTurned).Single().Amount);
            Vec3 wanted = CMath.ForwardFromYaw(aimYaw);
            Assert.Equal(0f, Vec2.Distance(new Vec2(wanted.X, wanted.Z), RiptideLaneTests.Current(d).Arrow(0)), 2);
        }

        [Theory]
        [InlineData(90f)]
        [InlineData(270f)]
        [Verifies("AUG-444")]
        public void Rank_2_Turns_The_Lane_Itself_So_Travel_Along_The_New_Arrow_Gets_The_Bonus(float aimYaw)
        {
            // The 8 m lane runs from (0, -3) to (0, 5) along +Z, centered on (0, 1). Turned 90 degrees it runs across the
            // old line through the same center, keeps Water 2 and its remaining lifetime, and holds one field slot.
            HalenDuel d = Stream(HalenTestKit.Aug(RiptideValve.Id, 2));
            SpellEvent created = d.SpellEvents(SpellEventKind.FieldCreated).Single();
            d.Run(30);
            Vec3 center = RiptideLaneTests.Current(d).Lanes[0].Center;
            Assert.Equal(0f, Vec3.DistanceXZ(center, new Vec3(0f, 0f, 1f)), 0); // about (0, 1)
            d.Halen = TestWorlds.Face(aimYaw);
            d.Recast();
            d.Step();
            StreamCurrentEntity current = RiptideLaneTests.Current(d);
            Vec2 arrow = current.Arrow(0);
            Vec3 wanted = CMath.ForwardFromYaw(aimYaw);
            Assert.Equal(0f, Vec2.Distance(new Vec2(wanted.X, wanted.Z), arrow), 2);
            Assert.Equal(1, d.World.Owned.Entities.Count(e => e.Active && e.DefinitionId == RiptideLane.Id));
            Assert.Equal(0f, Vec3.DistanceXZ(current.Lanes[0].Center, center), 2); // turned about its own center

            Vec3 Across(float meters) => new Vec3(center.X + arrow.X * meters, 0f, center.Z + arrow.Y * meters);
            Assert.Equal(2, d.Units(Across(3.5f)).Water);   // the turned lane, 8 m long
            Assert.Equal(2, d.Units(Across(-3.5f)).Water);
            Assert.Equal(2, d.Units(Across(0f)).Water);
            Assert.Equal(0, d.Units(Across(4.6f)).Water);
            Assert.Equal(0, d.Units(new Vec3(0f, 0f, -2.5f)).Water); // the old orientation is gone
            Assert.Equal(0, d.Units(new Vec3(0f, 0f, 4.5f)).Water);

            // Counterplay "Read new arrow and claim route": a body travelling along the new arrow over the turned lane, well
            // away from the old 2 m crossing, moves with the current (+15%).
            TestWorlds.Place(d.World, 1, Across(-3.2f), aimYaw);
            d.Other = TestWorlds.Face(aimYaw).Moving(0f, 0.6f);
            d.Run(15);
            Assert.True(current.IsBoosted(1));
            Assert.Equal(0.15f, d.P1.Stats.MovementBonus, 3);
            Assert.True(Vec3.DistanceXZ(d.P1.Position, center) > 1.2f);

            // The turned lane ends when the original would have ("without extending lifetime").
            d.RunUntil(() => !current.Alive, Ticks.FromSeconds(7.0));
            SpellEvent ended = d.SpellEvents(SpellEventKind.FieldEnded).Last();
            Assert.InRange(ended.Tick, created.Tick + created.Ticks - 1, created.Tick + created.Ticks + 1);
            Assert.Equal(0f, d.P1.Stats.MovementBonus, 3);
        }

        [Fact]
        [Verifies("AUG-443", "AUG-444")]
        public void Fork_And_Valve_Combine_Shorter_Lanes_Both_Turn()
        {
            HalenDuel d = Stream(HalenTestKit.Aug(RiptideFork.Id, 1), HalenTestKit.Aug(RiptideValve.Id, 1));
            StreamCurrentEntity current = RiptideLaneTests.Current(d);
            Vec2 a = current.Arrow(0);
            Vec2 b = current.Arrow(1);
            Assert.Equal(1, d.Units(Along(a, 4.4f)).Water);   // 6 m x 0.8 = 4.8 m
            Assert.Equal(0, d.Units(Along(a, 5.4f)).Water);
            d.Recast();
            Assert.Equal(0f, Vec2.Distance(new Vec2(-a.X, -a.Y), current.Arrow(0)), 3);
            Assert.Equal(0f, Vec2.Distance(new Vec2(-b.X, -b.Y), current.Arrow(1)), 3);
        }
    }
}
