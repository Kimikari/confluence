using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using LiteNetLib;
using LiteNetLib.Layers;
using Xunit.Abstractions;

namespace Confluence.Net.Tests.Transport
{
    /// <summary>
    /// The second trigger of LiteNetLib #616 (research §2.8.1, §2.6.7): a packet layer (the future AEAD layer) that
    /// rejects a datagram by setting its length to 0. In library-thread mode (the production default) N rejected forged
    /// datagrams in front of a valid one never delay it past one Poll, and the packet pool does not grow.
    /// </summary>
    public sealed class PacketLayerRejectionTests
    {
        private readonly ITestOutputHelper _output;

        public PacketLayerRejectionTests(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>Rejects every datagram starting with 0xEE, the way an authentication layer drops a forgery.</summary>
        private sealed class RejectingLayer : PacketLayerBase
        {
            public int Rejected;

            public RejectingLayer() : base(0)
            {
            }

            public override void ProcessInboundPacket(ref IPEndPoint endPoint, ref byte[] data, ref int length)
            {
                if (length > 0 && data[0] == 0xEE)
                {
                    Interlocked.Increment(ref Rejected);
                    length = 0;
                }
            }

            public override void ProcessOutBoundPacket(ref IPEndPoint endPoint, ref byte[] data, ref int offset, ref int length)
            {
            }
        }

        private static string _poolField;

        private static int PoolSize(object manager)
        {
            // The pool's current size is private; read whichever int field counts it (diagnostics only).
            for (Type t = manager.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(x => x.FieldType == typeof(int) && x.Name.IndexOf("pool", StringComparison.OrdinalIgnoreCase) >= 0);
                if (f == null) continue;
                _poolField = t.Name + "." + f.Name;
                return (int)f.GetValue(manager);
            }

            return -1;
        }

        [Fact]
        public void Forged_Datagrams_A_Layer_Rejects_Never_Delay_A_Valid_One_Past_One_Poll_Or_Grow_The_Pool()
        {
            var listener = new EventBasedNetListener();
            string received = null;
            listener.NetworkReceiveUnconnectedEvent += (ep, reader, type) => received = reader.GetString();
            var layer = new RejectingLayer();
            var server = new NetManager(listener, layer) { UnconnectedMessagesEnabled = true };
            var client = new NetManager(new EventBasedNetListener()) { UnconnectedMessagesEnabled = true };
            try
            {
                Assert.True(server.Start(IPAddress.Loopback, IPAddress.IPv6None, 0));
                Assert.True(client.Start(IPAddress.Loopback, IPAddress.IPv6None, 0));
                var to = new IPEndPoint(IPAddress.Loopback, server.LocalPort);
                int poolBefore = PoolSize(server);
                using (var raw = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                {
                    var forged = new byte[] { 0xEE, 1, 2, 3, 4, 5, 6, 7 };
                    for (int i = 0; i < 500; i++) raw.Send(forged, forged.Length, to);
                }

                var writer = new LiteNetLib.Utils.NetDataWriter();
                writer.Put("valid after the forgeries");
                client.SendUnconnectedMessage(writer, to);
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 300) Thread.Sleep(1);
                server.PollEvents(); // exactly one Poll
                int poolAfter = PoolSize(server);
                _output.WriteLine("rejected " + layer.Rejected + ", pool (" + _poolField + ") " + poolBefore + " -> " + poolAfter);
                Assert.Equal(500, layer.Rejected);
                Assert.Equal("valid after the forgeries", received);
                if (poolBefore >= 0) Assert.True(poolAfter - poolBefore <= 4, "the packet pool grew from " + poolBefore + " to " + poolAfter);
            }
            finally
            {
                client.Stop();
                server.Stop();
            }
        }
    }
}
