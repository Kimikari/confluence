using System.Linq;
using Confluence.Core.Rules;
using Confluence.Match;
using Confluence.Net.Game.Headless;
using Confluence.Net.Game.Server;
using Confluence.Net.Game.Wire;
using Confluence.Net.Tests.TestSupport;
using Confluence.Net.Transport;
using Xunit.Abstractions;

namespace Confluence.Net.Tests.Game
{
    /// <summary>
    /// A reconnect within the 90 s window (spec L55) restores everything the player missed: its own results and reports
    /// (L49), and after the match the final standings and placements. Messages sent while it was away are replayed from
    /// the server's per-seat history; the client keeps one entry per duel.
    /// </summary>
    public sealed class ReconnectRecoveryTests
    {
        private readonly ITestOutputHelper _output;

        public ReconnectRecoveryTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static GameServerSettings LabDuel() => new GameServerSettings
        {
            Mode = MatchMode.Duel,
            PlayerCount = 2,
            Seed = EndToEndTests.SeedWithFirstDiameter(MatchMode.Duel, 24, 300),
            Laboratory = true,
            ArenaPrefetchRounds = 2,
        };

        private static TransportSettings Reconnecting()
        {
            TransportSettings t = GameRig.FastTransport();
            t.AutoReconnect = true;
            t.ReconnectRetryMs = 500;
            return t;
        }

        [Fact]
        public void A_Player_Away_While_Its_Duel_Resolves_Gets_Its_Result_And_Report_On_Resume()
        {
            using (var rig = new GameRig(LabDuel()))
            {
                HeadlessPlayer a = rig.AddPlayer("Ash", "WIZ-002", seed: 1, transport: Reconnecting());
                HeadlessPlayer b = rig.AddPlayer("Brook", "WIZ-010", seed: 2, transport: Reconnecting());
                Assert.True(rig.RunUntil(() => a.Client.Duel != null && a.Client.Duel.Phase == Confluence.Sim.Duel.DuelPhase.Combat, 60 * 60 * 2));
                int duelId = a.Client.Duel.DuelId;

                // Ash's process freezes; the server notices after the 5 s timeout of the test transport.
                rig.Paused.Add(a);
                Assert.True(rig.RunUntil(() => rig.Log.Any(l => l.Contains("Seat 0 disconnected")), 60 * 10), "the server never saw the drop");
                // Brook surrenders (a normal loss, L53): the duel resolves while Ash is away.
                b.Client.Surrender();
                Assert.True(rig.RunUntil(() => b.Client.View.Results.Count >= 1 && b.Client.View.Reports.Count >= 1, 60 * 20), "the duel did not resolve");
                Assert.Empty(a.Client.View.Results);

                rig.Paused.Remove(a);
                Assert.True(rig.RunUntil(() => a.Client.IsConnected && a.Client.View.Results.Count >= 1 && a.Client.View.Reports.Count >= 1, 60 * 30),
                    "the resumed client never learned its result");
                DuelResultMessage result = a.Client.View.Results.Single();
                Assert.Equal(duelId, result.DuelId);
                Assert.Equal(Confluence.Match.DuelEndReason.Surrender, result.Reason);
                Assert.Equal(0, result.WinnerSeat);
                Assert.Equal(duelId, a.Client.View.Reports.Single().DuelId);
                Assert.True(a.Client.Duel.Ended, "the report ends the client's duel");

                // The match goes on, and later catch-ups never duplicate entries.
                Assert.True(rig.RunUntil(() => a.Client.View.Results.Count >= 2, 60 * 60 * 4), "the next duel did not resolve");
                Assert.Equal(a.Client.View.Results.Count, a.Client.View.Results.Select(r => r.DuelId).Distinct().Count());
                Assert.Equal(a.Client.View.Reports.Count, a.Client.View.Reports.Select(r => r.DuelId).Distinct().Count());
            }
        }

        [Fact]
        public void A_Player_Resuming_After_The_Match_Ended_Recovers_The_Final_Standings_Results_And_Placements()
        {
            using (var rig = new GameRig(LabDuel()))
            {
                HeadlessPlayer a = rig.AddPlayer("Ash", "WIZ-002", seed: 1, transport: Reconnecting());
                HeadlessPlayer b = rig.AddPlayer("Brook", "WIZ-010", seed: 2, transport: Reconnecting());
                // Brook surrenders every duel, so the match is short.
                bool done = rig.RunUntil(() =>
                {
                    if (b.Client.Duel != null && !b.Client.Duel.Ended && b.Client.Duel.Phase == Confluence.Sim.Duel.DuelPhase.Combat && !_surrendered.Contains(b.Client.Duel.DuelId))
                    {
                        _surrendered.Add(b.Client.Duel.DuelId);
                        b.Client.Surrender();
                    }

                    return a.MatchOver && b.MatchOver;
                }, 60 * 60 * 30);
                Assert.True(done, "the match did not finish");
                Assert.Equal(LobbyStatus.Finished, rig.Server.Status);
                MatchEndMessage end = a.Client.View.End;
                int results = a.Client.View.Results.Count;
                int reports = a.Client.View.Reports.Count;
                Assert.True(results >= 1 && reports >= 1);

                // Ash drops after the end, loses its view (a restarted client process would), and resumes within 90 s.
                rig.Paused.Add(a);
                Assert.True(rig.RunUntil(() => rig.Log.Any(l => l.Contains("Seat 0 disconnected")), 60 * 10), "the server never saw the drop");
                a.Client.View.Results.Clear();
                a.Client.View.Reports.Clear();
                a.Client.View.End = null;
                a.Client.View.Standings = null;
                rig.Paused.Remove(a);
                Assert.True(rig.RunUntil(() => a.Client.IsConnected && a.Client.View.End != null && a.Client.View.Results.Count == results &&
                    a.Client.View.Reports.Count == reports && a.Client.View.Standings != null, 60 * 30), "the final state was not resent");
                Assert.Contains(rig.Log, l => l.Contains("reconnected after the match ended"));
                Assert.Equal(end.ChampionSeat, a.Client.View.End.ChampionSeat);
                Assert.Equal(end.Placements.Count, a.Client.View.End.Placements.Count);
                foreach (string m in rig.Server.Metrics.Where(m => m.Contains("session-summary"))) _output.WriteLine(m);
                Assert.Contains(rig.Server.Metrics, m => m.Contains("\"type\":\"session-summary\"") && m.Contains("\"reason\":\"match-end\"") && m.Contains("\"seat\":0"));
                Assert.Contains(rig.Server.Metrics, m => m.Contains("\"type\":\"session-summary\"") && m.Contains("\"seat\":1"));
            }
        }

        private readonly System.Collections.Generic.HashSet<int> _surrendered = new System.Collections.Generic.HashSet<int>();
    }
}
