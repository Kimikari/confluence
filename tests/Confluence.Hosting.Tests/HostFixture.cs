using System;
using System.Collections.Generic;
using Confluence.Arena.Generation;
using Confluence.Core.Content;
using Confluence.Core.Random;
using Confluence.Core.Rules;
using Confluence.Hosting.Arenas;
using Confluence.Hosting.Bots;
using Confluence.Hosting.Content;
using Confluence.Match;

namespace Confluence.Hosting.Tests
{
    /// <summary>Shared catalogue, generator, and released-arena cache (generation is deterministic, so tests may share layouts).</summary>
    internal static class HostFixture
    {
        private static readonly Lazy<Catalogue> LazyCatalogue = new Lazy<Catalogue>(() => Catalogue.Load(Catalogue.FindDefaultPath()));
        private static readonly Lazy<ArenaGenerator> LazyGenerator = new Lazy<ArenaGenerator>(() => new ArenaGenerator(LazyCatalogue.Value));

        public static Catalogue Catalogue => LazyCatalogue.Value;
        public static ArenaGenerator Generator => LazyGenerator.Value;
        public static ArenaCache Arenas { get; } = new ArenaCache();

        /// <summary>Wizards for an eight-seat lobby: both M1 wizards twice, plus four others with the generic cast script.</summary>
        public static readonly string[] EightWizards = { "WIZ-002", "WIZ-010", "WIZ-002", "WIZ-010", "WIZ-001", "WIZ-003", "WIZ-004", "WIZ-005" };

        /// <summary>
        /// Laboratory overrides used by match tests: no catalogue equipment or augment carries a behavior class yet, so
        /// the implemented pools are empty and the standard floors refuse to start (D-010). These tests draw from the
        /// whole catalogue and bypass the floors; the host fingerprint records both overrides.
        /// </summary>
        public static LaboratoryOverrides WholeCatalogue { get; } = new LaboratoryOverrides(bypassEconomyFloors: true, poolsUseWholeCatalogue: true);

        /// <summary>A Duel seed whose first round is fought on a 24 m arena (fast to generate in Debug builds).</summary>
        public static readonly ulong SmallDuelSeed = SeedWithFirstDiameter(MatchMode.Duel, 24, 100);

        /// <summary>A Conclave seed whose first round is fought on a 24 m arena.</summary>
        public static readonly ulong SmallConclaveSeed = SeedWithFirstDiameter(MatchMode.Conclave, 24, 200);

        public static MatchHostSettings ConclaveSettings(ulong seed, LaboratoryOverrides overrides = null, IReadOnlyList<string> wizards = null, int prefetchRounds = 16)
        {
            wizards = wizards ?? EightWizards;
            return new MatchHostSettings
            {
                Mode = MatchMode.Conclave,
                MatchSeed = seed,
                Conclave = wizards.Count == GlobalRules.Match.LobbySize ? Confluence.Match.ConclaveSettings.Unranked : Confluence.Match.ConclaveSettings.Custom(wizards.Count),
                WizardIds = wizards,
                Overrides = overrides ?? WholeCatalogue,
                RulesetFingerprint = "test-ruleset",
                ArenaPrefetchRounds = prefetchRounds, // full-match tests fast-forward rounds: generate the schedule in the background
            };
        }

        public static MatchHostSettings DuelSettings(ulong seed, LaboratoryOverrides overrides = null, string wizard0 = "WIZ-002", string wizard1 = "WIZ-010", int prefetchRounds = 1)
        {
            return new MatchHostSettings
            {
                Mode = MatchMode.Duel,
                MatchSeed = seed,
                WizardIds = new[] { wizard0, wizard1 },
                Overrides = overrides ?? WholeCatalogue,
                RulesetFingerprint = "test-ruleset",
                ArenaPrefetchRounds = prefetchRounds,
            };
        }

        public static MatchHost Host(MatchHostSettings settings, ImplementedContent implemented = null) =>
            new MatchHost(Generator, settings, implemented ?? ImplementedContent.FromShippedAssemblies(), Arenas);

        /// <summary>A host with a bot in every seat, cycling through the four competence settings.</summary>
        public static MatchHost BotHost(MatchHostSettings settings, BotCompetence first = BotCompetence.Novice)
        {
            MatchHost host = Host(settings);
            for (int p = 0; p < host.PlayerCount; p++)
            {
                host.AddBot(p, BotProfile.For((BotCompetence)(((int)first + p) % GlobalRules.Match.BotCompetenceLevels)));
            }

            return host;
        }

        /// <summary>Ticks until the match reaches <paramref name="phase"/> (or fails after a cap).</summary>
        public static void RunUntilPhase(MatchHost host, MatchPhase phase, int maxTicks = 20000)
        {
            for (int i = 0; i < maxTicks && host.Flow.Phase != phase; i++) host.Tick();
            Assert.Equal(phase, host.Flow.Phase);
        }

        /// <summary>The first seed at or after <paramref name="start"/> whose round-1 diameter (Match's sequence) is <paramref name="diameter"/>.</summary>
        public static ulong SeedWithFirstDiameter(MatchMode mode, int diameter, ulong start)
        {
            int capacity = mode == MatchMode.Conclave ? MatchRules.ConclaveScheduledRoundCapacity : MatchRules.DuelScheduledRoundCapacity;
            for (ulong seed = start; seed < start + 1000; seed++)
            {
                int[] sequence = Schedules.ArenaDiameterSequence(RngStreams.For(seed, RngStream.Arena, 0), capacity);
                if (sequence[0] == diameter) return seed;
            }

            throw new InvalidOperationException("No seed found.");
        }
    }
}
