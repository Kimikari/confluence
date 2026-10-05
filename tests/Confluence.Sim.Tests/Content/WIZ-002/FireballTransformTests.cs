using System;
using System.Linq;
using Confluence.Casting;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Combat;
using Confluence.Sim.Content.Wizards.Wiz002;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;
using Confluence.Sim.World;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Content.Wiz002
{
    /// <summary>
    /// SPL-009 Fireball's transformations through WIZ-002's grammar: AUG-305 Skipping Fire and AUG-306 Cinder Seed
    /// (exclusive group SPL-009-delivery).
    /// </summary>
    public class FireballTransformTests
    {
        private const string Fireball = "SPL-009";

        /// <summary>The ground point 8 m ahead of Bram: the reflected fireball rises into the dummy's body at 6 m.</summary>
        private static readonly Vec3 BouncePoint = new Vec3(0f, 0f, 2f);

        private static DuelWorld Duel(ArenaLayout layout = null, IWorldMaterials materials = null, params ActiveAugment[] augments) =>
            BramKit.Duel(BramKit.Bram(null, augments), layout, BramKit.Caster(BramKit.Bolt("SPL-T90", 30f)), materials: materials);

        /// <summary>Casts the Fireball at a point (aim held through the windup and the flight).</summary>
        private static void Fire(DuelWorld w, Vec3 at, int hold = BramKit.NormalHold, InputButtons extra = InputButtons.None, int run = 90)
        {
            PlayerInput aim = BramKit.AimAt(w, at, 0).With(extra);
            BramKit.Cast(w, 0, hold, aim);
            BramKit.Run(w, run, BramKit.AimAt(w, at, 0));
        }

        private static Exception Unwrap(Exception e)
        {
            while (e != null && !(e is InvalidOperationException) && e.InnerException != null) e = e.InnerException;
            return e;
        }

        // ----- AUG-305 Skipping Fire --------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 36f)]
        [InlineData(2, 40f)]
        [Verifies("AUG-305")]
        public void Skipping_Fire_Bounces_Once_Off_The_Ground_And_Hits_At_Its_Bounced_Damage(int rank, float bouncedDirect)
        {
            DuelWorld w = Duel(null, null, BramKit.Aug("AUG-305", rank));
            Fire(w, BouncePoint);
            Assert.Equal(20f, BramKit.Spell(w, SpellEventKind.ProjectileLaunched).Single().Amount, 3); // "speed 24 becomes 20 m/s"
            SpellEvent bounce = BramKit.Spell(w, SpellEventKind.ProjectileBounced).Single();     // "bounces once off ground"
            Assert.InRange(bounce.Position.Z, 0.8f, 2.05f);
            Assert.True(bounce.Direction.Y > 0f);
            DamageEvent hit = BramKit.DamageTo(w, 1, Fireball).Single();
            Assert.Equal(DamageKind.Direct, hit.Kind);
            Assert.Equal(bouncedDirect, hit.Dealt, 3); // "at 36 direct" / "bounced damage 40/22"
            Assert.Equal(DamageChannel.Thermal, hit.Resolution.Channels.Single().Channel);
        }

        [Fact]
        [Verifies("AUG-305")]
        public void Skipping_Fire_Marks_Its_Bounce_At_Launch_And_A_Direct_Hit_Before_The_Bounce_Is_A_Normal_Fireball()
        {
            DuelWorld w = Duel(null, null, BramKit.Aug("AUG-305"));
            Fire(w, BouncePoint, run: 1);
            BramKit.RunUntil(w, () => BramKit.Spell(w, SpellEventKind.ProjectileLaunched).Any(), 40, BramKit.AimAt(w, BouncePoint, 0));
            SpellEvent launch = BramKit.Spell(w, SpellEventKind.ProjectileLaunched).Single();
            SpellEvent mark = BramKit.Spell(w, SpellEventKind.Marker).Single(); // "bounce visibly marked"
            Assert.Equal(launch.Tick, mark.Tick);
            Assert.Equal(launch.EntityId, mark.EntityId);
            Assert.InRange(mark.Position.Z, 1.9f, 2.1f);
            Assert.Equal(0f, mark.Position.Y, 2);

            // The dummy standing before the bounce point takes the unbounced 48.
            DuelWorld v = Duel(null, null, BramKit.Aug("AUG-305"));
            TestWorlds.Place(v, 1, new Vec3(0f, 0f, -2f), 180f);
            Fire(v, BouncePoint);
            Assert.Empty(BramKit.Spell(v, SpellEventKind.ProjectileBounced));
            Assert.Equal(48f, BramKit.DamageTo(v, 1, Fireball).Single().Dealt, 3);
        }

        [Theory]
        [InlineData(1, 20f)]
        [InlineData(2, 22f)]
        [Verifies("AUG-305")]
        public void Skipping_Fire_Bounced_Splash_Is_20_Or_22_And_Still_Excludes_A_Direct_Hit(int rank, float bouncedSplash)
        {
            // The rising fireball passes beside the dummy and bursts on the wall behind it.
            ArenaLayout wall = SpellTestKit.FlatWith(SpellTestKit.Wall(60, new Vec3(0f, 0f, 8f), new Vec3(6f, 3f, 1f)));
            DuelWorld w = Duel(wall, null, BramKit.Aug("AUG-305", rank));
            TestWorlds.Place(w, 1, new Vec3(1.0f, 0f, 6.5f), 180f);
            Fire(w, BouncePoint);
            Assert.Single(BramKit.Spell(w, SpellEventKind.ProjectileBounced));
            DamageEvent splash = BramKit.DamageTo(w, 1, Fireball).Single();
            Assert.Equal(DamageKind.Splash, splash.Kind);
            Assert.Equal(bouncedSplash, splash.Dealt, 3);
        }

        [Fact]
        [Verifies("AUG-305")]
        public void Skipping_Fire_Keeps_The_One_Crest_Bonus_After_The_Bounce()
        {
            DuelWorld w = Duel(null, null, BramKit.Aug("AUG-305"));
            Fire(w, BouncePoint, BramKit.CrestHold);
            Assert.Equal(36f * 1.12f, BramKit.DamageTo(w, 1, Fireball).Single().Dealt, 3); // spec L508: one mastery result
        }

        [Fact]
        [Verifies("AUG-305")]
        public void Skipping_Fire_Rank_2_Chooses_The_Normal_Impact_With_The_Cast_Modifier()
        {
            var materials = new RecordingMaterials();
            DuelWorld w = Duel(null, materials, BramKit.Aug("AUG-305", 2));
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, 3.2f), 180f);
            Fire(w, BouncePoint, BramKit.NormalHold, InputButtons.CastModifier);
            Assert.Empty(BramKit.Spell(w, SpellEventKind.ProjectileBounced));
            Assert.Empty(BramKit.Spell(w, SpellEventKind.Marker)); // nothing to mark: no bounce
            DamageEvent splash = BramKit.DamageTo(w, 1, Fireball).Single();
            Assert.Equal(DamageKind.Splash, splash.Kind);
            Assert.Equal(28f, splash.Dealt, 3); // the base Fireball's impact
            WorldDeposit heat = materials.Deposits.Single(d => d.Kind == DepositKind.Heat);
            Assert.Equal(2, heat.Strength); // Heat 2 at impact

            // Rank 1 has no choice: the modifier changes nothing.
            DuelWorld r1 = Duel(null, null, BramKit.Aug("AUG-305", 1));
            Fire(r1, BouncePoint, BramKit.NormalHold, InputButtons.CastModifier);
            Assert.Single(BramKit.Spell(r1, SpellEventKind.ProjectileBounced));
        }

        [Fact]
        [Verifies("AUG-305")]
        public void Skipping_Fire_Counterplay_Cover_Beyond_The_Bounce_Stops_It()
        {
            ArenaLayout cover = SpellTestKit.FlatWith(SpellTestKit.Wall(61, new Vec3(0f, 0f, 4.2f), new Vec3(2f, 1.6f, 0.4f)));
            DuelWorld w = Duel(cover, null, BramKit.Aug("AUG-305"));
            Fire(w, BouncePoint);
            Assert.Single(BramKit.Spell(w, SpellEventKind.ProjectileBounced));
            SpellEvent impact = BramKit.Spell(w, SpellEventKind.ProjectileImpact).Single();
            Assert.Equal(61, impact.SolidId);
            Assert.Empty(BramKit.DamageTo(w, 1, Fireball));
        }

        [Fact]
        [Verifies("AUG-305")]
        [Verifies("AUG-306")]
        public void Skipping_Fire_And_Cinder_Seed_Share_The_Exclusive_Delivery_Group()
        {
            Exception e = Unwrap(Record.Exception(() => Duel(null, null, BramKit.Aug("AUG-305"), BramKit.Aug("AUG-306"))));
            Assert.IsType<InvalidOperationException>(e);
            Assert.Contains("SPL-009-delivery", e.Message);
            Assert.Equal("SPL-009-delivery", BramKit.Catalogue.AugmentsById["AUG-305"].ExclusiveGroup);
            Assert.Equal("SPL-009-delivery", BramKit.Catalogue.AugmentsById["AUG-306"].ExclusiveGroup);
        }

        // ----- AUG-306 Cinder Seed ----------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 3f, 24f, 1.5f)]
        [InlineData(2, 4f, 28f, 1.8f)]
        [Verifies("AUG-306")]
        public void Cinder_Seed_Plants_A_25_Health_Cinder_And_The_Recast_Bursts_It(int rank, float seconds, float burst, float radius)
        {
            DuelWorld w = Duel(null, null, BramKit.Aug("AUG-306", rank));
            Fire(w, BramKit.Chest(w, 1), run: 60);
            Assert.Equal(24f, BramKit.DamageTo(w, 1, Fireball).Single().Dealt, 3); // "Initial damage 48/28 becomes 24/16"
            CinderEntity cinder = BramKit.Entity<CinderEntity>(w);
            Assert.NotNull(cinder);
            Assert.Equal(25f, cinder.Health); // "25-health cinder"
            SpellEvent placed = BramKit.Spell(w, SpellEventKind.DeployablePlaced).Single();
            Assert.Equal(Ticks.FromSeconds(seconds), placed.Ticks);
            Assert.InRange(cinder.Position.Y, 0.2f, 0.3f); // on the ground below the struck body

            // The dummy steps away to just inside the burst radius; the recast bursts the cinder.
            TestWorlds.Place(w, 1, new Vec3(cinder.Position.X, 0f, cinder.Position.Z + radius + 0.3f), 180f);
            BramKit.Recast(w, 0);
            SpellEvent det = BramKit.Spell(w, SpellEventKind.DeployableDetonated).Single();
            Assert.Equal(radius, det.Amount, 3);
            DamageEvent hit = BramKit.DamageTo(w, 1, Fireball).Last();
            Assert.Equal(DamageKind.Splash, hit.Kind);
            Assert.Equal(burst, hit.Dealt, 3);
            Assert.False(cinder.Alive);
            Assert.Equal(2, BramKit.DamageTo(w, 1, Fireball).Count);
        }

        [Fact]
        [Verifies("AUG-306")]
        [Verifies("SPL-011")]
        public void Cinder_Seed_Burst_Is_Secondary_Stationary_Damage_Heat_Haze_Does_Not_Reduce_And_It_Raises_No_Procs()
        {
            // Two Brams: B holds Heat Haze (12% against projectiles) and Tempered Hands (thermal damage taken -> shield).
            DuelPlayerSetup b = DuelPlayerSetup.FromCatalogue(BramKit.Catalogue, "B", "WIZ-002", BramKit.DefaultKit);
            DuelWorld w = BramKit.Duel(BramKit.Bram(null, BramKit.Aug("AUG-306")), other: b);
            Fire(w, new Vec3(0f, 0f, 3f), run: 60); // the fireball lands 3 m short of B: no splash on B
            CinderEntity cinder = BramKit.Entity<CinderEntity>(w);
            Assert.NotNull(cinder);
            Assert.Empty(BramKit.DamageTo(w, 1, Fireball));

            int haze = w.Player(1).Setup.EquippedSpellIds.IndexOf("SPL-011");
            PlayerInput bHaze = TestWorlds.B.Selecting(haze);
            for (int i = 0; i < BramKit.NormalHold; i++) w.Step(TestWorlds.A, bHaze.With(InputButtons.Primary));
            BramKit.RunUntil(w, () => BramKit.Runtime(w).Entities.OfType<BuffEntity>().Any(e => e.Owner == 1), 40, TestWorlds.A, bHaze);
            Assert.Contains(BramKit.Runtime(w).Entities.OfType<BuffEntity>(), e => e.Owner == 1 && e.Alive); // B's shimmer is up

            TestWorlds.Place(w, 1, new Vec3(cinder.Position.X, 0f, cinder.Position.Z + 1.2f), 180f);
            BramKit.Recast(w, 0, null, bHaze);
            DamageEvent burst = BramKit.DamageTo(w, 1, Fireball).Single();
            Assert.Equal(24f, burst.Dealt, 3);       // a stationary burst, not projectile damage: no 12% haze reduction
            Assert.Equal(1, burst.Generation);       // new secondary damage of the transformation (spec L508)
            BramKit.Run(w, 2, TestWorlds.A, bHaze);
            Assert.Equal(0f, w.Player(1).Shields.Total, 3); // no trigger: Tempered Hands does not answer secondary damage (L126)
        }

        [Fact]
        [Verifies("AUG-306")]
        public void Cinder_Seed_Splashes_16_And_Shares_Heat_2_Between_The_Impact_And_The_Burst()
        {
            var materials = new RecordingMaterials();
            DuelWorld w = Duel(null, materials, BramKit.Aug("AUG-306"));
            Fire(w, new Vec3(0f, 0f, 5.3f), run: 60);
            DamageEvent splash = BramKit.DamageTo(w, 1, Fireball).Single();
            Assert.Equal(DamageKind.Splash, splash.Kind);
            Assert.Equal(16f, splash.Dealt, 3); // "becomes 24/16"
            Assert.Equal(1, materials.Deposits.Count(d => d.Kind == DepositKind.Heat));
            Assert.Equal(1, materials.Deposits.Single(d => d.Kind == DepositKind.Heat).Strength);

            BramKit.Recast(w, 0);
            Assert.Single(BramKit.Spell(w, SpellEventKind.DeployableDetonated));
            Assert.Equal(2, materials.Deposits.Where(d => d.Kind == DepositKind.Heat).Sum(d => d.Strength)); // "Heat 2 total shared"
            Assert.All(materials.Deposits, d => Assert.Equal(0, d.AdjacentStrength)); // no extra ring beyond the shared budget
        }

        [Fact]
        [Verifies("AUG-306")]
        public void Cinder_Seed_Counterplay_Shoot_The_Cinder_Or_Leave_The_Marker()
        {
            DuelWorld w = Duel(null, null, BramKit.Aug("AUG-306"));
            Fire(w, new Vec3(0f, 0f, 3f), run: 60);
            CinderEntity cinder = BramKit.Entity<CinderEntity>(w);
            Assert.NotNull(cinder);
            BramKit.OpponentFires(w, 0, cinder.Position); // 30 against 25 health
            BramKit.RunUntil(w, () => !cinder.Alive, 40);
            Assert.Single(BramKit.Spell(w, SpellEventKind.DeployableDestroyed));
            BramKit.Recast(w, 0);
            Assert.Empty(BramKit.Spell(w, SpellEventKind.DeployableDetonated)); // a shot cinder never bursts

            // Leaving the marker: outside 1.5 m the burst misses.
            DuelWorld v = Duel(null, null, BramKit.Aug("AUG-306"));
            Fire(v, new Vec3(0f, 0f, 3f), run: 60);
            CinderEntity c2 = BramKit.Entity<CinderEntity>(v);
            TestWorlds.Place(v, 1, new Vec3(c2.Position.X + 2.1f, 0f, c2.Position.Z), 180f);
            BramKit.Recast(v, 0);
            Assert.Single(BramKit.Spell(v, SpellEventKind.DeployableDetonated));
            Assert.Empty(BramKit.DamageTo(v, 1, Fireball));

            // An expired cinder never bursts either.
            DuelWorld x = Duel(null, null, BramKit.Aug("AUG-306"));
            Fire(x, new Vec3(0f, 0f, 3f), run: 60);
            CinderEntity c3 = BramKit.Entity<CinderEntity>(x);
            BramKit.RunUntil(x, () => !c3.Alive, Ticks.FromSeconds(3.5));
            Assert.Single(BramKit.Spell(x, SpellEventKind.DeployableExpired));
            TestWorlds.Place(x, 1, new Vec3(c3.Position.X, 0f, c3.Position.Z + 0.5f), 180f);
            BramKit.Recast(x, 0);
            Assert.Empty(BramKit.Spell(x, SpellEventKind.DeployableDetonated));
            Assert.Empty(BramKit.DamageTo(x, 1, Fireball));
        }

        [Fact]
        [Verifies("AUG-306")]
        public void Cinder_Seed_Recasts_Once_Through_The_Spell_Recast_Binding()
        {
            DuelWorld w = Duel(null, null, BramKit.Aug("AUG-306"));
            Assert.Equal(1, BramKit.Form(w, Fireball).Castable.Recast.MaxRecasts); // spec L94: one recast
            Assert.Equal(0f, BramKit.Form(w, Fireball).Castable.Recast.ExtraMana);
            Fire(w, new Vec3(0f, 0f, 3f), run: 60);
            float mana = BramKit.Mana(w);
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, 3.5f), 180f);
            BramKit.Recast(w, 0);
            BramKit.Recast(w, 0);
            Assert.Single(BramKit.Spell(w, SpellEventKind.DeployableDetonated));
            Assert.Single(BramKit.DamageTo(w, 1, Fireball));
            Assert.True(BramKit.Mana(w) >= mana); // the recast adds no cost
        }
    }
}
