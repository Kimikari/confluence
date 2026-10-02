using System;
using System.Collections.Generic;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Arena.Modules
{
    /// <summary>Footprint edge in module-local space: North is local +Z, East +X, South -Z, West -X.</summary>
    public enum ModuleEdge
    {
        North = 0,
        East = 1,
        South = 2,
        West = 3,
    }

    /// <summary>
    /// An entrance on a footprint edge (spec module sockets: "2.5m or wider paired entrances; attach only matching
    /// level or a validated ramp"). Offset is measured along the edge from its midpoint: local X for North and South
    /// edges, local Z for East and West edges.
    /// </summary>
    public sealed class ModuleSocket
    {
        public ModuleSocket(ModuleEdge edge, float offset, float width, float floorY)
        {
            Edge = edge;
            Offset = offset;
            Width = width;
            FloorY = floorY;
        }

        public ModuleEdge Edge { get; }
        public float Offset { get; }
        public float Width { get; }

        /// <summary>Surface height at the socket relative to the connected floor (0 for every authored module).</summary>
        public float FloorY { get; }

        /// <summary>Local ground point at the middle of the socket opening.</summary>
        public Vec2 LocalPoint(float width, float depth)
        {
            switch (Edge)
            {
                case ModuleEdge.North: return new Vec2(Offset, depth * 0.5f);
                case ModuleEdge.South: return new Vec2(Offset, -depth * 0.5f);
                case ModuleEdge.East: return new Vec2(width * 0.5f, Offset);
                default: return new Vec2(-width * 0.5f, Offset);
            }
        }

        /// <summary>The edge after rotating the module by a multiple of 90 degrees (positive yaw turns clockwise).</summary>
        public static ModuleEdge Rotate(ModuleEdge edge, int quarterTurns)
        {
            int q = ((quarterTurns % 4) + 4) % 4;
            return (ModuleEdge)(((int)edge + q) % 4);
        }
    }

    /// <summary>One authored solid of a module, in module-local space (origin at the footprint center on the floor).</summary>
    public sealed class ModulePiece
    {
        public SolidKind Kind { get; set; }
        public YawBox Box { get; set; }
        public float RampRise { get; set; }
        public string Material { get; set; }
        public MaterialFlags Flags { get; set; }

        /// <summary>Durability from the module's durability text; 0 when anchored (spec L171).</summary>
        public float Durability { get; set; }

        public bool Anchored { get; set; }
        public bool RetiresAtFinal { get; set; }
        public bool BlocksSight { get; set; } = true;
        public bool Walkable { get; set; }

        /// <summary>Authoring role such as "pillar", "low-cover", "deck", "ramp", "stair", "prism".</summary>
        public string Role { get; set; }

        /// <summary>Movable pieces slide along a marked straight track between these two local center positions.</summary>
        public Vec2 TrackFrom { get; set; }

        public Vec2 TrackTo { get; set; }

        public bool IsMovable { get; set; }

        public ModulePiece Clone() => (ModulePiece)MemberwiseClone();
    }

    /// <summary>An authored material patch in module-local space.</summary>
    public sealed class ModulePatch
    {
        public Vec2 Center { get; set; }
        public Vec2 HalfSize { get; set; }
        public float Radius { get; set; }
        public float SurfaceY { get; set; }
        public int Water { get; set; }
        public int Heat { get; set; }
        public int Frost { get; set; }
        public int Dust { get; set; }
        public int Sand { get; set; }
        public int Growth { get; set; }
        public MaterialFlags Flags { get; set; }

        /// <summary>Map-owned natural source (spec L668): previewed bed or conductor section with finite units.</summary>
        public bool NaturalSource { get; set; }

        /// <summary>Identifier of the rule the patch participates in (for example "MOD-046:trough"), or null.</summary>
        public string Rule { get; set; }

        /// <summary>Durability the module text assigns to this material (ice tiles 45, growth 50), or 0.</summary>
        public float Durability { get; set; }

        public ModulePatch Clone() => (ModulePatch)MemberwiseClone();
    }

    /// <summary>A local axis-aligned rectangle.</summary>
    public struct LocalRect
    {
        public LocalRect(float x0, float z0, float x1, float z1)
        {
            MinX = Math.Min(x0, x1);
            MinZ = Math.Min(z0, z1);
            MaxX = Math.Max(x0, x1);
            MaxZ = Math.Max(z0, z1);
        }

        public float MinX;
        public float MinZ;
        public float MaxX;
        public float MaxZ;

        public float Width => MaxX - MinX;
        public float Depth => MaxZ - MinZ;
        public Vec2 Center => new Vec2((MinX + MaxX) * 0.5f, (MinZ + MaxZ) * 0.5f);
    }

    /// <summary>
    /// Authored gameplay geometry of one catalogue module (spec L656 "Modules supply gameplay geometry and sockets"):
    /// solids, ramps and stairs, material patches, sockets, and main-floor cutouts for sunken modules, all in
    /// module-local space. The footprint includes its connecting clear space (spec L686).
    /// </summary>
    public sealed class ModuleGeometry
    {
        internal ModuleGeometry(string moduleId, ModuleCategory category, float width, float depth, float height)
        {
            ModuleId = moduleId;
            Category = category;
            Width = width;
            Depth = depth;
            Height = height;
        }

        public string ModuleId { get; }
        public ModuleCategory Category { get; }

        /// <summary>Footprint along local X (first catalogue dimension).</summary>
        public float Width { get; }

        /// <summary>Footprint along local Z (second catalogue dimension).</summary>
        public float Depth { get; }

        /// <summary>Catalogue height relative to the connected floor (negative for sunken modules).</summary>
        public float Height { get; }

        public List<ModulePiece> Pieces { get; } = new List<ModulePiece>();
        public List<ModulePatch> Patches { get; } = new List<ModulePatch>();
        public List<ModuleSocket> Sockets { get; } = new List<ModuleSocket>();

        /// <summary>Regions where the main floor slab is removed (sunken modules supply their own lower floor).</summary>
        public List<LocalRect> FloorCutouts { get; } = new List<LocalRect>();

        /// <summary>Optional 4 m vantage (spec L312): two access routes and a safe descent are authored.</summary>
        public bool IsVantage { get; internal set; }

        /// <summary>True when the geometry maps onto itself under a 180-degree turn (required for center modules).</summary>
        public bool IsPointSymmetric { get; internal set; }

        /// <summary>Durability values parsed from the catalogue text, by label.</summary>
        public ModuleDurability DeclaredDurability { get; internal set; }

        public bool HasSocketOn(ModuleEdge edge)
        {
            for (int i = 0; i < Sockets.Count; i++)
            {
                if (Sockets[i].Edge == edge)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Rotated footprint extents for a yaw that is a multiple of 90 degrees.</summary>
        public void RotatedSize(int quarterTurns, out float sizeX, out float sizeZ)
        {
            bool swap = (((quarterTurns % 4) + 4) % 4) % 2 == 1;
            sizeX = swap ? Depth : Width;
            sizeZ = swap ? Width : Depth;
        }

        /// <summary>Highest walkable surface (the module's elevation).</summary>
        public float MaxWalkableHeight()
        {
            float max = 0f;
            for (int i = 0; i < Pieces.Count; i++)
            {
                if (Pieces[i].Walkable)
                {
                    max = Math.Max(max, Pieces[i].Box.Top);
                }
            }

            return max;
        }

        /// <summary>Lowest walkable surface (negative for sunken modules).</summary>
        public float MinWalkableHeight()
        {
            float min = 0f;
            for (int i = 0; i < Pieces.Count; i++)
            {
                ModulePiece p = Pieces[i];
                if (!p.Walkable)
                {
                    continue;
                }

                float low = p.RampRise > 0f ? p.Box.Top - p.RampRise : p.Box.Top;
                min = Math.Min(min, low);
            }

            return min;
        }

        /// <summary>Steepest ramp or stair surface in degrees.</summary>
        public float MaxSlopeDeg()
        {
            float max = 0f;
            for (int i = 0; i < Pieces.Count; i++)
            {
                ModulePiece p = Pieces[i];
                if (p.RampRise > 0f)
                {
                    max = Math.Max(max, new RampShape(p.Box, p.RampRise).SlopeDeg);
                }
            }

            return max;
        }
    }
}
