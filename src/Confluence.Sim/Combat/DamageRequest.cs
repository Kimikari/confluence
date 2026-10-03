using System;
using Confluence.Casting;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Sim.Combat
{
    /// <summary>How damage was delivered; decides guard, mitigation, and trigger eligibility (spec L51, L102-L126, L672).</summary>
    public enum DamageKind
    {
        /// <summary>A spell's direct impact (projectile, ray, beam, contact).</summary>
        Direct,

        /// <summary>Area damage around an impact; part of the original cast but not a direct hit.</summary>
        Splash,

        /// <summary>Secondary/proc damage: never triggers on-hit, on-kill, resource return, or another proc (spec L126).</summary>
        Secondary,

        /// <summary>Damage-over-time pulses of a named status (burn, venom).</summary>
        Status,

        /// <summary>Environment or reaction damage (shards, charged water, burning growth): no direct-hit rewards (spec L672).</summary>
        Environment,

        /// <summary>Boundary damage and edge impacts: bypass all mitigation and trigger nothing (spec L51).</summary>
        Boundary,

        /// <summary>Common melee: not a spell, may trigger eligible direct-hit effects (spec L102).</summary>
        Melee,

        /// <summary>A moving prop's collision (spec L173): never spell-hit lifesteal or cascading item damage.</summary>
        Prop,

        /// <summary>Physical fall damage above a 3 m drop (spec L175).</summary>
        Fall,

        /// <summary>The 40 health outside-map recall penalty (spec L175): bypasses mitigation, cannot activate procs.</summary>
        Recall,
    }

    /// <summary>Reaction family of environment damage, for feat attribution (FEAT-020 conductivity).</summary>
    public enum ReactionTag
    {
        None,
        Conductivity,
        Steam,
        Ice,
        Shatter,
        Burn,
        Growth,
        Prop,
        Other,
    }

    /// <summary>One channel portion of an attack; a split attack resolves each part separately (spec L112).</summary>
    public struct DamagePart
    {
        public DamageChannel Channel;
        public float Amount;

        public DamagePart(DamageChannel channel, float amount)
        {
            Channel = channel;
            Amount = amount;
        }
    }

    /// <summary>Guard result for one hit (spec L104).</summary>
    public enum GuardOutcome
    {
        None,
        Guarded,
        PerfectGuard,
    }

    /// <summary>
    /// A request to damage a player. Built by melee, spells, reactions, and rules; resolved by
    /// <see cref="DamagePipeline"/> in the spec L112 order.
    /// </summary>
    public sealed class DamageRequest
    {
        /// <summary>Attacking player index, or -1 for the world (boundary, falls, unattributed environment).</summary>
        public int Attacker = -1;

        public int Target;
        public DamageKind Kind;

        /// <summary>Catalogue ID (SPL-, EQP-, AUG-) or a rule source id from <see cref="DamageSources"/>.</summary>
        public string SourceId;

        /// <summary>Channel portions; most attacks have one.</summary>
        public DamagePart[] Parts;

        /// <summary>Weapon or physical projectile direct damage that a frontal guard can reduce (spec L104).</summary>
        public bool GuardEligible;

        /// <summary>The spell is precision-tagged and struck its stated weak point (spec L110).</summary>
        public bool PrecisionTagged;

        /// <summary>The stated weak-point multiplier; clamped to 1.25 and ignored when not precision-tagged.</summary>
        public float PrecisionMultiplier = 1f;

        /// <summary>Where the attack came from, for the guard arc. Without it no guard applies.</summary>
        public Vec3 SourcePosition;

        public bool HasSourcePosition;

        /// <summary>Associated direct knockback distance (reduced by guard by the same fraction, spec L104).</summary>
        public float Knockback;

        /// <summary>Ground-plane direction of the knockback (defaults to away from the source).</summary>
        public Vec3 KnockbackDirection;

        /// <summary>Original target-hit or cast event this damage belongs to (beam ticks share one). 0 = this hit.</summary>
        public int OriginEventId;

        /// <summary>0 for original damage, 1 for secondary effects; deeper generations are refused (spec L126, L508).</summary>
        public int Generation;

        public ReactionTag Reaction;

        /// <summary>Distance from attacker to target at release, for range feats (FEAT-009).</summary>
        public float Range;

        // ----- spell runtime additions ------------------------------------------------------------------------

        /// <summary>The committed cast this damage belongs to; recasts, splits, and delayed portions share it (spec L94, L508).</summary>
        public CastEventId CastId;

        /// <summary>
        /// A conditional passive reduction the target holds against this particular hit (for example SPL-011 "reduces
        /// incoming projectile damage 12%"). It combines multiplicatively with resistance and the other passives and
        /// stays inside the 45% total cap (spec L112).
        /// </summary>
        public float ExtraPassiveReduction;

        public float TotalAmount
        {
            get
            {
                float sum = 0f;
                if (Parts != null)
                {
                    for (int i = 0; i < Parts.Length; i++)
                    {
                        sum += Math.Max(0f, Parts[i].Amount);
                    }
                }

                return sum;
            }
        }

        /// <summary>Spell, melee, and splash damage of the original generation can trigger on-hit effects.</summary>
        public bool CanTriggerEffects => Generation == 0 && (Kind == DamageKind.Direct || Kind == DamageKind.Splash || Kind == DamageKind.Melee);

        /// <summary>Direct-hit effects (lifesteal, direct-hit procs): spell direct impacts and common melee (spec L102, L114).</summary>
        public bool IsDirectHit => Generation == 0 && (Kind == DamageKind.Direct || Kind == DamageKind.Melee);

        /// <summary>A hit by an exclusive spell (melee is not a spell, spec L102).</summary>
        public bool IsSpellHit => Generation == 0 && (Kind == DamageKind.Direct || Kind == DamageKind.Splash)
            && SourceId != null && SourceId.StartsWith("SPL-", StringComparison.Ordinal);

        /// <summary>Boundary rules and the recall penalty bypass guard, resistance, passives, and shields (spec L51, L175).</summary>
        public bool BypassesMitigation => Kind == DamageKind.Boundary || Kind == DamageKind.Recall;

        public static DamageRequest Single(int attacker, int target, DamageKind kind, string sourceId, DamageChannel channel, float amount)
        {
            return new DamageRequest
            {
                Attacker = attacker,
                Target = target,
                Kind = kind,
                SourceId = sourceId,
                Parts = new[] { new DamagePart(channel, amount) },
                GuardEligible = DefaultGuardEligible(kind),
            };
        }

        public static DamageRequest Split(int attacker, int target, DamageKind kind, string sourceId, params DamagePart[] parts)
        {
            return new DamageRequest
            {
                Attacker = attacker,
                Target = target,
                Kind = kind,
                SourceId = sourceId,
                Parts = parts,
                GuardEligible = DefaultGuardEligible(kind),
            };
        }

        /// <summary>
        /// Default guard eligibility: weapon and projectile direct damage (spec L104). Splash, ground fields,
        /// environment, boundary, props, falls, and statuses are not guarded. A spell clears it for sound without
        /// a physical projectile.
        /// </summary>
        public static bool DefaultGuardEligible(DamageKind kind) => kind == DamageKind.Direct || kind == DamageKind.Melee;

        public DamageRequest From(Vec3 sourcePosition)
        {
            SourcePosition = sourcePosition;
            HasSourcePosition = true;
            return this;
        }
    }

    /// <summary>Rule source ids used where no catalogue record is the source.</summary>
    public static class DamageSources
    {
        public const string Boundary = "RULE:BOUNDARY";        // spec L51
        public const string BoundaryEdge = "RULE:BOUNDARY-EDGE"; // spec L51
        public const string Fall = "RULE:FALL";                // spec L175
        public const string Recall = "RULE:RECALL";            // spec L175
        public const string CommonMelee = "RULE:MELEE";        // spec L100, issued plain focus
    }
}
