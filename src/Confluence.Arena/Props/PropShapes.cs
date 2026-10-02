using System;
using System.Collections.Generic;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;

namespace Confluence.Arena.Props
{
    /// <summary>
    /// Physical footprint of one prop definition. The catalogue gives mass, durability, and effect prose but no
    /// dimensions, so the shapes below are read from each effect text (for example PROP-014 "Opaque 1.8 m cover",
    /// PROP-026 "marked 4 m track", PROP-003 "marked 3 m length"). The generator writes each placement's collision
    /// box from its shape (<see cref="PropShapes.WriteCollision"/>); placement, navigation, validation, and the Sim
    /// read that box.
    /// </summary>
    public sealed class PropShape
    {
        public PropShape(string propId, float sizeX, float sizeZ, float height, bool blocksMovement, float trackLength = 0f)
        {
            PropId = propId;
            SizeX = sizeX;
            SizeZ = sizeZ;
            Height = height;
            BlocksMovement = blocksMovement;
            TrackLength = trackLength;
        }

        public string PropId { get; }

        /// <summary>Footprint width along the prop's local X (its right).</summary>
        public float SizeX { get; }

        /// <summary>Footprint depth along the prop's local Z (its facing).</summary>
        public float SizeZ { get; }

        public float Height { get; }

        /// <summary>False for floor-level props (grates, tiles, beds, plates) that players walk over.</summary>
        public bool BlocksMovement { get; }

        /// <summary>Length of a marked straight track or arc the prop needs clear along its local Z (0 if none).</summary>
        public float TrackLength { get; }

        /// <summary>Radius of the circle enclosing the footprint and its track.</summary>
        public float PlacementRadius
        {
            get
            {
                float x = SizeX * 0.5f;
                float z = Math.Max(SizeZ, TrackLength) * 0.5f;
                return MathF.Sqrt(x * x + z * z);
            }
        }

        /// <summary>World collision box of a placed instance.</summary>
        public YawBox BoxAt(Vec3 position, float yawDeg)
        {
            return YawBox.OnFloor(position, new Vec3(SizeX, Math.Max(0.05f, Height), SizeZ), yawDeg);
        }

        /// <summary>World box of the footprint plus its marked track (what placement must keep clear).</summary>
        public YawBox ClearanceBoxAt(Vec3 position, float yawDeg)
        {
            return YawBox.OnFloor(position, new Vec3(SizeX, Math.Max(0.05f, Height), Math.Max(SizeZ, TrackLength)), yawDeg);
        }
    }

    /// <summary>Shapes for PROP-001..PROP-048.</summary>
    public static class PropShapes
    {
        private static readonly Dictionary<string, PropShape> Shapes = Build();

        public static bool TryGet(string propId, out PropShape shape) => Shapes.TryGetValue(propId ?? string.Empty, out shape);

        public static PropShape For(string propId)
        {
            if (!TryGet(propId, out PropShape shape))
            {
                throw new ArgumentException("No prop shape for " + propId, nameof(propId));
            }

            return shape;
        }

        public static int Count => Shapes.Count;

        /// <summary>
        /// Collision box a placement must carry, computed from its shape (false for floor-level props that players
        /// walk over, and for unknown definitions). Generated layouts store it in <see cref="PropPlacement.CollisionBox"/>.
        /// </summary>
        public static bool TryGetCollisionBox(PropPlacement placement, out YawBox box)
        {
            box = default;
            if (placement == null || !TryGet(placement.PropId, out PropShape shape) || !shape.BlocksMovement)
            {
                return false;
            }

            box = shape.BoxAt(placement.Position, placement.YawDeg);
            return true;
        }

        /// <summary>
        /// Writes a placement's collision volume from its shape (<see cref="PropPlacement.CollisionBox"/> and
        /// <see cref="PropPlacement.HasCollision"/>), so the Sim, navigation, and validators all read one footprint.
        /// Call again after moving or turning the placement.
        /// </summary>
        public static void WriteCollision(PropPlacement placement)
        {
            if (placement == null) throw new ArgumentNullException(nameof(placement));
            placement.HasCollision = TryGetCollisionBox(placement, out YawBox box);
            placement.CollisionBox = box;
        }

        /// <summary>True when the placement's stored collision volume is exactly what its shape gives.</summary>
        public static bool CollisionMatchesShape(PropPlacement placement)
        {
            bool expected = TryGetCollisionBox(placement, out YawBox box);
            if (placement.HasCollision != expected)
            {
                return false;
            }

            if (!expected)
            {
                return true;
            }

            YawBox stored = placement.CollisionBox;
            return Vec3.Distance(stored.Center, box.Center) <= 1e-4f && Vec3.Distance(stored.HalfExtents, box.HalfExtents) <= 1e-4f
                && MathF.Abs(CMath.DeltaAngle(stored.YawDeg, box.YawDeg)) <= 1e-3f;
        }

        private static Dictionary<string, PropShape> Build()
        {
            var d = new Dictionary<string, PropShape>(StringComparer.Ordinal);
            void Add(string id, float x, float z, float h, bool blocks = true, float track = 0f) => d[id] = new PropShape(id, x, z, h, blocks, track);

            Add("PROP-001", 0.9f, 0.9f, 1.0f);            // Copper Barrel
            Add("PROP-002", 0.8f, 0.8f, 2.0f);            // Grounding Statue
            Add("PROP-003", 0.25f, 3.0f, 1.0f);           // Segmented Rail: "marked 3 m length"
            Add("PROP-004", 1.0f, 1.0f, 1.2f);            // Iron Bell
            Add("PROP-005", 0.8f, 0.8f, 1.5f);            // Coil Stand: charges a 1 m zone
            Add("PROP-006", 1.0f, 2.0f, 1.0f);            // Insulated Crate: top strip "2 m long"
            Add("PROP-007", 0.8f, 0.8f, 1.0f);            // Water Urn
            Add("PROP-008", 1.0f, 1.0f, 1.0f);            // Porous Block
            Add("PROP-009", 1.2f, 1.2f, 0.9f);            // Sealed Basin
            Add("PROP-010", 0.9f, 0.9f, 1.0f);            // Ice Cask
            Add("PROP-011", 1.0f, 1.0f, 1.2f);            // Rain Catcher
            Add("PROP-012", 1.0f, 1.0f, 0.05f, false);   // Drain Grate: "its 1 m patch"
            Add("PROP-013", 0.8f, 0.8f, 0.8f);            // Briar Bundle
            Add("PROP-014", 0.2f, 2.0f, 1.8f);            // Canvas Screen: "Opaque 1.8 m cover"
            Add("PROP-015", 0.6f, 0.6f, 0.6f);            // Resin Pot
            Add("PROP-016", 1.0f, 2.0f, 1.2f);            // Timber Stack: two visible halves
            Add("PROP-017", 1.5f, 1.5f, 0.1f, false);    // Dry Leaf Bed: "1.5 m dust cloud" source bed
            Add("PROP-018", 0.5f, 0.5f, 0.8f);            // Seed Lantern
            Add("PROP-019", 0.8f, 0.8f, 1.8f);            // Ray Prism: fixed visible normal along local +Z
            Add("PROP-020", 0.3f, 1.5f, 2.0f);            // Shutter Mirror: opaque base panel
            Add("PROP-021", 0.8f, 0.8f, 1.2f);            // Frost Lens
            Add("PROP-022", 0.6f, 0.6f, 2.2f);            // Dimming Lamp
            Add("PROP-023", 0.6f, 0.6f, 0.5f);            // Sunstone
            Add("PROP-024", 0.8f, 0.8f, 1.5f);            // Signal Crystal
            Add("PROP-025", 1.4f, 1.4f, 1.4f);            // Rolling Boulder
            Add("PROP-026", 0.8f, 1.6f, 1.6f, true, 4f);  // Shield Cart: "marked 4 m track", "cover remains 1.6 m tall"
            Add("PROP-027", 0.5f, 0.5f, 0.5f);            // Glass Jug
            Add("PROP-028", 0.8f, 0.8f, 3.0f, true, 2f);  // Pendulum Weight: "marked 2 m arc"
            Add("PROP-029", 0.6f, 1.0f, 0.6f);            // Sandbag
            Add("PROP-030", 1.0f, 1.0f, 0.05f, false);   // Counterweight Plate: "connected 1 m cover plate" lies flush
            Add("PROP-031", 0.3f, 1.2f, 2.0f);            // Resonant Gong
            Add("PROP-032", 0.6f, 0.6f, 0.8f);            // Echo Vase
            Add("PROP-033", 0.2f, 2.0f, 1.8f);            // Quiet Felt Screen
            Add("PROP-034", 0.2f, 2.0f, 0.05f, false);   // Chime Chain: "marked strip"
            Add("PROP-035", 1.0f, 1.0f, 1.0f);            // Drum Pedestal: "1.5 m 2 s cloud"
            Add("PROP-036", 1.0f, 1.0f, 3.0f);            // Hollow Pillar: upper cover portion
            Add("PROP-037", 1.0f, 1.0f, 1.4f);            // Brittle Crystal
            Add("PROP-038", 0.6f, 0.6f, 0.6f);            // Venom Cap
            Add("PROP-039", 0.5f, 0.5f, 0.4f);            // Gravity Pebble
            Add("PROP-040", 0.4f, 0.4f, 0.8f);            // Spirit Wick
            Add("PROP-041", 0.8f, 0.8f, 1.2f);            // Arcane Knot: "its 1 m zone"
            Add("PROP-042", 1.0f, 1.0f, 1.2f);            // Dust Siphon
            Add("PROP-043", 0.4f, 2.0f, 1.6f);            // Repairable Cover Frame
            Add("PROP-044", 1.5f, 2.5f, 0.3f, false);    // Launch Ramp: walked up, never a barrier
            Add("PROP-045", 0.6f, 0.6f, 1.0f);            // Anchoring Ring
            Add("PROP-046", 0.6f, 0.6f, 1.5f);            // Clearance Beacon
            Add("PROP-047", 1.0f, 1.0f, 0.05f, false);   // Insulation Tile: "A 1 m dry tile"
            Add("PROP-048", 0.6f, 0.6f, 0.05f, false);   // Return Marker
            return d;
        }
    }
}
