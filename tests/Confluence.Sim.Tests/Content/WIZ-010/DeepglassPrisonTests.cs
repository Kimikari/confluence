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
using Confluence.Sim.Input;
using Confluence.Sim.Spells;
using Confluence.Sim.Status;
using Confluence.Sim.Tests.Spells;

namespace Confluence.Sim.Tests.Content.Halen
{
    /// <summary>
    /// SPL-079 Deepglass Prison through WIZ-010's grammar: "22 m/s bubble projectile roots a hit target for 0.6 s; then
    /// applies 20% slow for 1.5 s." "26" physical; "Water 2 at impact; root obeys global immunity; cannot float or imprison
    /// indefinitely." 32 mana, 17 s, 0.65 s, 18 m.
    /// </summary>
    public class DeepglassPrisonTests
    {
        internal static HalenDuel Fire(HalenDuel d = null, bool mastery = false)
        {
            d = d ?? HalenTestKit.With(DeepglassPrison.Id);
            d.AimAtOther();
            d.Cast(0, mastery: mastery);
            return d;
        }

        internal static DamageEvent HitOn(HalenDuel d) => d.DamageTo(1, DeepglassPrison.Id).SingleOrDefault();

        [Fact]
        [Verifies("SPL-079")]
        public void A_Hit_Deals_26_Roots_For_06_Seconds_And_Then_Slows_20_Percent_For_15_Seconds()
        {
            HalenDuel d = Fire();
            d.RunUntil(() => HitOn(d) != null, 120);
            DamageEvent hit = HitOn(d);
            Assert.Equal(26f, hit.Dealt, 3);
            Assert.Equal(DamageKind.Direct, hit.Kind);
            Assert.Equal(DamageChannel.Physical, hit.Resolution.Channels.Single().Channel);
            StatusInstance root = d.P1.Statuses.Find(StatusKind.Root, DeepglassPrison.Id);
            Assert.NotNull(root);
            Assert.Equal(Ticks.FromSeconds(0.6), root.ExpireTick - root.AppliedTick);
            Assert.Null(d.P1.Statuses.Find(StatusKind.Slow, DeepglassPrison.Id)); // the slow comes after the root

            // Rooted: walking does nothing and the body stays on the ground (no floating).
            d.Other = TestWorlds.B.Moving(0f, 1f);
            Vec3 at = d.P1.Position;
            d.Run(20);
            Assert.Equal(0f, Vec3.DistanceXZ(at, d.P1.Position), 2);
            Assert.True(d.P1.Motor.Grounded);
            Assert.Equal(0f, d.P1.Position.Y, 3);

            d.RunUntil(() => d.P1.Statuses.Find(StatusKind.Slow, DeepglassPrison.Id) != null, 30);
            StatusInstance slow = d.P1.Statuses.Find(StatusKind.Slow, DeepglassPrison.Id);
            Assert.Equal(root.ExpireTick, slow.AppliedTick);
            Assert.Equal(0.2f, slow.Intensity, 3);
            Assert.Equal(Ticks.FromSeconds(1.5), slow.ExpireTick - slow.AppliedTick);
            Assert.Single(d.Tide(TideEventKind.PrisonReleased));
        }

        [Fact]
        [Verifies("SPL-079")]
        public void The_Root_Obeys_Hard_Control_Immunity_And_Grants_It()
        {
            // Counterplay "trigger its control while protected by hard-control immunity".
            HalenDuel d = HalenTestKit.With(DeepglassPrison.Id);
            d.World.ApplyStatus(1, StatusApplication.Of(StatusKind.Stun, "SPL-TEST", 0, 6));
            d.Run(8); // the stun ends: 2 s of immunity
            Assert.True(d.P1.Statuses.IsControlImmune(d.World.Tick));
            Fire(d);
            d.RunUntil(() => HitOn(d) != null, 120);
            Assert.Equal(26f, HitOn(d).Dealt, 3);                                   // damage still lands
            Assert.Null(d.P1.Statuses.Find(StatusKind.Root, DeepglassPrison.Id));   // no root, nothing queued
            Assert.Contains(d.Events<StatusEvent>(), e => e.Kind == StatusKind.Root && e.Change == StatusChange.BlockedByImmunity);
            d.Step();
            Assert.NotNull(d.P1.Statuses.Find(StatusKind.Slow, DeepglassPrison.Id)); // the slow is not hard control

            // After a root that did apply, the 2 s immunity refuses the next hard control.
            HalenDuel e = Fire();
            e.RunUntil(() => HitOn(e) != null, 120);
            e.RunUntil(() => !e.P1.Statuses.IsRooted, 60);
            e.Step();
            Assert.True(e.P1.Statuses.IsControlImmune(e.World.Tick));
            Assert.False(e.World.ApplyStatus(1, StatusApplication.Of(StatusKind.Root, "SPL-TEST", 0, 30)).Succeeded);
        }

        [Fact]
        [Verifies("SPL-079")]
        public void It_Is_Telegraphed_By_The_Hard_Control_Cue_And_Flies_22_Meters_Per_Second()
        {
            HalenDuel d = HalenTestKit.With(DeepglassPrison.Id);
            Assert.True(d.P0.Caster.Loadout.FormFor(DeepglassPrison.Id).Castable.RequiresAnticipatoryCue); // spec L96
            d.AimAtOther();
            CastDuelEvent commit = d.Cast(0);
            Assert.Equal(32f, commit.Cast.Mana, 3);
            Assert.Equal(Ticks.FromSeconds(17.0), d.P0.Caster.CooldownRemainingTicks(DeepglassPrison.Id));
            SpellEvent cue = d.SpellEvents(SpellEventKind.Telegraph).Single();
            Assert.True(cue.Tick < commit.Tick);
            d.RunUntil(() => d.SpellEvents(SpellEventKind.ProjectileLaunched).Any(), 60);
            SpellEvent launch = d.SpellEvents(SpellEventKind.ProjectileLaunched).Single();
            Assert.Equal(commit.Tick + Ticks.FromSeconds(0.65), launch.Tick);
            Assert.Equal(22f, launch.Amount, 3);
        }

        [Fact]
        [Verifies("SPL-079")]
        public void Water_2_Lands_At_The_Impact_And_Its_Range_Is_18_Meters()
        {
            HalenDuel d = Fire();
            d.RunUntil(() => HitOn(d) != null, 120);
            Assert.Equal(2, d.Total(Material.Water, Footprint.Circle(new Vec2(0f, 6f), 1f)));
            Assert.Equal(2, d.Total(Material.Water, Footprint.Circle(new Vec2(0f, 6f), 5f)));

            HalenDuel far = HalenTestKit.Duel(HalenTestKit.Halen(new[] { DeepglassPrison.Id }), layout: TestWorlds.Flat(boundaryRadius: 40f, floorHalf: 40f));
            TestWorlds.Place(far.World, 1, new Vec3(0f, 0f, 20f), 180f);
            far.Halen = TestWorlds.A;
            far.Cast(0);
            far.Run(90);
            Assert.Equal(18f, far.SpellEvents(SpellEventKind.ProjectileExpired).Single().Amount, 2);
            Assert.Empty(far.DamageTo(1));
        }

        [Fact]
        [Verifies("SPL-079")]
        public void Dodging_The_Bubble_Avoids_The_Prison()
        {
            HalenDuel d = HalenTestKit.With(DeepglassPrison.Id);
            d.AimAtOther();
            d.Cast(0);
            d.Other = TestWorlds.B.Moving(1f, 0f); // strafe away during the 0.65 s windup
            d.Run(90);
            Assert.Null(HitOn(d));
            Assert.False(d.P1.Statuses.IsRooted);
        }
    }
}
