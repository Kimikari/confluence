using System.Collections.Generic;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Sim.Duel;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;

namespace Confluence.Sim.Content
{
    // Catalogue behaviors (docs/ARCHITECTURE.md "Content model"). A class carrying [ContentBehavior("ID")] implements
    // exactly the record's specific behavior and one of these interfaces. Behaviors are stateless singletons shared by
    // every duel: per-duel state lives in the spell forms, deliveries, and effects they create.

    /// <summary>
    /// An exclusive base spell (SPL-). <see cref="Build"/> receives a builder already holding the record's numeric
    /// fields (mana, cooldown, cast time, range, channel; spec L525, D-011) and declares the delivery, payload, and
    /// world interaction. Every number that exists only in the record's prose is a <see cref="ProseParameter"/>
    /// quoting its source phrase.
    /// </summary>
    public interface ISpellBehavior
    {
        IReadOnlyList<ProseParameter> Parameters { get; }

        void Build(SpellFormBuilder form);
    }

    /// <summary>
    /// A wizard spell transformation (AUG- with target spells). Applied while a form is resolved, in the spec order
    /// base, delivery/trajectory, payload/reaction, equipment, mastery, caps (spec L286), ascending augment id inside
    /// a stage (L510). Structural tag changes come from <c>Core.Content.StructuralDeltas</c> (D-012).
    /// </summary>
    public interface ISpellTransform
    {
        TransformStage Stage { get; }

        IReadOnlyList<ProseParameter> Parameters { get; }

        /// <summary>Edits the form for rank 1 or 2; rank 2 keeps rank 1 except its replaced parameters (spec L541).</summary>
        void Apply(SpellFormBuilder form, int rank);
    }

    /// <summary>A wizard's signature technique and passive (WIZ-; the grammar lives in Confluence.Casting).</summary>
    public interface IWizardKitBehavior
    {
        IReadOnlyList<ProseParameter> Parameters { get; }

        /// <summary>The signature: listed activation, cost, cooldown, and windup, no grammar (spec L472).</summary>
        void BuildSignature(SpellFormBuilder form);

        /// <summary>Attaches the wizard passive to the player's effect host.</summary>
        void AttachPassive(IEffectHost host);
    }

    /// <summary>A shared augment (AUG- without target spells): attaches its effects for the given rank.</summary>
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
        void Install(DuelWorld world, BiomeDef biome);
    }

    /// <summary>An arena prop's interactive behavior (PROP-), installed for each placed instance.</summary>
    public interface IPropBehavior
    {
        void Install(DuelWorld world, PropDef definition, PropPlacement placement);
    }

    /// <summary>
    /// The per-player target that augment, equipment, and passive behaviors attach to. The effects framework
    /// (Confluence.Sim.Effects) implements it; this minimal contract lets content and the spell runtime depend on it
    /// without depending on that framework's types.
    /// </summary>
    public interface IEffectHost
    {
        int Player { get; }

        DuelWorld World { get; }

        /// <summary>A stat modifier that lasts for the duel (applied under the global caps by the stat sheet).</summary>
        void AddModifier(StatModifier modifier);

        /// <summary>Attaches a framework effect (trigger, condition, magnitude, budget group...).</summary>
        void Attach(ISimEffect effect);
    }

    /// <summary>Marker for an effect the effects framework runs; identified by its catalogue source.</summary>
    public interface ISimEffect
    {
        string SourceId { get; }
    }
}
