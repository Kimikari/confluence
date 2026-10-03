using System;
using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Hashing;
using Confluence.Sim.Movement;
using Confluence.Sim.Players;

namespace Confluence.Sim.Stats
{
    /// <summary>
    /// The modifier collection of one player with global cap enforcement for every capped quantity (spec L78-L124,
    /// L175, L579). Raw values sum their modifiers on top of the rule base (passive reductions combine
    /// multiplicatively); effective values apply the caps, so the build inspector can show both (spec L355). The
    /// motor, resources, damage pipeline, guard, melee, dodge, jump, and sprint read their parameters here, so
    /// equipment and augments change them only through modifiers.
    /// </summary>
    public class StatBlock
    {
        private static readonly DamageChannel[] Channels =
        {
            DamageChannel.Physical, DamageChannel.Thermal, DamageChannel.Electrical, DamageChannel.Corrosive, DamageChannel.Arcane,
        };

        private readonly List<StatModifier> _modifiers = new List<StatModifier>();

        /// <summary>Modifiers in insertion order.</summary>
        public IReadOnlyList<StatModifier> Modifiers => _modifiers;

        /// <summary>Incremented on every change, so dependants (resource maxima) can resynchronize.</summary>
        public int Version { get; private set; }

        public void Add(StatModifier modifier)
        {
            _modifiers.Add(modifier);
            Version++;
        }

        public void Add(StatKind stat, float value, string sourceId, DamageChannel channel = DamageChannel.None)
        {
            Add(new StatModifier(stat, value, sourceId, channel));
        }

        /// <summary>Removes every modifier from a source (an item unequipped, a buff ended). Returns the count removed.</summary>
        public int RemoveSource(string sourceId)
        {
            int removed = _modifiers.RemoveAll(m => string.Equals(m.SourceId, sourceId, StringComparison.Ordinal));
            if (removed > 0)
            {
                Version++;
            }

            return removed;
        }

        /// <summary>Number of modifiers carrying a source id (stacking rules count instances by source).</summary>
        public int CountSource(string sourceId)
        {
            int count = 0;
            for (int i = 0; i < _modifiers.Count; i++)
            {
                if (string.Equals(_modifiers[i].SourceId, sourceId, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Sets the expiry of every timed modifier from a source (a refreshed buff). Returns the count changed.</summary>
        public int SetExpiry(string sourceId, int expireTick)
        {
            int changed = 0;
            for (int i = 0; i < _modifiers.Count; i++)
            {
                StatModifier m = _modifiers[i];
                if (m.IsTimed && string.Equals(m.SourceId, sourceId, StringComparison.Ordinal) && m.ExpireTick != expireTick)
                {
                    m.ExpireTick = expireTick;
                    _modifiers[i] = m;
                    changed++;
                }
            }

            if (changed > 0)
            {
                Version++;
            }

            return changed;
        }

        /// <summary>Removes timed modifiers whose duration ended at or before now. Returns the count removed.</summary>
        public int Expire(int now)
        {
            int removed = _modifiers.RemoveAll(m => m.IsTimed && m.ExpireTick <= now);
            if (removed > 0)
            {
                Version++;
            }

            return removed;
        }

        /// <summary>Uncapped sum of a stat's modifiers (resistance and passive reduction consider the channel).</summary>
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

        // ----- movement (spec L80-L82, L124) -----------------------------------------------------------------

        /// <summary>The all-gait movement bonus, at most +20% (spec L124).</summary>
        public float MovementBonus => Math.Min(Raw(StatKind.MovementBonus), GlobalRules.Modifiers.MovementBonusCap);

        /// <summary>Speed multiplier from all-gait movement modifiers alone (slows are applied separately).</summary>
        public float MovementMultiplier => Math.Max(0f, 1f + MovementBonus);

        /// <summary>Walking (and crouching) bonus: all-gait plus walk modifiers, together at most +20% (spec L124).</summary>
        public float WalkBonus => Math.Min(Raw(StatKind.MovementBonus) + Raw(StatKind.WalkSpeedBonus), GlobalRules.Modifiers.MovementBonusCap);

        /// <summary>Sprinting bonus: all-gait plus sprint modifiers, together at most +20% (spec L124, EQP-081).</summary>
        public float SprintBonus => Math.Min(Raw(StatKind.MovementBonus) + Raw(StatKind.SprintSpeedBonus), GlobalRules.Modifiers.MovementBonusCap);

        public float SprintStaminaPerSecond => Math.Max(0f, GlobalRules.Movement.SprintStaminaPerSecond + Raw(StatKind.SprintStaminaPerSecond));

        public float JumpStaminaCost => Math.Max(0f, GlobalRules.Movement.JumpStaminaCost + Raw(StatKind.JumpStaminaCost));

        public float DodgeStaminaCost => Math.Max(0f, GlobalRules.Movement.DodgeStaminaCost + Raw(StatKind.DodgeStaminaCost));

        /// <summary>Dodge travel, never beyond the 8 m dash cap (spec L82, L124).</summary>
        public float DodgeDistance => CMath.Clamp(GlobalRules.Movement.DodgeDistance + Raw(StatKind.DodgeDistance), 0f, GlobalRules.Modifiers.MaxDashDistance);

        /// <summary>Dodge cooldown, never below the 0.5 s minimum recharge (spec L82, L124).</summary>
        public float DodgeCooldownSec => Math.Max(GlobalRules.Modifiers.MinRechargeSec, GlobalRules.Movement.DodgeCooldownSec + Raw(StatKind.DodgeCooldownSec));

        /// <summary>The motor parameters these stats produce (shared with client prediction).</summary>
        public MotorTuning Motor => new MotorTuning
        {
            Specified = true,
            WalkSpeed = GlobalRules.Movement.WalkSpeed,
            SprintSpeed = GlobalRules.Movement.SprintSpeed,
            CrouchSpeed = GlobalRules.Movement.CrouchSpeed,
            WalkMultiplier = Math.Max(0f, 1f + WalkBonus),
            SprintMultiplier = Math.Max(0f, 1f + SprintBonus),
            SprintStaminaPerSecond = SprintStaminaPerSecond,
            JumpStaminaCost = JumpStaminaCost,
            DodgeStaminaCost = DodgeStaminaCost,
            DodgeDistance = DodgeDistance,
            DodgeCooldownTicks = Ticks.FromSeconds(DodgeCooldownSec),
        };

        public float Traction => Clamp01(Raw(StatKind.Traction), 1f);

        /// <summary>The strongest of a status slow and a surface slow, at most 30% (spec L116, L175).</summary>
        public static float EffectiveSlow(float statusSlow, float surfaceSlow)
        {
            return CMath.Clamp(Math.Max(statusSlow, surfaceSlow), 0f, GlobalRules.Control.SlowCap);
        }

        // ----- casting economy (spec L90, L124) --------------------------------------------------------------

        public float CooldownReduction => Clamp01(Raw(StatKind.CooldownReduction), GlobalRules.Modifiers.CooldownReductionCap);

        public float CostReduction => Clamp01(Raw(StatKind.CostReduction), GlobalRules.Modifiers.CostReductionCap);

        public float RefundFraction => Clamp01(Raw(StatKind.RefundFraction), GlobalRules.Modifiers.RefundCapFraction);

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

        /// <summary>Paid cost after the stat reductions: at least 70% of the original (spec L124).</summary>
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

        // ----- dashes and teleports (spec L124) --------------------------------------------------------------

        public int DashCharges => Math.Max(0, Math.Min((int)Math.Floor(Raw(StatKind.DashCharges)), GlobalRules.Modifiers.MaxDashCharges));

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

        // ----- resources (spec L78, L114) --------------------------------------------------------------------

        /// <summary>Maximum health: 300 plus bonuses, capped at 450 (spec L114).</summary>
        public float MaxHealth => Math.Max(1f, Math.Min(RawMaxHealth, GlobalRules.Resources.MaxHealthCap));

        public float RawMaxHealth => GlobalRules.Resources.BaseHealth + Raw(StatKind.MaxHealthBonus);

        /// <summary>Maximum mana: 100 plus static capacity (spec L78, L579).</summary>
        public float MaxMana => Math.Max(1f, GlobalRules.Resources.BaseMana + Raw(StatKind.MaxManaBonus));

        /// <summary>Maximum stamina: 100 plus static capacity (spec L78, L579).</summary>
        public float MaxStamina => Math.Max(1f, GlobalRules.Resources.BaseStamina + Raw(StatKind.MaxStaminaBonus));

        /// <summary>Uncapped mana regeneration: the 10/s base scaled by its percentage, plus flat bonuses.</summary>
        public float RawManaRegenPerSecond =>
            GlobalRules.Resources.ManaRegenPerSecond * (1f + Raw(StatKind.ManaRegenPercent)) + Raw(StatKind.ManaRegenBonus);

        /// <summary>Mana regeneration per second after modifiers, capped at 14 (spec L78; EQP-066 "within +40% cap").</summary>
        public float ManaRegenPerSecond => Math.Max(0f, Math.Min(RawManaRegenPerSecond, GlobalRules.Resources.ManaRegenCapPerSecond));

        public float StaminaRegenPerSecond => Math.Max(0f, GlobalRules.Resources.StaminaRegenPerSecond * (1f + Raw(StatKind.StaminaRegenPercent)));

        public int ManaRegenDelayTicks => Ticks.FromSeconds(Math.Max(0f, GlobalRules.Resources.ManaRegenDelaySec + Raw(StatKind.ManaRegenDelaySec)));

        public int StaminaRegenDelayTicks => Ticks.FromSeconds(Math.Max(0f, GlobalRules.Resources.StaminaRegenDelaySec + Raw(StatKind.StaminaRegenDelaySec)));

        public float Lifesteal => Clamp01(Raw(StatKind.Lifesteal), GlobalRules.Healing.LifestealFractionCap);

        // ----- mitigation (spec L112) ------------------------------------------------------------------------

        /// <summary>Channel resistance clamped to [-15%, 35%] (spec L112).</summary>
        public float Resistance(DamageChannel channel)
        {
            return CMath.Clamp(Raw(StatKind.Resistance, channel), GlobalRules.Damage.NegativeResistanceFloor, GlobalRules.Damage.ChannelResistanceCap);
        }

        /// <summary>
        /// Damage multiplier of resistance and all other passive reductions for a channel: multiplicative, with a
        /// total reduction cap of 45% (spec L112). Negative resistance and vulnerabilities can raise it above 1.
        /// </summary>
        public float PassiveMultiplier(DamageChannel channel)
        {
            float m = (1f - Resistance(channel)) * PassiveProduct(channel);
            return Math.Max(m, 1f - GlobalRules.Damage.PassiveReductionCap);
        }

        // ----- guard (spec L104-L106) ------------------------------------------------------------------------

        public float GuardStaminaCost => Math.Max(0f, GlobalRules.Guard.StaminaCost + Raw(StatKind.GuardStaminaCost));

        /// <summary>Longest guard hold: the 0.8 s rule is a ceiling, so modifiers only shorten it (to at least the perfect window).</summary>
        public float GuardMaxHoldSec => CMath.Clamp(GlobalRules.Guard.MaxHoldSec + Raw(StatKind.GuardMaxHoldSec), GlobalRules.Guard.PerfectWindowSec, GlobalRules.Guard.MaxHoldSec);

        /// <summary>Guard cooldown from release, never below the 0.5 s minimum recharge (spec L104, L124).</summary>
        public float GuardCooldownSec => Math.Max(GlobalRules.Modifiers.MinRechargeSec, GlobalRules.Guard.CooldownSec + Raw(StatKind.GuardCooldownSec));

        public int GuardMaxHoldTicks => Ticks.FromSeconds(GuardMaxHoldSec);

        public int GuardCooldownTicks => Ticks.FromSeconds(GuardCooldownSec);

        // ----- common melee and outgoing damage (spec L100-L102) --------------------------------------------

        /// <summary>
        /// The active melee profile after modifiers. Damage, reach and stamina never go negative; the windup keeps at
        /// least one tick and the cycle at least the windup plus the live strike, so an attack always resolves.
        /// </summary>
        public MeleeProfile EffectiveMelee(MeleeProfile profile)
        {
            float damage = Math.Max(0f, profile.Damage + Raw(StatKind.MeleeDamage));
            float reach = Math.Max(0f, profile.Reach + Raw(StatKind.MeleeReach));
            float stamina = Math.Max(0f, profile.Stamina + Raw(StatKind.MeleeStaminaCost));
            float windup = Math.Max(Ticks.Dt, profile.Windup + Raw(StatKind.MeleeWindupSec));
            float minCycle = windup + (SimRules.Melee.StrikeActiveTicks + 1) * Ticks.Dt;
            float cycle = Math.Max(minCycle, profile.Cycle + Raw(StatKind.MeleeCycleSec) + (windup - profile.Windup));
            if (damage == profile.Damage && reach == profile.Reach && stamina == profile.Stamina && windup == profile.Windup && cycle == profile.Cycle)
            {
                return profile;
            }

            return new MeleeProfile(profile.Name, damage, reach, cycle, stamina, windup);
        }

        /// <summary>Multiplier on the attacker's original spell hits.</summary>
        public float SpellDamageMultiplier => Math.Max(0f, 1f + Raw(StatKind.SpellDamageBonus));

        /// <summary>Multiplier on the attacker's common melee hits.</summary>
        public float WeaponDamageMultiplier => Math.Max(0f, 1f + Raw(StatKind.WeaponDamageBonus));

        // ----- inspection (spec L355) ------------------------------------------------------------------------

        /// <summary>Raw versus effective values for every modified stat (compact form).</summary>
        public List<StatLine> Inspect()
        {
            List<StatLine> all = InspectAll();
            var lines = new List<StatLine>();
            for (int i = 0; i < all.Count; i++)
            {
                if (IsModified(all[i]))
                {
                    lines.Add(all[i]);
                }
            }

            return lines;
        }

        // A derived line counts as modified when any stat that feeds it carries a modifier.
        private bool IsModified(StatLine line)
        {
            switch (line.Stat)
            {
                case StatKind.ManaRegenPercent:
                    return HasModifier(StatKind.ManaRegenPercent, DamageChannel.None) || HasModifier(StatKind.ManaRegenBonus, DamageChannel.None);
                case StatKind.PassiveReduction:
                    return HasModifier(StatKind.PassiveReduction, line.Channel) || HasModifier(StatKind.Resistance, line.Channel);
                default:
                    return HasModifier(line.Stat, line.Channel);
            }
        }

        /// <summary>
        /// Every stat the build panel shows (spec L355 "final capped statistics. If raw bonuses exceed a cap, show both
        /// raw and effective values"): rule base, raw value, effective value, the binding cap, and whether it binds.
        /// </summary>
        public List<StatLine> InspectAll()
        {
            float none = float.NaN;
            var lines = new List<StatLine>();
            lines.Add(Line(StatKind.MaxHealthBonus, "Maximum health", GlobalRules.Resources.BaseHealth, RawMaxHealth, MaxHealth, GlobalRules.Resources.MaxHealthCap));
            lines.Add(Line(StatKind.MaxManaBonus, "Maximum mana", GlobalRules.Resources.BaseMana, GlobalRules.Resources.BaseMana + Raw(StatKind.MaxManaBonus), MaxMana, none));
            lines.Add(Line(StatKind.MaxStaminaBonus, "Maximum stamina", GlobalRules.Resources.BaseStamina, GlobalRules.Resources.BaseStamina + Raw(StatKind.MaxStaminaBonus), MaxStamina, none));
            lines.Add(Line(StatKind.ManaRegenPercent, "Mana regeneration /s", GlobalRules.Resources.ManaRegenPerSecond, RawManaRegenPerSecond, ManaRegenPerSecond, GlobalRules.Resources.ManaRegenCapPerSecond));
            lines.Add(Line(StatKind.ManaRegenBonus, "Mana regeneration bonus /s", 0f, Raw(StatKind.ManaRegenBonus), Raw(StatKind.ManaRegenBonus), none));
            lines.Add(Line(StatKind.StaminaRegenPercent, "Stamina regeneration /s", GlobalRules.Resources.StaminaRegenPerSecond,
                GlobalRules.Resources.StaminaRegenPerSecond * (1f + Raw(StatKind.StaminaRegenPercent)), StaminaRegenPerSecond, none));
            lines.Add(Line(StatKind.ManaRegenDelaySec, "Mana regeneration delay s", GlobalRules.Resources.ManaRegenDelaySec,
                GlobalRules.Resources.ManaRegenDelaySec + Raw(StatKind.ManaRegenDelaySec), ManaRegenDelayTicks / (float)Ticks.PerSecond, none));
            lines.Add(Line(StatKind.StaminaRegenDelaySec, "Stamina regeneration delay s", GlobalRules.Resources.StaminaRegenDelaySec,
                GlobalRules.Resources.StaminaRegenDelaySec + Raw(StatKind.StaminaRegenDelaySec), StaminaRegenDelayTicks / (float)Ticks.PerSecond, none));
            lines.Add(Line(StatKind.MovementBonus, "Movement bonus", 0f, Raw(StatKind.MovementBonus), MovementBonus, GlobalRules.Modifiers.MovementBonusCap));
            lines.Add(Line(StatKind.WalkSpeedBonus, "Walk speed bonus", 0f, Raw(StatKind.MovementBonus) + Raw(StatKind.WalkSpeedBonus), WalkBonus, GlobalRules.Modifiers.MovementBonusCap));
            lines.Add(Line(StatKind.SprintSpeedBonus, "Sprint speed bonus", 0f, Raw(StatKind.MovementBonus) + Raw(StatKind.SprintSpeedBonus), SprintBonus, GlobalRules.Modifiers.MovementBonusCap));
            lines.Add(Line(StatKind.SprintStaminaPerSecond, "Sprint stamina /s", GlobalRules.Movement.SprintStaminaPerSecond,
                GlobalRules.Movement.SprintStaminaPerSecond + Raw(StatKind.SprintStaminaPerSecond), SprintStaminaPerSecond, none));
            lines.Add(Line(StatKind.JumpStaminaCost, "Jump stamina", GlobalRules.Movement.JumpStaminaCost, GlobalRules.Movement.JumpStaminaCost + Raw(StatKind.JumpStaminaCost), JumpStaminaCost, none));
            lines.Add(Line(StatKind.DodgeStaminaCost, "Dodge stamina", GlobalRules.Movement.DodgeStaminaCost, GlobalRules.Movement.DodgeStaminaCost + Raw(StatKind.DodgeStaminaCost), DodgeStaminaCost, none));
            lines.Add(Line(StatKind.DodgeDistance, "Dodge distance m", GlobalRules.Movement.DodgeDistance, GlobalRules.Movement.DodgeDistance + Raw(StatKind.DodgeDistance), DodgeDistance, GlobalRules.Modifiers.MaxDashDistance));
            lines.Add(Line(StatKind.DodgeCooldownSec, "Dodge cooldown s", GlobalRules.Movement.DodgeCooldownSec, GlobalRules.Movement.DodgeCooldownSec + Raw(StatKind.DodgeCooldownSec), DodgeCooldownSec, GlobalRules.Modifiers.MinRechargeSec));
            lines.Add(Line(StatKind.Traction, "Ice traction", 0f, Raw(StatKind.Traction), Traction, 1f));
            lines.Add(Line(StatKind.CooldownReduction, "Cooldown reduction", 0f, Raw(StatKind.CooldownReduction), CooldownReduction, GlobalRules.Modifiers.CooldownReductionCap));
            lines.Add(Line(StatKind.CostReduction, "Cost reduction", 0f, Raw(StatKind.CostReduction), CostReduction, GlobalRules.Modifiers.CostReductionCap));
            lines.Add(Line(StatKind.RefundFraction, "Refund", 0f, Raw(StatKind.RefundFraction), RefundFraction, GlobalRules.Modifiers.RefundCapFraction));
            lines.Add(Line(StatKind.DashCharges, "Dash charges", 0f, Raw(StatKind.DashCharges), DashCharges, GlobalRules.Modifiers.MaxDashCharges));
            lines.Add(Line(StatKind.DashDistance, "Dash distance bonus m", 0f, Raw(StatKind.DashDistance), Math.Min(Raw(StatKind.DashDistance), GlobalRules.Modifiers.MaxDashDistance), GlobalRules.Modifiers.MaxDashDistance));
            lines.Add(Line(StatKind.TeleportDistance, "Teleport distance bonus m", 0f, Raw(StatKind.TeleportDistance), Math.Min(Raw(StatKind.TeleportDistance), GlobalRules.Modifiers.MaxTeleportDistance), GlobalRules.Modifiers.MaxTeleportDistance));
            lines.Add(Line(StatKind.Lifesteal, "Lifesteal", 0f, Raw(StatKind.Lifesteal), Lifesteal, GlobalRules.Healing.LifestealFractionCap));
            lines.Add(Line(StatKind.GuardStaminaCost, "Guard stamina", GlobalRules.Guard.StaminaCost, GlobalRules.Guard.StaminaCost + Raw(StatKind.GuardStaminaCost), GuardStaminaCost, none));
            lines.Add(Line(StatKind.GuardMaxHoldSec, "Guard hold s", GlobalRules.Guard.MaxHoldSec, GlobalRules.Guard.MaxHoldSec + Raw(StatKind.GuardMaxHoldSec), GuardMaxHoldSec, GlobalRules.Guard.MaxHoldSec));
            lines.Add(Line(StatKind.GuardCooldownSec, "Guard cooldown s", GlobalRules.Guard.CooldownSec, GlobalRules.Guard.CooldownSec + Raw(StatKind.GuardCooldownSec), GuardCooldownSec, GlobalRules.Modifiers.MinRechargeSec));
            lines.Add(Line(StatKind.MeleeDamage, "Melee damage bonus", 0f, Raw(StatKind.MeleeDamage), Raw(StatKind.MeleeDamage), none));
            lines.Add(Line(StatKind.MeleeStaminaCost, "Melee stamina bonus", 0f, Raw(StatKind.MeleeStaminaCost), Raw(StatKind.MeleeStaminaCost), none));
            lines.Add(Line(StatKind.MeleeReach, "Melee reach bonus m", 0f, Raw(StatKind.MeleeReach), Raw(StatKind.MeleeReach), none));
            lines.Add(Line(StatKind.MeleeWindupSec, "Melee windup bonus s", 0f, Raw(StatKind.MeleeWindupSec), Raw(StatKind.MeleeWindupSec), none));
            lines.Add(Line(StatKind.MeleeCycleSec, "Melee cycle bonus s", 0f, Raw(StatKind.MeleeCycleSec), Raw(StatKind.MeleeCycleSec), none));
            lines.Add(Line(StatKind.SpellDamageBonus, "Spell damage bonus", 0f, Raw(StatKind.SpellDamageBonus), SpellDamageMultiplier - 1f, none));
            lines.Add(Line(StatKind.WeaponDamageBonus, "Weapon damage bonus", 0f, Raw(StatKind.WeaponDamageBonus), WeaponDamageMultiplier - 1f, none));
            for (int c = 0; c < Channels.Length; c++)
            {
                DamageChannel ch = Channels[c];
                StatLine resist = Line(StatKind.Resistance, ch + " resistance", 0f, Raw(StatKind.Resistance, ch), Resistance(ch), GlobalRules.Damage.ChannelResistanceCap);
                resist.Channel = ch;
                resist.Capped = Math.Abs(resist.Raw - resist.Effective) > 1e-6f; // the -15% floor binds too
                lines.Add(resist);
                float raw = 1f - (1f - Raw(StatKind.Resistance, ch)) * PassiveProduct(ch);
                StatLine passive = Line(StatKind.PassiveReduction, ch + " total reduction", 0f, raw, 1f - PassiveMultiplier(ch), GlobalRules.Damage.PassiveReductionCap);
                passive.Channel = ch;
                lines.Add(passive);
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
                h.Add(_modifiers[i].ExpireTick);
            }
        }

        // Each modifier's reduction multiplies; negative values are vulnerabilities (clamped to [-100%, 100%]).
        private float PassiveProduct(DamageChannel channel)
        {
            float product = 1f;
            for (int i = 0; i < _modifiers.Count; i++)
            {
                StatModifier m = _modifiers[i];
                if (m.Stat == StatKind.PassiveReduction && (m.Channel == DamageChannel.None || m.Channel == channel))
                {
                    product *= 1f - CMath.Clamp(m.Value, -1f, 1f);
                }
            }

            return product;
        }

        private bool HasModifier(StatKind stat, DamageChannel channel)
        {
            for (int i = 0; i < _modifiers.Count; i++)
            {
                StatModifier m = _modifiers[i];
                if (m.Stat != stat)
                {
                    continue;
                }

                if (m.Channel == channel || (stat == StatKind.PassiveReduction && m.Channel == DamageChannel.None))
                {
                    return true;
                }
            }

            return false;
        }

        private static StatLine Line(StatKind stat, string label, float baseValue, float raw, float effective, float cap)
        {
            return new StatLine
            {
                Stat = stat,
                Channel = DamageChannel.None,
                Label = label,
                Base = baseValue,
                Raw = raw,
                Effective = effective,
                Cap = cap,
                Capped = Math.Abs(raw - effective) > 1e-6f,
            };
        }

        private static float Clamp01(float value, float cap) => Math.Max(0f, Math.Min(value, cap));
    }
}
