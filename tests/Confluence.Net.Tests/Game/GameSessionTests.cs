using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Rules;
using Confluence.Hosting.Commands;
using Confluence.Match;
using Confluence.Net.Diagnostics;
using Confluence.Net.Game;
using Confluence.Net.Game.Client;
using Confluence.Net.Game.Headless;
using Confluence.Net.Game.Server;
using Confluence.Net.Game.Wire;
using Confluence.Net.Requests;
using Confluence.Net.Tests.TestSupport;
using Xunit.Abstractions;

namespace Confluence.Net.Tests.Game
{
    /// <summary>
    /// Game sessions over real UDP on the virtual clock: idempotent and rate-limited shop commands (spec L429),
    /// prediction and reconciliation under latency and loss (L419), lag-compensation claims under impairment (L423),
    /// and reconnecting within the 90 s window (L55).
    /// </summary>
    public sealed class GameSessionTests
    {
        private readonly ITestOutputHelper _output;

        public GameSessionTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static GameServerSettings LabDuel(ulong start = 300) => new GameServerSettings
        {
            Mode = MatchMode.Duel,
            PlayerCount = 2,
            Seed = EndToEndTests.SeedWithFirstDiameter(MatchMode.Duel, 24, start),
            Laboratory = true,
            ArenaPrefetchRounds = 1,
        };

        private static bool ShopOpen(HeadlessPlayer p) =>
            p.Client.View.CurrentPhase == MatchPhase.Shop && p.Client.View.Shop != null && p.Client.View.Shop.Open && !p.Client.View.Shop.Ready;

        [Fact]
        public void A_Purchase_Resent_With_The_Same_Request_Id_Is_Charged_Once()
        {
            using (var rig = new GameRig(LabDuel()))
            {
                HeadlessPlayer a = rig.AddPlayer("Ash", "WIZ-002", seed: 1, autoEconomy: false);
                rig.AddPlayer("Brook", "WIZ-010", seed: 2);
                Assert.True(rig.RunUntil(() => ShopOpen(a), 60 * 60), "the shop never opened");
                ShopStateMessage before = a.Client.View.Shop;
                ShopCardWire card = before.Cards.Where(c => c.State == Confluence.Economy.Shop.ShopCardState.Offered && c.Price <= before.Crowns)
                    .OrderBy(c => c.Price).First();
                int items = before.Items.Count;
                var results = new List<RequestResult<CommandResponse>>();
                long duplicatesBefore = rig.Server.Transport.Requests.Duplicates;
                uint id = a.Client.Buy(card.Position, r => results.Add(r));
                // The same request goes out twice more before any response (a resend after a lost response, or a resume).
                a.Client.Transport.Requests.ResendPending();
                a.Client.Transport.Requests.ResendPending();
                Assert.True(rig.RunUntil(() => results.Count > 0 && a.Client.View.Shop != before, 120));
                rig.RunUntil(() => false, 30);

                Assert.Single(results);
                Assert.True(results[0].IsOk, "purchase " + results[0].Status + " " + (CommandCode)results[0].Reason);
                Assert.Equal(id, results[0].RequestId);
                Assert.True(rig.Server.Transport.Requests.Duplicates - duplicatesBefore >= 2, "the server saw the duplicates");
                ShopStateMessage after = a.Client.View.Shop;
                Assert.Equal(before.Crowns - card.Price, after.Crowns);
                Assert.Equal(items + 1, after.Items.Count);
            }
        }

        [Fact]
        public void Rerolls_Are_Rate_Limited_And_Commands_Must_Use_Their_Request_Type()
        {
            using (var rig = new GameRig(LabDuel()))
            {
                HeadlessPlayer a = rig.AddPlayer("Ash", "WIZ-002", seed: 1, autoEconomy: false);
                rig.AddPlayer("Brook", "WIZ-010", seed: 2);
                Assert.True(rig.RunUntil(() => ShopOpen(a), 60 * 60), "the shop never opened");
                var rerolls = new List<RequestResult<CommandResponse>>();
                for (int i = 0; i < 10; i++) a.Client.Send(CommandRequest.Reroll(i % 3), r => rerolls.Add(r));
                var wrong = new List<RequestResult<CommandResponse>>();
                a.Client.Transport.Requests.Send(GameProtocol.DraftCommand, CommandRequest.Buy(0), r => wrong.Add(r));
                Assert.True(rig.RunUntil(() => rerolls.Count == 10 && wrong.Count == 1, 240));

                int limited = rerolls.Count(r => r.Status == RequestStatus.RateLimited);
                _output.WriteLine("rerolls: " + string.Join(", ", rerolls.Select(r => r.Status + "/" + (CommandCode)r.Reason)));
                Assert.Equal(10 - NetRules.Rerolls.Burst, limited); // spec L429 "rate-limits rerolls": burst 3, then 1 per 250 ms
                Assert.Equal(RequestStatus.Rejected, wrong[0].Status);
                Assert.Equal((ushort)CommandCode.InvalidArguments, wrong[0].Reason);
            }
        }

        [Theory]
        [InlineData(20, 2, 0.0)]
        [InlineData(60, 5, 0.03)]
        [InlineData(100, 10, 0.05)]
        public void Prediction_Converges_And_Views_Stay_Honest_Under_Latency_And_Loss(int oneWayMs, int jitterMs, double loss)
        {
            NetworkConditions link = NetworkConditions.Symmetric(oneWayMs, jitterMs, loss, seed: 7);
            using (var rig = new GameRig(LabDuel(), link))
            {
                HeadlessPlayer a = rig.AddPlayer("Ash", "WIZ-002", seed: 1);
                HeadlessPlayer b = rig.AddPlayer("Brook", "WIZ-010", seed: 2);
                bool done = rig.RunUntil(() => a.Client.View.Results.Count >= 1 && b.Client.View.Results.Count >= 1, 60 * 60 * 6);
                foreach (string m in rig.Server.Metrics) _output.WriteLine(m);
                Assert.True(done, "the first duel did not finish");
                foreach (HeadlessPlayer p in new[] { a, b })
                {
                    ClientDuel duel = p.Client.Duel;
                    _output.WriteLine(p.Options.Name + ": reconciliations " + duel.Reconciliations + ", corrections " + duel.Corrections + ", p95 " +
                        duel.CorrectionP95.ToString("0.0000") + " m, max " + duel.MaxCorrection.ToString("0.000") + " m, snaps " + duel.Snaps +
                        ", interpolation " + duel.Interpolation.DelayTicks.ToString("0.00") + " ticks, lead adjust " + duel.Lead.Adjust +
                        ", starved renders " + duel.StarvedRenders + ", lateness p95 " + duel.Interpolation.LatenessP95.ToString("0.00"));
                    Assert.True(duel.Reconciliations > 1000);
                    // Converges: almost every reconciliation agrees with the prediction; nothing needed a snap.
                    Assert.True(duel.CorrectionP95 < 0.05f, "p95 correction " + duel.CorrectionP95);
                    Assert.True(duel.MaxCorrection < 1.5f, "max correction " + duel.MaxCorrection);
                    Assert.Equal(0, duel.Snaps);
                    Assert.Equal(0, p.Client.RejectedMessages);
                    Assert.True(duel.Interpolation.DelayTicks >= InterpolationClock.MinDelayTicks - 1e-6 && duel.Interpolation.DelayTicks <= InterpolationClock.MaxDelayTicks + 1e-6);
                }

                // Honest clients' view claims are accepted (research §2.2.3) and rewinds never exceed 150 ms (L423).
                foreach (string m in rig.Server.Metrics)
                {
                    long claims = Field(m, "viewClaims");
                    long replaced = Field(m, "viewsReplaced");
                    Assert.True(Field(m, "maxRewindTicks") <= GameServerTestsLimits.MaxRewindTicks);
                    Assert.True(replaced * 20 <= claims, "replaced " + replaced + " of " + claims);
                    Assert.Equal(0, Field(m, "violations"));
                    // The sustained-surplus cut is a backstop: a lossy, jittery link may meet it once or twice after a burst.
                    if (loss == 0) Assert.Equal(0, Field(m, "inputSurplusCut"));
                    else Assert.True(Field(m, "inputSurplusCut") <= 12, "surplus cut " + Field(m, "inputSurplusCut"));
                }
            }
        }

        [Fact]
        public void A_Five_Second_Outage_Resumes_Both_Seats_And_The_Match_Goes_On()
        {
            NetworkConditions link = NetworkConditions.Symmetric(10, 0, 0.0, seed: 3);
            using (var rig = new GameRig(LabDuel(), link))
            {
                Confluence.Net.Transport.TransportSettings reconnecting = GameRig.FastTransport();
                reconnecting.AutoReconnect = true;
                reconnecting.ReconnectRetryMs = 500;
                HeadlessPlayer a = rig.AddPlayer("Ash", "WIZ-002", seed: 1, transport: reconnecting);
                HeadlessPlayer b = rig.AddPlayer("Brook", "WIZ-010", seed: 2, transport: reconnecting.Clone());
                Assert.True(rig.RunUntil(() => a.Client.Duel != null && a.Client.Duel.Phase == Confluence.Sim.Duel.DuelPhase.Combat, 60 * 60 * 2));
                rig.Link.Conditions.ClientToServer.Blackhole = true;
                rig.Link.Conditions.ServerToClient.Blackhole = true;
                rig.RunUntil(() => false, 60 * 7); // longer than the 5 s disconnect timeout of the test transport
                Assert.False(a.Client.IsConnected);
                rig.Link.Conditions.ClientToServer.Blackhole = false;
                rig.Link.Conditions.ServerToClient.Blackhole = false;
                Assert.True(rig.RunUntil(() => a.Client.IsConnected && b.Client.IsConnected, 60 * 30), "the clients did not resume within the window");
                Assert.True(rig.Log.Any(l => l.Contains("Seat 0 reconnected")), "seat 0 resumed");
                // The match continues: the duel resolves (the absent ticks played out with neutral input, L55) and the next
                // round's shop opens for both.
                Assert.True(rig.RunUntil(() => a.Client.View.Results.Count >= 1 && b.Client.View.Results.Count >= 1 &&
                    a.Client.View.Phase != null && a.Client.View.Phase.Round >= 2, 60 * 60 * 3), "the match did not continue after the resume");
                Assert.True(a.Client.Transport.Clock.HasEstimate);
            }
        }

        [Fact]
        public void A_Networked_Duel_Over_A_Lossy_Link_Replays_To_The_Same_Final_State()
        {
            // The manifest records the commands as the server consumed them (after loss substitution, trims, view and
            // event rebasing), so a networked duel is a reproducible replay (spec L419, L427; research §2.9.6).
            NetworkConditions link = NetworkConditions.Symmetric(40, 5, 0.03, seed: 9);
            using (var rig = new GameRig(LabDuel(), link))
            {
                HeadlessPlayer a = rig.AddPlayer("Ash", "WIZ-002", seed: 1);
                rig.AddPlayer("Brook", "WIZ-010", seed: 2);
                Assert.True(rig.RunUntil(() => a.Client.View.Results.Count >= 1, 60 * 60 * 6));
                Confluence.Hosting.Duels.DuelReport report = rig.Server.Host.Duels.Reports.First();
                Confluence.Hosting.Duels.ReplayResult replay = Confluence.Hosting.Duels.DuelReplayer.Replay(report.Replay, GameRig.Generator,
                    expectedRulesetFingerprint: LoopbackRig.Ruleset.ToHex());
                Assert.True(replay.Matches, replay.Mismatch);
                Assert.Equal(report.FinalStateHash, replay.FinalStateHash);
            }
        }

        private static long Field(string json, string key)
        {
            string token = "\"" + key + "\":";
            int at = json.IndexOf(token, StringComparison.Ordinal);
            if (at < 0) throw new ArgumentException(key + " missing");
            at += token.Length;
            int end = at;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-' || json[end] == '.')) end++;
            return (long)double.Parse(json.Substring(at, end - at), System.Globalization.CultureInfo.InvariantCulture);
        }

        private static class GameServerTestsLimits
        {
            public static int MaxRewindTicks => LagCompensation.MaxRewindTicks;
        }
    }
}
