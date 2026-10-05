using System.Linq;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Time;
using Confluence.Reactions;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Sim.Casting;
using Confluence.Sim.Combat;
using Confluence.Sim.Content.Wizards.HalenTide;
using Confluence.Sim.Duel;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;

namespace Confluence.Sim.Tests.Content.Halen
{
    /// <summary>AUG-441 Cascade Landing and AUG-442 Cascade Brake on SPL-077 Cascade Vault, cast through WIZ-010's grammar.</summary>
    public class VaultTransformTests
    {
        private const int Slot = CascadeVaultTests.Slot;

        private static HalenDuel With(string augment, int rank) => HalenTestKit.Duel(HalenTestKit.Halen(null, HalenTestKit.Aug(augment, rank)));

        private static SpellEvent Started(HalenDuel d)
        {
            d.RunUntil(() => d.SpellEvents(SpellEventKind.MovementStarted).Any(), 40);
            return d.SpellEvents(SpellEventKind.MovementStarted).Single();
        }

        // ---- AUG-441 Cascade Landing ---------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 1f, 3.0)]
        [InlineData(2, 1.5f, 4.0)]
        [Verifies("AUG-441")]
        public void The_Landing_Leaves_A_Water_1_Puddle_For_Its_Lifetime(int rank, float radius, double lifetime)
        {
            HalenDuel d = With(CascadeLanding.Id, rank);
            CascadeVaultTests.Vault(new Vec3(0f, 0f, -1f), d: d);
            SpellEvent end = CascadeVaultTests.Landed(d);
            Assert.NotNull(end);
            d.Step();
            Vec3 landing = d.P0.Position;
            Assert.Equal(1, d.Units(landing).Water);                                 // "Water 1"
            Assert.Equal(1, d.Units(landing + new Vec3(0.6f, 0f, 0f)).Water);
            Assert.Equal(rank >= 2 ? 1 : 0, d.Units(landing + new Vec3(1.3f, 0f, 0f)).Water); // 1 m vs 1.5 m radius
            Assert.Equal(1, d.Units(landing + new Vec3(0f, 0f, radius - 0.3f)).Water);
            Assert.Equal(0, d.Units(landing + new Vec3(1.8f, 0f, 0f)).Water);
            Assert.Empty(d.DamageTo(1));                                             // "no damage"

            // The puddle lasts the stated time (the launch water keeps its own 3 s).
            int formed = end.Tick;
            d.RunUntil(() => d.World.Tick >= formed + Ticks.FromSeconds(lifetime) - 6, Ticks.FromSeconds(5.0));
            Assert.Equal(1, d.Units(landing + new Vec3(0.6f, 0f, 0f)).Water);
            d.Run(12);
            Assert.Equal(0, d.Units(landing + new Vec3(0.6f, 0f, 0f)).Water);
        }

        [Fact]
        [Verifies("AUG-441")]
        public void Leap_Range_6_Becomes_5_Meters()
        {
            HalenDuel d = With(CascadeLanding.Id, 1);
            Assert.Equal(5f, d.P0.Caster.Loadout.FormFor(CascadeVault.Id).Range, 3);
            CascadeVaultTests.Vault(new Vec3(0f, 0f, 8f), d: d);
            CascadeVaultTests.Landed(d);
            Assert.Equal(5f, Vec3.DistanceXZ(d.P0.Position, new Vec3(0f, 0f, -6f)), 1);

            HalenDuel plain = CascadeVaultTests.Vault(new Vec3(0f, 0f, 8f));
            CascadeVaultTests.Landed(plain);
            Assert.Equal(6f, Vec3.DistanceXZ(plain.P0.Position, new Vec3(0f, 0f, -6f)), 1);
        }

        [Fact]
        [Verifies("AUG-441")]
        public void The_Combined_Source_Cap_Holds_For_A_Hop_Inside_The_Launch_Cell()
        {
            HalenDuel d = With(CascadeLanding.Id, 2);
            CascadeVaultTests.Vault(new Vec3(0.1f, 0f, -5.75f), d: d);
            CascadeVaultTests.Landed(d);
            d.Step();
            Assert.Equal(2, d.Units(d.P0.Position).Water); // launch 1 + puddle 1: never above "Water 2"
        }

        [Fact]
        [Verifies("AUG-441")]
        public void A_Charge_In_The_Landing_Puddle_Reaches_Halen()
        {
            // Counterplay "Attack landing or exploit conductivity": the puddle under the fixed landing conducts.
            HalenDuel d = With(CascadeLanding.Id, 1);
            CascadeVaultTests.Vault(new Vec3(0f, 0f, -1f), d: d);
            CascadeVaultTests.Landed(d);
            d.Run(20);
            Vec3 at = d.P0.Position;
            d.Reactions.Discharge(new ElectricDischarge { Point = at + new Vec3(0.5f, 0f, 0f), ReactionDamage = 10f, Owner = 1, SourceEventId = 0 });
            d.Run(3);
            Assert.Contains(d.DamageTo(0), e => e.Kind == DamageKind.Environment);
        }

        // ---- AUG-442 Cascade Brake -----------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 2f)]
        [InlineData(2, 3f)]
        [Verifies("AUG-442")]
        public void A_Midair_Recast_Pulls_The_Landing_Back_Along_The_Arc(int rank, float brake)
        {
            HalenDuel plain = CascadeVaultTests.Vault(new Vec3(0f, 0f, 0f));
            SpellEvent plainStart = Started(plain);
            SpellEvent plainEnd = CascadeVaultTests.Landed(plain);

            HalenDuel d = With(CascadeBrake.Id, rank);
            Assert.True(d.P0.Caster.Loadout.FormFor(CascadeVault.Id).HasTag("recast")); // a pending state (spec L549)
            CascadeVaultTests.Vault(new Vec3(0f, 0f, 0f), d: d);
            SpellEvent start = Started(d);
            d.Run(4);
            Assert.False(d.P0.Motor.Grounded);
            d.Recast();
            TideEvent braked = d.Tide(TideEventKind.VaultBraked).Single();
            Assert.Equal(brake, braked.Amount, 3);
            SpellEvent end = CascadeVaultTests.Landed(d);
            Assert.NotNull(end);
            Assert.Equal(6f - brake, Vec3.DistanceXZ(d.P0.Position, start.Position), 1);
            Assert.Equal(0f, Vec3.DistanceXZ(d.P0.Position, braked.Position), 1); // the telegraphed new landing
            Assert.InRange(end.Tick - start.Tick, plainEnd.Tick - plainStart.Tick - 2, plainEnd.Tick - plainStart.Tick + 2); // same airtime
        }

        [Fact]
        [Verifies("AUG-442")]
        public void The_Brake_Never_Extends_The_Leap_And_Works_Once()
        {
            // A short hop: the remaining travel is less than the brake, so the landing moves back at most to the body.
            HalenDuel d = With(CascadeBrake.Id, 2);
            CascadeVaultTests.Vault(new Vec3(0f, 0f, -4f), d: d);
            SpellEvent start = Started(d);
            d.Run(30);
            Vec3 before = d.P0.Position;
            d.Recast();
            TideEvent braked = d.Tide(TideEventKind.VaultBraked).Single();
            Assert.True(braked.Amount < 3f);
            CascadeVaultTests.Landed(d);
            Assert.True(Vec3.DistanceXZ(d.P0.Position, start.Position) <= 2f + 0.05f);
            Assert.True(Vec3.DistanceXZ(d.P0.Position, before) < 0.6f);

            // A second recast in the same leap (or after landing) does nothing.
            HalenDuel twice = With(CascadeBrake.Id, 1);
            CascadeVaultTests.Vault(new Vec3(0f, 0f, 0f), d: twice);
            Started(twice);
            twice.Run(3);
            twice.Recast();
            twice.Step();
            twice.Recast();
            CascadeVaultTests.Landed(twice);
            twice.Recast();
            twice.Step();
            Assert.Single(twice.Tide(TideEventKind.VaultBraked));
            Assert.Equal(4f, Vec3.DistanceXZ(twice.P0.Position, new Vec3(0f, 0f, -6f)), 1);
        }

        [Fact]
        [Verifies("AUG-442")]
        public void It_Costs_4_More_Mana_And_Grants_No_Invulnerability_Or_Extra_Jump()
        {
            HalenDuel d = With(CascadeBrake.Id, 1);
            d.AimAt(new Vec3(0f, 0f, 0f));
            CastDuelEvent commit = d.Cast(Slot);
            Assert.Equal(28f, commit.Cast.Mana, 3); // "Mana +4"
            Started(d);
            d.Run(4);
            d.Recast();
            d.Step();
            Assert.False(d.P0.Motor.Grounded);
            DamageEvent hit = d.World.ApplyDamage(TestWorlds.Hit(d.World, 1, 0, 25f));
            Assert.Equal(25f, hit.Dealt, 3); // still fully hittable after the brake

            // No extra jump: the brake never lifts the body, its arc only falls from the recast on.
            float peak = d.P0.Position.Y;
            for (int i = 0; i < 60 && !d.P0.Motor.Grounded; i++)
            {
                d.Step();
                peak = System.Math.Max(peak, d.P0.Position.Y);
            }

            Assert.True(peak < 3.05f);
            Assert.True(d.P0.Motor.Grounded);
        }

        [Fact]
        [Verifies("AUG-441", "AUG-442")]
        public void Landing_And_Brake_Combine_The_Puddle_Lands_At_The_Braked_Point()
        {
            HalenDuel d = HalenTestKit.Duel(HalenTestKit.Halen(null, HalenTestKit.Aug(CascadeLanding.Id, 1), HalenTestKit.Aug(CascadeBrake.Id, 1)));
            CascadeVaultTests.Vault(new Vec3(0f, 0f, -1f), d: d);
            SpellEvent start = Started(d);
            d.Run(3);
            d.Recast();
            CascadeVaultTests.Landed(d);
            d.Step();
            Assert.Equal(3f, Vec3.DistanceXZ(d.P0.Position, start.Position), 1); // 5 m range, 2 m brake
            Assert.Equal(1, d.Units(d.P0.Position + new Vec3(0.6f, 0f, 0f)).Water);
        }
    }
}
