using System;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.World;

namespace Confluence.Sim.Movement
{
    /// <summary>
    /// Deterministic kinematic character motor (DECISIONS D-003, D-024). A pure step function: the result depends
    /// only on the state, the command, the collision world, and dt, so the server and client prediction produce
    /// identical motion. One call advances one authoritative tick (dt = <see cref="Ticks.Dt"/>); its timers count
    /// steps.
    /// Rules: walk 5.5 / sprint 7 m/s with 12 stamina/s, jump 10 stamina, moderate air steering that cannot reverse
    /// momentum immediately, no bunny-hop or slide stacking (spec L80); dodge 25 stamina, 3 m over 0.22 s, 1.6 s
    /// cooldown, no invulnerability (L82); casting movement factors (L92); slopes up to 30 degrees walkable and
    /// standardized step-up (L312); slippery ice drift without control loss (L175).
    /// </summary>
    public static class CharacterMotor
    {
        private static readonly float WalkableMinNormalY = MathF.Cos(SimRules.Motor.MaxWalkableSlopeDeg * CMath.Deg2Rad);
        private const float CeilingMaxNormalY = -0.1f;
        private const float StepProgressEpsilon = 1e-4f;

        public static float Radius => GlobalRules.Movement.CapsuleRadius;

        /// <summary>A contact normal that counts as standable ground (slope at most 30 degrees, spec L312).</summary>
        public static bool IsWalkable(Vec3 normal) => normal.Y >= WalkableMinNormalY;

        public static int DodgeTicks => Ticks.FromSeconds(GlobalRules.Movement.DodgeDurationSec);

        public static int DodgeCooldownTicks => Ticks.FromSeconds(GlobalRules.Movement.DodgeCooldownSec);

        /// <summary>Advances the character by one tick.</summary>
        public static void Step(ref MotorState s, in MotorCommand c, CollisionWorld world, float dt, out MotorReport report)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (!(dt > 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(dt));
            }

            report = default;
            Vec3 start = s.Position;
            bool wasGrounded = s.Grounded;
            float supportBefore = s.SupportY;
            float stamina = c.Stamina;
            float spent = 0f;

            s.YawDeg = CMath.WrapAngle(Finite(c.YawDeg));
            s.PitchDeg = CMath.Clamp(Finite(c.PitchDeg), -89f, 89f);
            if (s.DodgeCooldownTicks > 0)
            {
                s.DodgeCooldownTicks--;
            }

            UpdateCrouch(ref s, c, world);

            bool displaced = s.DisplaceTicks > 0;
            bool moveLocked = c.IsLocked(MotorLocks.Move) || displaced;
            Vec2 move = ClampMove(c.Move);
            float moveMag = move.Length();
            Vec3 forward = CMath.ForwardFromYaw(s.YawDeg);
            Vec3 right = CMath.RightFromYaw(s.YawDeg);
            Vec3 wishDir = moveMag > 0f ? (right * move.X + forward * move.Y) / moveMag : Vec3.Zero;

            // Sprint (spec L80): forward movement, stamina for this tick, not crouched, dodging, or locked.
            bool sprint = c.SprintHeld && !moveLocked && !c.IsLocked(MotorLocks.Sprint) && !s.Crouched
                && s.DodgeTicks == 0 && move.Y > SimRules.Motor.SprintMinForwardInput;
            float sprintCost = GlobalRules.Movement.SprintStaminaPerSecond * dt;
            if (sprint && stamina - spent >= sprintCost)
            {
                spent += sprintCost;
                report.Sprinted = true;
            }
            else
            {
                sprint = false;
            }

            s.Sprinting = sprint;
            float maxSpeed = MaxSpeed(s.Crouched, sprint, c);
            Vec3 wishVel = moveLocked ? Vec3.Zero : wishDir * (moveMag * maxSpeed);

            // Dodge (spec L82): grounded, off cooldown, full stamina cost; travels its distance over its duration.
            if (c.DodgePressed && s.Grounded && s.DodgeTicks == 0 && s.DodgeCooldownTicks == 0 && !moveLocked
                && !c.IsLocked(MotorLocks.Dodge) && stamina - spent >= GlobalRules.Movement.DodgeStaminaCost)
            {
                Vec3 dir = wishDir.LengthSquared() > 0f ? wishDir : -forward;
                int ticks = DodgeTicks;
                s.DodgeTicks = ticks;
                s.DodgeVelocity = dir * (GlobalRules.Movement.DodgeDistance / (ticks * dt));
                s.DodgeCooldownTicks = DodgeCooldownTicks;
                spent += GlobalRules.Movement.DodgeStaminaCost;
                report.DodgeStarted = true;
                s.Sprinting = false;
            }

            // Horizontal velocity.
            Vec3 hv = new Vec3(s.Velocity.X, 0f, s.Velocity.Z);
            if (displaced)
            {
                hv = new Vec3(s.DisplaceVelocity.X, 0f, s.DisplaceVelocity.Z);
            }
            else if (s.DodgeTicks > 0)
            {
                hv = s.DodgeVelocity;
            }
            else if (s.Grounded)
            {
                float accel = GlobalRules.Movement.GroundAcceleration * TractionFactor(c);
                hv = Vec3.MoveTowards(hv, wishVel, accel * dt);
            }
            else
            {
                // Moderate air steering (D-024): toward the wished velocity at 8 m/s^2. The target never exceeds the
                // ground maximum, so air control cannot add speed (no bunny-hop acceleration).
                hv = Vec3.MoveTowards(hv, wishVel, GlobalRules.Movement.AirAcceleration * dt);
            }

            // Jump (spec L80): one universal jump from the ground, 10 stamina, fresh press.
            float vy = s.Velocity.Y;
            bool jumped = false;
            if (c.JumpPressed && s.Grounded && s.DodgeTicks == 0 && !moveLocked && !c.IsLocked(MotorLocks.Jump)
                && stamina - spent >= GlobalRules.Movement.JumpStaminaCost)
            {
                vy = GlobalRules.Movement.JumpLaunchSpeed;
                spent += GlobalRules.Movement.JumpStaminaCost;
                jumped = true;
                report.Jumped = true;
                s.Grounded = false;
                s.AirPeakY = s.Position.Y;
            }

            float verticalStep = 0f;
            if (s.Grounded)
            {
                vy = 0f;
            }
            else
            {
                // Average of the start and end velocity: exact under constant gravity, so the common jump reaches its
                // 1.1 m apex and 0.70 s airtime (D-024) at any tick rate.
                float vyEnd = Math.Max(vy - GlobalRules.Movement.Gravity * dt, -SimRules.Motor.MaxFallSpeed);
                verticalStep = 0.5f * (vy + vyEnd) * dt;
                vy = vyEnd;
            }

            s.Velocity = new Vec3(hv.X, vy, hv.Z);
            Vec3 delta = new Vec3(hv.X * dt, verticalStep, hv.Z * dt);
            if (s.Grounded && IsWalkable(s.GroundNormal))
            {
                // Follow the ground plane while keeping horizontal speed (ramps up to 30 degrees, spec L312).
                Vec3 n = s.GroundNormal;
                delta.Y = -(n.X * delta.X + n.Z * delta.Z) / n.Y;
            }

            MotorState beforeMove = s;
            bool blockedSteep = Slide(world, ref s, delta);
            if (blockedSteep && wasGrounded && !jumped)
            {
                TryStepUp(world, ref s, beforeMove, new Vec3(delta.X, 0f, delta.Z));
            }

            UpdateGround(world, ref s, wasGrounded && !jumped);

            if (!s.Grounded)
            {
                if (wasGrounded && !jumped)
                {
                    s.AirPeakY = Math.Max(Math.Max(s.Position.Y, start.Y), supportBefore);
                }

                s.AirPeakY = Math.Max(s.AirPeakY, s.Position.Y);
            }
            else if (!wasGrounded)
            {
                report.Landed = true;
                report.LandingDrop = Math.Max(0f, s.AirPeakY - s.SupportY);
            }

            AdvanceTimedMotion(ref s, maxSpeed, ref report);

            if ((displaced || s.DisplaceTicks > 0) && c.DisplacementBounds != null)
            {
                ClampDisplacementToBoundary(world, ref s, start, c.DisplacementBounds, ref report);
            }

            if (c.Confined)
            {
                Confine(ref s, c.Confinement);
            }

            if (s.Grounded)
            {
                s.LastGroundedPosition = s.Position;
            }

            report.FellOutOfMap = s.Position.Y < world.KillPlaneY;
            report.StaminaSpent = spent;
            bool forced = displaced && beforeMove.DisplaceKind == DisplacementKind.Knockback;
            report.SelfMovedDistance = forced ? 0f : Vec3.DistanceXZ(start, s.Position);
        }

        /// <summary>
        /// Maximum horizontal speed: walk, sprint, or crouch; limited by the casting factor of walk speed
        /// (spec L92), then scaled by the capped bonus and strongest slow (spec L116, L124).
        /// </summary>
        public static float MaxSpeed(bool crouched, bool sprinting, in MotorCommand c)
        {
            float speed = crouched ? GlobalRules.Movement.CrouchSpeed
                : sprinting ? GlobalRules.Movement.SprintSpeed : GlobalRules.Movement.WalkSpeed;
            float cast = CMath.Clamp(Finite(c.CastSpeedFactor, 1f), 0f, 1f);
            if (cast < 1f)
            {
                speed = Math.Min(speed, GlobalRules.Movement.WalkSpeed * cast);
            }

            return speed * Math.Max(0f, Finite(c.SpeedMultiplier, 1f));
        }

        /// <summary>Starts a displacement (knockback or dash) that overrides horizontal velocity for its ticks.</summary>
        public static void BeginDisplacement(ref MotorState s, DisplacementKind kind, Vec3 horizontalVelocity, int ticks)
        {
            if (ticks <= 0 || kind == DisplacementKind.None)
            {
                return;
            }

            s.DisplaceKind = kind;
            s.DisplaceTicks = ticks;
            s.DisplaceVelocity = new Vec3(horizontalVelocity.X, 0f, horizontalVelocity.Z);
            s.DodgeTicks = 0;
            s.Sprinting = false;
        }

        /// <summary>Ends any displacement immediately (recall, teleport).</summary>
        public static void CancelDisplacement(ref MotorState s)
        {
            s.DisplaceTicks = 0;
            s.DisplaceKind = DisplacementKind.None;
            s.DisplaceVelocity = Vec3.Zero;
        }

        // ----- internals -------------------------------------------------------------------------------------

        private static float TractionFactor(in MotorCommand c)
        {
            if (!c.Surface.Slippery)
            {
                return 1f;
            }

            return CMath.Lerp(SimRules.Motor.IceAccelerationFactor, 1f, CMath.Clamp01(c.Traction));
        }

        private static void UpdateCrouch(ref MotorState s, in MotorCommand c, CollisionWorld world)
        {
            bool want = c.CrouchHeld && !c.IsLocked(MotorLocks.Crouch);
            if (want)
            {
                s.Crouched = true;
                return;
            }

            if (s.Crouched)
            {
                Capsule standing = Capsule.Vertical(s.Position + new Vec3(0f, 0.01f, 0f), GlobalRules.Movement.StandingHeight, Radius);
                if (world.IsCapsuleClear(standing, 1e-3f))
                {
                    s.Crouched = false;
                }
            }
        }

        private static Vec2 ClampMove(Vec2 move)
        {
            move = new Vec2(Finite(move.X), Finite(move.Y));
            float len = move.Length();
            if (len < SimRules.Motor.MoveDeadzone)
            {
                return Vec2.Zero;
            }

            return len > 1f ? move / len : move;
        }

        // Moves by delta in substeps, resolving penetrations. Returns true if a steep (wall) contact blocked motion.
        private static bool Slide(CollisionWorld world, ref MotorState s, Vec3 delta)
        {
            float len = delta.Length();
            int steps = Math.Max(1, (int)MathF.Ceiling(len / SimRules.Motor.MaxSubstepDistance));
            Vec3 step = delta / steps;
            bool blocked = false;
            for (int i = 0; i < steps; i++)
            {
                s.Position += step;
                blocked |= Resolve(world, ref s, ref step);
            }

            return blocked;
        }

        // Pushes the capsule out of solids. Walkable contacts lift vertically (no sliding down slopes); ceilings push
        // down; walls and edges push horizontally (an edge never launches the character upward) and clip the velocity
        // and the remaining substep motion.
        private static bool Resolve(CollisionWorld world, ref MotorState s, ref Vec3 step)
        {
            bool blocked = false;
            for (int iter = 0; iter < SimRules.Motor.DepenetrationIterations; iter++)
            {
                if (!world.DeepestPenetration(s.Capsule, out Penetration pen, out _))
                {
                    break;
                }

                Vec3 n = pen.Normal;
                if (IsWalkable(n))
                {
                    s.Position.Y += pen.Depth / n.Y;
                    if (s.Velocity.Y < 0f)
                    {
                        s.Velocity.Y = 0f;
                    }

                    if (step.Y < 0f)
                    {
                        step.Y = 0f;
                    }

                    continue;
                }

                if (n.Y <= CeilingMaxNormalY)
                {
                    s.Position += n * pen.Depth;
                    if (s.Velocity.Y > 0f)
                    {
                        s.Velocity.Y = 0f;
                    }

                    if (step.Y > 0f)
                    {
                        step.Y = 0f;
                    }

                    continue;
                }

                blocked = true;
                float horizontal = MathF.Sqrt(n.X * n.X + n.Z * n.Z);
                n = new Vec3(n.X / horizontal, 0f, n.Z / horizontal);
                s.Position += n * (pen.Depth / horizontal);

                float vn = Vec3.Dot(s.Velocity, n);
                if (vn < 0f)
                {
                    s.Velocity -= n * vn;
                }

                float sn = Vec3.Dot(step, n);
                if (sn < 0f)
                {
                    step -= n * sn;
                }
            }

            return blocked;
        }

        // Standardized step-up (spec L312, D-024 step height 0.35 m): lift, move, and settle; keep it only if it
        // lands on walkable ground no higher than the step height and gets further than the blocked move.
        private static void TryStepUp(CollisionWorld world, ref MotorState s, MotorState before, Vec3 horizontal)
        {
            if (horizontal.LengthSquared() < 1e-10f)
            {
                return;
            }

            float stepHeight = GlobalRules.Movement.StepHeight;
            MotorState trial = before;
            Vec3 up = new Vec3(0f, stepHeight, 0f);
            Capsule lifted = Capsule.Vertical(trial.Position + up, trial.Height, Radius);
            if (!world.IsCapsuleClear(lifted, 1e-3f))
            {
                return;
            }

            trial.Position += up;
            Slide(world, ref trial, horizontal);
            if (!world.ProbeGround(trial.Position, Radius, 0f, stepHeight + SimRules.Motor.GroundContactTolerance,
                WalkableMinNormalY, out WorldHit hit, out _))
            {
                return;
            }

            // The step is measured at the touched surface: a sphere resting against a ledge corner sits lower than the
            // ledge top, which must not let a 0.5 m block pass as a 0.35 m step.
            float groundY = hit.Point.Y - Radius;
            if (hit.Contact.Y - before.Position.Y > stepHeight + 1e-3f)
            {
                return;
            }

            trial.Position.Y = groundY;
            float trialProgress = Vec3.DistanceXZ(before.Position, trial.Position);
            float slideProgress = Vec3.DistanceXZ(before.Position, s.Position);
            if (trialProgress <= slideProgress + StepProgressEpsilon)
            {
                return;
            }

            if (!world.IsCapsuleClear(Capsule.Vertical(trial.Position + new Vec3(0f, 0.005f, 0f), trial.Height, Radius), 2e-3f))
            {
                return;
            }

            trial.Grounded = true;
            trial.SupportY = hit.Contact.Y;
            trial.GroundNormal = hit.Normal;
            trial.GroundSolidId = hit.SolidId;
            s = trial;
        }

        // Ground detection and snapping: a walking character stays attached down stairs and ramps within the snap
        // distance; an airborne one lands when its feet reach walkable ground.
        private static void UpdateGround(CollisionWorld world, ref MotorState s, bool snap)
        {
            float distance = snap ? SimRules.Motor.GroundSnapDistance : SimRules.Motor.GroundContactTolerance;
            if (s.Velocity.Y > 0f)
            {
                s.Grounded = false;
                return;
            }

            if (world.ProbeGround(s.Position, Radius, SimRules.Motor.GroundProbeLift, distance, WalkableMinNormalY, out WorldHit hit, out _))
            {
                s.Position.Y = hit.Point.Y - Radius;
                s.Grounded = true;
                s.SupportY = hit.Contact.Y;
                s.GroundNormal = hit.Normal;
                s.GroundSolidId = hit.SolidId;
                s.Velocity.Y = 0f;
                return;
            }

            s.Grounded = false;
            s.GroundNormal = Vec3.Up;
            s.GroundSolidId = -1;
        }

        private static void AdvanceTimedMotion(ref MotorState s, float maxSpeed, ref MotorReport report)
        {
            if (s.DodgeTicks > 0)
            {
                s.DodgeTicks--;
                if (s.DodgeTicks == 0)
                {
                    // No slide speed stacking (spec L80): leave the dodge at no more than the current maximum speed.
                    s.Velocity = ClampHorizontal(s.Velocity, maxSpeed);
                    s.DodgeVelocity = Vec3.Zero;
                }
            }

            if (s.DisplaceTicks > 0)
            {
                s.DisplaceTicks--;
                if (s.DisplaceTicks == 0)
                {
                    EndDisplacement(ref s, maxSpeed, ref report);
                }
            }
        }

        private static void EndDisplacement(ref MotorState s, float maxSpeed, ref MotorReport report)
        {
            report.DisplacementEnded = true;
            report.EndedDisplacement = s.DisplaceKind;
            s.Velocity = s.DisplaceKind == DisplacementKind.Knockback
                ? new Vec3(0f, s.Velocity.Y, 0f)
                : ClampHorizontal(s.Velocity, maxSpeed);
            s.DisplaceKind = DisplacementKind.None;
            s.DisplaceTicks = 0;
            s.DisplaceVelocity = Vec3.Zero;
        }

        // Forced displacement and dashes stop at the legal boundary edge (spec L51) rather than leaving it.
        private static void ClampDisplacementToBoundary(CollisionWorld world, ref MotorState s, Vec3 start, IPositionConstraint bounds, ref MotorReport report)
        {
            if (bounds.Contains(s.Position))
            {
                return;
            }

            DisplacementKind kind = s.DisplaceKind != DisplacementKind.None ? s.DisplaceKind : report.EndedDisplacement;
            Vec3 end = s.Position;
            if (bounds.Contains(start))
            {
                float lo = 0f;
                float hi = 1f;
                for (int i = 0; i < SimRules.Motor.EdgeBisectionIterations; i++)
                {
                    float mid = 0.5f * (lo + hi);
                    if (bounds.Contains(Vec3.LerpUnclamped(start, end, mid)))
                    {
                        lo = mid;
                    }
                    else
                    {
                        hi = mid;
                    }
                }

                Vec3 edge = Vec3.LerpUnclamped(start, end, lo);
                s.Position = new Vec3(edge.X, end.Y, edge.Z);
            }
            else
            {
                // Already outside (a contracting boundary): never pushed further out.
                s.Position = new Vec3(start.X, end.Y, start.Z);
            }

            Vec3 none = Vec3.Zero;
            Resolve(world, ref s, ref none);
            if (s.DisplaceTicks > 0)
            {
                CancelDisplacement(ref s);
                report.DisplacementEnded = true;
                report.EndedDisplacement = kind;
            }

            s.Velocity = new Vec3(0f, s.Velocity.Y, 0f);
            report.StoppedAtEdge = kind == DisplacementKind.Knockback;
        }

        private static void Confine(ref MotorState s, YawBox box)
        {
            Vec3 local = box.ToLocal(s.Position);
            float ix = Math.Max(0f, box.HalfExtents.X - Radius);
            float iz = Math.Max(0f, box.HalfExtents.Z - Radius);
            float cx = CMath.Clamp(local.X, -ix, ix);
            float cz = CMath.Clamp(local.Z, -iz, iz);
            if (cx == local.X && cz == local.Z)
            {
                return;
            }

            Vec3 clamped = box.ToWorld(new Vec3(cx, local.Y, cz));
            s.Position = new Vec3(clamped.X, s.Position.Y, clamped.Z);
            Vec3 localVel = box.DirectionToLocal(s.Velocity);
            if (cx != local.X)
            {
                localVel.X = 0f;
            }

            if (cz != local.Z)
            {
                localVel.Z = 0f;
            }

            s.Velocity = box.DirectionToWorld(localVel);
        }

        private static Vec3 ClampHorizontal(Vec3 v, float maxSpeed)
        {
            float h = v.LengthXZ();
            if (h <= maxSpeed || h <= 0f)
            {
                return v;
            }

            float k = maxSpeed / h;
            return new Vec3(v.X * k, v.Y, v.Z * k);
        }

        private static float Finite(float value, float fallback = 0f) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
    }
}
