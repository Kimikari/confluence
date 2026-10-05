using System;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Casting;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Effects;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Stats;

namespace Confluence.Sim.Content.Augments.AugmentsA
{
    /// <summary>
    /// Shared helpers of the augments-a families (Casting mastery, Projectile and beam shaping, Fieldcraft and terrain,
    /// Melee and guard, Movement arts, World reactions). Numbers the records leave open are named here with the spec
    /// line they interpret.
    /// </summary>
    internal static class AugmentsAKit
    {
        /// <summary>
        /// "Walking": grounded, not sprinting, and moving at least this horizontal speed (m/s). The records contrast walking
        /// with sprinting and standing still; 1 m/s is well below every walking gait, including the 70% casting factor
        /// (3.85 m/s, spec L92) and a 30% slow (spec L116), and above residual drift.
        /// </summary>
        public const float WalkingMinSpeed = 1f;

        /// <summary>"below 90 health" and "below 75 health" are absolute health points (300 base, spec L78).</summary>
        public static bool HealthBelow(DuelPlayer p, float health) => p.Resources.Health < health;

        public static float ByRank(int rank, float rank1, float rank2) => rank >= 2 ? rank2 : rank1;

        public static int Ticks(float seconds) => Confluence.Core.Time.Ticks.FromSeconds(seconds);

        public static bool IsProfile(MeleeProfile profile, MeleeProfile kind) => string.Equals(profile.Name, kind.Name, StringComparison.Ordinal);

        /// <summary>
        /// A strike profile with changes, mirroring <see cref="StatBlock.EffectiveMelee"/>: damage, reach, and stamina never go
        /// negative; a longer windup lengthens the cycle by the same amount (the recovery is unchanged) and keeps one tick.
        /// </summary>
        public static MeleeProfile With(MeleeProfile p, float damage = 0f, float reach = 0f, float stamina = 0f, float windupSec = 0f)
        {
            float windup = Math.Max(Confluence.Core.Time.Ticks.Dt, p.Windup + windupSec);
            float cycle = p.Cycle + (windup - p.Windup);
            return new MeleeProfile(p.Name, Math.Max(0f, p.Damage + damage), Math.Max(0f, p.Reach + reach), cycle, Math.Max(0f, p.Stamina + stamina), windup);
        }

        public static float HorizontalSpeed(DuelPlayer p) => new Vec3(p.Motor.Velocity.X, 0f, p.Motor.Velocity.Z).Length();

        /// <summary>Walking now: grounded, not sprinting, and moving (see <see cref="WalkingMinSpeed"/>).</summary>
        public static bool IsWalking(DuelPlayer p) => p.Motor.Grounded && !p.Motor.Sprinting && !p.Motor.IsDisplaced && HorizontalSpeed(p) >= WalkingMinSpeed;

        // Gait bonuses and penalties go through EffectHook.AdjustGait: the host sums every record's request and applies the
        // +20% movement cap once (Effects/HookAggregates.cs GaitAdjust, spec L124).

        public static bool IsSpellSource(string sourceId) => sourceId != null && sourceId.StartsWith("SPL-", StringComparison.Ordinal);

        /// <summary>An original (generation 0) hit by the attacker on its opponent that dealt damage.</summary>
        public static bool IsOriginalHitOn(DamageEvent d, int attacker) =>
            d != null && d.Attacker == attacker && d.Target == 1 - attacker && d.Generation == 0 && d.Dealt > 0f;

        /// <summary>A direct spell impact (not splash, field, or reaction) by the attacker on its opponent.</summary>
        public static bool IsDirectSpellHit(DamageEvent d, int attacker) =>
            IsOriginalHitOn(d, attacker) && d.Kind == DamageKind.Direct && IsSpellSource(d.SourceId);

        /// <summary>A common melee hit by the attacker on its opponent.</summary>
        public static bool IsWeaponHit(DamageEvent d, int attacker) => IsOriginalHitOn(d, attacker) && d.Kind == DamageKind.Melee;

        /// <summary>The live spell runtime of a duel, or null.</summary>
        public static SpellRuntime Runtime(DuelWorld world) => world.Spells as SpellRuntime;

        /// <summary>The live record of a cast, or null once it ended.</summary>
        public static CastRecord FindCast(DuelWorld world, Confluence.Casting.CastEventId castId)
        {
            SpellRuntime runtime = Runtime(world);
            return runtime != null && castId.IsValid ? runtime.FindCast(castId) : null;
        }

        /// <summary>The form of a spell in the player's loadout, or null.</summary>
        public static SpellForm FormOf(DuelPlayer p, string spellId) => p.Caster != null ? p.Caster.FormFor(spellId) : null;

        /// <summary>Probe height above the feet for wall and cover tests (waist of the 1.8 m standing capsule).</summary>
        public const float WallProbeHeight = 0.9f;

        /// <summary>
        /// The outward normal of the nearest vertical solid face (walls, cover, pillars, props) within <paramref name="range"/> of
        /// a body standing at the feet, probed in 16 horizontal directions at waist height; floors, ceilings and slopes
        /// (normal more than 30 degrees from horizontal) do not count.
        /// </summary>
        public static bool NearVerticalSolid(Confluence.Sim.World.CollisionWorld collision, Vec3 feet, float range, out Vec3 away)
        {
            away = Vec3.Zero;
            Vec3 origin = feet + new Vec3(0f, WallProbeHeight, 0f);
            float reach = Confluence.Sim.Movement.CharacterMotor.Radius + range;
            float best = float.PositiveInfinity;
            for (int i = 0; i < 16; i++)
            {
                double a = i * Math.PI / 8.0;
                Vec3 dir = new Vec3((float)Math.Sin(a), 0f, (float)Math.Cos(a));
                if (!collision.SegmentCast(origin, origin + dir * reach, Confluence.Sim.World.SolidMask.Movement, out Confluence.Sim.World.WorldHit hit))
                {
                    continue;
                }

                Vec3 n = new Vec3(hit.Normal.X, 0f, hit.Normal.Z);
                if (Math.Abs(hit.Normal.Y) > 0.5f || n.LengthSquared() < 1e-6f || hit.Distance >= best)
                {
                    continue;
                }

                best = hit.Distance;
                away = n.Normalized();
            }

            return best < float.PositiveInfinity;
        }

        /// <summary>The player's cast currently in preparation or later (invalid when idle).</summary>
        public static Confluence.Casting.CastEventId CurrentCast(DuelPlayer p) =>
            p.Caster != null ? p.Caster.Machine.CurrentCastId : Confluence.Casting.CastEventId.None;
    }

    /// <summary>An effect action that runs a record-specific callback (arming a hook, granting a record's output).</summary>
    internal sealed class CallbackAction : IEffectAction
    {
        private readonly Action<EffectContext> _apply;

        public CallbackAction(SecondaryEvents produces, string describe, Action<EffectContext> apply)
        {
            Produces = produces;
            Describe = describe;
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        }

        public SecondaryEvents Produces { get; }
        public string Describe { get; }

        public void Apply(in EffectContext context) => _apply(context);
    }
}
