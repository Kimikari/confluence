using System;
using System.Collections.Generic;
using Confluence.Core.Random;
using Confluence.Match;
using MatchArenaRequest = Confluence.Match.ArenaRequest;

namespace Confluence.Hosting.Arenas
{
    /// <summary>
    /// Generates upcoming rounds' arenas in the background while the current round plays, so a round never waits on
    /// generation (0.3-8 s per layout). It predicts the first-pass request of a scheduled round from public match data:
    /// Match's diameter sequence and its arena seed rule (<c>MatchFlow.ArenaSeedFor</c>: the Arena stream of the match
    /// seed by round and attempt, or a Custom lobby's seed rotation, <c>ConclaveMatch.ArenaSeedFor</c>). The prediction
    /// is only a cache warm-up: <see cref="ArenaProvider"/> keys its cache by every generator input, so a stale rule
    /// costs time, never correctness or determinism. Replays and reruns are not predicted (they are rare).
    /// </summary>
    public sealed class ArenaPrefetcher
    {
        private readonly ArenaProvider _provider;
        private readonly ulong _matchSeed;
        private readonly IReadOnlyList<int> _diameters;
        private readonly IReadOnlyList<ulong> _rotation;
        private readonly HashSet<int> _started = new HashSet<int>();

        public ArenaPrefetcher(ArenaProvider provider, ulong matchSeed, IReadOnlyList<int> diameters, IReadOnlyList<ulong> seedRotation, int roundsAhead)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _diameters = diameters ?? throw new ArgumentNullException(nameof(diameters));
            _matchSeed = matchSeed;
            _rotation = seedRotation ?? new ulong[0];
            RoundsAhead = Math.Max(0, roundsAhead);
        }

        /// <summary>How many scheduled rounds ahead of the current one to generate (0 disables prefetching).</summary>
        public int RoundsAhead { get; }

        /// <summary>Rounds whose prefetch has been started.</summary>
        public int Started => _started.Count;

        /// <summary>Starts the rounds <paramref name="firstRound"/> .. firstRound + RoundsAhead - 1 not yet started.</summary>
        public void PrefetchFrom(int firstRound)
        {
            for (int round = Math.Max(1, firstRound); round < firstRound + RoundsAhead && round <= _diameters.Count; round++)
            {
                if (!_started.Add(round)) continue;
                _provider.Prefetch(Predict(round));
            }
        }

        /// <summary>The predicted first-pass request of a scheduled round.</summary>
        public MatchArenaRequest Predict(int round) =>
            new MatchArenaRequest(_matchSeed, round, 0, _diameters[round - 1], PredictSeed(_matchSeed, _rotation, round, 0));

        /// <summary>Match's arena seed rule for a pass (mirrors MatchFlow and ConclaveMatch ArenaSeedFor).</summary>
        public static ulong PredictSeed(ulong matchSeed, IReadOnlyList<ulong> rotation, int round, int attempt)
        {
            if (rotation == null || rotation.Count == 0) return RngStreams.SeedFor(matchSeed, RngStream.Arena, round, attempt);
            ulong hostSeed = rotation[(round - 1) % rotation.Count];
            return attempt == 0 ? hostSeed : SeedHash.Combine(hostSeed, (ulong)attempt);
        }
    }
}
