using System;
using System.Collections.Generic;
using Confluence.Arena.Generation;
using Confluence.Core.Content;
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
        /// Laboratory overrides used by full-match tests: no catalogue equipment or augment carries a behavior class yet,
        /// so the implemented pools are empty and the standard floors refuse to start (D-010). The tests draw from the
        /// whole catalogue and bypass the floors, which the host fingerprint records.
        /// </summary>
        public static LaboratoryOverrides WholeCatalogue { get; } = new LaboratoryOverrides(bypassEconomyFloors: true, poolsUseWholeCatalogue: true);

        public static MatchHostSettings ConclaveSettings(ulong seed, LaboratoryOverrides overrides = null, IReadOnlyList<string> wizards = null)
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
                ArenaPrefetchRounds = 16, // tests fast-forward rounds; generate every scheduled arena in the background
            };
        }

        public static MatchHostSettings DuelSettings(ulong seed, LaboratoryOverrides overrides = null, string wizard0 = "WIZ-002", string wizard1 = "WIZ-010")
        {
            return new MatchHostSettings
            {
                Mode = MatchMode.Duel,
                MatchSeed = seed,
                WizardIds = new[] { wizard0, wizard1 },
                Overrides = overrides ?? WholeCatalogue,
                RulesetFingerprint = "test-ruleset",
                ArenaPrefetchRounds = 16, // tests fast-forward rounds; generate every scheduled arena in the background
            };
        }

        public static MatchHost Host(MatchHostSettings settings, ImplementedContent implemented = null) =>
            new MatchHost(Generator, settings, implemented ?? ImplementedContent.FromShippedAssemblies(), Arenas);

        /// <summary>A host with a bot in every seat.</summary>
        public static MatchHost BotHost(MatchHostSettings settings, BotCompetence competence = BotCompetence.Adept)
        {
            MatchHost host = Host(settings);
            BotCompetence[] levels = { BotCompetence.Novice, BotCompetence.Apprentice, BotCompetence.Adept, BotCompetence.Expert };
            for (int p = 0; p < host.PlayerCount; p++)
            {
                host.AddBot(p, BotProfile.For(levels[(p + (int)competence) % levels.Length]));
            }

            return host;
        }
    }
}
