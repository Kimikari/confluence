using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Confluence.Arena;
using Confluence.Arena.Generation;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Random;
using Confluence.Core.Rules;
using Confluence.Hosting.Arenas;
using Confluence.Hosting.Content;
using Confluence.Match;
using MatchArenaRequest = Confluence.Match.ArenaRequest;

namespace Confluence.Hosting.Tests
{
    /// <summary>Match's arena seam over the Arena generator (spec L34, L44, L306, L316, L329).</summary>
    public sealed class ArenaProviderTests
    {
        private static ArenaProvider Provider(HostContentPolicy policy = null) =>
            new ArenaProvider(HostFixture.Generator, policy ?? Policy(), HostFixture.Arenas);

        private static HostContentPolicy Policy(IEnumerable<string> biomes = null, ImplementedContent implemented = null) =>
            HostContentPolicy.Build(HostFixture.Catalogue, implemented ?? ImplementedContent.FromShippedAssemblies(), LaboratoryOverrides.None, biomes);

        private static MatchArenaRequest SmallRequest()
        {
            ulong seed = HostFixture.SmallDuelSeed;
            return new MatchArenaRequest(seed, 1, 0, 24, RngStreams.SeedFor(seed, RngStream.Arena, 1, 0));
        }

        [Theory]
        [InlineData(MatchMode.Conclave)]
        [InlineData(MatchMode.Duel)]
        public void Match_Owns_The_Round_Diameter_In_Both_Modes(MatchMode mode)
        {
            // Match owns the round diameter: the provider always hands the generator Match's diameter, so the preview Match
            // receives matches the round it scheduled. Arena's own schedule (ArenaSchedule.DiameterForRound) repeats Match's
            // draw since the Stage 1b arena fix, so the two derivations agree on every round.
            ArenaProvider provider = Provider();
            int disagreements = 0, rounds = 0;
            for (ulong seed = 1; seed <= 24; seed++)
            {
                MatchHostSettings settings = mode == MatchMode.Conclave ? HostFixture.ConclaveSettings(seed) : HostFixture.DuelSettings(seed);
                MatchHost host = HostFixture.Host(settings);
                IReadOnlyList<int> matchDiameters = host.Flow.ArenaDiameters;
                Assert.Equal(mode == MatchMode.Conclave ? MatchRules.ConclaveScheduledRoundCapacity : MatchRules.DuelScheduledRoundCapacity, matchDiameters.Count);
                for (int round = 1; round <= matchDiameters.Count; round++)
                {
                    var request = new MatchArenaRequest(seed, round, 0, matchDiameters[round - 1], ArenaPrefetcher.PredictSeed(seed, null, round, 0));
                    Confluence.Arena.Generation.ArenaRequest generator = provider.ToGeneratorRequest(request);
                    Assert.Equal(matchDiameters[round - 1], generator.Diameter);
                    Assert.True(Schedules.IsValidArenaDiameter(generator.Diameter));
                    rounds++;
                    if (ArenaSchedule.DiameterForRound(seed, round) != matchDiameters[round - 1]) disagreements++;
                }
            }

            Assert.True(rounds > 0);
            Assert.Equal(0, disagreements);
        }

        [Fact]
        public void Generated_Preview_Carries_Public_Metadata_Only()
        {
            ArenaProvider provider = Provider();
            MatchArenaRequest request = SmallRequest();
            ArenaPreview preview = provider.RequestArena(request);
            Assert.Equal(24, preview.Diameter);
            Assert.Same(preview, provider.RequestArena(request)); // one arena per pass, shared by every pair (L34)
            Assert.Single(provider.Arenas);

            HostedArena arena = provider.Get(preview.ArenaId);
            ArenaPreviewInfo info = arena.PublicPreview;
            Assert.Equal(24, info.Diameter);
            Assert.Equal(arena.BiomeId, info.BiomeId);
            Assert.False(string.IsNullOrEmpty(info.BiomeName));
            Assert.False(string.IsNullOrEmpty(info.EnvironmentRule));   // the announced bounded rule (L335)
            Assert.NotEmpty(info.MajorTerrainIds);
            Assert.Equal(preview.MajorTerrainIds, info.MajorTerrainIds);
            Assert.Equal(preview.PropIds, info.PropIds);
            Assert.Equal(2, info.Spawns.Count);
            Assert.NotEmpty(info.Shapes);
            Assert.All(info.Props, p => Assert.False(string.IsNullOrEmpty(p.Category)));
            Assert.NotEmpty(info.Materials);
            Assert.All(info.Materials, m => Assert.False(string.IsNullOrEmpty(m.Description)));

            // No seed, layout hash, validation report, retry count, player, or standing in the public preview type.
            string[] hidden = { "Seed", "Hash", "Report", "Attempt", "Fallback", "Player", "Resolve", "Loadout" };
            foreach (PropertyInfo property in typeof(ArenaPreviewInfo).GetProperties())
            {
                Assert.DoesNotContain(hidden, h => property.Name.IndexOf(h, StringComparison.Ordinal) >= 0);
            }

            // The recipe regenerates the same layout (replays, L427).
            GeneratedArena again = HostFixture.Generator.Generate(arena.Recipe.ToGeneratorRequest(HostFixture.Catalogue));
            Assert.Equal(arena.LayoutHash, ArenaLayoutHash.Compute(again.Layout));
        }

        [Fact]
        public void Each_Pair_Fights_In_Its_Own_Copy()
        {
            ArenaProvider provider = Provider();
            HostedArena arena = provider.Get(provider.RequestArena(SmallRequest()).ArenaId);
            ArenaLayout a = arena.CreateCopy();
            ArenaLayout b = arena.CreateCopy();
            Assert.NotSame(a, b);
            Assert.Equal(ArenaLayoutHash.Compute(a), ArenaLayoutHash.Compute(b));

            a.Solids.RemoveAt(a.Solids.Count - 1); // destruction in one duel never leaks into another (L34)
            Assert.NotEqual(ArenaLayoutHash.Compute(a), ArenaLayoutHash.Compute(b));
            Assert.Equal(arena.LayoutHash, ArenaLayoutHash.Compute(arena.CreateCopy()));
        }

        [Fact]
        public void Arena_Availability_Falls_Back_To_An_Explicit_Logged_Set()
        {
            var log = new HostLog();
            HostContentPolicy policy = HostContentPolicy.Build(HostFixture.Catalogue, ImplementedContent.FromIds(new string[0]), LaboratoryOverrides.None, log: log);
            Assert.Equal(HostFixture.Catalogue.Biomes.Count, policy.BiomeIds.Count);
            Assert.Equal(HostFixture.Catalogue.Props.Count, policy.PropIds.Count);
            Assert.Contains(log.Lines, l => l.Contains("No BIO record carries [ContentBehavior]"));
            Assert.Contains(log.Lines, l => l.Contains("No PROP record carries [ContentBehavior]"));

            ImplementedContent noBehaviors = ImplementedContent.FromIds(new string[0]);
            HostContentPolicy configured = Policy(new[] { "BIO-001", "BIO-011" }, noBehaviors);
            Assert.Equal(new[] { "BIO-001", "BIO-011" }, configured.BiomeIds);
            Confluence.Arena.Generation.ArenaRequest generator = Provider(configured).ToGeneratorRequest(SmallRequest());
            Assert.True(generator.Biomes.IsAvailable("BIO-001"));
            Assert.False(generator.Biomes.IsAvailable("BIO-002"));
            Assert.Throws<ArgumentException>(() => Policy(new[] { "PROP-001" }, noBehaviors));

            // The shipped Sim registers BIO-001 and BIO-011 behaviors, so standard arenas use exactly those biomes.
            Assert.Equal(new[] { "BIO-001", "BIO-011" }, Policy(new[] { "BIO-002" }).BiomeIds);

            // Once biomes carry behaviors the registry decides, not the configured set.
            HostContentPolicy registry = Policy(new[] { "BIO-001" }, ImplementedContent.FromIds(new[] { "BIO-003", "EQP-001", "AUG-001" }));
            Assert.Equal(new[] { "BIO-003" }, registry.BiomeIds);
            Assert.Equal(new[] { "AUG-001", "EQP-001" }, registry.PoolIds);
        }

        [Fact]
        public void Lobby_Allowed_Content_Restricts_Only_The_Types_It_Names()
        {
            HostContentPolicy policy = HostContentPolicy.Build(HostFixture.Catalogue, ImplementedContent.FromIds(new string[0]),
                new LaboratoryOverrides(poolsUseWholeCatalogue: true), allowedIds: new[] { "EQP-001", "EQP-002" });
            Assert.True(policy.Pools.IsAvailable("EQP-001"));
            Assert.False(policy.Pools.IsAvailable("EQP-003"));
            Assert.True(policy.Pools.IsAvailable("AUG-001"));      // augments were not named: unrestricted
            Assert.Equal(HostFixture.Catalogue.Biomes.Count, policy.BiomeIds.Count);
            Assert.Contains(policy.Notes, n => n.Contains("allowed content"));
        }
    }
}
