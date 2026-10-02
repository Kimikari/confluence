using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Confluence.Match
{
    /// <summary>
    /// Request for the arena one pass of a round is fought in. Match chooses the diameter from the seeded balanced
    /// sequence (spec L306, D-028) and the arena seed; the provider generates, validates, and may retry seeds (L323).
    /// Every pair of the pass receives a copy of the same arena (L34).
    /// </summary>
    public sealed class ArenaRequest
    {
        public ArenaRequest(ulong matchSeed, int round, int attempt, int diameter, ulong arenaSeed)
        {
            if (round < 1) throw new ArgumentOutOfRangeException(nameof(round));
            if (attempt < 0) throw new ArgumentOutOfRangeException(nameof(attempt));
            MatchSeed = matchSeed;
            Round = round;
            Attempt = attempt;
            Diameter = diameter;
            ArenaSeed = arenaSeed;
        }

        public ulong MatchSeed { get; }

        /// <summary>Scheduled round number (Conclave bracket rounds continue at 13; the Duel decider is round 8).</summary>
        public int Round { get; }

        /// <summary>0 for the round's first pass; replays and void reruns count up and need a new seed (L38).</summary>
        public int Attempt { get; }

        /// <summary>24, 32, 40, or 48 meters. A replay keeps its round's diameter (D-028).</summary>
        public int Diameter { get; }

        /// <summary>Recorded seed to generate from (spec L316).</summary>
        public ulong ArenaSeed { get; }
    }

    /// <summary>
    /// Public arena information shown at reveal and during the shop (spec L44, L329): diameter, biome, major
    /// terrain, props, and the announced environment rule. Hidden gameplay state is never part of the preview.
    /// </summary>
    public sealed class ArenaPreview
    {
        public ArenaPreview(string arenaId, ulong seed, int diameter, string biomeId, string environmentRule,
            IEnumerable<string> majorTerrainIds, IEnumerable<string> propIds)
        {
            if (string.IsNullOrEmpty(arenaId)) throw new ArgumentException("An arena needs an id.", nameof(arenaId));
            ArenaId = arenaId;
            Seed = seed;
            Diameter = diameter;
            BiomeId = biomeId;
            EnvironmentRule = environmentRule;
            MajorTerrainIds = Freeze(majorTerrainIds);
            PropIds = Freeze(propIds);
        }

        /// <summary>Server handle the duel runner uses to instantiate copies of this arena.</summary>
        public string ArenaId { get; }

        /// <summary>Seed the layout was actually generated from (after any validation retries).</summary>
        public ulong Seed { get; }

        public int Diameter { get; }

        /// <summary>BIO-xxx.</summary>
        public string BiomeId { get; }

        public string EnvironmentRule { get; }

        /// <summary>MOD-xxx of the major terrain modules.</summary>
        public IReadOnlyList<string> MajorTerrainIds { get; }

        /// <summary>PROP-xxx of the placed interactive props.</summary>
        public IReadOnlyList<string> PropIds { get; }

        public override string ToString() =>
            ArenaId + " seed=" + Seed.ToString("X16") + " d=" + Diameter + " biome=" + (BiomeId ?? "-") +
            " terrain=[" + string.Join(",", MajorTerrainIds) + "] props=[" + string.Join(",", PropIds) + "]";

        private static IReadOnlyList<string> Freeze(IEnumerable<string> ids) =>
            new ReadOnlyCollection<string>(ids == null ? new List<string>() : new List<string>(ids));
    }

    /// <summary>Generates (or fetches) the shared arena for one pass of a round. Implemented by the server's Arena adapter.</summary>
    public interface IArenaProvider
    {
        ArenaPreview RequestArena(ArenaRequest request);
    }
}
