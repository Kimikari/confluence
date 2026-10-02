using Confluence.Core.Geometry;
using Confluence.Net.Protocol;
using Confluence.Net.RateLimiting;
using Confluence.Net.Requests;
using Confluence.Net.Serialization;
using Confluence.Net.Sync;

namespace Confluence.Net.Tests.TestSupport
{
    /// <summary>Text on the events lane, in both directions.</summary>
    public sealed class TextMessage : INetMessage
    {
        public string Text { get; set; } = "";

        public void Serialize(NetWriter writer) => writer.WriteString(Text);

        public void Deserialize(NetReader reader) => Text = reader.ReadString();
    }

    /// <summary>Sequence number on a configurable lane (ordering tests).</summary>
    public sealed class CounterMessage : INetMessage
    {
        public int Value { get; set; }

        public void Serialize(NetWriter writer) => writer.WriteVarInt(Value);

        public void Deserialize(NetReader reader) => Value = reader.ReadVarInt();
    }

    /// <summary>A float-carrying message (non-finite values must be rejected).</summary>
    public sealed class AimMessage : INetMessage
    {
        public float Yaw { get; set; }

        public void Serialize(NetWriter writer) => writer.WriteFloat(Yaw);

        public void Deserialize(NetReader reader) => Yaw = reader.ReadFloat();
    }

    /// <summary>World snapshot of one entity for the snapshot-lane test.</summary>
    public sealed class PositionSnapshot : INetMessage
    {
        public SnapshotHeader Header { get; set; }

        public Vec3 Position { get; set; }

        public void Serialize(NetWriter writer)
        {
            Header.Write(writer);
            writer.WritePosition(Position);
        }

        public void Deserialize(NetReader reader)
        {
            Header = SnapshotHeader.Read(reader);
            Position = reader.ReadPosition();
        }
    }

    public sealed class PurchaseRequest : INetMessage
    {
        public string ItemId { get; set; } = "";

        public void Serialize(NetWriter writer) => writer.WriteString(ItemId, 32);

        public void Deserialize(NetReader reader) => ItemId = reader.ReadString(32);
    }

    public sealed class PurchaseResponse : INetMessage
    {
        public string ItemId { get; set; } = "";

        public int Balance { get; set; }

        public void Serialize(NetWriter writer)
        {
            writer.WriteString(ItemId, 32);
            writer.WriteVarInt(Balance);
        }

        public void Deserialize(NetReader reader)
        {
            ItemId = reader.ReadString(32);
            Balance = reader.ReadVarInt();
        }
    }

    /// <summary>Movement input with a delta codec, standing in for the Stage 3 input command.</summary>
    public struct TestInput
    {
        public TestInput(int moveX, int moveZ, bool jump)
        {
            MoveX = moveX;
            MoveZ = moveZ;
            Jump = jump;
        }

        public int MoveX { get; }

        public int MoveZ { get; }

        public bool Jump { get; }
    }

    public sealed class TestInputCodec : IInputCodec<TestInput>
    {
        public void Write(NetWriter writer, TestInput value, TestInput previous)
        {
            writer.WriteIntDelta(value.MoveX, previous.MoveX);
            writer.WriteIntDelta(value.MoveZ, previous.MoveZ);
            writer.WriteBool(value.Jump);
        }

        public TestInput Read(NetReader reader, TestInput previous)
        {
            int x = reader.ReadIntDelta(previous.MoveX);
            int z = reader.ReadIntDelta(previous.MoveZ);
            return new TestInput(x, z, reader.ReadBool());
        }
    }

    public static class TestMessages
    {
        public static readonly MessageType<TextMessage> Text =
            new MessageType<TextMessage>(100, "test.text", NetChannel.Events, MessageDirection.Both);

        public static readonly MessageType<CounterMessage> EventCounter =
            new MessageType<CounterMessage>(101, "test.event_counter", NetChannel.Events, MessageDirection.Both);

        public static readonly MessageType<CounterMessage> MatchCounter =
            new MessageType<CounterMessage>(102, "test.match_counter", NetChannel.Match, MessageDirection.ClientToServer);

        public static readonly MessageType<CounterMessage> BulkCounter =
            new MessageType<CounterMessage>(103, "test.bulk_counter", NetChannel.Bulk, MessageDirection.ServerToClient);

        /// <summary>Chat-like message: four per minute (no refill during a test).</summary>
        public static readonly MessageType<TextMessage> Chat =
            new MessageType<TextMessage>(104, "test.chat", NetChannel.Match, MessageDirection.ClientToServer, new RateLimit(4, 60000));

        public static readonly MessageType<AimMessage> Aim =
            new MessageType<AimMessage>(105, "test.aim", NetChannel.Events, MessageDirection.ClientToServer);

        public static readonly MessageType<PositionSnapshot> Snapshot =
            new MessageType<PositionSnapshot>(106, "test.snapshot", NetChannel.Snapshot, MessageDirection.ServerToClient);

        public static readonly MessageType<InputPacketMessage<TestInput, TestInputCodec>> Inputs =
            new MessageType<InputPacketMessage<TestInput, TestInputCodec>>(107, "test.inputs", NetChannel.Input, MessageDirection.ClientToServer);

        /// <summary>Purchases: generous limit, used for duplicate detection.</summary>
        public static readonly RequestType<PurchaseRequest, PurchaseResponse> Purchase =
            new RequestType<PurchaseRequest, PurchaseResponse>(110, 111, "test.purchase", NetChannel.Shop, NetRules.Transactions);

        /// <summary>Rerolls: three per minute, so no token refills during a test.</summary>
        public static readonly RequestType<PurchaseRequest, PurchaseResponse> Reroll =
            new RequestType<PurchaseRequest, PurchaseResponse>(112, 113, "test.reroll", NetChannel.Draft, new RateLimit(3, 60000));
    }
}
