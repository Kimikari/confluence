using System;
using System.Collections.Generic;
using Confluence.Net.Protocol;
using Confluence.Net.RateLimiting;
using Confluence.Net.Serialization;
using Confluence.Net.Transport;

namespace Confluence.Net.Requests
{
    /// <summary>Classification of a request id against a player's ledger.</summary>
    public enum LedgerCheck : byte
    {
        New,
        Duplicate,
        Stale,
    }

    /// <summary>
    /// Per-player record of answered request ids. Ids within the window behind the highest id seen are looked
    /// up exactly (requests on different reliable channels may interleave); ids further back are stale, so an
    /// old transaction can never be replayed once its cached response is evicted.
    /// </summary>
    public sealed class RequestLedger
    {
        private readonly int _window;
        private readonly Dictionary<uint, byte[]> _responses = new Dictionary<uint, byte[]>();
        private readonly Queue<uint> _order = new Queue<uint>();

        public RequestLedger(int window)
        {
            if (window <= 0) throw new ArgumentOutOfRangeException(nameof(window));
            _window = window;
        }

        public uint HighestId { get; private set; }

        public int Count => _responses.Count;

        public LedgerCheck Check(uint requestId, out byte[] cachedResponse)
        {
            if (_responses.TryGetValue(requestId, out cachedResponse))
            {
                return LedgerCheck.Duplicate;
            }

            if (HighestId >= (uint)_window && requestId <= HighestId - (uint)_window)
            {
                return LedgerCheck.Stale;
            }

            return LedgerCheck.New;
        }

        /// <summary>Remembers the framed response of a processed request.</summary>
        public void Record(uint requestId, byte[] framedResponse)
        {
            if (_responses.ContainsKey(requestId)) return;
            _responses.Add(requestId, framedResponse);
            _order.Enqueue(requestId);
            if (requestId > HighestId) HighestId = requestId;
            while (_order.Count > _window)
            {
                _responses.Remove(_order.Dequeue());
            }
        }
    }

    /// <summary>
    /// Server half of request/response. For each request: duplicates get their original response bytes,
    /// stale ids get <see cref="RequestStatus.Stale"/>, over-limit requests get
    /// <see cref="RequestStatus.RateLimited"/> (not remembered, so a later retry can succeed), and new requests
    /// run their handler once; that response is remembered and sent. Ledgers persist across reconnects of the
    /// same session and are dropped when the session expires.
    /// </summary>
    public sealed class RequestServer
    {
        private readonly MessageRegistry<PlayerSession> _registry;
        private readonly Action<PlayerSession, NetChannel, byte[]> _sendFramed;
        private readonly INetClock _clock;
        private readonly RateLimiter _limiter;
        private readonly int _window;
        private readonly Dictionary<int, RequestLedger> _ledgers = new Dictionary<int, RequestLedger>();
        private readonly NetWriter _writer = new NetWriter(256);

        public RequestServer(
            MessageRegistry<PlayerSession> registry,
            Action<PlayerSession, NetChannel, byte[]> sendFramed,
            INetClock clock,
            RateLimiter limiter,
            int dedupeWindow = NetRules.RequestDedupeWindow)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _sendFramed = sendFramed ?? throw new ArgumentNullException(nameof(sendFramed));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _limiter = limiter ?? throw new ArgumentNullException(nameof(limiter));
            _window = dedupeWindow;
        }

        public long Processed { get; private set; }

        public long Duplicates { get; private set; }

        public long StaleRejected { get; private set; }

        public long RateLimited { get; private set; }

        public long ServerErrors { get; private set; }

        /// <summary>Registers the handler for <paramref name="type"/>.</summary>
        public void Handle<TRequest, TResponse>(RequestType<TRequest, TResponse> type, RequestHandler<PlayerSession, TRequest, TResponse> handler)
            where TRequest : INetMessage, new()
            where TResponse : INetMessage, new()
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            Handle(type, (RequestHandlerWithId<PlayerSession, TRequest, TResponse>)((session, id, body) => handler(session, body)));
        }

        /// <summary>
        /// Registers a handler that also receives the client's request id. Game commands use it as their idempotency key,
        /// so a resent request maps to the same transaction in the match host's ledger (spec L429).
        /// </summary>
        public void Handle<TRequest, TResponse>(RequestType<TRequest, TResponse> type, RequestHandlerWithId<PlayerSession, TRequest, TResponse> handler)
            where TRequest : INetMessage, new()
            where TResponse : INetMessage, new()
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            _registry.On(type.RequestMessage, (session, envelope) => OnRequest(type, handler, session, envelope));
        }

        /// <summary>Forgets a player's ledger (call when the session expires).</summary>
        public void Forget(int playerId) => _ledgers.Remove(playerId);

        public RequestLedger LedgerOf(int playerId)
        {
            if (!_ledgers.TryGetValue(playerId, out RequestLedger ledger))
            {
                ledger = new RequestLedger(_window);
                _ledgers[playerId] = ledger;
            }

            return ledger;
        }

        private void OnRequest<TRequest, TResponse>(
            RequestType<TRequest, TResponse> type,
            RequestHandlerWithId<PlayerSession, TRequest, TResponse> handler,
            PlayerSession session,
            RequestEnvelope<TRequest> envelope)
            where TRequest : INetMessage, new()
            where TResponse : INetMessage, new()
        {
            RequestLedger ledger = LedgerOf(session.PlayerId);
            switch (ledger.Check(envelope.RequestId, out byte[] cached))
            {
                case LedgerCheck.Duplicate:
                    Duplicates++;
                    _sendFramed(session, type.Channel, cached);
                    return;
                case LedgerCheck.Stale:
                    StaleRejected++;
                    _sendFramed(session, type.Channel, Frame(type, envelope.RequestId, RequestStatus.Stale, 0, default));
                    return;
            }

            if (!_limiter.TryAcquire(session.PlayerId, type.RequestMessage.Id, type.RateLimit, _clock.NowMs))
            {
                RateLimited++;
                _sendFramed(session, type.Channel, Frame(type, envelope.RequestId, RequestStatus.RateLimited, 0, default));
                return;
            }

            Reply<TResponse> reply;
            try
            {
                reply = handler(session, envelope.RequestId, envelope.Body);
            }
            catch
            {
                // The handler may have run partway; remember the failure so the same id never runs twice.
                ServerErrors++;
                byte[] failure = Frame(type, envelope.RequestId, RequestStatus.ServerError, 0, default);
                ledger.Record(envelope.RequestId, failure);
                _sendFramed(session, type.Channel, failure);
                throw;
            }

            Processed++;
            byte[] framed = Frame(type, envelope.RequestId, reply.Status, reply.Reason, reply.Body);
            ledger.Record(envelope.RequestId, framed);
            _sendFramed(session, type.Channel, framed);
        }

        private byte[] Frame<TRequest, TResponse>(RequestType<TRequest, TResponse> type, uint requestId, RequestStatus status, ushort reason, TResponse body)
            where TRequest : INetMessage, new()
            where TResponse : INetMessage, new()
        {
            var response = new ResponseEnvelope<TResponse> { RequestId = requestId, Status = status, Reason = reason, Body = body };
            _writer.Reset();
            MessageFraming.Write(_writer, type.ResponseMessage, response);
            return _writer.ToArray();
        }
    }
}
