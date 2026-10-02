using System.Collections.Generic;
using Confluence.Net.Diagnostics;
using Confluence.Net.Protocol;
using Confluence.Net.Requests;
using Confluence.Net.Tests.TestSupport;
using Confluence.Net.Transport;

namespace Confluence.Net.Tests.Transport
{
    /// <summary>Spec L55: a reconnect is allowed for 90 seconds and resumes the same player identity.</summary>
    public class ReconnectTests
    {
        private static PlayerSession SessionOf(LoopbackRig rig, ClientTransport client)
        {
            Assert.True(rig.Server.Sessions.TryGet(client.PlayerId, out PlayerSession session));
            return session;
        }

        [Fact]
        public void Reconnect_Inside_The_Window_Resumes_The_Same_Player()
        {
            using (var rig = new LoopbackRig())
            {
                var resumedFlags = new List<bool>();
                rig.Server.SessionConnected += (s, resumed) => resumedFlags.Add(resumed);
                ClientTransport client = rig.Connect("Bram");
                int playerId = client.PlayerId;
                ReconnectToken token = client.ReconnectToken;
                PlayerSession session = SessionOf(rig, client);

                client.Disconnect();
                Assert.True(rig.PumpUntil(() => session.State == SessionState.Disconnected));

                rig.ServerClock.Advance(89000);
                client.Reconnect();
                rig.WaitSettled(client);

                Assert.Equal(ClientState.Connected, client.State);
                Assert.True(client.Resumed);
                Assert.Equal(playerId, client.PlayerId);
                Assert.Equal(token, client.ReconnectToken);
                Assert.Single(rig.Server.Sessions.All);
                Assert.Equal(SessionState.Connected, session.State);
                Assert.Equal(2, session.ConnectionCount);
                Assert.Equal(new[] { false, true }, resumedFlags);
            }
        }

        [Fact]
        public void Window_Bound_Is_Inclusive_At_Ninety_Seconds()
        {
            var directory = new SessionDirectory(NetRules.ReconnectWindowMs, new SeededTokenSource(3));
            PlayerSession s = directory.Create("Bram", "b");
            directory.MarkConnected(s, 1);
            directory.MarkDisconnected(s, 1000);

            Assert.Equal(90000, NetRules.ReconnectWindowMs);
            Assert.Equal(ResumeCheck.Resumable, directory.Check(s.Token, 1000 + 90000, out _));
            Assert.Equal(ResumeCheck.Expired, directory.Check(s.Token, 1000 + 90001, out _));
            Assert.Equal(ResumeCheck.UnknownToken, directory.Check(new ReconnectToken(1, 2), 1000, out _));
            Assert.Equal(ResumeCheck.UnknownToken, directory.Check(ReconnectToken.None, 1000, out _));

            var expired = new List<PlayerSession>();
            directory.ExpireOverdue(1000 + 90001, expired);
            Assert.Equal(new[] { s }, expired);
            Assert.Equal(SessionState.Expired, s.State);
            Assert.Equal(ResumeCheck.Expired, directory.Check(s.Token, 0, out _));
        }

        [Fact]
        public void Reconnect_After_The_Window_Is_Rejected_And_The_Session_Expires()
        {
            using (var rig = new LoopbackRig())
            {
                var expired = new List<PlayerSession>();
                rig.Server.SessionExpired += s => expired.Add(s);
                ClientTransport client = rig.Connect("Bram");
                PlayerSession session = SessionOf(rig, client);

                client.Disconnect();
                Assert.True(rig.PumpUntil(() => session.State == SessionState.Disconnected));

                rig.ServerClock.Advance(NetRules.ReconnectWindowMs + 1000);
                rig.PumpOnce();
                Assert.Equal(SessionState.Expired, session.State);
                Assert.Equal(new[] { session }, expired);

                client.Reconnect();
                rig.WaitSettled(client);
                Assert.Equal(ClientState.Rejected, client.State);
                Assert.Equal(RejectReason.ReconnectExpired, client.Rejection.Reason);
                Assert.Equal(1, rig.Server.Counters.Rejections(RejectReason.ReconnectExpired));
            }
        }

        [Fact]
        public void Restarted_Client_Resumes_With_A_Persisted_Token()
        {
            using (var rig = new LoopbackRig())
            {
                ClientTransport first = rig.Connect("Bram");
                ReconnectToken saved = first.ReconnectToken;
                int playerId = first.PlayerId;
                first.Stop();
                PlayerSession session = SessionOf(rig, first);
                Assert.True(rig.PumpUntil(() => session.State == SessionState.Disconnected));

                ClientTransport second = rig.NewClient("Bram");
                second.Connect(rig.Endpoint, saved);
                rig.WaitSettled(second);

                Assert.Equal(ClientState.Connected, second.State);
                Assert.True(second.Resumed);
                Assert.Equal(playerId, second.PlayerId);
            }
        }

        [Fact]
        public void Unknown_Token_Is_Rejected()
        {
            using (var rig = new LoopbackRig())
            {
                ClientTransport client = rig.NewClient("Bram");
                client.Connect(rig.Endpoint, new ReconnectToken(123, 456));
                rig.WaitSettled(client);
                Assert.Equal(RejectReason.UnknownSession, client.Rejection.Reason);
            }
        }

        [Fact]
        public void A_Second_Connection_With_The_Token_Takes_Over_The_Session()
        {
            using (var rig = new LoopbackRig())
            {
                ClientTransport original = rig.Connect("Bram");
                ClientTransport replacement = rig.NewClient("Bram");
                replacement.Connect(rig.Endpoint, original.ReconnectToken);
                rig.WaitSettled(replacement);

                Assert.True(rig.PumpUntil(() => original.State == ClientState.Disconnected));
                Assert.Equal(DisconnectCause.SessionTakenOver, original.LastDisconnectCause);
                Assert.Equal(ClientState.Connected, replacement.State);
                Assert.Equal(original.PlayerId, replacement.PlayerId);
                Assert.Single(rig.Server.Sessions.All);
            }
        }

        [Fact]
        public void Kicked_Player_Cannot_Resume()
        {
            using (var rig = new LoopbackRig())
            {
                ClientTransport client = rig.Connect("Bram");
                PlayerSession session = SessionOf(rig, client);
                rig.Server.Kick(session, "conduct");

                Assert.True(rig.PumpUntil(() => client.State == ClientState.Disconnected));
                Assert.Equal(DisconnectCause.Kicked, client.LastDisconnectCause);
                Assert.Equal("conduct", client.LastDisconnectDetail);
                Assert.Equal(SessionState.Expired, session.State);

                client.Reconnect();
                rig.WaitSettled(client);
                Assert.Equal(RejectReason.ReconnectExpired, client.Rejection.Reason);
            }
        }

        [Fact]
        public void Automatic_Reconnect_After_An_Outage_Resumes_And_Completes_Pending_Requests()
        {
            TransportSettings settings = LoopbackRig.FastSettings();
            settings.DisconnectTimeoutMs = 800;
            settings.AutoReconnect = true;
            settings.ReconnectRetryMs = 150;
            using (var rig = new LoopbackRig(settings, new NetworkConditions()))
            {
                int purchases = 0;
                rig.Server.Requests.Handle(TestMessages.Purchase, (s, req) =>
                {
                    purchases++;
                    return Reply<PurchaseResponse>.Ok(new PurchaseResponse { ItemId = req.ItemId, Balance = 400 });
                });

                ClientTransport client = rig.Connect("Bram");
                int playerId = client.PlayerId;
                PlayerSession session = SessionOf(rig, client);

                rig.Link.Conditions.ClientToServer.Blackhole = true;
                rig.Link.Conditions.ServerToClient.Blackhole = true;
                var results = new List<RequestResult<PurchaseResponse>>();
                client.Requests.Send(TestMessages.Purchase, new PurchaseRequest { ItemId = "EQP-001" }, r => results.Add(r));

                Assert.True(rig.PumpUntil(() => client.State == ClientState.Disconnected && session.State == SessionState.Disconnected, 8000));
                Assert.Equal(DisconnectCause.Timeout, client.LastDisconnectCause);
                Assert.Empty(results);

                rig.Link.Conditions.ClientToServer.Blackhole = false;
                rig.Link.Conditions.ServerToClient.Blackhole = false;
                Assert.True(rig.PumpUntil(() => client.State == ClientState.Connected && results.Count == 1, 10000));

                Assert.True(client.Resumed);
                Assert.Equal(playerId, client.PlayerId);
                Assert.Equal(RequestStatus.Ok, results[0].Status);
                Assert.Equal("EQP-001", results[0].Body.ItemId);
                Assert.Equal(1, purchases);
            }
        }
    }
}
