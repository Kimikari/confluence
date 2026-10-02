using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Arena;
using Confluence.Core.Rules;
using Confluence.Hosting.Duels;
using Confluence.Hosting.Messages;
using Confluence.Match;
using MatchDuelEndReason = Confluence.Match.DuelEndReason;

namespace Confluence.Hosting.Tests
{
    /// <summary>
    /// Whole matches played by bots through the host at fast-forward: every round, shop, draft, alcove, and duel runs
    /// through the real adapters (Arena generator, Economy, Sim). These use the Laboratory overrides of
    /// <see cref="HostFixture.WholeCatalogue"/> because no equipment or augment is implemented yet (D-010).
    /// </summary>
    public sealed class FullMatchTests
    {
        private const ulong ConclaveSeed = 7919;
        private const int TickCap = 60 * 60 * 120; // two hours of match time

        [Fact]
        public void Eight_Bot_Conclave_Runs_End_To_End_With_Consistent_Resolve_And_Placements()
        {
            MatchHost host = HostFixture.BotHost(HostFixture.ConclaveSettings(ConclaveSeed));
            var layoutsByDuel = new Dictionary<int, ArenaLayout>();
            host.DuelStarted += d => layoutsByDuel.Add(d.DuelId, d.World.Layout);
            var messages = new List<HostMessage>();
            host.Outbox.Queued += messages.Add;

            HostStartResult start = host.Start();
            Assert.True(start.Started);
            Assert.Empty(start.Gaps); // the whole catalogue meets the floors; only the implemented pools would not (D-010)
            host.RunUntilFinished(TickCap);
            Assert.True(host.IsFinished);

            MatchFlow flow = host.Flow;
            MatchEventLog events = flow.Events;
            Assert.Empty(events.OfType<RoundVoidedEvent>());

            // Every duel resolved by death or by time (or simultaneous lethal), with a report and a complete replay.
            List<DuelResolvedEvent> resolved = events.OfType<DuelResolvedEvent>();
            Assert.NotEmpty(resolved);
            Assert.All(resolved, e => Assert.Contains(e.Outcome.Reason, new[] { MatchDuelEndReason.Death, MatchDuelEndReason.Time, MatchDuelEndReason.SimultaneousLethal }));
            Assert.Contains(resolved, e => e.Outcome.Reason == MatchDuelEndReason.Death);
            Assert.Contains(resolved, e => e.Outcome.Reason == MatchDuelEndReason.Time);
            Assert.Equal(resolved.Count, host.Duels.Reports.Count);
            Assert.All(host.Duels.Reports, r => Assert.True(r.Replay.IsComplete));
            Assert.All(resolved, e => Assert.True(e.Outcome.ResolvedCombatTick <= flow.Timings.CombatMaxTicks));

            // Shops every round for every active player; drafts exactly on the Conclave draft rounds (L216-L225).
            var pairings = events.OfType<PairingsAnnouncedEvent>().Where(p => p.PassKind == PassKind.Original).ToList();
            foreach (PairingsAnnouncedEvent p in pairings)
            {
                var active = new HashSet<int>(p.Pairs.SelectMany(s => new[] { s.Side0Player, s.Side1Player }));
                foreach (int bye in p.Byes ?? new int[0]) active.Add(bye);
                var shops = events.OfType<ShopOpenedEvent>().Where(s => s.Round == p.Round && s.BracketRound == p.BracketRound).Select(s => s.Opening.Player).ToList();
                Assert.Equal(active.OrderBy(x => x), shops.OrderBy(x => x));
                var drafts = events.OfType<DraftOpenedEvent>().Where(d => d.Round == p.Round).ToList();
                bool draftRound = Schedules.DraftRarity(MatchMode.Conclave, p.Round, p.BracketRound > 0, out Rarity rarity);
                if (draftRound)
                {
                    Assert.Equal(active.OrderBy(x => x), drafts.Select(d => d.Player).OrderBy(x => x));
                    Assert.All(drafts, d => Assert.Equal(rarity, d.Rarity));
                }
                else
                {
                    Assert.Empty(drafts);
                }
            }

            Assert.Contains(events.OfType<DraftOpenedEvent>(), d => d.Round == 1);
            Assert.DoesNotContain(events.OfType<DraftOpenedEvent>(), d => d.Round == 2);

            // Bots shopped and drafted through the command API: purchases and augments show in finalized loadouts.
            List<LoadoutFinalizedEvent> loadouts = events.OfType<LoadoutFinalizedEvent>();
            Assert.Contains(loadouts, l => l.Loadout.EquipmentIds.Count > 0);
            Assert.Contains(loadouts, l => l.Loadout.AugmentIds.Count > 0);
            Assert.Contains(messages, m => m is CommandResultMessage c && c.Result.Succeeded && c.Command.Kind == Commands.CommandKind.DraftSelect);
            Assert.All(messages.OfType<CommandResultMessage>(), m => Assert.NotEqual(Commands.CommandCode.RateLimited, m.Result.Code));

            // Resolve (L36): losers and both drawing players lose ResolveLoss(round), clamped at zero; winners and byes keep it.
            foreach (RoundResult round in flow.RoundResults.Where(r => r.BracketRound == 0))
            {
                foreach (RoundResultEntry entry in round.Entries)
                {
                    int loss = Math.Min(entry.ResolveBefore, Schedules.ResolveLoss(round.Round));
                    int expected = entry.Outcome == RoundOutcome.Loss || entry.Outcome == RoundOutcome.Draw ? -loss : 0;
                    Assert.Equal(expected, entry.ResolveDelta);
                    Assert.Equal(entry.ResolveAfter == 0, entry.Eliminated);
                }
            }

            // Placements (L40): every player exactly once, tied blocks of the right size, positions 1..8 covered.
            MatchResult result = flow.Result;
            Assert.NotNull(result);
            Assert.Equal(8, result.Placements.Count);
            Assert.Equal(Enumerable.Range(0, 8), result.Placements.Select(p => p.Player).OrderBy(x => x));
            Assert.Equal(36.0, result.Placements.Sum(p => p.Position), 6);
            foreach (var block in result.Placements.GroupBy(p => (p.FirstPosition, p.LastPosition)))
            {
                Assert.Equal(block.Key.LastPosition - block.Key.FirstPosition + 1, block.Count());
            }

            foreach (MatchPlayer player in flow.Players)
            {
                bool eliminatedEarly = events.OfType<PlayerEliminatedEvent>().Any(e => e.Placement.Player == player.Id);
                if (eliminatedEarly) Assert.Equal(0, player.Resolve);
            }

            Assert.False(result.CountsForRating);
            Assert.False(host.CountsForRating);

            // One arena per pass shared by every pair (L34), each duel on its own copy.
            foreach (var pass in events.OfType<DuelStartedEvent>().GroupBy(e => (e.Round, e.BracketRound, e.Attempt)))
            {
                Assert.Single(pass.Select(e => e.Setup.Arena.ArenaId).Distinct());
                var copies = pass.Select(e => layoutsByDuel[e.Setup.DuelId]).ToList();
                Assert.Equal(copies.Count, copies.Distinct().Count());
                ulong hash = host.Arenas.Get(pass.First().Setup.Arena.ArenaId).LayoutHash;
                Assert.All(pass, e => Assert.Equal(e.Setup.Arena.Diameter, flow.ArenaDiameters[e.Round - 1]));
                Assert.NotEqual(0UL, hash);
            }

            // Public previews accompany every reveal; private shop state only reaches its own player.
            Assert.Equal(events.OfType<ArenaRevealedEvent>().Count, messages.OfType<ArenaPreviewMessage>().Count());
            Assert.All(messages.OfType<ShopStateMessage>(), m => Assert.Equal(m.Player, m.Audience));
            Assert.All(messages.OfType<DraftStateMessage>(), m => Assert.Equal(m.Player, m.Audience));
        }

        [Fact]
        public void Bot_Duel_Plays_A_Full_Best_Of_Seven()
        {
            MatchHost host = HostFixture.BotHost(HostFixture.DuelSettings(HostFixture.SmallDuelSeed, prefetchRounds: 8));
            Assert.True(host.Start().Started);
            host.RunUntilFinished(TickCap);
            Assert.True(host.IsFinished);

            DuelMatch duel = host.Duel;
            MatchResult result = duel.Result;
            float s0 = duel.Score(0), s1 = duel.Score(1);
            Assert.True(s0 >= GlobalRules.Match.DuelWinsToClinch || s1 >= GlobalRules.Match.DuelWinsToClinch || duel.IsRecordedDraw || duel.IsDecider);
            Assert.Equal(result.DuelHalfWins0, duel.Player(0).DuelHalfWins);
            Assert.Equal(result.DuelHalfWins1, duel.Player(1).DuelHalfWins);
            if (!duel.IsRecordedDraw) Assert.Equal(s0 > s1 ? 0 : 1, result.Champion);

            // Duel drafts: rounds 1-2 Silver, 3-4 Gold, 5-6 Prismatic (L225); shops every round (L65).
            foreach (DraftOpenedEvent d in duel.Events.OfType<DraftOpenedEvent>())
            {
                Assert.True(Schedules.DraftRarity(MatchMode.Duel, d.Round, out Rarity rarity));
                Assert.Equal(rarity, d.Rarity);
            }

            int rounds = duel.Events.OfType<PairingsAnnouncedEvent>().Where(p => p.PassKind == PassKind.Original).Select(p => p.Round).Distinct().Count();
            Assert.True(rounds >= 4 && rounds <= GlobalRules.Match.DuelBestOf + 1);
            Assert.Equal(2 * rounds, duel.Events.OfType<ShopOpenedEvent>().Count);
            Assert.Equal(duel.Events.OfType<DuelResolvedEvent>().Count, host.Duels.Reports.Count);

            // Income (L193): 600 first, then 500 plus 100 after a non-replay win; Match decides it, the economy credits it.
            foreach (ShopOpenedEvent shop in duel.Events.OfType<ShopOpenedEvent>())
            {
                Assert.Equal(shop.Opening.IsFirstShop ? GlobalRules.Economy.FirstShopIncome : GlobalRules.Economy.LaterShopIncome, shop.Opening.BaselineIncome);
                Assert.True(shop.Opening.WinnerBonus == 0 || shop.Opening.WinnerBonus == GlobalRules.Economy.WinnerBonus);
            }
        }

        [Fact]
        public void Whole_Match_Is_Deterministic_From_Seed_And_Bot_Inputs_Serial_Or_Parallel()
        {
            Run serial = Play(parallel: false);
            Run parallel = Play(parallel: true);
            Assert.Equal(serial.EventFingerprint, parallel.EventFingerprint);
            Assert.Equal(serial.DuelHashes, parallel.DuelHashes);
            Assert.Equal(serial.Messages, parallel.Messages);
            Assert.Equal(serial.Ticks, parallel.Ticks);
        }

        [Fact]
        public void Replay_Manifest_Reproduces_The_Duel_Final_State_Hash()
        {
            MatchHost host = HostFixture.BotHost(HostFixture.DuelSettings(HostFixture.SmallDuelSeed, prefetchRounds: 8));
            Assert.True(host.Start().Started);
            HostFixture.RunUntilPhase(host, MatchPhase.Results, 60 * 60 * 10);
            DuelReport report = host.Duels.Reports.Single();
            ReplayManifest manifest = report.Replay;

            Assert.Equal("test-ruleset", manifest.RulesetFingerprint);
            Assert.Equal(report.FinalStateHash, manifest.FinalStateHash);
            Assert.Equal(report.FinalTick, manifest.Inputs[0].Length);
            Assert.Equal(report.FinalTick, manifest.Inputs[1].Length);
            Assert.Contains("WIZ-002", manifest.ContentIds);
            Assert.Contains(host.Arenas.Get(manifest.ArenaId).BiomeId, manifest.ContentIds);
            Assert.Equal(report.Setup.Side0Player, manifest.Side0Player);

            // Regenerated from the recorded arena recipe (seed, round, Match's diameter, biome, props), not the cache.
            ReplayResult replay = DuelReplayer.Replay(manifest, HostFixture.Generator, expectedRulesetFingerprint: "test-ruleset");
            Assert.True(replay.Matches, replay.Mismatch);
            Assert.Equal(report.FinalStateHash, replay.FinalStateHash);
            Assert.Equal(report.SimResult.Winner, replay.World.Result.Winner);

            // An incompatible build refuses the replay instead of reconstructing something misleading (L427).
            ReplayResult refused = DuelReplayer.Replay(manifest, host.Arenas.Get(manifest.ArenaId).CreateCopy(), expectedRulesetFingerprint: "other-ruleset");
            Assert.False(refused.Matches);
            Assert.Contains("unsupported version", refused.Mismatch);
        }

        private static Run Play(bool parallel)
        {
            // Four seats so two duels run at once; the same match seed reuses the eight-seat test's arenas.
            MatchHostSettings settings = HostFixture.ConclaveSettings(ConclaveSeed, wizards: new[] { "WIZ-002", "WIZ-010", "WIZ-001", "WIZ-003" });
            settings.ParallelDuels = parallel;
            MatchHost host = HostFixture.BotHost(settings);
            Assert.True(host.Start().Started);
            int ticks = host.RunUntilFinished(TickCap);
            Assert.True(host.IsFinished);
            return new Run
            {
                EventFingerprint = host.Flow.Events.Fingerprint(),
                DuelHashes = host.Duels.Reports.Select(r => r.Setup.DuelId + ":" + r.FinalTick + ":" + r.FinalStateHash.ToString("X16")).ToList(),
                Messages = host.Outbox.TotalCount,
                Ticks = ticks,
            };
        }

        private sealed class Run
        {
            public ulong EventFingerprint;
            public List<string> DuelHashes;
            public int Messages;
            public int Ticks;
        }
    }
}
