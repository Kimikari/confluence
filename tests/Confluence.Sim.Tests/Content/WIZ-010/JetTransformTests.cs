using System.Linq;
using Confluence.Casting;
using Confluence.Core.Arena;
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
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;

namespace Confluence.Sim.Tests.Content.Halen
{
    /// <summary>AUG-433 Jet Bank and AUG-434 Jet Mist on SPL-073 Pressure Jet, cast through WIZ-010's grammar.</summary>
    public class JetTransformTests
    {
        // A wall whose face is the plane x = 3 along the duel's axis: a jet aimed at (3, 1.35, 0) banks to the target.
        private static Solid BankWall() => SpellTestKit.Wall(60, new Vec3(3.25f, 0f, 0f), new Vec3(0.5f, 3f, 14f));

        private static HalenDuel Banker(int rank, bool wet)
        {
            HalenDuel d = HalenTestKit.Duel(HalenTestKit.Halen(null, HalenTestKit.Aug(JetBank.Id, rank)), layout: HalenTestKit.Layout(BankWall()));
            if (wet) d.Deposit(Material.Water, 1, Footprint.Rectangle(new Vec2(2.75f, 0f), new Vec2(0.25f, 1.5f), 0f), 6f, 0);
            d.Step();
            d.AimAt(new Vec3(3f, 1.35f, 0f));
            return d;
        }

        [Theory]
        [InlineData(1, 24f)]
        [InlineData(2, 28f)]
        [Verifies("AUG-433")]
        public void A_Jet_Banks_Once_From_A_Wet_Wall_At_The_Bank_Damage_With_The_Normal_Push(int rank, float bankDamage)
        {
            HalenDuel d = Banker(rank, true);
            int before = d.Total(Material.Water, Footprint.Rectangle(new Vec2(2.75f, 0f), new Vec2(0.25f, 1.5f), 0f));
            d.Cast(0);
            d.RunUntil(() => d.DamageTo(1, PressureJet.Id).Any(), 90);
            DamageEvent hit = d.DamageTo(1, PressureJet.Id).Single();
            Assert.Equal(bankDamage, hit.Dealt, 3);
            Assert.Single(d.SpellEvents(SpellEventKind.ProjectileBounced));
            TideEvent bank = d.Tide(TideEventKind.WetWallBank).Single();
            Assert.Equal(1.5f, hit.Resolution.Knockback, 3); // "normal push remains 1.5 m"
            d.Step();
            Assert.Equal(before - 1, d.Total(Material.Water, Footprint.Rectangle(new Vec2(2.75f, 0f), new Vec2(0.25f, 1.5f), 0f))); // "consumes Water 1 on wall"
            Assert.True(d.P0.Caster.Loadout.FormFor(PressureJet.Id).HasTag("ricochet"));
            Assert.True(bank.Position.X > 2.4f);
        }

        [Fact]
        [Verifies("AUG-433")]
        public void A_Dry_Wall_Or_A_Dried_One_Takes_The_Jet_And_The_Direct_Hit_Is_28()
        {
            HalenDuel dry = Banker(1, false);
            dry.Cast(0);
            dry.Run(90);
            Assert.Empty(dry.SpellEvents(SpellEventKind.ProjectileBounced));
            Assert.Equal(60, dry.SpellEvents(SpellEventKind.ProjectileImpact).Single().SolidId);
            Assert.Empty(dry.DamageTo(1));

            // Counterplay "Dry bank surface": heat on the wet base turns its water to steam before the jet arrives.
            HalenDuel dried = Banker(2, true);
            dried.Deposit(Material.Heat, 2, Footprint.Rectangle(new Vec2(2.75f, 0f), new Vec2(0.25f, 1.5f), 0f), 6f, 1);
            dried.Step();
            dried.Cast(0);
            dried.Run(90);
            Assert.Empty(dried.SpellEvents(SpellEventKind.ProjectileBounced));
            Assert.Empty(dried.DamageTo(1, PressureJet.Id));

            // Drawback "Direct damage 32 becomes 28".
            HalenDuel direct = HalenTestKit.Duel(HalenTestKit.Halen(null, HalenTestKit.Aug(JetBank.Id, 1)));
            direct.AimAtOther();
            direct.Cast(0);
            direct.RunUntil(() => direct.DamageTo(1, PressureJet.Id).Any(), 90);
            Assert.Equal(28f, direct.DamageTo(1, PressureJet.Id).Single().Dealt, 3);
        }

        [Fact]
        [Verifies("AUG-433")]
        public void Sidestepping_The_Reflected_Path_Avoids_The_Bank()
        {
            HalenDuel d = Banker(2, true);
            d.Cast(0);
            d.Other = TestWorlds.B.Moving(1f, 0f);
            d.Run(90);
            Assert.Single(d.SpellEvents(SpellEventKind.ProjectileBounced));
            Assert.Empty(d.DamageTo(1, PressureJet.Id));
        }

        private static HalenDuel Mister(int rank)
        {
            HalenDuel d = HalenTestKit.Duel(HalenTestKit.Halen(null, HalenTestKit.Aug(JetMist.Id, rank)));
            d.AimAt(new Vec3(0f, 0f, 0f)); // a miss into the floor
            d.Cast(0);
            d.RunUntil(() => d.SpellEvents(SpellEventKind.ProjectileImpact).Any(), 60);
            d.Step();
            return d;
        }

        [Theory]
        [InlineData(1, 1f, 1.5f)]
        [InlineData(2, 1.5f, 2f)]
        [Verifies("AUG-434")]
        public void A_Missed_Shot_Leaves_A_Mist_Cloud_At_The_Impact(int rank, float radius, float lifetime)
        {
            HalenDuel d = Mister(rank);
            ObscurationVolume mist = d.Reactions.Volumes.Single();
            Assert.Equal(VolumeKind.Mist, mist.Kind);
            Assert.Equal(0, mist.Owner);
            Assert.Equal(Ticks.FromSeconds(lifetime), mist.ExpiresTick - mist.CreatedTick);
            mist.GetBounds(out Vec2 min, out Vec2 max);
            Assert.InRange(max.X - min.X, 2f * radius - 0.6f, 2f * radius + 0.01f);
            Assert.Single(d.Tide(TideEventKind.MissCloud));
            Assert.Contains(d.World.Owned.Entities, e => e.Owner == 0 && e.DefinitionId.StartsWith("CLOUD:")); // one of Halen's fields
        }

        [Fact]
        [Verifies("AUG-434")]
        public void The_Mist_Blocks_Sight_But_Wets_Nobody_And_Wind_Disperses_It()
        {
            HalenDuel d = Mister(2);
            ObscurationVolume mist = d.Reactions.Volumes.Single();
            mist.GetBounds(out Vec2 min, out Vec2 max);
            Vec2 c = (min + max) * 0.5f;
            Assert.True(d.World.Materials.ObscuringPathLength(new Vec3(c.X - 3f, 1f, c.Y), new Vec3(c.X + 3f, 1f, c.Y)) >= 2f); // concealment
            TestWorlds.Place(d.World, 1, new Vec3(c.X + 1f, 0f, c.Y + 0.25f), 180f); // inside the cloud, off the wet impact cell
            d.Run(30);
            Assert.False(d.P1.Statuses.IsWet); // "no Wet from mist"

            // Counterplay "disperse cloud": a strength-2 gust disperses its front slice (spec L151).
            int columns = mist.ColumnCount;
            d.Reactions.Wind(new WindGust { Area = Footprint.Circle(c, 3f), Direction = new Vec2(1f, 0f), Strength = 2, Owner = 1, SourceEventId = 0 });
            d.Step();
            Assert.True(d.Reactions.Volumes.Count == 0 || d.Reactions.Volumes.Single().ColumnCount < columns);
        }

        [Fact]
        [Verifies("AUG-434")]
        public void A_Hit_Makes_No_Mist_And_Pushes_Only_1_Meter()
        {
            HalenDuel d = HalenTestKit.Duel(HalenTestKit.Halen(null, HalenTestKit.Aug(JetMist.Id, 1)));
            d.AimAtOther();
            d.Cast(0);
            d.RunUntil(() => d.DamageTo(1, PressureJet.Id).Any(), 90);
            DamageEvent hit = d.DamageTo(1, PressureJet.Id).Single();
            Assert.Equal(32f, hit.Dealt, 3);
            Assert.Equal(1f, hit.Resolution.Knockback, 3); // "Push 1.5 becomes 1 m"
            d.Run(30);
            Assert.Empty(d.Reactions.Volumes);
            Assert.Equal(7f, d.P1.Position.Z, 1);
        }

        [Fact]
        [Verifies("AUG-433", "AUG-434")]
        public void Bank_And_Mist_Combine_In_Stage_Order_With_The_Mist_Push_Winning()
        {
            SpellForm form = HalenTestKit.Form(PressureJet.Id, HalenTestKit.Aug(JetMist.Id, 1), HalenTestKit.Aug(JetBank.Id, 2));
            Assert.Equal(new[] { JetBank.Id, JetMist.Id }, form.AppliedTransforms); // delivery before payload (spec L286, L510)
            var jet = (TideProjectileDelivery)form.Delivery;
            Assert.True(jet.BanksOffWetWalls);
            Assert.Equal(1f, form.Payload.Push, 3);
            Assert.Equal(1f, jet.BankPayload.Push, 3);
            Assert.Equal(28f, jet.BankPayload.Direct, 3);
            Assert.Equal(28f, form.Payload.Direct, 3);
            Assert.Equal(1.5f, jet.MissCloudRadius, 3 * 0 + 3 - 3 + 3);
        }
    }
}
