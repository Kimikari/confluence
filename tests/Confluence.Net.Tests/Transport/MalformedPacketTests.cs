using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Confluence.Core.Random;
using Confluence.Net.Protocol;
using Confluence.Net.Serialization;
using Confluence.Net.Tests.TestSupport;
using Confluence.Net.Transport;
using LiteNetLib;

namespace Confluence.Net.Tests.Transport
{
    /// <summary>Malformed input is rejected and counted; it never reaches a handler and never crashes the server.</summary>
    public class MalformedPacketTests
    {
        private static byte[] Frame(MessageType type, INetMessage body, params byte[] trailing)
        {
            var writer = new NetWriter();
            MessageFraming.Write(writer, type, body);
            writer.WriteBytes(trailing);
            return writer.ToArray();
        }

        [Fact]
        public void Garbage_From_A_Connected_Client_Is_Dropped_And_Counted()
        {
            using (var rig = new LoopbackRig())
            {
                var texts = new List<string>();
                var rejected = new List<DispatchResult>();
                rig.Server.Messages.On(TestMessages.Text, (s, m) => texts.Add(m.Text));
                rig.Server.Messages.On(TestMessages.Aim, (s, m) => texts.Add("aim " + m.Yaw));
                rig.Server.MessageRejected += (s, outcome) => rejected.Add(outcome.Result);
                ClientTransport client = rig.Connect("Bram");

                var truncated = new NetWriter();
                truncated.WriteVarUInt(TestMessages.Text.Id);
                truncated.WriteVarUInt(50); // claims 50 string bytes, sends 2
                truncated.WriteByte(65);
                truncated.WriteByte(66);

                var nan = new NetWriter();
                nan.WriteVarUInt(TestMessages.Aim.Id);
                nan.WriteInt(BitConverter.SingleToInt32Bits(float.NaN));

                var overlongVarint = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 };

                client.SendRaw(NetChannel.Events, new byte[] { 0x80 });                                        // id cut short
                client.SendRaw(NetChannel.Events, new byte[] { 0x8F, 0x4E });                                  // unknown id 9999
                client.SendRaw(NetChannel.Events, overlongVarint);                                             // id overflow
                client.SendRaw(NetChannel.Events, truncated.ToArray());                                        // truncated body
                client.SendRaw(NetChannel.Events, Frame(TestMessages.Text, new TextMessage { Text = "x" }, 9)); // trailing byte
                client.SendRaw(NetChannel.Events, nan.ToArray());                                              // non-finite float
                client.SendRaw(NetChannel.Match, Frame(TestMessages.Text, new TextMessage { Text = "lane" }));  // wrong lane
                client.SendRaw(NetChannel.Control, Frame(ProtocolMessages.Welcome, new Welcome { PlayerId = 1 })); // server-to-client id
                client.Send(TestMessages.Text, new TextMessage { Text = "still fine" });

                Assert.True(rig.PumpUntil(() => texts.Count == 1), string.Join(",", rejected));
                Assert.Equal("still fine", texts[0]);
                Assert.True(rig.PumpUntil(() => rejected.Count == 8), string.Join(",", rejected));

                TransportCounters counters = rig.Server.Counters;
                Assert.Equal(5, counters.Malformed);       // empty, varint overflow, truncated, trailing byte, NaN
                Assert.Equal(1, counters.WrongChannel);
                Assert.Equal(2, counters.UnknownMessages); // id 9999 and the client-sent welcome
                Assert.Equal(8, counters.Violations);
                Assert.Equal(ClientState.Connected, client.State);
                Assert.False(client.SendRaw(NetChannel.Events, new byte[0])); // never sent: it would stall a reliable lane
            }
        }

        [Fact]
        public void Repeated_Violations_Close_The_Connection_For_Good()
        {
            TransportSettings settings = LoopbackRig.FastSettings();
            settings.MalformedKickThreshold = 5;
            using (var rig = new LoopbackRig(settings))
            {
                ClientTransport client = rig.Connect("Bram");
                Assert.True(rig.Server.Sessions.TryGet(client.PlayerId, out PlayerSession session));
                for (int i = 0; i < 6; i++)
                {
                    client.SendRaw(NetChannel.Events, new byte[] { 0x7F });
                }

                Assert.True(rig.PumpUntil(() => client.State == ClientState.Disconnected));
                Assert.Equal(DisconnectCause.ProtocolViolation, client.LastDisconnectCause);
                Assert.Equal(SessionState.Expired, session.State);
                Assert.Equal(5, session.Counters.Violations);
            }
        }

        [Fact]
        public void Malformed_And_Invalid_Handshakes_Are_Rejected()
        {
            using (var rig = new LoopbackRig())
            {
                Assert.Equal(RejectReason.MalformedHandshake, RawConnect(rig, new byte[] { 1, 2, 3, 4, 5 }));

                var badName = new HandshakeRequest { Ruleset = LoopbackRig.Ruleset, DisplayName = "bad\u0007name" };
                var writer = new NetWriter();
                badName.Serialize(writer);
                Assert.Equal(RejectReason.InvalidDisplayName, RawConnect(rig, writer.ToArray()));

                var trailing = new HandshakeRequest { Ruleset = LoopbackRig.Ruleset, DisplayName = "Bram" };
                writer.Reset();
                trailing.Serialize(writer);
                writer.WriteByte(0);
                Assert.Equal(RejectReason.MalformedHandshake, RawConnect(rig, writer.ToArray()));

                Assert.Equal(2, rig.Server.Counters.Rejections(RejectReason.MalformedHandshake));
                Assert.Empty(rig.Server.Sessions.All);
                Assert.Equal(ClientState.Connected, rig.Connect("Bram").State);
            }
        }

        [Fact]
        public void Random_Datagrams_At_The_Server_Port_Do_No_Harm()
        {
            using (var rig = new LoopbackRig())
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                var rng = new Rng(99);
                var target = new IPEndPoint(IPAddress.Loopback, rig.Server.Port);
                for (int i = 0; i < 300; i++)
                {
                    var junk = new byte[1 + rng.NextInt(600)];
                    for (int b = 0; b < junk.Length; b++)
                    {
                        junk[b] = (byte)rng.NextInt(256);
                    }

                    socket.SendTo(junk, target);
                    if (i % 50 == 0) rig.PumpOnce();
                }

                rig.PumpFor(50);
                ClientTransport client = rig.Connect("Bram");
                Assert.Equal(ClientState.Connected, client.State);
            }
        }

        [Fact]
        public void Registry_Reports_Each_Failure_Kind_Without_Throwing()
        {
            var registry = new MessageRegistry<string>(true);
            int handled = 0;
            registry.On(TestMessages.Text, (s, m) => handled++);
            registry.On(TestMessages.Chat, (s, m) => throw new InvalidOperationException("handler bug"));

            Assert.Equal(DispatchResult.Handled, registry.Dispatch("p", new NetReader(Frame(TestMessages.Text, new TextMessage { Text = "ok" })), NetChannel.Events).Result);
            Assert.Equal(DispatchResult.Malformed, registry.Dispatch("p", new NetReader(new byte[0]), NetChannel.Events).Result);
            Assert.Equal(DispatchResult.UnknownMessage, registry.Dispatch("p", new NetReader(new byte[] { 0x63 }), NetChannel.Events).Result);
            Assert.Equal(DispatchResult.WrongChannel, registry.Dispatch("p", new NetReader(Frame(TestMessages.Text, new TextMessage())), NetChannel.Shop).Result);
            Assert.Equal(DispatchResult.Malformed, registry.Dispatch("p", new NetReader(Frame(TestMessages.Text, new TextMessage(), 1)), NetChannel.Events).Result);
            Assert.Equal(DispatchResult.RateLimited,
                registry.Dispatch("p", new NetReader(Frame(TestMessages.Text, new TextMessage())), NetChannel.Events, (s, t) => false).Result);
            DispatchOutcome fault = registry.Dispatch("p", new NetReader(Frame(TestMessages.Chat, new TextMessage())), NetChannel.Match);
            Assert.Equal(DispatchResult.HandlerFault, fault.Result);
            Assert.Equal("handler bug", fault.Error.Message);
            Assert.False(fault.IsViolation);
            Assert.Equal(1, handled);
        }

        [Fact]
        public void Registry_Refuses_Conflicting_Or_Unreceivable_Registrations()
        {
            var server = new MessageRegistry<string>(true);
            var clash = new MessageType<TextMessage>(TestMessages.Text.Id, "clash", NetChannel.Events, MessageDirection.Both);
            server.On(TestMessages.Text, (s, m) => { });
            Assert.Throws<ArgumentException>(() => server.On(clash, (s, m) => { }));
            Assert.Throws<ArgumentException>(() => server.On(ProtocolMessages.Welcome, (s, m) => { }));
            var client = new MessageRegistry<string>(false);
            Assert.Throws<ArgumentException>(() => client.On(ProtocolMessages.ClockPing, (s, m) => { }));
        }

        /// <summary>Connects a bare LiteNetLib client with arbitrary connection data and returns the reject reason.</summary>
        private static RejectReason RawConnect(LoopbackRig rig, byte[] connectData)
        {
            var listener = new EventBasedNetListener();
            RejectReason? reason = null;
            listener.PeerDisconnectedEvent += (peer, info) =>
            {
                var rejection = new ConnectionRejection();
                if (info.Reason == DisconnectReason.ConnectionRejected && info.AdditionalData.AvailableBytes > 0)
                {
                    var reader = new NetReader(info.AdditionalData.RawData, info.AdditionalData.Position, info.AdditionalData.AvailableBytes);
                    rejection.Deserialize(reader);
                }

                reason = rejection.Reason;
            };

            var manager = new NetManager(listener);
            try
            {
                manager.Start();
                manager.Connect(new IPEndPoint(IPAddress.Loopback, rig.Server.Port), new ReadOnlySpan<byte>(connectData));
                var watch = Stopwatch.StartNew();
                while (!reason.HasValue && watch.ElapsedMilliseconds < 5000)
                {
                    rig.PumpOnce();
                    manager.PollEvents();
                    Thread.Sleep(1);
                }
            }
            finally
            {
                manager.Stop();
            }

            Assert.True(reason.HasValue, "no rejection received");
            return reason.Value;
        }
    }
}
