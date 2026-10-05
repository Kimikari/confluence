using System;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Sim.Players;

namespace Confluence.Sim.Combat
{
    /// <summary>Per-channel result of one hit.</summary>
    public struct ChannelDamage
    {
        public DamageChannel Channel;
        public float Raw;

        /// <summary>After precision, guard, resistance, and passive reductions (before shields).</summary>
        public float Mitigated;
    }

    /// <summary>Every stage of one resolved hit, for events, the death explanation, and tests (spec L112, L359).</summary>
    public sealed class DamageResolution
    {
        public float Raw;
        public float AfterPrecision;
        public float AfterGuard;
        public float AfterResistance;

        /// <summary>After all passive reductions (resistance included, 45% cap): the amount shields and health receive.</summary>
        public float Mitigated;

        public float ShieldAbsorbed;
        public float HealthLost;

        /// <summary>
        /// Damage the hit's conditional passive reduction (<see cref="DamageRequest.ExtraPassiveReduction"/>, e.g. SPL-011's
        /// projectile reduction) prevented on its own: the mitigated amount without it minus the amount with it.
        /// </summary>
        public float ConditionalPrevented;
        public GuardOutcome Guard;
        public float GuardReduction;

        /// <summary>Associated knockback after the guard's same-fraction reduction (spec L104).</summary>
        public float Knockback;

        public ChannelDamage[] Channels;

        /// <summary>The target's health reached zero or below with this hit.</summary>
        public bool Lethal;

        /// <summary>Total damage dealt (shield absorption plus health loss).</summary>
        public float Dealt => ShieldAbsorbed + HealthLost;
    }

    /// <summary>
    /// The damage pipeline in the exact spec order (L112): valid hit and guard angle; explicit precision multiplier
    /// (precision-tagged only, at most 1.25, no second critical, L110); guard (50%, perfect 80%; weapon and projectile
    /// direct damage inside the frontal arc, L104); channel resistance (cap 35%, floor -15%); other passive
    /// reductions (combined with resistance multiplicatively, total cap 45%; guard is exempt from that cap, L106);
    /// shields (earliest-expiring first, L114); remaining health loss. A split attack resolves per channel and rejoins
    /// as one hit. Boundary damage and the recall penalty bypass guard, resistance, passives, and shields (L51, L175).
    /// </summary>
    public static class DamagePipeline
    {
        public static DamageResolution Resolve(DamageRequest request, DuelPlayer target, int now)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            DamagePart[] parts = request.Parts ?? new DamagePart[0];
            var result = new DamageResolution { Channels = new ChannelDamage[parts.Length] };
            bool bypass = request.BypassesMitigation;

            // 1. Valid hit and guard angle.
            result.Guard = bypass ? GuardOutcome.None : GuardOutcomeFor(request, target, now);
            result.GuardReduction = result.Guard == GuardOutcome.PerfectGuard ? GlobalRules.Guard.PerfectDamageReduction
                : result.Guard == GuardOutcome.Guarded ? GlobalRules.Guard.DamageReduction : 0f;

            // 2. Precision multiplier: only precision-tagged spells, at most 1.25, a single bonus (spec L110).
            float precision = request.PrecisionTagged && !bypass
                ? CMath.Clamp(request.PrecisionMultiplier, 1f, GlobalRules.Damage.PrecisionMultiplierMax)
                : 1f;

            for (int i = 0; i < parts.Length; i++)
            {
                DamageChannel channel = parts[i].Channel;
                float raw = Math.Max(0f, parts[i].Amount);
                float afterPrecision = raw * precision;
                float afterGuard = afterPrecision * (1f - result.GuardReduction);
                float afterResistance = bypass ? afterGuard : afterGuard * (1f - target.Stats.Resistance(channel));
                float mitigated = bypass ? afterGuard : afterGuard * PassiveMultiplier(target, channel, request.ExtraPassiveReduction);

                result.Raw += raw;
                result.AfterPrecision += afterPrecision;
                result.AfterGuard += afterGuard;
                result.AfterResistance += afterResistance;
                result.Mitigated += mitigated;
                if (!bypass && request.ExtraPassiveReduction > 0f)
                {
                    result.ConditionalPrevented += Math.Max(0f, afterGuard * PassiveMultiplier(target, channel, 0f) - mitigated);
                }
                result.Channels[i] = new ChannelDamage { Channel = channel, Raw = raw, Mitigated = mitigated };
            }

            result.Knockback = Math.Max(0f, request.Knockback) * (1f - result.GuardReduction);

            // 6. Shields, then 7. health.
            float remaining = result.Mitigated;
            if (!bypass && remaining > 0f)
            {
                var contact = new ShieldContact
                {
                    HasSource = request.HasSourcePosition,
                    Source = request.SourcePosition,
                    Defender = target.Motor.Position,
                    DefenderYawDeg = target.Motor.YawDeg,
                };
                result.ShieldAbsorbed = target.Shields.Absorb(remaining, contact);
                remaining -= result.ShieldAbsorbed;
            }

            result.HealthLost = Math.Max(0f, remaining);
            bool wasAlive = target.Resources.Alive;
            target.Resources.LoseHealth(result.HealthLost);
            result.Lethal = wasAlive && !target.Resources.Alive;
            return result;
        }

        /// <summary>
        /// Resistance and passive reductions with the hit's conditional reduction (for example a buff against
        /// projectiles): multiplicative, total reduction at most 45% (spec L112).
        /// </summary>
        private static float PassiveMultiplier(DuelPlayer target, DamageChannel channel, float extraReduction)
        {
            float m = target.Stats.PassiveMultiplier(channel);
            if (!(extraReduction > 0f))
            {
                return m;
            }

            return Math.Max(m * (1f - Math.Min(extraReduction, 1f)), 1f - GlobalRules.Damage.PassiveReductionCap);
        }

        /// <summary>
        /// Guard applies to guard-eligible damage (weapon and physical projectile direct hits) from a source inside the
        /// defender's frontal arc while the guard is held (spec L104). Rear and flank attacks, ground fields, and
        /// boundary damage are never guarded.
        /// </summary>
        public static GuardOutcome GuardOutcomeFor(DamageRequest request, DuelPlayer target, int now)
        {
            if (!request.GuardEligible || !request.HasSourcePosition || !target.Guard.Active)
            {
                return GuardOutcome.None;
            }

            if (!GuardState.InArc(target.Motor.Position, target.Motor.YawDeg, request.SourcePosition))
            {
                return GuardOutcome.None;
            }

            return target.Guard.IsPerfect(now) ? GuardOutcome.PerfectGuard : GuardOutcome.Guarded;
        }
    }
}
