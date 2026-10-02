using System;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Sim.World
{
    /// <summary>Which query a solid participates in.</summary>
    [Flags]
    public enum SolidMask
    {
        None = 0,
        /// <summary>Blocks characters, strikes, and projectiles.</summary>
        Movement = 1 << 0,
        /// <summary>Blocks sightlines and target acquisition.</summary>
        Sight = 1 << 1,
        All = Movement | Sight,
    }

    /// <summary>
    /// One collision and sight solid of a duel: a layout solid (anchored terrain or a frangible piece) or a
    /// dynamic temporary solid such as a wall or ice block (spec L134, L171). Durability 0 with Anchored means
    /// permanent terrain that cannot be removed; positive durability marks a destructible piece.
    /// </summary>
    public sealed class CollisionSolid
    {
        internal CollisionSolid(int id, SolidKind kind, YawBox box, float rampRise, MaterialFlags flags, bool blocksSight,
            bool walkable, bool anchored, float durability, bool dynamic, int owner, string sourceId, int createdTick,
            int expireTick, bool retiresAtFinal)
        {
            Id = id;
            Kind = kind;
            Box = box;
            RampRise = kind == SolidKind.Ramp ? Math.Max(0f, rampRise) : 0f;
            Flags = flags;
            BlocksSight = blocksSight;
            Walkable = walkable;
            Anchored = anchored;
            MaxDurability = Math.Max(0f, durability);
            Durability = MaxDurability;
            Dynamic = dynamic;
            OwnerPlayer = owner;
            SourceId = sourceId;
            CreatedTick = createdTick;
            ExpireTick = expireTick;
            RetiresAtFinal = retiresAtFinal;
            Active = true;
            Bounds = box.Bounds();
        }

        public int Id { get; }
        public SolidKind Kind { get; }
        public YawBox Box { get; }
        public float RampRise { get; }
        public bool IsRamp => RampRise > 0f;
        public RampShape Ramp => new RampShape(Box, RampRise);
        public MaterialFlags Flags { get; }
        public bool BlocksSight { get; }
        public bool Walkable { get; }
        public bool Anchored { get; }
        public float MaxDurability { get; }
        public float Durability { get; internal set; }

        /// <summary>Frangible panels, prop covers, temporary walls, ice (spec L171).</summary>
        public bool Destructible => !Anchored && MaxDurability > 0f;

        public bool Active { get; internal set; }

        /// <summary>Created during the duel (walls, ice, growth); never part of the layout.</summary>
        public bool Dynamic { get; }

        /// <summary>Player index that created a dynamic solid, or -1 for map-owned solids.</summary>
        public int OwnerPlayer { get; }

        /// <summary>Catalogue ID of the creating effect, or null.</summary>
        public string SourceId { get; }

        public int CreatedTick { get; }

        /// <summary>Tick at which a temporary solid expires, or int.MaxValue.</summary>
        public int ExpireTick { get; }

        /// <summary>Map-owned soft cover in the final center that retires at 85 s (spec L678).</summary>
        public bool RetiresAtFinal { get; }

        public Aabb Bounds { get; }

        public bool Slippery => (Flags & MaterialFlags.Ice) != 0;

        public bool Matches(SolidMask mask)
        {
            if (!Active)
            {
                return false;
            }

            if ((mask & SolidMask.Movement) != 0)
            {
                return true;
            }

            return (mask & SolidMask.Sight) != 0 && BlocksSight;
        }

        /// <summary>Top surface height at a world (x, z) inside the footprint, or NaN outside.</summary>
        public float SurfaceHeightAt(float x, float z)
        {
            if (IsRamp)
            {
                return Ramp.SurfaceHeightAt(x, z);
            }

            return Box.ContainsXZ(new Vec2(x, z)) ? Box.Top : float.NaN;
        }

        public bool Raycast(Ray ray, float maxDistance, out RayHit hit)
        {
            return IsRamp ? Ramp.Raycast(ray, maxDistance, out hit) : Box.Raycast(ray, maxDistance, out hit);
        }

        public bool SphereSweep(Vec3 start, Vec3 end, float radius, out float t01, out RayHit hit)
        {
            return IsRamp
                ? Intersections.SphereSweepVsRamp(start, end, radius, Ramp, out t01, out hit)
                : Intersections.SphereSweepVsYawBox(start, end, radius, Box, out t01, out hit);
        }

        public bool CapsulePenetration(Capsule capsule, out Penetration penetration)
        {
            return IsRamp
                ? Intersections.CapsuleVsRamp(capsule, Ramp, out penetration)
                : Intersections.CapsuleVsYawBox(capsule, Box, out penetration);
        }

        public bool ContainsPoint(Vec3 point) => IsRamp ? Ramp.Contains(point) : Box.Contains(point);

        public override string ToString() => "Solid#" + Id + " " + Kind + (Active ? "" : " (inactive)");
    }

    /// <summary>Description of a dynamic temporary solid (wall, ice block, growth) created during the duel.</summary>
    public struct TemporarySolidSpec
    {
        public YawBox Box;
        public SolidKind Kind;
        public MaterialFlags Flags;
        public bool BlocksSight;
        public bool Walkable;

        /// <summary>Durability before destruction; 0 means it cannot be damaged (still expires).</summary>
        public float Durability;

        /// <summary>Lifetime in ticks; 0 or less means until destroyed.</summary>
        public int LifetimeTicks;

        public int OwnerPlayer;
        public string SourceId;
    }

    /// <summary>Nearest solid hit by a world query.</summary>
    public struct WorldHit
    {
        public float Distance;

        /// <summary>Contact point (for sweeps, the swept sphere's center at contact).</summary>
        public Vec3 Point;

        /// <summary>The touched point on the solid's surface (equal to <see cref="Point"/> for rays).</summary>
        public Vec3 Contact;

        public Vec3 Normal;
        public int SolidId;
    }
}
