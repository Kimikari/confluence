using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Net.Diagnostics;
using Confluence.Net.Sync;
using Confluence.Net.Tests.TestSupport;
using Confluence.Net.Transport;

namespace Confluence.Net.Tests.Transport
{
    /// <summary>Real UDP through the impairment relay: reliability, ordering, and input redundancy under loss.</summary>
    public class LossyLinkTests
    {
        private static TransportSettings LossySettings()
        {
            TransportSettings s = LoopbackRig.FastSettings();
            s.DisconnectTimeoutMs = 10000; // keep-alives are lost too
            return s;
        }

        [Fact]
        public void Reliable_Ordered_Lanes_Deliver_Everything_In_Order_Under_Loss()
        {
            NetworkConditions conditions = NetworkConditions.Symmetric(latencyMs: 15, jitterMs: 10, lossChance: 0.2, seed: 11);
            using (var rig = new LoopbackRig(LossySettings(), conditions))
            {
                var atServer = new List<int>();
                var atClient = new List<int>();
                rig.Server.Messages.On(TestMessages.MatchCounter, (s, m) => atServer.Add(m.Value));
                ClientTransport client = rig.NewClient("Bram");
                client.Messages.On(TestMessages.EventCounter, (c, m) => atClient.Add(m.Value));
                client.Connect(rig.Endpoint);
                rig.WaitSettled(client, 20000);
                Assert.True(rig.Server.Sessions.TryGet(client.PlayerId, out PlayerSession session));

                const int Count = 200;
                for (int i = 0; i < Count; i++)
                {
                    rig.Server.Send(session, TestMessages.EventCounter, new CounterMessage { Value = i });
                    client.Send(TestMessages.MatchCounter, new CounterMessage { Value = i });
                    if (i % 10 == 0) rig.PumpOnce();
                }

                Assert.True(rig.PumpUntil(() => atServer.Count == Count && atClient.Count == Count, 30000),
                    "server got " + atServer.Count + ", client got " + atClient.Count);
                for (int i = 0; i < Count; i++)
                {
                    Assert.Equal(i, atServer[i]);
                    Assert.Equal(i, atClient[i]);
                }

                Assert.True(rig.Link.Dropped > 0);
            }
        }

        [Fact]
        public void Reliable_Unordered_Lane_Delivers_Every_Message_Once_Under_Loss()
        {
            NetworkConditions conditions = NetworkConditions.Symmetric(latencyMs: 10, jitterMs: 20, lossChance: 0.2, seed: 12);
            conditions.ServerToClient.DuplicateChance = 0.1;
            using (var rig = new LoopbackRig(LossySettings(), conditions))
            {
                var seen = new HashSet<int>();
                int deliveries = 0;
                ClientTransport client = rig.NewClient("Bram");
                client.Messages.On(TestMessages.BulkCounter, (c, m) =>
                {
                    deliveries++;
                    seen.Add(m.Value);
                });
                client.Connect(rig.Endpoint);
                rig.WaitSettled(client, 20000);

                const int Count = 150;
                for (int i = 0; i < Count; i++)
                {
                    rig.Server.Broadcast(TestMessages.BulkCounter, new CounterMessage { Value = i });
                }

                Assert.True(rig.PumpUntil(() => seen.Count == Count, 30000), "got " + seen.Count);
                Assert.Equal(Count, deliveries); // the relay duplicated datagrams, the lane did not
            }
        }

        [Fact]
        public void Input_Redundancy_Recovers_Dropped_Input_Packets()
        {
            // No jitter here: this loop sends far faster than 60 Hz, so any jitter would reorder dozens of packets
            // and the sequenced lane would drop the overtaken ones (InputBufferTests covers bounded reordering).
            var conditions = new NetworkConditions { Seed = 13 };
            conditions.ClientToServer.LossChance = 0.2;
            conditions.ClientToServer.LatencyMs = 5;
            using (var rig = new LoopbackRig(LossySettings(), conditions))
            {
                // The server consumes only after everything arrived, so let the whole stream queue.
                var received = new InputReceiveBuffer<TestInput>(maxBufferedTicks: 1024);
                rig.Server.Messages.On(TestMessages.Inputs, (s, packet) => received.Accept(packet.Commands));
                ClientTransport client = rig.Connect("Bram");
                Assert.Equal(ClientState.Connected, client.State);

                // Eight redundant inputs per packet: losing an input needs nine consecutive lost packets.
                var sendBuffer = new InputSendBuffer<TestInput>(redundancy: 8, capacity: 512);
                var message = new InputPacketMessage<TestInput, TestInputCodec>();
                const int Real = 240;
                long droppedBefore = rig.Link.Dropped;
                for (int tick = 1; tick <= Real + 20; tick++)
                {
                    sendBuffer.Push(new TestInput(tick % 7 - 3, tick % 5, tick % 11 == 0));
                    sendBuffer.FillPacket(message.Commands);
                    Assert.True(client.Send(TestMessages.Inputs, message));
                    rig.PumpOnce();
                }

                Assert.True(rig.PumpUntil(() => received.NewestReceivedSequence >= Real, 10000));
                Assert.True(rig.Link.Dropped - droppedBefore > 10, "the link should have dropped input packets");

                for (int seq = 1; seq <= Real; seq++)
                {
                    ConsumedInput<TestInput> consumed = received.ConsumeNext();
                    Assert.Equal(InputSource.Received, consumed.Source);
                    Assert.Equal((uint)seq, consumed.Sequence);
                    Assert.Equal(seq % 7 - 3, consumed.Input.MoveX);
                    Assert.Equal(seq % 5, consumed.Input.MoveZ);
                    Assert.Equal(seq % 11 == 0, consumed.Input.Jump);
                }

                Assert.True(received.Duplicates > 0);
            }
        }

        [Fact]
        public void Snapshots_On_The_Sequenced_Lane_Interpolate_At_The_Render_Delay()
        {
            using (var rig = new LoopbackRig())
            {
                var buffer = new SnapshotBuffer<Vec3>();
                uint lastAck = 0;
                ClientTransport client = rig.NewClient("Bram");
                client.Messages.On(TestMessages.Snapshot, (c, s) =>
                {
                    buffer.Add(s.Header.ServerTick, s.Position);
                    lastAck = s.Header.LastProcessedInput;
                });
                client.Connect(rig.Endpoint);
                rig.WaitSettled(client);
                Assert.True(rig.Server.Sessions.TryGet(client.PlayerId, out PlayerSession session));

                var selector = new SnapshotRateSelector();
                for (int tick = 0; tick <= 60; tick++)
                {
                    if (!selector.ShouldSend(tick)) continue;
                    var position = new Vec3(tick * 0.1f, 1f, -tick * 0.05f); // 6 m/s along X
                    rig.Server.Send(session, TestMessages.Snapshot, new PositionSnapshot
                    {
                        Header = new SnapshotHeader(tick, (uint)tick, tick >= 2 ? tick - 2 : SnapshotHeader.NoBaseline),
                        Position = position,
                    });
                    rig.PumpOnce();
                }

                Assert.True(rig.PumpUntil(() => buffer.NewestTick == 60));
                Assert.Equal(60u, lastAck);
                Assert.True(buffer.Count >= 25); // 30 Hz over one second; loopback rarely drops

                // Render 100 ms (6 ticks) behind tick 37.5: between snapshots 30 and 32.
                double renderTick = 37.5 - NetRules.InterpolationDelayMs * NetRules.TickRate / 1000.0;
                Assert.True(buffer.TrySample(renderTick, Vec3.Lerp, out Vec3 sampled));
                Assert.Equal(renderTick * 0.1, sampled.X, 3);
                Assert.Equal(-renderTick * 0.05, sampled.Z, 3);
            }
        }
    }
}
