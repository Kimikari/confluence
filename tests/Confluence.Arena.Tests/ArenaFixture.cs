using System;
using System.Collections.Generic;
using Confluence.Arena;
using Confluence.Arena.Generation;
using Confluence.Arena.Modules;
using Confluence.Arena.Navigation;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;

namespace Confluence.Arena.Tests
{
    /// <summary>Shared, thread-safe test inputs: the real catalogue and one generator (its fallback cache is shared).</summary>
    public static class ArenaFixture
    {
        private static readonly Lazy<Catalogue> LazyCatalogue = new Lazy<Catalogue>(() => Catalogue.Load(Catalogue.FindDefaultPath()));
        private static readonly Lazy<ArenaGenerator> LazyGenerator = new Lazy<ArenaGenerator>(() => new ArenaGenerator(LazyCatalogue.Value));

        public static Catalogue Catalogue => LazyCatalogue.Value;
        public static ArenaGenerator Generator => LazyGenerator.Value;
        public static ModuleLibrary Modules => Generator.Content.Modules;

        public static readonly int[] Diameters = { 24, 32, 40, 48 };

        /// <summary>A layout holding one module at the origin on a floor slab, for module-level navigation checks.</summary>
        public static ArenaLayout ModuleLayout(ModuleGeometry g)
        {
            var layout = new ArenaLayout { Diameter = 24, Center = Vec3.Zero };
            float half = Math.Max(g.Width, g.Depth) * 0.5f + 3f;
            layout.Boundary = BoundaryContour.Circle(Vec3.Zero, half + 2f);
            layout.Solids.Add(new Solid
            {
                Id = 0,
                Kind = SolidKind.Floor,
                Box = new YawBox(new Vec3(0f, -2f, 0f), new Vec3(half, 2f, half), 0f),
                Material = ArenaMaterials.Floor,
                Anchored = true,
                Walkable = true,
                MirrorId = 0,
            });
            for (int i = 0; i < g.Pieces.Count; i++)
            {
                ModulePiece p = g.Pieces[i];
                layout.Solids.Add(new Solid
                {
                    Id = i + 1,
                    Kind = p.Kind,
                    Box = p.Box,
                    RampRise = p.RampRise,
                    Material = p.Material,
                    Flags = p.Flags,
                    Durability = p.Durability,
                    Anchored = p.Anchored,
                    BlocksSight = p.BlocksSight,
                    Walkable = p.Walkable,
                });
            }

            return layout;
        }

        public static ArenaNav ModuleNav(ModuleGeometry g)
        {
            ArenaLayout layout = ModuleLayout(g);
            return ArenaNav.Build(layout, new ArenaNavOptions { HalfExtent = Math.Max(g.Width, g.Depth) * 0.5f + 3f, PropsAsObstacles = false });
        }

        /// <summary>Nodes inside a module's footprint (with a small margin), as a navigation mask.</summary>
        public static bool[] FootprintMask(ArenaNav nav, ModuleGeometry g, float margin = 0.3f)
        {
            var mask = new bool[nav.NodeCount];
            for (int i = 0; i < nav.NodeCount; i++)
            {
                Vec3 p = nav.NodePosition(i);
                mask[i] = Math.Abs(p.X) <= g.Width * 0.5f + margin && Math.Abs(p.Z) <= g.Depth * 0.5f + margin;
            }

            return mask;
        }

        /// <summary>The floor node just inside a socket opening (0.6 m in from the edge, at floor level).</summary>
        public static int SocketNode(ArenaNav nav, ModuleGeometry g, ModuleSocket s)
        {
            Vec2 edge = s.LocalPoint(g.Width, g.Depth);
            Vec2 p = edge + InwardNormal(s.Edge) * 0.6f;
            return nav.FindNode(new Vec3(p.X, 0f, p.Y), 0.5f, 1.25f, ArenaRules.CapsuleClearance);
        }

        public static Vec2 InwardNormal(ModuleEdge edge)
        {
            switch (edge)
            {
                case ModuleEdge.North: return new Vec2(0f, -1f);
                case ModuleEdge.South: return new Vec2(0f, 1f);
                case ModuleEdge.East: return new Vec2(-1f, 0f);
                default: return new Vec2(1f, 0f);
            }
        }

        public static List<ModulePiece> PiecesWithRole(ModuleGeometry g, string role)
        {
            var result = new List<ModulePiece>();
            foreach (ModulePiece p in g.Pieces)
            {
                if (p.Role == role)
                {
                    result.Add(p);
                }
            }

            return result;
        }
    }
}
