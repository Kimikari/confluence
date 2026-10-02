using System;
using System.Collections.Generic;
using Confluence.Net.Protocol;
using Confluence.Net.Serialization;

namespace Confluence.Net.Requests
{
    /// <summary>
    /// Client half of request/response. Each request gets the next id (from 1), stays pending until its
    /// response arrives, and is re-sent with the same id after a reconnect so the server can answer it from its
    /// ledger instead of running the transaction twice. Each callback fires exactly once.
    /// </summary>
    /// <typeparam name="TSender">Sender type of the client's message registry.</typeparam>
    public sealed class RequestClient<TSender>
    {
        private readonly MessageRegistry<TSender> _registry;
        private readonly Func<NetChannel, byte[], bool> _sendFramed;
        private readonly SortedDictionary<uint, PendingRequest> _pending = new SortedDictionary<uint, PendingRequest>();
        private readonly HashSet<ushort> _registeredResponses = new HashSet<ushort>();
        private readonly NetWriter _writer = new NetWriter(256);
        private uint _nextId = 1;

        /// <param name="registry">Registry receiving responses.</param>
        /// <param name="sendFramed">Sends a framed message on a channel; returns false when not connected.</param>
        public RequestClient(MessageRegistry<TSender> registry, Func<NetChannel, byte[], bool> sendFramed)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _sendFramed = sendFramed ?? throw new ArgumentNullException(nameof(sendFramed));
        }

        public int PendingCount => _pending.Count;

        /// <summary>Id the next request will use.</summary>
        public uint NextRequestId => _nextId;

        /// <summary>Responses whose id had no pending request (duplicates after a resend).</summary>
        public long UnmatchedResponses { get; private set; }

        /// <summary>
        /// Sends a request and returns its id. When the client is not connected the request stays pending and goes
        /// out with <see cref="ResendPending"/> once the session resumes.
        /// </summary>
        public uint Send<TRequest, TResponse>(RequestType<TRequest, TResponse> type, TRequest request, Action<RequestResult<TResponse>> onResult)
            where TRequest : INetMessage, new()
            where TResponse : INetMessage, new()
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (request == null) throw new ArgumentNullException(nameof(request));
            EnsureResponseHandler(type);

            uint id = _nextId++;
            var envelope = new RequestEnvelope<TRequest> { RequestId = id, Body = request };
            _writer.Reset();
            MessageFraming.Write(_writer, type.RequestMessage, envelope);
            var pending = new PendingRequest<TResponse>(id, type.Channel, type.ResponseMessage.Id, _writer.ToArray(), onResult);
            _pending.Add(id, pending);
            _sendFramed(pending.Channel, pending.Framed);
            return id;
        }

        /// <summary>Sends every pending request again, in id order (after a resumed reconnect).</summary>
        public void ResendPending()
        {
            foreach (KeyValuePair<uint, PendingRequest> pair in _pending)
            {
                _sendFramed(pair.Value.Channel, pair.Value.Framed);
            }
        }

        /// <summary>Completes every pending request with <paramref name="status"/> (the session was lost).</summary>
        public void FailPending(RequestStatus status = RequestStatus.ConnectionLost)
        {
            var failed = new List<PendingRequest>(_pending.Values);
            _pending.Clear();
            foreach (PendingRequest p in failed)
            {
                p.Fail(status);
            }
        }

        /// <summary>Re-sends one pending request as-is (diagnostics and duplicate-handling tests).</summary>
        internal bool ResendRaw(uint requestId)
        {
            return _pending.TryGetValue(requestId, out PendingRequest p) && _sendFramed(p.Channel, p.Framed);
        }

        private void EnsureResponseHandler<TRequest, TResponse>(RequestType<TRequest, TResponse> type)
            where TRequest : INetMessage, new()
            where TResponse : INetMessage, new()
        {
            if (!_registeredResponses.Add(type.ResponseMessage.Id))
            {
                return;
            }

            _registry.On(type.ResponseMessage, (sender, response) => OnResponse(type.ResponseMessage.Id, response));
        }

        private void OnResponse<TResponse>(ushort responseMessageId, ResponseEnvelope<TResponse> response)
            where TResponse : INetMessage, new()
        {
            if (!_pending.TryGetValue(response.RequestId, out PendingRequest pending)
                || pending.ResponseMessageId != responseMessageId
                || !(pending is PendingRequest<TResponse> typed))
            {
                UnmatchedResponses++;
                return;
            }

            _pending.Remove(response.RequestId);
            typed.Complete(new RequestResult<TResponse>(response.RequestId, response.Status, response.Reason, response.Body));
        }

        private abstract class PendingRequest
        {
            protected PendingRequest(uint id, NetChannel channel, ushort responseMessageId, byte[] framed)
            {
                Id = id;
                Channel = channel;
                ResponseMessageId = responseMessageId;
                Framed = framed;
            }

            public uint Id { get; }

            public NetChannel Channel { get; }

            public ushort ResponseMessageId { get; }

            public byte[] Framed { get; }

            public abstract void Fail(RequestStatus status);
        }

        private sealed class PendingRequest<TResponse> : PendingRequest
        {
            private readonly Action<RequestResult<TResponse>> _callback;

            public PendingRequest(uint id, NetChannel channel, ushort responseMessageId, byte[] framed, Action<RequestResult<TResponse>> callback)
                : base(id, channel, responseMessageId, framed)
            {
                _callback = callback;
            }

            public void Complete(RequestResult<TResponse> result) => _callback?.Invoke(result);

            public override void Fail(RequestStatus status) => _callback?.Invoke(new RequestResult<TResponse>(Id, status, 0, default));
        }
    }
}
