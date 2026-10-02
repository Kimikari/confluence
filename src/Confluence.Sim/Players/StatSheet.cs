using Confluence.Core.Rules;
using Confluence.Sim.Stats;

namespace Confluence.Sim.Players
{
    /// <summary>
    /// Statistics that equipment, augments, buffs, and drawbacks modify. Each stat is additive in its own unit (meters,
    /// seconds, points, or fractions); its effective value applies the global caps (spec L78-L124, L175). The values
    /// are kept in this order so serialized ids stay stable; new stats are appended.
    /// </summary>
    public enum StatKind
    {
        /// <summary>Fractional movement speed bonus for every gait; effective at most +20% with the gait bonuses (spec L124).</summary>
        MovementBonus,

        /// <summary>Permanent cooldown reduction; at most 25%, and no recharge below 0.5 s (spec L124).</summary>
        CooldownReduction,

        /// <summary>Cost reduction fraction from all sources; at least 70% of the cost is paid (spec L124).</summary>
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

        /// <summary>
        /// Other passive damage reduction (all channels or one); combined with resistance at most 45% (spec L112).
        /// Negative values are vulnerabilities ("incoming damage increases 2%").
        /// </summary>
        PassiveReduction,

        /// <summary>Ice traction 0-1 from a declared traction item (spec L175).</summary>
        Traction,

        /// <summary>Added maximum mana ("Maximum mana increases 8"); static capacity, not regeneration (spec L579).</summary>
        MaxManaBonus,

        /// <summary>Added maximum stamina ("Maximum stamina decreases 4").</summary>
        MaxStaminaBonus,

        /// <summary>Mana regeneration percentage of the base 10/s ("increases 8%, within +40% cap"): at most 14/s (spec L78).</summary>
        ManaRegenPercent,

        /// <summary>Stamina regeneration percentage of the base 20/s (spec L78).</summary>
        StaminaRegenPercent,

        /// <summary>Seconds added to the 1.5 s mana regeneration delay (spec L78); the delay never goes below 0.</summary>
        ManaRegenDelaySec,

        /// <summary>Seconds added to the 1 s stamina regeneration delay ("delay decreases 0.1 s", spec L78).</summary>
        StaminaRegenDelaySec,

        /// <summary>Walk speed bonus fraction ("Walking speed decreases 2%"); shares the +20% cap (spec L124).</summary>
        WalkSpeedBonus,

        /// <summary>Sprint speed bonus fraction ("Sprint speed increases 3%, within +20% cap", spec L124).</summary>
        SprintSpeedBonus,

        /// <summary>Stamina per second added to the 12/s sprint drain (spec L80).</summary>
        SprintStaminaPerSecond,

        /// <summary>Stamina added to the 10 jump cost (spec L80).</summary>
        JumpStaminaCost,

        /// <summary>Stamina added to the 25 dodge cost (spec L82).</summary>
        DodgeStaminaCost,

        /// <summary>Meters added to the 3 m dodge; a dodge never exceeds the 8 m dash cap (spec L82, L124).</summary>
        DodgeDistance,

        /// <summary>Seconds added to the 1.6 s dodge cooldown; never below the 0.5 s minimum recharge (spec L82, L124).</summary>
        DodgeCooldownSec,

        /// <summary>Stamina added to the 25 guard cost (spec L104).</summary>
        GuardStaminaCost,

        /// <summary>Seconds added to the guard hold; the guard still lasts at most 0.8 s (spec L104).</summary>
        GuardMaxHoldSec,

        /// <summary>Seconds added to the 3 s guard cooldown; never below the 0.5 s minimum recharge (spec L104, L124).</summary>
        GuardCooldownSec,

        /// <summary>Damage added to the common melee profile (spec L100).</summary>
        MeleeDamage,

        /// <summary>Stamina added to the common melee strike cost (spec L100).</summary>
        MeleeStaminaCost,

        /// <summary>Meters added to the common melee reach (spec L100).</summary>
        MeleeReach,

        /// <summary>Seconds added to the common melee windup (spec L102).</summary>
        MeleeWindupSec,

        /// <summary>Seconds added to the complete common melee cycle (spec L100).</summary>
        MeleeCycleSec,

        /// <summary>Fractional bonus to the attacker's original spell hits ("Direct player damage decreases 2%").</summary>
        SpellDamageBonus,

        /// <summary>Fractional bonus to the attacker's common melee hits ("Damage increases 4%").</summary>
        WeaponDamageBonus,
    }

    /// <summary>One modifier from a named source.</summary>
    public struct StatModifier
    {
        public StatKind Stat;

        /// <summary>Damage channel for resistance and channel-specific passive reductions (None = all channels).</summary>
        public DamageChannel Channel;

        public float Value;
        public string SourceId;

        /// <summary>Tick at which a timed modifier ends (spec L433 "duration"); 0 for one that lasts until removed.</summary>
        public int ExpireTick;

        public StatModifier(StatKind stat, float value, string sourceId, DamageChannel channel = DamageChannel.None, int expireTick = 0)
        {
            Stat = stat;
            Value = value;
            SourceId = sourceId;
            Channel = channel;
            ExpireTick = expireTick;
        }

        public bool IsTimed => ExpireTick > 0;
    }

    /// <summary>A line of the build inspector: base, raw and effective value and whether a cap binds (spec L355).</summary>
    public struct StatLine
    {
        public StatKind Stat;
        public DamageChannel Channel;

        /// <summary>Readable name for the build panel.</summary>
        public string Label;

        /// <summary>Unmodified rule value (0 for pure bonuses).</summary>
        public float Base;

        public float Raw;
        public float Effective;

        /// <summary>The binding global limit, or NaN when the stat has none.</summary>
        public float Cap;

        public bool Capped;
    }

    /// <summary>
    /// The per-player stat block (<see cref="StatBlock"/>). Kept under this name because <see cref="DuelPlayer.Stats"/>
    /// and existing callers use it.
    /// </summary>
    public sealed class StatSheet : StatBlock
    {
    }
}
