using System;
using System.Collections.Generic;
using System.Net;
using Confluence.Arena.Generation;
using Confluence.Core.Content;
using Confluence.Hosting.Arenas;
using Confluence.Hosting.Bots;
using Confluence.Net.Diagnostics;
using Confluence.Net.Game.Headless;
using Confluence.Net.Game.Server;
using Confluence.Net.Protocol;
using Confluence.Net.Transport;

namespace Confluence.Net.Tests.TestSupport
{
    /// <summary>
    /// An in-process game server on real UDP loopback plus headless clients, all driven from the test thread on one
    /// virtual clock (the labeled accelerated test pace: each <see cref="Step"/> is one authoritative tick, 1/60 s of
    /// virtual time, with no sleeping). Transport timers, clock sync, rate limits, and reconnect windows all run on the
    /// same virtual clock, so the session behaves as in real time, only faster.
    /// </summary>
    public sealed class GameRig : IDisposable
    {
        private static readonly Lazy<Catalogue> LazyCatalogue = new Lazy<Catalogue>(() => Catalogue.Load(Catalogue.FindDefaultPath()));
        private static readonly Lazy<ArenaGenerator> LazyGenerator = new Lazy<ArenaGenerator>(() => new ArenaGenerator(LazyCatalogue.Value));

        public static Catalogue Catalogue => LazyCatalogue.Value;
        public static ArenaGenerator Generator => LazyGenerator.Value;
        public static ArenaCache ServerArenas { get; } = new ArenaCache();

        private readonly List<HeadlessPlayer> _players = new List<HeadlessPlayer>();
        private long _ticks;

        public GameRig(GameServerSettings settings, NetworkConditions conditions = null)
        {
            Clock = new ManualNetClock(1000);
            Log = new List<string>();
            settings.Transport = settings.Transport ?? FastTransport();
            settings.Log = settings.Log ?? (line => { lock (Log) Log.Add(line); });
            Server = new GameServer(Catalogue, Generator, LoopbackRig.Ruleset, settings, Clock, ServerArenas, new SeededTokenSource(11));
            Server.Start(0, IPAddress.Loopback);
            if (conditions != null) Link = new UdpLinkSimulator(Server.Transport.Port, conditions, Clock);
        }

        public ManualNetClock Clock { get; }
        public GameServer Server { get; }
        public UdpLinkSimulator Link { get; }
        public List<string> Log { get; }
        public IReadOnlyList<HeadlessPlayer> Players => _players;

        public IPEndPoint Endpoint => new IPEndPoint(IPAddress.Loopback, Link?.Port ?? Server.Transport.Port);

        /// <summary>Ticks stepped so far.</summary>
        public long Ticks => _ticks;

        public static TransportSettings FastTransport()
        {
            return new TransportSettings
            {
                DisconnectTimeoutMs = 5000,
                PingIntervalMs = 250,
                RethrowHandlerExceptions = true,
            };
        }

        public HeadlessPlayer AddPlayer(string name, string wizard, BotCompetence competence = BotCompetence.Adept, ulong seed = 1, TransportSettings transport = null,
            bool autoEconomy = true, bool downloadArenas = false)
        {
            var player = new HeadlessPlayer(new HeadlessPlayerOptions
            {
                Name = name,
                WizardId = wizard,
                Competence = competence,
                Seed = seed,
                Catalogue = Catalogue,
                Generator = Generator,
                Ruleset = LoopbackRig.Ruleset,
                Transport = transport ?? FastTransport(),
                Clock = Clock,
                AutoSpectate = true,
                AutoEconomy = autoEconomy,
                DownloadArenas = downloadArenas,
                Log = line => { lock (Log) Log.Add(line); },
            });
            player.Connect(Endpoint);
            _players.Add(player);
            return player;
        }

        /// <summary>One virtual tick: advance the clock 1/60 s, pump the server (one authoritative step) and every client.</summary>
        public void Step()
        {
            _ticks++;
            long target = 1000 + (_ticks * 1000) / 60;
            Clock.Advance(target - Clock.NowMs);
            Link?.Poll();
            Server.Poll();
            Server.Tick();
            Server.Flush();
            Link?.Poll();
            foreach (HeadlessPlayer p in _players) p.Update();
            Link?.Poll();
        }

        /// <summary>Steps until <paramref name="condition"/> holds; false after <paramref name="maxTicks"/>.</summary>
        public bool RunUntil(Func<bool> condition, int maxTicks)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                if (condition()) return true;
                Step();
            }

            return condition();
        }

        /// <summary>Pumps without advancing the clock (lets loopback packets land), for at most the given real time.</summary>
        public void Settle(int iterations = 5)
        {
            for (int i = 0; i < iterations; i++)
            {
                Link?.Poll();
                Server.Poll();
                Server.Flush();
                foreach (HeadlessPlayer p in _players) p.Client.Update();
                System.Threading.Thread.Sleep(1);
            }
        }

        public void Dispose()
        {
            foreach (HeadlessPlayer p in _players) p.Dispose();
            Server.Dispose();
            Link?.Dispose();
        }
    }
}
