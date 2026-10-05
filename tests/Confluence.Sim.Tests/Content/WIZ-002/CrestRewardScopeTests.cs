using System.Linq;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Sim.Combat;
using Confluence.Sim.Content.Wizards.Wiz002;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;

namespace Confluence.Sim.Tests.Content.Wiz002
{
    /// <summary>
    /// Where WIZ-002's crest reward "direct damage gains 12%" applies (kit interpretation, see
    /// <c>KitSupport.PrimaryBurstMastery</c> and docs/notes/kit-wiz-002.md): the primary payload's direct amount where it
    /// has one (Fireball's alternative 28 splash is unchanged), the record's area burst where that is the spell's only
    /// damage (Boiler Orb, Phoenix Rush), never new secondary damage (AUG-306's cinder burst).
    /// </summary>
    public class CrestRewardScopeTests
    {
        private const string Fireball = "SPL-009";

        private static DuelWorld Duel(params ActiveAugment[] augments) =>
            BramKit.Duel(BramKit.Bram(null, augments), null, BramKit.Caster(BramKit.Bolt("SPL-T90", 30f)));

        private static void Fire(DuelWorld w, Vec3 at, int hold, int run = 90)
        {
            PlayerInput aim = BramKit.AimAt(w, at, 0);
            BramKit.Cast(w, 0, hold, aim);
            BramKit.Run(w, run, aim);
        }

        [Fact]
        [Verifies("WIZ-002")]
        public void A_Crest_Fireball_Raises_Its_Direct_Hit_But_Not_Its_Alternative_Splash()
        {
            DuelWorld direct = Duel();
            Fire(direct, BramKit.Chest(direct, 1), BramKit.CrestHold);
            Assert.Equal(MasteryReward.DirectDamage, BramKit.Casts(direct, CastEventKind.Committed).Single().Cast.Mastery.Reward);
            DamageEvent hit = BramKit.DamageTo(direct, 1, Fireball).Single();
            Assert.Equal(DamageKind.Direct, hit.Kind);
            Assert.Equal(48f * 1.12f, hit.Dealt, 3);

            DuelWorld splash = Duel();
            Fire(splash, new Vec3(0f, 0f, 5f), BramKit.CrestHold); // the floor 1 m in front of the dummy
            DamageEvent near = BramKit.DamageTo(splash, 1, Fireball).Single();
            Assert.Equal(DamageKind.Splash, near.Kind);
            Assert.Equal(28f, near.Dealt, 3);
        }

        [Theory]
        [InlineData(1, 24f, 1.5f)]
        [InlineData(2, 28f, 1.8f)]
        [Verifies("AUG-306")]
        public void A_Crest_Cinder_Seed_Raises_The_Impact_But_Never_The_Secondary_Cinder_Burst(int rank, float burst, float radius)
        {
            DuelWorld w = Duel(BramKit.Aug("AUG-306", rank));
            Fire(w, BramKit.Chest(w, 1), BramKit.CrestHold, 60);
            Assert.Equal(24f * 1.12f, BramKit.DamageTo(w, 1, Fireball).Single().Dealt, 3); // the transformed direct hit
            CinderEntity cinder = BramKit.Entity<CinderEntity>(w);
            Assert.NotNull(cinder);
            TestWorlds.Place(w, 1, new Vec3(cinder.Position.X, 0f, cinder.Position.Z + radius + 0.3f), 180f);
            BramKit.Recast(w, 0);
            Assert.Equal(burst, BramKit.DamageTo(w, 1, Fireball).Last().Dealt, 3); // secondary: no grammar bonus (L480)
            Assert.Equal(1, BramKit.DamageTo(w, 1, Fireball).Last().Generation);
        }
    }
}
