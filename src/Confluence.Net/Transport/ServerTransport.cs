using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using Confluence.Net.Protocol;
using Confluence.Net.RateLimiting;
using Confluence.Net.Requests;
using Confluence.Net.Serialization;
using LiteNetLib;

namespace Confluence.Net.Transport
{
    /// <summary>What the server requires of connecting clients.</summary>
    public sealed class ServerIdentity
    {
        public ServerIdentity(RulesetFingerprint ruleset)
        {
            Ruleset = ruleset ?? throw new ArgumentNullException(nameof(ruleset));
        }

        public RulesetFingerprint Ruleset { get; }

        public string RulesVersion { get; set; } = NetRules.RulesVersion;

        public ushort ProtocolVersion { get; set; } = NetRules.ProtocolVersion;

        /// <summary>When set, only this exact client build may connect.</summary>
        public string RequiredClientBuild { get; set; }

        /// <summary>Connected plus resumable sessions allowed at once.</summary>
        public int MaxSessions { get; set; } = NetRules.DefaultMaxSessions;
    }

    /// <summary>Congestion signals of one connection.</summary>
    public struct LinkStats
    {
        public int RttMs;

        /// <summary>Fraction of packets reported lost over the last measurement window (0 when unknown).</summary>
        public float LossFraction;

        /// <summary>Reliable packets queued and not yet acknowledged, across all reliable lanes.</summary>
        public int ReliableQueued;
    }

    /// <summary>
    /// Authoritative server endpoint over LiteNetLib. Validates the handshake (protocol version, ruleset
    /// fingerprint, build, display name), binds connections to <see cref="PlayerSession"/> identities that
    /// survive reconnects inside the window (spec L55), and dispatches messages through <see cref="Messages"/>
    /// with direction, channel, and rate-limit checks. Nothing runs on another thread: all socket I/O, timers,
    /// and handlers happen inside <see cref="Poll"/> and <see cref="Flush"/>.
    /// </summary>
    public sealed class ServerTransport : IDisposable
    {
        private readonly NetManager _net;
        private readonly TransportSettings _settings;
        private readonly ServerIdentity _identity;
        private readonly INetClock _clock;
        private readonly Dictionary<int, NetPeer> _peerOfPlayer = new Dictionary<int, NetPeer>();
        private readonly Dictionary<int, LossWindow> _loss = new Dictionary<int, LossWindow>();
        private readonly NetReader _reader = new NetReader();
        private readonly NetWriter _writer = new NetWriter(1024);
        private readonly List<PlayerSession> _expired = new List<PlayerSession>();
        private readonly Func<PlayerSession, MessageType, bool> _admit;
        private long _lastUpdateMs;
        private ExceptionDispatchInfo _pendingFault;

        public ServerTransport(ServerIdentity identity, TransportSettings settings = null, INetClock clock = null, ITokenSource tokens = null)
        {
            _identity = identity ?? throw new ArgumentNullException(nameof(identity));
            _settings = (settings ?? new TransportSettings()).Clone();
            _clock = clock ?? SystemNetClock.Instance;
            Sessions = new SessionDirectory(_settings.ReconnectWindowMs, tokens ?? new CryptoTokenSource());
            Messages = new MessageRegistry<PlayerSession>(true);
            RateLimiter = new RateLimiter();
            Requests = new RequestServer(Messages, (s, channel, framed) => SendFramed(s, channel, framed, framed.Length), _clock, RateLimiter);
            TickClock = new ElapsedTickClock(_clock);
            _admit = (session, type) => RateLimiter.TryAcquire(session.PlayerId, type.Id, type.RateLimit, _clock.NowMs);

            Messages.On(ProtocolMessages.ClockPing, OnClockPing);

            _net = new NetManager(new Listener(this))
            {
                ChannelsCount = NetChannels.LaneCount,
                AutoRecycle = true,
                DisconnectTimeout = _settings.DisconnectTimeoutMs,
                PingInterval = _settings.PingIntervalMs,
                EnableStatistics = _settings.EnableStatistics,
                UnconnectedMessagesEnabled = false,
                UnsyncedEvents = false,
            };
        }

        public MessageRegistry<PlayerSession> Messages { get; }

        public RequestServer Requests { get; }

        public SessionDirectory Sessions { get; }

        public RateLimiter RateLimiter { get; }

        public TransportCounters Counters { get; } = new TransportCounters();

        public ServerIdentity Identity => _identity;

        public INetClock Clock => _clock;

        /// <summary>Simulation time reported to clients for clock sync; the match server sets its sim clock here.</summary>
        public ITickClock TickClock { get; set; }

        /// <summary>When false only reconnects are accepted (for example after the match started).</summary>
        public bool AcceptNewPlayers { get; set; } = true;

        /// <summary>
        /// Simulation ticks per wall-clock second announced in the welcome (the client's clock estimate runs at this
        /// pace). 60 in real time; a labeled accelerated test server announces its faster pace.
        /// </summary>
        public int WelcomeTickRate { get; set; } = NetRules.TickRate;

        /// <summary>Largest unreliable or sequenced payload that fits one datagram for this player right now (0 when not connected).</summary>
        public int MaxUnreliablePayload(PlayerSession session)
        {
            if (session == null || !_peerOfPlayer.TryGetValue(session.PlayerId, out NetPeer peer)) return 0;
            return peer.GetMaxSinglePacketSize(DeliveryMethod.Sequenced);
        }

        public bool IsRunning => _net.IsRunning;

        public int Port => _net.LocalPort;

        /// <summary>A session was bound to a connection; the flag is true for a resumed identity.</summary>
        public event Action<PlayerSession, bool> SessionConnected;

        /// <summary>A session lost its connection; it stays resumable for the reconnect window.</summary>
        public event Action<PlayerSession> SessionDisconnected;

        /// <summary>A session can no longer be resumed (window passed or closed for good).</summary>
        public event Action<PlayerSession> SessionExpired;

        /// <summary>A connection request was refused.</summary>
        public event Action<IPEndPoint, ConnectionRejection> ConnectionRejected;

        /// <summary>A message was dropped (violation or rate limit) or its handler threw.</summary>
        public event Action<PlayerSession, DispatchOutcome> MessageRejected;

        /// <summary>Starts listening and returns the bound port (0 picks an ephemeral port).</summary>
        public int Start(int port = 0, IPAddress bindAddress = null)
        {
            if (_net.IsRunning) throw new InvalidOperationException("The server is already running.");
            IPAddress v4 = bindAddress ?? IPAddress.Any;
            IPAddress v6 = IPAddress.IsLoopback(v4) ? IPAddress.IPv6Loopback : IPAddress.IPv6Any;
            bool started = _settings.ManualMode ? _net.StartInManualMode(v4, v6, port) : _net.Start(v4, v6, port);
            if (!started)
            {
                throw new SocketException((int)SocketError.AddressAlreadyInUse);
            }

            _lastUpdateMs = _clock.NowMs;
            return _net.LocalPort;
        }

        /// <summary>
        /// Pumps the transport: receives packets, raises events, runs handlers, advances library timers (sending
        /// acknowledgements and resends), and retires sessions whose reconnect window has passed.
        /// </summary>
        public void Poll()
        {
            if (!_net.IsRunning) return;
            _net.PollEvents();
            Advance();
            ExpireOverdue();
            UpdateLossWindows();
            RethrowPendingFault();
        }

        /// <summary>Sends everything queued since the last update without waiting for the next Poll.</summary>
        public void Flush()
        {
            if (!_net.IsRunning) return;
            if (_settings.ManualMode) _net.ManualUpdate(0f);
            else _net.TriggerUpdate();
        }

        public void Stop()
        {
            if (!_net.IsRunning) return;
            _net.Stop(true);
            long now = _clock.NowMs;
            foreach (PlayerSession s in Sessions.All)
            {
                if (s.IsConnected)
                {
                    Sessions.MarkDisconnected(s, now);
                }
            }

            _peerOfPlayer.Clear();
        }

        public void Dispose() => Stop();

        /// <summary>
        /// Clean shutdown: every connected player receives a server-shutdown notice (their client shows why the session
        /// ended and does not try to resume), queued traffic is flushed, then the socket closes.
        /// </summary>
        public void Shutdown(string detail)
        {
            if (!_net.IsRunning) return;
            var connected = new List<PlayerSession>();
            foreach (PlayerSession s in Sessions.All)
                if (s.IsConnected) connected.Add(s);
            Flush();
            foreach (PlayerSession s in connected) Close(s, DisconnectNoticeReason.ServerShutdown, detail ?? "server shutdown", false);
            Flush();
            Stop();
        }

        // ---- Sending ---------------------------------------------------------------------------

        /// <summary>Sends to one player. Returns false when the player is not connected or the packet is oversized.</summary>
        public bool Send<T>(PlayerSession to, MessageType<T> type, T message)
            where T : INetMessage, new()
        {
            if (to == null) throw new ArgumentNullException(nameof(to));
            CheckSendable(type);
            _writer.Reset();
            MessageFraming.Write(_writer, type, message);
            return SendFramed(to, type.Channel, _writer.Buffer, _writer.Length);
        }

        /// <summary>Sends to every connected player (in player id order) except <paramref name="except"/>; returns the count.</summary>
        public int Broadcast<T>(MessageType<T> type, T message, PlayerSession except = null)
            where T : INetMessage, new()
        {
            CheckSendable(type);
            _writer.Reset();
            MessageFraming.Write(_writer, type, message);
            int sent = 0;
            foreach (PlayerSession s in Sessions.All)
            {
                if (s.IsConnected && !ReferenceEquals(s, except) && SendFramed(s, type.Channel, _writer.Buffer, _writer.Length))
                {
                    sent++;
                }
            }

            return sent;
        }

        /// <summary>Closes a player's connection. Unless <paramref name="allowResume"/>, the session expires at once.</summary>
        public void Kick(PlayerSession session, string detail, bool allowResume = false)
        {
            Close(session, DisconnectNoticeReason.Kicked, detail, allowResume);
        }

        /// <summary>Round-trip time, windowed loss, and reliable backlog of a connected player.</summary>
        public LinkStats GetLinkStats(PlayerSession session)
        {
            var stats = new LinkStats();
            if (session == null || !_peerOfPlayer.TryGetValue(session.PlayerId, out NetPeer peer))
            {
                return stats;
            }

            stats.RttMs = peer.RoundTripTime;
            if (_loss.TryGetValue(session.PlayerId, out LossWindow window))
            {
                stats.LossFraction = window.Fraction;
            }

            for (byte lane = 0; lane < NetChannels.LaneCount; lane++)
            {
                NetChannel channel = (NetChannel)lane;
                if (NetChannels.IsReliable(channel))
                {
                    stats.ReliableQueued += peer.GetPacketsCountInReliableQueue(lane, NetChannels.DeliveryOf(channel) == DeliveryMethod.ReliableOrdered);
                }
            }

            return stats;
        }

        internal bool SendFramed(PlayerSession to, NetChannel channel, byte[] data, int length)
        {
            // Framed messages are never empty; an empty reliable payload would stall its lane (see ClientTransport).
            if (length <= 0 || !_peerOfPlayer.TryGetValue(to.PlayerId, out NetPeer peer))
            {
                return false;
            }

            DeliveryMethod method = NetChannels.DeliveryOf(channel);
            if (!NetChannels.IsReliable(channel) && length > peer.GetMaxSinglePacketSize(method))
            {
                Counters.RecordOversized();
                to.Counters.RecordOversized();
                return false;
            }

            peer.Send(data, 0, length, NetChannels.LaneOf(channel), method);
            Counters.RecordSent(length);
            to.Counters.RecordSent(length);
            return true;
        }

        private static void CheckSendable(MessageType type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (!type.CanBeReceivedBy(false))
            {
                throw new ArgumentException(type + " is a client-to-server message.", nameof(type));
            }
        }

        // ---- Handshake -------------------------------------------------------------------------

        private void OnConnectionRequest(ConnectionRequest request)
        {
            ArraySegment<byte> data = request.Data.GetRemainingBytesSegment();
            byte[] bytes = data.Array ?? new byte[0];

            // The protocol version is checked on the frozen preamble first, so a client of another protocol gets
            // the unsupported-version reason (spec L427) even if the rest of its handshake has another layout.
            _reader.Reset(bytes, data.Offset, data.Count);
            if (HandshakeRequest.TryReadProtocolVersion(_reader, out ushort clientProtocol) && clientProtocol != _identity.ProtocolVersion)
            {
                Reject(request, RejectReason.UnsupportedProtocol,
                    "server protocol " + _identity.ProtocolVersion + ", client protocol " + clientProtocol);
                return;
            }

            var handshake = new HandshakeRequest();
            try
            {
                _reader.Reset(bytes, data.Offset, data.Count);
                handshake.Deserialize(_reader);
                _reader.ExpectEnd();
            }
            catch (NetReadException e)
            {
                Reject(request, RejectReason.MalformedHandshake, e.Error.ToString());
                return;
            }

            if (!handshake.Ruleset.Equals(_identity.Ruleset))
            {
                Reject(request, RejectReason.RulesetMismatch,
                    "server ruleset " + _identity.Ruleset.ShortHex + " (" + _identity.RulesVersion + "), client ruleset "
                    + handshake.Ruleset.ShortHex + " (" + handshake.RulesVersion + ")");
                return;
            }

            if (_identity.RequiredClientBuild != null && handshake.ClientBuild != _identity.RequiredClientBuild)
            {
                Reject(request, RejectReason.UnsupportedClientBuild, "server requires client build " + _identity.RequiredClientBuild);
                return;
            }

            string name = DisplayNames.Normalize(handshake.DisplayName);
            if (name == null)
            {
                Reject(request, RejectReason.InvalidDisplayName,
                    "display names have " + NetRules.DisplayNameMinChars + "-" + NetRules.DisplayNameMaxChars + " characters and no control characters");
                return;
            }

            ExpireOverdue();
            long now = _clock.NowMs;
            if (!handshake.ReconnectToken.IsNone)
            {
                switch (Sessions.Check(handshake.ReconnectToken, now, out PlayerSession existing))
                {
                    case ResumeCheck.UnknownToken:
                        Reject(request, RejectReason.UnknownSession, "this server does not know the session");
                        return;
                    case ResumeCheck.Expired:
                        Reject(request, RejectReason.ReconnectExpired, "the reconnect window has passed");
                        return;
                }

                if (existing.IsConnected)
                {
                    // The same player is back on a new connection before the old one timed out: the newest wins.
                    Close(existing, DisconnectNoticeReason.SessionTakenOver, "reconnected from another connection", true);
                }

                existing.ClientBuild = handshake.ClientBuild;
                Bind(existing, request.Accept(), true);
                return;
            }

            if (!AcceptNewPlayers)
            {
                Reject(request, RejectReason.NotAcceptingPlayers, "the server is not accepting new players");
                return;
            }

            if (Sessions.LiveCount >= _identity.MaxSessions)
            {
                Reject(request, RejectReason.ServerFull, "the server is full (" + _identity.MaxSessions + " players)");
                return;
            }

            PlayerSession session = Sessions.Create(name, handshake.ClientBuild);
            Bind(session, request.Accept(), false);
        }

        private void Reject(ConnectionRequest request, RejectReason reason, string detail)
        {
            var rejection = new ConnectionRejection
            {
                Reason = reason,
                ServerProtocolVersion = _identity.ProtocolVersion,
                ServerRulesVersion = _identity.RulesVersion,
                ServerRuleset = _identity.Ruleset,
                Detail = detail,
            };
            _writer.Reset();
            rejection.Serialize(_writer);
            request.Reject(_writer.ToArray());
            Counters.RecordRejection(reason);
            ConnectionRejected?.Invoke(request.RemoteEndPoint, rejection);
        }

        private void Bind(PlayerSession session, NetPeer peer, bool resumed)
        {
            peer.Tag = session;
            _peerOfPlayer[session.PlayerId] = peer;
            _loss[session.PlayerId] = new LossWindow();
            Sessions.MarkConnected(session, peer.Id);

            var welcome = new Welcome
            {
                PlayerId = session.PlayerId,
                ReconnectToken = session.Token,
                Resumed = resumed,
                ReconnectWindowMs = Sessions.ReconnectWindowMs,
                ServerTick = TickClock.CurrentTick,
                TickRate = WelcomeTickRate,
            };
            Send(session, ProtocolMessages.Welcome, welcome);
            SessionConnected?.Invoke(session, resumed);
        }

        private void Close(PlayerSession session, DisconnectNoticeReason reason, string detail, bool allowResume)
        {
            if (session == null) return;
            if (_peerOfPlayer.TryGetValue(session.PlayerId, out NetPeer peer))
            {
                var notice = new DisconnectNotice { Reason = reason, Detail = detail };
                _writer.Reset();
                notice.Serialize(_writer);
                peer.Disconnect(_writer.ToArray());
                Unbind(session);
            }

            if (!allowResume)
            {
                Retire(session);
            }
        }

        private void Unbind(PlayerSession session)
        {
            _peerOfPlayer.Remove(session.PlayerId);
            _loss.Remove(session.PlayerId);
            Sessions.MarkDisconnected(session, _clock.NowMs);
            SessionDisconnected?.Invoke(session);
        }

        private void Retire(PlayerSession session)
        {
            if (session.State == SessionState.Expired) return;
            Sessions.ExpireSession(session);
            Requests.Forget(session.PlayerId);
            RateLimiter.Forget(session.PlayerId);
            SessionExpired?.Invoke(session);
        }

        private void ExpireOverdue()
        {
            _expired.Clear();
            Sessions.ExpireOverdue(_clock.NowMs, _expired);
            foreach (PlayerSession s in _expired)
            {
                Requests.Forget(s.PlayerId);
                RateLimiter.Forget(s.PlayerId);
                SessionExpired?.Invoke(s);
            }
        }

        // ---- Receiving -------------------------------------------------------------------------

        private void OnPeerDisconnected(NetPeer peer)
        {
            if (peer.Tag is PlayerSession session && IsCurrent(session, peer))
            {
                Unbind(session);
            }
        }

        private void OnReceive(NetPeer peer, NetPacketReader packet, byte lane, DeliveryMethod method)
        {
            if (!(peer.Tag is PlayerSession session) || !IsCurrent(session, peer))
            {
                return;
            }

            int length = packet.AvailableBytes;
            DispatchOutcome outcome;
            if (!NetChannels.TryFromLane(lane, method, out NetChannel channel))
            {
                outcome = new DispatchOutcome(DispatchResult.WrongChannel, 0, null, null);
            }
            else
            {
                _reader.Reset(packet.RawData, packet.Position, length);
                outcome = Messages.Dispatch(session, _reader, channel, _admit);
            }

            Counters.Record(outcome.Result, length);
            session.Counters.Record(outcome.Result, length);
            if (outcome.Result == DispatchResult.Handled)
            {
                return;
            }

            MessageRejected?.Invoke(session, outcome);
            if (outcome.Result == DispatchResult.HandlerFault && _settings.RethrowHandlerExceptions && _pendingFault == null)
            {
                _pendingFault = ExceptionDispatchInfo.Capture(outcome.Error);
            }

            if (outcome.IsViolation && _settings.MalformedKickThreshold > 0 && session.Counters.Violations >= _settings.MalformedKickThreshold)
            {
                Close(session, DisconnectNoticeReason.ProtocolViolation, "too many malformed messages", false);
            }
        }

        private void OnClockPing(PlayerSession session, ClockPing ping)
        {
            Send(session, ProtocolMessages.ClockPong, new ClockPong { ClientTimeMs = ping.ClientTimeMs, ServerTick = TickClock.CurrentTick });
        }

        private bool IsCurrent(PlayerSession session, NetPeer peer)
        {
            return _peerOfPlayer.TryGetValue(session.PlayerId, out NetPeer current) && ReferenceEquals(current, peer);
        }

        // ---- Timers ----------------------------------------------------------------------------

        private void Advance()
        {
            long now = _clock.NowMs;
            long elapsed = Math.Max(0, Math.Min(_settings.MaxUpdateStepMs, now - _lastUpdateMs));
            _lastUpdateMs = now;
            if (_settings.ManualMode)
            {
                _net.ManualUpdate(elapsed);
            }
        }

        private void UpdateLossWindows()
        {
            if (!_settings.EnableStatistics) return;
            long now = _clock.NowMs;
            foreach (PlayerSession s in Sessions.All)
            {
                if (_peerOfPlayer.TryGetValue(s.PlayerId, out NetPeer peer) && _loss.TryGetValue(s.PlayerId, out LossWindow window))
                {
                    window.Update(now, peer.Statistics);
                }
            }
        }

        private void RethrowPendingFault()
        {
            ExceptionDispatchInfo fault = _pendingFault;
            _pendingFault = null;
            fault?.Throw();
        }

        /// <summary>Loss fraction over roughly one-second windows from LiteNetLib's cumulative statistics.</summary>
        private sealed class LossWindow
        {
            private const long WindowMs = 1000;
            private long _startMs = -1;
            private long _sent;
            private long _lost;

            public float Fraction { get; private set; }

            public void Update(long nowMs, NetStatistics stats)
            {
                if (stats == null) return;
                if (_startMs < 0)
                {
                    Begin(nowMs, stats);
                    return;
                }

                if (nowMs - _startMs < WindowMs) return;
                long sent = stats.PacketsSent - _sent;
                long lost = stats.PacketLoss - _lost;
                Fraction = sent > 0 ? Math.Min(1f, Math.Max(0f, lost / (float)sent)) : 0f;
                Begin(nowMs, stats);
            }

            private void Begin(long nowMs, NetStatistics stats)
            {
                _startMs = nowMs;
                _sent = stats.PacketsSent;
                _lost = stats.PacketLoss;
            }
        }

        private sealed class Listener : INetEventListener
        {
            private readonly ServerTransport _owner;

            public Listener(ServerTransport owner)
            {
                _owner = owner;
            }

            public void OnConnectionRequest(ConnectionRequest request) => _owner.OnConnectionRequest(request);

            public void OnPeerConnected(NetPeer peer)
            {
                // Sessions are bound when the request is accepted (OnConnectionRequest).
            }

            public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) => _owner.OnPeerDisconnected(peer);

            public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
            {
                _owner.OnReceive(peer, reader, channelNumber, deliveryMethod);
            }

            public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
            {
            }

            public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
            {
            }

            public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
            {
            }

            public void OnMessageDelivered(NetPeer peer, object userData)
            {
            }

            public void OnNtpResponse(LiteNetLib.Utils.NtpPacket packet)
            {
            }

            public void OnPeerAddressChanged(NetPeer peer, IPEndPoint previousAddress)
            {
            }
        }
    }
}
