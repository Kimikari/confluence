using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Sim.Content.Augments.AugmentsA;
using Confluence.Sim.Content.Wizards.Wiz002;
using Confluence.Sim.Content.Wizards.HalenTide;
using Confluence.Sim.Duel;
using Confluence.Sim.Effects;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Content.AugmentsA;
using Confluence.Sim.Tests.Content.Halen;
using Confluence.Sim.Tests.World;

namespace Confluence.Sim.Tests.Integration
{
    /// <summary>
    /// Stage 2 merge: the augments-a projectile family on the kits' own projectile deliveries. WIZ-010's Pressure Jet and
    /// Deepglass Prison (TideProjectileDelivery) and WIZ-002's transformed Fireball (HeatProjectileDelivery, AUG-305) carry
    /// the "projectile" tag the Economy drafts these records for (spec L243); the shared projectile shape
    /// (Spells/Deliveries/ProjectileShapes.cs) lets the records edit and judge them exactly as the framework projectile
    /// (spec L543: general augments edit the resolved structure; L525-L527 projectile numbers).
    /// </summary>
    public sealed class KitProjectileAugmentTests
    {
        private static DuelPlayerSetup Halen(IEnumerable<string> spells, params ActiveAugment[] augments) =>
            DuelPlayerSetup.FromCatalogue(AKit.Catalogue, "A", AKit.Halen, spells, null, augments);

        private static readonly string[] JetAndPrison = { "SPL-073", "SPL-079", "SPL-074", "SPL-075", "SPL-076", "SPL-077" };

        private static SpellForm Form(DuelWorld w, string spellId) => w.Player(0).Caster.Loadout.FormFor(spellId);

        private static IProjectileDelivery Shape(SpellForm form) => Assert.IsAssignableFrom<IProjectileDelivery>(form.Delivery);

        private static HalenDuel Facing(DuelWorld w, float distance)
        {
            AKit.FaceOff(w, distance);
            var d = new HalenDuel(w);
            d.AimAtOther();
            return d;
        }

        [Theory]
        [InlineData(1, 0.05f)]
        [InlineData(2, 0.08f)]
        [Verifies("AUG-026")]
        public void Heavy_Core_Edits_Halen_Tide_Projectiles_And_Bram_Transformed_Fireball(int rank, float bonus)
        {
            AKit.AssertQuotes(HeavyCore.Id, "Direct damage increases 5%.", "Direct damage increases 8%.", "Projectile speed decreases 8%.");
            DuelWorld plain = AKit.Duel(Halen(JetAndPrison));
            DuelWorld heavy = AKit.Duel(Halen(JetAndPrison, new ActiveAugment(HeavyCore.Id, rank)));
            foreach (string spell in new[] { PressureJet.Id, DeepglassPrison.Id })
            {
                SpellForm a = Form(plain, spell), b = Form(heavy, spell);
                Assert.IsType<TideProjectileDelivery>(b.Delivery);
                Assert.Equal(a.Payload.Direct * (1f + bonus), b.Payload.Direct, 3);
                Assert.Equal(Shape(a).Speed * (1f - HeavyCore.SpeedCut), Shape(b).Speed, 3);
            }

            Assert.Equal(32f * (1f + bonus), Form(heavy, PressureJet.Id).Payload.Direct, 3); // SPL-073 "32"

            // AUG-305 Skipping Fire turns Fireball into WIZ-002's bouncing heat projectile; Heavy Core still applies after it.
            var skip = new ActiveAugment(SkippingFire.Id, 1);
            SpellForm fire = Form(AKit.Duel(AKit.Setup("A", AKit.Bram, new[] { skip })), "SPL-009");
            SpellForm heavyFire = Form(AKit.Duel(AKit.Setup("A", AKit.Bram, new[] { skip, new ActiveAugment(HeavyCore.Id, rank) })), "SPL-009");
            Assert.IsType<HeatProjectileDelivery>(heavyFire.Delivery);
            Assert.Equal(fire.Payload.Direct * (1f + bonus), heavyFire.Payload.Direct, 3);
            Assert.Equal(Shape(fire).Speed * (1f - HeavyCore.SpeedCut), Shape(heavyFire).Speed, 3);
        }

        [Theory]
        [InlineData(1, 0.08f)]
        [InlineData(2, 0.12f)]
        [Verifies("AUG-025")]
        public void Fast_Release_Speeds_Halen_Straight_Projectiles_And_Shortens_Their_Range(int rank, float bonus)
        {
            AKit.AssertQuotes(FastRelease.Id, "Projectile speed increases 8%.", "Projectile speed increases 12%.", "Projectile lifetime falls 8%, reducing maximum travel range.");
            DuelWorld plain = AKit.Duel(Halen(JetAndPrison));
            DuelWorld fast = AKit.Duel(Halen(JetAndPrison, new ActiveAugment(FastRelease.Id, rank)));
            foreach (string spell in new[] { PressureJet.Id, DeepglassPrison.Id })
            {
                IProjectileDelivery a = Shape(Form(plain, spell)), b = Shape(Form(fast, spell));
                Assert.Equal(a.Speed * (1f + bonus), b.Speed, 3);
                Assert.Equal(a.MaxRange * (1f - FastRelease.LifetimeCut), b.MaxRange, 3);
            }

            Assert.Equal(35f * (1f + bonus), Shape(Form(fast, PressureJet.Id)).Speed, 3); // "35 m/s stream projectile"
        }

        [Theory]
        [InlineData(1, 0.05f)]
        [InlineData(2, 0.08f)]
        [Verifies("AUG-038")]
        public void First_Contact_Raises_A_Pressure_Jet_Hit_On_A_Full_Health_Enemy(int rank, float bonus)
        {
            AKit.AssertQuotes(FirstContact.Id, "A direct projectile hits an enemy with full health.", "Damage increases 5%.", "Damage increases 8%.");
            HalenDuel d = Facing(AKit.Duel(Halen(JetAndPrison, new ActiveAugment(FirstContact.Id, rank))), 8f);
            Assert.Equal(PressureJet.Id, d.Cast(0).SpellId);
            d.RunUntil(() => AKit.Hits(d.World, 1).Any(), 90);
            DamageEvent first = AKit.Hits(d.World, 1).Single();
            Assert.Equal(PressureJet.Id, first.SourceId);
            Assert.Equal(32f * (1f + bonus), first.Resolution.Raw, 3);
            Assert.Equal(32f * (1f + bonus), first.Dealt, 3);
        }

        [Fact]
        [Verifies("AUG-001")]
        public void Steady_Hand_Speeds_A_Pressure_Jet_Released_After_Steady_Aim()
        {
            AKit.AssertQuotes(SteadyHand.Id, "within a 1-degree cone for 0.3 s", "Gain 6% projectile speed.", "Gain 10% projectile speed.");
            foreach (int rank in new[] { 1, 2 })
            {
                HalenDuel d = Facing(AKit.Duel(Halen(JetAndPrison, new ActiveAugment(SteadyHand.Id, rank))), 12f);
                d.Run(20); // the aim settles and stays within 1 degree through the 0.4 s hold
                d.Cast(0);
                d.RunUntil(() => d.SpellEvents(SpellEventKind.ProjectileLaunched).Any(), 60); // released after the 0.25 s windup
                SpellEvent launch = d.SpellEvents(SpellEventKind.ProjectileLaunched).Single();
                Assert.Equal(35f * (1f + (rank == 2 ? SteadyHand.Rank2Speed : SteadyHand.Rank1Speed)), launch.Amount, 3);
                TideProjectileEntity jet = d.Runtime.Entities.OfType<TideProjectileEntity>().Single();
                Assert.Equal(launch.Amount, jet.Speed, 3);
            }
        }

        [Theory]
        [InlineData(1, 0.07f)]
        [InlineData(2, 0.11f)]
        [Verifies("AUG-030")]
        public void Ground_Skimmer_Speeds_Halen_Rolling_Prison_Bubble_After_2_Metres(int rank, float bonus)
        {
            AKit.AssertQuotes(GroundSkimmer.Id, "within 0.3 m of solid ground for at least 2 m", "Speed increases 7%.", "Speed increases 11%.");
            string[] spells = { "SPL-079", "SPL-073", "SPL-074", "SPL-075", "SPL-076", "SPL-077" };
            HalenDuel d = Facing(AKit.Duel(Halen(spells, new ActiveAugment("AUG-445", 1), new ActiveAugment(GroundSkimmer.Id, rank))), 16f);
            Assert.Equal(DeepglassPrison.Id, d.Cast(0).SpellId);
            d.RunUntil(() => d.SpellEvents(SpellEventKind.ProjectileLaunched).Any(), 90); // released after the 0.65 s windup
            TideProjectileEntity bubble = d.Runtime.Entities.OfType<TideProjectileEntity>().Single();
            Assert.True(bubble.Spec.Rolling);
            float rolling = bubble.Speed; // AUG-445 rank 1 "rolls along ground at 16 m/s"
            Assert.Equal(16f, rolling, 3);
            d.RunUntil(() => !bubble.Alive || bubble.Travelled >= 2.6f, 60);
            Assert.True(bubble.Alive);
            Assert.Equal(rolling * (1f + bonus), bubble.Speed, 3);
        }

        [Fact]
        [Verifies("AUG-043")]
        public void Visible_Afterimage_Follows_A_Pressure_Jet_That_Narrowly_Misses()
        {
            HalenDuel d = Facing(AKit.Duel(Halen(JetAndPrison, new ActiveAugment(VisibleAfterimage.Id, 1))), 8f);
            d.AimAt(d.P1.Position + new Vec3(1.0f, 1.1f, 0f)); // 1 m beside the body axis: a miss within 1 m of its surface
            d.Cast(0);
            d.Run(60);
            Assert.Empty(AKit.Hits(d.World, 1));
            Assert.Contains(d.World.EventLog.OfType<EffectEvent>(), e => e.SourceId == VisibleAfterimage.Id && e.Change == EffectChange.Activated);
            Assert.Contains(d.World.EventLog.OfType<MarkerEvent>(), m => m.Cue == VisibleAfterimage.TrailCue);
        }
    }
}
