using System;
using Confluence.Client.Arena;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Sim.Movement;
using Confluence.Sim.World;

namespace Confluence.Client.Laboratory
{
    /// <summary>
    /// Laboratory modification (spec L69): a copy of a generated layout whose two spawn points are moved next to the
    /// central floor, a few meters apart, facing each other with clear sight, so the sandbox starts at sparring range.
    /// Only the spawn points (and their alcove boxes) change; terrain, materials, props and the boundary stay as
    /// generated. A real round always uses the generated spawns (spec L47).
    /// </summary>
    public static class SparringLayout
    {
        public const float SparringDistance = 5f;
        public const float AlcoveHalfSize = 2f;
        public const float AlcoveHalfHeight = 1.5f;

        public static ArenaLayout Create(ArenaLayout source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            ArenaLayout layout = source.Clone();
            var world = CollisionWorld.FromLayout(layout);
            Vec3 center = layout.Center;
            Vec3 toSpawn = layout.Spawns != null && layout.Spawns.Length > 0 ? layout.Spawns[0].Position - center : new Vec3(0f, 0f, -1f);
            toSpawn = new Vec3(toSpawn.X, 0f, toSpawn.Z);
            Vec3 dir = toSpawn.Length() > 1e-3f ? toSpawn.Normalized() : new Vec3(0f, 0f, -1f);

            Vec3 dummy = FindClearFeet(layout, world, center + dir * 0.5f, center);
            Vec3 player = FindClearFeet(layout, world, dummy + dir * SparringDistance, dummy);
            PilotMath.Aim(player, dummy, out float playerYaw, out _);
            PilotMath.Aim(dummy, player, out float dummyYaw, out _);
            layout.Spawns = new[] { Spawn(0, player, playerYaw), Spawn(1, dummy, dummyYaw) };
            return layout;
        }

        private static SpawnPoint Spawn(int side, Vec3 feet, float yaw)
        {
            return new SpawnPoint
            {
                Side = side,
                Position = feet,
                YawDeg = yaw,
                Alcove = new YawBox(feet + new Vec3(0f, AlcoveHalfHeight, 0f), new Vec3(AlcoveHalfSize, AlcoveHalfHeight, AlcoveHalfSize), yaw),
            };
        }

        /// <summary>
        /// The nearest standable point to <paramref name="desired"/> (rings of 0.5 m, up to 6 m) on ground whose standing
        /// capsule is clear, preferring points that see <paramref name="lookAt"/> at eye height.
        /// </summary>
        public static Vec3 FindClearFeet(ArenaLayout layout, CollisionWorld world, Vec3 desired, Vec3 lookAt)
        {
            Vec3 fallback = desired;
            bool haveFallback = false;
            for (int ring = 0; ring <= 12; ring++)
            {
                float radius = ring * 0.5f;
                int steps = ring == 0 ? 1 : 8 + ring * 4;
                for (int k = 0; k < steps; k++)
                {
                    double angle = 2.0 * Math.PI * k / steps;
                    var probe = new Vec3(desired.X + radius * (float)Math.Sin(angle), 0f, desired.Z + radius * (float)Math.Cos(angle));
                    if (!TryStand(layout, world, probe, out Vec3 feet))
                    {
                        continue;
                    }

                    Vec3 eye = feet + new Vec3(0f, 1.6f, 0f);
                    if (world.HasLineOfSight(eye, lookAt + new Vec3(0f, 1.2f, 0f)))
                    {
                        return feet;
                    }

                    if (!haveFallback)
                    {
                        fallback = feet;
                        haveFallback = true;
                    }
                }
            }

            return fallback;
        }

        private static bool TryStand(ArenaLayout layout, CollisionWorld world, Vec3 probe, out Vec3 feet)
        {
            float y = ArenaQueries.SurfaceHeightAt(layout, probe.X, probe.Z, layout.Center.Y + 2f);
            feet = new Vec3(probe.X, y, probe.Z);
            if (float.IsNaN(y) || float.IsInfinity(y) || Math.Abs(y - layout.Center.Y) > 0.75f)
            {
                return false;
            }

            MotorState state = MotorState.At(feet, 0f);
            if (!CharacterMotor.SnapToGround(ref state, world))
            {
                return false;
            }

            feet = state.Position;
            Capsule body = Capsule.Vertical(feet + new Vec3(0f, 0.02f, 0f), state.Height, CharacterMotor.Radius + 0.05f);
            return world.IsCapsuleClear(body, 1e-3f);
        }
    }
}
