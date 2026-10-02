using System;
using System.Collections.Generic;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Arena.Modules
{
    /// <summary>
    /// Authoring helper for module blueprints. Coordinates are module-local: X across the first footprint dimension,
    /// Z across the second, origin at the footprint center on the connected floor (y = 0). Durability numbers are
    /// looked up from the module's own durability text (<see cref="ModuleDurability"/>); 0 means anchored.
    /// </summary>
    public sealed class ModuleBuilder
    {
        private readonly ModuleGeometry _geometry;

        internal ModuleBuilder(ModuleDef def)
        {
            Def = def;
            Width = (float)def.FootprintWidth;
            Depth = (float)def.FootprintDepth;
            Durability = new ModuleDurability(def.Durability);
            PrimaryMaterial = StructuralMaterial(def.Materials);
            _geometry = new ModuleGeometry(def.Id, def.Category, Width, Depth, (float)def.HeightM)
            {
                DeclaredDurability = Durability,
            };
        }

        public ModuleDef Def { get; }
        public float Width { get; }
        public float Depth { get; }
        public float HalfW => Width * 0.5f;
        public float HalfD => Depth * 0.5f;

        /// <summary>The structural (load-bearing) material: the first listed stone-like material, else the first listed.</summary>
        public string PrimaryMaterial { get; }

        private static string StructuralMaterial(IReadOnlyList<string> materials)
        {
            for (int i = 0; i < materials.Count; i++)
            {
                string m = materials[i];
                if (m == ArenaMaterials.Stone || m == ArenaMaterials.Slate || m == ArenaMaterials.Basalt || m == ArenaMaterials.Rock)
                {
                    return m;
                }
            }

            return materials.Count > 0 ? materials[0] : ArenaMaterials.Stone;
        }

        public ModuleDurability Durability { get; }

        internal ModuleGeometry Geometry => _geometry;

        public bool IsVantage
        {
            set => _geometry.IsVantage = value;
        }

        public bool IsPointSymmetric
        {
            set => _geometry.IsPointSymmetric = value;
        }

        // ---- Solids ------------------------------------------------------------------------------

        /// <summary>Axis-aligned block from (x0, z0) to (x1, z1) spanning heights y0..y1.</summary>
        public ModulePiece Block(float x0, float z0, float x1, float z1, float y0, float y1, SolidKind kind, string material, float durability, string role)
        {
            var box = new YawBox(
                new Vec3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f),
                new Vec3(Math.Abs(x1 - x0) * 0.5f, Math.Abs(y1 - y0) * 0.5f, Math.Abs(z1 - z0) * 0.5f),
                0f);
            return Add(box, 0f, kind, material, durability, role, false);
        }

        /// <summary>Rotated block centered at (cx, cz) with the given size along its local axes.</summary>
        public ModulePiece RotatedBlock(float cx, float cz, float sizeX, float sizeZ, float yawDeg, float y0, float y1, SolidKind kind, string material, float durability, string role)
        {
            var box = new YawBox(new Vec3(cx, (y0 + y1) * 0.5f, cz), new Vec3(sizeX * 0.5f, (y1 - y0) * 0.5f, sizeZ * 0.5f), yawDeg);
            return Add(box, 0f, kind, material, durability, role, false);
        }

        /// <summary>Anchored load-bearing wall of the structural material (spec L171).</summary>
        public ModulePiece Wall(float x0, float z0, float x1, float z1, float height, string role = "wall")
        {
            return Block(x0, z0, x1, z1, 0f, height, SolidKind.Wall, PrimaryMaterial, 0f, role);
        }

        /// <summary>Destructible cover with a durability from the module text.</summary>
        public ModulePiece Cover(float x0, float z0, float x1, float z1, float height, float durability, string material = null, string role = "cover")
        {
            return Block(x0, z0, x1, z1, 0f, height, SolidKind.Cover, material ?? PrimaryMaterial, durability, role);
        }

        /// <summary>Square pillar; durability 0 makes it anchored.</summary>
        public ModulePiece Pillar(float cx, float cz, float size, float height, float durability, string material = null, string role = "pillar")
        {
            float h = size * 0.5f;
            return Block(cx - h, cz - h, cx + h, cz + h, 0f, height, SolidKind.Pillar, material ?? PrimaryMaterial, durability, role);
        }

        /// <summary>Anchored walkable platform filled down to the floor, with its top at the given height.</summary>
        public ModulePiece Deck(float x0, float z0, float x1, float z1, float top, string material = null, float bottom = 0f)
        {
            ModulePiece p = Block(x0, z0, x1, z1, bottom, top, SolidKind.Platform, material ?? PrimaryMaterial, 0f, "deck");
            p.Walkable = true;
            return p;
        }

        /// <summary>Anchored walkable slab with open space beneath (a bridge deck).</summary>
        public ModulePiece Bridge(float x0, float z0, float x1, float z1, float top, float thickness, string material = null)
        {
            ModulePiece p = Block(x0, z0, x1, z1, top - thickness, top, SolidKind.Platform, material ?? PrimaryMaterial, 0f, "bridge");
            p.Walkable = true;
            return p;
        }

        /// <summary>
        /// Anchored walkable ramp over an axis-aligned footprint rising toward one edge from lowY to highY. The
        /// solid fills down to the floor (or to lowY for a ramp descending into a sunken module).
        /// </summary>
        public ModulePiece Ramp(float x0, float z0, float x1, float z1, ModuleEdge highEdge, float lowY, float highY, string material = null)
        {
            return Slope(x0, z0, x1, z1, highEdge, lowY, highY, SolidKind.Ramp, material, "ramp");
        }

        /// <summary>
        /// Standardized stair: a ramp-shaped collider whose length fits 0.25 m risers on 0.45 m treads (about 29
        /// degrees, spec L312 "stairs and collision edges are standardized"). Throws if the run is too short.
        /// </summary>
        public ModulePiece Stair(float x0, float z0, float x1, float z1, ModuleEdge highEdge, float lowY, float highY, string material = null)
        {
            float run = highEdge == ModuleEdge.North || highEdge == ModuleEdge.South ? Math.Abs(z1 - z0) : Math.Abs(x1 - x0);
            float needed = ArenaRules.StairRunFor(highY - lowY);
            if (run + 1e-3f < needed)
            {
                throw new InvalidOperationException(Def.Id + ": stair run " + run + " m is shorter than the standard " + needed + " m.");
            }

            return Slope(x0, z0, x1, z1, highEdge, lowY, highY, SolidKind.Stair, material, "stair");
        }

        private ModulePiece Slope(float x0, float z0, float x1, float z1, ModuleEdge highEdge, float lowY, float highY, SolidKind kind, string material, string role)
        {
            float minX = Math.Min(x0, x1);
            float maxX = Math.Max(x0, x1);
            float minZ = Math.Min(z0, z1);
            float maxZ = Math.Max(z0, z1);
            float bottom = Math.Min(0f, lowY);
            bool alongX = highEdge == ModuleEdge.East || highEdge == ModuleEdge.West;
            float across = alongX ? maxZ - minZ : maxX - minX;
            float along = alongX ? maxX - minX : maxZ - minZ;
            float yaw;
            switch (highEdge)
            {
                case ModuleEdge.North: yaw = 0f; break;
                case ModuleEdge.East: yaw = 90f; break;
                case ModuleEdge.South: yaw = 180f; break;
                default: yaw = 270f; break;
            }

            var box = new YawBox(
                new Vec3((minX + maxX) * 0.5f, (bottom + highY) * 0.5f, (minZ + maxZ) * 0.5f),
                new Vec3(across * 0.5f, (highY - bottom) * 0.5f, along * 0.5f),
                yaw);
            ModulePiece p = Add(box, highY - lowY, kind, material ?? PrimaryMaterial, 0f, role, true);
            return p;
        }

        private ModulePiece Add(YawBox box, float rise, SolidKind kind, string material, float durability, string role, bool walkable)
        {
            bool anchored = durability <= 0f;
            var piece = new ModulePiece
            {
                Kind = kind,
                Box = box,
                RampRise = rise,
                Material = material,
                Flags = ArenaMaterials.FlagsFor(material) | (anchored ? MaterialFlags.Anchored : MaterialFlags.None),
                Durability = anchored ? 0f : durability,
                Anchored = anchored,
                Walkable = walkable,
                Role = role,
            };
            _geometry.Pieces.Add(piece);
            return piece;
        }

        // ---- Floor and patches ---------------------------------------------------------------------

        /// <summary>Removes the main floor over a rectangle and lays an anchored lower floor at floorY (sunken modules).</summary>
        public ModulePiece SunkenFloor(float x0, float z0, float x1, float z1, float floorY)
        {
            _geometry.FloorCutouts.Add(new LocalRect(x0, z0, x1, z1));
            ModulePiece p = Block(x0, z0, x1, z1, -ArenaRules.FloorSlabThickness, floorY, SolidKind.Floor, PrimaryMaterial, 0f, "sunken-floor");
            p.Walkable = true;
            return p;
        }

        public ModulePatch Patch(float x0, float z0, float x1, float z1, MaterialFlags flags, bool naturalSource, string rule = null)
        {
            var patch = new ModulePatch
            {
                Center = new Vec2((x0 + x1) * 0.5f, (z0 + z1) * 0.5f),
                HalfSize = new Vec2(Math.Abs(x1 - x0) * 0.5f, Math.Abs(z1 - z0) * 0.5f),
                Flags = flags,
                NaturalSource = naturalSource,
                Rule = rule,
            };
            _geometry.Patches.Add(patch);
            return patch;
        }

        // ---- Sockets -------------------------------------------------------------------------------

        public ModuleBuilder Socket(ModuleEdge edge, float offset, float width)
        {
            _geometry.Sockets.Add(new ModuleSocket(edge, offset, width, 0f));
            return this;
        }

        /// <summary>One socket centered on each listed edge, spanning the edge minus a margin at both corners.</summary>
        public ModuleBuilder OpenEdges(params ModuleEdge[] edges)
        {
            foreach (ModuleEdge e in edges)
            {
                float length = e == ModuleEdge.North || e == ModuleEdge.South ? Width : Depth;
                if (length + 1e-3f < GlobalRules.Arena.MinRouteWidth)
                {
                    continue; // a short edge of a small cover module is clear space, not an entrance
                }

                Socket(e, 0f, Math.Min(length, Math.Max(GlobalRules.Arena.MinRouteWidth, length - 0.5f)));
            }

            return this;
        }

        public ModuleBuilder OpenAllEdges() => OpenEdges(ModuleEdge.North, ModuleEdge.East, ModuleEdge.South, ModuleEdge.West);
    }
}
