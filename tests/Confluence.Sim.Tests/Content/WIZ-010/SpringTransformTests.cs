using System.Linq;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Time;
using Confluence.Reactions;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Casting;
using Confluence.Sim.Combat;
using Confluence.Sim.Content.Wizards.HalenTide;
using Confluence.Sim.Duel;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;

namespace Confluence.Sim.Tests.Content.Halen
{
    /// <summary>AUG-447 Spring Fan and AUG-448 Spring Steam on SPL-080 Seal the Spring, cast through WIZ-010's grammar.</summary>
    public class SpringTransformTests
    {
        // A one-cell-wide strip of heat 2 straight ahead of Halen (feet at (0, -6), facing +Z), from z = -4 to z = 3.
        private static readonly Footprint Strip = Footprint.Rectangle(new Vec2(0.25f, -0.5f), new Vec2(0.2f, 3.5f), 0f);

        private static HalenDuel Fan(int rank, params Footprint[] extraHeat)
        {
            HalenDuel d = SealTheSpringTests.Ready(HalenTestKit.Aug(SpringFan.Id, rank));
            d.Deposit(Material.Heat, 2, Strip);
            foreach (Footprint f in extraHeat) d.Deposit(Material.Heat, 2, f);
            d.Step();
            return d;
        }

        private static void Release(HalenDuel d)
        {
            d.Cast(0);
            d.RunUntil(() => d.Tide(TideEventKind.SpringNeutralized).Any(), 60);
            d.Step();
        }

        private static int HeatAt(HalenDuel d, float x, float z) => d.Units(new Vec3(x, 0f, z)).Heat;

        // ---- AUG-447 Spring Fan --------------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 6f)]
        [InlineData(2, 8f)]
        [Verifies("AUG-447")]
        public void A_Ground_Cone_From_The_Feet_Removes_One_Unit_Per_Cell_Within_Its_Reach(int rank, float reach)
        {
            HalenDuel d = Fan(rank);
            d.Halen = TestWorlds.A;
            Release(d);
            for (float z = -3.75f; z < 3f; z += 0.5f)
            {
                float distance = z + 6f;
                int expected = distance <= reach - 0.3f ? 1 : distance >= reach + 0.3f ? 2 : -1;
                if (expected < 0) continue;
                Assert.True(expected == HeatAt(d, 0.25f, z), "z " + z + ": heat " + HeatAt(d, 0.25f, z) + ", expected " + expected);
            }

            TideEvent done = d.Tide(TideEventKind.SpringNeutralized).Single();
            Assert.True(done.Amount >= (reach - 2.5f) * 2f - 1f);  // one unit from every covered strip cell
            Assert.True(d.P0.Caster.Loadout.FormFor(SealTheSpring.Id).HasTag("cone"));
            Assert.Contains(d.SpellEvents(SpellEventKind.ConeReleased), e => e.Amount == reach);
        }

        [Fact]
        [Verifies("AUG-447")]
        public void The_Cone_Keeps_The_Framework_90_Degree_Angle_And_Needs_No_Surface()
        {
            Footprint inside = Footprint.Point(new Vec2(0.75f, -3.25f));  // 15 degrees off the axis
            Footprint outside = Footprint.Point(new Vec2(2.25f, -4.75f)); // 61 degrees off the axis
            HalenDuel d = Fan(1, inside, outside);
            d.Halen = TestWorlds.A.Facing(0f, 35f); // aimed at the sky: the cone still runs along the ground
            Release(d);
            Assert.Equal(1, HeatAt(d, 0.75f, -3.25f));
            Assert.Equal(2, HeatAt(d, 2.25f, -4.75f));
            Assert.Empty(d.Tide(TideEventKind.SpringNoSurface));
        }

        [Fact]
        [Verifies("AUG-447")]
        public void Focused_Removal_2_Becomes_1_And_Mana_Rises_By_3()
        {
            // A single strength-2 cell next to the feet loses only 1 (the base splash would take both).
            Footprint cell = Footprint.Point(new Vec2(0.25f, -4.25f));
            HalenDuel d = SealTheSpringTests.Ready(HalenTestKit.Aug(SpringFan.Id, 1));
            d.Deposit(Material.Heat, 2, cell);
            d.Deposit(Material.Venom, 1, Footprint.Point(new Vec2(-0.25f, -3.75f)));
            d.Step();
            d.Halen = TestWorlds.A;
            CastDuelEvent commit = d.Cast(0);
            Assert.Equal(29f, commit.Cast.Mana, 3); // "mana +3"
            d.RunUntil(() => d.Tide(TideEventKind.SpringNeutralized).Any(), 60);
            Assert.Equal(commit.Tick + Ticks.FromSeconds(0.6), d.Tide(TideEventKind.SpringNeutralized).Single().Tick);
            d.Step();
            Assert.Equal(1, HeatAt(d, 0.25f, -4.25f));
            Assert.Equal(0, d.Units(new Vec3(-0.25f, 0f, -3.75f)).Venom); // corrosive cells too
            Assert.Equal(0, d.Total(Material.Water, Footprint.Circle(new Vec2(0f, -3f), 4f)));
        }

        [Fact]
        [Verifies("AUG-447")]
        public void Spreading_The_Hazard_Or_Striking_During_The_Windup_Beats_It()
        {
            // Counterplay "Spread hazard or attack during cone windup": a hazard past the reach is untouched and the 0.6 s
            // windup grants no protection.
            HalenDuel d = SealTheSpringTests.Ready(HalenTestKit.Aug(SpringFan.Id, 1));
            Footprint far = Footprint.Circle(new Vec2(0f, 2f), 1f);
            d.Deposit(Material.Heat, 2, far);
            d.Step();
            int before = d.Total(Material.Heat, far);
            d.Halen = TestWorlds.A;
            d.Cast(0);
            DamageEvent hit = d.World.ApplyDamage(TestWorlds.Hit(d.World, 1, 0, 20f));
            Assert.Equal(20f, hit.Dealt, 3);
            d.RunUntil(() => d.Tide(TideEventKind.SpringNeutralized).Any(), 60);
            d.Step();
            Assert.Equal(before, d.Total(Material.Heat, far));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [Verifies("AUG-447")]
        public void The_Cone_Keeps_The_Visible_Field_Rule_A_Wall_Shields_The_Hazard_Behind_It(int rank)
        {
            // The branch replaces only the splash (L541): SPL-080's "one visible hot/corrosive ground field" still holds,
            // so heat behind a 3 m wall inside the cone keeps every unit while heat in front of the wall loses one per cell.
            HalenDuel d = HalenTestKit.Duel(HalenTestKit.Halen(new[] { SealTheSpring.Id }, HalenTestKit.Aug(SpringFan.Id, rank)),
                layout: HalenTestKit.Layout(SpellTestKit.Wall(50, new Vec3(0f, 0f, -3.5f), new Vec3(6f, 3f, 0.5f))));
            Footprint hidden = Footprint.Circle(new Vec2(0f, -1.5f), 0.6f);
            Footprint near = Footprint.Point(new Vec2(0.25f, -4.75f));
            d.Deposit(Material.Heat, 2, hidden);
            d.Deposit(Material.Heat, 2, near);
            d.Step();
            int hiddenBefore = d.Total(Material.Heat, hidden);
            Assert.True(hiddenBefore >= 4);
            d.Halen = TestWorlds.A;
            Release(d);
            Assert.Equal(hiddenBefore, d.Total(Material.Heat, hidden));
            Assert.Equal(1, HeatAt(d, 0.25f, -4.75f));
            Assert.Equal(1f, d.Tide(TideEventKind.SpringNeutralized).Single().Amount);
        }

        // ---- AUG-448 Spring Steam ------------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 1.5f, 2.0)]
        [InlineData(2, 2f, 3.0)]
        [Verifies("AUG-448")]
        public void Neutralizing_Heat_Leaves_A_Steam_Cloud_And_No_Water(int rank, float radius, double lifetime)
        {
            HalenDuel d = SealTheSpringTests.Ready(HalenTestKit.Aug(SpringSteam.Id, rank));
            Footprint hot = Footprint.Circle(Vec2.Zero, 1f);
            d.Deposit(Material.Heat, 2, hot);
            d.Step();
            int before = d.Total(Material.Heat, hot);
            SealTheSpringTests.Splash(d, Vec3.Zero);
            Assert.Equal(before - 2, d.Total(Material.Heat, hot)); // still up to 2 units
            ObscurationVolume steam = d.Reactions.Volumes.Single();
            Assert.Equal(VolumeKind.Steam, steam.Kind);
            Assert.Equal(0, steam.Owner);
            Assert.Equal(Ticks.FromSeconds(lifetime), steam.ExpiresTick - steam.CreatedTick);
            steam.GetBounds(out Vec2 min, out Vec2 max);
            Assert.InRange(max.X - min.X, 2f * radius - 0.6f, 2f * radius + 0.01f);
            Assert.True(steam.ContainsPoint(new Vec3(0f, 1f, 0f)));
            Assert.Equal(0, d.Total(Material.Water, Footprint.Circle(Vec2.Zero, 3f))); // "Water 2 still consumed"
            Assert.Contains(d.World.Owned.Entities, e => e.Owner == 0 && e.DefinitionId.StartsWith("CLOUD:"));
            d.Run(Ticks.FromSeconds(lifetime) + 2);
            Assert.Empty(d.Reactions.Volumes);
        }

        [Fact]
        [Verifies("AUG-448")]
        public void Corrosion_Alone_Is_Untouched_Without_Heat_To_Neutralize()
        {
            HalenDuel d = SealTheSpringTests.Ready(HalenTestKit.Aug(SpringSteam.Id, 1));
            Footprint venom = Footprint.Circle(new Vec2(0f, 1f), 1f);
            d.Deposit(Material.Venom, 1, venom);
            d.Step();
            int before = d.Total(Material.Venom, venom);
            SealTheSpringTests.Splash(d, new Vec3(0f, 0f, 1f));
            Assert.Equal(before, d.Total(Material.Venom, venom)); // drawback
            Assert.Empty(d.Reactions.Volumes);
            Assert.Equal(0f, d.Tide(TideEventKind.SpringNeutralized).Single().Amount);

            // With heat in the same patch the neutralization is real and corrosion goes with it.
            HalenDuel mixed = SealTheSpringTests.Ready(HalenTestKit.Aug(SpringSteam.Id, 1));
            mixed.Deposit(Material.Heat, 1, Footprint.Point(new Vec2(0.25f, 0.25f)));
            mixed.Deposit(Material.Venom, 1, Footprint.Point(new Vec2(0.25f, 0.75f)));
            mixed.Step();
            SealTheSpringTests.Splash(mixed, new Vec3(0.25f, 0f, 0.25f));
            Assert.Equal(0, mixed.Units(new Vec3(0.25f, 0f, 0.25f)).Heat);
            Assert.Equal(0, mixed.Units(new Vec3(0.25f, 0f, 0.75f)).Venom);
            Assert.Single(mixed.Reactions.Volumes);
        }

        [Fact]
        [Verifies("AUG-448")]
        public void The_Steam_Can_Be_Shot_Through_Or_Cleared_By_Wind()
        {
            // Counterplay "Wind-clear or shoot through": steam conceals but never stops a projectile (spec L167), and a
            // gust disperses it (spec L151).
            HalenDuel d = SealTheSpringTests.Ready(HalenTestKit.Aug(SpringSteam.Id, 2));
            d.Deposit(Material.Heat, 2, Footprint.Circle(Vec2.Zero, 1f));
            d.Step();
            SealTheSpringTests.Splash(d, Vec3.Zero);
            Assert.True(d.World.Materials.ObscuringPathLength(new Vec3(0f, 1.2f, -3f), new Vec3(0f, 1.2f, 3f)) >= 2f);
            d.RunUntil(() => SpellTestKit.CastEvents(d.World, CastEventKind.RecoveryEnded).Any(), 60);
            d.Run(2);
            d.AimAtOther();
            d.Cast(1); // SPL-073 Pressure Jet straight through the steam
            d.RunUntil(() => d.DamageTo(1, PressureJet.Id).Any(), 90);
            Assert.Single(d.DamageTo(1, PressureJet.Id));

            ObscurationVolume steam = d.Reactions.Volumes.Single();
            int columns = steam.ColumnCount;
            d.Reactions.Wind(new WindGust { Area = Footprint.Circle(Vec2.Zero, 4f), Direction = new Vec2(1f, 0f), Strength = 2, Owner = 1, SourceEventId = 0 });
            d.Step();
            Assert.True(d.Reactions.Volumes.Count == 0 || d.Reactions.Volumes.Single().ColumnCount < columns);
        }

        [Fact]
        [Verifies("AUG-447", "AUG-448")]
        public void Fan_And_Steam_Combine_The_Cloud_Forms_At_The_Nearest_Neutralized_Heat()
        {
            HalenDuel d = SealTheSpringTests.Ready(HalenTestKit.Aug(SpringFan.Id, 1), HalenTestKit.Aug(SpringSteam.Id, 1));
            d.Deposit(Material.Heat, 2, Strip);
            d.Step();
            d.Halen = TestWorlds.A;
            Release(d);
            ObscurationVolume steam = d.Reactions.Volumes.Single();
            Assert.True(steam.ContainsPoint(new Vec3(0.25f, 1f, -3.75f)));
            Assert.Equal(1, HeatAt(d, 0.25f, -2.75f));
        }
    }
}
