namespace Confluence.Core.Rules
{
    /// <summary>
    /// Phases of one simulation step's reaction processing, in the fixed order of spec L165 (see
    /// <see cref="GlobalRules.World.ReactionOrder"/>). The order is identical on server, clients, and replays.
    /// </summary>
    public enum ReactionPhase
    {
        /// <summary>Incoming direct impacts: deposits, shatters, prop impulses.</summary>
        DirectImpacts,

        /// <summary>Heat-water, heat-ice, and frost-water consumption (steam, melt, ice).</summary>
        MaterialConsumption,

        /// <summary>Electricity through connected wet cells, metal, and grounded conductors.</summary>
        ElectricityConnectivity,

        /// <summary>Wind translation and dispersal of steam and dust volumes.</summary>
        WindMovement,

        /// <summary>Lifetime decay of volumes, deposits, and temporary materials.</summary>
        Decay,
    }
}
