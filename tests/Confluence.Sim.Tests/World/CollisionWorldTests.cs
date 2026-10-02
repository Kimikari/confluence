using System.Collections.Generic;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Sim.Duel;
using Confluence.Sim.Movement;
using Confluence.Sim.World;

namespace Confluence.Sim.Tests.World
{
    /// <summary>Collision and sight queries over layout and temporary solids (spec L134, L167, L171).</summary>
    public class CollisionWorldTests
    {
        private static TemporarySolidSpec Wall(Vec3 floorCenter, Vec3 size, float durability = 0f, int lifetime = 0, int owner = -1)
        {
            return new TemporarySolidSpec
            {
                Box = YawBox.OnFloor(floorCenter, size, 0f),
                Kind = SolidKind.Wall,
                BlocksSight = true,
                Durability = durability,
                LifetimeTicks = lifetime,
                OwnerPlayer = owner,
                SourceId = "SPL-WALL",
            };
        }

        [Fact]
        public void Raycast_And_Sphere_Sweep_Find_The_Nearest_Face()
        {
            CollisionWorld world = TestWorlds.Collision(TestWorlds.Box(2, new Vec3(0f, 0f, 5f), new Vec3(4f, 3f, 1f)));
            Vec3 origin = new Vec3(0f, 1f, 0f);
            Assert.True(world.Raycast(origin, new Vec3(0f, 0f, 1f), 20f, SolidMask.Movement, out WorldHit hit));
            Assert.Equal(4.5f, hit.Distance, 4);
            Assert.Equal(-1f, hit.Normal.Z, 4);
            Assert.Equal(2, hit.SolidId);
            Assert.False(world.Raycast(origin, new Vec3(0f, 0f, 1f), 4f, SolidMask.Movement, out _));

            Assert.True(world.SphereSweep(origin, origin + new Vec3(0f, 0f, 10f), 0.5f, SolidMask.Movement, out WorldHit sweep));
            Assert.Equal(4.0f, sweep.Distance, 3);
            Assert.Equal(4.5f, sweep.Contact.Z, 3);
        }

        [Fact]
        public void Overlap_Queries_Report_Touching_Solids()
        {
            CollisionWorld world = TestWorlds.Collision(TestWorlds.Box(2, new Vec3(0f, 0f, 1f), new Vec3(1f, 2f, 1f), SolidKind.Pillar));
            var ids = new List<int>();
            world.OverlapCapsule(Capsule.Vertical(new Vec3(0f, 0.1f, 0.2f), 1.8f, 0.4f), SolidMask.Movement, ids);
            Assert.Equal(new[] { 2 }, ids.ToArray());
            ids.Clear();
            Assert.Equal(0, world.OverlapSphere(new Vec3(0f, 1f, -2f), 0.5f, SolidMask.Movement, ids));
        }

        [Fact]
        public void Sight_Is_Blocked_Only_By_Sight_Blocking_Solids()
        {
            Solid glass = TestWorlds.Box(3, new Vec3(0f, 0f, 3f), new Vec3(4f, 3f, 0.2f));
            glass.BlocksSight = false;
            CollisionWorld world = TestWorlds.Collision(glass);
            Vec3 a = new Vec3(0f, 1.5f, 0f);
            Vec3 b = new Vec3(0f, 1.5f, 6f);
            Assert.True(world.HasLineOfSight(a, b));
            Assert.True(world.SegmentCast(a, b, SolidMask.Movement, out _)); // it still blocks movement and strikes

            CollisionWorld walled = TestWorlds.Collision(TestWorlds.Box(3, new Vec3(0f, 0f, 3f), new Vec3(4f, 3f, 0.2f)));
            Assert.False(walled.HasLineOfSight(a, b));
        }

        [Fact]
        public void Temporary_Solids_Take_Durability_Damage_Break_And_Expire()
        {
            DuelWorld w = TestWorlds.Duel();
            int wall = w.AddTemporarySolid(Wall(new Vec3(0f, 0f, 0f), new Vec3(4f, 2.5f, 0.5f), durability: 60f, owner: 1));
            int timed = w.AddTemporarySolid(Wall(new Vec3(5f, 0f, 0f), new Vec3(1f, 2.5f, 0.5f), lifetime: 30));
            int anchored = w.AddTemporarySolid(Wall(new Vec3(-5f, 0f, 0f), new Vec3(1f, 2.5f, 0.5f)));
            Vec3 eye = w.Player(0).Eye;
            Assert.False(w.HasClearSight(eye, w.Player(1).Eye));

            Assert.False(w.DamageSolid(wall, 30f, 0));
            Assert.Equal(30f, w.Collision.Find(wall).Durability, 4);
            Assert.True(w.DamageSolid(wall, 45f, 0));
            Assert.False(w.Collision.Find(wall).Active);
            Assert.True(w.HasClearSight(eye, w.Player(1).Eye)); // collision changes in the same update
            Assert.Equal(1, w.Player(0).Feats.CoversBroken);
            Assert.False(w.DamageSolid(anchored, 500f, 0));

            TestWorlds.Run(w, 30, TestWorlds.A, TestWorlds.B);
            Assert.False(w.Collision.Find(timed).Active);
            Assert.Contains(w.EventLog, e => e is SolidEvent s && s.Change == SolidChange.Expired && s.SolidId == timed);
        }

        [Fact]
        public void Layout_Terrain_Is_Anchored()
        {
            CollisionWorld world = TestWorlds.Collision();
            Assert.False(world.Remove(TestWorlds.FloorId));
            Assert.False(world.DamageSolid(TestWorlds.FloorId, 1000f, out _));
            Assert.True(world.Find(TestWorlds.FloorId).Active);
        }

        [Fact]
        public void A_Wall_Formed_Under_A_Player_Raises_Them_At_Most_Half_A_Meter()
        {
            DuelWorld w = TestWorlds.Duel();
            Vec3 feet = w.Player(0).Position;
            int low = w.AddTemporarySolid(Wall(feet, new Vec3(2f, 0.4f, 2f), durability: 50f));
            Assert.True(low > 0);
            Assert.Equal(0.4f, w.Player(0).Position.Y, 4);
            Assert.Contains(w.EventLog, e => e is MovementEvent m && m.Change == MovementChange.RaisedByTerrain);
            Assert.True(w.Collision.IsCapsuleClear(w.Player(0).HitVolume, 2e-3f));

            Vec3 other = w.Player(1).Position;
            int tall = w.AddTemporarySolid(Wall(other, new Vec3(2f, 1.5f, 2f), durability: 50f));
            Assert.Equal(-1, tall); // it would trap the player: the placement fails for a refund
            Assert.Equal(other, w.Player(1).Position);
            Assert.True(w.HasClearSight(w.Player(0).Eye, w.Player(1).Eye));
        }

        [Fact]
        public void Ramp_Surface_Rises_Linearly_And_Steep_Ramps_Are_Not_Standable()
        {
            CollisionWorld world = TestWorlds.Collision(TestWorlds.Ramp(2, new Vec3(0f, 0f, 3f), 1.5f, 2f, 2f));
            CollisionSolid ramp = world.Find(2);
            Assert.Equal(0f, ramp.SurfaceHeightAt(0f, 1f), 3);
            Assert.Equal(1f, ramp.SurfaceHeightAt(0f, 3f), 3);
            Assert.Equal(2f, ramp.SurfaceHeightAt(0f, 5f), 3);
            Assert.True(CharacterMotor.IsWalkable(ramp.Ramp.SurfaceNormal)); // 26.6 degrees

            Assert.False(CharacterMotor.IsWalkable(new Vec3(0f, 0.8f, -0.6f))); // about 37 degrees
            Assert.True(GlobalRules.Arena.RampMaxSlopeDeg <= 30f);
        }

        [Fact]
        public void Nearest_Standable_Point_Respects_The_Legal_Boundary()
        {
            ArenaLayout layout = TestWorlds.Flat(boundaryRadius: 10f, floorHalf: 20f);
            DuelWorld w = TestWorlds.Duel(layout);
            Assert.True(w.Collision.TryFindNearestStandablePoint(new Vec3(15f, 0f, 0f), CharacterMotor.Radius, 1.8f, w.LegalBoundary, out Vec3 feet));
            Assert.True(w.LegalBoundary.Contains(feet));
            Assert.InRange(feet.X, 9f, 10f);
        }
    }
}
