using System;
using Confluence.Core.Time;

namespace Confluence.Hosting.Bots
{
    /// <summary>The four bot competence settings (spec L381; GlobalRules.Roster.BotCompetenceLevels).</summary>
    public enum BotCompetence
    {
        /// <summary>Telegraphs decisions and uses normal casts.</summary>
        Novice,

        /// <summary>Moves between cover and keeps moving while fighting.</summary>
        Apprentice,

        /// <summary>Manages resources and guard.</summary>
        Adept,

        /// <summary>Chooses among bounded plans from what it actually sees; not perfect.</summary>
        Expert,
    }

    /// <summary>
    /// Tunable behaviour of one bot. Every number is bounded: reactions never beat <see cref="ReactionFloorSec"/>
    /// ("reaction time has an explicit humanlike floor", L381), aim error is never zero, and guards and dodges are
    /// chances, not certainties ("does not execute perfect gestures or parries every time"). The defaults per
    /// competence come from <see cref="For"/>; tests and Training may adjust a copy.
    /// </summary>
    public sealed class BotProfile
    {
        /// <summary>The humanlike reaction floor every competence respects (L381).</summary>
        public const float ReactionFloorSec = 0.20f;

        /// <summary>Smallest aim error any bot keeps, in degrees (no perfect aim, L381).</summary>
        public const float MinAimErrorDeg = 0.5f;

        public BotCompetence Competence { get; set; }

        /// <summary>Reaction to a newly perceived event, drawn uniformly per event in [min, max] seconds.</summary>
        public float ReactionMinSec { get; set; }
        public float ReactionMaxSec { get; set; }

        /// <summary>Largest random aim error at rest, in degrees; target motion adds to it (L381 "accuracy depends on movement").</summary>
        public float AimErrorDeg { get; set; }

        /// <summary>Extra aim error per m/s of the target's lateral speed, in degrees.</summary>
        public float AimErrorPerSpeedDeg { get; set; }

        /// <summary>Largest view turn per tick, in degrees.</summary>
        public float TurnRateDegPerTick { get; set; }

        /// <summary>Aims ahead of a moving target with a nominal projectile speed (L381 "projectile travel").</summary>
        public bool LeadsTargets { get; set; }

        /// <summary>Distance the bot tries to fight at, in meters.</summary>
        public float PreferredRange { get; set; }

        /// <summary>Chance of guarding a visible incoming attack once the reaction time has passed.</summary>
        public float GuardChance { get; set; }

        /// <summary>Chance of dodging a visible incoming attack it does not guard.</summary>
        public float DodgeChance { get; set; }

        /// <summary>Chance per decision of an occasional hop while strafing.</summary>
        public float JumpChance { get; set; }

        /// <summary>Chance per decision of pressing in for common melee (rises late in the duel).</summary>
        public float AggressionChance { get; set; }

        /// <summary>Stamina kept back for a guard (25, L104) when the bot manages resources.</summary>
        public float StaminaReserve { get; set; }

        /// <summary>Strafes and changes direction while fighting instead of standing.</summary>
        public bool StrafesInCombat { get; set; }

        /// <summary>Stops and faces the target this long before a cast (Novice "telegraphs decisions").</summary>
        public float TelegraphSec { get; set; }

        /// <summary>Tries the wizard's mastery timing (crest, band) instead of a plain normal cast.</summary>
        public bool AttemptsMastery { get; set; }

        /// <summary>Random error added to scripted cast timings, in ticks (both directions).</summary>
        public int CastTimingJitterTicks { get; set; }

        /// <summary>Seconds between decisions (target choice, aggression rolls, path replans).</summary>
        public float ThinkIntervalSec { get; set; }

        /// <summary>Seconds a bot waits in the shop or draft before acting, and between economy actions.</summary>
        public float EconomyThinkSec { get; set; }

        public int ReactionMinTicks => Math.Max(Ticks.FromSeconds(ReactionFloorSec), Ticks.FromSeconds(Math.Max(ReactionFloorSec, ReactionMinSec)));
        public int ReactionMaxTicks => Math.Max(ReactionMinTicks, Ticks.FromSeconds(ReactionMaxSec));
        public int TelegraphTicks => Ticks.FromSeconds(Math.Max(0f, TelegraphSec));
        public int ThinkTicks => Math.Max(1, Ticks.FromSeconds(ThinkIntervalSec));
        public int EconomyThinkTicks => Math.Max(1, Ticks.FromSeconds(EconomyThinkSec));
        public float EffectiveAimErrorDeg => Math.Max(MinAimErrorDeg, AimErrorDeg);

        public BotProfile Clone() => (BotProfile)MemberwiseClone();

        /// <summary>The default profile of a competence setting (L381).</summary>
        public static BotProfile For(BotCompetence competence)
        {
            switch (competence)
            {
                case BotCompetence.Novice:
                    return new BotProfile
                    {
                        Competence = competence, ReactionMinSec = 0.45f, ReactionMaxSec = 0.70f, AimErrorDeg = 9f, AimErrorPerSpeedDeg = 1.5f,
                        TurnRateDegPerTick = 4f, LeadsTargets = false, PreferredRange = 7f, GuardChance = 0.15f, DodgeChance = 0f,
                        JumpChance = 0f, AggressionChance = 0.10f, StaminaReserve = 0f, StrafesInCombat = false, TelegraphSec = 0.5f,
                        AttemptsMastery = false, CastTimingJitterTicks = 6, ThinkIntervalSec = 0.8f, EconomyThinkSec = 2.0f,
                    };
                case BotCompetence.Apprentice:
                    return new BotProfile
                    {
                        Competence = competence, ReactionMinSec = 0.35f, ReactionMaxSec = 0.50f, AimErrorDeg = 6f, AimErrorPerSpeedDeg = 1.0f,
                        TurnRateDegPerTick = 6f, LeadsTargets = false, PreferredRange = 7f, GuardChance = 0.35f, DodgeChance = 0.1f,
                        JumpChance = 0.05f, AggressionChance = 0.15f, StaminaReserve = 0f, StrafesInCombat = true, TelegraphSec = 0.2f,
                        AttemptsMastery = false, CastTimingJitterTicks = 4, ThinkIntervalSec = 0.6f, EconomyThinkSec = 1.5f,
                    };
                case BotCompetence.Adept:
                    return new BotProfile
                    {
                        Competence = competence, ReactionMinSec = 0.27f, ReactionMaxSec = 0.38f, AimErrorDeg = 3.5f, AimErrorPerSpeedDeg = 0.6f,
                        TurnRateDegPerTick = 9f, LeadsTargets = true, PreferredRange = 6f, GuardChance = 0.6f, DodgeChance = 0.25f,
                        JumpChance = 0.08f, AggressionChance = 0.2f, StaminaReserve = 25f, StrafesInCombat = true, TelegraphSec = 0f,
                        AttemptsMastery = true, CastTimingJitterTicks = 3, ThinkIntervalSec = 0.5f, EconomyThinkSec = 1.0f,
                    };
                case BotCompetence.Expert:
                    return new BotProfile
                    {
                        Competence = competence, ReactionMinSec = 0.20f, ReactionMaxSec = 0.28f, AimErrorDeg = 1.8f, AimErrorPerSpeedDeg = 0.35f,
                        TurnRateDegPerTick = 12f, LeadsTargets = true, PreferredRange = 6f, GuardChance = 0.75f, DodgeChance = 0.35f,
                        JumpChance = 0.1f, AggressionChance = 0.25f, StaminaReserve = 25f, StrafesInCombat = true, TelegraphSec = 0f,
                        AttemptsMastery = true, CastTimingJitterTicks = 2, ThinkIntervalSec = 0.4f, EconomyThinkSec = 0.75f,
                    };
                default:
                    throw new ArgumentOutOfRangeException(nameof(competence), competence, "Unknown bot competence.");
            }
        }
    }
}
