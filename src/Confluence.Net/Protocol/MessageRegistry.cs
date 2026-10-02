using System;
using System.Collections.Generic;
using Confluence.Net.Serialization;

namespace Confluence.Net.Protocol
{
    public delegate void MessageHandler<TSender, T>(TSender sender, T message);

    /// <summary>What happened to one received message.</summary>
    public enum DispatchResult : byte
    {
        Handled,

        /// <summary>No handler is registered for the message id on this endpoint.</summary>
        UnknownMessage,

        /// <summary>The message type may not be sent by this peer's role.</summary>
        WrongDirection,

        /// <summary>The message arrived on a lane or delivery method other than its declared channel.</summary>
        WrongChannel,

        /// <summary>The per-sender rate limit rejected it.</summary>
        RateLimited,

        /// <summary>Truncated, oversized, out-of-range, or trailing bytes.</summary>
        Malformed,

        /// <summary>The message decoded but its handler threw.</summary>
        HandlerFault,
    }

    /// <summary>Result of <see cref="MessageRegistry{TSender}.Dispatch"/>.</summary>
    public struct DispatchOutcome
    {
        public DispatchOutcome(DispatchResult result, uint messageId, MessageType type, Exception error)
        {
            Result = result;
            MessageId = messageId;
            Type = type;
            Error = error;
        }

        public DispatchResult Result { get; }

        public uint MessageId { get; }

        /// <summary>Resolved type, or null when the id was unknown or unreadable.</summary>
        public MessageType Type { get; }

        public Exception Error { get; }

        /// <summary>True for outcomes caused by the remote peer's bytes (counted toward a kick).</summary>
        public bool IsViolation =>
            Result == DispatchResult.UnknownMessage
            || Result == DispatchResult.WrongDirection
            || Result == DispatchResult.WrongChannel
            || Result == DispatchResult.Malformed;
    }

    /// <summary>Wire framing: every packet is one message, a varint type id followed by the body.</summary>
    public static class MessageFraming
    {
        public static void Write(NetWriter writer, MessageType type, INetMessage body)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (body.GetType() != type.BodyType)
            {
                throw new ArgumentException("Body " + body.GetType().Name + " does not match " + type + ".", nameof(body));
            }

            writer.WriteVarUInt(type.Id);
            body.Serialize(writer);
        }
    }

    /// <summary>
    /// Message id to handler table for one endpoint. Dispatch decodes into a fresh body, requires the bytes to
    /// be consumed exactly, and only then runs the handler, so handlers never see partial or padded messages.
    /// Malformed input and handler exceptions are reported in the outcome rather than thrown.
    /// </summary>
    public sealed class MessageRegistry<TSender>
    {
        private readonly bool _isServer;
        private readonly Dictionary<ushort, Entry> _entries = new Dictionary<ushort, Entry>();

        public MessageRegistry(bool isServer)
        {
            _isServer = isServer;
        }

        /// <summary>True on the server side (affects which directions are accepted).</summary>
        public bool IsServer => _isServer;

        /// <summary>Registers (or replaces) the handler of <paramref name="type"/>.</summary>
        public void On<T>(MessageType<T> type, MessageHandler<TSender, T> handler)
            where T : INetMessage, new()
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (!type.CanBeReceivedBy(_isServer))
            {
                throw new ArgumentException(type + " is never received by the " + (_isServer ? "server" : "client") + ".", nameof(type));
            }

            if (_entries.TryGetValue(type.Id, out Entry existing) && !ReferenceEquals(existing.Type, type))
            {
                throw new ArgumentException("Message id " + type.Id + " is already used by " + existing.Type + ".", nameof(type));
            }

            _entries[type.Id] = new Entry<T>(type, handler);
        }

        public bool Remove(MessageType type) => type != null && _entries.Remove(type.Id);

        public bool TryGetType(ushort id, out MessageType type)
        {
            if (_entries.TryGetValue(id, out Entry entry))
            {
                type = entry.Type;
                return true;
            }

            type = null;
            return false;
        }

        /// <summary>
        /// Decodes and handles one message. <paramref name="admit"/>, when given, is consulted after the type is
        /// known and before decoding (rate limiting); returning false drops the message as rate limited.
        /// </summary>
        public DispatchOutcome Dispatch(TSender sender, NetReader reader, NetChannel arrivedOn, Func<TSender, MessageType, bool> admit = null)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            uint id;
            try
            {
                id = reader.ReadVarUInt();
            }
            catch (NetReadException e)
            {
                return new DispatchOutcome(DispatchResult.Malformed, 0, null, e);
            }

            if (id > ushort.MaxValue || !_entries.TryGetValue((ushort)id, out Entry entry))
            {
                return new DispatchOutcome(DispatchResult.UnknownMessage, id, null, null);
            }

            MessageType type = entry.Type;
            if (!type.CanBeReceivedBy(_isServer))
            {
                return new DispatchOutcome(DispatchResult.WrongDirection, id, type, null);
            }

            if (type.Channel != arrivedOn)
            {
                return new DispatchOutcome(DispatchResult.WrongChannel, id, type, null);
            }

            if (admit != null && !admit(sender, type))
            {
                return new DispatchOutcome(DispatchResult.RateLimited, id, type, null);
            }

            INetMessage body;
            try
            {
                body = entry.Decode(reader);
            }
            catch (NetReadException e)
            {
                return new DispatchOutcome(DispatchResult.Malformed, id, type, e);
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                // A body's Deserialize may throw its own validation exceptions; any of them means bad bytes.
                return new DispatchOutcome(DispatchResult.Malformed, id, type, e);
            }

            try
            {
                entry.Invoke(sender, body);
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                return new DispatchOutcome(DispatchResult.HandlerFault, id, type, e);
            }

            return new DispatchOutcome(DispatchResult.Handled, id, type, null);
        }

        private abstract class Entry
        {
            protected Entry(MessageType type)
            {
                Type = type;
            }

            public MessageType Type { get; }

            public abstract INetMessage Decode(NetReader reader);

            public abstract void Invoke(TSender sender, INetMessage body);
        }

        private sealed class Entry<T> : Entry
            where T : INetMessage, new()
        {
            private readonly MessageHandler<TSender, T> _handler;

            public Entry(MessageType<T> type, MessageHandler<TSender, T> handler)
                : base(type)
            {
                _handler = handler;
            }

            public override INetMessage Decode(NetReader reader)
            {
                var body = new T();
                body.Deserialize(reader);
                reader.ExpectEnd();
                return body;
            }

            public override void Invoke(TSender sender, INetMessage body) => _handler(sender, (T)body);
        }
    }
}
