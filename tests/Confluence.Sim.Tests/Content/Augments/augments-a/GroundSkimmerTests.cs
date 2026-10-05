using System.Linq;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Sim.Combat;
using Confluence.Sim.Content.Augments.AugmentsA;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;
using Confluence.Sim.Tests.World;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Content.AugmentsA
{
    /// <summary>AUG-030 Ground Skimmer on WIZ-002's Fireball (24 m/s, radius 0.15 m, launched from the 1.6 m eye).</summary>
    public class GroundSkimmerTests
    {
        /// <summary>A long slab under the flight line: its top 1.3 m leaves the Fireball's underside (1.45 m) 0.15 m above it.</summary>
        private static ArenaLayout Slab(float top = 1.3f) =>
            SpellTestKit.FlatWith(TestWorlds.Box(70, new Vec3(0f, 0f, 6.5f), new Vec3(1.2f, top, 11f), SolidKind.Platform));

        private static ProjectileEntity Bolt(DuelWorld w) =>
            ((SpellRuntime)w.Spells).Entities.OfType<ProjectileEntity>().SingleOrDefault(p => p.Alive && p.Owner == 0);

        /// <summary>Commits the Fireball and steps until it has flown the given distance; returns its speed.</summary>
        private static float SpeedAfter(DuelWorld w, float distance)
        {
            AKit.Step(w, 1);
            AKit.HoldAndRelease(w, AKit.NormalHold);
            for (int i = 0; i < 60 && (Bolt(w) == null || Bolt(w).Travelled < distance); i++) AKit.Step(w, 1);
            ProjectileEntity p = Bolt(w);
            Assert.NotNull(p);
            return p.Velocity.Length();
        }

        [Theory]
        [InlineData(1, 0.07f)]
        [InlineData(2, 0.11f)]
        [Verifies("AUG-030")]
        public void Ground_Skimmer_Speeds_A_Projectile_After_2_Metres_Close_Over_Ground(int rank, float bonus)
        {
            AKit.AssertQuotes(GroundSkimmer.Id, "within 0.3 m of solid ground for at least 2 m", "Speed increases 7%.", "Speed increases 11%.", "Airborne-target damage decreases 5%.");
            DuelWorld w = AKit.With(GroundSkimmer.Id, rank, layout: Slab());
            AKit.FaceOff(w, 16f);
            Assert.Equal(24f, SpeedAfter(w, 1.2f), 3);   // not yet 2 m over the slab
            for (int i = 0; i < 8; i++) AKit.Step(w, 1);
            Assert.Equal(24f * (1f + bonus), Bolt(w).Velocity.Length(), 3);
        }

        [Fact]
        [Verifies("AUG-030")]
        public void Ground_Skimmer_Needs_Ground_Within_0_3_Metres()
        {
            DuelWorld open = AKit.With(GroundSkimmer.Id, 2);
            AKit.FaceOff(open, 16f);
            Assert.Equal(24f, SpeedAfter(open, 8f), 3);

            DuelWorld high = AKit.With(GroundSkimmer.Id, 2, layout: Slab(1.05f)); // 0.4 m of clearance
            AKit.FaceOff(high, 16f);
            Assert.Equal(24f, SpeedAfter(high, 8f), 3);
        }

        [Fact]
        [Verifies("AUG-030")]
        public void Ground_Skimmer_Drawback_Cuts_Damage_To_Airborne_Targets()
        {
            DuelWorld grounded = AKit.With(GroundSkimmer.Id, 1);
            AKit.FaceOff(grounded, 6f);
            AKit.Step(grounded, 1);
            AKit.HoldAndRelease(grounded, AKit.NormalHold);
            AKit.Step(grounded, 60);
            Assert.Equal(48f, AKit.Hits(grounded, 1, DamageKind.Direct).Single().Resolution.Raw, 3);

            // The enemy jumps as the Fireball launches (0.7 s airtime; the flight takes 0.2 s).
            DuelWorld w = AKit.With(GroundSkimmer.Id, 1);
            AKit.FaceOff(w, 6f);
            AKit.Step(w, 1);
            AKit.HoldAndRelease(w, AKit.NormalHold);
            for (int i = 0; i < 60 && Bolt(w) == null; i++) AKit.Step(w, 1);
            AKit.Step(w, 1, null, AKit.B.With(InputButtons.Jump));
            AKit.Step(w, 40);
            DamageEvent hit = AKit.Hits(w, 1, DamageKind.Direct).Single();
            Assert.Equal(48f * 0.95f, hit.Resolution.Raw, 3);
        }
    }
}
