using Confluence.Core.Content;
using Confluence.Sim.Duel;
using Confluence.Sim.Players;

namespace Confluence.Sim.Effects
{
    // Behavior contracts for shared augments, equipment, and biome rules (docs/ARCHITECTURE.md "Content model"). The
    // signatures match Confluence.Sim.Content (casting/spells stage) member for member, so the merge keeps one copy:
    // see docs/notes/sim-world.md "Binding to Confluence.Sim.Content". A class carrying [ContentBehavior("ID")]
    // implements exactly its record's behavior and has a public parameterless constructor; one stateless instance
    // serves every duel, and per-duel state lives in the effects it attaches.

    /// <summary>An effect the framework runs, identified by its catalogue source (spec L433 "Effects expose ...").</summary>
    public interface ISimEffect
    {
        string SourceId { get; }
    }

    /// <summary>
    /// The per-player target that augment, equipment, and passive behaviors attach to (implemented by
    /// <see cref="EffectHost"/>).
    /// </summary>
    public interface IEffectHost
    {
        int Player { get; }

        DuelWorld World { get; }

        /// <summary>A stat modifier that lasts for the duel; the stat block applies it under the global caps.</summary>
        void AddModifier(StatModifier modifier);

        /// <summary>Attaches a framework effect: an <see cref="Effect"/> (triggered) or a <see cref="ConditionalModifier"/>.</summary>
        void Attach(ISimEffect effect);
    }

    /// <summary>A shared augment (AUG- without target spells): attaches its effects for rank 1 or 2.</summary>
    public interface IAugmentBehavior
    {
        void Attach(IEffectHost host, int rank);
    }

    /// <summary>An equipment record (EQP-): attaches its passive and active effects in the occupied slot.</summary>
    public interface IEquipmentBehavior
    {
        void Attach(IEffectHost host, EquippedItem item);
    }

    /// <summary>A biome's environment rule (BIO-), installed once when a duel world is built.</summary>
    public interface IBiomeRuleBehavior
    {
        /// <summary>Installs the rule; <paramref name="biome"/> is the catalogue record when one is available, else null.</summary>
        void Install(DuelWorld world, BiomeDef biome);
    }
}
