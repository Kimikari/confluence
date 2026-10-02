using System;
using System.Collections.Generic;
using System.Linq;

namespace Confluence.Match.Tests.Fakes
{
    /// <summary>A match wired to scripted fakes, with run helpers.</summary>
    internal sealed class Harness
    {
        private Harness(MatchFlow match, FakeDuelRunner runner, FakeEconomy economy, FakeArenaProvider arenas)
        {
            Match = match;
            Runner = runner;
            Economy = economy;
            Arenas = arenas;
            economy.Clock = () => match.CurrentTick;
        }

        public MatchFlow Match { get; }
        public FakeDuelRunner Runner { get; }
        public FakeEconomy Economy { get; }
        public FakeArenaProvider Arenas { get; }

        public ConclaveMatch Conclave => (ConclaveMatch)Match;
        public DuelMatch Duel => (DuelMatch)Match;

        /// <summary>A Conclave whose players ready one tick after each shop or draft opens (fast tests) unless readyAfter is null.</summary>
        public static Harness ForConclave(ConclaveSettings settings, ulong seed, Func<DuelSetup, DuelScript> decide, int? readyAfter = 1)
        {
            var runner = new FakeDuelRunner(decide);
            var economy = new FakeEconomy { DefaultReadyAfter = readyAfter };
            var arenas = new FakeArenaProvider();
            return new Harness(new ConclaveMatch(settings, seed, runner, economy, arenas), runner, economy, arenas);
        }

        public static Harness ForConclave(int players, ulong seed, Func<DuelSetup, DuelScript> decide, int? readyAfter = 1) =>
            ForConclave(players == 8 ? ConclaveSettings.Unranked : ConclaveSettings.Custom(players), seed, decide, readyAfter);

        /// <summary>A Conclave whose bracket starts after <paramref name="regularRounds"/> regular rounds (internal test seam).</summary>
        public static Harness ForShortConclave(int players, ulong seed, int regularRounds, Func<DuelSetup, DuelScript> decide, int? readyAfter = 1)
        {
            var settings = players == 8 ? ConclaveSettings.Unranked : ConclaveSettings.Custom(players);
            var runner = new FakeDuelRunner(decide);
            var economy = new FakeEconomy { DefaultReadyAfter = readyAfter };
            var arenas = new FakeArenaProvider();
            return new Harness(new ConclaveMatch(settings, seed, runner, economy, arenas, regularRounds), runner, economy, arenas);
        }

        public static Harness ForDuel(ulong seed, Func<DuelSetup, DuelScript> decide, int? readyAfter = 1, DuelMatchOptions options = null)
        {
            var runner = new FakeDuelRunner(decide);
            var economy = new FakeEconomy { DefaultReadyAfter = readyAfter };
            var arenas = new FakeArenaProvider();
            return new Harness(new DuelMatch(seed, runner, economy, arenas, options), runner, economy, arenas);
        }

        public Harness Started()
        {
            Match.Start();
            return this;
        }

        public void RunToEnd(int maxTicks = 3_000_000)
        {
            if (Match.Phase == MatchPhase.Lobby) Match.Start();
            while (!Match.IsFinished)
            {
                Match.Tick();
                if (Match.CurrentTick > maxTicks) throw new InvalidOperationException("Match did not finish within " + maxTicks + " ticks.");
            }
        }

        /// <summary>Ticks until the condition holds (checked before each tick).</summary>
        public void RunUntil(Func<bool> condition, int maxTicks = 3_000_000)
        {
            if (Match.Phase == MatchPhase.Lobby) Match.Start();
            while (!condition())
            {
                if (Match.IsFinished) throw new InvalidOperationException("Match finished before the condition held.");
                Match.Tick();
                if (Match.CurrentTick > maxTicks) throw new InvalidOperationException("Condition not met within " + maxTicks + " ticks.");
            }
        }

        public void RunUntilPhase(MatchPhase phase, int round) => RunUntil(() => Match.Phase == phase && Match.Round == round);

        public List<PhaseChangedEvent> Phases(int round) =>
            Match.Events.OfType<PhaseChangedEvent>().Where(e => e.Round == round).ToList();

        public List<ShopOpening> ShopsIn(int round) => Economy.Shops.Where(s => s.Round == round).ToList();

        public List<RoundResult> ResultsOf(int round) => Match.RoundResults.Where(r => r.Round == round).ToList();
    }
}
