using Confluence.Core.Content;

namespace Confluence.Arena.Generation
{
    /// <summary>
    /// Server request for one round arena. Only match-level facts appear here: generation is independent of player
    /// builds and standings (spec L325), so no loadout, wizard, or score can influence the layout. Equal requests
    /// always yield byte-identical layouts.
    /// </summary>
    public sealed class ArenaRequest
    {
        public ulong MatchSeed { get; set; }

        /// <summary>Scheduled round number (1-based). A tied-round replay passes its original round (D-028).</summary>
        public int Round { get; set; } = 1;

        /// <summary>Forced diameter (24, 32, 40, 48), or 0 to take the round's slot in the balanced schedule (spec L306).</summary>
        public int Diameter { get; set; }

        /// <summary>Forced biome id, or null for a seeded choice among available biomes supporting the diameter.</summary>
        public string BiomeId { get; set; }

        /// <summary>Biomes the server may offer (implemented ones only, D-010).</summary>
        public IContentAvailability Biomes { get; set; } = ContentAvailability.All;

        /// <summary>Props the server may place (implemented ones only, D-010).</summary>
        public IContentAvailability Props { get; set; } = ContentAvailability.All;

        public ArenaRequest Clone() => (ArenaRequest)MemberwiseClone();
    }
}
