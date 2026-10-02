using System;
using System.Collections.Generic;
using Confluence.Net.Protocol;

namespace Confluence.Net.Transport
{
    public enum SessionState : byte
    {
        Connected,

        /// <summary>Disconnected but inside the reconnect window; the identity can be resumed.</summary>
        Disconnected,

        /// <summary>The window passed or the session was closed for good; the token no longer works.</summary>
        Expired,
    }

    /// <summary>
    /// One player identity on the server. It outlives individual connections: a reconnect within the window
    /// (spec L55) binds a new connection to the same session and player id. Disconnecting never pauses the
    /// match; the match layer decides what an absent player's rounds do.
    /// </summary>
    public sealed class PlayerSession
    {
        internal PlayerSession(int playerId, string displayName, string clientBuild, ReconnectToken token)
        {
            PlayerId = playerId;
            DisplayName = displayName;
            ClientBuild = clientBuild;
            Token = token;
            State = SessionState.Disconnected;
        }

        /// <summary>Stable 1-based identity for the server's lifetime.</summary>
        public int PlayerId { get; }

        public string DisplayName { get; }

        /// <summary>Client build id reported at the first handshake (support logs).</summary>
        public string ClientBuild { get; internal set; }

        public SessionState State { get; internal set; }

        public bool IsConnected => State == SessionState.Connected;

        /// <summary>Number of connections bound to this session so far (1 after the first connect).</summary>
        public int ConnectionCount { get; internal set; }

        /// <summary>Network clock time of the last disconnect (meaningful while Disconnected or Expired).</summary>
        public long DisconnectedAtMs { get; internal set; }

        /// <summary>Counters of the current and previous connections of this player.</summary>
        public TransportCounters Counters { get; } = new TransportCounters();

        /// <summary>Free slot for the game layer (match seat, input buffers).</summary>
        public object Tag { get; set; }

        internal ReconnectToken Token { get; }

        internal int PeerId { get; set; } = -1;

        public override string ToString() => "P" + PlayerId + " '" + DisplayName + "' " + State;
    }

    /// <summary>Outcome of presenting a reconnect token.</summary>
    public enum ResumeCheck : byte
    {
        Resumable,
        UnknownToken,
        Expired,
    }

    /// <summary>
    /// Player sessions, their reconnect tokens, and the reconnect window. A token resumes its session when
    /// presented no later than the window after the server observed the disconnect (inclusive bound); after
    /// that the session expires permanently. Sessions are listed in player id order.
    /// </summary>
    public sealed class SessionDirectory
    {
        private readonly List<PlayerSession> _sessions = new List<PlayerSession>();
        private readonly Dictionary<ReconnectToken, PlayerSession> _byToken = new Dictionary<ReconnectToken, PlayerSession>();
        private readonly ITokenSource _tokens;
        private int _nextPlayerId = 1;

        public SessionDirectory(int reconnectWindowMs, ITokenSource tokens)
        {
            if (reconnectWindowMs < 0) throw new ArgumentOutOfRangeException(nameof(reconnectWindowMs));
            ReconnectWindowMs = reconnectWindowMs;
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        }

        public int ReconnectWindowMs { get; }

        /// <summary>All sessions ever created, in player id order.</summary>
        public IReadOnlyList<PlayerSession> All => _sessions;

        /// <summary>Sessions that are connected or still resumable.</summary>
        public int LiveCount
        {
            get
            {
                int n = 0;
                foreach (PlayerSession s in _sessions)
                {
                    if (s.State != SessionState.Expired) n++;
                }

                return n;
            }
        }

        public PlayerSession Create(string displayName, string clientBuild)
        {
            ReconnectToken token;
            do
            {
                token = _tokens.Next();
            }
            while (token.IsNone || _byToken.ContainsKey(token));

            var session = new PlayerSession(_nextPlayerId++, displayName, clientBuild, token);
            _sessions.Add(session);
            _byToken.Add(token, session);
            return session;
        }

        public bool TryGet(int playerId, out PlayerSession session)
        {
            int index = playerId - 1;
            if (index >= 0 && index < _sessions.Count)
            {
                session = _sessions[index];
                return true;
            }

            session = null;
            return false;
        }

        /// <summary>Whether <paramref name="token"/> can resume a session at <paramref name="nowMs"/>.</summary>
        public ResumeCheck Check(ReconnectToken token, long nowMs, out PlayerSession session)
        {
            if (token.IsNone || !_byToken.TryGetValue(token, out session))
            {
                session = null;
                return ResumeCheck.UnknownToken;
            }

            if (session.State == SessionState.Expired)
            {
                return ResumeCheck.Expired;
            }

            if (session.State == SessionState.Disconnected && nowMs - session.DisconnectedAtMs > ReconnectWindowMs)
            {
                return ResumeCheck.Expired; // ExpireOverdue retires it and reports the expiry
            }

            return ResumeCheck.Resumable;
        }

        internal void MarkConnected(PlayerSession session, int peerId)
        {
            session.State = SessionState.Connected;
            session.PeerId = peerId;
            session.ConnectionCount++;
        }

        internal void MarkDisconnected(PlayerSession session, long nowMs)
        {
            if (session.State != SessionState.Connected) return;
            session.State = SessionState.Disconnected;
            session.PeerId = -1;
            session.DisconnectedAtMs = nowMs;
        }

        /// <summary>Ends a session for good (window passed, kick, or match over); its token stops working.</summary>
        public void ExpireSession(PlayerSession session)
        {
            if (session == null || session.State == SessionState.Expired) return;
            session.State = SessionState.Expired;
            session.PeerId = -1;
            // Keep the token mapped so a late reconnect reports Expired rather than UnknownToken.
        }

        /// <summary>Expires every disconnected session whose window has passed; appends them in player id order.</summary>
        public void ExpireOverdue(long nowMs, List<PlayerSession> expired)
        {
            foreach (PlayerSession s in _sessions)
            {
                if (s.State == SessionState.Disconnected && nowMs - s.DisconnectedAtMs > ReconnectWindowMs)
                {
                    ExpireSession(s);
                    expired?.Add(s);
                }
            }
        }

        /// <summary>Milliseconds of reconnect window left for a disconnected session (0 when none).</summary>
        public long WindowRemainingMs(PlayerSession session, long nowMs)
        {
            if (session == null || session.State != SessionState.Disconnected) return 0;
            return Math.Max(0, ReconnectWindowMs - (nowMs - session.DisconnectedAtMs));
        }
    }
}
