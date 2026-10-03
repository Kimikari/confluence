using System;
using Confluence.Net.Protocol;
using Confluence.Net.RateLimiting;
using Confluence.Net.Serialization;

namespace Confluence.Net.Requests
{
    /// <summary>Outcome of a request.</summary>
    public enum RequestStatus : byte
    {
        /// <summary>Processed and accepted; the body is the result.</summary>
        Ok = 0,

        /// <summary>Processed and refused by the rules (for example not enough Crowns); the reason code explains.</summary>
        Rejected = 1,

        /// <summary>Not processed: the per-player rate limit was exhausted. A retry with the same id may succeed.</summary>
        RateLimited = 2,

        /// <summary>Not processed: the id is older than the server's duplicate-detection window.</summary>
        Stale = 3,

        /// <summary>The server handler failed; nothing should be assumed about the transaction.</summary>
        ServerError = 4,

        /// <summary>Client-local: the session ended before a response arrived.</summary>
        ConnectionLost = 5,
    }

    /// <summary>A request body tagged with its per-client request id (ids start at 1 and increase).</summary>
    public sealed class RequestEnvelope<T> : INetMessage
        where T : INetMessage, new()
    {
        public uint RequestId { get; set; }

        public T Body { get; set; }

        public void Serialize(NetWriter writer)
        {
            writer.WriteVarUInt(RequestId);
            Body.Serialize(writer);
        }

        public void Deserialize(NetReader reader)
        {
            RequestId = reader.ReadVarUInt();
            if (RequestId == 0)
            {
                throw new NetReadException(NetReadError.InvalidValue, "request id 0 is reserved");
            }

            Body = new T();
            Body.Deserialize(reader);
        }
    }

    /// <summary>Response to one request id. The body is present for Ok and Rejected outcomes.</summary>
    public sealed class ResponseEnvelope<T> : INetMessage
        where T : INetMessage, new()
    {
        public uint RequestId { get; set; }

        public RequestStatus Status { get; set; }

        public ushort Reason { get; set; }

        public T Body { get; set; }

        public static bool HasBody(RequestStatus status) => status == RequestStatus.Ok || status == RequestStatus.Rejected;

        public void Serialize(NetWriter writer)
        {
            writer.WriteVarUInt(RequestId);
            writer.WriteByte((byte)Status);
            writer.WriteVarUInt(Reason);
            if (HasBody(Status))
            {
                (Body == null ? new T() : Body).Serialize(writer);
            }
        }

        public void Deserialize(NetReader reader)
        {
            RequestId = reader.ReadVarUInt();
            Status = (RequestStatus)reader.ReadByteInRange((byte)RequestStatus.Ok, (byte)RequestStatus.ServerError);
            uint reason = reader.ReadVarUInt();
            if (reason > ushort.MaxValue)
            {
                throw new NetReadException(NetReadError.InvalidValue, "reason code out of range");
            }

            Reason = (ushort)reason;
            if (HasBody(Status))
            {
                Body = new T();
                Body.Deserialize(reader);
            }
            else
            {
                Body = default;
            }
        }
    }

    /// <summary>
    /// A request/response pair on a reliable channel (shop and draft transactions). The server answers every
    /// request id exactly once; a repeated id returns the original response bytes without re-running the
    /// handler (spec L429: duplicate purchase requests are prevented). Over-limit requests are answered with
    /// <see cref="RequestStatus.RateLimited"/> instead of being processed (spec L429: rerolls are rate-limited).
    /// </summary>
    public sealed class RequestType<TRequest, TResponse>
        where TRequest : INetMessage, new()
        where TResponse : INetMessage, new()
    {
        public RequestType(ushort requestMessageId, ushort responseMessageId, string name, NetChannel channel, RateLimit rateLimit)
        {
            if (requestMessageId == responseMessageId) throw new ArgumentException("Request and response need distinct message ids.");
            if (!NetChannels.IsReliable(channel)) throw new ArgumentException("Requests need a reliable channel.", nameof(channel));
            Name = name;
            Channel = channel;
            RateLimit = rateLimit;
            RequestMessage = new MessageType<RequestEnvelope<TRequest>>(requestMessageId, name + ".request", channel, MessageDirection.ClientToServer);
            ResponseMessage = new MessageType<ResponseEnvelope<TResponse>>(responseMessageId, name + ".response", channel, MessageDirection.ServerToClient);
        }

        public string Name { get; }

        public NetChannel Channel { get; }

        /// <summary>Per-player limit enforced by the request server (answered, not silently dropped).</summary>
        public RateLimit RateLimit { get; }

        public MessageType<RequestEnvelope<TRequest>> RequestMessage { get; }

        public MessageType<ResponseEnvelope<TResponse>> ResponseMessage { get; }

        public override string ToString() => Name;
    }

    /// <summary>What a request handler decided.</summary>
    public struct Reply<T>
    {
        private Reply(RequestStatus status, ushort reason, T body)
        {
            Status = status;
            Reason = reason;
            Body = body;
        }

        public RequestStatus Status { get; }

        public ushort Reason { get; }

        public T Body { get; }

        public static Reply<T> Ok(T body) => new Reply<T>(RequestStatus.Ok, 0, body);

        public static Reply<T> Reject(ushort reason, T body = default) => new Reply<T>(RequestStatus.Rejected, reason, body);
    }

    public delegate Reply<TResponse> RequestHandler<TSender, TRequest, TResponse>(TSender sender, TRequest request);

    /// <summary>A request handler that also receives the client-assigned request id.</summary>
    public delegate Reply<TResponse> RequestHandlerWithId<TSender, TRequest, TResponse>(TSender sender, uint requestId, TRequest request);

    /// <summary>Result delivered to the client's callback.</summary>
    public struct RequestResult<T>
    {
        public RequestResult(uint requestId, RequestStatus status, ushort reason, T body)
        {
            RequestId = requestId;
            Status = status;
            Reason = reason;
            Body = body;
        }

        public uint RequestId { get; }

        public RequestStatus Status { get; }

        public ushort Reason { get; }

        public T Body { get; }

        public bool IsOk => Status == RequestStatus.Ok;
    }
}
