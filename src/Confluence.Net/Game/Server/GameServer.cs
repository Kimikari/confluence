using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using Confluence.Arena.Generation;
using Confluence.Casting.Grammars;
using Confluence.Core.Content;
using Confluence.Core.Random;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Hosting;
using Confluence.Hosting.Arenas;
using Confluence.Hosting.Bots;
using Confluence.Hosting.Commands;
using Confluence.Hosting.Content;
using Confluence.Hosting.Duels;
using Confluence.Hosting.Messages;
using Confluence.Match;
using Confluence.Net.Game.Snapshots;
using Confluence.Net.Game.Wire;
using Confluence.Net.Protocol;
using Confluence.Net.RateLimiting;
using Confluence.Net.Requests;
using Confluence.Net.Serialization;
using Confluence.Net.Sync;
using Confluence.Net.Transport;
using Confluence.Sim.Duel;
using Confluence.Sim.Players;
using HostDraftState = Confluence.Hosting.Messages.DraftStateMessage;
using HostShopState = Confluence.Hosting.Messages.ShopStateMessage;
using MatchDuelEndReason = Confluence.Match.DuelEndReason;
using WireDuelReport = Confluence.Net.Game.Wire.DuelReportMessage;

namespace Confluence.Net.Game.Server
{
    /// <summary>Everything a game server needs besides its catalogue, generator, and clock.</summary>
    public sealed class GameServerSettings
    {
        public MatchMode Mode { get; set; } = MatchMode.Duel;

        /// <summary>Seats: 2 for a Duel, 2-8 for a Conclave (8 = the unranked lobby, fewer = Custom, spec L71).</summary>
        public int PlayerCount { get; set; } = 2;

        /// <summary>Seats filled by server bots (the last seats); the match starts when every other seat has joined.</summary>
        public int Bots { get; set; }

        public BotCompetence BotCompetence { get; set; } = BotCompetence.Adept;

        /// <summary>
        /// Recorded match seed (spec L419, L435), never sent to clients. Null (the default) draws it from a
        /// cryptographic random source, so no client can guess the Offer, Draft, Bot, or Pairing streams derived from it
        /// (research §2.6.2); a fixed seed is for Laboratory, Custom, tests, and reproductions only.
        /// </summary>
        public ulong? Seed { get; set; }

        /// <summary>
        /// Laboratory overrides (spec L69): bypass the D-010 economy floors, draw pools from the whole catalogue, and allow
        /// wizards whose grammar executes even when their kit has no behavior class yet. The match is labeled and never
        /// counts for rating.
        /// </summary>
        public bool Laboratory { get; set; }

        /// <summary>Authoritative ticks per wall-clock second: 60 in real time; more is the labeled accelerated test mode.</summary>
        public int TicksPerWallSecond { get; set; } = Ticks.PerSecond;

        public int ArenaPrefetchRounds { get; set; } = 1;
        public bool ParallelDuels { get; set; }

        /// <summary>Spectator delay; never below 15 s while the match is active (spec L391).</summary>
        public float SpectatorDelaySec { get; set; } = GlobalRules.Match.SpectatorDelayMinSec;

        public int SpectatorRateHz { get; set; } = 10;

        /// <summary>Wizards bots pick from (null = the lobby's allowed list).</summary>
        public IReadOnlyList<string> BotWizards { get; set; }

        public TransportSettings Transport { get; set; }

        /// <summary>Log sink (console, file); also receives the match host's log.</summary>
        public Action<string> Log { get; set; }

        public bool IsAccelerated => TicksPerWallSecond != Ticks.PerSecond;
    }

    /// <summary>
    /// The authoritative network game server (spec §14): a lobby on the transport, then one <see cref="MatchHost"/>
    /// stepped once per <see cref="Tick"/> (60 per second in real time). It maps the host's command API, outbox, and
    /// duel hooks onto the game protocol with interest management: a client receives only its own duel's snapshots and
    /// events, only its own shop, draft, grammar, and resource state, and other duels only as delayed spectator data
    /// once it is outside a live duel (spec L49, L72, L391). Reconnects within 90 s resume the seat (L55). Nothing runs on
    /// another thread: call <see cref="Poll"/>, <see cref="Tick"/>, and <see cref="Flush"/> from one loop (see
    /// <see cref="GameServerRunner"/>).
    /// </summary>
    public sealed class GameServer : IDisposable
    {
        private const int SentHistory = 64;
        private const int RateUpdateTicks = 10;

        /// <summary>Cosmetic cues kept for the next snapshot; older ones are dropped (they are cosmetic).</summary>
        private const int MaxPendingCosmetic = 64;

        /// <summary>Message id, snapshot header, and input status bytes in front of a snapshot body (upper bound).</summary>
        private const int SnapshotOverheadBytes = 24;

        private readonly Catalogue _catalogue;
        private readonly ArenaGenerator _generator;
        private readonly ArenaCache _arenaCache;
        private readonly GameServerSettings _settings;
        private readonly RulesetFingerprint _ruleset;
        private readonly INetClock _clock;
        private readonly Seat[] _seats;
        private readonly Dictionary<int, Seat> _seatBySession = new Dictionary<int, Seat>();
        private readonly Dictionary<int, LiveDuel> _duels = new Dictionary<int, LiveDuel>();
        private readonly List<LiveDuel> _duelOrder = new List<LiveDuel>();
        private readonly List<WizardChoiceWire> _wizards = new List<WizardChoiceWire>();
        private readonly NetWriter _body = new NetWriter(4096);
        private readonly List<SpectatorItem> _released = new List<SpectatorItem>();
        private readonly ServerTickClock _tickClock;
        private readonly List<string> _metrics = new List<string>();
        private readonly Dictionary<string, byte[]> _layoutBytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private int _ticksRun;
        private string _notice = string.Empty;
        private System.Threading.Tasks.Task _warmup;

        public GameServer(Catalogue catalogue, ArenaGenerator generator, RulesetFingerprint ruleset, GameServerSettings settings,
            INetClock clock = null, ArenaCache arenaCache = null, ITokenSource tokens = null)
        {
            _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
            _ruleset = ruleset ?? throw new ArgumentNullException(nameof(ruleset));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (settings.Mode == MatchMode.Duel && settings.PlayerCount != 2) throw new ArgumentException("A Duel has two seats.");
            if (settings.PlayerCount < 2 || settings.PlayerCount > GlobalRules.Match.LobbySize) throw new ArgumentException("2-8 seats.");
            if (settings.Bots < 0 || settings.Bots > settings.PlayerCount) throw new ArgumentException("Bots must fit the seats.");
            if (settings.TicksPerWallSecond < 1) throw new ArgumentException("The pace must be positive.");
            _clock = clock ?? SystemNetClock.Instance;
            _arenaCache = arenaCache ?? new ArenaCache();
            MatchSeed = settings.Seed ?? RandomSeed();
            Implemented = ImplementedContent.FromShippedAssemblies();
            BuildWizardList();
            SettingsFingerprint = ComputeSettingsFingerprint();

            var identity = new ServerIdentity(ruleset) { MaxSessions = settings.PlayerCount - settings.Bots + 8 };
            Transport = new ServerTransport(identity, settings.Transport ?? GameTransport.Defaults(), _clock, tokens);
            _tickClock = new ServerTickClock(this, _clock);
            Transport.TickClock = _tickClock;
            if (settings.IsAccelerated)
            {
                RateLimit inputs = GameProtocol.InputLimitAt(settings.TicksPerWallSecond);
                Transport.LimitOverride = type => type.Id == GameProtocol.DuelInput.Id ? inputs : type.RateLimit;
            }
            Transport.WelcomeTickRate = settings.TicksPerWallSecond;
            Transport.SessionConnected += OnSessionConnected;
            Transport.SessionDisconnected += OnSessionDisconnected;
            Transport.SessionExpired += OnSessionExpired;
            Transport.Requests.Handle(GameProtocol.JoinLobby, OnJoin);
            foreach (var type in new[] { GameProtocol.ShopCommand, GameProtocol.ShopReroll, GameProtocol.DraftCommand, GameProtocol.DraftReroll, GameProtocol.Surrender })
            {
                RequestType<CommandRequest, CommandResponse> bound = type;
                Transport.Requests.Handle(bound, (RequestHandlerWithId<PlayerSession, CommandRequest, CommandResponse>)((s, id, req) => OnCommand(bound, s, id, req)));
            }

            Transport.Messages.On(GameProtocol.DuelInput, OnInput);
            Transport.Messages.On(GameProtocol.Spectate, OnSpectate);
            Transport.Messages.On(GameProtocol.ArenaLayoutRequest, OnArenaLayoutRequest);

            _seats = new Seat[settings.PlayerCount];
            for (int i = 0; i < _seats.Length; i++) _seats[i] = new Seat(i);
            int firstBot = settings.PlayerCount - settings.Bots;
            IReadOnlyList<string> botWizards = settings.BotWizards != null && settings.BotWizards.Count > 0 ? settings.BotWizards : AllowedWizardIds();
            for (int i = firstBot; i < _seats.Length; i++)
            {
                Seat bot = _seats[i];
                bot.IsBot = true;
                bot.Joined = true;
                bot.Name = "Bot " + (i + 1).ToString(CultureInfo.InvariantCulture);
                bot.WizardId = botWizards.Count > 0 ? botWizards[i % botWizards.Count] : null;
            }

            Status = LobbyStatus.Waiting;
            Log("Server ready: " + settings.Mode + ", " + settings.PlayerCount + " seats (" + settings.Bots + " bots), " +
                (settings.Laboratory ? "LABORATORY overrides" : "standard rules") +
                (settings.IsAccelerated ? ", ACCELERATED TEST PACE " + settings.TicksPerWallSecond + " ticks/s" : ", real time 60 ticks/s") +
                ", ruleset " + ruleset.ShortHex + ", match seed " + MatchSeed.ToString("X16", CultureInfo.InvariantCulture) +
                (settings.Seed.HasValue ? " (fixed)" : " (random)") + ".");
        }

        public ServerTransport Transport { get; }

        /// <summary>Null until every seat has joined and the match was created.</summary>
        public MatchHost Host { get; private set; }

        public LobbyStatus Status { get; private set; }

        public ImplementedContent Implemented { get; }

        /// <summary>The match seed in use: the configured one, or a cryptographically random one (server-side only).</summary>
        public ulong MatchSeed { get; }

        /// <summary>The settings fingerprint sent in every lobby state, before joining (spec L71).</summary>
        public string SettingsFingerprint { get; }

        public GameServerSettings Settings => _settings;

        public bool IsFinished => Status == LobbyStatus.Finished || Status == LobbyStatus.Refused;

        /// <summary>Authoritative ticks run so far (the match tick once the match runs).</summary>
        public int CurrentTick => Host != null ? Host.CurrentTick : 0;

        /// <summary>Wall-clock time of the last authoritative tick (the tick clock's fraction).</summary>
        internal long LastTickMs { get; private set; }

        /// <summary>Wizards the lobby offers: implemented kits, plus executable-grammar wizards under Laboratory overrides.</summary>
        public IReadOnlyList<WizardChoiceWire> Wizards => _wizards;

        /// <summary>Snapshots sent per path (diagnostics, tests).</summary>
        public long SnapshotsSent { get; private set; }

        public long ReliableSnapshotsSent { get; private set; }

        public long DeltaSnapshotsSent { get; private set; }

        public long SpectatorFramesSent { get; private set; }

        /// <summary>Exact layouts sent to clients that could not regenerate an arena.</summary>
        public long LayoutsSent { get; private set; }

        /// <summary>Every session metrics record written so far (one JSON line per human duelist and duel).</summary>
        public IReadOnlyList<string> Metrics => _metrics;

        /// <summary>Raised for every message sent to a client: (seat, message type) — interest-management tests.</summary>
        public event Action<int, MessageType, INetMessage> Sent;

        public int Start(int port = 0, IPAddress bind = null)
        {
            int bound = Transport.Start(port, bind);
            Log("Listening on UDP port " + bound + ".");
            return bound;
        }

        /// <summary>Receives and handles everything that arrived; starts the match once every seat has joined.</summary>
        public void Poll()
        {
            Transport.Poll();
            if (Status != LobbyStatus.Waiting) return;
            if (Host == null)
            {
                if (AllSeatsJoined()) StartMatch();
            }
            else if (_warmup != null && _warmup.IsCompleted)
            {
                BeginMatch();
            }
        }

        /// <summary>One authoritative step (spec L421) with its outgoing traffic. A no-op outside a running match.</summary>
        public void Tick()
        {
            if (Status != LobbyStatus.Running || Host == null) return;
            if (!Host.IsFinished)
            {
                Host.Tick();
                _ticksRun++;
                LastTickMs = _clock.NowMs;
                RouteOutbox();
            }

            ReleaseSpectators();
            ReleaseDeferred();
            if (Host.IsFinished && Status == LobbyStatus.Running)
            {
                Status = LobbyStatus.Finished;
                BroadcastLobby();
                Log("Match finished at tick " + Host.CurrentTick + ".");
            }
        }

        public void Flush() => Transport.Flush();

        /// <summary>Clean shutdown: a notice to everyone, then a server-shutdown disconnect (clients do not try to resume).</summary>
        public void Shutdown(string reason)
        {
            if (!Transport.IsRunning) return;
            Log("Shutting down: " + reason);
            BroadcastNotice("Server shutting down: " + reason);
            Transport.Flush();
            Transport.Shutdown(reason);
        }

        public void Dispose()
        {
            if (Transport.IsRunning) Shutdown("disposed");
        }

        // ---- Lobby ------------------------------------------------------------------------------------------------

        private void BuildWizardList()
        {
            var kits = new HashSet<string>(Implemented.IdsOf(ContentType.Wizard), StringComparer.Ordinal);
            foreach (WizardDef wizard in _catalogue.Wizards)
            {
                bool kit = kits.Contains(wizard.Id);
                bool grammar = GrammarRegistry.IsExecutable(wizard.Id);
                if (!kit && !(_settings.Laboratory && grammar)) continue;
                GrammarRegistry.TryDescribe(wizard.Id, out GrammarDescriptor descriptor);
                _wizards.Add(new WizardChoiceWire
                {
                    WizardId = wizard.Id,
                    Name = wizard.Name,
                    Routes = descriptor != null ? descriptor.Routes : InputRoute.Standard,
                    KitImplemented = kit,
                });
            }
        }

        /// <summary>
        /// The settings fingerprint shown before joining (spec L71): every server setting that changes the rules a
        /// joining player accepts. Unchanged settings always give the same text; the match host's own fingerprint is
        /// added to <see cref="MatchSettingsMessage"/> once the match exists.
        /// </summary>
        private string ComputeSettingsFingerprint()
        {
            var text = new System.Text.StringBuilder("confluence.server.settings.v1");
            text.Append(";ruleset=").Append(_ruleset.ToHex());
            text.Append(";mode=").Append(_settings.Mode);
            text.Append(";seats=").Append(_settings.PlayerCount.ToString(CultureInfo.InvariantCulture));
            text.Append(";bots=").Append(_settings.Bots.ToString(CultureInfo.InvariantCulture)).Append('/').Append(_settings.BotCompetence);
            text.Append(";lab=").Append(_settings.Laboratory ? new LaboratoryOverrides(true, true).CanonicalText() : LaboratoryOverrides.None.CanonicalText());
            if (_settings.Mode == MatchMode.Conclave) text.Append(";conclave=").Append(ConclaveSettingsFor().Fingerprint);
            text.Append(";spectator=").Append(SpectatorDelayTicks.ToString(CultureInfo.InvariantCulture));
            text.Append(";pace=").Append(_settings.TicksPerWallSecond.ToString(CultureInfo.InvariantCulture));
            text.Append(";wizards=");
            foreach (WizardChoiceWire w in _wizards) text.Append(w.WizardId).Append(w.KitImplemented ? "+" : "-").Append(',');
            text.Append(";content=").Append(SeedHash.Of(string.Join(",", Implemented.Ids)).ToString("X16", CultureInfo.InvariantCulture));
            return SeedHash.Of(text.ToString()).ToString("X16", CultureInfo.InvariantCulture);
        }

        /// <summary>A 64-bit seed from the operating system's cryptographic generator (never derived from time or ids).</summary>
        public static ulong RandomSeed()
        {
            var bytes = new byte[8];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return BitConverter.ToUInt64(bytes, 0);
        }

        private ConclaveSettings ConclaveSettingsFor()
        {
            return _settings.PlayerCount == GlobalRules.Match.LobbySize && _settings.SpectatorDelaySec <= GlobalRules.Match.SpectatorDelayMinSec
                ? ConclaveSettings.Unranked
                : ConclaveSettings.Custom(_settings.PlayerCount, spectatorDelaySec: Math.Max(GlobalRules.Match.SpectatorDelayMinSec, _settings.SpectatorDelaySec));
        }

        /// <summary>The spectator delay in ticks: the configured delay, never below 15 s (spec L391).</summary>
        private int SpectatorDelayTicks => Ticks.FromSeconds(Math.Max(GlobalRules.Match.SpectatorDelayMinSec, _settings.SpectatorDelaySec));

        private List<string> AllowedWizardIds()
        {
            var ids = new List<string>();
            foreach (WizardChoiceWire w in _wizards) ids.Add(w.WizardId);
            return ids;
        }

        private bool AllSeatsJoined()
        {
            foreach (Seat s in _seats)
                if (!s.Joined || s.WizardId == null) return false;
            return true;
        }

        private Reply<JoinLobbyResponse> OnJoin(PlayerSession session, JoinLobbyRequest request)
        {
            if (Status != LobbyStatus.Waiting)
            {
                return Reply<JoinLobbyResponse>.Reject((ushort)JoinCode.MatchRunning, new JoinLobbyResponse { Detail = "the match has already started" });
            }

            if (request.WizardId == null || !_catalogue.TryGetWizard(request.WizardId, out _))
            {
                return Reply<JoinLobbyResponse>.Reject((ushort)JoinCode.UnknownWizard, new JoinLobbyResponse { Detail = "unknown wizard" });
            }

            WizardChoiceWire choice = _wizards.Find(w => w.WizardId == request.WizardId);
            if (choice == null)
            {
                return Reply<JoinLobbyResponse>.Reject((ushort)JoinCode.WizardNotAvailable,
                    new JoinLobbyResponse { Detail = request.WizardId + "'s kit is not implemented under these rules" });
            }

            if ((choice.Routes & request.Route) == 0)
            {
                return Reply<JoinLobbyResponse>.Reject((ushort)JoinCode.RouteNotSupported,
                    new JoinLobbyResponse { Detail = request.WizardId + "'s grammar does not accept " + request.Route });
            }

            if (!_seatBySession.TryGetValue(session.PlayerId, out Seat seat))
            {
                seat = Array.Find(_seats, s => !s.IsBot && !s.Joined && s.Session == null);
                if (seat == null) return Reply<JoinLobbyResponse>.Reject((ushort)JoinCode.LobbyFull, new JoinLobbyResponse { Detail = "the lobby is full" });
                seat.Session = session;
                _seatBySession[session.PlayerId] = seat;
            }

            seat.Name = session.DisplayName;
            seat.WizardId = request.WizardId;
            seat.Route = request.Route;
            seat.Joined = true;
            Log("Seat " + seat.Index + ": " + seat.Name + " joined as " + seat.WizardId + " (" + seat.Route + ").");
            BroadcastLobby();
            return Reply<JoinLobbyResponse>.Ok(new JoinLobbyResponse { Seat = seat.Index, Detail = "joined" });
        }

        private void StartMatch()
        {
            var wizards = new List<string>();
            var ids = new List<string>();
            foreach (Seat s in _seats)
            {
                wizards.Add(s.WizardId);
                ids.Add("P" + s.Index.ToString(CultureInfo.InvariantCulture) + ":" + s.Name);
            }

            LaboratoryOverrides overrides = _settings.Laboratory ? new LaboratoryOverrides(true, true) : LaboratoryOverrides.None;
            var hostSettings = new MatchHostSettings
            {
                Mode = _settings.Mode,
                MatchSeed = MatchSeed,
                Conclave = _settings.Mode == MatchMode.Conclave ? ConclaveSettingsFor() : ConclaveSettings.Unranked,
                WizardIds = wizards,
                PlayerIds = ids,
                Overrides = overrides,
                RulesetFingerprint = _ruleset.ToHex(),
                ParallelDuels = _settings.ParallelDuels,
                ArenaPrefetchRounds = _settings.ArenaPrefetchRounds,
            };

            MatchHost host;
            try
            {
                host = new MatchHost(_generator, hostSettings, Implemented, _arenaCache, new HostLog(line => Log("host " + line)));
            }
            catch (Exception e)
            {
                Status = LobbyStatus.Refused;
                _notice = "The match could not be created: " + e.Message;
                Log(_notice);
                BroadcastLobby();
                return;
            }

            foreach (Seat s in _seats)
            {
                if (s.IsBot)
                {
                    host.AddBot(s.Index, BotProfile.For(_settings.BotCompetence));
                }
                else
                {
                    s.Input = new NetworkInputSource { Connected = s.Session != null && s.Session.IsConnected };
                    host.SetInputSource(s.Index, s.Input);
                }
            }

            host.DuelStarted += OnDuelStarted;
            host.DuelStepped += OnDuelStepped;
            Host = host;

            // Generate the first round's arena in the background while the transport keeps polling, so the first
            // authoritative tick never stalls the connections (generation takes 0.3-8 s per layout).
            _warmup = host.Arenas.Prefetch(host.ArenaPrefetch.Predict(1));
            Log("Preparing round 1 arena before the match starts.");
        }

        private void BeginMatch()
        {
            MatchHost host = Host;
            _warmup = null;
            HostStartResult result = host.Start();
            if (!result.Started)
            {
                Status = LobbyStatus.Refused;
                _notice = "Match start refused (D-010): " + string.Join("; ", result.Gaps);
                Log(_notice);
                RouteOutbox();
                BroadcastLobby();
                return;
            }

            Status = LobbyStatus.Running;
            Transport.AcceptNewPlayers = false;
            LastTickMs = _clock.NowMs;
            _notice = host.Overrides.IsModified ? "Laboratory match (" + host.Overrides.CanonicalText() + "): no ranked rewards." : string.Empty;
            Log("Match started: host fingerprint " + host.Fingerprint + ", match " + host.MatchFingerprint + (host.CountsForRating ? ", rated" : ", unrated") + ".");
            BroadcastLobby();
            foreach (Seat s in _seats)
                if (!s.IsBot && s.Session != null) SendTo(s, GameProtocol.MatchSettings, SettingsFor(s));
            RouteOutbox();
        }

        private MatchSettingsMessage SettingsFor(Seat seat)
        {
            MatchTimings timings = Host.Flow.Timings;
            var msg = new MatchSettingsMessage
            {
                YourSeat = seat.Index,
                Mode = _settings.Mode,
                HostFingerprint = Host.Fingerprint,
                MatchFingerprint = Host.MatchFingerprint,
                RulesetShortHex = _ruleset.ShortHex,
                LaboratoryLabel = Host.Overrides.IsModified ? Host.Overrides.CanonicalText() : string.Empty,
                CountsForRating = Host.CountsForRating,
                ShopTicks = timings.ShopTicks,
                DraftTicks = timings.DraftTicks,
                AlcoveTicks = timings.AlcoveTicks,
                CombatTicks = timings.CombatMaxTicks,
                ResultsTicks = timings.ResultsTicks,
                TicksPerWallSecond = _settings.TicksPerWallSecond,
                Accelerated = _settings.IsAccelerated,
            };
            foreach (Seat s in _seats) msg.Seats.Add(new SeatWire { Seat = s.Index, Name = s.Name, WizardId = s.WizardId, IsBot = s.IsBot });
            return msg;
        }

        private LobbyStateMessage LobbyMessage()
        {
            var msg = new LobbyStateMessage
            {
                Mode = _settings.Mode,
                PlayerCount = _settings.PlayerCount,
                Status = Status,
                LaboratoryLabel = _settings.Laboratory ? new LaboratoryOverrides(true, true).CanonicalText() : string.Empty,
                CountsForRating = Host != null ? Host.CountsForRating : false,
                SettingsFingerprint = SettingsFingerprint,
                Notice = _notice,
            };
            foreach (Seat s in _seats)
            {
                msg.Seats.Add(new LobbySeatWire
                {
                    Seat = s.Index,
                    Name = s.Name ?? string.Empty,
                    WizardId = s.WizardId,
                    IsBot = s.IsBot,
                    Joined = s.Joined,
                    Connected = s.IsBot || (s.Session != null && s.Session.IsConnected),
                });
            }

            msg.Wizards.AddRange(_wizards);
            return msg;
        }

        private void BroadcastLobby()
        {
            LobbyStateMessage msg = LobbyMessage();
            foreach (PlayerSession session in Transport.Sessions.All)
            {
                if (!session.IsConnected) continue;
                Transport.Send(session, GameProtocol.LobbyState, msg);
                if (_seatBySession.TryGetValue(session.PlayerId, out Seat seat)) Sent?.Invoke(seat.Index, GameProtocol.LobbyState, msg);
            }
        }

        private void BroadcastNotice(string text)
        {
            var msg = new NoticeMessage { Text = text };
            foreach (PlayerSession session in Transport.Sessions.All)
            {
                if (!session.IsConnected || !Transport.Send(session, GameProtocol.Notice, msg)) continue;
                if (_seatBySession.TryGetValue(session.PlayerId, out Seat seat)) Sent?.Invoke(seat.Index, GameProtocol.Notice, msg);
            }
        }

        // ---- Sessions ---------------------------------------------------------------------------------------------

        private void OnSessionConnected(PlayerSession session, bool resumed)
        {
            LobbyStateMessage lobby = LobbyMessage();
            bool sent = Transport.Send(session, GameProtocol.LobbyState, lobby);
            if (!_seatBySession.TryGetValue(session.PlayerId, out Seat seat)) return;
            if (sent) Sent?.Invoke(seat.Index, GameProtocol.LobbyState, lobby);
            if (Host == null || Status != LobbyStatus.Running) return;
            bool accepted = Host.SetConnected(seat.Index, true);
            if (seat.Input != null) seat.Input.Connected = accepted;
            Log("Seat " + seat.Index + " reconnected" + (accepted ? "" : " after the 90 s window (L55): the seat stays absent") + ".");
            if (accepted) CatchUp(seat);
        }

        private void OnSessionDisconnected(PlayerSession session)
        {
            if (!_seatBySession.TryGetValue(session.PlayerId, out Seat seat)) return;
            Log("Seat " + seat.Index + " disconnected; the seat is kept for 90 s (L55).");
            if (Host != null && Status == LobbyStatus.Running)
            {
                Host.SetConnected(seat.Index, false);
                if (seat.Input != null) seat.Input.Connected = false;
            }
            else
            {
                BroadcastLobby();
            }
        }

        private void OnSessionExpired(PlayerSession session)
        {
            if (!_seatBySession.TryGetValue(session.PlayerId, out Seat seat)) return;
            if (Status == LobbyStatus.Waiting)
            {
                // Before the match a seat whose session expired is free again.
                _seatBySession.Remove(session.PlayerId);
                seat.Session = null;
                seat.Joined = false;
                seat.WizardId = null;
                BroadcastLobby();
            }
        }

        /// <summary>Resends everything a resumed client needs to rebuild its view (the snapshot baselines restart in full).</summary>
        private void CatchUp(Seat seat)
        {
            SendTo(seat, GameProtocol.MatchSettings, SettingsFor(seat));
            MatchFlow flow = Host.Flow;
            SendTo(seat, GameProtocol.Phase, new PhaseMessage
            {
                Phase = flow.Phase,
                Round = flow.Round,
                BracketRound = flow.BracketRound,
                Attempt = flow.Attempt,
                StartTick = flow.PhaseStartTick,
                EndTick = flow.PhaseEndTick,
            });
            SendTo(seat, GameProtocol.Standings, StandingsNow());
            ArenaPreview arena = flow.CurrentArena;
            if (arena != null)
            {
                HostedArena hosted = Host.Arenas.Get(arena.ArenaId);
                if (hosted != null) SendTo(seat, GameProtocol.Arena, ArenaWire(hosted));
                SendReveal(seat);
            }

            if (Host.Economy.OpenShopOf(seat.Index) != null) SendTo(seat, GameProtocol.ShopState, EconomyViews.Shop(Host, seat.Index));
            if (Host.Economy.CurrentDraftOf(seat.Index) != null) SendTo(seat, GameProtocol.DraftState, EconomyViews.Draft(Host, seat.Index));
            foreach (LiveDuel live in _duelOrder)
            {
                if (live.Ended) continue;
                for (int side = 0; side < 2; side++)
                {
                    Stream stream = live.Streams[side];
                    if (stream == null || stream.Seat != seat) continue;
                    stream.ResetBaseline();
                    seat.Input?.ResetViewHistory();
                    SendTo(seat, GameProtocol.DuelStart, live.StartFor(side));
                }
            }
        }

        // ---- Commands and inputs ----------------------------------------------------------------------------------

        private Reply<CommandResponse> OnCommand(RequestType<CommandRequest, CommandResponse> type, PlayerSession session, uint requestId, CommandRequest request)
        {
            if (!_seatBySession.TryGetValue(session.PlayerId, out Seat seat) || Host == null)
            {
                return Reply<CommandResponse>.Reject((ushort)CommandCode.MatchNotRunning, new CommandResponse { Code = CommandCode.MatchNotRunning });
            }

            if (GameProtocol.RequestFor(request.Kind) != type)
            {
                // A command on the wrong request type would dodge its rate limit (rerolls, L429).
                return Reply<CommandResponse>.Reject((ushort)CommandCode.InvalidArguments, new CommandResponse { Code = CommandCode.InvalidArguments });
            }

            PlayerCommand command;
            try
            {
                command = request.ToCommand(seat.Index, requestId);
            }
            catch (ArgumentException)
            {
                return Reply<CommandResponse>.Reject((ushort)CommandCode.InvalidArguments, new CommandResponse { Code = CommandCode.InvalidArguments });
            }

            CommandResult result = Host.Submit(command);
            var response = new CommandResponse { Code = result.Code, Transaction = result.Transaction, Duplicate = result.IsDuplicate };
            RouteOutbox();
            return result.Succeeded ? Reply<CommandResponse>.Ok(response) : Reply<CommandResponse>.Reject((ushort)result.Code, response);
        }

        private void OnInput(PlayerSession session, DuelInputMessage message)
        {
            if (!_seatBySession.TryGetValue(session.PlayerId, out Seat seat) || seat.Input == null) return;
            _duels.TryGetValue(message.DuelId, out LiveDuel live);
            int arrivalTick = live != null && !live.Ended ? live.Duel.World.Tick : -1;
            seat.Input.Accept(message.DuelId, message.Commands, arrivalTick, message.InterpolationTicks);
            if (live != null)
            {
                for (int side = 0; side < 2; side++)
                {
                    Stream stream = live.Streams[side];
                    if (stream == null || stream.Seat != seat) continue;
                    stream.Acknowledge(message.AckSnapshotTick);
                    stream.Monitor.OnAck(message.AckSnapshotTick, message.SnapshotsReceived, _clock.NowMs);
                }
            }
        }

        private void OnSpectate(PlayerSession session, SpectateRequest request)
        {
            if (!_seatBySession.TryGetValue(session.PlayerId, out Seat seat) || Host == null) return;
            if (InLiveDuel(seat)) return; // live players never receive other pairs' duels (spec L72, L391)
            seat.SpectateDuel = request.DuelId < 0 ? -1 : request.DuelId;
            seat.Spectating = true;
        }

        /// <summary>Whether a seat is fighting a duel right now (interest management; tests).</summary>
        public bool IsInLiveDuel(int seat) => seat >= 0 && seat < _seats.Length && InLiveDuel(_seats[seat]);

        /// <summary>
        /// Sends the exact layout of an arena already announced to this seat (research §2.7.1: the authoritative fallback
        /// when a client's regeneration does not match the layout hash). Unannounced arenas are never sent.
        /// </summary>
        private void OnArenaLayoutRequest(PlayerSession session, ArenaLayoutRequest request)
        {
            if (!_seatBySession.TryGetValue(session.PlayerId, out Seat seat) || Host == null || request.ArenaId == null) return;
            if (!seat.AnnouncedArenas.Contains(request.ArenaId)) return;
            HostedArena hosted = Host.Arenas.Get(request.ArenaId);
            if (hosted == null) return;
            if (!_layoutBytes.TryGetValue(request.ArenaId, out byte[] bytes))
            {
                bytes = LayoutCodec.Encode(hosted.CreateCopy());
                _layoutBytes[request.ArenaId] = bytes;
            }

            LayoutsSent++;
            SendTo(seat, GameProtocol.ArenaLayout, new ArenaLayoutMessage { ArenaId = request.ArenaId, LayoutHash = hosted.LayoutHash, Layout = bytes });
        }

        private bool InLiveDuel(Seat seat)
        {
            foreach (LiveDuel d in _duelOrder)
                if (!d.Ended && (d.SeatOfSide[0] == seat.Index || d.SeatOfSide[1] == seat.Index)) return true;
            return false;
        }

        // ---- Duels: start, snapshots, events ----------------------------------------------------------------------

        private void OnDuelStarted(RunningDuel duel)
        {
            var live = new LiveDuel(duel, SpectatorDelayTicks);
            live.StartMatchTick = Host.CurrentTick - duel.World.Tick;
            for (int side = 0; side < 2; side++)
            {
                DuelSeat duelSeat = duel.Seats[side];
                Seat seat = _seats[duelSeat.Player];
                live.SeatOfSide[side] = seat.Index;
                live.Sides[side] = SideWire(duelSeat, seat, duel.World.AlcoveOf(side));
                if (!seat.IsBot)
                {
                    live.Streams[side] = new Stream(seat, side);
                    seat.Spectating = false;
                }
            }

            _duels[duel.DuelId] = live;
            _duelOrder.Add(live);
            for (int side = 0; side < 2; side++)
            {
                Stream stream = live.Streams[side];
                if (stream != null) SendTo(stream.Seat, GameProtocol.DuelStart, live.StartFor(side));
            }
        }

        private DuelSideWire SideWire(DuelSeat duelSeat, Seat seat, Confluence.Core.Geometry.YawBox alcove)
        {
            DuelPlayerSetup setup = duelSeat.World.Player(duelSeat.Index).Setup;
            var side = new DuelSideWire { Seat = seat.Index, Name = seat.Name, WizardId = setup.WizardId, EnabledActiveId = setup.EnabledActiveItemId, Alcove = alcove };
            side.SpellIds.AddRange(setup.EquippedSpellIds);
            foreach (EquippedItem item in setup.Equipment)
            {
                side.EquipmentIds.Add(item.Id);
                side.EquipmentSlots.Add(item.Slot);
            }

            foreach (ActiveAugment a in setup.Augments)
            {
                side.AugmentIds.Add(a.Id);
                side.AugmentRanks.Add(Math.Max(1, Math.Min(2, a.Rank)));
            }

            return side;
        }

        private void OnDuelStepped(RunningDuel duel)
        {
            if (!_duels.TryGetValue(duel.DuelId, out LiveDuel live)) return;
            DuelWorld world = duel.World;
            int matchTick = Host.CurrentTick;
            bool over = world.IsOver;

            // Visibility (spec L120, research §2.6.1) and events of this step, per audience.
            for (int side = 0; side < 2; side++)
            {
                Stream stream = live.Streams[side];
                if (stream == null) continue;
                stream.OpponentReplicated = FrameCapture.ReplicatesTo(world, side, 1 - side);
                DuelEventFilter.Convert(world, side, stream.PendingEvents, stream.OpponentReplicated ? -1 : 1 - side, stream.PendingCosmetic);
                if (stream.PendingCosmetic.Count > MaxPendingCosmetic)
                {
                    stream.CosmeticDropped += stream.PendingCosmetic.Count - MaxPendingCosmetic;
                    stream.PendingCosmetic.RemoveRange(0, stream.PendingCosmetic.Count - MaxPendingCosmetic);
                }
            }

            DuelEventFilter.Convert(world, DuelEventFilter.SpectatorView, live.SpectatorEvents);

            // Snapshots at each viewer's congestion-selected rate (spec L421), the final state always.
            bool capture = false;
            for (int side = 0; side < 2; side++)
            {
                Stream stream = live.Streams[side];
                if (stream == null) continue;
                if (stream.Seat.Session == null || !stream.Seat.Session.IsConnected)
                {
                    stream.SendNow = false;
                    continue;
                }

                if (world.Tick % RateUpdateTicks == 0) UpdateLink(stream);
                stream.SendNow = over || stream.Rate.ShouldSend(world.Tick);
                capture |= stream.SendNow;
            }

            int spectatorInterval = Math.Max(1, Ticks.PerSecond / Math.Max(1, Math.Min(Ticks.PerSecond, _settings.SpectatorRateHz)));
            bool spectatorPush = over || world.Tick % spectatorInterval == 0;
            if (capture || spectatorPush)
            {
                DuelFrame truth = FrameCapture.Capture(world, duel.DuelId, matchTick, live.Cells);
                for (int side = 0; side < 2; side++)
                {
                    Stream stream = live.Streams[side];
                    if (stream == null || !stream.SendNow) continue;
                    if (stream.OpponentReplicated) stream.LastSeenOpponent = truth.Players[1 - side];
                    DuelFrame view = FrameCapture.ForViewer(truth, side, stream.OpponentReplicated, stream.LastSeenOpponent);
                    SendSnapshot(stream, view, FrameCapture.CapturePrivate(world, side), over, world);
                    FlushCosmetic(live, stream);
                }

                if (spectatorPush)
                {
                    _body.Reset();
                    SnapshotCodec.EncodeBody(_body, FrameCapture.ForSpectator(truth), null, null);
                    var item = new SpectatorItem
                    {
                        DuelId = duel.DuelId,
                        Side0 = live.SeatOfSide[0],
                        Side1 = live.SeatOfSide[1],
                        Tick = world.Tick,
                        Body = _body.ToArray(),
                        Events = new List<WireDuelEvent>(live.SpectatorEvents),
                        Ended = over,
                    };
                    live.SpectatorEvents.Clear();
                    live.Spectator.Push(matchTick, item);
                }
            }

            for (int side = 0; side < 2; side++)
            {
                Stream stream = live.Streams[side];
                if (stream == null) continue;
                FlushEvents(live, stream);
                stream.Snap.Ticks++;
                if (stream.Rate.CurrentRate != stream.Rate.HighRate) stream.Snap.LowRateTicks++;
            }

            if (over)
            {
                live.Ended = true;
                WriteMetrics(live);
            }
        }

        /// <summary>
        /// Feeds the stream's congestion selector from application-level RTT and snapshot loss (research §2.8.4) and
        /// gives lag compensation its one-way delay and jitter in ticks.
        /// </summary>
        private void UpdateLink(Stream stream)
        {
            long now = _clock.NowMs;
            stream.Monitor.Update(now);
            stream.Rate.Update(now, stream.Monitor.ToLinkStats(Transport.GetLinkStats(stream.Seat.Session)));
            NetworkInputSource input = stream.Seat.Input;
            if (input != null) input.SnapshotIntervalTicks = Math.Max(1, Ticks.PerSecond / Math.Max(1, stream.Rate.CurrentRate));
            if (input != null && stream.Monitor.RttSamples > 0)
            {
                double ticksPerMs = _settings.TicksPerWallSecond / 1000.0;
                input.OneWayTicks = (int)Math.Round(stream.Monitor.RttMs * 0.5 * ticksPerMs);
                input.JitterTicks = (int)Math.Ceiling(stream.Monitor.JitterMs * ticksPerMs);
            }
        }

        private void SendSnapshot(Stream stream, DuelFrame frame, PrivateEntry own, bool forceReliable, DuelWorld world)
        {
            DuelFrame baseline = stream.BaselineFor(frame.Tick);
            int limit = Transport.MaxUnreliablePayload(stream.Seat.Session) - SnapshotOverheadBytes;
            _body.Reset();
            DuelFrame held = frame;
            bool overBudget = false;
            if (forceReliable)
            {
                // The final state of a duel always arrives, complete.
                SnapshotCodec.EncodeBody(_body, frame, baseline, own);
            }
            else
            {
                stream.Prepare(world, own);
                held = SnapshotCodec.EncodeBody(_body, frame, baseline, own, limit, stream.Priority, out int deferred, out overBudget);
                stream.NoteDeferrals(frame, held, deferred);
            }

            var header = new SnapshotHeader(frame.Tick, stream.Seat.Input != null ? stream.Seat.Input.LastProcessedSequence : 0u,
                baseline != null ? baseline.Tick : SnapshotHeader.NoBaseline);
            var msg = new SnapshotMessage
            {
                Header = header,
                Body = _body.Buffer,
                BodyLength = _body.Length,
                InputQueue = stream.Seat.Input != null ? stream.Seat.Input.QueueDepth : 0,
                InputStarved = stream.Seat.Input != null ? (uint)Math.Min(uint.MaxValue, stream.Seat.Input.Buffer.StarvedTicks) : 0u,
            };
            bool reliable = forceReliable || overBudget || _body.Length > limit;
            MessageType<SnapshotMessage> type = reliable ? GameProtocol.SnapshotReliable : GameProtocol.Snapshot;
            if (Transport.Send(stream.Seat.Session, type, msg))
            {
                SnapshotsSent++;
                if (reliable) ReliableSnapshotsSent++;
                if (baseline != null) DeltaSnapshotsSent++;
                stream.Monitor.OnSent(frame.Tick, _clock.NowMs);
                stream.Snap.Sent++;
                if (baseline != null) stream.Snap.Delta++;
                if (reliable) stream.Snap.Reliable++;
                stream.Snap.BodyBytes += _body.Length;
                if (_body.Length > stream.Snap.MaxBodyBytes) stream.Snap.MaxBodyBytes = _body.Length;
                Sent?.Invoke(stream.Seat.Index, type, msg);
            }

            stream.Remember(held);
            if (stream.Seat.Input != null) stream.Seat.Input.NewestShownTick = frame.Tick;
        }

        /// <summary>Cosmetic cues ride with each snapshot on their own sequenced lane (research §2.5.1).</summary>
        private void FlushCosmetic(LiveDuel live, Stream stream)
        {
            if (stream.PendingCosmetic.Count == 0) return;
            var msg = new DuelEventsMessage { DuelId = live.Duel.DuelId };
            int count = Math.Min(WireLimits.MaxDuelEventsPerMessage, stream.PendingCosmetic.Count);
            msg.Events.AddRange(stream.PendingCosmetic.GetRange(stream.PendingCosmetic.Count - count, count));
            stream.CosmeticDropped += stream.PendingCosmetic.Count - count;
            stream.PendingCosmetic.Clear();
            SendTo(stream.Seat, GameProtocol.DuelCosmetic, msg);
        }

        /// <summary>One metrics record per human duelist at duel end (research §2.9.5).</summary>
        private void WriteMetrics(LiveDuel live)
        {
            for (int side = 0; side < 2; side++)
            {
                Stream stream = live.Streams[side];
                if (stream == null) continue;
                PlayerSession session = stream.Seat.Session;
                LinkStats transport = session != null && session.IsConnected ? Transport.GetLinkStats(session) : default;
                string line = SessionMetrics.Format(stream.Seat.Index, live.Duel.DuelId, live.Duel.Setup.Round, stream.Monitor, stream.Rate, stream.Snap,
                    stream.Seat.Input, session?.Counters, transport);
                _metrics.Add(line);
                Log("metrics " + line);
            }
        }

        private void FlushEvents(LiveDuel live, Stream stream)
        {
            if (stream.PendingEvents.Count == 0) return;
            if (stream.Seat.Session == null || !stream.Seat.Session.IsConnected)
            {
                stream.PendingEvents.Clear(); // a resumed client rebuilds from the next full snapshot
                return;
            }

            int index = 0;
            while (index < stream.PendingEvents.Count)
            {
                var msg = new DuelEventsMessage { DuelId = live.Duel.DuelId };
                int count = Math.Min(WireLimits.MaxDuelEventsPerMessage, stream.PendingEvents.Count - index);
                msg.Events.AddRange(stream.PendingEvents.GetRange(index, count));
                index += count;
                SendTo(stream.Seat, GameProtocol.DuelEvents, msg);
            }

            stream.PendingEvents.Clear();
        }

        private void ReleaseSpectators()
        {
            if (Host == null) return;
            int now = Host.CurrentTick;
            for (int i = _duelOrder.Count - 1; i >= 0; i--)
            {
                LiveDuel live = _duelOrder[i];
                _released.Clear();
                // While the match is active the delay never drops below 15 s; afterwards everything may be released.
                if (Host.IsFinished) live.Spectator.ReleaseAll(_released);
                else live.Spectator.Release(now, _released);
                foreach (SpectatorItem item in _released)
                {
                    foreach (Seat seat in _seats)
                    {
                        if (seat.IsBot || !seat.Spectating || seat.Session == null || !seat.Session.IsConnected) continue;
                        if (seat.SpectateDuel >= 0 && seat.SpectateDuel != item.DuelId) continue;
                        if (seat.Index == item.Side0 || seat.Index == item.Side1) continue;
                        if (InLiveDuel(seat)) continue;
                        var msg = new SpectatorFrameMessage
                        {
                            DuelId = item.DuelId,
                            Side0Seat = item.Side0,
                            Side1Seat = item.Side1,
                            DuelEnded = item.Ended,
                            FrameTick = item.Tick,
                            FrameBody = item.Body,
                        };
                        msg.Events.AddRange(item.Events);
                        SendTo(seat, GameProtocol.SpectatorFrame, msg);
                        SpectatorFramesSent++;
                    }
                }

                if (live.Ended && live.Spectator.Count == 0)
                {
                    _duelOrder.RemoveAt(i);
                    _duels.Remove(live.Duel.DuelId);
                }
            }
        }

        // ---- Host outbox ------------------------------------------------------------------------------------------

        private void RouteOutbox()
        {
            if (Host == null) return;
            foreach (HostMessage message in Host.Outbox.Drain())
            {
                switch (message)
                {
                    case MatchEventMessage e:
                        RouteMatchEvent(e.Event);
                        break;
                    case ArenaPreviewMessage preview:
                        HostedArena hosted = Host.Arenas.Get(preview.Preview.ArenaId);
                        if (hosted != null) BroadcastSeats(GameProtocol.Arena, ArenaWire(hosted));
                        foreach (Seat seat in _seats)
                            if (!seat.IsBot) SendReveal(seat);
                        break;
                    case HostShopState shop:
                        SendTo(_seats[shop.Player], GameProtocol.ShopState, EconomyViews.Shop(Host, shop.Player));
                        break;
                    case HostDraftState draft:
                        SendTo(_seats[draft.Player], GameProtocol.DraftState, EconomyViews.Draft(Host, draft.Player));
                        break;
                    case Confluence.Hosting.Messages.DuelReportMessage report:
                        SendReports(report.Report);
                        break;
                    case HostNoticeMessage notice:
                        BroadcastNotice(notice.Text);
                        break;
                }
            }
        }

        private void RouteMatchEvent(MatchEvent e)
        {
            switch (e)
            {
                case PhaseChangedEvent phase:
                    Log("t" + phase.Tick + " phase " + phase.Phase + " round " + phase.Round + (phase.BracketRound > 0 ? " bracket " + phase.BracketRound : "") +
                        (phase.Attempt > 0 ? " attempt " + phase.Attempt : "") + (phase.EndTick >= 0 ? " until t" + phase.EndTick : ""));
                    BroadcastSeats(GameProtocol.Phase, new PhaseMessage
                    {
                        Phase = phase.Phase,
                        Round = phase.Round,
                        BracketRound = phase.BracketRound,
                        Attempt = phase.Attempt,
                        StartTick = phase.Tick,
                        EndTick = phase.EndTick,
                    });
                    if (phase.Phase == MatchPhase.Alcove) SendFinalLoadouts();
                    break;
                case DuelResolvedEvent resolved:
                    SendResult(ResultWire(resolved));
                    break;
                case ResolveChangedEvent _:
                case DuelScoreChangedEvent _:
                case PlayerEliminatedEvent _:
                case ConnectionChangedEvent _:
                    SendStandings();
                    break;
                case MatchFinishedEvent finished:
                    ReleaseDeferred();
                    BroadcastSeats(GameProtocol.Standings, StandingsNow());
                    BroadcastSeats(GameProtocol.MatchEnd, EndWire(finished.Result));
                    break;
                case RoundVoidedEvent voided:
                    BroadcastNotice("Round voided: " + voided.Reason);
                    break;
            }
        }

        /// <summary>
        /// A duel result goes at once to its two duelists and to everyone outside a live duel; a player still fighting
        /// receives other pairs' results only when its own duel ends or the spectator delay has passed (research §2.6.4,
        /// spec L391: live players get no other pair's information early).
        /// </summary>
        private void SendResult(DuelResultMessage result)
        {
            int release = Host.CurrentTick + SpectatorDelayTicks;
            foreach (Seat seat in _seats)
            {
                if (seat.IsBot) continue;
                bool own = seat.Index == result.Side0Seat || seat.Index == result.Side1Seat;
                if (own || !InLiveDuel(seat)) SendTo(seat, GameProtocol.DuelResult, result);
                else seat.DeferredResults.Add(new KeyValuePair<int, DuelResultMessage>(release, result));
            }
        }

        /// <summary>Standings follow the same rule as results: a live duelist sees them after its duel or the delay.</summary>
        private void SendStandings()
        {
            StandingsMessage now = StandingsNow();
            foreach (Seat seat in _seats)
            {
                if (seat.IsBot) continue;
                if (!InLiveDuel(seat)) SendTo(seat, GameProtocol.Standings, now);
                else if (seat.StandingsReleaseTick < 0) seat.StandingsReleaseTick = Host.CurrentTick + SpectatorDelayTicks;
            }
        }

        /// <summary>Releases deferred results and standings whose holder left its live duel or whose delay passed.</summary>
        private void ReleaseDeferred()
        {
            int now = Host.CurrentTick;
            foreach (Seat seat in _seats)
            {
                if (seat.IsBot || (seat.DeferredResults.Count == 0 && seat.StandingsReleaseTick < 0)) continue;
                bool free = !InLiveDuel(seat) || Host.IsFinished;
                for (int i = 0; i < seat.DeferredResults.Count; i++)
                {
                    if (!free && seat.DeferredResults[i].Key > now) continue;
                    SendTo(seat, GameProtocol.DuelResult, seat.DeferredResults[i].Value);
                    seat.DeferredResults.RemoveAt(i--);
                }

                if (seat.StandingsReleaseTick >= 0 && (free || seat.StandingsReleaseTick <= now))
                {
                    seat.StandingsReleaseTick = -1;
                    SendTo(seat, GameProtocol.Standings, StandingsNow());
                }
            }
        }

        private void SendReveal(Seat seat)
        {
            RevealInfo reveal = Host.Flow.GetReveal(seat.Index);
            if (reveal == null) return;
            SendTo(seat, GameProtocol.RoundReveal, new RoundRevealMessage
            {
                Round = reveal.Round,
                BracketRound = reveal.BracketRound,
                Attempt = reveal.Attempt,
                PassKind = reveal.PassKind,
                OpponentSeat = reveal.IsBye ? -1 : reveal.Opponent,
                Side = reveal.Side,
                OwnResolve = reveal.OwnResolve,
                OpponentResolve = reveal.OpponentResolve,
                ArenaId = reveal.Arena?.ArenaId,
                OpponentLoadout = reveal.OpponentLoadout,
            });
        }

        private void SendFinalLoadouts()
        {
            foreach (Seat seat in _seats)
            {
                if (seat.IsBot) continue;
                RevealInfo reveal = Host.Flow.GetReveal(seat.Index);
                if (reveal == null || reveal.IsBye) continue;
                SendTo(seat, GameProtocol.FinalLoadouts, new FinalLoadoutsMessage
                {
                    Round = reveal.Round,
                    Attempt = reveal.Attempt,
                    OpponentSeat = reveal.Opponent,
                    Own = Host.Flow.Player(seat.Index).CurrentLoadout,
                    Opponent = Host.Flow.VisibleOpponentLoadout(seat.Index),
                });
            }
        }

        private void SendReports(DuelReport report)
        {
            for (int side = 0; side < 2; side++)
            {
                Seat seat = _seats[side == 0 ? report.Setup.Side0Player : report.Setup.Side1Player];
                if (seat.IsBot) continue;
                var msg = new WireDuelReport
                {
                    DuelId = report.Setup.DuelId,
                    WinnerSide = report.SimResult != null && !report.SimResult.IsDraw ? report.SimResult.Winner : -1,
                    Reason = (byte)report.Outcome.Reason,
                    Health0 = report.Sides[0].HealthFraction,
                    Health1 = report.Sides[1].HealthFraction,
                    FinalTick = report.FinalTick,
                };
                for (int s = 0; s < 2; s++)
                {
                    DuelSideReport sideReport = report.Sides[s];
                    msg.Feats[s].AddRange(sideReport.Feats);
                    IReadOnlyList<DuelEvent> sequence = sideReport.DecisiveSequence;
                    for (int k = 0; k < sequence.Count && msg.Decisive[s].Count < WireLimits.MaxDuelEventsPerMessage; k++)
                    {
                        WireDuelEvent w = DuelEventFilter.Convert(sequence[k], side);
                        if (w != null) msg.Decisive[s].Add(w);
                    }
                }

                SendTo(seat, GameProtocol.DuelReport, msg);
            }
        }

        private DuelResultMessage ResultWire(DuelResolvedEvent e)
        {
            Confluence.Match.DuelOutcome outcome = e.Outcome;
            return new DuelResultMessage
            {
                DuelId = e.Setup.DuelId,
                Round = e.Round,
                Attempt = e.Attempt,
                Side0Seat = e.Setup.Side0Player,
                Side1Seat = e.Setup.Side1Player,
                WinnerSeat = outcome.IsDraw ? -1 : outcome.Winner,
                Reason = outcome.Reason,
                Health0 = outcome.For(e.Setup.Side0Player).HealthFraction,
                Health1 = outcome.For(e.Setup.Side1Player).HealthFraction,
            };
        }

        private StandingsMessage StandingsNow()
        {
            var msg = new StandingsMessage { Round = Host.Flow.Round };
            foreach (MatchPlayer p in Host.Flow.Players)
            {
                msg.Seats.Add(new StandingWire
                {
                    Seat = p.Id,
                    Resolve = p.Resolve,
                    CombatWins = p.CombatWins,
                    Losses = p.Losses,
                    Draws = p.Draws,
                    DuelHalfWins = p.DuelHalfWins,
                    Eliminated = p.IsEliminated,
                    Connected = _seats[p.Id].IsBot || p.IsConnected,
                });
            }

            return msg;
        }

        private static MatchEndMessage EndWire(MatchResult result)
        {
            var msg = new MatchEndMessage
            {
                ChampionSeat = result.Champion,
                CountsForRating = result.CountsForRating,
                DuelHalfWins0 = result.DuelHalfWins0,
                DuelHalfWins1 = result.DuelHalfWins1,
            };
            foreach (MatchPlacement p in result.Placements)
                msg.Placements.Add(new PlacementWire { Seat = p.Player, FirstPosition = p.FirstPosition, LastPosition = p.LastPosition });
            return msg;
        }

        private ArenaMessage ArenaWire(HostedArena arena)
        {
            ArenaPreviewInfo info = arena.PublicPreview;
            var msg = new ArenaMessage
            {
                ArenaId = arena.ArenaId,
                Round = arena.Recipe.Round,
                RecipeSeed = arena.Recipe.RootSeed,
                Diameter = arena.Recipe.Diameter,
                BiomeId = arena.Recipe.BiomeId,
                AvailablePropIds = new List<string>(arena.Recipe.AvailablePropIds),
                LayoutHash = arena.LayoutHash,
                BiomeName = info.BiomeName,
                EnvironmentRule = info.EnvironmentRule,
                FinalFloorRadius = info.FinalFloorRadius,
            };
            foreach (PreviewSpawn s in info.Spawns) msg.Spawns.Add(new SpawnWire { Side = s.Side, Position = s.Position, YawDeg = s.YawDeg });
            foreach (PreviewModule m in info.Modules)
            {
                if (msg.Modules.Count >= WireLimits.MaxPreviewEntries) break;
                msg.Modules.Add(new PreviewEntryWire { Id = m.ModuleId, Name = m.Name, Category = m.Category, Position = m.Position, YawDeg = m.YawDeg });
            }

            foreach (PreviewProp p in info.Props)
            {
                if (msg.Props.Count >= WireLimits.MaxPreviewEntries) break;
                msg.Props.Add(new PreviewEntryWire { Id = p.PropId, Name = p.Name, Category = p.Category, Effect = p.Effect, Position = p.Position, YawDeg = p.YawDeg });
            }

            foreach (PreviewShape s in info.Shapes)
            {
                if (msg.Shapes.Count >= WireLimits.MaxPreviewEntries) break;
                msg.Shapes.Add(new PreviewShapeWire
                {
                    Kind = (byte)s.Kind,
                    Center = s.Center,
                    HalfExtents = s.HalfExtents,
                    YawDeg = s.YawDeg,
                    TopY = s.TopY,
                    Material = s.Material,
                    Walkable = s.Walkable,
                });
            }

            foreach (MaterialNote n in info.Materials)
            {
                if (msg.Materials.Count >= WireLimits.MaxPreviewEntries) break;
                msg.Materials.Add(new MaterialNoteWire { Key = n.Key, Description = n.Description, Count = n.Count });
            }

            return msg;
        }

        // ---- Sending helpers --------------------------------------------------------------------------------------

        private void BroadcastSeats<T>(MessageType<T> type, T message) where T : INetMessage, new()
        {
            foreach (Seat seat in _seats)
                if (!seat.IsBot) SendTo(seat, type, message);
        }

        private void SendTo<T>(Seat seat, MessageType<T> type, T message) where T : INetMessage, new()
        {
            if (seat == null || seat.IsBot || seat.Session == null || !seat.Session.IsConnected) return;
            if (message is ArenaMessage arena && arena.ArenaId != null) seat.AnnouncedArenas.Add(arena.ArenaId);
            if (Transport.Send(seat.Session, type, message)) Sent?.Invoke(seat.Index, type, message);
        }

        private void Log(string line) => _settings.Log?.Invoke(line);

        // ---- Nested state -----------------------------------------------------------------------------------------

        private sealed class Seat
        {
            public Seat(int index)
            {
                Index = index;
            }

            public int Index { get; }
            public PlayerSession Session;
            public string Name;
            public string WizardId;
            public InputRoute Route = InputRoute.Standard;
            public bool IsBot;
            public bool Joined;
            public NetworkInputSource Input;
            public bool Spectating;
            public int SpectateDuel = -1;

            /// <summary>Other pairs' results held while this player fights (release match tick, message).</summary>
            public readonly List<KeyValuePair<int, DuelResultMessage>> DeferredResults = new List<KeyValuePair<int, DuelResultMessage>>();

            /// <summary>Match tick at which held standings are sent, or -1.</summary>
            public int StandingsReleaseTick = -1;

            /// <summary>Arena ids announced to this seat (only these layouts may be downloaded).</summary>
            public readonly HashSet<string> AnnouncedArenas = new HashSet<string>(StringComparer.Ordinal);
        }

        /// <summary>One duelist's snapshot stream: rate selection and the frames it may still be acknowledged against.</summary>
        private sealed class Stream
        {
            private readonly Dictionary<int, DuelFrame> _sent = new Dictionary<int, DuelFrame>();
            private readonly Queue<int> _order = new Queue<int>();

            public Stream(Seat seat, int side)
            {
                Seat = seat;
                Side = side;
                Priority = Score;
            }

            public Seat Seat { get; }
            public int Side { get; }
            public SnapshotRateSelector Rate { get; } = new SnapshotRateSelector();
            public SnapshotLinkMonitor Monitor { get; } = new SnapshotLinkMonitor();
            public SessionMetrics.SnapshotStats Snap { get; } = new SessionMetrics.SnapshotStats();

            private readonly Dictionary<long, int> _deferrals = new Dictionary<long, int>();
            private Confluence.Reactions.Grid.CellGrid _grid;
            private Confluence.Core.Geometry.Vec3 _viewer;

            /// <summary>Scores deferred entries for the next budgeted encode (lower first).</summary>
            public Func<BudgetedSection, FrameEntry, float> Priority { get; }

            /// <summary>The receiver's position and the reaction grid the priority uses for this encode.</summary>
            public void Prepare(DuelWorld world, PrivateEntry own)
            {
                _grid = (world.Materials as Confluence.Sim.World.Materials.ReactionMaterials)?.World?.Grid;
                _viewer = own != null ? own.Motor.Position : world.Player(Side).Position;
            }

            /// <summary>
            /// Priority accumulator (research §2.4.1): distance from the receiver in meters, minus 2 m per earlier deferral
            /// (nothing starves), minus 4 m for sight-blocking volumes (steam blocks targeting, spec L167) and 2 m for
            /// cells whose state bits changed gameplay (ice, burning, charged, wet).
            /// </summary>
            private float Score(BudgetedSection section, FrameEntry entry)
            {
                Confluence.Core.Geometry.Vec3 at = _viewer;
                float bonus = 0f;
                switch (entry)
                {
                    case PropEntry prop:
                        at = Quantization.DequantizePosition(prop.Position);
                        break;
                    case CellEntry cell:
                        if (_grid != null && cell.Key < _grid.CellCount)
                        {
                            Confluence.Core.Geometry.Vec2 c = _grid.CellCenter(cell.Key);
                            at = new Confluence.Core.Geometry.Vec3(c.X, _viewer.Y, c.Y);
                        }

                        if ((cell.Bits & (CellBits.Ice | CellBits.Burning | CellBits.Charged | CellBits.Wet)) != 0) bonus += 2f;
                        break;
                    case VolumeEntry volume:
                        if (_grid != null)
                        {
                            Confluence.Core.Geometry.Vec2 c = _grid.CellCenter(volume.MinX + volume.SizeX / 2, volume.MinZ + volume.SizeZ / 2);
                            at = new Confluence.Core.Geometry.Vec3(c.X + volume.OffsetXmm / 1000f, _viewer.Y, c.Y + volume.OffsetZmm / 1000f);
                        }

                        if (volume.BlocksSight) bonus += 4f;
                        break;
                }

                _deferrals.TryGetValue(DeferralKey(section, entry.Key), out int waited);
                return Confluence.Core.Geometry.Vec3.Distance(_viewer, at) - bonus - 2f * waited;
            }

            /// <summary>Counts how often each changed entry was deferred; sent entries start over.</summary>
            public void NoteDeferrals(DuelFrame frame, DuelFrame held, int deferred)
            {
                if (deferred > 0)
                {
                    Snap.Partial++;
                    Snap.Deferred += deferred;
                }

                if (deferred == 0)
                {
                    if (_deferrals.Count > 0) _deferrals.Clear();
                    return;
                }

                Note(BudgetedSection.Props, frame.Props, held.Props);
                Note(BudgetedSection.Volumes, frame.Volumes, held.Volumes);
                Note(BudgetedSection.Cells, frame.Cells, held.Cells);
            }

            private void Note<T>(BudgetedSection section, List<T> wanted, List<T> held) where T : FrameEntry
            {
                int h = 0;
                foreach (T e in wanted)
                {
                    while (h < held.Count && held[h].Key < e.Key) h++;
                    bool sent = h < held.Count && held[h].Key == e.Key && ReferenceEquals(held[h], e);
                    long key = DeferralKey(section, e.Key);
                    if (sent) _deferrals.Remove(key);
                    else _deferrals[key] = _deferrals.TryGetValue(key, out int n) ? n + 1 : 1;
                }
            }

            private static long DeferralKey(BudgetedSection section, int key) => ((long)section << 32) | (uint)key;
            public List<WireDuelEvent> PendingEvents { get; } = new List<WireDuelEvent>();
            public List<WireDuelEvent> PendingCosmetic { get; } = new List<WireDuelEvent>();
            public long CosmeticDropped;

            /// <summary>Whether the opponent's live pose may be sent this step (spec L120).</summary>
            public bool OpponentReplicated = true;

            /// <summary>The opponent as this viewer last saw it (the frozen pose while hidden).</summary>
            public PlayerEntry LastSeenOpponent;
            public bool SendNow;
            public int AckedTick = -1;

            public void Acknowledge(int tick)
            {
                if (tick > AckedTick && _sent.ContainsKey(tick)) AckedTick = tick;
            }

            public DuelFrame BaselineFor(int tick)
            {
                if (AckedTick < 0 || AckedTick >= tick) return null;
                return _sent.TryGetValue(AckedTick, out DuelFrame f) ? f : null;
            }

            public void Remember(DuelFrame frame)
            {
                if (_sent.ContainsKey(frame.Tick)) return;
                _sent[frame.Tick] = frame;
                _order.Enqueue(frame.Tick);
                while (_order.Count > SentHistory) _sent.Remove(_order.Dequeue());
            }

            public void ResetBaseline()
            {
                AckedTick = -1;
                _sent.Clear();
                _order.Clear();
            }
        }

        private sealed class SpectatorItem
        {
            public int DuelId;
            public int Side0;
            public int Side1;
            public int Tick;
            public byte[] Body;
            public List<WireDuelEvent> Events;
            public bool Ended;
        }

        private sealed class LiveDuel
        {
            public LiveDuel(RunningDuel duel, int spectatorDelayTicks)
            {
                Duel = duel;
                Spectator = new SpectatorDelayBuffer<SpectatorItem>(spectatorDelayTicks);
            }

            public RunningDuel Duel { get; }
            public int[] SeatOfSide { get; } = new int[2];
            public DuelSideWire[] Sides { get; } = new DuelSideWire[2];
            public Stream[] Streams { get; } = new Stream[2];
            public SpectatorDelayBuffer<SpectatorItem> Spectator { get; }
            public List<WireDuelEvent> SpectatorEvents { get; } = new List<WireDuelEvent>();
            public CellCapture Cells { get; } = new CellCapture();
            public int StartMatchTick;
            public bool Ended;

            public DuelStartMessage StartFor(int side)
            {
                DuelSetup setup = Duel.Setup;
                var msg = new DuelStartMessage
                {
                    DuelId = setup.DuelId,
                    Round = setup.Round,
                    Attempt = setup.Attempt,
                    ArenaId = setup.Arena.ArenaId,
                    YourSide = side,
                    AlcoveTicks = Duel.World.AlcoveTicks,
                    CombatTicks = Duel.World.CombatTicks,
                    StartMatchTick = StartMatchTick,
                };
                msg.Sides[0] = Sides[0];
                msg.Sides[1] = Sides[1];
                return msg;
            }
        }

        /// <summary>Simulation time for clock sync: the authoritative tick plus the elapsed fraction of the current one.</summary>
        private sealed class ServerTickClock : ITickClock
        {
            private readonly GameServer _server;
            private readonly INetClock _clock;

            public ServerTickClock(GameServer server, INetClock clock)
            {
                _server = server;
                _clock = clock;
            }

            public double CurrentTick
            {
                get
                {
                    if (_server.Host == null || _server.Status != LobbyStatus.Running) return _server.CurrentTick;
                    double fraction = (_clock.NowMs - _server.LastTickMs) * (_server._settings.TicksPerWallSecond / 1000.0);
                    if (fraction < 0) fraction = 0;
                    if (fraction > 1) fraction = 1;
                    return _server.CurrentTick + fraction;
                }
            }
        }
    }
}
