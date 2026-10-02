using System;
using System.Collections.Generic;
using System.Globalization;
using Confluence.Arena.Generation;
using Confluence.Core.Content;
using Confluence.Hosting.Content;
using Confluence.Match;
using GeneratorRequest = Confluence.Arena.Generation.ArenaRequest;
using MatchArenaRequest = Confluence.Match.ArenaRequest;

namespace Confluence.Hosting.Arenas
{
    /// <summary>
    /// Match's <see cref="IArenaProvider"/> over the Arena generator. One arena is generated per pass and shared by every
    /// pair (spec L34). Match owns the round diameter: its balanced sequence (D-028, D-095) is passed to the generator
    /// as a forced diameter, so the generator's own schedule (<c>ArenaSchedule.DiameterForRound</c>) is never consulted.
    /// The generator's match seed is the arena seed Match recorded for the pass, which Match derives from the match seed,
    /// the round, and the attempt (a replay or rerun gets a new seed, L38); the generator then picks the biome and its
    /// retry seeds from it (L316, L323). Biome and prop availability come from the host content policy (D-010).
    /// </summary>
    public sealed class ArenaProvider : IArenaProvider
    {
        private readonly ArenaGenerator _generator;
        private readonly Catalogue _catalogue;
        private readonly HostContentPolicy _content;
        private readonly ArenaCache _cache;
        private readonly IContentAvailability _props;
        private readonly Dictionary<string, HostedArena> _byId = new Dictionary<string, HostedArena>(StringComparer.Ordinal);
        private readonly List<HostedArena> _ordered = new List<HostedArena>();

        public ArenaProvider(ArenaGenerator generator, HostContentPolicy content, ArenaCache cache = null)
        {
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _catalogue = generator.Content.Catalogue;
            _cache = cache ?? new ArenaCache();
            _props = ArenaRecipe.PropsAvailability(_catalogue, content.PropIds);
        }

        /// <summary>Arenas released by this provider, in request order.</summary>
        public IReadOnlyList<HostedArena> Arenas => _ordered.AsReadOnly();

        public ArenaGenerator Generator => _generator;

        public ArenaPreview RequestArena(MatchArenaRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            GeneratorRequest generatorRequest = ToGeneratorRequest(request);
            string key = CacheKey(request);
            GeneratedArena generated = _cache.GetOrGenerate(key, () => _generator.Generate(generatorRequest));
            if (generated.Layout.Diameter != request.Diameter)
            {
                throw new InvalidOperationException("Arena generator returned " + generated.Layout.Diameter + " m for a " + request.Diameter + " m request.");
            }

            string arenaId = "arena:r" + request.Round.ToString(CultureInfo.InvariantCulture) + ".a" +
                request.Attempt.ToString(CultureInfo.InvariantCulture) + ":" + request.ArenaSeed.ToString("X16", CultureInfo.InvariantCulture);
            if (_byId.TryGetValue(arenaId, out HostedArena existing)) return existing.Preview;

            var recipe = new ArenaRecipe(request.ArenaSeed, request.Round, request.Diameter, generated.Layout.BiomeId, _content.PropIds);
            ArenaPreviewInfo info = ArenaPreviewInfo.Build(arenaId, generated.Layout, _catalogue);
            var preview = new ArenaPreview(arenaId, generated.Layout.Seed, generated.Layout.Diameter, generated.Layout.BiomeId,
                generated.Layout.EnvironmentRule, info.MajorTerrainIds, info.PropIds);
            var hosted = new HostedArena(arenaId, request, recipe, generated, preview, info);
            _byId.Add(arenaId, hosted);
            _ordered.Add(hosted);
            return preview;
        }

        public HostedArena Get(string arenaId)
        {
            if (arenaId == null || !_byId.TryGetValue(arenaId, out HostedArena arena))
            {
                throw new KeyNotFoundException("Arena " + arenaId + " was not released by this provider.");
            }

            return arena;
        }

        public bool TryGet(string arenaId, out HostedArena arena) => _byId.TryGetValue(arenaId ?? string.Empty, out arena);

        /// <summary>
        /// The generator request for a Match request: Match's diameter (forced), Match's recorded pass seed as the
        /// generator's match seed, the scheduled round, and the policy's biome and prop availability.
        /// </summary>
        public GeneratorRequest ToGeneratorRequest(MatchArenaRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return new GeneratorRequest
            {
                MatchSeed = request.ArenaSeed,
                Round = request.Round,
                Diameter = request.Diameter,
                Biomes = _content.Biomes,
                Props = _props,
            };
        }

        private string CacheKey(MatchArenaRequest request) =>
            request.ArenaSeed.ToString("X16", CultureInfo.InvariantCulture) + "|" + request.Round.ToString(CultureInfo.InvariantCulture) + "|" +
            request.Diameter.ToString(CultureInfo.InvariantCulture) + "|" + string.Join(",", _content.BiomeIds) + "|" + string.Join(",", _content.PropIds);
    }
}
