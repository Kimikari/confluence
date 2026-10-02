using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Arena.Modules;
using Confluence.Arena.Navigation;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Arena.Tests.Modules
{
    /// <summary>Checks shared by every module test: catalogue facts, authoring rules, and walkable sockets.</summary>
    public static class ModuleAssert
    {
        /// <summary>
        /// Footprint, height, and category match the record; the library's authoring checks pass (pieces inside the
        /// footprint, sockets 2.5 m or wider, ramps at most 30 degrees, durability from the text, center symmetry);
        /// every socket opens onto walkable floor with route clearance; all sockets connect inside the footprint.
        /// </summary>
        public static ModuleGeometry Authored(string id)
        {
            ModuleDef def = ArenaFixture.Catalogue.Module(id);
            ModuleGeometry g = ArenaFixture.Modules.Get(id);
            Assert.Equal(def.Category, g.Category);
            Assert.Equal((float)def.FootprintWidth, g.Width, 3);
            Assert.Equal((float)def.FootprintDepth, g.Depth, 3);
            Assert.Equal((float)def.HeightM, g.Height, 3);
            Assert.Empty(ModuleLibrary.Check(g));

            ArenaNav nav = ArenaFixture.ModuleNav(g);
            NavQuery inside = NavQuery.Capsule.WithAllowed(ArenaFixture.FootprintMask(nav, g));
            var nodes = new List<int>();
            foreach (ModuleSocket s in g.Sockets)
            {
                Assert.True(ClearOpening(nav, g, s) >= GlobalRules.Arena.MinRouteWidth - 0.26f,
                    id + " socket on " + s.Edge + " at " + s.Offset + " opens less than 2.5 m of floor");
                int node = ArenaFixture.SocketNode(nav, g, s);
                Assert.True(node >= 0, id + " socket on " + s.Edge + " at " + s.Offset + " has no floor");
                nodes.Add(node);
            }

            // Islands of cover stand in open floor (their edges are clear space); every other module connects each
            // socket to another one through its own footprint.
            if (g.Category != ModuleCategory.Cover && nodes.Count > 1)
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    bool[] reach = nav.Reachable(new[] { nodes[i] }, inside);
                    bool connected = false;
                    for (int j = 0; j < nodes.Count && !connected; j++)
                    {
                        connected = j != i && reach[nodes[j]];
                    }

                    Assert.True(connected, id + ": socket " + i + " reaches no other socket inside the footprint");
                }
            }

            return g;
        }

        /// <summary>
        /// Longest run of standable floor along a socket, sampled 0.3 m inside its edge every 0.25 m: the clear width
        /// of the opening (the 2.5 m route rule of the module sockets text, spec L312).
        /// </summary>
        public static float ClearOpening(ArenaNav nav, ModuleGeometry g, ModuleSocket s)
        {
            Vec2 center = s.LocalPoint(g.Width, g.Depth) + ArenaFixture.InwardNormal(s.Edge) * 0.3f;
            Vec2 along = s.Edge == ModuleEdge.North || s.Edge == ModuleEdge.South ? new Vec2(1f, 0f) : new Vec2(0f, 1f);
            float best = 0f;
            float run = 0f;
            for (float t = -s.Width * 0.5f; t <= s.Width * 0.5f + 1e-3f; t += 0.25f)
            {
                Vec2 p = center + along * t;
                int column = nav.ColumnAt(p);
                bool open = column >= 0 && nav.NodeInColumnNear(column, s.FloorY, GlobalRules.Movement.StepHeight) >= 0; // a ramp foot or first stair counts
                run = open ? run + 0.25f : 0f;
                best = Math.Max(best, run);
            }

            return best;
        }

        public static List<ModulePiece> Role(ModuleGeometry g, string role) => g.Pieces.Where(p => p.Role == role).ToList();

        public static List<ModulePatch> Rule(ModuleGeometry g, string rule) => g.Patches.Where(p => p.Rule == rule).ToList();

        public static void Anchored(ModulePiece p)
        {
            Assert.True(p.Anchored);
            Assert.Equal(0f, p.Durability);
            Assert.True((p.Flags & MaterialFlags.Anchored) != 0);
        }

        public static void Destructible(ModulePiece p, float durability)
        {
            Assert.False(p.Anchored);
            Assert.Equal(durability, p.Durability);
        }

        /// <summary>Sockets on an edge, sorted by offset.</summary>
        public static List<ModuleSocket> On(ModuleGeometry g, ModuleEdge edge) => g.Sockets.Where(s => s.Edge == edge).OrderBy(s => s.Offset).ToList();

        /// <summary>The walkable top at a module-local point is reachable from a socket by common movement.</summary>
        public static void ReachableTop(ModuleGeometry g, Vec2 local, float height)
        {
            ArenaNav nav = ArenaFixture.ModuleNav(g);
            int top = nav.FindNode(new Vec3(local.X, height, local.Y), 0.3f, 1.0f, ArenaRules.CapsuleClearance);
            Assert.True(top >= 0, g.ModuleId + " has no standing node at height " + height);
            Assert.Equal(height, nav.NodeHeight(top), 1);
            int socket = ArenaFixture.SocketNode(nav, g, g.Sockets[0]);
            bool[] reach = nav.Reachable(new[] { socket }, NavQuery.Capsule);
            bool[] back = nav.CanReach(new[] { socket }, NavQuery.Capsule);
            Assert.True(reach[top], g.ModuleId + " top at " + height + " m is not reachable");
            Assert.True(back[top], g.ModuleId + " top at " + height + " m has no way back down");
        }
    }
}
