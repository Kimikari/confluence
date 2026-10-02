using System;
using Confluence.Core.Rules;
using Confluence.Core.Time;

namespace Confluence.Sim.Status
{
    /// <summary>Named statuses (spec L116-L120). Their specific behavior is described by the source definition.</summary>
    public enum StatusKind
    {
        Burn,
        Venom,
        Chill,
        Brittle,
        Charge,
        Marked,
        Wet,
        Slow,
        Root,
        Stun,
        Silence,

        /// <summary>Hostile displacement that prevents deliberate movement (knockback); hard control (spec L116).</summary>
        Displaced,

        Reveal,
        Invisibility,
    }

    /// <summary>Rule data of one status kind.</summary>
    public sealed class StatusRule
    {
        internal StatusRule(StatusKind kind, bool hardControl, bool harmful, bool removable, float maxSeconds, bool damageOverTime,
            bool strongestOnly, float maxIntensity)
        {
            Kind = kind;
            HardControl = hardControl;
            Harmful = harmful;
            Removable = removable;
            MaxTicks = maxSeconds > 0f ? Ticks.FromSeconds(maxSeconds) : int.MaxValue;
            DamageOverTime = damageOverTime;
            StrongestOnly = strongestOnly;
            MaxIntensity = maxIntensity;
        }

        public StatusKind Kind { get; }

        /// <summary>Stun, root, silence, forced inability, displacement (spec L116).</summary>
        public bool HardControl { get; }

        /// <summary>A hostile status that dispels and cleanses target (spec L118).</summary>
        public bool Harmful { get; }

        /// <summary>Dispel may remove it (spec L118).</summary>
        public bool Removable { get; }

        /// <summary>Single-application duration cap in ticks (int.MaxValue when only the source limits it).</summary>
        public int MaxTicks { get; }

        public bool DamageOverTime { get; }

        /// <summary>Only the strongest instance applies (slows, spec L116).</summary>
        public bool StrongestOnly { get; }

        /// <summary>Intensity ceiling (slow fraction 0.30); float.MaxValue when uncapped.</summary>
        public float MaxIntensity { get; }
    }

    /// <summary>Registry of the global status rules (spec L116-L120).</summary>
    public static class StatusRules
    {
        private static readonly StatusRule[] Rules = Build();

        public static StatusRule Get(StatusKind kind) => Rules[(int)kind];

        public static bool IsHardControl(StatusKind kind) => Get(kind).HardControl;

        public static int HardControlWindowTicks => Ticks.FromSeconds(GlobalRules.Control.HardControlMaxSec);

        public static int ControlImmunityTicks => Ticks.FromSeconds(GlobalRules.Control.HardControlImmunitySec);

        public static int DotIntervalTicks => Ticks.FromSeconds(SimRules.Status.DotIntervalSec);

        private static StatusRule[] Build()
        {
            int count = Enum.GetValues(typeof(StatusKind)).Length;
            var rules = new StatusRule[count];
            float control = GlobalRules.Control.HardControlMaxSec;     // spec L116
            float silence = GlobalRules.Control.SilenceMaxSec;         // spec L116
            float slow = GlobalRules.Control.SlowMaxDurationSec;       // spec L116
            float uncapped = float.MaxValue;

            Set(rules, new StatusRule(StatusKind.Burn, false, true, true, 0f, true, false, uncapped));
            Set(rules, new StatusRule(StatusKind.Venom, false, true, true, 0f, true, false, uncapped));
            Set(rules, new StatusRule(StatusKind.Chill, false, true, true, 0f, false, false, uncapped));
            Set(rules, new StatusRule(StatusKind.Brittle, false, true, true, 0f, false, false, uncapped));
            Set(rules, new StatusRule(StatusKind.Charge, false, true, true, 0f, false, false, uncapped));
            Set(rules, new StatusRule(StatusKind.Marked, false, true, true, 0f, false, false, uncapped));
            Set(rules, new StatusRule(StatusKind.Wet, false, true, true, 0f, false, false, uncapped));
            Set(rules, new StatusRule(StatusKind.Slow, false, true, true, slow, false, true, GlobalRules.Control.SlowCap));
            Set(rules, new StatusRule(StatusKind.Root, true, true, true, control, false, false, uncapped));
            Set(rules, new StatusRule(StatusKind.Stun, true, true, true, control, false, false, uncapped));
            Set(rules, new StatusRule(StatusKind.Silence, true, true, true, silence, false, false, uncapped));
            Set(rules, new StatusRule(StatusKind.Displaced, true, true, false, control, false, false, uncapped));
            Set(rules, new StatusRule(StatusKind.Reveal, false, true, true, GlobalRules.Statuses.RevealMaxSec, false, false, uncapped));
            Set(rules, new StatusRule(StatusKind.Invisibility, false, false, false, GlobalRules.Statuses.InvisibilityMaxSec, false, false, uncapped));
            return rules;
        }

        private static void Set(StatusRule[] rules, StatusRule rule) => rules[(int)rule.Kind] = rule;
    }
}
