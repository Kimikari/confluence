using System;

namespace Confluence.Core.Geometry
{
    /// <summary>
    /// Box rotated about the vertical axis: the arena's standard solid (walls, cover, platforms, alcoves).
    /// Local +Z is the box's forward (yaw direction) and local +X its right. Immutable; the rotation is cached.
    /// </summary>
    public readonly struct YawBox
    {
        public readonly Vec3 Center;
        public readonly Vec3 HalfExtents;
        public readonly float YawDeg;

        // Cached rotation. Cosine is stored minus one so default(YawBox) is an unrotated box.
        private readonly float _sin;
        private readonly float _cosMinusOne;

        public YawBox(Vec3 center, Vec3 halfExtents, float yawDeg)
        {
            Center = center;
            HalfExtents = Vec3.Abs(halfExtents);
            YawDeg = yawDeg;
            CMath.SinCosDeg(yawDeg, out float s, out float c);
            _sin = s;
            _cosMinusOne = c - 1f;
        }

        /// <summary>Box standing on a floor point: the bottom face is at floorCenter.Y.</summary>
        public static YawBox OnFloor(Vec3 floorCenter, Vec3 size, float yawDeg)
        {
            Vec3 half = Vec3.Abs(size) * 0.5f;
            return new YawBox(floorCenter + new Vec3(0f, half.Y, 0f), half, yawDeg);
        }

        private float Cos => _cosMinusOne + 1f;

        public Vec3 Size => HalfExtents * 2f;
        public float Bottom => Center.Y - HalfExtents.Y;
        public float Top => Center.Y + HalfExtents.Y;
        public float FootprintArea => 4f * HalfExtents.X * HalfExtents.Z;

        /// <summary>World direction of local +X.</summary>
        public Vec3 Right => new Vec3(Cos, 0f, -_sin);

        /// <summary>World direction of local +Z.</summary>
        public Vec3 Forward => new Vec3(_sin, 0f, Cos);

        public Vec3 ToLocal(Vec3 world)
        {
            float dx = world.X - Center.X;
            float dz = world.Z - Center.Z;
            float c = Cos;
            return new Vec3(dx * c - dz * _sin, world.Y - Center.Y, dx * _sin + dz * c);
        }

        public Vec3 ToWorld(Vec3 local)
        {
            float c = Cos;
            return new Vec3(
                Center.X + local.X * c + local.Z * _sin,
                Center.Y + local.Y,
                Center.Z - local.X * _sin + local.Z * c);
        }

        public Vec3 DirectionToLocal(Vec3 world)
        {
            float c = Cos;
            return new Vec3(world.X * c - world.Z * _sin, world.Y, world.X * _sin + world.Z * c);
        }

        public Vec3 DirectionToWorld(Vec3 local)
        {
            float c = Cos;
            return new Vec3(local.X * c + local.Z * _sin, local.Y, -local.X * _sin + local.Z * c);
        }

        public bool Contains(Vec3 point) => BoxMath.Contains(ToLocal(point), HalfExtents);

        /// <summary>True if the ground-plane point (X, Y = world Z) lies inside the footprint.</summary>
        public bool ContainsXZ(Vec2 point)
        {
            Vec3 local = ToLocal(new Vec3(point.X, Center.Y, point.Y));
            return MathF.Abs(local.X) <= HalfExtents.X && MathF.Abs(local.Z) <= HalfExtents.Z;
        }

        /// <summary>Closest point of the solid box; the point itself when inside.</summary>
        public Vec3 ClosestPoint(Vec3 point) => ToWorld(BoxMath.Clamp(ToLocal(point), HalfExtents));

        /// <summary>Closest point of the footprint rectangle to a ground-plane point.</summary>
        public Vec2 ClosestPointXZ(Vec2 point)
        {
            Vec3 local = ToLocal(new Vec3(point.X, Center.Y, point.Y));
            local.X = CMath.Clamp(local.X, -HalfExtents.X, HalfExtents.X);
            local.Y = 0f;
            local.Z = CMath.Clamp(local.Z, -HalfExtents.Z, HalfExtents.Z);
            return ToWorld(local).XZ();
        }

        public float DistanceSquared(Vec3 point)
        {
            Vec3 local = ToLocal(point);
            return (local - BoxMath.Clamp(local, HalfExtents)).LengthSquared();
        }

        /// <summary>
        /// Raycast using a slab test in local space. A ray starting inside reports distance 0 with the
        /// reversed ray direction as its normal.
        /// </summary>
        public bool Raycast(Ray ray, float maxDistance, out RayHit hit)
        {
            Vec3 o = ToLocal(ray.Origin);
            Vec3 d = DirectionToLocal(ray.Direction);
            if (!BoxMath.Raycast(o, d, HalfExtents, maxDistance, out float t, out Vec3 localNormal, out bool inside))
            {
                hit = default;
                return false;
            }

            Vec3 normal = inside ? -ray.Direction : DirectionToWorld(localNormal);
            hit = new RayHit(t, ray.PointAt(t), normal);
            return true;
        }

        /// <summary>Sphere overlap; the normal points out of the box toward the sphere.</summary>
        public bool SphereOverlap(Vec3 center, float radius, out Penetration penetration)
        {
            if (!BoxMath.SpherePenetration(ToLocal(center), radius, HalfExtents, out Vec3 n, out float depth))
            {
                penetration = default;
                return false;
            }

            penetration = new Penetration(DirectionToWorld(n), depth);
            return true;
        }

        /// <summary>World corner by index; bit 0 selects +X, bit 1 +Y, bit 2 +Z (local axes).</summary>
        public Vec3 Corner(int index) => ToWorld(BoxMath.Corner(index & 7, HalfExtents));

        /// <summary>Writes the eight world corners into destination (length at least 8).</summary>
        public void GetCorners(Span<Vec3> destination)
        {
            for (int i = 0; i < 8; i++)
            {
                destination[i] = Corner(i);
            }
        }

        /// <summary>Footprint corner on the ground plane, counter-clockwise from local (-X, -Z) seen from above.</summary>
        public Vec2 FootprintCorner(int index)
        {
            float x;
            float z;
            switch (index & 3)
            {
                case 0: x = -HalfExtents.X; z = -HalfExtents.Z; break;
                case 1: x = HalfExtents.X; z = -HalfExtents.Z; break;
                case 2: x = HalfExtents.X; z = HalfExtents.Z; break;
                default: x = -HalfExtents.X; z = HalfExtents.Z; break;
            }

            return ToWorld(new Vec3(x, 0f, z)).XZ();
        }

        /// <summary>World-aligned bounds enclosing the rotated box.</summary>
        public Aabb Bounds()
        {
            float c = MathF.Abs(Cos);
            float s = MathF.Abs(_sin);
            Vec3 extents = new Vec3(
                c * HalfExtents.X + s * HalfExtents.Z,
                HalfExtents.Y,
                s * HalfExtents.X + c * HalfExtents.Z);
            return Aabb.FromCenterExtents(Center, extents);
        }

        public YawBox Translated(Vec3 delta) => new YawBox(Center + delta, HalfExtents, YawDeg);

        public YawBox WithCenter(Vec3 center) => new YawBox(center, HalfExtents, YawDeg);

        public YawBox WithYaw(float yawDeg) => new YawBox(Center, HalfExtents, yawDeg);

        /// <summary>Rotates the box about a vertical axis through pivot (used for mirrored layouts).</summary>
        public YawBox RotatedAbout(Vec3 pivot, float deltaYawDeg)
        {
            Vec2 offset = (Center - pivot).XZ().RotatedByYaw(deltaYawDeg);
            Vec3 center = new Vec3(pivot.X + offset.X, Center.Y, pivot.Z + offset.Y);
            return new YawBox(center, HalfExtents, CMath.WrapAngle(YawDeg + deltaYawDeg));
        }

        public override string ToString() => "YawBox(" + Center + ", half=" + HalfExtents + ", yaw=" + YawDeg.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ")";
    }
}
