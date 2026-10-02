using System.Collections.Generic;
using System.Text;
using Confluence.Core.Content;
using Confluence.Net.Protocol;
using Confluence.Net.Tests.TestSupport;
using Confluence.Net.Transport;

namespace Confluence.Net.Tests.Transport
{
    public class HandshakeTests
    {
        [Fact]
        public void Matching_Client_Is_Welcomed_With_Identity_And_Token()
        {
            using (var rig = new LoopbackRig())
            {
                var connected = new List<KeyValuePair<PlayerSession, bool>>();
                rig.Server.SessionConnected += (s, resumed) => connected.Add(new KeyValuePair<PlayerSession, bool>(s, resumed));

                ClientTransport bram = rig.Connect("Bram");
                ClientTransport halen = rig.Connect("Halen");

                Assert.Equal(ClientState.Connected, bram.State);
                Assert.Equal(ClientState.Connected, halen.State);
                Assert.Equal(1, bram.PlayerId);
                Assert.Equal(2, halen.PlayerId);
                Assert.False(bram.Resumed);
                Assert.False(bram.ReconnectToken.IsNone);
                Assert.NotEqual(bram.ReconnectToken, halen.ReconnectToken);

                Assert.Equal(2, connected.Count);
                Assert.Equal("Bram", connected[0].Key.DisplayName);
                Assert.Equal("test-build", connected[0].Key.ClientBuild);
                Assert.False(connected[0].Value);
                Assert.Equal(2, rig.Server.Sessions.LiveCount);
            }
        }

        [Fact]
        public void Messages_Flow_Both_Ways_After_The_Welcome()
        {
            using (var rig = new LoopbackRig())
            {
                string atServer = null;
                string atClient = null;
                rig.Server.Messages.On(TestMessages.Text, (s, m) =>
                {
                    atServer = s.DisplayName + ":" + m.Text;
                    rig.Server.Send(s, TestMessages.Text, new TextMessage { Text = "echo " + m.Text });
                });

                ClientTransport client = rig.NewClient("Bram");
                client.Messages.On(TestMessages.Text, (c, m) => atClient = m.Text);
                client.Connect(rig.Endpoint);
                rig.WaitSettled(client);

                Assert.True(client.Send(TestMessages.Text, new TextMessage { Text = "hello" }));
                Assert.True(rig.PumpUntil(() => atClient != null));
                Assert.Equal("Bram:hello", atServer);
                Assert.Equal("echo hello", atClient);
            }
        }

        [Fact]
        public void Ruleset_Mismatch_Is_Rejected_With_A_Version_State()
        {
            using (var rig = new LoopbackRig())
            {
                RulesetFingerprint other = RulesetFingerprint.Compute(Encoding.UTF8.GetBytes("{\"other\":true}"), NetRules.RulesVersion);
                var identity = new ClientIdentity("Bram", other, "test-build");
                bool rejectedEvent = false;
                ClientTransport client = rig.NewClient(identity: identity);
                client.Rejected += c => rejectedEvent = true;
                client.Connect(rig.Endpoint);
                rig.WaitSettled(client);

                Assert.Equal(ClientState.Rejected, client.State);
                Assert.True(rejectedEvent);
                Assert.Equal(RejectReason.RulesetMismatch, client.Rejection.Reason);
                Assert.True(client.Rejection.IsVersionMismatch);
                Assert.Equal(LoopbackRig.Ruleset, client.Rejection.ServerRuleset);
                Assert.Equal(NetRules.RulesVersion, client.Rejection.ServerRulesVersion);
                Assert.Contains(LoopbackRig.Ruleset.ShortHex, client.Rejection.Detail);
                Assert.Equal(1, rig.Server.Counters.Rejections(RejectReason.RulesetMismatch));
                Assert.Empty(rig.Server.Sessions.All);
            }
        }

        [Fact]
        public void Rules_Version_Change_Alone_Changes_The_Fingerprint_And_Is_Rejected()
        {
            using (var rig = new LoopbackRig())
            {
                RulesetFingerprint newerRules = RulesetFingerprint.FromFile(Catalogue.FindDefaultPath(), NetRules.RulesVersion + "-patch");
                ClientTransport client = rig.Connect(identity: new ClientIdentity("Bram", newerRules) { RulesVersion = NetRules.RulesVersion + "-patch" });

                Assert.Equal(ClientState.Rejected, client.State);
                Assert.Equal(RejectReason.RulesetMismatch, client.Rejection.Reason);
                Assert.Contains("-patch", client.Rejection.Detail);
            }
        }

        [Fact]
        public void Protocol_Mismatch_Is_Rejected_Before_The_Ruleset_Check()
        {
            using (var rig = new LoopbackRig())
            {
                var identity = new ClientIdentity("Bram", LoopbackRig.Ruleset) { ProtocolVersion = (ushort)(NetRules.ProtocolVersion + 1) };
                ClientTransport client = rig.Connect(identity: identity);

                Assert.Equal(ClientState.Rejected, client.State);
                Assert.Equal(RejectReason.UnsupportedProtocol, client.Rejection.Reason);
                Assert.True(client.Rejection.IsVersionMismatch);
                Assert.Equal(NetRules.ProtocolVersion, client.Rejection.ServerProtocolVersion);
            }
        }

        [Fact]
        public void Required_Client_Build_Is_Enforced()
        {
            using (var rig = new LoopbackRig(configure: s => s.Identity.RequiredClientBuild = "build-42"))
            {
                ClientTransport wrong = rig.Connect(identity: new ClientIdentity("Bram", LoopbackRig.Ruleset, "build-41"));
                ClientTransport right = rig.Connect(identity: new ClientIdentity("Halen", LoopbackRig.Ruleset, "build-42"));

                Assert.Equal(RejectReason.UnsupportedClientBuild, wrong.Rejection.Reason);
                Assert.Equal(ClientState.Connected, right.State);
            }
        }

        [Fact]
        public void Full_Server_Rejects_Newcomers()
        {
            using (var rig = new LoopbackRig(configure: s => s.Identity.MaxSessions = 2))
            {
                rig.Connect("A");
                rig.Connect("B");
                ClientTransport third = rig.Connect("C");

                Assert.Equal(ClientState.Rejected, third.State);
                Assert.Equal(RejectReason.ServerFull, third.Rejection.Reason);
                Assert.False(third.Rejection.IsVersionMismatch);
            }
        }

        [Fact]
        public void Closed_Server_Rejects_New_Players()
        {
            using (var rig = new LoopbackRig(configure: s => s.AcceptNewPlayers = false))
            {
                ClientTransport client = rig.Connect("Bram");
                Assert.Equal(RejectReason.NotAcceptingPlayers, client.Rejection.Reason);
            }
        }

        [Fact]
        public void Display_Names_Are_Trimmed_And_Validated()
        {
            Assert.Equal("Bram Cinder", DisplayNames.Normalize("  Bram Cinder "));
            Assert.Null(DisplayNames.Normalize(""));
            Assert.Null(DisplayNames.Normalize("   "));
            Assert.Null(DisplayNames.Normalize(new string('x', NetRules.DisplayNameMaxChars + 1)));
            Assert.Null(DisplayNames.Normalize("tab\tname"));
            Assert.NotNull(DisplayNames.Normalize(new string('x', NetRules.DisplayNameMaxChars)));
            Assert.Throws<System.ArgumentException>(() => new ClientTransport(new ClientIdentity("\u0001", LoopbackRig.Ruleset)));
        }
    }
}
