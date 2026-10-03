using System;
using Confluence.Client.Arena;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Sim.Movement;
using Confluence.Sim.World;

namespace Confluence.Client.Laboratory
{
    /// <summary>
    /// Laboratory modification (spec L69): a copy of a generated layout whose two spawn points are moved onto the
    /// anchored final floor (spec L48, L678), a few meters apart, facing each other with clear sight and a clear walking
    /// line, so the sandbox starts at sparring range and the contracting boundary never reaches either fighter. Only
    /// the spawn points (and their alcove boxes) change; terrain, materials, props and the boundary stay as generated.
    /// A real round always uses the generated spawns (spec L47).
    /// </summary>
    public static class SparringLayout
    {
        public const float SparringDistance = 3.2f;
        public const float AlcoveHalfSize = 2f;
        public const float AlcoveHalfHeight = 1.5f;
        public const float FloorMargin = 0.6f;

        private static readonly float[] Distances = { SparringDistance, 2.8f, 3.6f, 2.4f };

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
            float baseYaw = (float)(Math.Atan2(toSpawn.X, toSpawn.Z) * 180.0 / Math.PI);
            float maxFromCenter = Math.Max(1.5f, layout.FinalFloorRadius - FloorMargin);

            Vec3 dummy = Nearest(layout, world, center, 1.5f, out bool _);
            Vec3 player = dummy + Direction(baseYaw) * SparringDistance;
            bool found = false;
            foreach (float distance in Distances)
            {
                for (int k = 0; k < 12 && !found; k++)
                {
                    // Try the original spawn side first, then alternate around it in 30-degree steps.
                    float yaw = baseYaw + (k % 2 == 0 ? 1 : -1) * 30f * ((k + 1) / 2);
                    Vec3 probe = dummy + Direction(yaw) * distance;
                    if (Vec3.DistanceXZ(probe, center) > maxFromCenter || !TryStand(layout, world, probe, out Vec3 feet))
                    {
                        continue;
                    }

                    if (CanSpar(world, feet, dummy))
                    {
                        player = feet;
                        found = true;
                    }
                }

                if (found)
                {
                    break;
                }
            }

            PilotMath.Aim(player, dummy, out float playerYaw, out _);
            PilotMath.Aim(dummy, player, out float dummyYaw, out _);
            layout.Spawns = new[] { Spawn(0, player, playerYaw), Spawn(1, dummy, dummyYaw) };
            return layout;
        }

        /// <summary>Clear sight at eye height and a clear capsule path between the two fighters.</summary>
        public static bool CanSpar(CollisionWorld world, Vec3 a, Vec3 b)
        {
            var eye = new Vec3(0f, 1.6f, 0f);
            if (!world.HasLineOfSight(a + eye, b + new Vec3(0f, 1.2f, 0f)))
            {
                return false;
            }

            var waist = new Vec3(0f, 0.7f, 0f);
            Vec3 along = b - a;
            float length = along.Length();
            if (length < 1e-3f)
            {
                return false;
            }

            Vec3 end = a + along * ((length - 0.9f) / length); // stop short of the other body
            return !world.SphereSweep(a + waist, end + waist, CharacterMotor.Radius, SolidMask.Movement, out _);
        }

        private static Vec3 Direction(float yawDeg)
        {
            double r = yawDeg * Math.PI / 180.0;
            return new Vec3((float)Math.Sin(r), 0f, (float)Math.Cos(r));
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

        /// <summary>The nearest standable point to <paramref name="desired"/> in rings of 0.25 m, up to the given radius.</summary>
        private static Vec3 Nearest(ArenaLayout layout, CollisionWorld world, Vec3 desired, float maxRadius, out bool found)
        {
            for (float radius = 0f; radius <= maxRadius + 1e-3f; radius += 0.25f)
            {
                int steps = radius <= 0f ? 1 : 8 + (int)(radius * 16f);
                for (int k = 0; k < steps; k++)
                {
                    double angle = 2.0 * Math.PI * k / steps;
                    var probe = new Vec3(desired.X + radius * (float)Math.Sin(angle), 0f, desired.Z + radius * (float)Math.Cos(angle));
                    if (TryStand(layout, world, probe, out Vec3 feet))
                    {
                        found = true;
                        return feet;
                    }
                }
            }

            found = false;
            return desired;
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
