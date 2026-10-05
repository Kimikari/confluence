using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Rules;
using Confluence.Hosting.Arenas;
using Confluence.Net.Game.Headless;
using Confluence.Net.Game.Server;
using Confluence.Net.Game.Wire;
using Confluence.Net.Protocol;
using Confluence.Net.Serialization;
using Confluence.Net.Tests.TestSupport;

namespace Confluence.Net.Tests.Game
{
    /// <summary>
    /// The lobby freezes wizard choices once the match host exists (spec L292 exclusive kits, L419 server-owned casts),
    /// and every pace the server may announce decodes on the client (spec L427: a compatible client never sees a
    /// misleading reconstruction or a malformed welcome).
    /// </summary>
    public sealed class LobbyAndPaceTests
    {
        private static T RoundTrip<T>(T message) where T : INetMessage, new()
        {
            var w = new NetWriter(256);
            message.Serialize(w);
            var copy = new T();
            var r = new NetReader(w.ToArray(), 0, w.Length);
            copy.Deserialize(r);
            r.ExpectEnd();
            return copy;
        }

        [Fact]
        public void Every_Accelerated_Pace_The_Server_Accepts_Decodes_In_The_Welcome_And_The_Match_Settings()
        {
            Assert.Equal(1000, NetRules.MaxTickRate);
            Assert.Equal(16, NetRules.MaxAcceleration);
            for (int factor = 1; factor <= NetRules.MaxAcceleration; factor++)
            {
                int rate = NetRules.TickRate * factor;
                Welcome welcome = RoundTrip(new Welcome { PlayerId = 1, ReconnectToken = new ReconnectToken(1, 2), TickRate = rate });
                Assert.Equal(rate, welcome.TickRate);
                MatchSettingsMessage settings = RoundTrip(new MatchSettingsMessage { YourSeat = 0, HostFingerprint = "AB", TicksPerWallSecond = rate, Accelerated = factor > 1 });
                Assert.Equal(rate, settings.TicksPerWallSecond);
            }

            // Factor 17 (1020 ticks/s) was accepted by the CLI before and failed every client's welcome decode.
            var w = new NetWriter(64);
            new Welcome { PlayerId = 1, TickRate = NetRules.TickRate * 17 }.Serialize(w);
            Assert.Throws<NetReadException>(() => new Welcome().Deserialize(new NetReader(w.ToArray(), 0, w.Length)));
            var settingsTooFast = new GameServerSettings { TicksPerWallSecond = NetRules.TickRate * 17, Transport = GameRig.FastTransport() };
            Assert.Throws<ArgumentException>(() => new GameServer(GameRig.Catalogue, GameRig.Generator, LoopbackRig.Ruleset, settingsTooFast));
        }

        [Fact]
        public void A_Wizard_Change_During_The_Arena_Warm_Up_Is_Refused_And_The_Host_Keeps_The_Original_Kit()
        {
            var settings = new GameServerSettings
            {
                Mode = MatchMode.Duel,
                PlayerCount = 2,
                Seed = EndToEndTests.SeedWithFirstDiameter(MatchMode.Duel, 48, 900),
                Laboratory = true,
            };
            // A private arena cache: the first arena is really generated, so the warm-up lasts many ticks.
            using (var rig = new GameRig(settings, arenas: new ArenaCache()))
            {
                HeadlessPlayer a = rig.AddPlayer("Ash", "WIZ-002", seed: 1);
                HeadlessPlayer b = rig.AddPlayer("Brook", "WIZ-010", seed: 2);
                Assert.True(rig.RunUntil(() => rig.Server.Host != null, 60 * 60), "the host was never created");
                Assert.Equal(LobbyStatus.Waiting, rig.Server.Status); // warming up

                var answers = new List<KeyValuePair<JoinCode, LobbyStatus>>();
                a.Client.Transport.Requests.Send(Confluence.Net.Game.GameProtocol.JoinLobby, new JoinLobbyRequest { WizardId = "WIZ-010", Route = a.Client.Options.Route },
                    r => answers.Add(new KeyValuePair<JoinCode, LobbyStatus>(r.IsOk ? JoinCode.Accepted : (JoinCode)r.Reason, rig.Server.Status)));
                // An identical re-join (a resent request) is still accepted.
                a.Client.Transport.Requests.Send(Confluence.Net.Game.GameProtocol.JoinLobby, new JoinLobbyRequest { WizardId = "WIZ-002", Route = a.Client.Options.Route },
                    r => answers.Add(new KeyValuePair<JoinCode, LobbyStatus>(r.IsOk ? JoinCode.Accepted : (JoinCode)r.Reason, rig.Server.Status)));
                Assert.True(rig.RunUntil(() => answers.Count == 2, 60 * 10), "no answers");
                Assert.Equal(JoinCode.MatchRunning, answers[0].Key);
                Assert.Equal(JoinCode.Accepted, answers[1].Key);
                Assert.Equal(LobbyStatus.Waiting, answers[0].Value); // refused during the warm-up itself, not after the start

                // The background generation takes wall time; virtual ticks fly meanwhile.
                var wall = System.Diagnostics.Stopwatch.StartNew();
                while (!(a.Client.View.Settings != null && a.Client.Duel != null) && wall.Elapsed.TotalSeconds < 90) rig.Step();
                Assert.True(a.Client.View.Settings != null && a.Client.Duel != null, "the match did not start");
                Assert.Equal("WIZ-002", a.Client.View.Settings.Seats.Single(s => s.Seat == a.Client.View.Seat).WizardId);
                Assert.Equal("WIZ-002", a.Client.Duel.Own.WizardId); // the duel setup the host simulates
            }
        }
    }
}
