using System;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Sim.Duel;
using Confluence.Sim.Players;
using Confluence.Sim.Status;
using SurfaceKind = Confluence.Sim.World.SurfaceKind;

namespace Confluence.Sim.Effects
{
    /// <summary>What a condition or action sees when an effect is evaluated.</summary>
    public readonly struct EffectContext
    {
        public EffectContext(EffectSystem system, EffectHost host, Effect effect, in TriggerEvent trigger)
        {
            System = system;
            Host = host;
            Effect = effect;
            Trigger = trigger;
        }

        public EffectSystem System { get; }
        public EffectHost Host { get; }

        /// <summary>The triggered effect, or null for a conditional modifier.</summary>
        public Effect Effect { get; }

        public TriggerEvent Trigger { get; }

        public DuelWorld World => System.World;
        public int Tick => System.World.Tick;
        public DuelPlayer Self => System.World.Player(Host.Player);
        public DuelPlayer Opponent => System.World.Opponent(Host.Player);

        /// <summary>The player an action affects, chosen by the effect's target rule.</summary>
        public int TargetPlayer
        {
            get
            {
                EffectTarget target = Effect == null ? EffectTarget.Self : Effect.Target;
                switch (target)
                {
                    case EffectTarget.Opponent:
                        return 1 - Host.Player;
                    case EffectTarget.TriggerCounterpart:
                        return Trigger.Other >= 0 && Trigger.Other < DuelWorld.PlayerCount ? Trigger.Other : -1;
                    default:
                        return Host.Player;
                }
            }
        }
    }

    /// <summary>A condition over player and world state (spec L433 "condition"). Must be a pure function of the context.</summary>
    public interface IEffectCondition
    {
        /// <summary>Short readable form for the build inspector.</summary>
        string Describe { get; }

        bool Holds(in EffectContext context);
    }

    /// <summary>The condition vocabulary used by catalogue effects.</summary>
    public static class Conditions
    {
        public static readonly IEffectCondition Always = new Named("always", (in EffectContext c) => true);

        public static readonly IEffectCondition Grounded = new Named("grounded", (in EffectContext c) => c.Self.Motor.Grounded);

        public static readonly IEffectCondition Airborne = new Named("airborne", (in EffectContext c) => !c.Self.Motor.Grounded);

        public static readonly IEffectCondition Sprinting = new Named("sprinting", (in EffectContext c) => c.Self.Motor.Sprinting);

        public static readonly IEffectCondition Crouched = new Named("crouched", (in EffectContext c) => c.Self.Motor.Crouched);

        public static readonly IEffectCondition InCombat = new Named("in combat", (in EffectContext c) => c.World.Phase == DuelPhase.Combat);

        /// <summary>Ordinary mana regeneration is active: 1.5 s (after modifiers) without spending (spec L78).</summary>
        public static readonly IEffectCondition ManaRegenerating = new Named("mana regenerating",
            (in EffectContext c) => c.Tick - c.Self.Resources.LastManaSpendTick >= c.Self.Stats.ManaRegenDelayTicks);

        /// <summary>Ordinary stamina regeneration is active (spec L78).</summary>
        public static readonly IEffectCondition StaminaRegenerating = new Named("stamina regenerating",
            (in EffectContext c) => c.Tick - c.Self.Resources.LastStaminaSpendTick >= c.Self.Stats.StaminaRegenDelayTicks);

        public static IEffectCondition HealthFractionBelow(float fraction) =>
            new Named("health below " + Pct(fraction), (in EffectContext c) => c.Self.Resources.HealthFraction < fraction);

        public static IEffectCondition ManaBelow(float mana) =>
            new Named("mana below " + mana, (in EffectContext c) => c.Self.Resources.Mana < mana);

        public static IEffectCondition OpponentWithin(float meters) =>
            new Named("opponent within " + meters + " m", (in EffectContext c) => Vec3.Distance(c.Self.Position, c.Opponent.Position) <= meters);

        public static IEffectCondition OpponentBeyond(float meters) =>
            new Named("opponent beyond " + meters + " m", (in EffectContext c) => Vec3.Distance(c.Self.Position, c.Opponent.Position) > meters);

        public static IEffectCondition HasStatus(StatusKind kind) =>
            new Named("has " + kind, (in EffectContext c) => c.Self.Statuses.Has(kind));

        public static IEffectCondition OnSurface(SurfaceKind kind) =>
            new Named("on " + kind, (in EffectContext c) => c.World.SurfaceUnder(c.Self).Kind == kind);

        public static IEffectCondition Not(IEffectCondition inner)
        {
            Require(inner);
            return new Named("not " + inner.Describe, (in EffectContext c) => !inner.Holds(c));
        }

        public static IEffectCondition All(params IEffectCondition[] parts)
        {
            Require(parts);
            return new Named(Join(" and ", parts), (in EffectContext c) =>
            {
                for (int i = 0; i < parts.Length; i++)
                {
                    if (!parts[i].Holds(c))
                    {
                        return false;
                    }
                }

                return true;
            });
        }

        public static IEffectCondition Any(params IEffectCondition[] parts)
        {
            Require(parts);
            return new Named(Join(" or ", parts), (in EffectContext c) =>
            {
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i].Holds(c))
                    {
                        return true;
                    }
                }

                return false;
            });
        }

        /// <summary>A record-specific condition; the predicate must be deterministic and must not change state.</summary>
        public static IEffectCondition When(string describe, ConditionPredicate predicate) => new Named(describe, predicate);

        public delegate bool ConditionPredicate(in EffectContext context);

        private static void Require(object value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
        }

        private static string Join(string separator, IEffectCondition[] parts)
        {
            var names = new string[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                names[i] = parts[i] == null ? "?" : parts[i].Describe;
            }

            return string.Join(separator, names);
        }

        private static string Pct(float fraction) => Math.Round(fraction * 100f, 1) + "%";

        private sealed class Named : IEffectCondition
        {
            private readonly ConditionPredicate _predicate;

            public Named(string describe, ConditionPredicate predicate)
            {
                Describe = describe ?? throw new ArgumentNullException(nameof(describe));
                _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
            }

            public string Describe { get; }

            public bool Holds(in EffectContext context) => _predicate(context);
        }
    }
}
