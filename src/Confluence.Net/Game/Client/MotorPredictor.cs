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
    /// <summary>The own cast's movement effects as the local cast machine predicts them for one tick.</summary>
    public struct PredictedCast
    {
        public bool Valid;
        public float MovementFactor;
        public bool SprintLock;
    }

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
            return Step(state, input, previousButtons, tick, context, default, ref stamina);
        }

        /// <summary>
        /// As above, with the own cast's movement status predicted by the local cast machine (research §2.3.1) in place of
        /// the snapshot's when <paramref name="cast"/> is valid: the casting movement factor and the channel or quick-cast
        /// sprint lock (spec L80, L92). Opponent- and world-caused slows, stuns, and surfaces still come from the snapshot.
        /// </summary>
        public MotorState Step(MotorState state, in PlayerInput input, InputButtons previousButtons, int tick, PrivateEntry context, PredictedCast cast,
            ref float stamina)
        {
            // The visible 0.5 s recall holds the motor in place until its end tick (spec L175, D-109; DuelWorld step 2).
            if (context != null && context.RecallEndTick >= 0 && tick < context.RecallEndTick) return state;

            PlayerInput i = Wire.PlayerInputCodec.SanitizeLikeServer(input, input.SelectedSlot);
            InputButtons pressed = PressedEdges(i, previousButtons);
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
                CastSpeedFactor = cast.Valid ? cast.MovementFactor : context != null ? context.CastMovementFactor : 1f,
                Traction = context != null ? context.Traction : 0f,
                Tuning = context != null ? context.Tuning : default,
                SpeedMultiplier = context != null ? context.SpeedMultiplier : 1f,
                Locks = LocksWith(context, cast),
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

        /// <summary>
        /// Press edges the server's step 1 derives (DuelWorld.ApplyInput): newly held buttons, plus taps of buttons neither
        /// held now nor before (pressed and released inside the tick, or a press recovered from a trimmed command).
        /// </summary>
        public static InputButtons PressedEdges(in PlayerInput input, InputButtons previousButtons)
        {
            return (input.Buttons & ~previousButtons) | (input.Tapped & ~input.Buttons & ~previousButtons);
        }

        private static MotorLocks LocksWith(PrivateEntry context, PredictedCast cast)
        {
            MotorLocks locks = context != null ? context.Locks : MotorLocks.None;
            if (!cast.Valid) return locks;
            // The snapshot's sprint lock may come from its own cast state; replace that part with the prediction.
            bool castLocked = context != null && (context.CastChanneling || context.CastSuppressesSprint);
            if (castLocked) locks &= ~MotorLocks.Sprint;
            if (cast.SprintLock) locks |= MotorLocks.Sprint;
            return locks;
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
