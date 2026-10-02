using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using Confluence.Core.Content;
using Confluence.Net.Diagnostics;
using Confluence.Net.Protocol;
using Confluence.Net.Transport;

namespace Confluence.Net.Tests.TestSupport
{
    /// <summary>
    /// A real server and clients on 127.0.0.1 with ephemeral ports, optionally through a
    /// <see cref="UdpLinkSimulator"/>. Everything is pumped from the test thread; waits poll a condition with
    /// a timeout instead of sleeping for fixed periods.
    /// </summary>
    public sealed class LoopbackRig : IDisposable
    {
        private static readonly Lazy<RulesetFingerprint> RealRuleset =
            new Lazy<RulesetFingerprint>(() => RulesetFingerprint.ForCurrentRules(Catalogue.FindDefaultPath()));

        private readonly List<ClientTransport> _clients = new List<ClientTransport>();

        public LoopbackRig(TransportSettings settings = null, NetworkConditions conditions = null, Action<ServerTransport> configure = null)
        {
            Settings = settings ?? FastSettings();
            ServerClock = new OffsetNetClock();
            Server = new ServerTransport(new ServerIdentity(Ruleset), Settings, ServerClock, new SeededTokenSource(7));
            configure?.Invoke(Server);
            Server.Start(0, IPAddress.Loopback);
            if (conditions != null)
            {
                Link = new UdpLinkSimulator(Server.Port, conditions);
            }
        }

        /// <summary>Fingerprint of the real catalogue with the current rules version.</summary>
        public static RulesetFingerprint Ruleset => RealRuleset.Value;

        public TransportSettings Settings { get; }

        /// <summary>The server's clock: real time plus whatever a test skips forward.</summary>
        public OffsetNetClock ServerClock { get; }

        public ServerTransport Server { get; }

        public UdpLinkSimulator Link { get; }

        /// <summary>Endpoint clients dial: the link simulator when present, else the server.</summary>
        public IPEndPoint Endpoint => new IPEndPoint(IPAddress.Loopback, Link?.Port ?? Server.Port);

        /// <summary>Short timeouts and rethrown handler exceptions.</summary>
        public static TransportSettings FastSettings()
        {
            return new TransportSettings
            {
                DisconnectTimeoutMs = 3000,
                PingIntervalMs = 250,
                RethrowHandlerExceptions = true,
            };
        }

        public ClientTransport NewClient(string name = "Bram", ClientIdentity identity = null, TransportSettings settings = null)
        {
            var client = new ClientTransport(identity ?? new ClientIdentity(name, Ruleset, "test-build"), settings ?? Settings);
            _clients.Add(client);
            return client;
        }

        /// <summary>Creates a client, connects it, and waits until it is connected or rejected.</summary>
        public ClientTransport Connect(string name = "Bram", ClientIdentity identity = null, TransportSettings settings = null)
        {
            ClientTransport client = NewClient(name, identity, settings);
            client.Connect(Endpoint);
            WaitSettled(client);
            return client;
        }

        public void WaitSettled(ClientTransport client, int timeoutMs = 10000)
        {
            bool settled = PumpUntil(() => client.State == ClientState.Connected || client.State == ClientState.Rejected
                || (client.State == ClientState.Disconnected && client.LastDisconnectCause == DisconnectCause.ConnectionFailed), timeoutMs);
            if (!settled)
            {
                throw new TimeoutException("Client did not settle; state " + client.State);
            }
        }

        public void PumpOnce()
        {
            Link?.Poll();
            Server.Poll();
            foreach (ClientTransport c in _clients)
            {
                c.Poll();
            }

            Server.Flush();
            foreach (ClientTransport c in _clients)
            {
                c.Flush();
            }

            Link?.Poll();
        }

        /// <summary>Pumps until <paramref name="condition"/> holds; false on timeout.</summary>
        public bool PumpUntil(Func<bool> condition, int timeoutMs = 5000)
        {
            var watch = Stopwatch.StartNew();
            while (!condition())
            {
                if (watch.ElapsedMilliseconds > timeoutMs)
                {
                    return false;
                }

                PumpOnce();
                Thread.Sleep(1);
            }

            return true;
        }

        /// <summary>Pumps for a fixed span (only where a test must observe that something does not happen).</summary>
        public void PumpFor(int milliseconds)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < milliseconds)
            {
                PumpOnce();
                Thread.Sleep(1);
            }
        }

        public void Dispose()
        {
            foreach (ClientTransport c in _clients)
            {
                c.Dispose();
            }

            Server.Dispose();
            Link?.Dispose();
        }
    }
}
