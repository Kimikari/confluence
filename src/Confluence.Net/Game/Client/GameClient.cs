using System;
using System.Collections.Generic;
using System.Net;
using Confluence.Arena.Generation;
using Confluence.Casting.Grammars;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Hosting.Commands;
using Confluence.Match;
using Confluence.Net.Game.Snapshots;
using Confluence.Net.Game.Wire;
using Confluence.Net.Protocol;
using Confluence.Net.Requests;
using Confluence.Net.Serialization;
using Confluence.Net.Sync;
using Confluence.Net.Transport;
using Confluence.Sim.Content;
using Confluence.Sim.Input;

namespace Confluence.Net.Game.Client
{
    /// <summary>What a game client presents and how it behaves.</summary>
    public sealed class GameClientOptions
    {
        public string DisplayName { get; set; } = "Player";

        /// <summary>The wizard to join with (spec L292); null = do not join automatically.</summary>
        public string WizardId { get; set; }

        /// <summary>The input alternative (spec L343, L365-L369).</summary>
        public InputRoute Route { get; set; } = InputRoute.Standard;

        public Catalogue Catalogue { get; set; }

        /// <summary>Regenerates arenas for rendering and prediction; null = no prediction (authoritative positions only).</summary>
        public ArenaGenerator Generator { get; set; }

        public ContentRegistry Content { get; set; }

        public RulesetFingerprint Ruleset { get; set; }

        public string ClientBuild { get; set; } = "headless";

        public TransportSettings Transport { get; set; }

        public INetClock Clock { get; set; }

        /// <summary>Remote entities render this far behind the server estimate (ARCHITECTURE: about 100 ms).</summary>
        public int InterpolationDelayMs { get; set; } = NetRules.InterpolationDelayMs;

        /// <summary>Follow other duels as a delayed spectator whenever this player is outside a live duel (spec L49).</summary>
        public bool AutoSpectate { get; set; }

        public Action<string> Log { get; set; }
    }

    /// <summary>
    /// The client session library (spec §14), used by the Unity client and the headless client: connection and
    /// reconnection, clock sync, the lobby join, the match view models, shop and draft commands as idempotent requests,
    /// and the live duel (<see cref="ClientDuel"/>): per-tick inputs produced by <see cref="InputProvider"/> a little
    /// ahead of the server clock and sent with redundancy, snapshot decoding against acknowledged baselines, prediction
    /// and reconciliation of the own motor, interpolation of remote entities, and the predicted grammar overlay.
    /// Everything happens inside <see cref="Update"/> on the caller's thread.
    /// </summary>
    public sealed class GameClient : IDisposable
    {
        private const int MaxStepsPerUpdate = 8;
        private const int MaxLeadTicks = 12;

        private readonly GameClientOptions _options;
        private readonly INetClock _clock;
        private readonly ClientArenas _arenas;
        private readonly DuelInputMessage _packet = new DuelInputMessage();
        private readonly Dictionary<int, SnapshotBuffer<DuelFrame>> _spectated = new Dictionary<int, SnapshotBuffer<DuelFrame>>();
        private bool _joinSent;
        private long _lastUpdateMs = -1;
        private bool _spectateRequested;

        public GameClient(GameClientOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            if (options.Ruleset == null) throw new ArgumentException("A ruleset fingerprint is required.", nameof(options));
            _clock = options.Clock ?? SystemNetClock.Instance;
            _arenas = new ClientArenas(options.Catalogue, options.Generator);
            Transport = new ClientTransport(new ClientIdentity(options.DisplayName, options.Ruleset, options.ClientBuild), options.Transport, _clock);
            Transport.Messages.On(GameProtocol.LobbyState, (c, m) => OnLobby(m));
            Transport.Messages.On(GameProtocol.MatchSettings, (c, m) => { View.Settings = m; View.Seat = m.YourSeat; });
            Transport.Messages.On(GameProtocol.Phase, (c, m) => OnPhase(m));
            Transport.Messages.On(GameProtocol.RoundReveal, (c, m) => View.Reveal = m);
            Transport.Messages.On(GameProtocol.Arena, (c, m) => OnArena(m));
            Transport.Messages.On(GameProtocol.FinalLoadouts, (c, m) => View.FinalLoadouts = m);
            Transport.Messages.On(GameProtocol.Standings, (c, m) => View.Standings = m);
            Transport.Messages.On(GameProtocol.DuelResult, (c, m) => View.Results.Add(m));
            Transport.Messages.On(GameProtocol.MatchEnd, (c, m) => { View.End = m; MatchEnded?.Invoke(this); });
            Transport.Messages.On(GameProtocol.Notice, (c, m) => { View.Notices.Add(m.Text); Log("notice: " + m.Text); });
            Transport.Messages.On(GameProtocol.ShopState, (c, m) => { View.Shop = m; ShopChanged?.Invoke(this); });
            Transport.Messages.On(GameProtocol.DraftState, (c, m) => { View.Draft = m; DraftChanged?.Invoke(this); });
            Transport.Messages.On(GameProtocol.DuelStart, (c, m) => OnDuelStart(m));
            Transport.Messages.On(GameProtocol.Snapshot, (c, m) => OnSnapshot(m));
            Transport.Messages.On(GameProtocol.SnapshotReliable, (c, m) => OnSnapshot(m));
            Transport.Messages.On(GameProtocol.DuelEvents, (c, m) => OnEvents(m));
            Transport.Messages.On(GameProtocol.DuelReport, (c, m) => OnReport(m));
            Transport.Messages.On(GameProtocol.SpectatorFrame, (c, m) => OnSpectatorFrame(m));
        }

        public ClientTransport Transport { get; }

        public ClientMatchView View { get; } = new ClientMatchView();

        /// <summary>The live (or just finished) duel, or null.</summary>
        public ClientDuel Duel { get; private set; }

        public SpectatorView Spectator { get; } = new SpectatorView();

        public ClientArenas Arenas => _arenas;

        public GameClientOptions Options => _options;

        /// <summary>Produces the command for the duel tick in <see cref="ClientDuel.LocalTick"/> + 1 (null = idle input).</summary>
        public Func<ClientDuel, int, PlayerInput> InputProvider { get; set; }

        public event Action<GameClient> ShopChanged;
        public event Action<GameClient> DraftChanged;
        public event Action<GameClient, PhaseMessage> PhaseChanged;
        public event Action<GameClient, ClientDuel> DuelStarted;
        public event Action<GameClient, ClientDuel> DuelEnded;
        public event Action<GameClient> MatchEnded;

        /// <summary>Estimated current server tick (fractional), or -1 before the clock has an estimate.</summary>
        public double ServerTick => Transport.Clock.HasEstimate ? Transport.Clock.EstimateServerTick(_clock.NowMs) : -1;

        public int InputsSent { get; private set; }

        public bool IsConnected => Transport.IsConnected;

        public void Connect(string host, int port) => Transport.Connect(host, port);

        public void Connect(IPEndPoint endpoint) => Transport.Connect(endpoint);

        /// <summary>Pumps the session: receive and handle, produce due inputs, advance interpolation, send.</summary>
        public void Update()
        {
            Transport.Poll();
            long now = _clock.NowMs;
            float dt = _lastUpdateMs < 0 ? 0f : Math.Max(0f, (now - _lastUpdateMs) / 1000f);
            _lastUpdateMs = now;
            if (Transport.IsConnected && !_joinSent && View.Lobby != null && _options.WizardId != null && View.Lobby.Status == LobbyStatus.Waiting)
            {
                Join(_options.WizardId, _options.Route);
            }

            ClientDuel duel = Duel;
            if (duel != null)
            {
                if (duel.Layout == null)
                {
                    ArenaLayout layout = _arenas.TryGet(duel.Start.ArenaId);
                    if (layout != null) duel.AttachLayout(layout);
                }

                if (!duel.Ended && Transport.IsConnected && Transport.Clock.HasEstimate) StepInputs(duel);
                double serverTick = ServerTick;
                if (serverTick >= 0)
                {
                    // The delay is simulation time (100 ms = 6 ticks), so an accelerated test server keeps the same buffer depth.
                    double delayTicks = _options.InterpolationDelayMs * (double)Confluence.Core.Time.Ticks.PerSecond / 1000.0;
                    double render = serverTick - delayTicks - duel.Start.StartMatchTick;
                    duel.UpdateRender(render, dt);
                }
            }

            if (_options.AutoSpectate && !_spectateRequested && Transport.IsConnected && View.Settings != null && (duel == null || duel.Ended))
            {
                Spectate(-1);
            }

            Transport.Flush();
        }

        // ---- Lobby and commands -----------------------------------------------------------------------------------

        public void Join(string wizardId, InputRoute route)
        {
            _joinSent = true;
            Transport.Requests.Send(GameProtocol.JoinLobby, new JoinLobbyRequest { WizardId = wizardId, Route = route }, r =>
            {
                View.LastJoinCode = r.IsOk ? JoinCode.Accepted : (JoinCode)r.Reason;
                View.LastJoinDetail = r.Body?.Detail;
                if (r.IsOk) View.Seat = r.Body.Seat;
                else if (r.Status == RequestStatus.ConnectionLost) _joinSent = false;
                Log("join " + (r.IsOk ? "accepted as seat " + r.Body.Seat : "refused: " + View.LastJoinCode + " " + View.LastJoinDetail));
            });
        }

        /// <summary>Sends a shop, draft, or surrender command on its request type; returns the request id.</summary>
        public uint Send(CommandRequest command, Action<RequestResult<CommandResponse>> onResult = null)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            View.CommandsSent++;
            return Transport.Requests.Send(GameProtocol.RequestFor(command.Kind), command, r =>
            {
                if (r.Status == RequestStatus.RateLimited) View.CommandsRateLimited++;
                if (r.Body != null)
                {
                    View.LastCommand = r.Body;
                    if (r.Body.Duplicate) View.DuplicateResponses++;
                }

                if (r.IsOk) View.CommandsSucceeded++;
                else View.CommandsRejected++;
                onResult?.Invoke(r);
            });
        }

        public uint Buy(int position, Action<RequestResult<CommandResponse>> onResult = null) => Send(CommandRequest.Buy(position), onResult);

        public uint Ready(Action<RequestResult<CommandResponse>> onResult = null) => Send(CommandRequest.Ready(), onResult);

        public uint Surrender(Action<RequestResult<CommandResponse>> onResult = null) => Send(CommandRequest.Surrender(), onResult);

        /// <summary>Asks to follow a duel as a delayed spectator (-1 = any); refused silently while in a live duel.</summary>
        public void Spectate(int duelId)
        {
            _spectateRequested = Transport.Send(GameProtocol.Spectate, new SpectateRequest { DuelId = duelId });
        }

        public void Disconnect() => Transport.Disconnect();

        public void Dispose() => Transport.Dispose();

        // ---- Inputs -----------------------------------------------------------------------------------------------

        private void StepInputs(ClientDuel duel)
        {
            double estimate = Transport.Clock.EstimateServerTick(_clock.NowMs);
            int lead = Math.Max(1, Math.Min(MaxLeadTicks, Transport.Clock.RecommendedInputLeadTicks));
            int target = (int)Math.Floor(estimate) + lead - duel.Start.StartMatchTick;
            if (duel.LocalTick == 0 || target - duel.LocalTick > 60 || duel.LocalTick - target > 60)
            {
                // First command of the duel, or the clock jumped: restart the local tick just behind the target.
                duel.LocalTick = Math.Max(Math.Max(0, duel.LatestTick), target - 1);
            }

            int steps = 0;
            while (duel.LocalTick < target && steps < MaxStepsPerUpdate)
            {
                int tick = duel.LocalTick + 1;
                PlayerInput input = InputProvider != null ? InputProvider(duel, tick) : PlayerInput.Idle(duel.HasPrediction ? duel.Predicted.YawDeg : 0f);
                int view = (int)Math.Floor(duel.RenderTick);
                input.ViewTick = view > 0 && view < tick ? view : 0;
                duel.Produce(tick, input);
                _packet.Commands.Clear();
                duel.FillPacket(_packet);
                if (Transport.Send(GameProtocol.DuelInput, _packet)) InputsSent++;
                steps++;
            }
        }

        // ---- Handlers ---------------------------------------------------------------------------------------------

        private void OnLobby(LobbyStateMessage message)
        {
            View.Lobby = message;
        }

        private void OnPhase(PhaseMessage message)
        {
            View.Phase = message;
            View.PhaseHistory.Add(message);
            if (message.Phase == MatchPhase.Shop) View.Draft = null;
            PhaseChanged?.Invoke(this, message);
        }

        private void OnArena(ArenaMessage message)
        {
            View.Arena = message;
            if (message.ArenaId != null) View.Arenas[message.ArenaId] = message;
            _arenas.Prepare(message);
        }

        private void OnDuelStart(DuelStartMessage message)
        {
            if (Duel != null && Duel.DuelId == message.DuelId && !Duel.Ended) return; // a catch-up resend of the running duel
            Duel = new ClientDuel(message, _options.Catalogue, _options.Content);
            _spectateRequested = false;
            ArenaLayout layout = _arenas.TryGet(message.ArenaId);
            if (layout != null) Duel.AttachLayout(layout);
            Log("duel " + message.DuelId + " started: side " + message.YourSide + " vs seat " + message.Sides[1 - message.YourSide].Seat +
                " (" + message.Sides[1 - message.YourSide].WizardId + ")");
            DuelStarted?.Invoke(this, Duel);
        }

        private void OnSnapshot(SnapshotMessage message)
        {
            Duel?.OnSnapshot(message.Header, message.Body);
        }

        private void OnEvents(DuelEventsMessage message)
        {
            ClientDuel duel = Duel;
            if (duel == null || duel.DuelId != message.DuelId) return;
            bool wasEnded = duel.Ended;
            duel.OnEvents(message);
            if (!wasEnded && duel.Ended) DuelEnded?.Invoke(this, duel);
        }

        private void OnReport(DuelReportMessage message)
        {
            View.Reports.Add(message);
            if (Duel != null && Duel.DuelId == message.DuelId)
            {
                bool wasEnded = Duel.Ended;
                Duel.Report = message;
                Duel.Ended = true;
                if (!wasEnded) DuelEnded?.Invoke(this, Duel);
            }
        }

        private void OnSpectatorFrame(SpectatorFrameMessage message)
        {
            try
            {
                var reader = new NetReader(message.FrameBody, 0, message.FrameBody.Length);
                DuelFrame frame = SnapshotCodec.DecodeBody(reader, new SnapshotHeader(message.FrameTick, 0), null, out _);
                Spectator.LatestByDuel[message.DuelId] = frame;
                Spectator.SeatsOfDuel0[message.DuelId] = message.Side0Seat;
                Spectator.SeatsOfDuel1[message.DuelId] = message.Side1Seat;
                Spectator.Events.AddRange(message.Events);
                Spectator.FramesReceived++;
                double now = ServerTick;
                if (now >= 0)
                {
                    int delay = (int)Math.Floor(now) - frame.MatchTick;
                    if (delay < Spectator.MinObservedDelayTicks) Spectator.MinObservedDelayTicks = delay;
                }
            }
            catch (NetReadException)
            {
                // A malformed spectator frame is dropped; nothing depends on it.
            }
        }

        private void Log(string line) => _options.Log?.Invoke(_options.DisplayName + ": " + line);
    }
}
