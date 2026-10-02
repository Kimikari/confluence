using System;
using System.Collections.Generic;
using Confluence.Net.Protocol;
using Confluence.Net.RateLimiting;
using Confluence.Net.Requests;
using Confluence.Net.Serialization;
using Confluence.Net.Tests.TestSupport;
using Confluence.Net.Transport;

namespace Confluence.Net.Tests.Requests
{
    /// <summary>Spec L429: inventory transactions are atomic, duplicate purchase requests are prevented, rerolls are rate-limited.</summary>
    public class RequestTests
    {
        /// <summary>In-memory server/client pair joined by direct calls, for exact ledger behavior.</summary>
        private sealed class MemoryPair
        {
            public readonly ManualNetClock Clock = new ManualNetClock(1000);
            public readonly MessageRegistry<PlayerSession> ServerRegistry = new MessageRegistry<PlayerSession>(true);
            public readonly MessageRegistry<object> ClientRegistry = new MessageRegistry<object>(false);
            public readonly List<byte[]> ToClient = new List<byte[]>();
            public readonly List<NetChannel> ToClientChannels = new List<NetChannel>();
            public readonly List<byte[]> ToServer = new List<byte[]>();
            public readonly List<NetChannel> ToServerChannels = new List<NetChannel>();
            public readonly RequestServer Server;
            public readonly RequestClient<object> Client;
            public readonly PlayerSession Session;
            public bool ClientLinkUp = true;

            public MemoryPair()
            {
                Server = new RequestServer(ServerRegistry, (s, channel, framed) =>
                {
                    ToClient.Add(framed);
                    ToClientChannels.Add(channel);
                }, Clock, new RateLimiter());
                Client = new RequestClient<object>(ClientRegistry, (channel, framed) =>
                {
                    if (!ClientLinkUp) return false;
                    ToServer.Add(framed);
                    ToServerChannels.Add(channel);
                    return true;
                });
                var sessions = new SessionDirectory(NetRules.ReconnectWindowMs, new SeededTokenSource(1));
                Session = sessions.Create("Bram", "b");
            }

            public void DeliverToServer()
            {
                for (int i = 0; i < ToServer.Count; i++)
                {
                    Assert.Equal(DispatchResult.Handled, ServerRegistry.Dispatch(Session, new NetReader(ToServer[i]), ToServerChannels[i]).Result);
                }

                ToServer.Clear();
                ToServerChannels.Clear();
            }

            public void DeliverToClient()
            {
                for (int i = 0; i < ToClient.Count; i++)
                {
                    Assert.Equal(DispatchResult.Handled, ClientRegistry.Dispatch(null, new NetReader(ToClient[i]), ToClientChannels[i]).Result);
                }

                ToClient.Clear();
                ToClientChannels.Clear();
            }
        }

        [Fact]
        public void Duplicate_Request_Returns_The_Original_Response_Bytes_Without_Rerunning()
        {
            var pair = new MemoryPair();
            int balance = 1000;
            pair.Server.Handle(TestMessages.Purchase, (s, req) =>
            {
                balance -= 200;
                return Reply<PurchaseResponse>.Ok(new PurchaseResponse { ItemId = req.ItemId, Balance = balance });
            });

            var results = new List<RequestResult<PurchaseResponse>>();
            uint id = pair.Client.Send(TestMessages.Purchase, new PurchaseRequest { ItemId = "EQP-007" }, r => results.Add(r));
            Assert.True(pair.Client.ResendRaw(id)); // a retry of the same transaction
            Assert.Equal(2, pair.ToServer.Count);
            pair.DeliverToServer();

            Assert.Equal(800, balance);
            Assert.Equal(2, pair.ToClient.Count);
            Assert.Equal(pair.ToClient[0], pair.ToClient[1]);
            Assert.Equal(1, pair.Server.Processed);
            Assert.Equal(1, pair.Server.Duplicates);

            pair.DeliverToClient();
            Assert.Single(results);
            Assert.Equal(RequestStatus.Ok, results[0].Status);
            Assert.Equal(800, results[0].Body.Balance);
            Assert.Equal(id, results[0].RequestId);
            Assert.Equal(1, pair.Client.UnmatchedResponses);
            Assert.Equal(0, pair.Client.PendingCount);
        }

        [Fact]
        public void Rejections_Are_Remembered_Like_Successes()
        {
            var pair = new MemoryPair();
            int calls = 0;
            pair.Server.Handle(TestMessages.Purchase, (s, req) =>
            {
                calls++;
                return Reply<PurchaseResponse>.Reject(7, new PurchaseResponse { ItemId = req.ItemId, Balance = 150 });
            });

            var results = new List<RequestResult<PurchaseResponse>>();
            uint id = pair.Client.Send(TestMessages.Purchase, new PurchaseRequest { ItemId = "EQP-090" }, r => results.Add(r));
            pair.Client.ResendRaw(id);
            pair.DeliverToServer();
            pair.DeliverToClient();

            Assert.Equal(1, calls);
            Assert.Single(results);
            Assert.Equal(RequestStatus.Rejected, results[0].Status);
            Assert.Equal(7, results[0].Reason);
            Assert.Equal(150, results[0].Body.Balance);
        }

        [Fact]
        public void Ledger_Classifies_New_Duplicate_And_Stale_Ids()
        {
            var ledger = new RequestLedger(4);
            Assert.Equal(LedgerCheck.New, ledger.Check(1, out _));
            for (uint id = 1; id <= 6; id++)
            {
                ledger.Record(id, new[] { (byte)id });
            }

            Assert.Equal(4, ledger.Count);
            Assert.Equal(LedgerCheck.Duplicate, ledger.Check(6, out byte[] cached));
            Assert.Equal(new byte[] { 6 }, cached);
            Assert.Equal(LedgerCheck.Duplicate, ledger.Check(3, out _));
            Assert.Equal(LedgerCheck.Stale, ledger.Check(2, out _)); // evicted and at least a window behind
            Assert.Equal(LedgerCheck.Stale, ledger.Check(1, out _));
            Assert.Equal(LedgerCheck.New, ledger.Check(7, out _));
        }

        [Fact]
        public void Interleaved_Ids_Inside_The_Window_Are_Still_New()
        {
            var ledger = new RequestLedger(8);
            ledger.Record(10, new byte[0]);
            Assert.Equal(LedgerCheck.New, ledger.Check(9, out _)); // overtaken on another reliable lane
        }

        [Fact]
        public void Stale_Request_Is_Answered_Without_Running_The_Handler()
        {
            var pair = new MemoryPair();
            int calls = 0;
            pair.Server.Handle(TestMessages.Purchase, (s, req) =>
            {
                calls++;
                return Reply<PurchaseResponse>.Ok(new PurchaseResponse());
            });

            RequestLedger ledger = pair.Server.LedgerOf(pair.Session.PlayerId);
            ledger.Record(500, new byte[0]);
            var results = new List<RequestResult<PurchaseResponse>>();
            pair.Client.Send(TestMessages.Purchase, new PurchaseRequest { ItemId = "EQP-001" }, r => results.Add(r)); // id 1
            pair.DeliverToServer();
            pair.DeliverToClient();

            Assert.Equal(0, calls);
            Assert.Equal(RequestStatus.Stale, results[0].Status);
            Assert.Equal(1, pair.Server.StaleRejected);
        }

        [Fact]
        public void Rate_Limited_Requests_Are_Answered_And_May_Be_Retried_Later()
        {
            var pair = new MemoryPair();
            int calls = 0;
            pair.Server.Handle(TestMessages.Reroll, (s, req) =>
            {
                calls++;
                return Reply<PurchaseResponse>.Ok(new PurchaseResponse { ItemId = req.ItemId });
            });

            var statuses = new List<RequestStatus>();
            for (int i = 0; i < 5; i++)
            {
                pair.Client.Send(TestMessages.Reroll, new PurchaseRequest { ItemId = "slot" + i }, r => statuses.Add(r.Status));
            }

            pair.DeliverToServer();
            pair.DeliverToClient();
            Assert.Equal(new[] { RequestStatus.Ok, RequestStatus.Ok, RequestStatus.Ok, RequestStatus.RateLimited, RequestStatus.RateLimited }, statuses);
            Assert.Equal(3, calls);
            Assert.Equal(2, pair.Server.RateLimited);

            // A rate-limited id is not remembered: once a token refills, the same id is processed.
            pair.Clock.Advance(60000);
            var envelope = new RequestEnvelope<PurchaseRequest> { RequestId = 4, Body = new PurchaseRequest { ItemId = "slot3" } };
            var writer = new NetWriter();
            MessageFraming.Write(writer, TestMessages.Reroll.RequestMessage, envelope);
            Assert.Equal(DispatchResult.Handled, pair.ServerRegistry.Dispatch(pair.Session, new NetReader(writer.ToArray()), NetChannel.Draft).Result);
            Assert.Equal(4, calls);
        }

        [Fact]
        public void Handler_Failure_Is_Reported_And_Never_Rerun()
        {
            var pair = new MemoryPair();
            int calls = 0;
            pair.Server.Handle(TestMessages.Purchase, (s, req) =>
            {
                calls++;
                throw new InvalidOperationException("boom");
            });

            var results = new List<RequestResult<PurchaseResponse>>();
            uint id = pair.Client.Send(TestMessages.Purchase, new PurchaseRequest { ItemId = "EQP-001" }, r => results.Add(r));
            pair.Client.ResendRaw(id);
            DispatchOutcome first = pair.ServerRegistry.Dispatch(pair.Session, new NetReader(pair.ToServer[0]), NetChannel.Shop);
            DispatchOutcome second = pair.ServerRegistry.Dispatch(pair.Session, new NetReader(pair.ToServer[1]), NetChannel.Shop);
            pair.ToServer.Clear();
            pair.ToServerChannels.Clear();

            Assert.Equal(DispatchResult.HandlerFault, first.Result);
            Assert.Equal("boom", first.Error.Message);
            Assert.Equal(DispatchResult.Handled, second.Result);
            Assert.Equal(1, calls);
            pair.DeliverToClient();
            Assert.Single(results);
            Assert.Equal(RequestStatus.ServerError, results[0].Status);
        }

        [Fact]
        public void Pending_Requests_Wait_While_Offline_And_Fail_When_The_Session_Is_Lost()
        {
            var pair = new MemoryPair { ClientLinkUp = false };
            pair.Server.Handle(TestMessages.Purchase, (s, req) => Reply<PurchaseResponse>.Ok(new PurchaseResponse { ItemId = req.ItemId }));
            var results = new List<RequestResult<PurchaseResponse>>();
            pair.Client.Send(TestMessages.Purchase, new PurchaseRequest { ItemId = "A" }, r => results.Add(r));
            pair.Client.Send(TestMessages.Purchase, new PurchaseRequest { ItemId = "B" }, r => results.Add(r));
            Assert.Empty(pair.ToServer);
            Assert.Equal(2, pair.Client.PendingCount);

            pair.ClientLinkUp = true;
            pair.Client.ResendPending();
            pair.DeliverToServer();
            pair.DeliverToClient();
            Assert.Equal(new[] { "A", "B" }, new[] { results[0].Body.ItemId, results[1].Body.ItemId });

            pair.Client.Send(TestMessages.Purchase, new PurchaseRequest { ItemId = "C" }, r => results.Add(r));
            pair.ToServer.Clear();
            pair.ToServerChannels.Clear();
            pair.Client.FailPending();
            Assert.Equal(RequestStatus.ConnectionLost, results[2].Status);
            Assert.Equal(0, pair.Client.PendingCount);
        }

        [Fact]
        public void Duplicate_Purchase_Over_Loopback_Charges_Once()
        {
            using (var rig = new LoopbackRig())
            {
                int balance = 600;
                rig.Server.Requests.Handle(TestMessages.Purchase, (s, req) =>
                {
                    balance -= 200;
                    return Reply<PurchaseResponse>.Ok(new PurchaseResponse { ItemId = req.ItemId, Balance = balance });
                });

                ClientTransport client = rig.Connect("Bram");
                var results = new List<RequestResult<PurchaseResponse>>();
                uint id = client.Requests.Send(TestMessages.Purchase, new PurchaseRequest { ItemId = "EQP-003" }, r => results.Add(r));
                Assert.True(client.Requests.ResendRaw(id));
                Assert.True(client.Requests.ResendRaw(id));

                Assert.True(rig.PumpUntil(() => results.Count == 1 && client.Requests.UnmatchedResponses == 2));
                Assert.Equal(400, balance);
                Assert.Equal(400, results[0].Body.Balance);
                Assert.Equal(2, rig.Server.Requests.Duplicates);
            }
        }

        [Fact]
        public void Reroll_Spam_Over_Loopback_Is_Rate_Limited_And_Counted()
        {
            using (var rig = new LoopbackRig())
            {
                rig.Server.Requests.Handle(TestMessages.Reroll, (s, req) => Reply<PurchaseResponse>.Ok(new PurchaseResponse { ItemId = req.ItemId }));
                ClientTransport client = rig.Connect("Bram");
                var statuses = new List<RequestStatus>();
                for (int i = 0; i < 10; i++)
                {
                    client.Requests.Send(TestMessages.Reroll, new PurchaseRequest { ItemId = "card" + i }, r => statuses.Add(r.Status));
                }

                Assert.True(rig.PumpUntil(() => statuses.Count == 10));
                Assert.Equal(3, statuses.FindAll(s => s == RequestStatus.Ok).Count);
                Assert.Equal(7, statuses.FindAll(s => s == RequestStatus.RateLimited).Count);
                Assert.Equal(RequestStatus.Ok, statuses[0]);
                Assert.Equal(7, rig.Server.RateLimiter.RejectedFor(TestMessages.Reroll.RequestMessage.Id));

                rig.ServerClock.Advance(60000);
                RequestStatus? later = null;
                client.Requests.Send(TestMessages.Reroll, new PurchaseRequest { ItemId = "later" }, r => later = r.Status);
                Assert.True(rig.PumpUntil(() => later.HasValue));
                Assert.Equal(RequestStatus.Ok, later.Value);
            }
        }
    }
}
