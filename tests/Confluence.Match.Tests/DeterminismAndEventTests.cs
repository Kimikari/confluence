using System;
using System.Linq;
using Confluence.Core.Random;
using Confluence.Match.Tests.Fakes;

namespace Confluence.Match.Tests
{
    /// <summary>Same seed and same duel outcomes produce the same event stream (spec L419, L427); delayed spectator view (L391).</summary>
    public class DeterminismAndEventTests
    {
        private static Func<DuelSetup, DuelScript> Script(ulong salt) => s =>
        {
            ulong h = SeedHash.Combine(salt, (ulong)(s.Round * 64 + Math.Min(s.Side0Player, s.Side1Player) * 8 + Math.Max(s.Side0Player, s.Side1Player)));
            if (h % 9 == 0) return DuelScript.Draw((int)(h % 300));
            return DuelScript.Win((h >> 8) % 3 == 0 ? Math.Max(s.Side0Player, s.Side1Player) : Math.Min(s.Side0Player, s.Side1Player), (int)(h % 2000));
        };

        private static Harness RunConclave(ulong seed, ulong salt, int players = 8)
        {
            var h = Harness.ForConclave(players, seed, Script(salt));
            h.RunToEnd();
            return h;
        }

        [Fact]
        public void Equal_Seeds_And_Outcomes_Give_Identical_Conclave_Streams()
        {
            foreach (int players in new[] { 8, 7, 5 })
            {
                var a = RunConclave(42, 7, players);
                var b = RunConclave(42, 7, players);
                Assert.Equal(a.Match.Events.ToCanonicalText(), b.Match.Events.ToCanonicalText());
                Assert.Equal(a.Match.Events.Fingerprint(), b.Match.Events.Fingerprint());
                Assert.Equal(a.Match.Result.ToString(), b.Match.Result.ToString());
                Assert.Equal(a.Economy.Calls, b.Economy.Calls);
            }
        }

        [Fact]
        public void A_Different_Match_Seed_Changes_Arenas_And_Pairing_Ties()
        {
            var a = RunConclave(42, 7);
            var b = RunConclave(43, 7);
            Assert.NotEqual(a.Match.Events.Fingerprint(), b.Match.Events.Fingerprint());
            Assert.NotEqual(a.Arenas.Requests[0].ArenaSeed, b.Arenas.Requests[0].ArenaSeed);
            Assert.NotEqual(
                a.Match.Players.Select(p => p.LobbySeed),
                b.Match.Players.Select(p => p.LobbySeed));
        }

        [Fact]
        public void Equal_Seeds_And_Outcomes_Give_Identical_Duel_Streams()
        {
            Func<DuelSetup, DuelScript> script = s => s.Attempt == 0 && s.Round % 3 == 0 ? DuelScript.Draw() : DuelScript.Win(s.Round % 2, 50 + s.Round);
            var a = Harness.ForDuel(9, script);
            var b = Harness.ForDuel(9, script);
            a.RunToEnd();
            b.RunToEnd();
            Assert.Equal(a.Match.Events.ToCanonicalText(), b.Match.Events.ToCanonicalText());
        }

        [Fact]
        public void The_Stream_Narrates_Every_Phase_Pairing_Result_And_Elimination()
        {
            var h = RunConclave(5, 3);
            var events = h.Match.Events;
            Assert.IsType<MatchStartedEvent>(events[0]);
            Assert.IsType<MatchFinishedEvent>(events[events.Count - 1]);
            for (int i = 1; i < events.Count; i++) Assert.True(events[i].Tick >= events[i - 1].Tick);

            // Each pass: pairings and arena, then the phases; each duel starts and resolves; every placement is announced.
            int passes = events.OfType<PairingsAnnouncedEvent>().Count();
            Assert.Equal(passes, events.OfType<ArenaRevealedEvent>().Count());
            Assert.Equal(passes, events.OfType<PhaseChangedEvent>().Count(e => e.Phase == MatchPhase.Results));
            Assert.Equal(events.OfType<DuelStartedEvent>().Count(), events.OfType<DuelResolvedEvent>().Count());
            Assert.Equal(h.Match.RoundResults.Count, passes);
            int eliminated = h.Match.Result.Placements.Count(p => p.Player != h.Match.Result.Champion);
            Assert.True(events.OfType<PlayerEliminatedEvent>().Count() >= eliminated - 1);
            Assert.Contains("PairingsAnnounced", events.ToCanonicalText());

            // Incremental delivery returns the tail of the stream.
            Assert.Equal(events.Count - 10, events.Since(10).Count);
        }

        [Fact]
        public void Spectators_See_Events_Only_After_The_Delay()
        {
            var h = Harness.ForConclave(8, 6, FakeDuelRunner.LowerIdWins);
            h.RunUntilPhase(MatchPhase.Results, 2);
            int delay = ConclaveSettings.Ranked.SpectatorDelayTicks;
            var view = h.Match.Events.DelayedView(h.Match.CurrentTick, delay);
            Assert.NotEmpty(view);
            Assert.True(view.Count < h.Match.Events.Count);
            Assert.All(view, e => Assert.True(e.Tick <= h.Match.CurrentTick - delay));
            Assert.DoesNotContain(view, e => e is DuelResolvedEvent d && d.Round == 2);
        }
    }
}
