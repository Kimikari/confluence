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
using Confluence.Sim.Input;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;

namespace Confluence.Sim.Tests.Content.Halen
{
    /// <summary>
    /// SPL-077 Cascade Vault through WIZ-010's grammar: "Launch self toward a visible landing point up to 6 m away and 3 m
    /// higher; no flight or invulnerability." "Water 1 at launch and landing, combined source cap Water 2." 24 mana, 13 s,
    /// 0.3 s, 6 m.
    /// </summary>
    public class CascadeVaultTests
    {
        internal const int Slot = 4;

        internal static HalenDuel Vault(Vec3 aimAt, ArenaLayout layout = null, HalenDuel d = null)
        {
            d = d ?? HalenTestKit.Duel(layout: layout);
            d.AimAt(aimAt);
            d.Cast(Slot);
            return d;
        }

        internal static SpellEvent Landed(HalenDuel d, int maxTicks = 120)
        {
            d.RunUntil(() => d.SpellEvents(SpellEventKind.MovementEnded).Any(), maxTicks);
            return d.SpellEvents(SpellEventKind.MovementEnded).SingleOrDefault();
        }

        [Fact]
        [Verifies("SPL-077")]
        public void It_Launches_Toward_The_Aimed_Point_And_Lands_There_Leaving_Water_At_Both_Ends()
        {
            HalenDuel d = HalenTestKit.Duel();
            d.AimAt(new Vec3(0f, 0f, -1f));
            CastDuelEvent commit = d.Cast(Slot);
            Assert.Equal(24f, commit.Cast.Mana, 3);
            Assert.Equal(Ticks.FromSeconds(13.0), d.P0.Caster.CooldownRemainingTicks(CascadeVault.Id));
            d.RunUntil(() => d.SpellEvents(SpellEventKind.MovementStarted).Any(), 30);
            SpellEvent start = d.SpellEvents(SpellEventKind.MovementStarted).Single();
            Assert.Equal(commit.Tick + Ticks.FromSeconds(0.3), start.Tick);
            Assert.Equal(5f, start.Amount, 1);
            Vec3 launch = start.Position;
            Assert.Equal(1, d.Units(launch).Water); // Water 1 at launch
            Assert.False(d.P0.Motor.Grounded); // launched at release

            SpellEvent end = Landed(d);
            Assert.NotNull(end);
            Assert.True(d.P0.Motor.Grounded);
            Assert.Equal(0f, Vec3.DistanceXZ(d.P0.Position, new Vec3(0f, 0f, -1f)), 1);
            Assert.InRange(end.Tick - start.Tick, Ticks.FromSeconds(0.7), Ticks.FromSeconds(0.95));
            Assert.Equal(1, d.Units(d.P0.Position).Water); // Water 1 at landing
            Assert.Equal(2, d.Total(Material.Water, Footprint.Circle(new Vec2(0f, -3.5f), 4f)));
        }

        [Fact]
        [Verifies("SPL-077")]
        public void The_Landing_Is_At_Most_6_Meters_Away()
        {
            HalenDuel d = Vault(new Vec3(0f, 0f, 8f));
            Landed(d);
            Assert.Equal(6f, Vec3.DistanceXZ(d.P0.Position, new Vec3(0f, 0f, -6f)), 1);
        }

        [Fact]
        [Verifies("SPL-077")]
        public void It_Lands_At_Most_3_Meters_Higher()
        {
            // A visible platform top (below eye height, so the aim sees it) is a legal landing above the launch.
            Solid low = TestWorlds.Box(40, new Vec3(0f, 0f, -1.5f), new Vec3(3f, 1.4f, 2f), SolidKind.Platform);
            HalenDuel up = Vault(new Vec3(0f, 1.4f, -1.5f), HalenTestKit.Layout(low));
            Landed(up);
            Assert.Equal(1.4f, up.P0.Position.Y, 1);

            // A visible point 4.5 m up a steep face is beyond the 3 m rise: the arc tops out at 3 m and never lands there.
            Assert.Equal(3f, ((MovementDelivery)up.P0.Caster.Loadout.FormFor(CascadeVault.Id).Delivery).MaxRise, 3);
            Solid face = TestWorlds.Ramp(41, new Vec3(0f, 0f, -1f), 2f, 3f, 8f);
            HalenDuel tooHigh = Vault(new Vec3(0f, 4.5f, -0.625f), HalenTestKit.Layout(face));
            tooHigh.RunSeconds(2.0);
            Assert.True(tooHigh.P0.Position.Y < 3.5f, "the vault rose " + tooHigh.P0.Position.Y + " m");
        }

        [Fact]
        [Verifies("SPL-077")]
        public void It_Grants_No_Invulnerability_And_The_Fixed_Landing_Can_Be_Attacked()
        {
            HalenDuel d = Vault(new Vec3(0f, 0f, -1f));
            d.RunUntil(() => d.SpellEvents(SpellEventKind.MovementStarted).Any(), 30);
            SpellEvent start = d.SpellEvents(SpellEventKind.MovementStarted).Single();
            Vec3 predicted = start.Position + start.Direction * start.Amount;
            d.Run(10);
            Assert.False(d.P0.Motor.Grounded);
            DamageEvent hit = d.World.ApplyDamage(TestWorlds.Hit(d.World, 1, 0, 25f));
            Assert.Equal(25f, hit.Dealt, 3); // airborne, still fully hittable (spec L82: movement without invulnerability)
            d.Halen = TestWorlds.Face(90f);    // turning mid-arc never moves the landing
            Landed(d);
            Assert.Equal(0f, Vec3.DistanceXZ(d.P0.Position, predicted), 1);
        }

        [Fact]
        [Verifies("SPL-077")]
        public void One_Cast_Adds_At_Most_Water_2_To_A_Cell()
        {
            HalenDuel d = Vault(new Vec3(0.1f, 0f, -5.75f)); // a hop inside the launch cell
            Landed(d);
            Assert.Equal(2, d.Units(d.P0.Position).Water);   // launch 1 + landing 1, the combined source cap
            Assert.Equal(2, d.Total(Material.Water, Footprint.Circle(d.P0.Position.XZ(), 3f)));
        }

        [Fact]
        [Verifies("SPL-077")]
        public void A_Refused_Vault_Leaves_No_Water()
        {
            HalenDuel d = HalenTestKit.Duel();
            d.Halen = TestWorlds.A.Facing(0f, -89f); // straight down: no landing point away from the feet
            d.Cast(Slot);
            d.Run(30);
            Assert.Single(d.SpellEvents(SpellEventKind.MovementRefused));
            Assert.Equal(0, d.Total(Material.Water, Footprint.Circle(d.P0.Position.XZ(), 3f)));
        }
    }
}
