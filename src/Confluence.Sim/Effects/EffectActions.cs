using System;
using System.Collections.Generic;
using Confluence.Core.Rules;
using Confluence.Sim.Players;

namespace Confluence.Sim.Effects
{
    /// <summary>
    /// Secondary events an effect may produce (spec L433 "permitted secondary events", L126 "One generation of
    /// secondary effects"). An effect declares them; attaching one whose actions produce anything else is an error.
    /// </summary>
    [Flags]
    public enum SecondaryEvents
    {
        None = 0,
        StatModifier = 1 << 0,
        Shield = 1 << 1,
        Resource = 1 << 2,
        Healing = 1 << 3,
        CastBonus = 1 << 4,
        HitBonus = 1 << 5,
        Cue = 1 << 6,
    }

    /// <summary>A typed effect action (spec L433 "magnitude"). Actions run once per activation, in list order.</summary>
    public interface IEffectAction
    {
        /// <summary>The secondary event family this action produces.</summary>
        SecondaryEvents Produces { get; }

        string Describe { get; }

        void Apply(in EffectContext context);
    }

    /// <summary>Resources an action can grant.</summary>
    public enum EffectResource
    {
        Mana,
        Stamina,
    }

    /// <summary>
    /// Stat modifiers for a duration, following the effect's stacking rule (resistance after a hit, a movement burst).
    /// The stat block applies every global cap to the sum; a duration of 0 lasts for the rest of the duel.
    /// </summary>
    public sealed class ModifyStatsAction : IEffectAction
    {
        private readonly StatModifier[] _modifiers;

        public ModifyStatsAction(int durationTicks, params StatModifier[] modifiers)
        {
            if (modifiers == null || modifiers.Length == 0)
            {
                throw new ArgumentException("At least one modifier is required.", nameof(modifiers));
            }

            DurationTicks = Math.Max(0, durationTicks);
            _modifiers = (StatModifier[])modifiers.Clone();
        }

        public int DurationTicks { get; }
        public IReadOnlyList<StatModifier> Modifiers => _modifiers;
        public SecondaryEvents Produces => SecondaryEvents.StatModifier;
        public string Describe => _modifiers.Length + " stat modifier(s) for " + DurationTicks + " ticks";

        public void Apply(in EffectContext context)
        {
            int target = context.TargetPlayer;
            if (target >= 0)
            {
                context.System.Host(target).ApplyBuff(context.Effect, _modifiers, DurationTicks);
            }
        }
    }

    /// <summary>
    /// Shield capacity: a triggered shield lasts at most 8 s; a reserve shield (round start only) lasts until broken;
    /// all share the 100 cap (spec L114, L579).
    /// </summary>
    public sealed class GrantShieldAction : IEffectAction
    {
        public GrantShieldAction(float amount, int durationTicks, bool reserve = false)
        {
            Amount = Math.Max(0f, amount);
            DurationTicks = Math.Max(0, durationTicks);
            Reserve = reserve;
        }

        public float Amount { get; }
        public int DurationTicks { get; }
        public bool Reserve { get; }
        public SecondaryEvents Produces => SecondaryEvents.Shield;
        public string Describe => (Reserve ? "reserve shield " : "shield ") + Amount;

        public void Apply(in EffectContext context)
        {
            int target = context.TargetPlayer;
            if (target >= 0)
            {
                context.World.GrantShield(target, context.Effect.SourceId, Amount, DurationTicks, Reserve);
            }
        }
    }

    /// <summary>Mana or stamina gain, never above the pool's maximum (spec L78).</summary>
    public sealed class GainResourceAction : IEffectAction
    {
        public GainResourceAction(EffectResource resource, float amount)
        {
            Resource = resource;
            Amount = Math.Max(0f, amount);
        }

        public EffectResource Resource { get; }
        public float Amount { get; }
        public SecondaryEvents Produces => SecondaryEvents.Resource;
        public string Describe => "gain " + Amount + " " + Resource.ToString().ToLowerInvariant();

        public void Apply(in EffectContext context)
        {
            int target = context.TargetPlayer;
            if (target >= 0)
            {
                context.System.GainResource(target, Resource, Amount, context.Effect.SourceId);
            }
        }
    }

    /// <summary>Direct healing inside the shared 35/s and 120 per duel budget (spec L114).</summary>
    public sealed class HealAction : IEffectAction
    {
        public HealAction(float amount)
        {
            Amount = Math.Max(0f, amount);
        }

        public float Amount { get; }
        public SecondaryEvents Produces => SecondaryEvents.Healing;
        public string Describe => "heal " + Amount;

        public void Apply(in EffectContext context)
        {
            int target = context.TargetPlayer;
            if (target >= 0)
            {
                context.World.Heal(target, Amount, context.Effect.SourceId);
            }
        }
    }

    /// <summary>
    /// A cost change for the player's next matching paid cast ("the next paid spell costs 4 less mana"). Positive
    /// values reduce, negative values increase; all reductions together keep at least 70% of the cost (spec L124).
    /// </summary>
    public sealed class NextCastCostAction : IEffectAction
    {
        public NextCastCostAction(float flatMana, float fraction, int windowTicks, CastFilter filter = null, int delayTicks = 0)
        {
            FlatMana = flatMana;
            Fraction = fraction;
            WindowTicks = windowTicks;
            DelayTicks = Math.Max(0, delayTicks);
            Filter = filter ?? CastFilter.Any;
        }

        public float FlatMana { get; }
        public float Fraction { get; }

        /// <summary>How long the bonus waits for a cast; 0 or less waits for the rest of the duel.</summary>
        public int WindowTicks { get; }

        /// <summary>Ticks before the bonus applies ("after 1 s").</summary>
        public int DelayTicks { get; }

        public CastFilter Filter { get; }
        public SecondaryEvents Produces => SecondaryEvents.CastBonus;
        public string Describe => "next cast cost " + (-FlatMana) + " mana, " + (-Fraction * 100f) + "%";

        public void Apply(in EffectContext context)
        {
            context.Host.AddCastBonus(context.Effect, FlatMana, Fraction, WindowTicks, DelayTicks, Filter);
        }
    }

    /// <summary>A damage bonus for the player's next matching original hit ("next direct hit deals 6% more").</summary>
    public sealed class NextHitDamageAction : IEffectAction
    {
        public NextHitDamageAction(float fraction, float flat, int windowTicks, HitFilter filter = null)
        {
            Fraction = fraction;
            Flat = flat;
            WindowTicks = windowTicks;
            Filter = filter ?? HitFilter.AnyDirect;
        }

        public float Fraction { get; }
        public float Flat { get; }
        public int WindowTicks { get; }
        public HitFilter Filter { get; }
        public SecondaryEvents Produces => SecondaryEvents.HitBonus;
        public string Describe => "next hit +" + (Fraction * 100f) + "% +" + Flat;

        public void Apply(in EffectContext context)
        {
            context.Host.AddHitBonus(context.Effect, Fraction, Flat, WindowTicks, Filter);
        }
    }

    /// <summary>A presentation cue or marker event (HUD flash, ward glyph) with no rule effect.</summary>
    public sealed class CueAction : IEffectAction
    {
        public CueAction(string cue)
        {
            Cue = cue ?? throw new ArgumentNullException(nameof(cue));
        }

        public string Cue { get; }
        public SecondaryEvents Produces => SecondaryEvents.Cue;
        public string Describe => "cue " + Cue;

        public void Apply(in EffectContext context)
        {
            context.System.EmitCue(context.Host.Player, context.Effect, Cue);
        }
    }
}
