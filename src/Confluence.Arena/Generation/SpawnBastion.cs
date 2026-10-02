using System;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;

namespace Confluence.Arena.Generation
{
    /// <summary>
    /// The protected spawn alcove of side 0 and its screen (spec L46-L47; side 1 is its half-turn image). A 5 by 3 m
    /// alcove sits between an anchored back wall and a chevron screen: a short center segment hides the spawn from
    /// the opponent (L47, L310), and two wings sweep forward at <see cref="WingAngleDeg"/> toward the center. The
    /// player leaves past either wing tip, so each spawn has two disjoint exits, and the wing length sets the
    /// walking detour that keeps useful central cover 2.0-3.5 s away (L310). Because the wings lean inward, every
    /// way out of the alcove moves toward the center, so the contracting boundary never traps it (L48, L676).
    /// </summary>
    internal sealed class SpawnBastion
    {
        public const float InteriorHalfWidth = 2.5f;
        public const float InteriorHalfDepth = 1.5f;
        public const float ScreenCenterHalfWidth = 1.5f;
        public const float WingAngleDeg = 20f;
        public const float Thickness = 1.0f;

        /// <summary>Back wall thickness (anchored, 3 m tall).</summary>
        public const float BackThickness = 1.0f;
        public const float BackWallHalfWidth = 3f;
        public const float Height = ArenaRules.TallWallHeight;

        /// <summary>A back strip narrower than this between bastion and contour is filled with anchored rock.</summary>
        public const float BackingGapLimit = 3.5f;

        public const float BackingHalfWidth = 3.5f;

        private readonly LayoutWriter _w;
        private Solid _eastWing;
        private Solid _westWing;

        public SpawnBastion(LayoutWriter writer, float spawnDistance, float offsetX, float wingLength)
        {
            _w = writer;
            SpawnZ = -spawnDistance;
            OffsetX = offsetX;
            WingLength = wingLength;
        }

        public float SpawnZ { get; }

        /// <summary>
        /// Lateral offset of side 0's bastion. The half turn moves side 1's bastion the other way, so the exits of the
        /// two bastions no longer line up into one straight sightline along the spawn axis (spec L310 lane limit).
        /// </summary>
        public float OffsetX { get; }

        public float WingLength { get; private set; }

        /// <summary>Z of the screen's back face (the alcove's front edge).</summary>
        public float ScreenZ => SpawnZ + InteriorHalfDepth;

        /// <summary>Half width of the whole screen: the wing tips' outer x.</summary>
        public float LateralExtent
        {
            get
            {
                CMath.SinCosDeg(WingAngleDeg, out float s, out float c);
                return ScreenCenterHalfWidth + WingLength * c + Thickness * s;
            }
        }

        /// <summary>Front-most z of the screen (the wing tips).</summary>
        public float TipZ
        {
            get
            {
                CMath.SinCosDeg(WingAngleDeg, out float s, out float c);
                return ScreenZ + Thickness + WingLength * s;
            }
        }

        /// <summary>Wing length that gives a requested lateral half extent.</summary>
        public static float WingLengthFor(float lateralExtent)
        {
            CMath.SinCosDeg(WingAngleDeg, out float s, out float c);
            return Math.Max(0.5f, (lateralExtent - ScreenCenterHalfWidth - Thickness * s) / c);
        }

        public void Build(ArenaLayout layout)
        {
            float z = SpawnZ;
            float backZ = z - InteriorHalfDepth;
            float x0 = OffsetX;
            Solid back = _w.NewSolid(SolidKind.AlcoveWall, Box(x0 - BackWallHalfWidth, backZ - BackThickness, x0 + BackWallHalfWidth, backZ), ArenaMaterials.Alcove, 0f);
            _w.AddMirroredPair(back);
            Solid center = _w.NewSolid(SolidKind.AlcoveWall, Box(x0 - ScreenCenterHalfWidth, ScreenZ, x0 + ScreenCenterHalfWidth, ScreenZ + Thickness), ArenaMaterials.Alcove, 0f);
            _w.AddMirroredPair(center);
            _eastWing = _w.AddMirroredPair(_w.NewSolid(SolidKind.AlcoveWall, WingBox(1), ArenaMaterials.Alcove, 0f));
            _westWing = _w.AddMirroredPair(_w.NewSolid(SolidKind.AlcoveWall, WingBox(-1), ArenaMaterials.Alcove, 0f));

            // Where the contour runs close behind the back wall, anchored rock fills the strip up to and past the
            // boundary, so no floor cell hides behind the bastion while the boundary contracts (spec L48, L676).
            float backOuter = backZ - BackThickness;
            float nearest = float.PositiveInfinity;
            float farthest = 0f;
            for (float x = -BackingHalfWidth; x <= BackingHalfWidth + 1e-3f; x += BackingHalfWidth * 0.5f)
            {
                float margin = layout.Boundary.RadialMargin(new Vec3(x0 + x, 0f, backOuter), 0f);
                nearest = Math.Min(nearest, margin);
                farthest = Math.Max(farthest, margin);
            }

            if (nearest < BackingGapLimit)
            {
                float depth = Math.Max(0.5f, farthest) + 1f;
                _w.AddMirroredPair(_w.NewSolid(SolidKind.Wall, Box(x0 - BackingHalfWidth, backOuter - depth, x0 + BackingHalfWidth, backOuter), ArenaMaterials.Rock, 0f));
            }

            var alcove = new YawBox(new Vec3(x0, 1.5f, z), new Vec3(InteriorHalfWidth, 1.5f, InteriorHalfDepth), 0f);
            layout.Spawns[0] = new SpawnPoint { Side = 0, Position = new Vec3(x0, 0f, z), YawDeg = 0f, Alcove = alcove };
            layout.Spawns[1] = new SpawnPoint
            {
                Side = 1,
                Position = MirrorMath.Point(new Vec3(x0, 0f, z)),
                YawDeg = 180f,
                Alcove = MirrorMath.Box(alcove),
            };
        }

        /// <summary>Changes both wings on both sides (the arena stays mirrored).</summary>
        public void SetWingLength(ArenaLayout layout, float length)
        {
            WingLength = length;
            Update(layout, _eastWing, WingBox(1));
            Update(layout, _westWing, WingBox(-1));
        }

        /// <summary>Bastion, exits, and the crossover strip in front of the screen (a skeleton keep-out zone).</summary>
        public Rect2 Zone(float corridorWidth)
        {
            float half = LateralExtent + 0.25f + corridorWidth;
            return new Rect2(OffsetX - half, SpawnZ - InteriorHalfDepth - BackThickness - 0.5f, OffsetX + half, TipZ + corridorWidth);
        }

        /// <summary>
        /// The bastion's keep-out zones: the bastion itself with the walk behind each wing, a turning space around
        /// each wing tip, and the crossover strip in front of the screen. The floor beside the bastion behind the
        /// tips is not part of any route and stays open to terrain that closes sightlines past the tips.
        /// </summary>
        public Rect2[] Zones(float exitWidth)
        {
            float e = LateralExtent;
            float back = SpawnZ - InteriorHalfDepth - BackThickness - 0.5f;
            float turnBack = TipZ - Thickness - exitWidth;
            return new[]
            {
                new Rect2(OffsetX - e, back, OffsetX + e, TipZ),
                new Rect2(OffsetX + e - 0.5f, turnBack, OffsetX + e + 0.25f + exitWidth, TipZ + exitWidth),
                new Rect2(OffsetX - e - 0.25f - exitWidth, turnBack, OffsetX - e + 0.5f, TipZ + exitWidth),
                new Rect2(OffsetX - e - 0.25f - exitWidth, TipZ, OffsetX + e + 0.25f + exitWidth, TipZ + exitWidth),
            };
        }

        /// <summary>The bastion between its back wall and screen (no filler rock may stand here).</summary>
        public Rect2 Core()
        {
            return new Rect2(OffsetX - LateralExtent, SpawnZ - InteriorHalfDepth - 1f, OffsetX + LateralExtent, TipZ + 0.75f);
        }

        private static void Update(ArenaLayout layout, Solid solid, YawBox box)
        {
            solid.Box = box;
            layout.FindSolid(solid.MirrorId).Box = MirrorMath.Box(box);
        }

        private YawBox WingBox(int sign)
        {
            CMath.SinCosDeg(WingAngleDeg, out float s, out float c);
            Vec2 start = new Vec2(OffsetX + sign * (ScreenCenterHalfWidth - Thickness * 0.5f), ScreenZ + Thickness * 0.5f);
            Vec2 dir = new Vec2(sign * c, s);
            float half = WingLength * 0.5f + Thickness * 0.25f;
            Vec2 mid = start + dir * half;
            return new YawBox(new Vec3(mid.X, Height * 0.5f, mid.Y), new Vec3(half, Height * 0.5f, Thickness * 0.5f), sign > 0 ? -WingAngleDeg : WingAngleDeg);
        }

        private static YawBox Box(float x0, float z0, float x1, float z1)
        {
            return new YawBox(
                new Vec3((x0 + x1) * 0.5f, Height * 0.5f, (z0 + z1) * 0.5f),
                new Vec3(Math.Abs(x1 - x0) * 0.5f, Height * 0.5f, Math.Abs(z1 - z0) * 0.5f),
                0f);
        }
    }
}
