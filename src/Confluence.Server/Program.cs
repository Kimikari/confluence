using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;
using Confluence.Arena.Generation;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Hosting.Bots;
using Confluence.Net.Game.Server;
using Confluence.Net.Protocol;

namespace Confluence.Server
{
    /// <summary>
    /// The authoritative Confluence match server (spec §14): a lobby on UDP, then one match hosted at 60 ticks per
    /// second in real time. See <see cref="Usage"/> for the command line.
    /// </summary>
    public static class Program
    {
        private const string Usage =
            "Confluence.Server [options]\n" +
            "  --port <n>             UDP port (default 7777; 0 = ephemeral)\n" +
            "  --bind <ip>            bind address (default 0.0.0.0)\n" +
            "  --mode duel|conclave   match mode (default duel)\n" +
            "  --players <n>          seats: 2 for a Duel, 2-8 for a Conclave (default 2 / 8)\n" +
            "  --bots <n>             seats filled by server bots (default 0)\n" +
            "  --bot-competence <c>   novice|apprentice|adept|expert (default adept)\n" +
            "  --seed <n>             recorded match seed (default 1)\n" +
            "  --lab                  Laboratory overrides: bypass economy floors, whole-catalogue pools, grammar-route wizards\n" +
            "  --accelerated <x>      LABELED TEST MODE: run x times faster than real time (2-64)\n" +
            "  --spectator-delay <s>  spectator delay in seconds (never below 15 while the match runs)\n" +
            "  --catalogue <path>     catalogue JSON (default: docs/confluence/confluence-catalogue.json)\n" +
            "  --log <file>           also append the log to a file\n" +
            "  --max-seconds <n>      stop after n wall-clock seconds\n" +
            "  --stay                 keep running after the match ends (default: exit)\n";

        public static int Main(string[] args)
        {
            Options o;
            try
            {
                o = Options.Parse(args);
            }
            catch (ArgumentException e)
            {
                Console.Error.WriteLine(e.Message);
                Console.Error.WriteLine(Usage);
                return 64;
            }

            if (o.Help)
            {
                Console.WriteLine(Usage);
                return 0;
            }

            StreamWriter file = o.LogFile != null ? new StreamWriter(o.LogFile, true) { AutoFlush = true } : null;
            var clock = Stopwatch.StartNew();
            object gate = new object();
            Action<string> log = line =>
            {
                string stamped = "[" + clock.Elapsed.TotalSeconds.ToString("0000.000", CultureInfo.InvariantCulture) + "] " + line;
                lock (gate)
                {
                    Console.WriteLine(stamped);
                    file?.WriteLine(stamped);
                }
            };

            try
            {
                string path = o.CataloguePath ?? Catalogue.FindDefaultPath();
                log("Loading catalogue " + path);
                Catalogue catalogue = Catalogue.Load(path);
                var generator = new ArenaGenerator(catalogue);
                RulesetFingerprint ruleset = RulesetFingerprint.ForCurrentRules(path);
                var settings = new GameServerSettings
                {
                    Mode = o.Mode,
                    PlayerCount = o.Players,
                    Bots = o.Bots,
                    BotCompetence = o.Competence,
                    Seed = o.Seed,
                    Laboratory = o.Laboratory,
                    TicksPerWallSecond = 60 * o.Accelerated,
                    SpectatorDelaySec = o.SpectatorDelaySec,
                    Log = log,
                };

                using (var server = new GameServer(catalogue, generator, ruleset, settings))
                {
                    int port = server.Start(o.Port, o.Bind);
                    log("Confluence server on " + o.Bind + ":" + port + " — waiting for " + (o.Players - o.Bots) + " player(s).");
                    if (o.Accelerated != 1) log("WARNING: accelerated test mode x" + o.Accelerated + " — not a real-time game server.");
                    var stop = new ManualResetEventSlim(false);
                    Console.CancelKeyPress += (s, e) =>
                    {
                        e.Cancel = true;
                        stop.Set();
                    };

                    var runner = new GameServerRunner(server, null, log);
                    var wall = Stopwatch.StartNew();
                    runner.Run(() => !stop.IsSet && (o.MaxSeconds <= 0 || wall.Elapsed.TotalSeconds < o.MaxSeconds) && (o.Stay || !server.IsFinished),
                        drainMs: 0);
                    if (server.IsFinished)
                    {
                        // Let the final results reach every client before closing.
                        var drain = Stopwatch.StartNew();
                        while (drain.ElapsedMilliseconds < 2000)
                        {
                            server.Poll();
                            server.Flush();
                            Thread.Sleep(5);
                        }
                    }

                    string reason = stop.IsSet ? "operator stop" : server.IsFinished ? "match finished" : "time limit";
                    server.Shutdown(reason);
                    log("Server stopped (" + reason + "; " + server.CurrentTick + " ticks, " + server.SnapshotsSent + " snapshots, " +
                        runner.Stalls + " stalls).");
                    return server.Status == Net.Game.Wire.LobbyStatus.Refused ? 2 : 0;
                }
            }
            catch (Exception e)
            {
                log("FATAL: " + e);
                return 1;
            }
            finally
            {
                file?.Dispose();
            }
        }

        private sealed class Options
        {
            public int Port = 7777;
            public IPAddress Bind = IPAddress.Any;
            public MatchMode Mode = MatchMode.Duel;
            public int Players = -1;
            public int Bots;
            public BotCompetence Competence = BotCompetence.Adept;
            public ulong Seed = 1;
            public bool Laboratory;
            public int Accelerated = 1;
            public float SpectatorDelaySec = GlobalRules.Match.SpectatorDelayMinSec;
            public string CataloguePath;
            public string LogFile;
            public double MaxSeconds;
            public bool Stay;
            public bool Help;

            public static Options Parse(string[] args)
            {
                var o = new Options();
                for (int i = 0; i < args.Length; i++)
                {
                    string a = args[i];
                    switch (a)
                    {
                        case "--port": o.Port = Int(args, ref i, 0, 65535); break;
                        case "--bind": o.Bind = IPAddress.Parse(Value(args, ref i)); break;
                        case "--mode":
                            string mode = Value(args, ref i).ToLowerInvariant();
                            if (mode == "duel") o.Mode = MatchMode.Duel;
                            else if (mode == "conclave") o.Mode = MatchMode.Conclave;
                            else throw new ArgumentException("--mode is duel or conclave");
                            break;
                        case "--players": o.Players = Int(args, ref i, 2, GlobalRules.Match.LobbySize); break;
                        case "--bots": o.Bots = Int(args, ref i, 0, GlobalRules.Match.LobbySize); break;
                        case "--bot-competence":
                            if (!Enum.TryParse(Value(args, ref i), true, out o.Competence)) throw new ArgumentException("unknown competence");
                            break;
                        case "--seed": o.Seed = ulong.Parse(Value(args, ref i), CultureInfo.InvariantCulture); break;
                        case "--lab": o.Laboratory = true; break;
                        case "--accelerated": o.Accelerated = Int(args, ref i, 1, 64); break;
                        case "--spectator-delay": o.SpectatorDelaySec = float.Parse(Value(args, ref i), CultureInfo.InvariantCulture); break;
                        case "--catalogue": o.CataloguePath = Value(args, ref i); break;
                        case "--log": o.LogFile = Value(args, ref i); break;
                        case "--max-seconds": o.MaxSeconds = double.Parse(Value(args, ref i), CultureInfo.InvariantCulture); break;
                        case "--stay": o.Stay = true; break;
                        case "--help":
                        case "-h": o.Help = true; break;
                        default: throw new ArgumentException("unknown option " + a);
                    }
                }

                if (o.Players < 0) o.Players = o.Mode == MatchMode.Duel ? 2 : GlobalRules.Match.LobbySize;
                if (o.Mode == MatchMode.Duel && o.Players != 2) throw new ArgumentException("a Duel has 2 seats");
                if (o.Bots > o.Players) throw new ArgumentException("--bots exceeds --players");
                return o;
            }

            private static string Value(string[] args, ref int i)
            {
                if (i + 1 >= args.Length) throw new ArgumentException(args[i] + " needs a value");
                return args[++i];
            }

            private static int Int(string[] args, ref int i, int min, int max)
            {
                string flag = args[i];
                int v = int.Parse(Value(args, ref i), CultureInfo.InvariantCulture);
                if (v < min || v > max) throw new ArgumentException(flag + " must be " + min + "-" + max);
                return v;
            }
        }
    }
}
