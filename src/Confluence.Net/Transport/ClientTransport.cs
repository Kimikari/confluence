using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using Confluence.Net.Protocol;
using Confluence.Net.Requests;
using Confluence.Net.Serialization;
using Confluence.Net.Sync;
using LiteNetLib;

namespace Confluence.Net.Transport
{
    /// <summary>What the client presents at the handshake.</summary>
    public sealed class ClientIdentity
    {
        public ClientIdentity(string displayName, RulesetFingerprint ruleset, string clientBuild = "dev")
        {
            DisplayName = displayName;
            Ruleset = ruleset ?? throw new ArgumentNullException(nameof(ruleset));
            ClientBuild = clientBuild ?? "";
        }

        public string DisplayName { get; }

        public RulesetFingerprint Ruleset { get; }

        public string ClientBuild { get; }

        public string RulesVersion { get; set; } = NetRules.RulesVersion;

        public ushort ProtocolVersion { get; set; } = NetRules.ProtocolVersion;
    }

    public enum ClientState : byte
    {
        Idle,

        /// <summary>Transport connect in progress.</summary>
        Connecting,

        /// <summary>Accepted by the transport; waiting for the server's welcome.</summary>
        AwaitingWelcome,

        Connected,

        /// <summary>The connection ended; the session may still be resumable.</summary>
        Disconnected,

        /// <summary>The server refused the handshake; see <see cref="ClientTransport.Rejection"/>.</summary>
        Rejected,
    }

    /// <summary>Why the client's connection ended.</summary>
    public enum DisconnectCause : byte
    {
        None,
        LocalDisconnect,
        Timeout,
        ConnectionFailed,
        RemoteClosed,
        Kicked,
        ProtocolViolation,
        SessionTakenOver,
        ServerShutdown,
        Rejected,
        NetworkError,
    }

    /// <summary>
    /// Client endpoint over LiteNetLib. Connects with a <see cref="HandshakeRequest"/>, becomes Connected on the
    /// server's welcome (player id and reconnect token), keeps the server clock estimate fresh with periodic
    /// probes, and can resume its identity after a drop within the reconnect window (spec L55), optionally
    /// automatically. Rejections expose the server's protocol and ruleset so the UI can show the
    /// unsupported-version state (spec L427). Everything happens inside <see cref="Poll"/>.
    /// </summary>
    public sealed class ClientTransport : IDisposable
    {
        private readonly NetManager _net;
        private readonly TransportSettings _settings;
        private readonly ClientIdentity _identity;
        private readonly INetClock _clock;
        private readonly NetReader _reader = new NetReader();
        private readonly NetWriter _writer = new NetWriter(512);
        private readonly Queue<QueuedMessage> _preWelcome = new Queue<QueuedMessage>();
        private NetPeer _peer;
        private IPEndPoint _endpoint;
        private long _lastUpdateMs;
        private long _nextPingMs;
        private int _pingsSent;
        private long _disconnectedAtMs;
        private long _nextReconnectMs = -1;
        private int _reconnectWindowMs = NetRules.ReconnectWindowMs;
        private ExceptionDispatchInfo _pendingFault;

        public ClientTransport(ClientIdentity identity, TransportSettings settings = null, INetClock clock = null)
        {
            _identity = identity ?? throw new ArgumentNullException(nameof(identity));
            if (DisplayNames.Normalize(identity.DisplayName) == null)
            {
                throw new ArgumentException("Invalid display name.", nameof(identity));
            }

            _settings = (settings ?? new TransportSettings()).Clone();
            _clock = clock ?? SystemNetClock.Instance;
            Messages = new MessageRegistry<ClientTransport>(false);
            Requests = new RequestClient<ClientTransport>(Messages, (channel, framed) => SendFramed(channel, framed, framed.Length));
            Clock = new ClockSync();

            Messages.On(ProtocolMessages.Welcome, OnWelcome);
            Messages.On(ProtocolMessages.ClockPong, OnClockPong);

            _net = new NetManager(new Listener(this))
            {
                ChannelsCount = NetChannels.LaneCount,
                AutoRecycle = true,
                DisconnectTimeout = _settings.DisconnectTimeoutMs,
                PingInterval = _settings.PingIntervalMs,
                MaxConnectAttempts = _settings.MaxConnectAttempts,
                EnableStatistics = _settings.EnableStatistics,
                UnconnectedMessagesEnabled = false,
                UnsyncedEvents = false,
            };
        }

        public MessageRegistry<ClientTransport> Messages { get; }

        public RequestClient<ClientTransport> Requests { get; }

        /// <summary>Server clock estimate; recreated at the pace a welcome announces (60 ticks/s in real time).</summary>
        public ClockSync Clock { get; private set; }

        public TransportCounters Counters { get; } = new TransportCounters();

        public INetClock NetClock => _clock;

        public ClientState State { get; private set; }

        public bool IsConnected => State == ClientState.Connected;

        /// <summary>Player id from the last welcome (0 before the first).</summary>
        public int PlayerId { get; private set; }

        /// <summary>Token from the last welcome; persist it to resume after a client restart.</summary>
        public ReconnectToken ReconnectToken { get; private set; }

        /// <summary>True when the current connection resumed an earlier session.</summary>
        public bool Resumed { get; private set; }

        public ConnectionRejection Rejection { get; private set; }

        public DisconnectCause LastDisconnectCause { get; private set; }

        /// <summary>Detail text of the server's disconnect notice, if any.</summary>
        public string LastDisconnectDetail { get; private set; }

        /// <summary>Transport round-trip time of the current connection.</summary>
        public int RttMs => _peer != null ? _peer.RoundTripTime : 0;

        public event Action<ClientTransport> Connected;

        public event Action<ClientTransport> Disconnected;

        public event Action<ClientTransport> Rejected;

        /// <summary>A received message was dropped as illegal or its handler threw.</summary>
        public event Action<DispatchOutcome> MessageRejected;

        /// <summary>Connects as a new player, or resumes when a token from an earlier welcome is held.</summary>
        public void Connect(string host, int port)
        {
            IPAddress address;
            if (!IPAddress.TryParse(host, out address))
            {
                IPAddress[] found = Dns.GetHostAddresses(host);
                if (found.Length == 0) throw new ArgumentException("Cannot resolve host '" + host + "'.", nameof(host));
                address = found[0];
            }

            Connect(new IPEndPoint(address, port));
        }

        public void Connect(IPEndPoint endpoint) => Connect(endpoint, ReconnectToken);

        /// <summary>Connects presenting <paramref name="resumeToken"/> (for example persisted across a client restart).</summary>
        public void Connect(IPEndPoint endpoint, ReconnectToken resumeToken)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (State == ClientState.Connecting || State == ClientState.AwaitingWelcome || State == ClientState.Connected)
            {
                throw new InvalidOperationException("Already connecting or connected.");
            }

            EnsureStarted();
            _endpoint = endpoint;
            ReconnectToken = resumeToken;
            Rejection = null;
            _nextReconnectMs = -1;
            OpenConnection();
        }

        /// <summary>Reconnects to the last server with the held token.</summary>
        public void Reconnect()
        {
            if (_endpoint == null) throw new InvalidOperationException("No earlier connection to resume.");
            Connect(_endpoint, ReconnectToken);
        }

        /// <summary>Closes the connection gracefully. The token is kept so <see cref="Reconnect"/> can resume.</summary>
        public void Disconnect()
        {
            _nextReconnectMs = -1;
            if (_peer != null)
            {
                NetPeer peer = _peer;
                _peer = null;
                peer.Disconnect();
                EnterDisconnected(DisconnectCause.LocalDisconnect, null);
            }
        }

        /// <summary>
        /// Discards the clock estimate and probes quickly again (the server's simulation clock started or jumped, for
        /// example when the match begins after the lobby): stale samples never pin the estimate to the old timeline.
        /// </summary>
        public void ResyncClock()
        {
            Clock.Reset();
            _pingsSent = 0;
            _nextPingMs = _clock.NowMs;
        }

        /// <summary>Forgets the player identity (pending requests fail).</summary>
        public void ForgetSession()
        {
            ReconnectToken = ReconnectToken.None;
            PlayerId = 0;
            Requests.FailPending();
        }

        public void Poll()
        {
            if (!_net.IsRunning) return;
            _net.PollEvents();
            Advance();
            long now = _clock.NowMs;
            if (State == ClientState.Connected && now >= _nextPingMs)
            {
                SendClockPing(now);
            }

            if (_nextReconnectMs >= 0 && now >= _nextReconnectMs)
            {
                TryAutoReconnect(now);
            }

            ExceptionDispatchInfo fault = _pendingFault;
            _pendingFault = null;
            fault?.Throw();
        }

        /// <summary>Sends everything queued since the last update.</summary>
        public void Flush()
        {
            if (!_net.IsRunning) return;
            if (_settings.ManualMode) _net.ManualUpdate(0f);
            else _net.TriggerUpdate();
        }

        public void Stop()
        {
            _nextReconnectMs = -1;
            if (_net.IsRunning)
            {
                _net.Stop(true);
            }

            _peer = null;
            if (State != ClientState.Rejected)
            {
                State = ClientState.Idle;
            }
        }

        public void Dispose() => Stop();

        /// <summary>Sends a message to the server. Returns false when not connected or the packet is oversized.</summary>
        public bool Send<T>(MessageType<T> type, T message)
            where T : INetMessage, new()
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (!type.CanBeReceivedBy(true)) throw new ArgumentException(type + " is a server-to-client message.", nameof(type));
            if (State != ClientState.Connected) return false;
            _writer.Reset();
            MessageFraming.Write(_writer, type, message);
            return SendFramed(type.Channel, _writer.Buffer, _writer.Length);
        }

        /// <summary>Sends arbitrary bytes on a channel (tests use it to inject malformed packets).</summary>
        internal bool SendRaw(NetChannel channel, byte[] data) => SendFramed(channel, data, data.Length);

        internal bool SendFramed(NetChannel channel, byte[] data, int length)
        {
            // Framed messages are never empty; LiteNetLib never delivers an empty reliable payload and the lane
            // then stalls behind it, so refuse rather than send.
            if (State != ClientState.Connected || _peer == null || length <= 0) return false;
            DeliveryMethod method = NetChannels.DeliveryOf(channel);
            if (!NetChannels.IsReliable(channel) && length > _peer.GetMaxSinglePacketSize(method))
            {
                Counters.RecordOversized();
                return false;
            }

            _peer.Send(data, 0, length, NetChannels.LaneOf(channel), method);
            Counters.RecordSent(length);
            return true;
        }

        // ---- Lifecycle -------------------------------------------------------------------------

        private void EnsureStarted()
        {
            if (_net.IsRunning) return;
            bool started = _settings.ManualMode ? _net.StartInManualMode(0) : _net.Start();
            if (!started) throw new SocketException((int)SocketError.AddressNotAvailable);
            _lastUpdateMs = _clock.NowMs;
        }

        private void OpenConnection()
        {
            var handshake = new HandshakeRequest
            {
                ProtocolVersion = _identity.ProtocolVersion,
                Ruleset = _identity.Ruleset,
                RulesVersion = _identity.RulesVersion,
                ClientBuild = _identity.ClientBuild,
                DisplayName = _identity.DisplayName,
                ReconnectToken = ReconnectToken,
            };
            _writer.Reset();
            handshake.Serialize(_writer);
            _preWelcome.Clear();
            State = ClientState.Connecting;
            _peer = _net.Connect(_endpoint, new ReadOnlySpan<byte>(_writer.Buffer, 0, _writer.Length));
            if (_peer == null)
            {
                EnterDisconnected(DisconnectCause.ConnectionFailed, null);
            }
        }

        private void OnPeerConnected(NetPeer peer)
        {
            if (!ReferenceEquals(peer, _peer)) return;
            State = ClientState.AwaitingWelcome;
        }

        private void OnWelcome(ClientTransport self, Welcome welcome)
        {
            if (State != ClientState.AwaitingWelcome) return;
            bool sameSession = welcome.Resumed && PlayerId == welcome.PlayerId;
            PlayerId = welcome.PlayerId;
            ReconnectToken = welcome.ReconnectToken;
            Resumed = welcome.Resumed;
            _reconnectWindowMs = welcome.ReconnectWindowMs;
            State = ClientState.Connected;

            long now = _clock.NowMs;
            if (welcome.TickRate != Clock.TickRate) Clock = new ClockSync(welcome.TickRate);
            Clock.Reset();
            Clock.Seed(welcome.ServerTick, now, _peer != null ? _peer.Ping : 0);
            _pingsSent = 0;
            SendClockPing(now);

            if (!sameSession)
            {
                // A fresh identity cannot answer requests made by an earlier one.
                Requests.FailPending();
            }

            while (_preWelcome.Count > 0)
            {
                QueuedMessage m = _preWelcome.Dequeue();
                Dispatch(m.Channel, m.Data, 0, m.Data.Length);
            }

            if (sameSession)
            {
                Requests.ResendPending();
            }

            Connected?.Invoke(this);
        }

        private void OnClockPong(ClientTransport self, ClockPong pong)
        {
            Clock.AddSample(pong.ClientTimeMs, _clock.NowMs, pong.ServerTick);
        }

        private void SendClockPing(long now)
        {
            Send(ProtocolMessages.ClockPing, new ClockPing { ClientTimeMs = now });
            _pingsSent++;
            _nextPingMs = now + (_pingsSent < _settings.ClockPingFastCount ? _settings.ClockPingFastIntervalMs : _settings.ClockPingIntervalMs);
        }

        private void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
        {
            if (!ReferenceEquals(peer, _peer)) return;
            _peer = null;

            if (info.Reason == DisconnectReason.ConnectionRejected)
            {
                Rejection = ParseRejection(info.AdditionalData);
                State = ClientState.Rejected;
                LastDisconnectCause = DisconnectCause.Rejected;
                LastDisconnectDetail = Rejection.Detail;
                _nextReconnectMs = -1;
                Requests.FailPending();
                Rejected?.Invoke(this);
                return;
            }

            DisconnectCause cause = MapCause(info, out string detail);
            EnterDisconnected(cause, detail);
        }

        private void EnterDisconnected(DisconnectCause cause, string detail)
        {
            bool wasConnected = State == ClientState.Connected;
            State = ClientState.Disconnected;
            LastDisconnectCause = cause;
            LastDisconnectDetail = detail;
            long now = _clock.NowMs;
            if (wasConnected)
            {
                _disconnectedAtMs = now;
            }

            bool resumable = !ReconnectToken.IsNone
                && cause != DisconnectCause.LocalDisconnect
                && cause != DisconnectCause.Kicked
                && cause != DisconnectCause.ProtocolViolation
                && cause != DisconnectCause.SessionTakenOver
                && now - _disconnectedAtMs <= _reconnectWindowMs;
            _nextReconnectMs = _settings.AutoReconnect && resumable ? now + _settings.ReconnectRetryMs : -1;
            if (wasConnected)
            {
                Disconnected?.Invoke(this);
            }
        }

        private void TryAutoReconnect(long now)
        {
            _nextReconnectMs = -1;
            if (State != ClientState.Disconnected || now - _disconnectedAtMs > _reconnectWindowMs)
            {
                return;
            }

            OpenConnection();
        }

        private DisconnectCause MapCause(DisconnectInfo info, out string detail)
        {
            detail = null;
            switch (info.Reason)
            {
                case DisconnectReason.Timeout:
                    return DisconnectCause.Timeout;
                case DisconnectReason.ConnectionFailed:
                case DisconnectReason.UnknownHost:
                case DisconnectReason.HostUnreachable:
                    return DisconnectCause.ConnectionFailed;
                case DisconnectReason.NetworkUnreachable:
                    return DisconnectCause.NetworkError;
                case DisconnectReason.DisconnectPeerCalled:
                    return DisconnectCause.LocalDisconnect;
                case DisconnectReason.RemoteConnectionClose:
                    DisconnectNotice notice = ParseNotice(info.AdditionalData);
                    if (notice == null) return DisconnectCause.RemoteClosed;
                    detail = notice.Detail;
                    switch (notice.Reason)
                    {
                        case DisconnectNoticeReason.ProtocolViolation: return DisconnectCause.ProtocolViolation;
                        case DisconnectNoticeReason.SessionTakenOver: return DisconnectCause.SessionTakenOver;
                        case DisconnectNoticeReason.ServerShutdown: return DisconnectCause.ServerShutdown;
                        default: return DisconnectCause.Kicked;
                    }

                default:
                    return DisconnectCause.RemoteClosed;
            }
        }

        private ConnectionRejection ParseRejection(NetPacketReader data)
        {
            var rejection = new ConnectionRejection();
            try
            {
                if (data != null && data.AvailableBytes > 0)
                {
                    _reader.Reset(data.RawData, data.Position, data.AvailableBytes);
                    rejection.Deserialize(_reader);
                    return rejection;
                }
            }
            catch (NetReadException)
            {
            }

            return new ConnectionRejection { Reason = RejectReason.Unspecified, Detail = "the server rejected the connection" };
        }

        private DisconnectNotice ParseNotice(NetPacketReader data)
        {
            if (data == null || data.AvailableBytes == 0) return null;
            try
            {
                _reader.Reset(data.RawData, data.Position, data.AvailableBytes);
                var notice = new DisconnectNotice();
                notice.Deserialize(_reader);
                return notice;
            }
            catch (NetReadException)
            {
                return null;
            }
        }

        // ---- Receiving -------------------------------------------------------------------------

        private void OnReceive(NetPeer peer, NetPacketReader packet, byte lane, DeliveryMethod method)
        {
            if (!ReferenceEquals(peer, _peer)) return;
            int length = packet.AvailableBytes;
            if (!NetChannels.TryFromLane(lane, method, out NetChannel channel))
            {
                Record(new DispatchOutcome(DispatchResult.WrongChannel, 0, null, null), length);
                return;
            }

            // Lanes are ordered independently, so a match or event message may overtake the welcome on the control
            // lane; hold everything but control traffic until the identity is known.
            if (State == ClientState.AwaitingWelcome && channel != NetChannel.Control)
            {
                if (_preWelcome.Count >= NetRules.MaxPreWelcomeMessages)
                {
                    Counters.RecordPreWelcomeOverflow();
                    return;
                }

                var copy = new byte[length];
                Buffer.BlockCopy(packet.RawData, packet.Position, copy, 0, length);
                _preWelcome.Enqueue(new QueuedMessage(channel, copy));
                return;
            }

            Dispatch(channel, packet.RawData, packet.Position, length);
        }

        private void Dispatch(NetChannel channel, byte[] data, int offset, int length)
        {
            _reader.Reset(data, offset, length);
            Record(Messages.Dispatch(this, _reader, channel), length);
        }

        private void Record(DispatchOutcome outcome, int length)
        {
            Counters.Record(outcome.Result, length);
            if (outcome.Result == DispatchResult.Handled) return;
            MessageRejected?.Invoke(outcome);
            if (outcome.Result == DispatchResult.HandlerFault && _settings.RethrowHandlerExceptions && _pendingFault == null)
            {
                _pendingFault = ExceptionDispatchInfo.Capture(outcome.Error);
            }
        }

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

        private struct QueuedMessage
        {
            public QueuedMessage(NetChannel channel, byte[] data)
            {
                Channel = channel;
                Data = data;
            }

            public NetChannel Channel { get; }

            public byte[] Data { get; }
        }

        private sealed class Listener : INetEventListener
        {
            private readonly ClientTransport _owner;

            public Listener(ClientTransport owner)
            {
                _owner = owner;
            }

            public void OnPeerConnected(NetPeer peer) => _owner.OnPeerConnected(peer);

            public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) => _owner.OnPeerDisconnected(peer, disconnectInfo);

            public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
            {
                _owner.OnReceive(peer, reader, channelNumber, deliveryMethod);
            }

            public void OnConnectionRequest(ConnectionRequest request) => request.Reject();

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
