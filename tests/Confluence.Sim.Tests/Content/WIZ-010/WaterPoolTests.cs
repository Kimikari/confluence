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
using Confluence.Reactions.Volumes;
using Confluence.Sim.Casting;
using Confluence.Sim.Combat;
using Confluence.Sim.Content.Wizards.HalenTide;
using Confluence.Sim.Duel;
using Confluence.Sim.Fields;
using Confluence.Sim.Input;
using Confluence.Sim.Spells;
using Confluence.Sim.Status;
using Confluence.Sim.Tests.Spells;

namespace Confluence.Sim.Tests.Content.Halen
{
    /// <summary>
    /// SPL-076 Water Pool through WIZ-010's grammar: "Place a connected 3 m-radius shallow pool for 6 s; entrants are Wet; no
    /// automatic slow." "Water 2; Heat creates steam; Frost freezes it; electricity stays within its connected footprint."
    /// 14 mana, 8 s, 0.4 s, 18 m.
    /// </summary>
    public class WaterPoolTests
    {
        internal const int Slot = 3;

        internal static HalenDuel Placed(Vec3 at, HalenDuel d = null)
        {
            d = d ?? HalenTestKit.Duel();
            d.AimAt(at);
            d.Cast(Slot);
            d.RunUntil(() => d.SpellEvents(SpellEventKind.FieldCreated).Any(), 40);
            d.Step(); // the creation deposit lands at this step's reactions
            return d;
        }

        [Fact]
        [Verifies("SPL-076")]
        public void It_Places_One_3_Meter_Field_Of_Water_2_For_6_Seconds()
        {
            HalenDuel d = HalenTestKit.Duel();
            d.AimAt(Vec3.Zero);
            CastDuelEvent commit = d.Cast(Slot);
            Assert.Equal(14f, commit.Cast.Mana, 3);
            Assert.Equal(Ticks.FromSeconds(8.0), d.P0.Caster.CooldownRemainingTicks(WaterPool.Id));
            d.RunUntil(() => d.SpellEvents(SpellEventKind.FieldCreated).Any(), 40);
            SpellEvent created = d.SpellEvents(SpellEventKind.FieldCreated).Single();
            Assert.Equal(commit.Tick + Ticks.FromSeconds(0.4), created.Tick);
            Assert.Equal(Ticks.FromSeconds(6.0), created.Ticks);
            Assert.Equal(3f, created.Amount, 3);
            Assert.Equal(0f, Vec3.DistanceXZ(created.Position, Vec3.Zero), 1);
            OwnedEntity field = d.World.Owned.Entities.Single();
            Assert.Equal(OwnedEntityKind.Field, field.Kind);
            Assert.Equal(WaterPool.Id, field.DefinitionId);
            Assert.True(field.Neutral);

            d.Step();
            Vec2 c = created.Position.XZ();
            var cells = new List<int>();
            d.Reactions.Grid.Rasterize(Footprint.Circle(c, 3f), cells);
            Assert.All(cells, i => Assert.Equal(2, d.Reactions.UnitsAtCell(i).Water)); // "Water 2"
            Assert.Equal(2 * cells.Count, d.Total(Material.Water, Footprint.Circle(c, 4f)));
            Assert.Equal(0, d.Units((c + new Vec2(3.4f, 0f)).ToXZ()).Water);

            d.RunUntil(() => d.SpellEvents(SpellEventKind.FieldEnded).Any(), Ticks.FromSeconds(6.5));
            Assert.InRange(d.SpellEvents(SpellEventKind.FieldEnded).Single().Tick - created.Tick, Ticks.FromSeconds(6.0), Ticks.FromSeconds(6.0) + 2);
            d.Step();
            Assert.Equal(0, d.Total(Material.Water, Footprint.Circle(c, 4f)));
            Assert.Empty(d.World.Owned.Entities);
        }

        [Fact]
        [Verifies("SPL-076")]
        public void Its_Cells_Form_One_Connected_Disc()
        {
            HalenDuel d = Placed(Vec3.Zero);
            var cells = new List<int>();
            d.Reactions.Grid.Rasterize(Footprint.Circle(Vec2.Zero, 3f), cells);
            var set = new HashSet<int>(cells);
            var seen = new HashSet<int> { cells[0] };
            var queue = new Queue<int>(seen);
            var n = new List<int>();
            while (queue.Count > 0)
            {
                n.Clear();
                d.Reactions.Grid.Neighbors4(queue.Dequeue(), n);
                foreach (int x in n)
                {
                    if (set.Contains(x) && seen.Add(x)) queue.Enqueue(x);
                }
            }

            Assert.Equal(cells.Count, seen.Count); // "connected" (4-connected wet cells, spec L148)
        }

        [Fact]
        [Verifies("SPL-076")]
        public void Entrants_Are_Wet_And_Not_Slowed_And_Leaving_Dries_Them()
        {
            HalenDuel d = Placed(new Vec3(0f, 0f, 1.5f)); // the pool's edge is at z = 4.5, the target stands at z = 6
            Assert.False(d.P1.Statuses.IsWet);
            d.Other = TestWorlds.B.Moving(0f, 1f); // walk from z=6 toward -Z into the pool
            d.RunUntil(() => d.P1.Statuses.IsWet, 60);
            Assert.True(d.P1.Statuses.IsWet);
            Assert.InRange(d.P1.Position.Z, 3.8f, 4.75f);
            d.Run(5);
            Assert.Equal(0f, d.P1.Statuses.StrongestSlow);                                          // "no automatic slow"
            Assert.Equal(GlobalRules.Movement.WalkSpeed, new Vec2(d.P1.Motor.Velocity.X, d.P1.Motor.Velocity.Z).Length(), 2);

            // Counterplay "Leave the marked pool": out of the water the Wet lingers about a second and ends.
            TestWorlds.Place(d.World, 1, new Vec3(8f, 0f, 1.5f), 180f);
            d.Other = TestWorlds.B;
            d.RunSeconds(1.5);
            Assert.False(d.P1.Statuses.IsWet);
        }

        [Fact]
        [Verifies("SPL-076")]
        public void Heat_On_The_Pool_Creates_Steam()
        {
            HalenDuel d = Placed(Vec3.Zero);
            d.Deposit(Material.Heat, 1, Footprint.Circle(Vec2.Zero, 1f));
            d.Step();
            ObscurationVolume steam = d.Reactions.Volumes.Single();
            Assert.Equal(VolumeKind.Steam, steam.Kind);
            Assert.Equal(1, d.Units(Vec3.Zero).Water); // Water 2 - Heat 1 (spec L144)
            Assert.Equal(2, d.Units(new Vec3(2.5f, 0f, 0f)).Water);
        }

        [Fact]
        [Verifies("SPL-076")]
        public void Frost_Freezes_It_And_Ice_Wets_Nobody()
        {
            HalenDuel d = Placed(new Vec3(0f, 0f, 4f));
            TestWorlds.Place(d.World, 1, new Vec3(0f, 0f, 4f), 180f);
            d.Deposit(Material.Frost, 2, Footprint.Circle(new Vec2(0f, 4f), 1.2f));
            d.Step();
            Assert.True(d.Reactions.HasIceAt(new Vec3(0f, 0f, 4f)));              // two frost freeze one water (spec L145)
            Assert.False(d.Reactions.HasIceAt(new Vec3(2.5f, 0f, 4f)));
            d.RunSeconds(1.6);
            Assert.False(d.P1.Statuses.IsWet);                                     // standing on ice, not in water
        }

        [Fact]
        [Verifies("SPL-076")]
        public void Electricity_Stays_Within_Its_Connected_Footprint()
        {
            HalenDuel d = Placed(new Vec3(0f, 0f, 3f));
            TestWorlds.Place(d.World, 1, new Vec3(-2.5f, 0f, 3f), 90f);           // inside the pool near the contact
            Vec3 contact = new Vec3(-2.75f, 0f, 3f);
            d.Reactions.Discharge(new ElectricDischarge { Point = contact, ReactionDamage = 10f, Owner = 1, SourceEventId = 0 });
            d.Step();
            var charged = new List<int>();
            for (int i = 0; i < d.Reactions.Grid.CellCount; i++)
            {
                if (d.Reactions.IsChargedAt(d.Reactions.Grid.CellCenter(i).ToXZ())) charged.Add(i);
            }

            Assert.NotEmpty(charged);
            Assert.True(charged.Count <= GlobalRules.World.WetChargeMaxCells);
            Assert.All(charged, i => Assert.True(Vec2.Distance(d.Reactions.Grid.CellCenter(i), new Vec2(0f, 3f)) <= 3f));
            d.Step();
            Assert.Contains(d.DamageTo(1), e => e.Kind == DamageKind.Environment);

            // A body just outside the pool's dry edge is never charged.
            HalenDuel dry = Placed(new Vec3(0f, 0f, 3f));
            TestWorlds.Place(dry.World, 1, new Vec3(-3.9f, 0f, 3f), 90f);
            dry.Reactions.Discharge(new ElectricDischarge { Point = contact, ReactionDamage = 10f, Owner = 1, SourceEventId = 0 });
            dry.Run(3);
            Assert.DoesNotContain(dry.DamageTo(1), e => e.Kind == DamageKind.Environment);
        }
    }
}
