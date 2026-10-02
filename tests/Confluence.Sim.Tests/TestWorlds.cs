using System.Collections.Generic;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Movement;
using Confluence.Sim.Players;
using Confluence.Sim.World;

namespace Confluence.Sim.Tests
{
    /// <summary>Small hand-built arenas and duel helpers (the Arena generator is a separate stage).</summary>
    internal static class TestWorlds
    {
        public const int FloorId = 1;

        /// <summary>A flat 40 x 40 m floor whose top is y = 0, two facing spawns 12 m apart, a circular boundary.</summary>
        public static ArenaLayout Flat(float boundaryRadius = 18f, float floorHalf = 20f)
        {
            var layout = new ArenaLayout
            {
                Seed = 12345UL,
                Diameter = 40,
                Center = Vec3.Zero,
                Boundary = BoundaryContour.Circle(Vec3.Zero, boundaryRadius),
            };
            layout.Solids.Add(Box(FloorId, new Vec3(0f, -1f, 0f), new Vec3(2f * floorHalf, 1f, 2f * floorHalf), SolidKind.Floor));
            layout.Spawns = new[]
            {
                new SpawnPoint { Side = 0, Position = new Vec3(0f, 0f, -6f), YawDeg = 0f, Alcove = new YawBox(new Vec3(0f, 1.5f, -6f), new Vec3(2f, 1.5f, 2f), 0f) },
                new SpawnPoint { Side = 1, Position = new Vec3(0f, 0f, 6f), YawDeg = 180f, Alcove = new YawBox(new Vec3(0f, 1.5f, 6f), new Vec3(2f, 1.5f, 2f), 180f) },
            };
            return layout;
        }

        public static Solid Box(int id, Vec3 floorCenter, Vec3 size, SolidKind kind = SolidKind.Wall, float yawDeg = 0f, float durability = 0f)
        {
            return new Solid
            {
                Id = id,
                Kind = kind,
                Box = YawBox.OnFloor(floorCenter, size, yawDeg),
                Durability = durability,
                Anchored = durability <= 0f,
                BlocksSight = true,
                Walkable = kind == SolidKind.Floor || kind == SolidKind.Platform,
            };
        }

        /// <summary>A wedge ramp on the floor rising toward +Z (yaw 0) by rise over its length.</summary>
        public static Solid Ramp(int id, Vec3 floorCenter, float halfWidth, float halfLength, float rise, float yawDeg = 0f)
        {
            RampShape shape = RampShape.Wedge(floorCenter, halfWidth, halfLength, yawDeg, rise);
            return new Solid { Id = id, Kind = SolidKind.Ramp, Box = shape.Box, RampRise = rise, Anchored = true, BlocksSight = true, Walkable = true };
        }

        public static DuelPlayerSetup Setup(string id = "P")
        {
            return new DuelPlayerSetup { PlayerId = id, WizardId = "WIZ-002" };
        }

        /// <summary>A duel already in combat (no alcove phase) unless alcoveTicks is given.</summary>
        public static DuelWorld Duel(ArenaLayout layout = null, DuelPlayerSetup a = null, DuelPlayerSetup b = null, int alcoveTicks = 0,
            int combatTicks = -1, ISpellRuntime spells = null, IWorldMaterials materials = null)
        {
            var options = new DuelWorldOptions { AlcoveTicks = alcoveTicks, SpellRuntime = spells, Materials = materials };
            if (combatTicks > 0)
            {
                options.CombatTicks = combatTicks;
            }

            return new DuelWorld(layout ?? Flat(), a ?? Setup("A"), b ?? Setup("B"), options);
        }

        public static void Run(DuelWorld world, int ticks, PlayerInput a = default, PlayerInput b = default)
        {
            for (int i = 0; i < ticks && !world.IsOver; i++)
            {
                world.Step(a, b);
            }
        }

        public static void RunSeconds(DuelWorld world, float seconds, PlayerInput a = default, PlayerInput b = default)
        {
            Run(world, Ticks.FromSeconds(seconds), a, b);
        }

        /// <summary>Puts a player at a position facing a yaw, grounded (setup only).</summary>
        public static void Place(DuelWorld world, int player, Vec3 feet, float yawDeg)
        {
            DuelPlayer p = world.Player(player);
            p.Motor = MotorState.At(feet, yawDeg);
            p.Motor.Grounded = true;
        }

        public static PlayerInput Face(float yawDeg) => PlayerInput.Idle(yawDeg);

        // ----- direct motor stepping --------------------------------------------------------------------

        public static CollisionWorld Collision(params Solid[] extra)
        {
            var solids = new List<Solid> { Box(FloorId, new Vec3(0f, -1f, 0f), new Vec3(60f, 1f, 60f), SolidKind.Floor) };
            solids.AddRange(extra);
            return new CollisionWorld(solids);
        }

        public static MotorState Settled(CollisionWorld world, Vec3 feet, float yawDeg = 0f)
        {
            MotorState s = MotorState.At(feet, yawDeg);
            MotorCommand idle = MotorCommand.Default;
            idle.YawDeg = yawDeg;
            for (int i = 0; i < 5; i++)
            {
                CharacterMotor.Step(ref s, idle, world, Ticks.Dt, out _);
            }

            return s;
        }

        public static MotorCommand Forward(float yawDeg = 0f, float y = 1f, float x = 0f)
        {
            MotorCommand c = MotorCommand.Default;
            c.YawDeg = yawDeg;
            c.Move = new Vec2(x, y);
            return c;
        }

        public static void StepMotor(ref MotorState s, MotorCommand c, CollisionWorld world, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                CharacterMotor.Step(ref s, c, world, Ticks.Dt, out _);
            }
        }

        public static float Walk => GlobalRules.Movement.WalkSpeed;
    }
}
