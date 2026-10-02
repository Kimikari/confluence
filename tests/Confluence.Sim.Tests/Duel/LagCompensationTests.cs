using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;

namespace Confluence.Sim.Tests.Duel
{
    /// <summary>Hit-volume rewind for hitscan and melee validation, at most 150 ms (spec L423).</summary>
    public class LagCompensationTests
    {
        private static Capsule At(float x) => Capsule.Vertical(new Vec3(x, 0f, 0f), 1.8f, 0.4f);

        [Fact]
        public void History_Keeps_Nine_Ticks_And_Clamps_Older_Requests()
        {
            var history = new HitVolumeHistory(2, SimRules.LagCompensation.MaxRewindTicks);
            Assert.Equal(9, history.MaxRewindTicks);
            for (int tick = 1; tick <= 20; tick++)
            {
                history.Record(tick, new[] { At(tick), At(-tick) });
            }

            Assert.True(history.TryGet(0, 15, out Capsule v, out int used));
            Assert.Equal(15, used);
            Assert.Equal(15f, v.A.X, 4);
            Assert.True(history.TryGet(1, 3, out v, out used));
            Assert.Equal(11, used); // never more than 150 ms back
            Assert.Equal(-11f, v.A.X, 4);
            Assert.True(history.TryGet(0, 99, out _, out used));
            Assert.Equal(20, used);
        }

        [Fact]
        public void Hitscan_Rewinds_The_Target_To_The_Viewed_Tick_Within_150_Ms()
        {
            DuelWorld w = TestWorlds.Duel();
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, 6f), 180f);
            var positions = new Dictionary<int, Vec3>();
            PlayerInput strafe = TestWorlds.B.Moving(1f, 0f); // player 1 strafes toward -X at walk speed
            for (int i = 0; i < 40; i++)
            {
                w.Step(TestWorlds.A, strafe);
                positions[w.Tick] = w.Player(1).Position;
            }

            int now = w.Tick;
            Vec3 eye = w.Player(0).Eye;
            Vec3 seen = positions[now - 6] + new Vec3(0f, 1.2f, 0f);
            Assert.True(Vec3.DistanceXZ(positions[now - 6], positions[now]) > 0.5f);

            Assert.False(w.RaycastOpponent(0, eye, seen - eye, 30f, out _)); // without rewind the shot misses

            PlayerInput viewing = TestWorlds.A;
            viewing.ViewTick = now - 6;
            w.Step(viewing, strafe);
            positions[w.Tick] = w.Player(1).Position;
            Assert.True(w.RaycastOpponent(0, eye, seen - eye, 30f, out float distance));
            Assert.InRange(distance, 11f, 12.5f); // 12 m apart, hitting the near side of the capsule

            // A claim older than the window is clamped to 150 ms and does not reach the stale position.
            Vec3 stale = positions[now - 25] + new Vec3(0f, 1.2f, 0f);
            viewing.ViewTick = now - 25;
            w.Step(viewing, strafe);
            Assert.False(w.RaycastOpponent(0, eye, stale - eye, 30f, out _));
        }

        [Fact]
        public void Solids_Are_Tested_As_They_Are_Now()
        {
            DuelWorld w = TestWorlds.Duel();
            w.AddTemporarySolid(new Confluence.Sim.World.TemporarySolidSpec
            {
                Box = YawBox.OnFloor(new Vec3(0f, 0f, 0f), new Vec3(3f, 3f, 0.5f), 0f),
                Kind = Confluence.Core.Arena.SolidKind.Wall,
                BlocksSight = true,
                OwnerPlayer = -1,
            });
            PlayerInput viewing = TestWorlds.A;
            viewing.ViewTick = 1;
            TestWorlds.Run(w, 5, viewing, TestWorlds.B);
            Vec3 eye = w.Player(0).Eye;
            Vec3 chest = w.Player(1).Position + new Vec3(0f, 1.2f, 0f);
            Assert.False(w.RaycastOpponent(0, eye, chest - eye, 30f, out _));
        }
    }
}
