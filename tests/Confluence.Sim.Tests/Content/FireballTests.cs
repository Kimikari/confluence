using System.Linq;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;
using Confluence.Sim.World;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Content
{
    /// <summary>
    /// SPL-009 Fireball cast through WIZ-002's crest-release grammar from real input: "24 m/s projectile; 1.8 m splash;
    /// direct and splash cannot both damage one target", "48 direct OR 28 splash", "Heat 2 at impact".
    /// </summary>
    public class FireballTests
    {
        private static ProjectileEntity Flying(DuelWorld w) => SpellTestKit.Runtime(w).Entities.OfType<ProjectileEntity>().SingleOrDefault();

        [Fact]
        [Verifies("SPL-009")]
        public void A_Direct_Hit_Deals_48_Thermal_Damage_And_No_Splash_To_The_Same_Target()
        {
            DuelWorld w = ContentTestKit.BramVersusDummy();
            ContentTestKit.HoldAndRelease(w, 15);
            Assert.Equal("SPL-009", ContentTestKit.LastCommit(w).SpellId);
            SpellTestKit.Run(w, 70);
            DamageEvent hit = SpellTestKit.DamageTo(w, 1, "SPL-009").Single();
            Assert.Equal(DamageKind.Direct, hit.Kind);
            Assert.Equal(48f, hit.Dealt, 3);
            Assert.Equal(DamageChannel.Thermal, hit.Resolution.Channels.Single().Channel);
        }

        [Fact]
        [Verifies("SPL-009")]
        public void It_Flies_At_24_Meters_Per_Second_After_Its_Windup()
        {
            DuelWorld w = ContentTestKit.BramVersusDummy();
            ContentTestKit.HoldAndRelease(w, 15);
            int commitTick = ContentTestKit.LastCommit(w).Tick;
            SpellTestKit.RunUntil(w, () => Flying(w) != null, 40);
            SpellEvent launch = SpellTestKit.SpellEvents(w, SpellEventKind.ProjectileLaunched).Single();
            Assert.Equal(commitTick + Ticks.FromSeconds(0.35), launch.Tick); // the record's 0.35 s windup
            Assert.Equal(24f, launch.Amount, 3);
            float z0 = Flying(w).Position.Z;
            SpellTestKit.Run(w, 10);
            Assert.Equal(4f, Flying(w).Position.Z - z0, 3);
        }

        [Fact]
        [Verifies("SPL-009")]
        public void A_Near_Miss_Splashes_28_Within_1_8_Meters_And_Heat_2_Lands_At_The_Impact()
        {
            var materials = new RecordingMaterials();
            DuelWorld w = ContentTestKit.BramVersusDummy(materials: materials);
            PlayerInput aim = SpellTestKit.Aim(w, 0, new Vec3(0f, 0f, 5.3f));
            ContentTestKit.HoldAndRelease(w, 15, aim);
            SpellTestKit.Run(w, 70, aim);
            DamageEvent splash = SpellTestKit.DamageTo(w, 1, "SPL-009").Single();
            Assert.Equal(DamageKind.Splash, splash.Kind);
            Assert.Equal(28f, splash.Dealt, 3);

            WorldDeposit heat = materials.Deposits.Single();
            Assert.Equal(DepositKind.Heat, heat.Kind);
            Assert.Equal(2, heat.Strength);
            Assert.Equal(0, heat.SourcePlayer);
            Assert.Equal("SPL-009", heat.SourceId);
            SpellEvent impact = SpellTestKit.SpellEvents(w, SpellEventKind.ProjectileImpact).Single();
            Assert.Equal(impact.Position, heat.Position);
            Assert.Equal(TestWorlds.FloorId, impact.SolidId);
        }

        [Fact]
        [Verifies("SPL-009")]
        public void A_Target_Beyond_1_8_Meters_Of_The_Impact_Takes_No_Splash()
        {
            DuelWorld w = ContentTestKit.BramVersusDummy();
            PlayerInput aim = SpellTestKit.Aim(w, 0, new Vec3(0f, 0f, 3.5f));
            ContentTestKit.HoldAndRelease(w, 15, aim);
            SpellTestKit.Run(w, 70, aim);
            Assert.Single(SpellTestKit.SpellEvents(w, SpellEventKind.ProjectileImpact));
            Assert.Empty(SpellTestKit.DamageTo(w, 1, "SPL-009"));
        }

        [Fact]
        [Verifies("SPL-009")]
        public void A_Frontal_Guard_Halves_The_Direct_Hit()
        {
            DuelWorld w = ContentTestKit.BramVersusDummy();
            ContentTestKit.HoldAndRelease(w, 15);
            SpellTestKit.RunUntil(w, () => Flying(w) != null && Flying(w).Position.Z > -1f, 60);
            PlayerInput guard = TestWorlds.B.With(InputButtons.Guard);
            SpellTestKit.Run(w, 30, TestWorlds.A, guard);
            DamageEvent hit = SpellTestKit.DamageTo(w, 1, "SPL-009").Single();
            Assert.Equal(GuardOutcome.Guarded, hit.Resolution.Guard); // spec L104: projectile direct damage
            Assert.Equal(24f, hit.Dealt, 3);
        }

        [Fact]
        [Verifies("SPL-009")]
        public void A_Crest_Release_Adds_12_Percent_To_The_Direct_Hit_Only()
        {
            DuelWorld w = ContentTestKit.BramVersusDummy();
            ContentTestKit.HoldAndRelease(w, 27); // the 0.45 s crest
            MasteryResult mastery = ContentTestKit.LastCommit(w).Cast.Mastery;
            Assert.Equal(MasteryReward.DirectDamage, mastery.Reward);
            Assert.Equal(0.12f, mastery.Magnitude, 4);
            SpellTestKit.Run(w, 70);
            Assert.Equal(48f * 1.12f, SpellTestKit.DamageTo(w, 1, "SPL-009").Single().Dealt, 3);
            Assert.Equal(1, w.Player(0).Feats.MasteryCasts);

            DuelWorld v = ContentTestKit.BramVersusDummy();
            PlayerInput aim = SpellTestKit.Aim(v, 0, new Vec3(0f, 0f, 5.3f));
            ContentTestKit.HoldAndRelease(v, 27, aim);
            SpellTestKit.Run(v, 70, aim);
            Assert.Equal(28f, SpellTestKit.DamageTo(v, 1, "SPL-009").Single().Dealt, 3); // spec L480: primary direct payload only
        }

        [Fact]
        [Verifies("SPL-009")]
        public void It_Costs_18_Mana_At_Commitment_And_Then_Cools_Down_For_5_Seconds()
        {
            DuelWorld w = ContentTestKit.BramVersusDummy();
            w.Step(TestWorlds.A.With(InputButtons.Primary), TestWorlds.B);
            Assert.Equal(100f, w.Player(0).Resources.Mana); // preparation is free
            ContentTestKit.HoldAndRelease(w, 14);
            Assert.Equal(82f, w.Player(0).Resources.Mana);
            Assert.Equal(Ticks.FromSeconds(5.0), w.Player(0).Caster.CooldownRemainingTicks("SPL-009"));

            SpellTestKit.Run(w, 60);
            w.Step(TestWorlds.A.With(InputButtons.Primary), TestWorlds.B);
            Assert.Equal(RejectReason.CooldownNotReady, SpellTestKit.CastEvents(w, CastEventKind.Rejected).Single().Cast.Reject);
            SpellTestKit.RunUntil(w, () => w.Player(0).Caster.CooldownRemainingTicks("SPL-009") == 0, 300);
            ContentTestKit.HoldAndRelease(w, 15);
            Assert.Equal(2, SpellTestKit.CastEvents(w, CastEventKind.Committed).Count());
        }

        [Fact]
        [Verifies("SPL-009")]
        public void Its_30_Meter_Range_Ends_The_Flight()
        {
            DuelWorld w = ContentTestKit.BramVersusDummy(TestWorlds.Flat(boundaryRadius: 40f, floorHalf: 40f));
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, 30f), 180f);
            ContentTestKit.HoldAndRelease(w, 15);
            SpellTestKit.Run(w, 120);
            SpellEvent expired = SpellTestKit.SpellEvents(w, SpellEventKind.ProjectileExpired).Single();
            Assert.Equal(30f, expired.Amount, 2);
            Assert.Empty(SpellTestKit.DamageTo(w, 1, "SPL-009"));
        }
    }
}
