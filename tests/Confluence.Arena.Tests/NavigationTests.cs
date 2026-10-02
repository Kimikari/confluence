using System.Collections.Generic;
using Confluence.Arena.Navigation;
using Confluence.Arena.Validation;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Arena.Tests
{
    public class NavigationTests
    {
        private static ArenaLayout Room(params Solid[] solids)
        {
            var layout = new ArenaLayout { Diameter = 24, Boundary = BoundaryContour.Circle(Vec3.Zero, 12f) };
            layout.Solids.Add(new Solid
            {
                Id = 0,
                Kind = SolidKind.Floor,
                Box = new YawBox(new Vec3(0f, -1f, 0f), new Vec3(10f, 1f, 10f), 0f),
                Anchored = true,
                Walkable = true,
                MirrorId = 0,
            });
            for (int i = 0; i < solids.Length; i++)
            {
                solids[i].Id = i + 1;
                layout.Solids.Add(solids[i]);
            }

            return layout;
        }

        private static Solid Wall(float x0, float z0, float x1, float z1, float height = 3f)
        {
            return new Solid
            {
                Kind = SolidKind.Wall,
                Box = new YawBox(new Vec3((x0 + x1) * 0.5f, height * 0.5f, (z0 + z1) * 0.5f), new Vec3((x1 - x0) * 0.5f, height * 0.5f, (z1 - z0) * 0.5f), 0f),
                Anchored = true,
            };
        }

        [Fact]
        public void OpenFloorPathMatchesStraightDistance()
        {
            ArenaNav nav = ArenaNav.Build(Room(), new ArenaNavOptions { HalfExtent = 10f });
            int a = nav.FindNode(new Vec3(-5f, 0f, 0f));
            int b = nav.FindNode(new Vec3(5f, 0f, 0f));
            nav.FindPath(a, b, NavQuery.Capsule, out float length);
            Assert.InRange(length, 10f, 10.6f);
        }

        [Fact]
        public void WallsForceADetourAndBlockSight()
        {
            ArenaNav nav = ArenaNav.Build(Room(Wall(-0.25f, -4f, 0.25f, 4f)), new ArenaNavOptions { HalfExtent = 10f });
            int a = nav.FindNode(new Vec3(-3f, 0f, 0f));
            int b = nav.FindNode(new Vec3(3f, 0f, 0f));
            nav.FindPath(a, b, NavQuery.Capsule, out float length);
            Assert.True(length > 9.5f); // around the 8 m wall
            Assert.False(nav.HasLineOfSight(new Vec3(-3f, 1.6f, 0f), new Vec3(3f, 1.6f, 0f)));
            Assert.True(nav.HasLineOfSight(new Vec3(-3f, 1.6f, 6f), new Vec3(3f, 1.6f, 6f)));
        }

        [Fact]
        public void ClearanceMeasuresCorridorWidth()
        {
            // A 3 m corridor between two walls.
            ArenaNav nav = ArenaNav.Build(Room(Wall(-6f, 1.5f, 6f, 2f), Wall(-6f, -2f, 6f, -1.5f)), new ArenaNavOptions { HalfExtent = 10f });
            int middle = nav.FindNode(new Vec3(0.25f, 0f, 0.25f));
            Assert.InRange(nav.Clearance(middle), 1.2f, 1.3f);
            int a = nav.FindNode(new Vec3(-5f, 0f, 0f));
            int b = nav.FindNode(new Vec3(5f, 0f, 0f));
            nav.FindPath(a, b, NavQuery.Route, out float routed);
            Assert.False(float.IsPositiveInfinity(routed)); // a 3 m corridor is a 2.5 m route

            ArenaNav narrow = ArenaNav.Build(Room(Wall(-6f, 0.9f, 6f, 1.4f), Wall(-6f, -1.4f, 6f, -0.9f)), new ArenaNavOptions { HalfExtent = 10f });
            int na = narrow.FindNode(new Vec3(-5f, 0f, 0f));
            int nb = narrow.FindNode(new Vec3(5f, 0f, 0f));
            bool[] reach = narrow.Reachable(new[] { na }, NavQuery.Route.WithAllowed(InBand(narrow, 0.9f)));
            Assert.False(reach[nb]); // 1.8 m is no route
            Assert.True(narrow.Reachable(new[] { na }, NavQuery.Capsule)[nb]); // but a capsule fits
        }

        private static bool[] InBand(ArenaNav nav, float halfWidth)
        {
            var mask = new bool[nav.NodeCount];
            for (int i = 0; i < nav.NodeCount; i++)
            {
                mask[i] = System.Math.Abs(nav.NodePosition(i).Z) < halfWidth;
            }

            return mask;
        }

        [Fact]
        public void RampsAndDecksAreWalkableWithinTheSlopeLimit()
        {
            var deck = new Solid
            {
                Kind = SolidKind.Platform,
                Box = new YawBox(new Vec3(4f, 1f, 0f), new Vec3(1.5f, 1f, 1.5f), 0f),
                Anchored = true,
                Walkable = true,
            };
            var ramp = new Solid
            {
                Kind = SolidKind.Ramp,
                Box = new YawBox(new Vec3(0.5f, 1f, 0f), new Vec3(1.5f, 1f, 2f), 90f), // rises toward +X over 4 m (26.6 degrees)
                RampRise = 2f,
                Anchored = true,
                Walkable = true,
            };
            ArenaNav nav = ArenaNav.Build(Room(deck, ramp), new ArenaNavOptions { HalfExtent = 10f });
            int floor = nav.FindNode(new Vec3(-3f, 0f, 0f));
            int top = nav.FindNode(new Vec3(4f, 2f, 0f), 0.3f);
            Assert.True(top >= 0);
            Assert.Equal(2f, nav.NodeHeight(top), 2);
            Assert.True(nav.Reachable(new[] { floor }, NavQuery.Capsule)[top]);
            Assert.True(nav.CanReach(new[] { floor }, NavQuery.Capsule)[top]); // and back down
        }

        [Fact]
        public void LedgesNeedTheCommonJumpAndTallDropsAreOneWay()
        {
            var ledge = new Solid
            {
                Kind = SolidKind.Platform,
                Box = new YawBox(new Vec3(3f, 0.5f, 0f), new Vec3(1.5f, 0.5f, 1.5f), 0f),
                Anchored = true,
                Walkable = true,
            };
            var tall = new Solid
            {
                Kind = SolidKind.Platform,
                Box = new YawBox(new Vec3(-4f, 1.25f, 0f), new Vec3(1.5f, 1.25f, 1.5f), 0f),
                Anchored = true,
                Walkable = true,
            };
            ArenaNav nav = ArenaNav.Build(Room(ledge, tall), new ArenaNavOptions { HalfExtent = 10f });
            int floor = nav.FindNode(new Vec3(0f, 0f, 0f));
            int ledgeTop = nav.FindNode(new Vec3(3f, 1f, 0f), 0.2f);
            int tallTop = nav.FindNode(new Vec3(-4f, 2.5f, 0f), 0.2f);
            bool[] reach = nav.Reachable(new[] { floor }, NavQuery.Capsule);
            Assert.True(reach[ledgeTop]); // 1 m common-jump ledge
            Assert.False(reach[tallTop]); // 2.5 m is no jump
            Assert.True(nav.Reachable(new[] { tallTop }, NavQuery.Capsule)[floor]); // but a safe drop down
            var noJumps = new NavQuery { AllowJumps = false };
            Assert.False(nav.Reachable(new[] { floor }, noJumps)[ledgeTop]);
        }

        [Fact]
        public void MirrorColumnIsTheHalfTurnImage()
        {
            ArenaNav nav = ArenaNav.Build(Room(), new ArenaNavOptions { HalfExtent = 10f });
            int c = nav.ColumnAt(new Vec2(3.3f, -2.1f));
            Vec2 p = nav.ColumnCenter(c);
            Vec2 q = nav.ColumnCenter(nav.MirrorColumn(c));
            Assert.Equal(-p.X, q.X, 4);
            Assert.Equal(-p.Y, q.Y, 4);
        }

        [Fact]
        public void BlockedNodesActLikeAFieldPlacement()
        {
            ArenaLayout layout = Room(Wall(-6f, 1.5f, 6f, 2f), Wall(-6f, -2f, 6f, -1.5f));
            ArenaNav nav = ArenaNav.Build(layout, new ArenaNavOptions { HalfExtent = 10f });
            var survey = new ArenaSurvey(layout, nav);
            var blocked = new bool[nav.NodeCount];
            survey.MarkDisc(blocked, new Vec2(0f, 0f), ArenaRules.RepresentativeFieldRadius + ArenaRules.CapsuleClearance, new Vec2(99f, 99f), 0f, 0f);
            int a = nav.FindNode(new Vec3(-5f, 0f, 0f));
            int b = nav.FindNode(new Vec3(5f, 0f, 0f));
            bool[] inside = InBand(nav, 1.5f);
            Assert.True(nav.Reachable(new[] { a }, NavQuery.Capsule.WithAllowed(inside))[b]);
            NavQuery sealedCorridor = NavQuery.Capsule.WithAllowed(inside).WithBlocked(blocked);
            Assert.False(nav.Reachable(new[] { a }, sealedCorridor)[b]); // a 3 m wall seals a 3 m corridor
        }

        [Fact]
        public void DistancesAreMultiSourceAndReturnInfinityWhenUnreachable()
        {
            ArenaNav nav = ArenaNav.Build(Room(), new ArenaNavOptions { HalfExtent = 10f });
            int a = nav.FindNode(new Vec3(-5f, 0f, 0f));
            int b = nav.FindNode(new Vec3(5f, 0f, 0f));
            int c = nav.FindNode(new Vec3(0f, 0f, 0f));
            float[] d = nav.Distances(new List<int> { a, b }, NavQuery.Capsule);
            Assert.InRange(d[c], 4.9f, 5.4f);
            var blockAll = new bool[nav.NodeCount];
            for (int i = 0; i < blockAll.Length; i++)
            {
                blockAll[i] = i != a;
            }

            Assert.True(float.IsPositiveInfinity(nav.Distances(new[] { a }, NavQuery.Capsule.WithBlocked(blockAll))[b]));
        }
    }
}
