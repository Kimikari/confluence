using Confluence.Core.Content;
using Confluence.Sim.Effects;
using Confluence.Sim.Players;

namespace Confluence.Sim.Content.Augments
{
    /// <summary>
    /// AUG-145 Large Vessel (Silver, resource and recovery, condition "Always."): rank 1 "Maximum mana increases 8.",
    /// rank 2 "Maximum mana increases 12." (rank 2 replaces the rank 1 amount, spec L541). Drawback at both ranks:
    /// "Maximum stamina decreases 4."
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class LargeVessel : IAugmentBehavior
    {
        public const string Id = "AUG-145";
        public const float Rank1MaxMana = 8f;    // "Maximum mana increases 8."
        public const float Rank2MaxMana = 12f;   // "Maximum mana increases 12."
        public const float MaxStaminaBonus = -4f; // "Maximum stamina decreases 4."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.AddModifier(new StatModifier(StatKind.MaxManaBonus, rank >= 2 ? Rank2MaxMana : Rank1MaxMana, Id));
            host.AddModifier(new StatModifier(StatKind.MaxStaminaBonus, MaxStaminaBonus, Id));
        }
    }

    /// <summary>
    /// AUG-169 Healthy Habit (Silver, defense and commitment, condition "Always."): rank 1 "Maximum health increases
    /// 12.", rank 2 "Maximum health increases 20."; inside the 450 maximum-health cap (spec L114). Drawback:
    /// "Maximum mana decreases 3."
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class HealthyHabit : IAugmentBehavior
    {
        public const string Id = "AUG-169";
        public const float Rank1MaxHealth = 12f; // "Maximum health increases 12."
        public const float Rank2MaxHealth = 20f; // "Maximum health increases 20."
        public const float MaxManaBonus = -3f;   // "Maximum mana decreases 3."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.AddModifier(new StatModifier(StatKind.MaxHealthBonus, rank >= 2 ? Rank2MaxHealth : Rank1MaxHealth, Id));
            host.AddModifier(new StatModifier(StatKind.MaxManaBonus, MaxManaBonus, Id));
        }
    }

    /// <summary>
    /// AUG-175 Opening Ward (Silver, defense and commitment, condition "Round starts."): rank 1 "Gain 12 shield until
    /// broken.", rank 2 "Gain 20 shield until broken." A round-start reserve shield: issued once, lasts until broken or
    /// the duel ends, never regenerates, and shares the 100 shield cap with every other shield (spec L114, L579).
    /// Drawback: "Maximum health decreases 5."
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class OpeningWard : IAugmentBehavior
    {
        public const string Id = "AUG-175";
        public const float Rank1Shield = 12f;   // "Gain 12 shield until broken."
        public const float Rank2Shield = 20f;   // "Gain 20 shield until broken."
        public const float MaxHealthBonus = -5f; // "Maximum health decreases 5."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            var ward = new Effect(Id, EffectTrigger.RoundStart) { Label = "round-start reserve shield", MaxActivations = 1 };
            ward.Actions.Add(new GrantShieldAction(rank >= 2 ? Rank2Shield : Rank1Shield, 0, reserve: true));
            host.Attach(ward);
            host.AddModifier(new StatModifier(StatKind.MaxHealthBonus, MaxHealthBonus, Id));
        }
    }
}
