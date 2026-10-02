using Confluence.Core.Rules;
using Confluence.Core.Time;

namespace Confluence.Casting
{
    /// <summary>Recovery and movement class of a cast (spec L92).</summary>
    public enum CastWeight : byte
    {
        /// <summary>Quick projectiles and other quick casts: 0.20 s recovery, 85% move speed.</summary>
        Quick = 0,

        /// <summary>Heavy projectiles and long windups: 0.35 s recovery, 70% move speed.</summary>
        Heavy = 1,

        /// <summary>Control spells: 0.35 s recovery, 70% move speed.</summary>
        Control = 2,

        /// <summary>Major fields and summons: 0.45 s recovery, 70% move speed.</summary>
        MajorFieldOrSummon = 3,
    }

    public static class CastWeights
    {
        /// <summary>Default recovery for the class, used unless a record states a longer one (spec L92).</summary>
        public static int DefaultRecoveryTicks(CastWeight weight)
        {
            switch (weight)
            {
                case CastWeight.Quick: return Ticks.FromSeconds(GlobalRules.Casting.RecoveryQuickProjectileSec);
                case CastWeight.Heavy: return Ticks.FromSeconds(GlobalRules.Casting.RecoveryHeavyProjectileSec);
                case CastWeight.Control: return Ticks.FromSeconds(GlobalRules.Casting.RecoveryControlSec);
                default: return Ticks.FromSeconds(GlobalRules.Casting.RecoveryMajorFieldOrSummonSec);
            }
        }

        /// <summary>Move-speed factor while casting (spec L92): 0.85 quick, 0.70 heavy/control/field.</summary>
        public static float MoveSpeedFactor(CastWeight weight) =>
            weight == CastWeight.Quick ? GlobalRules.Casting.MoveSpeedFactorQuickCast : GlobalRules.Casting.MoveSpeedFactorHeavyCast;
    }
}
