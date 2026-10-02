using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Confluence.Core.Random;
using Confluence.Net.Transport;

namespace Confluence.Net.Diagnostics
{
    /// <summary>Impairment applied to one direction of a simulated link.</summary>
    public sealed class LinkConditions
    {
        /// <summary>Fixed one-way delay.</summary>
        public int LatencyMs { get; set; }

        /// <summary>Extra uniform random delay in [0, JitterMs]; jitter reorders datagrams.</summary>
        public int JitterMs { get; set; }

        /// <summary>Probability in [0, 1] that a datagram is dropped.</summary>
        public double LossChance { get; set; }

        /// <summary>Probability in [0, 1] that a datagram is delivered twice.</summary>
        public double DuplicateChance { get; set; }

        /// <summary>When true every datagram is dropped (a total outage).</summary>
        public bool Blackhole { get; set; }

        public static LinkConditions Perfect => new LinkConditions();
    }

    /// <summary>Network impairment for both directions, with the seed of the impairment decisions.</summary>
    public sealed class NetworkConditions
    {
        public LinkConditions ClientToServer { get; set; } = new LinkConditions();

        public LinkConditions ServerToClient { get; set; } = new LinkConditions();

        public ulong Seed { get; set; } = 1;

        /// <summary>Same latency, jitter, and loss in both directions.</summary>
        public static NetworkConditions Symmetric(int latencyMs, int jitterMs, double lossChance, ulong seed = 1)
        {
            return new NetworkConditions
            {
                ClientToServer = new LinkConditions { LatencyMs = latencyMs, JitterMs = jitterMs, LossChance = lossChance },
                ServerToClient = new LinkConditions { LatencyMs = latencyMs, JitterMs = jitterMs, LossChance = lossChance },
                Seed = seed,
            };
        }
    }

    /// <summary>
    /// UDP relay on 127.0.0.1 that impairs traffic between clients and a server: latency, jitter (and so
    /// reordering), loss, duplication, and outages, decided by a seeded <see cref="Rng"/>. Clients connect to
    /// <see cref="Port"/>; each distinct client endpoint gets its own upstream socket, so the server still sees
    /// one endpoint per client. Pumped by <see cref="Poll"/> on the caller's thread, like the transports.
    /// <para>
    /// LiteNetLib's own SimulatePacketLoss and SimulateLatency only work in DEBUG builds of the library (or with
    /// SIMULATE_NETWORK); the release NuGet build used here ignores them (verified: 100% configured loss still
    /// delivered every packet), so impairment happens at the socket level instead.
    /// </para>
    /// </summary>
    public sealed class UdpLinkSimulator : IDisposable
    {
        private readonly Socket _front;
        private readonly IPEndPoint _server;
        private readonly INetClock _clock;
        private readonly Rng _rng;
        private readonly List<Route> _routes = new List<Route>();
        private readonly List<Pending> _inFlight = new List<Pending>();
        private readonly byte[] _buffer = new byte[65536];
        private long _sequence;

        public UdpLinkSimulator(int serverPort, NetworkConditions conditions, INetClock clock = null)
        {
            Conditions = conditions ?? new NetworkConditions();
            _clock = clock ?? SystemNetClock.Instance;
            _rng = new Rng(Conditions.Seed);
            _server = new IPEndPoint(IPAddress.Loopback, serverPort);
            _front = CreateSocket();
        }

        public NetworkConditions Conditions { get; }

        /// <summary>Port clients connect to.</summary>
        public int Port => ((IPEndPoint)_front.LocalEndPoint).Port;

        public long Forwarded { get; private set; }

        public long Dropped { get; private set; }

        public long Duplicated { get; private set; }

        /// <summary>Receives waiting datagrams, applies the conditions, and sends every datagram that is due.</summary>
        public void Poll()
        {
            long now = _clock.NowMs;
            ReceiveFront(now);
            for (int i = 0; i < _routes.Count; i++)
            {
                ReceiveUpstream(_routes[i], now);
            }

            Deliver(now);
        }

        public void Dispose()
        {
            _front.Dispose();
            foreach (Route r in _routes)
            {
                r.Upstream.Dispose();
            }

            _routes.Clear();
            _inFlight.Clear();
        }

        private void ReceiveFront(long now)
        {
            while (_front.Available > 0)
            {
                EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                int n;
                try
                {
                    n = _front.ReceiveFrom(_buffer, ref from);
                }
                catch (SocketException)
                {
                    break; // ICMP port unreachable surfaces as a reset on Windows; nothing to forward
                }

                Route route = RouteFor((IPEndPoint)from);
                Schedule(route.Upstream, _server, n, Conditions.ClientToServer, now);
            }
        }

        private void ReceiveUpstream(Route route, long now)
        {
            while (route.Upstream.Available > 0)
            {
                EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                int n;
                try
                {
                    n = route.Upstream.ReceiveFrom(_buffer, ref from);
                }
                catch (SocketException)
                {
                    break;
                }

                Schedule(_front, route.Client, n, Conditions.ServerToClient, now);
            }
        }

        private void Schedule(Socket via, IPEndPoint to, int length, LinkConditions link, long now)
        {
            if (link.Blackhole || (link.LossChance > 0 && _rng.NextDouble() < link.LossChance))
            {
                Dropped++;
                return;
            }

            int copies = link.DuplicateChance > 0 && _rng.NextDouble() < link.DuplicateChance ? 2 : 1;
            if (copies == 2) Duplicated++;
            var data = new byte[length];
            Buffer.BlockCopy(_buffer, 0, data, 0, length);
            for (int c = 0; c < copies; c++)
            {
                long jitter = link.JitterMs > 0 ? _rng.NextInt(link.JitterMs + 1) : 0;
                _inFlight.Add(new Pending(now + link.LatencyMs + jitter, _sequence++, via, to, data));
            }
        }

        private void Deliver(long now)
        {
            if (_inFlight.Count == 0) return;
            // Due datagrams leave in due-time order (ties by arrival), so jitter reorders deterministically.
            _inFlight.Sort((a, b) => a.DueMs != b.DueMs ? a.DueMs.CompareTo(b.DueMs) : a.Sequence.CompareTo(b.Sequence));
            int sent = 0;
            while (sent < _inFlight.Count && _inFlight[sent].DueMs <= now)
            {
                Pending p = _inFlight[sent];
                try
                {
                    p.Via.SendTo(p.Data, p.To);
                    Forwarded++;
                }
                catch (SocketException)
                {
                    Dropped++;
                }

                sent++;
            }

            _inFlight.RemoveRange(0, sent);
        }

        private Route RouteFor(IPEndPoint client)
        {
            foreach (Route r in _routes)
            {
                if (r.Client.Equals(client)) return r;
            }

            var route = new Route(new IPEndPoint(client.Address, client.Port), CreateSocket());
            _routes.Add(route);
            return route;
        }

        private static Socket CreateSocket()
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Blocking = false;
            DisableConnectionReset(socket);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            return socket;
        }

        private static void DisableConnectionReset(Socket socket)
        {
            // Windows reports ICMP "port unreachable" as WSAECONNRESET on later receives (SIO_UDP_CONNRESET).
            const int SioUdpConnReset = -1744830452;
            try
            {
                socket.IOControl(SioUdpConnReset, new byte[] { 0 }, null);
            }
            catch (PlatformNotSupportedException)
            {
            }
            catch (SocketException)
            {
            }
        }

        private sealed class Route
        {
            public Route(IPEndPoint client, Socket upstream)
            {
                Client = client;
                Upstream = upstream;
            }

            public IPEndPoint Client { get; }

            public Socket Upstream { get; }
        }

        private struct Pending
        {
            public Pending(long dueMs, long sequence, Socket via, IPEndPoint to, byte[] data)
            {
                DueMs = dueMs;
                Sequence = sequence;
                Via = via;
                To = to;
                Data = data;
            }

            public long DueMs { get; }

            public long Sequence { get; }

            public Socket Via { get; }

            public IPEndPoint To { get; }

            public byte[] Data { get; }
        }
    }
}
