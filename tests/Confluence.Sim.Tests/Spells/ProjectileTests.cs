using System.Linq;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Spells;
using Confluence.Sim.World;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Spells
{
    /// <summary>Projectile flight, collision, direct/splash exclusivity, guard, range, gravity, bounces, and deposits.</summary>
    public class ProjectileTests
    {
        private static SpellFormBuilder Orb(float speed = 30f, float direct = 10f, float splash = 0f, float radius = 0f, bool exclusive = false)
        {
            SpellFormBuilder b = SpellTestKit.Form();
            b.Delivery = new ProjectileDelivery { Speed = speed, MaxRange = 30f };
            b.Payload.Direct = direct;
            b.Payload.Splash = splash;
            b.Payload.SplashRadius = radius;
            b.Payload.SplashExcludesDirect = exclusive;
            return b;
        }

        private static ProjectileEntity Flying(DuelWorld w) => SpellTestKit.Runtime(w).Entities.OfType<ProjectileEntity>().SingleOrDefault();

        [Fact]
        public void A_Projectile_Flies_At_Its_Speed_From_The_Eye_And_Strikes_The_Opponent()
        {
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Orb().Build()));
            SpellTestKit.Cast(w);
            ProjectileEntity p = Flying(w);
            Assert.Equal(-6f + 0.5f, p.Position.Z, 3); // released and advanced one tick at 30 m/s
            SpellTestKit.Run(w, 9);
            Assert.Equal(-6f + 5f, Flying(w).Position.Z, 3);

            SpellTestKit.RunUntil(w, () => Flying(w) == null, 60);
            DamageEvent hit = SpellTestKit.DamageTo(w, 1).Single();
            Assert.Equal(DamageKind.Direct, hit.Kind);
            Assert.Equal(10f, hit.Dealt, 3);
            Assert.Single(SpellTestKit.SpellEvents(w, SpellEventKind.ProjectileImpact), e => e.TargetPlayer == 1);
        }

        [Fact]
        public void Direct_And_Splash_Never_Both_Damage_One_Target_When_The_Record_Makes_Them_Exclusive()
        {
            DuelWorld exclusive = SpellTestKit.Duel(SpellTestKit.Loadout(Orb(direct: 20f, splash: 12f, radius: 2f, exclusive: true).Build()));
            SpellTestKit.Cast(exclusive);
            SpellTestKit.Run(exclusive, 60);
            Assert.Equal(new[] { DamageKind.Direct }, SpellTestKit.DamageTo(exclusive, 1).Select(d => d.Kind));
            Assert.Equal(20f, SpellTestKit.HealthLost(exclusive, 1), 3);

            DuelWorld both = SpellTestKit.Duel(SpellTestKit.Loadout(Orb(direct: 20f, splash: 12f, radius: 2f).Build()));
            SpellTestKit.Cast(both);
            SpellTestKit.Run(both, 60);
            Assert.Equal(32f, SpellTestKit.HealthLost(both, 1), 3);
        }

        [Fact]
        public void Splash_Around_A_Floor_Impact_Reaches_Nearby_Bodies_But_Not_Through_Cover()
        {
            DuelWorld open = SpellTestKit.Duel(SpellTestKit.Loadout(Orb(direct: 20f, splash: 12f, radius: 2f, exclusive: true).Build()));
            PlayerInput aim = SpellTestKit.Aim(open, 0, new Vec3(0f, 0f, 4.8f));
            SpellTestKit.Cast(open, aim);
            SpellTestKit.Run(open, 60, aim);
            DamageEvent splash = SpellTestKit.DamageTo(open, 1).Single();
            Assert.Equal(DamageKind.Splash, splash.Kind);
            Assert.Equal(GuardOutcome.None, splash.Resolution.Guard);
            Assert.Equal(12f, splash.Dealt, 3);

            // A wall between the impact and the target stops the splash (spec L120).
            DuelWorld covered = SpellTestKit.Duel(SpellTestKit.Loadout(Orb(direct: 20f, splash: 12f, radius: 2f).Build()),
                layout: SpellTestKit.FlatWith(SpellTestKit.Wall(50, new Vec3(0f, 0f, 4.6f), new Vec3(3f, 2.5f, 0.2f))));
            SpellTestKit.Cast(covered);
            SpellTestKit.Run(covered, 60);
            Assert.Contains(SpellTestKit.SpellEvents(covered, SpellEventKind.ProjectileImpact), e => e.SolidId == 50);
            Assert.Empty(SpellTestKit.DamageTo(covered, 1));
        }

        [Fact]
        public void A_Frontal_Guard_Reduces_The_Direct_Hit_But_Never_The_Splash()
        {
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(Orb(direct: 20f).Build()));
            PlayerInput guard = TestWorlds.B.With(InputButtons.Guard);
            SpellTestKit.Cast(w, null, guard);
            SpellTestKit.Run(w, 40, TestWorlds.A, guard);
            DamageEvent hit = SpellTestKit.DamageTo(w, 1).Single();
            Assert.Equal(GuardOutcome.Guarded, hit.Resolution.Guard); // spec L104: projectile direct damage, frontal arc
            Assert.Equal(10f, hit.Dealt, 3);
        }

        [Fact]
        public void Maximum_Range_Ends_The_Flight_And_An_Expiring_Burst_Is_Optional()
        {
            SpellFormBuilder b = SpellTestKit.Form();
            b.Delivery = new ProjectileDelivery { Speed = 30f, MaxRange = 5f };
            b.Payload.Direct = 10f;
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(b.Build()));
            SpellTestKit.Cast(w);
            SpellTestKit.Run(w, 30);
            SpellEvent expired = SpellTestKit.SpellEvents(w, SpellEventKind.ProjectileExpired).Single();
            Assert.Equal(5f, expired.Amount, 3);
            Assert.Equal(-1f, expired.Position.Z, 3);
            Assert.Empty(SpellTestKit.DamageTo(w, 1));

            SpellFormBuilder burst = SpellTestKit.Form();
            burst.Delivery = new ProjectileDelivery { Speed = 30f, MaxRange = 10f, DetonateOnExpire = true };
            burst.Payload.Splash = 15f;
            burst.Payload.SplashRadius = 3f;
            DuelWorld v = SpellTestKit.Duel(SpellTestKit.Loadout(burst.Build()));
            SpellTestKit.Cast(v);
            SpellTestKit.Run(v, 30);
            Assert.Equal(15f, SpellTestKit.HealthLost(v, 1), 3); // burst at z = 4, 2 m from the target
        }

        [Fact]
        public void Gravity_Drops_A_Lobbed_Projectile_Onto_The_Floor()
        {
            SpellFormBuilder b = SpellTestKit.Form();
            b.Delivery = new ProjectileDelivery { Speed = 20f, MaxRange = 30f, Gravity = 18f };
            b.Payload.Direct = 10f;
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(b.Build()));
            SpellTestKit.Cast(w);
            SpellTestKit.Run(w, 60);
            SpellEvent impact = SpellTestKit.SpellEvents(w, SpellEventKind.ProjectileImpact).Single();
            Assert.Equal(TestWorlds.FloorId, impact.SolidId);
            Assert.InRange(impact.Position.Z, -6f + 7.5f, -6f + 9.5f); // t = sqrt(2 * 1.45 / 18) = 0.40 s at 20 m/s
            Assert.Empty(SpellTestKit.DamageTo(w, 1));
        }

        [Fact]
        public void One_Ground_Bounce_Redirects_The_Projectile_And_Keeps_The_Stated_Damage_Fraction()
        {
            SpellFormBuilder b = SpellTestKit.Form();
            b.Delivery = new ProjectileDelivery { Speed = 30f, MaxRange = 30f, GroundBounces = 1, BounceDamageScale = 0.75f };
            b.Payload.Direct = 20f;
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(b.Build()));
            PlayerInput aim = SpellTestKit.Aim(w, 0, new Vec3(0f, 0f, 0f));
            SpellTestKit.Cast(w, aim);
            SpellTestKit.Run(w, 60, aim);
            Assert.Single(SpellTestKit.SpellEvents(w, SpellEventKind.ProjectileBounced));
            Assert.Equal(15f, SpellTestKit.DamageTo(w, 1).Single().Dealt, 3);
        }

        [Fact]
        public void Several_Projectiles_Of_One_Cast_Share_Its_Per_Target_Cap()
        {
            SpellFormBuilder b = SpellTestKit.Form();
            b.Delivery = new ProjectileDelivery { Speed = 30f, MaxRange = 30f, Count = 3, SpreadDeg = 2f };
            b.Payload.Direct = 10f;
            b.Payload.MaxPerTarget = 25f;
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(b.Build()));
            SpellTestKit.Cast(w);
            Assert.Equal(3, SpellTestKit.SpellEvents(w, SpellEventKind.ProjectileLaunched).Count());
            SpellTestKit.Run(w, 60);
            // Damage dealt (shield and health): the WIZ-002 dummy's Tempered Hands passive shields after the first thermal hit.
            Assert.Equal(25f, SpellTestKit.TotalDealt(w, 1), 3);
        }

        [Fact]
        public void Impact_Deposits_Are_Emitted_Through_The_Materials_Seam_With_Owner_And_Original_Event()
        {
            var materials = new RecordingMaterials();
            SpellFormBuilder b = Orb();
            b.Payload.AddDeposit(DepositKind.Heat, 2, DepositPlacement.Impact);
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(b.Build()), materials: materials);
            SpellTestKit.Cast(w);
            SpellTestKit.Run(w, 60);
            WorldDeposit d = materials.Deposits.Single();
            Assert.Equal(DepositKind.Heat, d.Kind);
            Assert.Equal(2, d.Strength);
            Assert.Equal(0, d.SourcePlayer);
            Assert.Equal("SPL-T01", d.SourceId);
            Assert.Equal(SpellTestKit.CastEvents(w, Confluence.Casting.CastEventKind.Committed).Single().Id, d.OriginEventId);
            Assert.True(d.HasFootprint);
            Assert.InRange(d.Position.Z, 5f, 6f);
        }

        [Fact]
        public void A_Split_Payload_Resolves_Each_Channel_And_Rejoins_As_One_Hit()
        {
            SpellFormBuilder b = Orb(direct: 20f);
            b.Payload.SplitChannel = DamageChannel.Electrical;
            b.Payload.SplitFraction = 0.5f;
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(b.Build()));
            w.Player(1).Stats.Add(Confluence.Sim.Players.StatKind.Resistance, 0.2f, "TEST-RES", DamageChannel.Thermal);
            SpellTestKit.Cast(w);
            SpellTestKit.Run(w, 40);
            DamageEvent hit = SpellTestKit.DamageTo(w, 1).Single(); // spec L112: one hit event, no doubled triggers
            Assert.Equal(2, hit.Resolution.Channels.Length);
            Assert.Equal(10f * 0.8f + 10f, hit.Dealt, 3);
        }

        [Fact]
        public void A_Projectile_Speed_Mastery_Reward_Speeds_The_Flight()
        {
            var grammar = new TapGrammar(0, Confluence.Casting.MasteryResult.Mastered(Confluence.Casting.MasteryReward.ProjectileSpeed, 0.15f));
            DuelWorld w = SpellTestKit.Duel(SpellTestKit.Loadout(grammar, Orb(speed: 20f).Build()));
            SpellTestKit.Cast(w);
            Assert.Equal(23f / 60f, Flying(w).Position.Z + 6f, 3);
        }
    }
}
