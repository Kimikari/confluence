using System;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Sim.Hashing;
using Confluence.Sim.World;

namespace Confluence.Sim.Movement
{
    /// <summary>Kind of scripted displacement currently moving a character.</summary>
    public enum DisplacementKind
    {
        None,

        /// <summary>Hostile knockback that prevents deliberate movement: hard control (spec L116, L173).</summary>
        Knockback,

        /// <summary>Self dash from a movement effect (at most 8 m, spec L124).</summary>
        Dash,
    }

    /// <summary>Movement actions the world currently forbids (statuses, actions, phases).</summary>
    [Flags]
    public enum MotorLocks
    {
        None = 0,
        Move = 1 << 0,
        Jump = 1 << 1,
        Dodge = 1 << 2,
        Sprint = 1 << 3,
        Crouch = 1 << 4,
        All = Move | Jump | Dodge | Sprint | Crouch,
    }

    /// <summary>
    /// Complete locomotion state of one character. A plain value type, so client prediction can copy, replay,
    /// and compare it against authoritative snapshots.
    /// </summary>
    public struct MotorState
    {
        /// <summary>Feet position (bottom of the capsule).</summary>
        public Vec3 Position;

        public Vec3 Velocity;
        public float YawDeg;
        public float PitchDeg;

        public bool Grounded;
        public Vec3 GroundNormal;

        /// <summary>Solid currently stood on, or -1.</summary>
        public int GroundSolidId;

        public bool Crouched;
        public bool Sprinting;

        /// <summary>Highest support height since leaving the ground; landing compares against it (spec L175).</summary>
        public float AirPeakY;

        /// <summary>
        /// Height of the ground contact point while grounded. A capsule rolling off a ledge lowers its feet before it
        /// leaves the corner, so fall height is measured between support points rather than feet heights.
        /// </summary>
        public float SupportY;

        public int DodgeTicks;
        public Vec3 DodgeVelocity;
        public int DodgeCooldownTicks;

        public int DisplaceTicks;
        public Vec3 DisplaceVelocity;
        public DisplacementKind DisplaceKind;

        /// <summary>Last position where the character stood on walkable ground.</summary>
        public Vec3 LastGroundedPosition;

        public static MotorState At(Vec3 feet, float yawDeg)
        {
            return new MotorState
            {
                Position = feet,
                YawDeg = yawDeg,
                GroundNormal = Vec3.Up,
                GroundSolidId = -1,
                AirPeakY = feet.Y,
                SupportY = feet.Y,
                LastGroundedPosition = feet,
            };
        }

        public float Height => Crouched ? GlobalRules.Movement.CrouchHeight : GlobalRules.Movement.StandingHeight;

        public float EyeHeight => Crouched ? GlobalRules.Movement.CrouchEyeHeight : GlobalRules.Movement.StandingEyeHeight;

        public Vec3 Eye => Position + new Vec3(0f, EyeHeight, 0f);

        public Vec3 Forward => CMath.ForwardFromYaw(YawDeg);

        public Vec3 AimDirection => CMath.ForwardFromYawPitch(YawDeg, PitchDeg);

        /// <summary>Character collision and hit volume.</summary>
        public Capsule Capsule => Capsule.Vertical(Position, Height, GlobalRules.Movement.CapsuleRadius);

        public bool IsDodging => DodgeTicks > 0;

        public bool IsDisplaced => DisplaceTicks > 0;

        public void Hash(ref StateHasher h)
        {
            h.Add(Position);
            h.Add(Velocity);
            h.Add(YawDeg);
            h.Add(PitchDeg);
            h.Add(Grounded);
            h.Add(GroundNormal);
            h.Add(GroundSolidId);
            h.Add(Crouched);
            h.Add(Sprinting);
            h.Add(AirPeakY);
            h.Add(SupportY);
            h.Add(DodgeTicks);
            h.Add(DodgeVelocity);
            h.Add(DodgeCooldownTicks);
            h.Add(DisplaceTicks);
            h.Add(DisplaceVelocity);
            h.Add((int)DisplaceKind);
            h.Add(LastGroundedPosition);
        }
    }

    /// <summary>
    /// Everything the motor needs for one step besides its state and the collision world. The world fills it
    /// from the player's input, resources, statuses, and actions; client prediction fills it the same way.
    /// </summary>
    public struct MotorCommand
    {
        /// <summary>Movement axes: X strafe right, Y forward. Magnitude is clamped to 1.</summary>
        public Vec2 Move;

        public float YawDeg;
        public float PitchDeg;
        public bool JumpPressed;
        public bool SprintHeld;
        public bool CrouchHeld;
        public bool DodgePressed;

        /// <summary>Stamina available this tick; actions need their full cost (spec L78).</summary>
        public float Stamina;

        public MotorLocks Locks;

        /// <summary>Capped movement bonus times the strongest slow (spec L116, L124). 1 = unmodified.</summary>
        public float SpeedMultiplier;

        /// <summary>Casting movement factor of walk speed: 1 none, 0.85 quick, 0.70 heavy/control/field, 0 stationary (spec L92).</summary>
        public float CastSpeedFactor;

        /// <summary>Ground surface under the feet at the start of the step (spec L175).</summary>
        public SurfaceInfo Surface;

        /// <summary>Declared traction 0-1 that reduces ice drift (spec L175 "a declared traction item").</summary>
        public float Traction;

        /// <summary>Confines the feet to a box footprint (the alcove ward, spec L46).</summary>
        public bool Confined;

        public YawBox Confinement;

        /// <summary>Legal boundary for forced displacement and dashes (spec L51), or null.</summary>
        public IPositionConstraint DisplacementBounds;

        public static MotorCommand Default => new MotorCommand { SpeedMultiplier = 1f, CastSpeedFactor = 1f, Stamina = float.PositiveInfinity };

        public bool IsLocked(MotorLocks lockFlags) => (Locks & lockFlags) != 0;
    }

    /// <summary>What happened during one motor step; the world turns it into costs, damage, and events.</summary>
    public struct MotorReport
    {
        public float StaminaSpent;
        public bool Sprinted;
        public bool Jumped;
        public bool DodgeStarted;
        public bool Landed;

        /// <summary>Peak-to-landing drop height of a landing this step.</summary>
        public float LandingDrop;

        public bool FellOutOfMap;

        /// <summary>A hostile knockback was stopped at the boundary edge (5 impact damage, spec L51).</summary>
        public bool StoppedAtEdge;

        public bool DisplacementEnded;
        public DisplacementKind EndedDisplacement;

        /// <summary>Horizontal distance of self-propelled movement (walk, sprint, jump, dodge, dash) this step.</summary>
        public float SelfMovedDistance;
    }
}
