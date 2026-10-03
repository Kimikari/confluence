using System;
using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Net.Game.Snapshots;
using Confluence.Sim.Input;
using Confluence.Sim.Movement;
using Confluence.Sim.World;

namespace Confluence.Net.Game.Client
{
    /// <summary>
    /// Client-side prediction of the local player's movement with the Sim's own <see cref="CharacterMotor"/> (spec L419).
    /// It steps against a mirror of the server's collision: the public layout plus every dynamic solid and destroyed
    /// layout solid replicated in snapshots, rebuilt when that set changes. The motor command is built the way the
    /// server's tick-order step 2 builds it, with the inputs the server sends in the private state (casting movement
    /// factor, slows, surface, locks, tuning, stamina) and the alcove confinement and boundary from the public layout.
    /// </summary>
    public sealed class MotorPredictor
    {
        private readonly ArenaLayout _layout;
        private readonly int _alcoveTicks;
        private readonly YawBox _alcove;
        private readonly Boundary _boundary;
        private List<SolidEntry> _appliedSolids = new List<SolidEntry>();

        public MotorPredictor(ArenaLayout layout, int alcoveTicks, YawBox alcove)
        {
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            _alcoveTicks = alcoveTicks;
            _alcove = alcove;
            _boundary = new Boundary(layout.Boundary);
            Collision = CollisionWorld.FromLayoutWithProps(layout);
        }

        public CollisionWorld Collision { get; private set; }

        /// <summary>Times the collision mirror was rebuilt from replicated solids.</summary>
        public int CollisionRebuilds { get; private set; }

        /// <summary>Rebuilds the collision mirror when the replicated solid set changed.</summary>
        public void ApplySolids(List<SolidEntry> solids)
        {
            if (solids == null || KeyedSection.SameSection(solids, _appliedSolids)) return;
            CollisionWorld world = CollisionWorld.FromLayout(_layout);
            foreach (SolidEntry s in solids)
            {
                if (s.Dynamic)
                {
                    if (!s.Active) continue;
                    world.AddTemporarySolid(new TemporarySolidSpec
                    {
                        Box = s.Box,
                        Kind = (SolidKind)s.Kind,
                        Flags = (MaterialFlags)s.Flags,
                        BlocksSight = s.BlocksSight,
                        Walkable = s.Walkable,
                        Durability = 0f,
                        LifetimeTicks = 0,
                        OwnerPlayer = -1,
                    }, 0);
                }
                else if (!world.Remove(s.Key))
                {
                    world.DamageSolid(s.Key, float.MaxValue, out _);
                }
            }

            Collision = world;
            _appliedSolids = solids;
            CollisionRebuilds++;
        }

        /// <summary>
        /// One predicted motor step for the command executed at duel tick <paramref name="tick"/> (the world's tick after
        /// its increment). <paramref name="input"/> must be canonical (as the server decodes it).
        /// </summary>
        public MotorState Step(MotorState state, in PlayerInput input, InputButtons previousButtons, int tick, PrivateEntry context, ref float stamina)
        {
            PlayerInput i = Wire.PlayerInputCodec.SanitizeLikeServer(input, input.SelectedSlot);
            InputButtons pressed = i.Buttons & ~previousButtons;
            var cmd = new MotorCommand
            {
                Move = new Vec2(i.MoveX, i.MoveY),
                YawDeg = i.YawDeg,
                PitchDeg = i.PitchDeg,
                JumpPressed = (pressed & InputButtons.Jump) != 0,
                SprintHeld = (i.Buttons & InputButtons.Sprint) != 0,
                CrouchHeld = (i.Buttons & InputButtons.Crouch) != 0,
                DodgePressed = (pressed & InputButtons.Dodge) != 0,
                Stamina = Math.Max(0f, stamina),
                CastSpeedFactor = context != null ? context.CastMovementFactor : 1f,
                Traction = context != null ? context.Traction : 0f,
                Tuning = context != null ? context.Tuning : default,
                SpeedMultiplier = context != null ? context.SpeedMultiplier : 1f,
                Locks = context != null ? context.Locks : MotorLocks.None,
                Surface = context != null ? new SurfaceInfo { Kind = (SurfaceKind)context.SurfaceKind, SlowFraction = context.SurfaceSlow } : SurfaceInfo.Normal,
            };

            if (tick <= _alcoveTicks)
            {
                cmd.Confined = true; // spec L46: movement within the alcove
                cmd.Confinement = _alcove;
            }
            else
            {
                _boundary.CombatSeconds = Math.Max(0, tick - _alcoveTicks) / (float)Ticks.PerSecond;
                cmd.DisplacementBounds = _boundary; // spec L51
            }

            CharacterMotor.Step(ref state, cmd, Collision, Ticks.Dt, out MotorReport report);
            if (report.StaminaSpent > 0f) stamina -= Math.Min(report.StaminaSpent, stamina);
            return state;
        }

        /// <summary>The legal boundary at a combat time, as the server's constraint computes it.</summary>
        private sealed class Boundary : IPositionConstraint
        {
            private readonly BoundaryContour _contour;

            public Boundary(BoundaryContour contour)
            {
                _contour = contour;
            }

            public float CombatSeconds { get; set; }

            public bool Contains(Vec3 point) => _contour == null || _contour.Contains(point, CombatSeconds);

            public Vec3 ClampInside(Vec3 point, float margin) => _contour == null ? point : _contour.ClampInside(point, CombatSeconds, margin);
        }
    }
}
