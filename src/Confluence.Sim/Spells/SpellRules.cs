using Confluence.Core.Rules;
using Confluence.Core.Time;

namespace Confluence.Sim.Spells
{
    /// <summary>
    /// Spell-runtime numbers the spec leaves open (global caps stay in <see cref="GlobalRules"/>). A record that states
    /// its own value always wins; these are the defaults a delivery uses when its text is silent. Each cites the text
    /// it interprets; see docs/notes/sim-spells.md.
    /// </summary>
    public static class SpellRules
    {
        /// <summary>
        /// Collision radius of a projectile whose record states no footprint (spec L527 "footprint ... come from its
        /// effect text"). 0.15 m is a hand-sized bolt; narrow and wide records state their own.
        /// </summary>
        public const float DefaultProjectileRadius = 0.15f;

        /// <summary>Spells leave the caster's focus a little below the eye, like common melee (spec L102).</summary>
        public const float CastOriginDropBelowEye = 0.35f;

        /// <summary>Half width of a ray that states no width (spec L527 "beams and rays stop at declared solid geometry").</summary>
        public const float DefaultRayHalfWidth = 0.05f;

        /// <summary>A reflection keeps 70% of the damage and bounces once (spec L157, L527).</summary>
        public const float ReflectionRetainedFraction = GlobalRules.World.PrismRetainedDamageFraction;

        public const int MaxReflections = GlobalRules.World.PrismMaxReflections;

        /// <summary>Directions sampled across a spell melee arc's sweep (swept volume, spec L102 "matching their visible movement").</summary>
        public const int ArcSamples = 9;

        /// <summary>
        /// A ground wave (SPL-118 "moves 18 m/s along ground") is split into segments of at most this width; each
        /// segment stops separately at an obstacle (SPL-192 "solid obstacles stop segments").
        /// </summary>
        public const float WaveSegmentWidth = 0.5f;

        /// <summary>Height above the ground at which a wave segment tests obstacles (below the 0.35 m step it would climb).</summary>
        public const float WaveObstacleProbeHeight = 0.4f;

        /// <summary>A wave touches bodies whose feet are at most this high above its ground (SPL-118 "airborne targets above 1 m avoid it").</summary>
        public const float WaveDefaultMaxTargetHeight = 1f;

        /// <summary>A deployable thrown to "wait on the first surface" sticks where its sweep first touches a solid.</summary>
        public const float DeployableRadius = 0.25f;

        /// <summary>Displacement of a pull or tether reel runs at the knockback speed of the motor (D-097: 12 m/s, 4 m in 1/3 s).</summary>
        public const float PullSpeed = 12f;

        /// <summary>Default dash travel speed when a record gives no duration (8 m in 0.3 s; spec L124 caps distance only).</summary>
        public const float DefaultDashSpeed = 26f;

        /// <summary>Ticks between a summon's attacks when its record gives no rate.</summary>
        public static readonly int DefaultSummonAttackIntervalTicks = Ticks.FromSeconds(1.0);

        /// <summary>Heights sampled on a target for per-target cover (chest and head; spec L120, SPL-010 "check cover per target").</summary>
        public const float CoverChestFraction = 0.6f;
    }
}
