using System.Linq;
using Confluence.Casting;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Casting;
using Confluence.Sim.Combat;
using Confluence.Sim.Content.Wizards.HalenTide;
using Confluence.Sim.Duel;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;
using Confluence.Sim.World;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Content.Halen
{
    /// <summary>AUG-437 Bubble Release and AUG-438 Bubble Steam on SPL-075 Bubble Buckler, cast through WIZ-010's grammar.</summary>
    public class BucklerTransformTests
    {
        private const int Slot = BubbleBucklerTests.Slot;

        private static HalenDuel Raised(string augment, int rank, ArenaLayout layout = null)
        {
            HalenDuel d = HalenTestKit.Duel(HalenTestKit.Halen(null, HalenTestKit.Aug(augment, rank)), layout: layout);
            return BubbleBucklerTests.Raised(d);
        }

        private static DamageEvent ThrownHit(HalenDuel d) => d.DamageTo(1, BubbleBuckler.Id).SingleOrDefault();

        // ---- AUG-437 Bubble Release ----------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 24f)]
        [InlineData(2, 30f)]
        [Verifies("AUG-437")]
        public void The_Recast_Throws_The_Remaining_Shield_As_A_20_Meter_Per_Second_Bubble(int rank, float damage)
        {
            HalenDuel d = Raised(BubbleRelease.Id, rank);
            ShieldEntry entry = d.P0.Shields.Entries.Single();
            Assert.Equal(35f, entry.Amount, 3);                          // drawback "Original shield 45 becomes 35"
            Assert.Contains(SpellTestKit.CastEvents(d.World, CastEventKind.RecastWindowOpened), e => e.SpellId == BubbleBuckler.Id); // a pending state (spec L549)
            BubbleBucklerTests.HitFrom(d, d.P1.Position, 10f);
            d.Step();
            float mana = d.P0.Resources.Mana;
            d.AimAtOther();
            d.Step();
            d.Recast();
            Assert.Equal(0f, d.P0.Shields.Total, 3);                     // "ending guard"
            TideEvent released = d.Tide(TideEventKind.BubbleReleased).Single();
            Assert.Equal(25f, released.Amount, 3);                       // the remaining shield was thrown
            SpellEvent launch = d.SpellEvents(SpellEventKind.ProjectileLaunched).Single();
            Assert.Equal(20f, launch.Amount, 3);
            d.RunUntil(() => ThrownHit(d) != null, 60);
            DamageEvent hit = ThrownHit(d);
            Assert.NotNull(hit);
            Assert.Equal(DamageKind.Direct, hit.Kind);
            Assert.Equal(damage, hit.Dealt, 3);
            Assert.Equal(DamageChannel.Physical, hit.Resolution.Channels.Single().Channel);
            Assert.InRange(hit.Tick - launch.Tick, 34, 40);               // about 11.4 m at 20 m/s
            Assert.Equal(mana, d.P0.Resources.Mana, 1);                  // the recast adds no payment (spec L512)
            Assert.Empty(d.Tide(TideEventKind.BubbleBroken));            // released, not broken: no break water
        }

        [Fact]
        [Verifies("AUG-437")]
        public void The_Thrown_Bubble_Travels_20_Meters_And_Never_Reflects()
        {
            HalenDuel far = HalenTestKit.Duel(HalenTestKit.Halen(null, HalenTestKit.Aug(BubbleRelease.Id, 2)), layout: TestWorlds.Flat(boundaryRadius: 40f, floorHalf: 40f));
            BubbleBucklerTests.Raised(far);
            TestWorlds.Place(far.World, 1, new Vec3(0f, 0f, 17f), 180f);
            far.Halen = TestWorlds.A;
            far.Recast();
            far.Run(80);
            Assert.Equal(20f, far.SpellEvents(SpellEventKind.ProjectileExpired).Single().Amount, 2); // "range 20 m"
            Assert.Empty(far.DamageTo(1));

            // Drawback "no reflection": a reflective wall stops it like any solid.
            Solid mirror = SpellTestKit.Wall(70, new Vec3(0f, 0f, 0f), new Vec3(4f, 3f, 0.5f), 0f, MaterialFlags.Reflective);
            HalenDuel d = Raised(BubbleRelease.Id, 1, HalenTestKit.Layout(mirror));
            d.Halen = TestWorlds.A;
            d.Recast();
            d.Run(30);
            Assert.Equal(70, d.SpellEvents(SpellEventKind.ProjectileImpact).Single().SolidId);
            Assert.Empty(d.SpellEvents(SpellEventKind.ProjectileBounced));
        }

        [Fact]
        [Verifies("AUG-437")]
        public void Dodging_The_Bubble_Or_Baiting_The_Release_Wastes_It()
        {
            // Counterplay "dodge bubble": a sidestep clears the 20 m/s line.
            HalenDuel d = Raised(BubbleRelease.Id, 2);
            d.AimAtOther();
            d.Step();
            d.Recast();
            d.Other = TestWorlds.B.Moving(1f, 0f);
            d.Run(60);
            Assert.Null(ThrownHit(d));

            // Counterplay "Bait release": once the shield is broken the recast has nothing to throw.
            HalenDuel bait = Raised(BubbleRelease.Id, 1);
            BubbleBucklerTests.HitFrom(bait, bait.P1.Position, 50f);
            bait.Run(2);
            Assert.Single(bait.Tide(TideEventKind.BubbleBroken));
            bait.AimAtOther();
            bait.Recast();
            bait.Run(40);
            Assert.Empty(bait.Tide(TideEventKind.BubbleReleased));
            Assert.Empty(bait.SpellEvents(SpellEventKind.ProjectileLaunched));
            Assert.Null(ThrownHit(bait));
        }

        [Fact]
        [Verifies("AUG-437")]
        public void A_Guard_Halves_The_Thrown_Bubble_And_The_Base_Bubble_Has_No_Recast()
        {
            HalenDuel d = Raised(BubbleRelease.Id, 1);
            d.AimAtOther();
            d.Step();
            d.Other = TestWorlds.B.With(InputButtons.Guard); // raised just before the throw: held under 0.8 s at the hit
            d.Run(4);
            d.Recast();
            d.RunUntil(() => ThrownHit(d) != null, 60);
            Assert.Equal(GuardOutcome.Guarded, ThrownHit(d).Resolution.Guard);
            Assert.Equal(12f, ThrownHit(d).Dealt, 3);

            HalenDuel plain = BubbleBucklerTests.Raised();
            plain.AimAtOther();
            plain.Recast();
            plain.Run(30);
            Assert.Equal(45f, plain.P0.Shields.Total, 1);
            Assert.Empty(plain.SpellEvents(SpellEventKind.ProjectileLaunched));
            Assert.False(plain.P0.Caster.Loadout.FormFor(BubbleBuckler.Id).HasTag("projectile"));
            Assert.True(d.P0.Caster.Loadout.FormFor(BubbleBuckler.Id).HasTag("projectile")); // structural delta (D-012)
            Assert.True(d.P0.Caster.Loadout.FormFor(BubbleBuckler.Id).HasTag("recast"));
        }

        // ---- AUG-438 Bubble Steam ------------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 1.5f, 1f)]
        [InlineData(2, 2f, 1.5f)]
        [Verifies("AUG-438")]
        public void A_Thermal_Break_Turns_The_Break_Water_Into_A_Steam_Puff(int rank, float radius, float lifetime)
        {
            HalenDuel d = Raised(BubbleSteam.Id, rank);
            DamageEvent hit = BubbleBucklerTests.HitFrom(d, d.P1.Position, 60f, DamageChannel.Thermal);
            Assert.Equal(45f, hit.Resolution.ShieldAbsorbed, 3);
            d.Run(2);
            Assert.Equal(1f, d.Tide(TideEventKind.BubbleBroken).Single().Amount); // a thermal break
            ObscurationVolume puff = d.Reactions.Volumes.Single();
            Assert.Equal(VolumeKind.Steam, puff.Kind);
            Assert.Equal(0, puff.Owner);
            Assert.Equal(Ticks.FromSeconds(lifetime), puff.ExpiresTick - puff.CreatedTick);
            puff.GetBounds(out Vec2 min, out Vec2 max);
            Assert.InRange(max.X - min.X, 2f * radius - 0.6f, 2f * radius + 0.01f);
            Vec3 front = BubbleBucklerTests.Front(d);
            Assert.True(puff.ContainsPoint(front + new Vec3(0f, 1f, 0f)));
            Assert.Equal(0, d.Total(Material.Water, Footprint.Circle(front.XZ(), 3f))); // "consumes Water 1"
            Assert.Contains(d.World.Owned.Entities, e => e.Owner == 0 && e.DefinitionId.StartsWith("CLOUD:")); // one of Halen's fields

            // The puff conceals: 2 m of steam blocks acquisition through it (spec L167). Counterplay "Use sound": sound passes.
            Assert.True(d.World.Materials.ObscuringPathLength(front + new Vec3(-radius - 0.5f, 1f, 0f), front + new Vec3(radius + 0.5f, 1f, 0f)) >= 2f * radius - 0.6f);
            d.Run(Ticks.FromSeconds(lifetime) + 2);
            Assert.Empty(d.Reactions.Volumes);
        }

        [Fact]
        [Verifies("AUG-438")]
        public void Each_Heat_Reaction_Consumes_15_Shield()
        {
            HalenDuel d = Raised(BubbleSteam.Id, 1);
            BubbleBucklerTests.HitFrom(d, d.P1.Position, 20f, DamageChannel.Thermal);
            d.Run(2);
            Assert.Equal(45f - 20f - 15f, d.P0.Shields.Total, 3);           // drawback "15 instead of 10"
            Assert.Equal(15f, d.Tide(TideEventKind.BubbleHeatReaction).Single().Amount, 3);
            ObscurationVolume puff = d.Reactions.Volumes.Single();           // the record's own 1 m reaction puff
            puff.GetBounds(out Vec2 min, out Vec2 max);
            Assert.InRange(max.X - min.X, 1.4f, 2.01f);
            Assert.Empty(d.Tide(TideEventKind.BubbleBroken));

            // A heat reaction that empties the bubble is a thermal break: the one larger puff and no water.
            BubbleBucklerTests.HitFrom(d, d.P1.Position, 5f, DamageChannel.Thermal);
            d.Run(2);
            Assert.Equal(0f, d.P0.Shields.Total, 3);
            Assert.Equal(5f, d.Tide(TideEventKind.BubbleHeatReaction).Last().Amount, 3);
            Assert.Equal(1f, d.Tide(TideEventKind.BubbleBroken).Single().Amount);
            Assert.Equal(2, d.Reactions.Volumes.Count);
            Assert.Equal(0, d.Total(Material.Water, Footprint.Circle(BubbleBucklerTests.Front(d).XZ(), 3f)));
        }

        [Fact]
        [Verifies("AUG-438")]
        public void A_Physical_Break_Still_Leaves_Water_And_A_Side_Attack_Goes_Around()
        {
            HalenDuel d = Raised(BubbleSteam.Id, 2);
            BubbleBucklerTests.HitFrom(d, d.P1.Position, 50f);
            d.Run(2);
            Assert.Equal(0f, d.Tide(TideEventKind.BubbleBroken).Single().Amount);
            Assert.Empty(d.Reactions.Volumes);
            Assert.Equal(1, d.Units(BubbleBucklerTests.Front(d)).Water);

            // Counterplay "attack from side": a flank hit is never absorbed, so it neither breaks nor steams the bubble.
            HalenDuel side = Raised(BubbleSteam.Id, 1);
            DamageEvent flank = BubbleBucklerTests.HitFrom(side, side.P0.Position + new Vec3(3f, 0f, 0f), 30f, DamageChannel.Thermal);
            Assert.Equal(0f, flank.Resolution.ShieldAbsorbed, 3);
            side.Run(2);
            Assert.Equal(45f, side.P0.Shields.Total, 3);
            Assert.Empty(side.Reactions.Volumes);
        }

        [Fact]
        [Verifies("AUG-437", "AUG-438")]
        public void Release_And_Steam_Combine_With_The_Release_Shield()
        {
            SpellForm form = HalenTestKit.Form(BubbleBuckler.Id, HalenTestKit.Aug(BubbleSteam.Id, 2), HalenTestKit.Aug(BubbleRelease.Id, 1));
            Assert.Equal(new[] { BubbleRelease.Id, BubbleSteam.Id }, form.AppliedTransforms); // delivery, then payload (spec L286)
            var bubble = (BubbleShieldDelivery)form.Delivery;
            Assert.Equal(35f, bubble.Capacity, 3);
            Assert.Equal(15f, bubble.HeatShieldLoss, 3);
            Assert.Equal(2f, bubble.ThermalBreakPuffRadius, 3);
            Assert.NotNull(bubble.ReleaseProjectile);
        }
    }
}
