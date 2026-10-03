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
    /// <para>
    /// The match owns the round diameter: Confluence.Match draws it once per match and the arena provider passes it
    /// as <see cref="ArenaRequest.Diameter"/>. <see cref="DiameterForRound"/> repeats that derivation exactly, so
    /// tools, previews, and unforced requests see the diameter the match will ask for.
    /// </para>
    /// </summary>
    public static class ArenaSchedule
    {
        /// <summary>
        /// Arena-stream round of the match-wide diameter schedule (with <see cref="DiameterScheduleSub"/>: the stream
        /// Match draws from). Stream round 0 never collides with a scheduled round's draws (rounds are 1-based).
        /// </summary>
        public const int DiameterScheduleRound = 0;

        /// <summary>Arena-stream sub-index of the match-wide diameter schedule (see <see cref="DiameterScheduleRound"/>).</summary>
        public const int DiameterScheduleSub = 0;

        /// <summary>Arena-stream sub-index of a round's biome draw.</summary>
        public const int BiomeSub = 2;

        /// <summary>Arena-stream sub-index of the first layout attempt; retry k uses AttemptSubBase + k.</summary>
        public const int AttemptSubBase = 16;

        /// <summary>
        /// Diameters of scheduled rounds 1..roundCount (index 0 is round 1) from the balanced four-round sets (spec
        /// L306, D-028), drawn exactly as Match draws them. Each set takes one draw in order, so a round's diameter
        /// does not depend on the length asked for: Conclave's 15 rounds and Duel's 8 share their first 8 diameters.
        /// </summary>
        public static int[] DiameterSequence(ulong matchSeed, int roundCount)
        {
            if (roundCount < 0) throw new ArgumentOutOfRangeException(nameof(roundCount), roundCount, "Round count cannot be negative.");
            Rng rng = RngStreams.For(matchSeed, RngStream.Arena, DiameterScheduleRound, DiameterScheduleSub);
            return Schedules.ArenaDiameterSequence(rng, roundCount);
        }

        /// <summary>Diameter of a scheduled round (1-based): the match's diameter for that round in either mode.</summary>
        public static int DiameterForRound(ulong matchSeed, int round)
        {
            if (round < 1) throw new ArgumentOutOfRangeException(nameof(round), round, "Rounds are 1-based.");
            int setRounds = GlobalRules.Arena.DiameterSetRounds;
            return DiameterSequence(matchSeed, (round + setRounds - 1) / setRounds * setRounds)[round - 1];
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
