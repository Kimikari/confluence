using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Core.Random;
using Confluence.Core.Rules;

namespace Confluence.Arena.Generation
{
    /// <summary>
    /// Step 1 of the pipeline (spec L316): size, biome, environment modifier, and recorded seed, all drawn from the
    /// match's Arena stream. Sub-indices keep the draws independent: the diameter schedule never shifts when a biome
    /// filter changes, and retries never change the size or biome.
    /// </summary>
    public static class ArenaSchedule
    {
        /// <summary>Arena-stream sub-index of the match-wide diameter schedule (drawn once, at round 0).</summary>
        public const int DiameterScheduleSub = 1;

        /// <summary>Arena-stream sub-index of a round's biome draw.</summary>
        public const int BiomeSub = 2;

        /// <summary>Arena-stream sub-index of the first layout attempt; retry k uses AttemptSubBase + k.</summary>
        public const int AttemptSubBase = 16;

        /// <summary>Rounds covered by one schedule draw: 12 regular rounds plus at most 3 bracket rounds, rounded up to a set.</summary>
        public const int ScheduledRoundCapacity = 16;

        /// <summary>Diameter of a scheduled round from the balanced four-round sets (spec L306, D-028).</summary>
        public static int DiameterForRound(ulong matchSeed, int round)
        {
            if (round < 1) throw new ArgumentOutOfRangeException(nameof(round), round, "Rounds are 1-based.");
            int count = Math.Max(ScheduledRoundCapacity, ((round + 3) / 4) * 4);
            Rng rng = RngStreams.For(matchSeed, RngStream.Arena, 0, DiameterScheduleSub);
            return Schedules.ArenaDiameterSequence(rng, count)[round - 1];
        }

        /// <summary>Biomes that support a diameter and pass the availability filter, in catalogue id order.</summary>
        public static List<BiomeDef> CandidateBiomes(Catalogue catalogue, int diameter, IContentAvailability availability)
        {
            availability = availability ?? ContentAvailability.All;
            var result = new List<BiomeDef>();
            for (int i = 0; i < catalogue.Biomes.Count; i++)
            {
                BiomeDef biome = catalogue.Biomes[i];
                if (biome.SupportsSize(diameter) && availability.IsAvailable(biome.Id))
                {
                    result.Add(biome);
                }
            }

            result.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return result;
        }

        /// <summary>Seeded uniform biome choice for a round, or null when no biome is available.</summary>
        public static BiomeDef BiomeForRound(Catalogue catalogue, ulong matchSeed, int round, int diameter, IContentAvailability availability)
        {
            List<BiomeDef> candidates = CandidateBiomes(catalogue, diameter, availability);
            if (candidates.Count == 0)
            {
                return null;
            }

            Rng rng = RngStreams.For(matchSeed, RngStream.Arena, round, BiomeSub);
            return candidates[rng.NextInt(candidates.Count)];
        }

        /// <summary>Recorded layout seed of attempt k (0-based) of a round (spec L316, L323).</summary>
        public static ulong AttemptSeed(ulong matchSeed, int round, int attempt)
        {
            return RngStreams.SeedFor(matchSeed, RngStream.Arena, round, AttemptSubBase + attempt);
        }
    }
}
