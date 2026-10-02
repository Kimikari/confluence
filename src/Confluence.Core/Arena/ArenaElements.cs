using System;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Core.Arena
{
    /// <summary>Gameplay role of a collision solid in an assembled arena.</summary>
    public enum SolidKind
    {
        Floor,
        Wall,
        Cover,
        Pillar,
        Platform,
        Ramp,
        Stair,
        AlcoveWall,
        Barrier,
    }

    /// <summary>A catalogue module (MOD-xxx) placed in the layout (spec L664: chosen modules and transforms).</summary>
    public sealed class ModuleInstance
    {
        public string ModuleId { get; set; }
        public Vec3 Position { get; set; }
        public float YawDeg { get; set; }

        /// <summary>-1 for the shared center, 0 or 1 for the mirrored spawn sides.</summary>
        public int Side { get; set; } = -1;

        /// <summary>Index pairing this instance with its mirrored counterpart (-1 if unpaired).</summary>
        public int MirrorIndex { get; set; } = -1;

        public ModuleInstance Clone() => (ModuleInstance)MemberwiseClone();
    }

    /// <summary>
    /// Collision and sight solid. Durability 0 means anchored, load-bearing terrain that cannot be removed
    /// (spec L171); positive durability marks frangible pieces with fracture stages.
    /// </summary>
    public sealed class Solid
    {
        public int Id { get; set; }
        public SolidKind Kind { get; set; }
        public YawBox Box { get; set; }

        /// <summary>Rise of the walkable surface for ramps (see <see cref="RampShape"/>); 0 otherwise.</summary>
        public float RampRise { get; set; }

        public string Material { get; set; }
        public MaterialFlags Flags { get; set; }
        public float Durability { get; set; }
        public bool Anchored { get; set; }

        /// <summary>Map-owned soft cover in the final center that retires at 85 s (spec L678).</summary>
        public bool RetiresAtFinal { get; set; }

        /// <summary>Index into ArenaLayout.Modules of the module that produced this solid (-1 if none).</summary>
        public int ModuleIndex { get; set; } = -1;

        /// <summary>Id of the mirrored counterpart solid (-1 if none).</summary>
        public int MirrorId { get; set; } = -1;

        public bool BlocksSight { get; set; } = true;
        public bool Walkable { get; set; }

        public bool IsRamp => Kind == SolidKind.Ramp && RampRise > 0f;

        public bool IsDestructible => !Anchored && Durability > 0f;

        /// <summary>The ramp wedge for ramp solids (a plain box shape when RampRise is 0).</summary>
        public RampShape Ramp => new RampShape(Box, RampRise);

        public Solid Clone() => (Solid)MemberwiseClone();
    }

    /// <summary>
    /// Initial material cells on the ground: water beds, heat, frost, dust, sand, growth, conductors (spec L136, L664,
    /// L668). A rectangle (Center, HalfSize, YawDeg) unless Radius &gt; 0, in which case a circle. Unit counts are
    /// per covered cell, 0 to GlobalRules.World.MaxUnitsPerCell. Biome text such as "Sand 2 Water 1" (BIO-007) or
    /// "Heat 1 and Growth 1" (BIO-002) maps one-to-one onto these counts (DECISIONS D-025).
    /// </summary>
    public sealed class MaterialPatch
    {
        public Vec2 Center { get; set; }
        public Vec2 HalfSize { get; set; }
        public float YawDeg { get; set; }
        public float Radius { get; set; }

        /// <summary>Height of the surface the patch lies on (0 for the main floor).</summary>
        public float SurfaceY { get; set; }

        public int Water { get; set; }
        public int Heat { get; set; }
        public int Frost { get; set; }
        public int Dust { get; set; }

        /// <summary>Sand units; equal water and sand units make damp sand (spec L154).</summary>
        public int Sand { get; set; }

        /// <summary>Combustible growth units; at least two heat units ignite growth (spec L153).</summary>
        public int Growth { get; set; }

        public MaterialFlags Flags { get; set; }

        /// <summary>Map-owned natural source; does not count against either player's field allocation (spec L668).</summary>
        public bool NaturalSource { get; set; }

        /// <summary>Biome refill rule text or identifier (for example the Raincourt rainfall pulse), or null.</summary>
        public string RefillRule { get; set; }

        public int MirrorId { get; set; } = -1;

        public bool IsCircle => Radius > 0f;

        public float Area => IsCircle ? MathF.PI * Radius * Radius : 4f * HalfSize.X * HalfSize.Y;

        /// <summary>True when every unit count lies within 0..GlobalRules.World.MaxUnitsPerCell (spec L136, L163).</summary>
        public bool HasValidUnits
        {
            get
            {
                return InRange(Water) && InRange(Heat) && InRange(Frost) && InRange(Dust) && InRange(Sand) && InRange(Growth);
            }
        }

        private static bool InRange(int units) => units >= 0 && units <= GlobalRules.World.MaxUnitsPerCell;

        public bool ContainsXZ(Vec2 point)
        {
            if (IsCircle)
            {
                return Intersections.PointInCircle(point, Center, Radius);
            }

            Vec2 local = (point - Center).RotatedByYaw(-YawDeg);
            return MathF.Abs(local.X) <= HalfSize.X && MathF.Abs(local.Y) <= HalfSize.Y;
        }

        public MaterialPatch Clone() => (MaterialPatch)MemberwiseClone();
    }

    /// <summary>A placed interactive prop instance (PROP-xxx).</summary>
    public sealed class PropPlacement
    {
        public int Id { get; set; }
        public string PropId { get; set; }
        public Vec3 Position { get; set; }
        public float YawDeg { get; set; }
        public int MirrorId { get; set; } = -1;

        /// <summary>
        /// Physical collision volume of the prop body in world space, written by the arena generator from the
        /// prop's shape so the simulation and validators share one footprint. Ignore when HasCollision is false.
        /// </summary>
        public YawBox CollisionBox { get; set; }

        public bool HasCollision { get; set; }

        public PropPlacement Clone() => (PropPlacement)MemberwiseClone();
    }

    /// <summary>Protected spawn alcove for one side (spec L46-L47).</summary>
    public sealed class SpawnPoint
    {
        public int Side { get; set; }
        public Vec3 Position { get; set; }
        public float YawDeg { get; set; }

        /// <summary>Volume the ward confines the player to during the 5 s alcove phase.</summary>
        public YawBox Alcove { get; set; }

        public SpawnPoint Clone() => (SpawnPoint)MemberwiseClone();
    }

    /// <summary>Standardized overhead light shaft (spec L320, L664).</summary>
    public sealed class LightShaft
    {
        public Vec3 Position { get; set; }
        public float Radius { get; set; }
        public int MirrorId { get; set; } = -1;

        public LightShaft Clone() => (LightShaft)MemberwiseClone();
    }

    /// <summary>Scheduled, announced wind gust from a biome rule (spec L664).</summary>
    public sealed class GustEvent
    {
        /// <summary>Combat time at which the gust starts.</summary>
        public float TimeSec { get; set; }

        public float DurationSec { get; set; }

        /// <summary>Unit ground-plane direction the gust pushes toward (X, Y = world Z).</summary>
        public Vec2 Direction { get; set; }

        /// <summary>Wind force strength (see GlobalRules.World wind thresholds).</summary>
        public int Strength { get; set; }

        public Vec2 Center { get; set; }

        /// <summary>Affected radius; 0 or less means the whole arena.</summary>
        public float Radius { get; set; }

        public GustEvent Clone() => (GustEvent)MemberwiseClone();
    }
}
