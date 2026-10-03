using Confluence.Core.Rules;
using Confluence.Core.Time;

namespace Confluence.Sim.Movement
{
    /// <summary>
    /// Per-player locomotion parameters after equipment and augment modifiers (spec L80-L82, L124). The world fills it
    /// from the player's stat block (<see cref="Stats.StatBlock.Motor"/>); client prediction fills it the same way.
    /// An unspecified tuning (<see cref="Specified"/> false, the default) means the standard rule values.
    /// </summary>
    public struct MotorTuning
    {
        public bool Specified;

        /// <summary>Unmodified gait speeds (m/s): walk 5.5, sprint 7, crouch 2.75 (spec L80, D-024).</summary>
        public float WalkSpeed;
        public float SprintSpeed;
        public float CrouchSpeed;

        /// <summary>Capped movement-bonus multipliers: walking and crouching, and sprinting (at most 1.2, spec L124).</summary>
        public float WalkMultiplier;
        public float SprintMultiplier;

        public float SprintStaminaPerSecond;
        public float JumpStaminaCost;
        public float DodgeStaminaCost;
        public float DodgeDistance;
        public int DodgeCooldownTicks;

        /// <summary>The rule values of spec L80-L82.</summary>
        public static MotorTuning Standard => new MotorTuning
        {
            Specified = true,
            WalkSpeed = GlobalRules.Movement.WalkSpeed,
            SprintSpeed = GlobalRules.Movement.SprintSpeed,
            CrouchSpeed = GlobalRules.Movement.CrouchSpeed,
            WalkMultiplier = 1f,
            SprintMultiplier = 1f,
            SprintStaminaPerSecond = GlobalRules.Movement.SprintStaminaPerSecond,
            JumpStaminaCost = GlobalRules.Movement.JumpStaminaCost,
            DodgeStaminaCost = GlobalRules.Movement.DodgeStaminaCost,
            DodgeDistance = GlobalRules.Movement.DodgeDistance,
            DodgeCooldownTicks = Ticks.FromSeconds(GlobalRules.Movement.DodgeCooldownSec),
        };

        /// <summary>This tuning, or the standard one when unspecified.</summary>
        public MotorTuning OrStandard() => Specified ? this : Standard;
    }
}
