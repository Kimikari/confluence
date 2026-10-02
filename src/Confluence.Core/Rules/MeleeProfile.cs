using System;
using Confluence.Core.Random;
using Confluence.Core.Time;

namespace Confluence.Core.Rules
{
    /// <summary>
    /// Common melee strike profile (spec L100-L102). Damage is physical; the cycle is windup plus recovery.
    /// Unless an equipment record names a category, weapons use the light-blade profile and wands and staffs
    /// use the focus profile (spec L100).
    /// </summary>
    public readonly struct MeleeProfile : IEquatable<MeleeProfile>
    {
        public readonly string Name;
        public readonly float Damage;
        public readonly float Reach;
        public readonly float Cycle;
        public readonly float Stamina;
        public readonly float Windup;

        public MeleeProfile(string name, float damage, float reach, float cycle, float stamina, float windup)
        {
            Name = name;
            Damage = damage;
            Reach = reach;
            Cycle = cycle;
            Stamina = stamina;
            Windup = windup;
        }

        /// <summary>Time after the strike lands before the next action: the rest of the cycle (spec L102).</summary>
        public float Recovery => Cycle - Windup;

        public int WindupTicks => Ticks.FromSeconds(Windup);
        public int CycleTicks => Ticks.FromSeconds(Cycle);
        public int RecoveryTicks => CycleTicks - WindupTicks;

        /// <summary>Wand or staff butt strike.</summary>
        public static readonly MeleeProfile Focus = new MeleeProfile(
            "Focus",
            GlobalRules.Melee.FocusDamage,
            GlobalRules.Melee.FocusReach,
            GlobalRules.Melee.FocusCycleSec,
            GlobalRules.Melee.FocusStamina,
            GlobalRules.Melee.FocusWindupSec);

        public static readonly MeleeProfile LightBlade = new MeleeProfile(
            "LightBlade",
            GlobalRules.Melee.LightBladeDamage,
            GlobalRules.Melee.LightBladeReach,
            GlobalRules.Melee.LightBladeCycleSec,
            GlobalRules.Melee.LightBladeStamina,
            GlobalRules.Melee.LightBladeWindupSec);

        public static readonly MeleeProfile Heavy = new MeleeProfile(
            "Heavy",
            GlobalRules.Melee.HeavyDamage,
            GlobalRules.Melee.HeavyReach,
            GlobalRules.Melee.HeavyCycleSec,
            GlobalRules.Melee.HeavyStamina,
            GlobalRules.Melee.HeavyWindupSec);

        public static readonly MeleeProfile Spear = new MeleeProfile(
            "Spear",
            GlobalRules.Melee.SpearDamage,
            GlobalRules.Melee.SpearReach,
            GlobalRules.Melee.SpearCycleSec,
            GlobalRules.Melee.SpearStamina,
            GlobalRules.Melee.SpearWindupSec);

        /// <summary>Default profile for a Main Focus or Sidearm category when the record names none (spec L100).</summary>
        public static MeleeProfile DefaultFor(EquipmentCategory category)
        {
            return category == EquipmentCategory.Weapon ? LightBlade : Focus;
        }

        public bool Equals(MeleeProfile other)
        {
            return string.Equals(Name, other.Name, StringComparison.Ordinal)
                && Damage.Equals(other.Damage) && Reach.Equals(other.Reach) && Cycle.Equals(other.Cycle)
                && Stamina.Equals(other.Stamina) && Windup.Equals(other.Windup);
        }

        public override bool Equals(object obj) => obj is MeleeProfile other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Name == null ? 0 : (int)SeedHash.Fnv1a64(Name);
                hash = (hash * 397) ^ Damage.GetHashCode();
                hash = (hash * 397) ^ Reach.GetHashCode();
                hash = (hash * 397) ^ Cycle.GetHashCode();
                return hash;
            }
        }

        public override string ToString() => Name ?? "MeleeProfile";
    }
}
