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
using Confluence.Sim.Fields;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;

namespace Confluence.Sim.Tests.Content.Halen
{
    /// <summary>
    /// SPL-078 Riptide Lane through WIZ-010's grammar: "Create a 10 m-long, 2 m-wide shallow stream for 6 s; everyone moving
    /// with its arrow gets +15% speed." "Water 2 along lane; crosswise travel has no speed bonus; remains connected for
    /// electricity." 22 mana, 11 s, 0.5 s, 20 m.
    /// </summary>
    public class RiptideLaneTests
    {
        internal const int Slot = 5;

        /// <summary>A lane from (0, -3) along +Z; player 1 stands at the given point facing +Z.</summary>
        internal static HalenDuel Stream(Vec3 target, HalenDuel d = null)
        {
            d = d ?? HalenTestKit.Duel();
            d.AimAt(new Vec3(0f, 0f, -3f));
            d.Cast(Slot);
            d.RunUntil(() => d.SpellEvents(SpellEventKind.FieldCreated).Any(), 50);
            TestWorlds.Place(d.World, 1, target, 0f);
            d.Other = TestWorlds.Face(0f);
            d.Step();
            return d;
        }

        internal static StreamCurrentEntity Current(HalenDuel d) => d.Runtime.Entities.OfType<StreamCurrentEntity>().SingleOrDefault();

        internal static float Speed(DuelPlayer p) => new Vec2(p.Motor.Velocity.X, p.Motor.Velocity.Z).Length();

        [Fact]
        [Verifies("SPL-078")]
        public void It_Creates_A_10_By_2_Meter_Stream_Of_Water_2_For_6_Seconds()
        {
            HalenDuel d = HalenTestKit.Duel();
            d.AimAt(new Vec3(0f, 0f, -3f));
            CastDuelEvent commit = d.Cast(Slot);
            Assert.Equal(22f, commit.Cast.Mana, 3);
            Assert.Equal(Ticks.FromSeconds(11.0), d.P0.Caster.CooldownRemainingTicks(RiptideLane.Id));
            d.RunUntil(() => d.SpellEvents(SpellEventKind.FieldCreated).Any(), 50);
            SpellEvent created = d.SpellEvents(SpellEventKind.FieldCreated).Single();
            Assert.Equal(commit.Tick + Ticks.FromSeconds(0.5), created.Tick);
            Assert.Equal(Ticks.FromSeconds(6.0), created.Ticks);
            Assert.Equal(RiptideLane.Id, d.World.Owned.Entities.Single().DefinitionId);
            d.Step();
            FieldEntity lane = Current(d).Lanes.Single();
            Footprint expected = Footprint.Lane(new Vec2(0f, -3f), new Vec2(0f, 7f), 2f);
            var cells = new List<int>();
            d.Reactions.Grid.Rasterize(expected, cells);
            Assert.All(cells, c => Assert.Equal(2, d.Reactions.UnitsAtCell(c).Water));
            Assert.Equal(2 * cells.Count, d.Total(Material.Water, Footprint.Circle(new Vec2(0f, 2f), 7f)));
            Assert.True(lane.Contains(new Vec3(0.9f, 0f, 6.9f)));
            Assert.False(lane.Contains(new Vec3(1.2f, 0f, 2f)));
            Assert.Equal(new Vec2(0f, 1f), Current(d).Arrow(0));
        }

        [Fact]
        [Verifies("SPL-078")]
        public void Everyone_Moving_With_Its_Arrow_Gets_15_Percent_Speed()
        {
            HalenDuel d = Stream(new Vec3(0f, 0f, -2f));
            d.Other = TestWorlds.Face(0f).Moving(0f, 1f); // forward = +Z, the arrow
            d.Run(20);
            Assert.True(Current(d).IsBoosted(1));
            Assert.Equal(0.15f, d.P1.Stats.MovementBonus, 3);
            Assert.Equal(GlobalRules.Movement.WalkSpeed * 1.15f, Speed(d.P1), 2);
            Assert.Contains(d.Tide(TideEventKind.StreamBonus), e => e.TargetPlayer == 1 && e.Amount == 1f);

            // The owner is not exempt (neutral terrain): Halen walking along it is carried too.
            TestWorlds.Place(d.World, 0, new Vec3(-0.5f, 0f, -2.5f), 0f);
            d.Halen = TestWorlds.Face(0f).Moving(0f, 1f);
            d.Run(15);
            Assert.True(Current(d).IsBoosted(0));
        }

        [Fact]
        [Verifies("SPL-078")]
        public void Crosswise_Or_Upstream_Travel_Gets_Nothing()
        {
            HalenDuel cross = Stream(new Vec3(-0.6f, 0f, 2f));
            cross.Other = TestWorlds.Face(0f).Moving(1f, 0f); // strafe across the 2 m width
            cross.Run(8);
            Assert.False(Current(cross).IsBoosted(1));
            Assert.Equal(0f, cross.P1.Stats.MovementBonus, 3);

            HalenDuel up = Stream(new Vec3(0f, 0f, 5f));
            up.Other = TestWorlds.Face(0f).Moving(0f, -1f); // against the arrow
            up.Run(20);
            Assert.False(Current(up).IsBoosted(1));
            Assert.Equal(GlobalRules.Movement.WalkSpeed, Speed(up.P1), 2);
        }

        [Fact]
        [Verifies("SPL-078")]
        public void The_Bonus_Stays_Inside_The_20_Percent_Movement_Cap()
        {
            HalenDuel d = Stream(new Vec3(0f, 0f, -2f));
            d.P1.Stats.Add(StatKind.MovementBonus, 0.10f, "EQP-TEST");
            d.Other = TestWorlds.Face(0f).Moving(0f, 1f);
            d.Run(20);
            Assert.True(Current(d).IsBoosted(1));
            Assert.Equal(GlobalRules.Modifiers.MovementBonusCap, d.P1.Stats.MovementBonus, 3); // 0.25 raw, 0.20 effective
            Assert.Equal(GlobalRules.Movement.WalkSpeed * 1.2f, Speed(d.P1), 2);
        }

        [Fact]
        [Verifies("SPL-078")]
        public void Freezing_Its_Route_Removes_The_Bonus_There_And_Leaving_Ends_It()
        {
            // Counterplay "freeze/disrupt its route": the bonus follows the water.
            HalenDuel d = Stream(new Vec3(0f, 0f, -2f));
            d.Deposit(Material.Frost, 2, Footprint.Lane(new Vec2(0f, 1f), new Vec2(0f, 7f), 2.5f));
            d.Step();
            Assert.True(d.Reactions.HasIceAt(new Vec3(0f, 0f, 3f)));
            d.Other = TestWorlds.Face(0f).Moving(0f, 1f);
            d.Run(15);
            Assert.True(Current(d).IsBoosted(1));                  // still in liquid water before the ice
            d.RunUntil(() => d.P1.Position.Z > 2f, 60);
            d.Run(2);
            Assert.False(Current(d).IsBoosted(1));                 // on the frozen part
            Assert.Equal(0f, d.P1.Stats.MovementBonus, 3);

            HalenDuel leave = Stream(new Vec3(0f, 0f, -2f));
            leave.Other = TestWorlds.Face(0f).Moving(0f, 1f);
            leave.Run(15);
            Assert.True(Current(leave).IsBoosted(1));
            TestWorlds.Place(leave.World, 1, new Vec3(4f, 0f, 0f), 0f);
            leave.Run(3);
            Assert.False(Current(leave).IsBoosted(1));
            Assert.Equal(0f, leave.P1.Stats.MovementBonus, 3);
        }

        [Fact]
        [Verifies("SPL-078")]
        public void It_Remains_Connected_For_Electricity_And_The_Bonus_Ends_With_It()
        {
            HalenDuel d = Stream(new Vec3(0f, 0f, 2f));
            Vec3 contact = new Vec3(0f, 0f, -2.75f);
            d.Reactions.Discharge(new ElectricDischarge { Point = contact, ReactionDamage = 10f, Owner = 1, SourceEventId = 0 });
            d.Step();
            var charged = new List<int>();
            for (int i = 0; i < d.Reactions.Grid.CellCount; i++)
            {
                if (d.Reactions.IsChargedAt(d.Reactions.Grid.CellCenter(i).ToXZ())) charged.Add(i);
            }

            Footprint lane = Footprint.Lane(new Vec2(0f, -3f), new Vec2(0f, 7f), 2f);
            Assert.Equal(GlobalRules.World.WetChargeMaxCells, charged.Count); // the strip carries the full 12-cell charge
            Assert.All(charged, c => Assert.True(lane.Contains(d.Reactions.Grid.CellCenter(c))));

            HalenDuel ends = Stream(new Vec3(0f, 0f, -2f));
            ends.Other = TestWorlds.Face(0f).Moving(0f, 0.5f); // a slow stroll keeps the target in the lane
            ends.Run(20);
            Assert.True(Current(ends).IsBoosted(1));
            ends.RunUntil(() => Current(ends) == null, Ticks.FromSeconds(6.5));
            Assert.Null(Current(ends));
            Assert.Equal(0f, ends.P1.Stats.MovementBonus, 3);
        }
    }
}
