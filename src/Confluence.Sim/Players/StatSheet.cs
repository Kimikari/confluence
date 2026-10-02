using System;
using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Sim.Hashing;

namespace Confluence.Sim.Players
{
    /// <summary>Statistics that equipment, augments, and buffs modify. Each has a global cap (spec L112-L124).</summary>
    public enum StatKind
    {
        /// <summary>Fractional movement speed bonus; effective at most +20% (spec L124).</summary>
        MovementBonus,

        /// <summary>Permanent cooldown reduction; at most 25%, and no recharge below 0.5 s (spec L124).</summary>
        CooldownReduction,

        /// <summary>Cost reduction from all sources; at least 70% of the cost is paid (spec L124).</summary>
        CostReduction,

        /// <summary>Refund fraction of a payment; at most 25% (spec L90, L124).</summary>
        RefundFraction,

        /// <summary>Dash charges held; at most 2 (spec L124).</summary>
        DashCharges,

        /// <summary>Dash distance in meters; at most 8 (spec L124).</summary>
        DashDistance,

        /// <summary>Teleport distance in meters; at most 10 (spec L124).</summary>
        TeleportDistance,

        /// <summary>Added mana regeneration per second; total regeneration at most 14 per second (spec L78).</summary>
        ManaRegenBonus,

        /// <summary>Lifesteal fraction of direct damage; at most 10% (spec L114).</summary>
        Lifesteal,

        /// <summary>Added maximum health; maximum health at most 450 (spec L114).</summary>
        MaxHealthBonus,

        /// <summary>Channel resistance (per channel); -15% to 35% (spec L112).</summary>
        Resistance,

        /// <summary>Other passive damage reduction (all channels or one); combined with resistance at most 45% (spec L112).</summary>
        PassiveReduction,

        /// <summary>Ice traction 0-1 from a declared traction item (spec L175).</summary>
        Traction,
    }

    /// <summary>One modifier from a named source.</summary>
    public struct StatModifier
    {
        public StatKind Stat;

        /// <summary>Damage channel for resistance and channel-specific passive reductions (None = all channels).</summary>
        public DamageChannel Channel;

        public float Value;
        public string SourceId;

        public StatModifier(StatKind stat, float value, string sourceId, DamageChannel channel = DamageChannel.None)
        {
            Stat = stat;
            Value = value;
            SourceId = sourceId;
            Channel = channel;
        }
    }

    /// <summary>A line of the build inspector: raw and effective value and whether a cap binds (spec L355).</summary>
    public struct StatLine
    {
        public StatKind Stat;
        public DamageChannel Channel;
        public float Raw;
        public float Effective;
        public bool Capped;
    }

    /// <summary>
    /// Modifier collection of one player with global cap enforcement. Raw values sum (passive reductions combine
    /// multiplicatively); effective values apply the caps, so the inspector can show both (spec L355).
    /// </summary>
    public sealed class StatSheet
    {
        private static readonly DamageChannel[] Channels =
        {
            DamageChannel.Physical, DamageChannel.Thermal, DamageChannel.Electrical, DamageChannel.Corrosive, DamageChannel.Arcane,
        };

        private readonly List<StatModifier> _modifiers = new List<StatModifier>();

        public IReadOnlyList<StatModifier> Modifiers => _modifiers;

        public void Add(StatModifier modifier) => _modifiers.Add(modifier);

        public void Add(StatKind stat, float value, string sourceId, DamageChannel channel = DamageChannel.None)
        {
            _modifiers.Add(new StatModifier(stat, value, sourceId, channel));
        }

        /// <summary>Removes every modifier from a source (an item unequipped, a buff ended). Returns the count removed.</summary>
        public int RemoveSource(string sourceId)
        {
            return _modifiers.RemoveAll(m => string.Equals(m.SourceId, sourceId, StringComparison.Ordinal));
        }

        /// <summary>Uncapped sum of a stat (for one channel, resistance and passive reduction consider that channel).</summary>
        public float Raw(StatKind stat, DamageChannel channel = DamageChannel.None)
        {
            if (stat == StatKind.PassiveReduction)
            {
                return 1f - PassiveProduct(channel);
            }

            float sum = 0f;
            for (int i = 0; i < _modifiers.Count; i++)
            {
                StatModifier m = _modifiers[i];
                if (m.Stat != stat)
                {
                    continue;
                }

                if (stat == StatKind.Resistance && m.Channel != channel)
                {
                    continue;
                }

                sum += m.Value;
            }

            return sum;
        }

        // ----- effective values ------------------------------------------------------------------------------

        public float MovementBonus => Math.Min(Raw(StatKind.MovementBonus), GlobalRules.Modifiers.MovementBonusCap);

        /// <summary>Speed multiplier from movement modifiers alone (slows are applied separately).</summary>
        public float MovementMultiplier => Math.Max(0f, 1f + MovementBonus);

        public float CooldownReduction => CMath01(Raw(StatKind.CooldownReduction), GlobalRules.Modifiers.CooldownReductionCap);

        public float CostReduction => CMath01(Raw(StatKind.CostReduction), GlobalRules.Modifiers.CostReductionCap);

        public float RefundFraction => CMath01(Raw(StatKind.RefundFraction), GlobalRules.Modifiers.RefundCapFraction);

        public int DashCharges => Math.Max(0, Math.Min((int)Math.Floor(Raw(StatKind.DashCharges)), GlobalRules.Modifiers.MaxDashCharges));

        public float Lifesteal => CMath01(Raw(StatKind.Lifesteal), GlobalRules.Healing.LifestealFractionCap);

        public float Traction => CMath01(Raw(StatKind.Traction), 1f);

        /// <summary>Mana regeneration per second after modifiers, capped at 14 (spec L78).</summary>
        public float ManaRegenPerSecond
        {
            get
            {
                float raw = GlobalRules.Resources.ManaRegenPerSecond + Raw(StatKind.ManaRegenBonus);
                return Math.Max(0f, Math.Min(raw, GlobalRules.Resources.ManaRegenCapPerSecond));
            }
        }

        /// <summary>Maximum health: 300 plus bonuses, capped at 450 (spec L114).</summary>
        public float MaxHealth
        {
            get
            {
                float raw = GlobalRules.Resources.BaseHealth + Raw(StatKind.MaxHealthBonus);
                return Math.Max(1f, Math.Min(raw, GlobalRules.Resources.MaxHealthCap));
            }
        }

        /// <summary>Channel resistance clamped to [-15%, 35%] (spec L112).</summary>
        public float Resistance(DamageChannel channel)
        {
            return CMath.Clamp(Raw(StatKind.Resistance, channel), GlobalRules.Damage.NegativeResistanceFloor, GlobalRules.Damage.ChannelResistanceCap);
        }

        /// <summary>
        /// Damage multiplier of resistance and all other passive reductions for a channel: multiplicative, with a
        /// total reduction cap of 45% (spec L112). Negative resistance can raise it above 1.
        /// </summary>
        public float PassiveMultiplier(DamageChannel channel)
        {
            float m = (1f - Resistance(channel)) * PassiveProduct(channel);
            return Math.Max(m, 1f - GlobalRules.Damage.PassiveReductionCap);
        }

        /// <summary>Recharge after permanent cooldown reduction; never below 0.5 s unless the base already is (spec L124).</summary>
        public float EffectiveCooldownSec(float baseSec)
        {
            if (!(baseSec > 0f))
            {
                return 0f;
            }

            float reduced = baseSec * (1f - CooldownReduction);
            return Math.Max(reduced, Math.Min(baseSec, GlobalRules.Modifiers.MinRechargeSec));
        }

        /// <summary>Paid cost after reductions: at least 70% of the original (spec L124).</summary>
        public float EffectiveCost(float baseCost)
        {
            return Math.Max(0f, baseCost) * Math.Max(GlobalRules.Modifiers.MinPaidCostFraction, 1f - CostReduction);
        }

        /// <summary>A refund of a payment, at most 25% of it (spec L90, L124).</summary>
        public float CapRefund(float payment, float requested)
        {
            float cap = Math.Max(0f, payment) * GlobalRules.Modifiers.RefundCapFraction;
            return Math.Max(0f, Math.Min(requested, cap));
        }

        /// <summary>Dash distance after modifiers, at most 8 m (spec L124).</summary>
        public float EffectiveDashDistance(float baseMeters)
        {
            return Math.Max(0f, Math.Min(baseMeters + Raw(StatKind.DashDistance), GlobalRules.Modifiers.MaxDashDistance));
        }

        /// <summary>Teleport distance after modifiers, at most 10 m (spec L124).</summary>
        public float EffectiveTeleportDistance(float baseMeters)
        {
            return Math.Max(0f, Math.Min(baseMeters + Raw(StatKind.TeleportDistance), GlobalRules.Modifiers.MaxTeleportDistance));
        }

        /// <summary>Raw versus effective values for every modified stat, for the inspector (spec L355).</summary>
        public List<StatLine> Inspect()
        {
            var lines = new List<StatLine>();
            AddLine(lines, StatKind.MovementBonus, Raw(StatKind.MovementBonus), MovementBonus);
            AddLine(lines, StatKind.CooldownReduction, Raw(StatKind.CooldownReduction), CooldownReduction);
            AddLine(lines, StatKind.CostReduction, Raw(StatKind.CostReduction), CostReduction);
            AddLine(lines, StatKind.RefundFraction, Raw(StatKind.RefundFraction), RefundFraction);
            AddLine(lines, StatKind.DashCharges, Raw(StatKind.DashCharges), DashCharges);
            AddLine(lines, StatKind.DashDistance, Raw(StatKind.DashDistance), Raw(StatKind.DashDistance));
            AddLine(lines, StatKind.TeleportDistance, Raw(StatKind.TeleportDistance), Raw(StatKind.TeleportDistance));
            AddLine(lines, StatKind.ManaRegenBonus, GlobalRules.Resources.ManaRegenPerSecond + Raw(StatKind.ManaRegenBonus), ManaRegenPerSecond);
            AddLine(lines, StatKind.Lifesteal, Raw(StatKind.Lifesteal), Lifesteal);
            AddLine(lines, StatKind.MaxHealthBonus, GlobalRules.Resources.BaseHealth + Raw(StatKind.MaxHealthBonus), MaxHealth);
            AddLine(lines, StatKind.Traction, Raw(StatKind.Traction), Traction);
            for (int c = 0; c < Channels.Length; c++)
            {
                DamageChannel ch = Channels[c];
                if (HasModifier(StatKind.Resistance, ch))
                {
                    lines.Add(Line(StatKind.Resistance, ch, Raw(StatKind.Resistance, ch), Resistance(ch)));
                }

                if (HasModifier(StatKind.PassiveReduction, DamageChannel.None) || HasModifier(StatKind.PassiveReduction, ch)
                    || HasModifier(StatKind.Resistance, ch))
                {
                    float raw = 1f - (1f - Raw(StatKind.Resistance, ch)) * PassiveProduct(ch);
                    lines.Add(Line(StatKind.PassiveReduction, ch, raw, 1f - PassiveMultiplier(ch)));
                }
            }

            return lines;
        }

        public void Hash(ref StateHasher h)
        {
            h.Add(_modifiers.Count);
            for (int i = 0; i < _modifiers.Count; i++)
            {
                h.Add((int)_modifiers[i].Stat);
                h.Add((int)_modifiers[i].Channel);
                h.Add(_modifiers[i].Value);
                h.Add(_modifiers[i].SourceId);
            }
        }

        private float PassiveProduct(DamageChannel channel)
        {
            float product = 1f;
            for (int i = 0; i < _modifiers.Count; i++)
            {
                StatModifier m = _modifiers[i];
                if (m.Stat == StatKind.PassiveReduction && (m.Channel == DamageChannel.None || m.Channel == channel))
                {
                    product *= 1f - CMath01(m.Value, 1f);
                }
            }

            return product;
        }

        private bool HasModifier(StatKind stat, DamageChannel channel)
        {
            for (int i = 0; i < _modifiers.Count; i++)
            {
                if (_modifiers[i].Stat == stat && _modifiers[i].Channel == channel)
                {
                    return true;
                }
            }

            return false;
        }

        private void AddLine(List<StatLine> lines, StatKind stat, float raw, float effective)
        {
            if (HasModifier(stat, DamageChannel.None))
            {
                lines.Add(Line(stat, DamageChannel.None, raw, effective));
            }
        }

        private static StatLine Line(StatKind stat, DamageChannel channel, float raw, float effective)
        {
            return new StatLine { Stat = stat, Channel = channel, Raw = raw, Effective = effective, Capped = Math.Abs(raw - effective) > 1e-6f };
        }

        private static float CMath01(float value, float cap) => Math.Max(0f, Math.Min(value, cap));
    }
}
