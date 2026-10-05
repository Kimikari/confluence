using System;
using System.Globalization;
using System.Text;
using Confluence.Arena.Generation;
using Confluence.Casting.Grammars;
using Confluence.Core.Content;
using Confluence.Hosting.Bots;
using Confluence.Net.Game.Client;
using Confluence.Net.Game.Wire;
using Confluence.Net.Protocol;
using Confluence.Net.Transport;
using Confluence.Sim.Content;

namespace Confluence.Net.Game.Headless
{
    public sealed class HeadlessPlayerOptions
    {
        public string Name { get; set; } = "Headless";
        public string WizardId { get; set; } = "WIZ-002";
        public InputRoute Route { get; set; } = InputRoute.Standard;
        public BotCompetence Competence { get; set; } = BotCompetence.Adept;

        /// <summary>Seed of the pilot's Bot RNG stream (spec L435: bots use their own stream).</summary>
        public ulong Seed { get; set; } = 1;

        public Catalogue Catalogue { get; set; }
        public ArenaGenerator Generator { get; set; }
        public RulesetFingerprint Ruleset { get; set; }
        public TransportSettings Transport { get; set; }
        public INetClock Clock { get; set; }
        public bool AutoSpectate { get; set; } = true;

        /// <summary>Shop and draft automatically (false: the caller sends commands itself, as tests do).</summary>
        public bool AutoEconomy { get; set; } = true;

        public Action<string> Log { get; set; }
    }

    /// <summary>
    /// A complete scripted network player: the <see cref="GameClient"/> session, <see cref="HeadlessEconomy"/> shop and
    /// draft heuristics, and a <see cref="ViewPilot"/> driving duel inputs from the replicated view. Used by the headless
    /// client executable and by the end-to-end tests.
    /// </summary>
    public sealed class HeadlessPlayer : IDisposable
    {
        public HeadlessPlayer(HeadlessPlayerOptions options)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            Client = new GameClient(new GameClientOptions
            {
                DisplayName = options.Name,
                WizardId = options.WizardId,
                Route = options.Route,
                Catalogue = options.Catalogue,
                Generator = options.Generator,
                Content = ContentRegistry.Default,
                Ruleset = options.Ruleset,
                Transport = options.Transport,
                Clock = options.Clock,
                AutoSpectate = options.AutoSpectate,
                Log = options.Log,
            });
            Economy = new HeadlessEconomy(Client);
            Pilot = new ViewPilot(BotProfile.For(options.Competence), options.Seed, 0, options.Catalogue);
            Client.InputProvider = Pilot.NextInput;
        }

        public HeadlessPlayerOptions Options { get; }
        public GameClient Client { get; }
        public HeadlessEconomy Economy { get; }
        public ViewPilot Pilot { get; }

        public bool MatchOver => Client.View.End != null;

        public void Connect(string host, int port) => Client.Connect(host, port);

        public void Connect(System.Net.IPEndPoint endpoint) => Client.Connect(endpoint);

        public void Update()
        {
            Client.Update();
            if (Options.AutoEconomy) Economy.Act();
        }

        public string Summary()
        {
            ClientMatchView v = Client.View;
            var sb = new StringBuilder();
            sb.Append(Options.Name).Append(" seat ").Append(v.Seat.ToString(CultureInfo.InvariantCulture));
            sb.Append(": rounds seen ").Append(v.Results.Count).Append(", purchases ").Append(Economy.Purchases);
            sb.Append(", drafts ").Append(Economy.DraftSelections).Append('/').Append(Economy.DraftPasses);
            sb.Append(", readies ").Append(Economy.Readies).Append(", casts ").Append(Pilot.Casts).Append(", melee ").Append(Pilot.MeleePresses);
            sb.Append(", inputs ").Append(Client.InputsSent);
            if (Client.Duel != null)
            {
                sb.Append(", last duel snapshots ").Append(Client.Duel.SnapshotsReceived).Append(" (").Append(Client.Duel.DeltaSnapshots).Append(" delta)");
                sb.Append(", hits ").Append(Client.Duel.HitsConfirmed).Append(", max correction ").Append(Client.Duel.MaxCorrection.ToString("0.000", CultureInfo.InvariantCulture)).Append(" m");
            }

            if (v.End != null)
            {
                foreach (PlacementWire p in v.End.Placements)
                    if (p.Seat == v.Seat) sb.Append(", placement ").Append(p.FirstPosition).Append(p.LastPosition != p.FirstPosition ? "-" + p.LastPosition : "");
                sb.Append(", champion seat ").Append(v.End.ChampionSeat);
            }

            return sb.ToString();
        }

        public void Dispose() => Client.Dispose();
    }
}
