using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Confluence.Arena.Generation;
using Confluence.Casting.Grammars;
using Confluence.Core.Content;
using Confluence.Hosting.Bots;
using Confluence.Match;
using Confluence.Net.Game.Headless;
using Confluence.Net.Game.Wire;
using Confluence.Net.Protocol;
using Confluence.Net.Transport;

namespace Confluence.HeadlessClient
{
    /// <summary>
    /// A scripted network player for automated validation: connects, joins with a wizard, shops and drafts with simple
    /// heuristics, and plays every duel with a bot driving inputs from the replicated view (no server state). Exit code
    /// 0 when the match ended normally, 2 when the session ended early (refused, disconnected), 3 on the time limit.
    /// </summary>
    public static class Program
    {
        private const string Usage =
            "Confluence.HeadlessClient [options]\n" +
            "  --host <host>          server host (default 127.0.0.1)\n" +
            "  --port <n>             server UDP port (default 7777)\n" +
            "  --name <name>          display name (default Headless)\n" +
            "  --wizard <WIZ-nnn>     wizard to join with (default WIZ-002)\n" +
            "  --route <route>        input alternative: standard|guided|rhythm|voice|directionkeys (default standard)\n" +
            "  --competence <c>       pilot competence novice|apprentice|adept|expert (default adept)\n" +
            "  --seed <n>             pilot RNG seed (default 1)\n" +
            "  --catalogue <path>     catalogue JSON (default: docs/confluence/confluence-catalogue.json)\n" +
            "  --max-seconds <n>      give up after n wall-clock seconds (default 1800)\n" +
            "  --status-seconds <n>   print a status line every n seconds (default 10)\n" +
            "  --no-spectate          do not follow other duels when outside one\n";

        public static int Main(string[] args)
        {
            string host = "127.0.0.1";
            int port = 7777;
            string name = "Headless";
            string wizard = "WIZ-002";
            InputRoute route = InputRoute.Standard;
            BotCompetence competence = BotCompetence.Adept;
            ulong seed = 1;
            string cataloguePath = null;
            double maxSeconds = 1800;
            double statusSeconds = 10;
            bool spectate = true;
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "--host": host = Value(args, ref i); break;
                        case "--port": port = int.Parse(Value(args, ref i), CultureInfo.InvariantCulture); break;
                        case "--name": name = Value(args, ref i); break;
                        case "--wizard": wizard = Value(args, ref i); break;
                        case "--route":
                            if (!Enum.TryParse(Value(args, ref i), true, out route)) throw new ArgumentException("unknown route");
                            break;
                        case "--competence":
                            if (!Enum.TryParse(Value(args, ref i), true, out competence)) throw new ArgumentException("unknown competence");
                            break;
                        case "--seed": seed = ulong.Parse(Value(args, ref i), CultureInfo.InvariantCulture); break;
                        case "--catalogue": cataloguePath = Value(args, ref i); break;
                        case "--max-seconds": maxSeconds = double.Parse(Value(args, ref i), CultureInfo.InvariantCulture); break;
                        case "--status-seconds": statusSeconds = double.Parse(Value(args, ref i), CultureInfo.InvariantCulture); break;
                        case "--no-spectate": spectate = false; break;
                        case "--help":
                        case "-h":
                            Console.WriteLine(Usage);
                            return 0;
                        default: throw new ArgumentException("unknown option " + args[i]);
                    }
                }
            }
            catch (Exception e) when (e is ArgumentException || e is FormatException || e is OverflowException)
            {
                Console.Error.WriteLine(e.Message);
                Console.Error.WriteLine(Usage);
                return 64;
            }

            var wall = Stopwatch.StartNew();
            Action<string> log = line => Console.WriteLine("[" + wall.Elapsed.TotalSeconds.ToString("0000.000", CultureInfo.InvariantCulture) + "] " + line);
            string path = cataloguePath ?? Catalogue.FindDefaultPath();
            Catalogue catalogue = Catalogue.Load(path);
            var player = new HeadlessPlayer(new HeadlessPlayerOptions
            {
                Name = name,
                WizardId = wizard,
                Route = route,
                Competence = competence,
                Seed = seed,
                Catalogue = catalogue,
                Generator = new ArenaGenerator(catalogue),
                Ruleset = RulesetFingerprint.ForCurrentRules(path),
                Transport = new TransportSettings { AutoReconnect = true },
                AutoSpectate = spectate,
                Log = log,
            });

            using (player)
            {
                player.Client.PhaseChanged += (c, p) => log(name + ": phase " + p.Phase + " round " + p.Round + (p.Attempt > 0 ? " attempt " + p.Attempt : ""));
                player.Client.DuelEnded += (c, d) => log(name + ": duel " + d.DuelId + " ended (hits " + d.HitsConfirmed + ", dealt " +
                    d.DamageDealt.ToString("0.0", CultureInfo.InvariantCulture) + ", taken " + d.DamageTaken.ToString("0.0", CultureInfo.InvariantCulture) +
                    ", snapshots " + d.SnapshotsReceived + ", max correction " + d.MaxCorrection.ToString("0.000", CultureInfo.InvariantCulture) + " m)");
                log(name + ": connecting to " + host + ":" + port + " as " + wizard + " (" + route + ")");
                player.Connect(host, port);
                double nextStatus = statusSeconds;
                while (true)
                {
                    player.Update();
                    if (player.MatchOver)
                    {
                        // Keep pumping briefly so the final acknowledgements go out.
                        var drain = Stopwatch.StartNew();
                        while (drain.ElapsedMilliseconds < 500)
                        {
                            player.Update();
                            Thread.Sleep(2);
                        }

                        log("MATCH OVER " + player.Summary());
                        return 0;
                    }

                    ClientTransport transport = player.Client.Transport;
                    if (transport.State == ClientState.Rejected)
                    {
                        log(name + ": rejected: " + transport.Rejection);
                        return 2;
                    }

                    if (transport.State == ClientState.Disconnected && (transport.LastDisconnectCause == DisconnectCause.ServerShutdown ||
                        transport.LastDisconnectCause == DisconnectCause.Kicked || transport.LastDisconnectCause == DisconnectCause.LocalDisconnect))
                    {
                        log(name + ": session ended: " + transport.LastDisconnectCause + " " + transport.LastDisconnectDetail);
                        return 2;
                    }

                    if (player.Client.View.Lobby != null && player.Client.View.Lobby.Status == LobbyStatus.Refused)
                    {
                        log(name + ": the server refused to start: " + player.Client.View.Lobby.Notice);
                        return 2;
                    }

                    double t = wall.Elapsed.TotalSeconds;
                    if (t >= maxSeconds)
                    {
                        log(name + ": time limit reached. " + player.Summary());
                        return 3;
                    }

                    if (statusSeconds > 0 && t >= nextStatus)
                    {
                        nextStatus += statusSeconds;
                        MatchPhase phase = player.Client.View.CurrentPhase;
                        log(name + ": status " + transport.State + ", phase " + phase + ", rtt " + transport.RttMs + " ms, server tick " +
                            player.Client.ServerTick.ToString("0", CultureInfo.InvariantCulture) + ". " + player.Summary());
                    }

                    Thread.Sleep(1);
                }
            }
        }

        private static string Value(string[] args, ref int i)
        {
            if (i + 1 >= args.Length) throw new ArgumentException(args[i] + " needs a value");
            return args[++i];
        }
    }
}
