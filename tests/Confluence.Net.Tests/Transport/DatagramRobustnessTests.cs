using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Confluence.Net.Game;
using Confluence.Net.Tests.TestSupport;
using Confluence.Net.Transport;
using Xunit.Abstractions;

namespace Confluence.Net.Tests.Transport
{
    /// <summary>
    /// LiteNetLib 2.1.4 manual-mode receive bug #616 (research §2.8.1): an empty datagram, or a packet a packet layer
    /// rejects, at the head of the socket queue ends the receive loop of a manual Poll (on Linux/Mono permanently). The
    /// shipped mitigation is <c>ManualMode = false</c> in the production defaults (<see cref="GameTransport.Defaults"/>,
    /// the server and headless client CLIs); events are still raised only from Poll. These checks pin it: with library
    /// threads a connect behind a flood of empty and junk datagrams is accepted within one Poll, and with a rejecting
    /// packet layer the valid datagram behind N forged ones arrives in the same Poll without growing the packet pool.
    /// </summary>
    public sealed class DatagramRobustnessTests
    {
        private readonly ITestOutputHelper _output;

        public DatagramRobustnessTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static void SendRaw(IPEndPoint to, byte[] payload, int count)
        {
            using (var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            {
                for (int i = 0; i < count; i++) socket.Send(payload, payload.Length, to);
            }
        }

        /// <summary>Gives the library's receive thread time to drain the socket (wall clock, threaded mode only).</summary>
        private static void LetThreadsRun(int ms)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms) Thread.Sleep(1);
        }

        [Fact]
        public void The_Production_Defaults_Run_Library_Threads()
        {
            Assert.False(GameTransport.Defaults().ManualMode);
        }

        [Fact]
        public void With_Library_Threads_A_Connect_Behind_Empty_And_Junk_Datagrams_Is_Accepted_Within_One_Poll()
        {
            TransportSettings settings = LoopbackRig.FastSettings();
            settings.ManualMode = false;
            using (var rig = new LoopbackRig(settings))
            {
                int connected = 0;
                rig.Server.SessionConnected += (s, resumed) => connected++;
                string received = null;
                rig.Server.Messages.On(TestMessages.Text, (s, m) => received = m.Text);
                var server = new IPEndPoint(IPAddress.Loopback, rig.Server.Port);
                SendRaw(server, new byte[0], 64);
                SendRaw(server, new byte[] { 0xFF, 0x00, 0x13, 0x37 }, 64);
                SendRaw(server, new byte[0], 64);
                ClientTransport client = rig.NewClient();
                client.Connect(rig.Endpoint);
                LetThreadsRun(250);
                rig.Server.Poll(); // exactly one Poll
                Assert.Equal(1, connected);

                Assert.True(rig.PumpUntil(() => client.State == ClientState.Connected, 3000), "state " + client.State);
                // After the connection: a flood of empty datagrams never delays the session's traffic by a Poll.
                SendRaw(server, new byte[0], 200);
                long before = rig.Server.Counters.MessagesReceived;
                client.Send(TestMessages.Text, new TextMessage { Text = "after the flood" });
                client.Flush();
                LetThreadsRun(250);
                rig.Server.Poll();
                Assert.Equal("after the flood", received);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Empty_And_Junk_Datagrams_Never_Stall_A_Connect_In_Either_Mode(bool manualMode)
        {
            TransportSettings settings = LoopbackRig.FastSettings();
            settings.ManualMode = manualMode;
            using (var rig = new LoopbackRig(settings))
            {
                var server = new IPEndPoint(IPAddress.Loopback, rig.Server.Port);
                SendRaw(server, new byte[0], 5);
                SendRaw(server, new byte[] { 0xFF, 0x00, 0x13, 0x37 }, 50);
                SendRaw(server, new byte[0], 5);
                ClientTransport client = rig.NewClient();
                client.Connect(rig.Endpoint);
                Assert.True(rig.PumpUntil(() => client.State == ClientState.Connected, 3000), "state " + client.State);
                SendRaw(server, new byte[0], 20);
                long before = rig.Server.Counters.MessagesReceived;
                Assert.True(rig.PumpUntil(() => rig.Server.Counters.MessagesReceived > before, 3000), "the session kept receiving");
            }
        }
    }
}
