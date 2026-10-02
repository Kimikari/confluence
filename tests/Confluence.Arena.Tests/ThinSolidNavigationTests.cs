using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Arena.Generation;
using Confluence.Arena.Navigation;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Arena.Tests
{
    /// <summary>
    /// Navigation sees solids at their true size, however thin (spec L312 route width, L308 functional symmetry; the
    /// Sim motor collides with exact boxes). Walls, plates, and cover thinner than the 0.5 m lattice used to slip
    /// between column centers: walk edges crossed them and clearance ignored them.
    /// </summary>
    public class ThinSolidNavigationTests
    {
        private static readonly float[] Yaws = { 0f, 12.5f, 30f, 45f, 63.4f, 90f, 117.3f, 151f };
        private static readonly float[] Offsets = { 0f, 0.1f, 0.25f, 0.37f, 0.5f };
        private static readonly float[] Thicknesses = { 0.04f, 0.15f, 0.3f };

        private static readonly NavQuery AnyNode = new NavQuery { MinClearance = 0f };

        /// <summary>Distance within which navigation measures clearance to obstacle footprints exactly.</summary>
        private const float ExactRadius = 1.5f;

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
                solids[i].MirrorId = -1;
                layout.Solids.Add(solids[i]);
            }

            return layout;
        }

        /// <summary>A plate of the given length and thickness through a ground point, spanning heights bottom..top.</summary>
        private static Solid Plate(Vec2 center, float length, float thickness, float bottom, float top, float yawDeg)
        {
            return new Solid
            {
                Kind = SolidKind.Wall,
                Box = new YawBox(new Vec3(center.X, (bottom + top) * 0.5f, center.Y), new Vec3(length * 0.5f, (top - bottom) * 0.5f, thickness * 0.5f), yawDeg),
                Anchored = true,
                BlocksSight = true,
            };
        }

        /// <summary>Unit normal of a plate at a yaw (its local +Z in world space).</summary>
        private static Vec2 Normal(float yawDeg)
        {
            Vec3 n = new YawBox(Vec3.Zero, Vec3.One, yawDeg).DirectionToWorld(new Vec3(0f, 0f, 1f));
            return new Vec2(n.X, n.Z);
        }

        private static ArenaNav Build(ArenaLayout layout, bool props = true)
        {
            return ArenaNav.Build(layout, new ArenaNavOptions { HalfExtent = 10f, PropsAsObstacles = props });
        }

        private static bool Connected(ArenaNav nav, Vec2 a, Vec2 b)
        {
            int na = nav.FindNode(new Vec3(a.X, 0f, a.Y));
            int nb = nav.FindNode(new Vec3(b.X, 0f, b.Y));
            Assert.True(na >= 0 && nb >= 0);
            return nav.Reachable(new[] { na }, AnyNode)[nb];
        }

        /// <summary>Horizontal distance from a node to a footprint.</summary>
        private static float FootprintDistance(ArenaNav nav, int node, YawBox box)
        {
            Vec2 p = nav.ColumnCenter(nav.NodeColumn(node));
            return Vec2.Distance(p, box.ClosestPointXZ(p));
        }

        [Fact]
        public void ThinWallsSealTheFloorAtAnyYawAndOffset()
        {
            foreach (float yaw in Yaws)
            {
                Vec2 n = Normal(yaw);
                foreach (float offset in Offsets)
                {
                    foreach (float thickness in Thicknesses)
                    {
                        // A 2.5 m wall longer than the floor, so the two halves connect only through it.
                        Solid wall = Plate(n * offset, 40f, thickness, 0f, 2.5f, yaw);
                        ArenaNav nav = Build(Room(wall));
                        string label = "yaw " + yaw + ", offset " + offset + ", thickness " + thickness;
                        Assert.False(Connected(nav, n * (offset - 3f), n * (offset + 3f)), label);

                        // Clearance is measured to the true footprint: within the exact radius (more than the 1.25 m
                        // route half-width) it never exceeds the distance to the wall.
                        for (int node = 0; node < nav.NodeCount; node++)
                        {
                            float d = FootprintDistance(nav, node, wall.Box);
                            Assert.True(d >= ExactRadius || nav.Clearance(node) <= d + 1e-3f, label + ": clearance " + nav.Clearance(node) + " beyond wall at " + d);
                        }
                    }
                }
            }
        }

        [Fact]
        public void LowCoverAndHangingPlatesBlockButOverheadAndFlushPlatesDoNot()
        {
            const float yaw = 33f;
            Vec2 n = Normal(yaw);
            Vec2 c = n * 0.19f;
            Vec2 a = c - n * 3f;
            Vec2 b = c + n * 3f;

            // Waist-high cover 0.2 m thick (center cover is 0.4 m by 1 m, MOD-065) and a hanging acoustic plate.
            Assert.False(Connected(Build(Room(Plate(c, 40f, 0.2f, 0f, 1f, yaw))), a, b));
            Assert.False(Connected(Build(Room(Plate(c, 40f, 0.1f, 0.6f, 2.6f, yaw))), a, b));
            Assert.False(Connected(Build(Room(Plate(c, 40f, 0.1f, GlobalRules.Movement.StandingHeight - 0.05f, 3f, yaw))), a, b));

            // A plate above standing height, a strip flush with the floor, and a low curb under the step height are
            // walked under, over, or stepped onto.
            Assert.True(Connected(Build(Room(Plate(c, 40f, 0.1f, GlobalRules.Movement.StandingHeight + 0.1f, 3f, yaw))), a, b));
            Assert.True(Connected(Build(Room(Plate(c, 40f, 0.1f, 0f, 0.04f, yaw))), a, b));
            Solid curb = Plate(c, 40f, 0.3f, 0f, 0.25f, yaw);
            curb.Walkable = true;
            Assert.True(Connected(Build(Room(curb)), a, b));
        }

        [Fact]
        public void CapsuleRoutesKeepTheCapsuleRadiusFromThinCover()
        {
            foreach (float yaw in Yaws)
            {
                Vec2 n = Normal(yaw);
                Vec2 center = new Vec2(0.13f, 0.37f);
                Solid cover = Plate(center, 3f, 0.15f, 0f, 1.2f, yaw);
                ArenaNav nav = Build(Room(cover));
                int a = nav.FindNode(new Vec3(center.X - n.X * 2.5f, 0f, center.Y - n.Y * 2.5f));
                int b = nav.FindNode(new Vec3(center.X + n.X * 2.5f, 0f, center.Y + n.Y * 2.5f));
                List<int> path = nav.FindPath(a, b, NavQuery.Capsule, out float length);
                Assert.NotNull(path);

                // The straight line is 5 m; going around a 3 m plate with 0.4 m of room costs well over that.
                Assert.True(length > 5.8f, "yaw " + yaw + ": path " + length + " m through the cover");
                foreach (int node in path)
                {
                    Assert.True(FootprintDistance(nav, node, cover.Box) >= ArenaRules.CapsuleClearance - 1e-3f, "yaw " + yaw + ": node within the capsule radius of the cover");
                }
            }
        }

        [Fact]
        public void PropsBlockThroughTheirStoredCollisionBox()
        {
            ArenaLayout layout = Room();
            var prop = new PropPlacement
            {
                Id = 0,
                PropId = "PROP-014",
                Position = new Vec3(0.21f, 0f, 0f),
                YawDeg = 71f,
                MirrorId = -1,
                HasCollision = true,
                CollisionBox = new YawBox(new Vec3(0.21f, 1f, 0f), new Vec3(20f, 1f, 0.05f), 71f),
            };
            layout.Props.Add(prop);
            Vec2 n = Normal(71f);
            Vec2 a = n * -3f;
            Vec2 b = n * 3f;

            Assert.False(Connected(Build(layout), a, b));
            Assert.True(Connected(Build(layout, props: false), a, b));
            prop.HasCollision = false;
            Assert.True(Connected(Build(layout), a, b));
        }

        /// <summary>
        /// Thin walls and their half-turn images (yaw a and a + 180, at offsets that put faces on and near column
        /// centers and edges) keep the graph, clearance, and distances exact mirror images (spec L308).
        /// </summary>
        [Fact]
        public void MirroredThinSolidsKeepTheGraphExactlySymmetric()
        {
            var pieces = new List<Solid>();
            var centers = new[] { new Vec2(-3.25f, -4f), new Vec2(4.6f, -1.75f), new Vec2(-0.5f, -6.13f), new Vec2(2f, -0.25f) };
            for (int i = 0; i < centers.Length; i++)
            {
                float yaw = Yaws[(i * 3 + 1) % Yaws.Length];
                float thickness = Thicknesses[i % Thicknesses.Length];
                pieces.Add(Plate(centers[i], 3f, thickness, 0f, 1f + i * 0.5f, yaw));
                pieces.Add(Plate(MirrorMath.Point(centers[i]), 3f, thickness, 0f, 1f + i * 0.5f, MirrorMath.Yaw(yaw)));
            }

            ArenaLayout layout = Room(pieces.ToArray());
            for (int i = 1; i < layout.Solids.Count; i += 2)
            {
                layout.Solids[i].MirrorId = layout.Solids[i + 1].Id;
                layout.Solids[i + 1].MirrorId = layout.Solids[i].Id;
            }

            ArenaNav nav = Build(layout);
            Assert.True(nav.IsHalfTurnSymmetric);
            for (int node = 0; node < nav.NodeCount; node++)
            {
                Assert.Equal(nav.Clearance(node), nav.Clearance(nav.MirrorNode(node)));
            }

            int s0 = nav.FindNode(new Vec3(0f, 0f, -8f));
            int s1 = nav.FindNode(new Vec3(0f, 0f, 8f));
            Assert.Equal(s1, nav.MirrorNode(s0));
            float[] d0 = nav.Distances(new[] { s0 }, NavQuery.Capsule);
            float[] d1 = nav.Distances(new[] { s1 }, NavQuery.Capsule);
            for (int node = 0; node < nav.NodeCount; node++)
            {
                Assert.Equal(d0[node], d1[nav.MirrorNode(node)]);
            }
        }

        /// <summary>
        /// In released arenas (every biome sample, plus match seed 7919 at 24 m whose 0.4 m by 1 m MOD-065 center cover
        /// stopped a Sim walker on a navigation path), no walk edge passes through a non-walkable solid or a prop's
        /// collision box, and no node claims more clearance than its distance to such a footprint. The check samples
        /// every edge every 5 mm against the layout's boxes, independently of the navigation's own geometry.
        /// </summary>
        [Fact]
        public void ReleasedArenasHaveNoEdgeThroughASolidAndTrueClearance()
        {
            var layouts = ArenaFixture.BiomeSamples.Select(a => a.Layout).ToList();
            layouts.Add(ArenaFixture.Generator.Generate(new ArenaRequest { MatchSeed = 7919UL, Round = 1, Diameter = 24 }).Layout);
            var problems = new List<string>();
            foreach (ArenaLayout layout in layouts)
            {
                ArenaNav nav = ArenaNav.Build(layout);
                var boxes = new List<YawBox>();
                boxes.AddRange(layout.Solids.Where(s => !s.Walkable).Select(s => s.Box));
                boxes.AddRange(layout.Props.Where(p => p.HasCollision).Select(p => p.CollisionBox));
                string label = layout.Diameter + " m " + layout.BiomeId + " seed " + layout.Seed.ToString("X");
                CheckEdges(nav, boxes, label, problems);
                CheckClearance(nav, boxes, label, problems);
            }

            Assert.True(problems.Count == 0, problems.Count + " problem(s):\n" + string.Join("\n", problems.Take(20)));
        }

        /// <summary>True when the box blocks a standing capsule whose feet are at height h (above 0.05 m, below the head).</summary>
        private static bool Obstructs(YawBox box, float h) => box.Top > h + 0.05f + 1e-4f && box.Bottom < h + ArenaRules.Headroom - 1e-4f;

        private static void CheckEdges(ArenaNav nav, List<YawBox> boxes, string label, List<string> problems)
        {
            for (int a = 0; a < nav.NodeCount; a++)
            {
                Vec3 pa = nav.NodePosition(a);
                for (int e = nav.EdgeBegin(a); e < nav.EdgeEnd(a); e++)
                {
                    if (nav.EdgeKind(e) != NavEdgeKind.Walk)
                    {
                        continue;
                    }

                    Vec3 pb = nav.NodePosition(nav.EdgeTarget(e));
                    foreach (YawBox box in boxes)
                    {
                        Aabb bounds = box.Bounds();
                        if (bounds.Max.X < Math.Min(pa.X, pb.X) || bounds.Min.X > Math.Max(pa.X, pb.X)
                            || bounds.Max.Z < Math.Min(pa.Z, pb.Z) || bounds.Min.Z > Math.Max(pa.Z, pb.Z))
                        {
                            continue;
                        }

                        int samples = (int)Math.Ceiling(Vec3.Distance(pa, pb) / 0.005f);
                        for (int k = 0; k <= samples; k++)
                        {
                            Vec3 p = Vec3.Lerp(pa, pb, k / (float)samples);
                            if (Obstructs(box, p.Y) && box.ContainsXZ(new Vec2(p.X, p.Z)))
                            {
                                problems.Add(label + ": walk edge " + pa + " -> " + pb + " passes through " + box);
                                break;
                            }
                        }
                    }
                }
            }
        }

        private static void CheckClearance(ArenaNav nav, List<YawBox> boxes, string label, List<string> problems)
        {
            for (int node = 0; node < nav.NodeCount; node++)
            {
                float h = nav.NodeHeight(node);
                foreach (YawBox box in boxes)
                {
                    if (!Obstructs(box, h))
                    {
                        continue;
                    }

                    float d = FootprintDistance(nav, node, box);
                    if (d < ExactRadius && nav.Clearance(node) > d + 1e-3f)
                    {
                        problems.Add(label + ": node at " + nav.NodePosition(node) + " has clearance " + nav.Clearance(node) + " but " + box + " is " + d + " m away");
                        break;
                    }
                }
            }
        }
    }
}
