using System;
using Confluence.Core.Geometry;
using Confluence.Core.Time;
using Confluence.Sim.Movement;
using Confluence.Sim.World;

namespace Confluence.Client.Movement
{
    /// <summary>
    /// A character moved locally by the shared <see cref="CharacterMotor"/>: the exact step function the server runs,
    /// one authoritative tick at a time, against a Sim <see cref="CollisionWorld"/> built from the layout
    /// (docs/ARCHITECTURE.md "Networking": the client predicts its own movement with Sim.CharacterMotor; DECISIONS
    /// D-003). It keeps the previous tick's state so presentation can interpolate between ticks. This is the basis of
    /// client prediction; the arena preview's walk mode uses it with unlimited stamina and no world statuses.
    /// </summary>
    public sealed class LocalMotor
    {
        private MotorState _state;
        private MotorState _previous;

        public LocalMotor(CollisionWorld world, Vec3 feet, float yawDeg)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            Respawn = feet;
            RespawnYawDeg = yawDeg;
            Teleport(feet, yawDeg);
        }

        public CollisionWorld World { get; }

        /// <summary>The state after the last simulated tick.</summary>
        public MotorState State => _state;

        /// <summary>The state one tick earlier (interpolation source).</summary>
        public MotorState Previous => _previous;

        public MotorReport LastReport { get; private set; }

        /// <summary>Ticks simulated since construction.</summary>
        public int TicksStepped { get; private set; }

        /// <summary>Where an out-of-map fall returns the character (the preview has no recall penalty, spec L175).</summary>
        public Vec3 Respawn { get; set; }

        public float RespawnYawDeg { get; set; }

        /// <summary>Places the character and attaches it to the ground below, if any (no interpolation across the jump).</summary>
        public void Teleport(Vec3 feet, float yawDeg)
        {
            _state = MotorState.At(feet, yawDeg);
            CharacterMotor.SnapToGround(ref _state, World);
            _previous = _state;
        }

        /// <summary>Advances exactly one tick with the given command.</summary>
        public void Step(in MotorCommand command)
        {
            _previous = _state;
            CharacterMotor.Step(ref _state, command, World, Ticks.Dt, out MotorReport report);
            LastReport = report;
            TicksStepped++;
            if (report.FellOutOfMap)
            {
                Teleport(Respawn, RespawnYawDeg);
            }
        }

        /// <summary>Feet position between the previous and the current tick (alpha 0-1).</summary>
        public Vec3 InterpolatedFeet(float alpha) => Vec3.Lerp(_previous.Position, _state.Position, alpha);

        /// <summary>Eye height between ticks, so crouching lowers the camera smoothly.</summary>
        public float InterpolatedEyeHeight(float alpha) => CMath.Lerp(_previous.EyeHeight, _state.EyeHeight, alpha);
    }
}
