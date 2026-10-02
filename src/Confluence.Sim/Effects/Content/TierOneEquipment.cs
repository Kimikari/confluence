using Confluence.Core.Content;
using Confluence.Sim.Players;

namespace Confluence.Sim.Effects.Content
{
    /// <summary>
    /// EQP-051 Training Cuirass (Armor, tier 1, passive, unique group health_armor): "Maximum health increases 15."
    /// Drawback: "Sprint stamina consumption increases 1/s." Maximum health stays inside the 450 cap (spec L114).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class TrainingCuirass : IEquipmentBehavior
    {
        public const string Id = "EQP-051";
        public const float MaxHealthBonus = 15f;        // "Maximum health increases 15."
        public const float SprintStaminaPerSecond = 1f; // "Sprint stamina consumption increases 1/s."

        public void Attach(IEffectHost host, EquippedItem item)
        {
            ContentArgs.Require(host);
            host.AddModifier(new StatModifier(StatKind.MaxHealthBonus, MaxHealthBonus, Id));
            host.AddModifier(new StatModifier(StatKind.SprintStaminaPerSecond, SprintStaminaPerSecond, Id));
        }
    }

    /// <summary>
    /// EQP-096 Pocket Reservoir (Trinket, tier 1, passive, unique group mana_trinket): "Maximum mana increases 8."
    /// Drawback: "Maximum stamina decreases 4." Static capacity, not regeneration (spec L579).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class PocketReservoir : IEquipmentBehavior
    {
        public const string Id = "EQP-096";
        public const float MaxManaBonus = 8f;     // "Maximum mana increases 8."
        public const float MaxStaminaBonus = -4f; // "Maximum stamina decreases 4."

        public void Attach(IEffectHost host, EquippedItem item)
        {
            ContentArgs.Require(host);
            host.AddModifier(new StatModifier(StatKind.MaxManaBonus, MaxManaBonus, Id));
            host.AddModifier(new StatModifier(StatKind.MaxStaminaBonus, MaxStaminaBonus, Id));
        }
    }

    /// <summary>
    /// EQP-066 Apprentice Mantle (Magical clothing, tier 1, passive, unique group regen_clothing): "Mana regeneration
    /// increases 8%, within +40% cap." The +40% cap is the global 14/s ceiling on the 10/s base (spec L78). Drawback:
    /// "Maximum health decreases 8."
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class ApprenticeMantle : IEquipmentBehavior
    {
        public const string Id = "EQP-066";
        public const float ManaRegenPercent = 0.08f; // "Mana regeneration increases 8%"
        public const float MaxHealthBonus = -8f;     // "Maximum health decreases 8."

        public void Attach(IEffectHost host, EquippedItem item)
        {
            ContentArgs.Require(host);
            host.AddModifier(new StatModifier(StatKind.ManaRegenPercent, ManaRegenPercent, Id));
            host.AddModifier(new StatModifier(StatKind.MaxHealthBonus, MaxHealthBonus, Id));
        }
    }
}
