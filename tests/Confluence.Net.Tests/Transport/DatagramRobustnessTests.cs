using System.Net;
using System.Net.Sockets;
using Confluence.Net.Tests.TestSupport;
using Confluence.Net.Transport;

namespace Confluence.Net.Tests.Transport
{
    /// <summary>
    /// LiteNetLib 2.1.4 manual-mode receive bug #616 (research §2.8.1): an empty datagram at the head of the socket queue
    /// ends the receive loop of a Poll. These checks pin the behavior the server relies on: junk and empty datagrams from
    /// any host never keep a valid connection from completing, in manual mode (the default) and with library threads.
    /// </summary>
    public sealed class DatagramRobustnessTests
    {
        private static void SendRaw(IPEndPoint to, byte[] payload, int count)
        {
            using (var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            {
                for (int i = 0; i < count; i++) socket.Send(payload, payload.Length, to);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Empty_And_Junk_Datagrams_Do_Not_Stall_A_Connect(bool manualMode)
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

                // And after the connection: more empty datagrams never block the session's traffic.
                SendRaw(server, new byte[0], 20);
                long before = rig.Server.Counters.MessagesReceived;
                Assert.True(rig.PumpUntil(() => rig.Server.Counters.MessagesReceived > before, 3000), "the session kept receiving");
            }
        }
    }
}
