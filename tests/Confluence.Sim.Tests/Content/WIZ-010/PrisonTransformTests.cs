using System.Linq;
using Confluence.Casting;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Casting;
using Confluence.Sim.Combat;
using Confluence.Sim.Content.Wizards.HalenTide;
using Confluence.Sim.Duel;
using Confluence.Sim.Spells;
using Confluence.Sim.Status;
using Confluence.Sim.Tests.Spells;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Content.Halen
{
    /// <summary>AUG-445 Prison Roll and AUG-446 Prison Pool on SPL-079 Deepglass Prison, cast through WIZ-010's grammar.</summary>
    public class PrisonTransformTests
    {
        private static HalenDuel With(string augment, int rank, ArenaLayout layout = null)
        {
            var spells = new[] { DeepglassPrison.Id };
            return HalenTestKit.Duel(HalenTestKit.Halen(spells, HalenTestKit.Aug(augment, rank)), layout: layout);
        }

        private static TideProjectileEntity Bubble(HalenDuel d) => d.Runtime.Entities.OfType<TideProjectileEntity>().SingleOrDefault();

        // ---- AUG-445 Prison Roll -------------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 16f)]
        [InlineData(2, 20f)]
        [Verifies("AUG-445")]
        public void The_Bubble_Rolls_Along_The_Ground_And_Roots_For_06_Seconds_At_22_Damage(int rank, float speed)
        {
            HalenDuel d = DeepglassPrisonTests.Fire(With(PrisonRoll.Id, rank));
            d.RunUntil(() => d.SpellEvents(SpellEventKind.ProjectileLaunched).Any(), 60);
            SpellEvent launch = d.SpellEvents(SpellEventKind.ProjectileLaunched).Single();
            Assert.Equal(speed, launch.Amount, 3);
            Assert.True(launch.Position.Y < 0.5f, "the bubble starts on the ground, not at the eye");
            Assert.Equal(0f, launch.Direction.Y, 3);
            Assert.True(d.P0.Caster.Loadout.FormFor(DeepglassPrison.Id).HasTag("grounded"));

            d.Step();
            Assert.True(Bubble(d).Position.Y < 0.5f);
            d.RunUntil(() => DeepglassPrisonTests.HitOn(d) != null, 90);
            DamageEvent hit = DeepglassPrisonTests.HitOn(d);
            Assert.NotNull(hit);
            Assert.Equal(22f, hit.Dealt, 3); // drawback "damage 26 becomes 22"
            StatusInstance root = d.P1.Statuses.Find(StatusKind.Root, DeepglassPrison.Id);
            Assert.Equal(Ticks.FromSeconds(0.6), root.ExpireTick - root.AppliedTick); // "root 0.6 s unchanged"

            // The roll time matches the speed: 12 m minus the body radii at 16 or 20 m/s.
            int travel = hit.Tick - launch.Tick;
            Assert.InRange(travel, Ticks.FromSeconds(11f / speed) - 2, Ticks.FromSeconds(12f / speed) + 2);
        }

        [Fact]
        [Verifies("AUG-445")]
        public void A_Jump_Avoids_The_Rolling_Bubble()
        {
            HalenDuel d = DeepglassPrisonTests.Fire(With(PrisonRoll.Id, 1));
            d.RunUntil(() => Bubble(d) != null && Bubble(d).Position.Z > d.P1.Position.Z - 2.6f, 90);
            d.Step(InputButtons.None, InputButtons.Jump);
            d.Run(40);
            Assert.Null(DeepglassPrisonTests.HitOn(d));
            Assert.False(d.P1.Statuses.IsRooted);

            // The same timing against the flying bubble (chest height) does not save the target.
            HalenDuel flying = DeepglassPrisonTests.Fire();
            flying.RunUntil(() => Bubble(flying) != null && Bubble(flying).Position.Z > flying.P1.Position.Z - 2.6f, 90);
            flying.Step(InputButtons.None, InputButtons.Jump);
            flying.Run(40);
            Assert.NotNull(DeepglassPrisonTests.HitOn(flying));
        }

        [Fact]
        [Verifies("AUG-445")]
        public void A_Low_Block_On_The_Route_Stops_It_But_A_Kerb_Below_The_Step_Height_Does_Not()
        {
            // Counterplay "block rolling route": a 1 m crate in the lane stops the rolling bubble, a flying one passes over.
            Solid crate = TestWorlds.Box(70, new Vec3(0f, 0f, 1f), new Vec3(2f, 1f, 0.6f));
            HalenDuel blocked = DeepglassPrisonTests.Fire(With(PrisonRoll.Id, 2, HalenTestKit.Layout(crate)));
            blocked.Run(90);
            Assert.Null(DeepglassPrisonTests.HitOn(blocked));
            Assert.Equal(70, blocked.SpellEvents(SpellEventKind.ProjectileImpact).Single().SolidId);

            HalenDuel over = HalenTestKit.Duel(HalenTestKit.Halen(new[] { DeepglassPrison.Id }), layout: HalenTestKit.Layout(TestWorlds.Box(70, new Vec3(0f, 0f, 1f), new Vec3(2f, 1f, 0.6f))));
            DeepglassPrisonTests.Fire(over);
            over.RunUntil(() => DeepglassPrisonTests.HitOn(over) != null, 90);
            Assert.NotNull(DeepglassPrisonTests.HitOn(over));

            // A kerb lower than the step height is climbed.
            float kerb = GlobalRules.Movement.StepHeight * 0.5f;
            HalenDuel climb = DeepglassPrisonTests.Fire(With(PrisonRoll.Id, 1, HalenTestKit.Layout(TestWorlds.Box(71, new Vec3(0f, 0f, 2f), new Vec3(4f, kerb, 6f), SolidKind.Platform))));
            climb.RunUntil(() => DeepglassPrisonTests.HitOn(climb) != null, 90);
            Assert.NotNull(DeepglassPrisonTests.HitOn(climb));
        }

        [Fact]
        [Verifies("AUG-445")]
        public void The_Rolling_Root_Still_Obeys_Hard_Control_Immunity()
        {
            HalenDuel d = With(PrisonRoll.Id, 1);
            d.World.ApplyStatus(1, StatusApplication.Of(StatusKind.Stun, "SPL-TEST", 0, 6));
            d.Run(8);
            DeepglassPrisonTests.Fire(d);
            d.RunUntil(() => DeepglassPrisonTests.HitOn(d) != null, 90);
            Assert.Equal(22f, DeepglassPrisonTests.HitOn(d).Dealt, 3);
            Assert.Null(d.P1.Statuses.Find(StatusKind.Root, DeepglassPrison.Id));
        }

        // ---- AUG-446 Prison Pool -------------------------------------------------------------------------------

        [Theory]
        [InlineData(1, 1f, 2.0)]
        [InlineData(2, 1.5f, 3.0)]
        [Verifies("AUG-446")]
        public void After_The_Root_Ends_A_Water_1_Puddle_Forms_At_The_Impact_Instead_Of_The_Slow(int rank, float radius, double lifetime)
        {
            HalenDuel d = DeepglassPrisonTests.Fire(With(PrisonPool.Id, rank));
            d.RunUntil(() => DeepglassPrisonTests.HitOn(d) != null, 90);
            Vec3 impact = d.P1.Position;
            StatusInstance root = d.P1.Statuses.Find(StatusKind.Root, DeepglassPrison.Id);
            Assert.NotNull(root);
            Vec3 side = impact + new Vec3(0.7f, 0f, 0f);
            Assert.Equal(0, d.Units(side).Water); // nothing yet: the puddle waits for the root to end

            d.RunUntil(() => d.Tide(TideEventKind.PrisonReleased).Any(), 60);
            TideEvent released = d.Tide(TideEventKind.PrisonReleased).Single();
            Assert.Equal(root.ExpireTick, released.Tick);
            d.Step();
            Assert.Equal(1, d.Units(side).Water);                                          // "Water 1"
            Assert.Equal(rank >= 2 ? 1 : 0, d.Units(impact + new Vec3(1.3f, 0f, 0f)).Water); // 1 m vs 1.5 m
            Assert.Equal(1, d.Units(impact + new Vec3(-(radius - 0.3f), 0f, 0f)).Water);
            Assert.Equal(0, d.Units(impact + new Vec3(1.8f, 0f, 0f)).Water);
            Assert.Null(d.P1.Statuses.Find(StatusKind.Slow, DeepglassPrison.Id));          // "Post-root slow removed"

            d.RunUntil(() => d.World.Tick >= released.Tick + Ticks.FromSeconds(lifetime) - 6, Ticks.FromSeconds(4.0));
            Assert.Equal(1, d.Units(side).Water);
            d.Run(12);
            Assert.Equal(0, d.Units(side).Water);
        }

        [Fact]
        [Verifies("AUG-446")]
        public void The_Puddle_Is_No_Control_Chain_And_The_Target_Walks_Out_At_Full_Speed()
        {
            // Counterplay "Move away after immunity begins": no slow, immunity intact.
            HalenDuel d = DeepglassPrisonTests.Fire(With(PrisonPool.Id, 2));
            d.RunUntil(() => DeepglassPrisonTests.HitOn(d) != null, 90);
            d.RunUntil(() => d.Tide(TideEventKind.PrisonReleased).Any(), 60);
            d.Step();
            Assert.True(d.P1.Statuses.IsControlImmune(d.World.Tick));
            Assert.False(d.World.ApplyStatus(1, StatusApplication.Of(StatusKind.Root, "SPL-TEST", 0, 30)).Succeeded);
            d.Other = TestWorlds.B.Moving(1f, 0f);
            d.Run(20);
            Assert.False(d.P1.Statuses.IsRooted);
            Assert.Equal(GlobalRules.Movement.WalkSpeed, RiptideLaneTests.Speed(d.P1), 2);
            Assert.DoesNotContain(d.P1.Statuses.Active, s => s.Kind == StatusKind.Slow);
        }

        [Fact]
        [Verifies("AUG-446")]
        public void A_Refused_Root_Makes_The_Puddle_At_Once()
        {
            HalenDuel d = With(PrisonPool.Id, 1);
            d.World.ApplyStatus(1, StatusApplication.Of(StatusKind.Stun, "SPL-TEST", 0, 6));
            d.Run(8);
            DeepglassPrisonTests.Fire(d);
            d.RunUntil(() => DeepglassPrisonTests.HitOn(d) != null, 90);
            DamageEvent hit = DeepglassPrisonTests.HitOn(d);
            Vec3 impact = d.P1.Position;
            d.Run(2);
            TideEvent released = d.Tide(TideEventKind.PrisonReleased).Single();
            Assert.Equal(0f, released.Amount);
            Assert.InRange(released.Tick, hit.Tick, hit.Tick + 1);
            Assert.Equal(1, d.Units(impact + new Vec3(0.7f, 0f, 0f)).Water);
            Assert.Null(d.P1.Statuses.Find(StatusKind.Slow, DeepglassPrison.Id));
        }

        [Fact]
        [Verifies("AUG-445", "AUG-446")]
        public void Roll_And_Pool_Combine()
        {
            HalenDuel d = HalenTestKit.Duel(HalenTestKit.Halen(new[] { DeepglassPrison.Id }, HalenTestKit.Aug(PrisonRoll.Id, 1), HalenTestKit.Aug(PrisonPool.Id, 1)));
            DeepglassPrisonTests.Fire(d);
            d.RunUntil(() => DeepglassPrisonTests.HitOn(d) != null, 90);
            Assert.Equal(22f, DeepglassPrisonTests.HitOn(d).Dealt, 3);
            Vec3 impact = d.P1.Position;
            d.RunUntil(() => d.Tide(TideEventKind.PrisonReleased).Any(), 60);
            d.Step();
            Assert.Equal(1, d.Units(impact + new Vec3(0.7f, 0f, 0f)).Water);
            Assert.Null(d.P1.Statuses.Find(StatusKind.Slow, DeepglassPrison.Id));
        }
    }
}
