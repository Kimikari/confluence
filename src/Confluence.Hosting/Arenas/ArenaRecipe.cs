using System;
using System.Collections.Generic;
using System.Globalization;
using Confluence.Core.Content;
using GeneratorRequest = Confluence.Arena.Generation.ArenaRequest;

namespace Confluence.Hosting.Arenas
{
    /// <summary>
    /// Everything the Arena generator needs to rebuild one released layout byte for byte (spec L316, L427): the root
    /// seed Match recorded for the pass, the scheduled round, Match's diameter, the biome actually chosen, and the prop
    /// availability set. Replays store it with the layout hash.
    /// </summary>
    public sealed class ArenaRecipe
    {
        public ArenaRecipe(ulong rootSeed, int round, int diameter, string biomeId, IEnumerable<string> availablePropIds)
        {
            if (round < 1) throw new ArgumentOutOfRangeException(nameof(round));
            RootSeed = rootSeed;
            Round = round;
            Diameter = diameter;
            BiomeId = biomeId;
            var props = new SortedSet<string>(StringComparer.Ordinal);
            if (availablePropIds != null)
                foreach (string id in availablePropIds)
                    if (!string.IsNullOrEmpty(id)) props.Add(id);
            AvailablePropIds = new List<string>(props).AsReadOnly();
        }

        /// <summary>Match's recorded arena seed of the pass, used as the generator's match seed.</summary>
        public ulong RootSeed { get; }

        public int Round { get; }

        /// <summary>Owned by Match (one balanced sequence per match, D-028, D-095).</summary>
        public int Diameter { get; }

        /// <summary>Biome the seeded choice produced (null before the first generation).</summary>
        public string BiomeId { get; }

        public IReadOnlyList<string> AvailablePropIds { get; }

        /// <summary>
        /// The generator request that rebuilds the layout. A prop set covering the whole catalogue is passed as
        /// <see cref="ContentAvailability.All"/>, which lets the generator reuse its cached fallbacks (identical layouts).
        /// </summary>
        public GeneratorRequest ToGeneratorRequest(Catalogue catalogue, IContentAvailability biomes = null)
        {
            if (catalogue == null) throw new ArgumentNullException(nameof(catalogue));
            return new GeneratorRequest
            {
                MatchSeed = RootSeed,
                Round = Round,
                Diameter = Diameter,
                BiomeId = BiomeId,
                Biomes = biomes ?? ContentAvailability.All,
                Props = PropsAvailability(catalogue, AvailablePropIds),
            };
        }

        internal static IContentAvailability PropsAvailability(Catalogue catalogue, IReadOnlyList<string> propIds)
        {
            if (propIds.Count == catalogue.Props.Count)
            {
                bool all = true;
                foreach (PropDef def in catalogue.Props)
                    if (!Contains(propIds, def.Id)) all = false;
                if (all) return ContentAvailability.All;
            }

            return ContentAvailability.FromIds(propIds);
        }

        private static bool Contains(IReadOnlyList<string> ids, string id)
        {
            for (int i = 0; i < ids.Count; i++)
                if (string.Equals(ids[i], id, StringComparison.Ordinal)) return true;
            return false;
        }

        public string CanonicalText() =>
            "seed=" + RootSeed.ToString("X16", CultureInfo.InvariantCulture) + ";round=" + Round + ";d=" + Diameter +
            ";biome=" + (BiomeId ?? "-") + ";props=" + string.Join(",", AvailablePropIds);

        public override string ToString() => CanonicalText();
    }
}
