using System;
using System.Collections.Generic;
using System.Globalization;
using Confluence.Arena.Generation;
using Confluence.Core.Content;
using Confluence.Core.Random;
using Confluence.Core.Rules;
using Confluence.Economy;
using Confluence.Economy.Augments;
using Confluence.Economy.Loadouts;
using Confluence.Economy.Shop;
using Confluence.Hosting.Arenas;
using Confluence.Hosting.Bots;
using Confluence.Hosting.Commands;
using Confluence.Hosting.Content;
using Confluence.Hosting.Duels;
using Confluence.Hosting.Economics;
using Confluence.Hosting.Messages;
using Confluence.Match;
using Confluence.Sim.Input;

namespace Confluence.Hosting
{
    /// <summary>Everything a host needs to create one match.</summary>
    public sealed class MatchHostSettings
    {
        public MatchMode Mode { get; set; } = MatchMode.Conclave;

        /// <summary>Recorded match seed; every random choice of the match derives from it (L435).</summary>
        public ulong MatchSeed { get; set; }

        /// <summary>Lobby rules of a Conclave (ignored for Duel).</summary>
        public ConclaveSettings Conclave { get; set; } = ConclaveSettings.Unranked;

        /// <summary>Options of a Duel (ignored for Conclave).</summary>
        public DuelMatchOptions Duel { get; set; } = DuelMatchOptions.Standard;

        /// <summary>The wizard each lobby slot chose, by player id (spec L292).</summary>
        public IReadOnlyList<string> WizardIds { get; set; }

        /// <summary>Account handles by player id (Sim player ids, logs); null gives "P0", "P1", ...</summary>
        public IReadOnlyList<string> PlayerIds { get; set; }

        /// <summary>Development and Laboratory deviations; recorded in the host fingerprint (L69, L71).</summary>
        public LaboratoryOverrides Overrides { get; set; } = LaboratoryOverrides.None;

        /// <summary>Arena biomes used while no biome carries a behavior class (null = every catalogue biome).</summary>
        public IEnumerable<string> ConfiguredBiomes { get; set; }

        /// <summary>Arena props used while no prop carries a behavior class (null = every catalogue prop).</summary>
        public IEnumerable<string> ConfiguredProps { get; set; }

        /// <summary>The transport's ruleset fingerprint, written into replay manifests (L427).</summary>
        public string RulesetFingerprint { get; set; } = string.Empty;

        public bool RecordReplays { get; set; } = true;

        /// <summary>
        /// Step running duels in parallel before Match polls them. Duels are independent, so the result is the same as
        /// the default single-threaded stepping; input sources must then be thread-safe per duel.
        /// </summary>
        public bool ParallelDuels { get; set; }

        /// <summary>Command rate limits (null = <see cref="TickRateLimiter"/>).</summary>
        public ICommandRateLimiter RateLimiter { get; set; }

        /// <summary>Creates duel worlds; the casting and reactions seams plug in here (null = Sim defaults).</summary>
        public ISimWorldFactory WorldFactory { get; set; }

        /// <summary>
        /// Scheduled rounds whose arenas are generated in the background ahead of play (default 1: the next round while
        /// the current one runs). Results never depend on it.
        /// </summary>
        public int ArenaPrefetchRounds { get; set; } = 1;
    }

    public enum HostStartCode
    {
        Started,

        /// <summary>
        /// The implemented pools break the ten-legal-alternatives-per-rarity draft floor (L233) or cannot fill the shop
        /// (D-010, D-082), and no Laboratory override allows it.
        /// </summary>
        RefusedEconomyFloors,
    }

    public sealed class HostStartResult
    {
        internal HostStartResult(HostStartCode code, MatchFloorReport floors, IReadOnlyList<string> gaps)
        {
            Code = code;
            Floors = floors;
            Gaps = gaps;
        }

        public HostStartCode Code { get; }
        public bool Started => Code == HostStartCode.Started;
        public MatchFloorReport Floors { get; }

        /// <summary>Plain floor gaps (empty when every floor holds).</summary>
        public IReadOnlyList<string> Gaps { get; }
    }

    /// <summary>
    /// The authoritative match host without transport. It owns a <see cref="ConclaveMatch"/> or <see cref="DuelMatch"/>
    /// and its three server adapters (arenas, economy, Sim duels), validates every player command, and advances the
    /// match and all running duels one authoritative 60 Hz step per <see cref="Tick"/> (spec L421), single-threaded and
    /// deterministic by default. Everything clients need leaves through <see cref="Outbox"/> (public match events and
    /// arena previews, private shop and draft state and command replies, duel reports); per-tick duel state is exposed
    /// through <see cref="DuelStepped"/> for the snapshot writer. A disconnected player keeps the slot, sends neutral
    /// input, and loses rounds normally; nothing pauses for anyone (L55).
    /// </summary>
    public sealed class MatchHost : ICommandContext
    {
        private const string FingerprintPrefix = "confluence.host.settings.v1";

        private readonly IInputSource[] _inputs;
        private readonly IEconomyAgent[] _agents;
        private readonly IReadOnlyList<string> _playerIds;
        private readonly SimDuelRunner _runner;
        private readonly CommandGateway _gateway;
        private readonly ArenaPrefetcher _prefetcher;
        private bool _started;

        public MatchHost(ArenaGenerator generator, MatchHostSettings settings, ImplementedContent implemented = null,
            ArenaCache arenaCache = null, HostLog log = null)
        {
            if (generator == null) throw new ArgumentNullException(nameof(generator));
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (settings.WizardIds == null) throw new ArgumentException("Every player needs a wizard.", nameof(settings));
            Catalogue = generator.Content.Catalogue;
            Log = log ?? new HostLog();
            Overrides = settings.Overrides ?? LaboratoryOverrides.None;
            int players = settings.WizardIds.Count;
            if (settings.Mode == MatchMode.Conclave && settings.Conclave.PlayerCount != players)
            {
                throw new ArgumentException("The Conclave lobby has " + settings.Conclave.PlayerCount + " seats but " + players + " wizards were chosen.", nameof(settings));
            }

            if (settings.Mode == MatchMode.Duel && players != 2) throw new ArgumentException("A Duel has exactly two players.", nameof(settings));
            foreach (string wizard in settings.WizardIds) Catalogue.Wizard(wizard); // throws for unknown ids
            _playerIds = PlayerIdsOf(settings, players);

            IReadOnlyCollection<string> allowed = settings.Mode == MatchMode.Conclave ? settings.Conclave.AllowedContentIds : null;
            Content = HostContentPolicy.Build(Catalogue, implemented ?? ImplementedContent.FromShippedAssemblies(), Overrides,
                settings.ConfiguredBiomes, settings.ConfiguredProps, allowed, Log);
            if (Overrides.IsModified) Log.Warn("Laboratory overrides active (" + Overrides.CanonicalText() + "): the match never counts for rating (L69, L71).");

            Economy = new HostedEconomy(Catalogue, settings.Mode, settings.MatchSeed, settings.WizardIds, Content.Pools);
            Arenas = new ArenaProvider(generator, Content, arenaCache);
            _inputs = new IInputSource[players];
            _agents = new IEconomyAgent[players];
            for (int i = 0; i < players; i++) _inputs[i] = new RemoteInputSource();

            var runnerOptions = new DuelRunnerOptions
            {
                RecordReplays = settings.RecordReplays,
                RulesetFingerprint = settings.RulesetFingerprint ?? string.Empty,
                WorldFactory = settings.WorldFactory ?? SimWorldFactory.Default,
            };
            _runner = new SimDuelRunner(Arenas, new HostedDuelLoadouts(Economy, _playerIds), p => _inputs[p], runnerOptions);
            _runner.Stepped += d => DuelStepped?.Invoke(d);
            _runner.Started += d => DuelStarted?.Invoke(d);
            _runner.Finished += OnDuelFinished;

            if (settings.Mode == MatchMode.Conclave)
            {
                settings.Conclave.ThrowIfInvalid(Catalogue);
                Conclave = new ConclaveMatch(settings.Conclave, settings.MatchSeed, _runner, Economy, Arenas);
                Flow = Conclave;
            }
            else
            {
                Duel = new DuelMatch(settings.MatchSeed, _runner, Economy, Arenas, settings.Duel);
                Flow = Duel;
            }

            Flow.Events.Appended += OnMatchEvent;
            _prefetcher = new ArenaPrefetcher(Arenas, settings.MatchSeed, Flow.ArenaDiameters,
                Conclave != null ? Conclave.Settings.ArenaSeedRotation : null, settings.ArenaPrefetchRounds);
            _gateway = new CommandGateway(this, settings.RateLimiter ?? new TickRateLimiter());
            Fingerprint = ComputeFingerprint();
            Log.Info("Host settings fingerprint " + Fingerprint + " (match " + MatchFingerprint + ", " + Overrides.CanonicalText() + ").");
        }

        public MatchHostSettings Settings { get; }
        public Catalogue Catalogue { get; }
        public HostLog Log { get; }
        public LaboratoryOverrides Overrides { get; }
        public HostContentPolicy Content { get; }
        public HostedEconomy Economy { get; }
        public ArenaProvider Arenas { get; }

        /// <summary>Background generation of upcoming rounds' arenas (<see cref="MatchHostSettings.ArenaPrefetchRounds"/>).</summary>
        public ArenaPrefetcher ArenaPrefetch => _prefetcher;
        public SimDuelRunner Duels => _runner;
        public CommandGateway Commands => _gateway;
        public HostOutbox Outbox { get; } = new HostOutbox();

        /// <summary>The running match flow (Conclave or Duel).</summary>
        public MatchFlow Flow { get; }

        /// <summary>The Conclave, or null in Duel mode.</summary>
        public ConclaveMatch Conclave { get; }

        /// <summary>The Duel, or null in Conclave mode.</summary>
        public DuelMatch Duel { get; }

        public int PlayerCount => _inputs.Length;
        public IReadOnlyList<string> PlayerIds => _playerIds;

        /// <summary>The match rules' own fingerprint (Conclave settings or Duel options, L71).</summary>
        public string MatchFingerprint => Conclave != null ? Conclave.Settings.Fingerprint : Duel.Options.Fingerprint;

        /// <summary>
        /// Host settings fingerprint: the match rules, the Laboratory overrides, and the content policy (biomes, props,
        /// pools). A match with any override or a different pool never shares a fingerprint with a standard one (L71).
        /// </summary>
        public string Fingerprint { get; }

        /// <summary>Only standard ranked rules with implemented pools and no override produce rating (L67, L69, L71).</summary>
        public bool CountsForRating => !Overrides.IsModified && (Conclave != null ? Conclave.Settings.CountsForRating : Duel.Options.Ranked);

        /// <summary>The floor check of the last <see cref="Start"/>.</summary>
        public MatchFloorReport FloorReport { get; private set; }

        public bool IsStarted => _started;
        public bool IsFinished => Flow.IsFinished;
        public int CurrentTick => Flow.CurrentTick;

        /// <summary>A duel world was created (alcove start).</summary>
        public event Action<RunningDuel> DuelStarted;

        /// <summary>A duel advanced one authoritative step: the snapshot hook for the transport (L421).</summary>
        public event Action<RunningDuel> DuelStepped;

        // ---- Players --------------------------------------------------------------------------------------------

        /// <summary>Replaces a player's input source (network-fed by default).</summary>
        public void SetInputSource(int player, IInputSource source)
        {
            RequirePlayer(player);
            _inputs[player] = source ?? throw new ArgumentNullException(nameof(source));
        }

        public IInputSource InputSourceOf(int player)
        {
            RequirePlayer(player);
            return _inputs[player];
        }

        /// <summary>Lets an agent shop and draft for a player (null removes it).</summary>
        public void SetEconomyAgent(int player, IEconomyAgent agent)
        {
            RequirePlayer(player);
            _agents[player] = agent;
        }

        /// <summary>Seats a bot: real inputs in duels and heuristic shopping through the command API (L381).</summary>
        public BotPlayer AddBot(int player, BotProfile profile)
        {
            RequirePlayer(player);
            var bot = new BotPlayer(player, Settings.MatchSeed, profile ?? BotProfile.For(BotCompetence.Adept));
            SetInputSource(player, bot.Input);
            SetEconomyAgent(player, bot.Economy);
            return bot;
        }

        /// <summary>Hands a network-fed player's latest command to the simulation.</summary>
        public void SubmitInput(int player, in PlayerInput input)
        {
            RequirePlayer(player);
            if (!(_inputs[player] is RemoteInputSource remote)) throw new InvalidOperationException("Player " + player + " is not network-fed.");
            remote.Submit(input);
        }

        /// <summary>
        /// Records a disconnection or reconnection (L55). The player keeps the slot; a network-fed source sends neutral
        /// input while disconnected. Returns false when a reconnection comes after the 90 s window.
        /// </summary>
        public bool SetConnected(int player, bool connected)
        {
            RequirePlayer(player);
            bool accepted = Flow.SetConnected(player, connected);
            if (accepted && _inputs[player] is RemoteInputSource remote) remote.Connected = connected;
            return accepted;
        }

        // ---- Lifecycle ------------------------------------------------------------------------------------------

        /// <summary>
        /// Checks the match-start floors (L233, D-010, D-082) and starts the match. Refuses when they fail unless the
        /// Laboratory override allows it, in which case the gaps are logged and announced.
        /// </summary>
        public HostStartResult Start()
        {
            if (_started) throw new InvalidOperationException("The match has already started.");
            MatchFloorReport floors = Economy.CheckFloors();
            FloorReport = floors;
            var gaps = new List<string>(floors.Gaps);
            if (!floors.MeetsAll)
            {
                string summary = "Economy floors fail: " + string.Join("; ", gaps);
                if (!Overrides.BypassEconomyFloors)
                {
                    Log.Warn(summary + ". Match start refused (D-010).");
                    Outbox.Add(new HostNoticeMessage(Flow.CurrentTick, "Match start refused: " + summary + "."));
                    return new HostStartResult(HostStartCode.RefusedEconomyFloors, floors, gaps.AsReadOnly());
                }

                Log.Warn(summary + ". Started anyway under the Laboratory override bypass-economy-floors.");
                Outbox.Add(new HostNoticeMessage(Flow.CurrentTick, "Laboratory match: " + summary + "."));
            }

            _started = true;
            _prefetcher.PrefetchFrom(1);
            Flow.Start();
            return new HostStartResult(HostStartCode.Started, floors, gaps.AsReadOnly());
        }

        /// <summary>
        /// One authoritative step (spec L421): economy agents act in player order, running duels advance one tick (in
        /// parallel when configured), then Match polls them and settles its phases.
        /// </summary>
        public void Tick()
        {
            if (!_started) throw new InvalidOperationException("Start the match before ticking it.");
            if (Flow.IsFinished) return;
            RunAgents();
            if (Settings.ParallelDuels) _runner.StepAll(true);
            Flow.Tick();
        }

        /// <summary>Fast-forwards until the match finishes or <paramref name="maxTicks"/> have run; returns the ticks run.</summary>
        public int RunUntilFinished(int maxTicks)
        {
            int ran = 0;
            while (!Flow.IsFinished && ran < maxTicks)
            {
                Tick();
                ran++;
            }

            return ran;
        }

        // ---- Commands -------------------------------------------------------------------------------------------

        /// <summary>
        /// Validates and executes one player command (spec L429) and queues the private reply plus the player's updated
        /// shop or draft view.
        /// </summary>
        public CommandResult Submit(PlayerCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            CommandResult result = _gateway.Submit(command);
            if (command.Player < 0 || command.Player >= PlayerCount) return result;
            Outbox.Add(new CommandResultMessage(Flow.CurrentTick, command, result));
            if (result.Succeeded && !result.IsDuplicate)
            {
                if (command.IsShopCommand) QueueShopState(command.Player);
                else if (command.IsDraftCommand) QueueDraftState(command.Player);
            }

            return result;
        }

        int ICommandContext.PlayerCount => PlayerCount;
        bool ICommandContext.IsRunning => _started && !Flow.IsFinished;
        int ICommandContext.CurrentTick => Flow.CurrentTick;
        bool ICommandContext.IsEliminated(int player) => Flow.Player(player).IsEliminated;
        HostedEconomy ICommandContext.Economy => Economy;
        bool ICommandContext.TrySurrender(int player) => _runner.Surrender(player);

        // ---- Outgoing state -------------------------------------------------------------------------------------

        private void OnMatchEvent(MatchEvent e)
        {
            int audience = HostMessage.Everyone;
            if (e is ShopOpenedEvent shop) audience = shop.Opening.Player;
            else if (e is DraftOpenedEvent draft) audience = draft.Player;
            Outbox.Add(new MatchEventMessage(e.Tick, audience, e));

            switch (e)
            {
                case ArenaRevealedEvent reveal:
                    Outbox.Add(new ArenaPreviewMessage(e.Tick, Arenas.Get(reveal.Arena.ArenaId).PublicPreview));
                    if (reveal.Attempt == 0) _prefetcher.PrefetchFrom(reveal.Round + 1);
                    break;
                case ShopOpenedEvent opened:
                    QueueShopState(opened.Opening.Player);
                    break;
                case DraftOpenedEvent opened:
                    QueueDraftState(opened.Player);
                    break;
            }
        }

        private void OnDuelFinished(DuelReport report) => Outbox.Add(new DuelReportMessage(Flow.CurrentTick, report));

        private void QueueShopState(int player)
        {
            EquipmentShop shop = Economy.OpenShopOf(player);
            if (shop == null) return;
            PlayerEconomy economy = Economy.Player(player);
            PublicLoadout loadout;
            var reserve = new List<string>();
            int crowns;
            lock (economy.SyncRoot)
            {
                loadout = HostedEconomy.ToPublic(economy.Loadout);
                foreach (OwnedEquipment item in economy.Loadout.Reserve)
                    if (item != null) reserve.Add(item.Id);
                crowns = economy.Crowns;
            }

            Outbox.Add(new ShopStateMessage(Flow.CurrentTick, player, crowns, shop.Cards, loadout, reserve.AsReadOnly(), Economy.IsShopReady(player)));
        }

        private void QueueDraftState(int player)
        {
            AugmentDraft draft = Economy.CurrentDraftOf(player);
            if (draft == null)
            {
                Outbox.Add(new DraftStateMessage(Flow.CurrentTick, player, DraftOutcome.Passed, new DraftCard[0], null));
                return;
            }

            Outbox.Add(new DraftStateMessage(Flow.CurrentTick, player, draft.Outcome, draft.Cards, draft.Selected));
        }

        // ---- Internals ------------------------------------------------------------------------------------------

        private void RunAgents()
        {
            MatchPhase phase = Flow.Phase;
            if (phase != MatchPhase.Shop && phase != MatchPhase.Draft) return;
            for (int p = 0; p < _agents.Length; p++)
            {
                IEconomyAgent agent = _agents[p];
                if (agent == null || Flow.Player(p).IsEliminated) continue;
                EconomyPhase economyPhase = Economy.PhaseOf(p);
                bool matches = phase == MatchPhase.Shop ? economyPhase == EconomyPhase.Shop : economyPhase == EconomyPhase.Draft;
                if (matches) agent.Act(new EconomyTurn(this, p, economyPhase));
            }
        }

        private string ComputeFingerprint()
        {
            string text = FingerprintPrefix + ";match=" + MatchFingerprint + ";mode=" + Flow.Mode + ";lab=" + Overrides.CanonicalText() +
                ";content=" + SeedHash.Of(Content.CanonicalText()).ToString("X16", CultureInfo.InvariantCulture);
            return SeedHash.Of(text).ToString("X16", CultureInfo.InvariantCulture);
        }

        private static IReadOnlyList<string> PlayerIdsOf(MatchHostSettings settings, int players)
        {
            if (settings.PlayerIds != null)
            {
                if (settings.PlayerIds.Count != players) throw new ArgumentException("One player id per wizard is required.", nameof(settings));
                return new List<string>(settings.PlayerIds).AsReadOnly();
            }

            var ids = new List<string>(players);
            for (int i = 0; i < players; i++) ids.Add("P" + i.ToString(CultureInfo.InvariantCulture));
            return ids.AsReadOnly();
        }

        private void RequirePlayer(int player)
        {
            if (player < 0 || player >= _inputs.Length) throw new ArgumentOutOfRangeException(nameof(player), player, "Player ids are 0.." + (_inputs.Length - 1) + ".");
        }
    }
}
