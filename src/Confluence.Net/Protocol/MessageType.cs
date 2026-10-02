using System;
using Confluence.Net.RateLimiting;
using Confluence.Net.Serialization;

namespace Confluence.Net.Protocol
{
    /// <summary>A message body that can write itself to and read itself from the wire.</summary>
    public interface INetMessage
    {
        void Serialize(NetWriter writer);

        /// <summary>Reads the body from untrusted bytes; throws <see cref="NetReadException"/> on any violation.</summary>
        void Deserialize(NetReader reader);
    }

    /// <summary>Who may send a message type.</summary>
    public enum MessageDirection : byte
    {
        ClientToServer,
        ServerToClient,
        Both,
    }

    /// <summary>
    /// Shared, immutable description of a message type: wire id, name, channel, direction, and an optional
    /// per-sender rate limit applied by the receiving server. Client and server build the same definitions
    /// and register different handlers for them.
    /// </summary>
    public abstract class MessageType
    {
        /// <summary>Ids below this are reserved for transport and protocol messages.</summary>
        public const ushort FirstGameMessageId = 64;

        protected MessageType(ushort id, string name, NetChannel channel, MessageDirection direction, RateLimit rateLimit)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("A message type needs a name.", nameof(name));
            Id = id;
            Name = name;
            Channel = channel;
            Direction = direction;
            RateLimit = rateLimit;
        }

        public ushort Id { get; }

        public string Name { get; }

        public NetChannel Channel { get; }

        public MessageDirection Direction { get; }

        /// <summary>Applied per sender on the server; messages over the limit are dropped and counted.</summary>
        public RateLimit RateLimit { get; }

        public abstract Type BodyType { get; }

        /// <summary>True when an endpoint of the given role may receive this message.</summary>
        public bool CanBeReceivedBy(bool isServer)
        {
            switch (Direction)
            {
                case MessageDirection.ClientToServer: return isServer;
                case MessageDirection.ServerToClient: return !isServer;
                default: return true;
            }
        }

        public override string ToString() => Name + "#" + Id;
    }

    /// <summary>Typed message definition; bodies are created fresh for each received message.</summary>
    public sealed class MessageType<T> : MessageType
        where T : INetMessage, new()
    {
        public MessageType(ushort id, string name, NetChannel channel, MessageDirection direction)
            : this(id, name, channel, direction, RateLimit.Unlimited)
        {
        }

        public MessageType(ushort id, string name, NetChannel channel, MessageDirection direction, RateLimit rateLimit)
            : base(id, name, channel, direction, rateLimit)
        {
        }

        public override Type BodyType => typeof(T);

        public T Create() => new T();
    }
}
