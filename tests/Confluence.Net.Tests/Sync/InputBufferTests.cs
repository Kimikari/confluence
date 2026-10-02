using System.Collections.Generic;
using Confluence.Net.Serialization;
using Confluence.Net.Sync;
using Confluence.Net.Tests.TestSupport;

namespace Confluence.Net.Tests.Sync
{
    public class InputBufferTests
    {
        private static readonly TestInputCodec Codec = new TestInputCodec();

        private static TestInput InputFor(uint seq) => new TestInput((int)(seq % 9) - 4, (int)(seq % 3), seq % 4 == 0);

        /// <summary>Encodes the packet the client would send after pushing <paramref name="seq"/>.</summary>
        private static byte[] PacketAfterPush(InputSendBuffer<TestInput> send, uint seq)
        {
            Assert.Equal(seq, send.Push(InputFor(seq)));
            var writer = new NetWriter();
            send.WritePacket(writer, Codec);
            return writer.ToArray();
        }

        [Fact]
        public void Packets_Carry_The_Newest_Input_And_The_Redundant_Tail()
        {
            var send = new InputSendBuffer<TestInput>(redundancy: 3);
            for (uint s = 1; s <= 6; s++) send.Push(InputFor(s));
            var commands = new List<InputCommand<TestInput>>();
            send.FillPacket(commands);
            Assert.Equal(new uint[] { 3, 4, 5, 6 }, commands.ConvertAll(c => c.Sequence));

            var writer = new NetWriter();
            send.WritePacket(writer, Codec);
            var decoded = new List<InputCommand<TestInput>>();
            InputPacket.Read(new NetReader(writer.ToArray()), Codec, decoded);
            Assert.Equal(new uint[] { 3, 4, 5, 6 }, decoded.ConvertAll(c => c.Sequence));
            Assert.Equal(InputFor(5).MoveX, decoded[2].Input.MoveX);
            Assert.Equal(InputFor(4).Jump, decoded[1].Input.Jump);

            send.Acknowledge(5);
            send.FillPacket(commands);
            Assert.Equal(new uint[] { 6 }, commands.ConvertAll(c => c.Sequence));
            Assert.Equal(5u, send.AckedSequence);
            send.Acknowledge(2); // stale ack ignored
            Assert.Equal(5u, send.AckedSequence);
        }

        [Fact]
        public void Redundancy_Recovers_Bursts_Shorter_Than_The_Redundant_Tail()
        {
            var send = new InputSendBuffer<TestInput>(redundancy: 4);
            var receive = new InputReceiveBuffer<TestInput>(maxBufferedTicks: 1000);
            // Drop packets in bursts of up to four (3 of 4, then 4 of 5) - always recoverable with redundancy 4.
            var dropped = new HashSet<uint> { 2, 3, 4, 10, 11, 12, 13, 20, 30, 31, 32, 33 };
            for (uint seq = 1; seq <= 40; seq++)
            {
                byte[] packet = PacketAfterPush(send, seq);
                if (!dropped.Contains(seq))
                {
                    receive.ReadPacket(new NetReader(packet), Codec);
                }
            }

            for (uint seq = 1; seq <= 40; seq++)
            {
                ConsumedInput<TestInput> c = receive.ConsumeNext();
                Assert.Equal(InputSource.Received, c.Source);
                Assert.Equal(seq, c.Sequence);
                Assert.Equal(InputFor(seq).MoveX, c.Input.MoveX);
            }

            Assert.Equal(40u, receive.LastProcessedSequence);
            Assert.True(receive.Duplicates > 0);
        }

        [Fact]
        public void Without_Redundancy_The_Same_Losses_Become_Substitutions()
        {
            var send = new InputSendBuffer<TestInput>(redundancy: 0);
            var receive = new InputReceiveBuffer<TestInput>(maxBufferedTicks: 1000);
            for (uint seq = 1; seq <= 10; seq++)
            {
                byte[] packet = PacketAfterPush(send, seq);
                if (seq != 4 && seq != 5)
                {
                    receive.ReadPacket(new NetReader(packet), Codec);
                }
            }

            var sources = new List<InputSource>();
            for (int i = 0; i < 10; i++) sources.Add(receive.ConsumeNext().Source);
            Assert.Equal(InputSource.Lost, sources[3]);
            Assert.Equal(InputSource.Lost, sources[4]);
            Assert.Equal(2, receive.LostSubstituted);
        }

        [Fact]
        public void Reordering_Within_The_Redundant_Tail_Loses_Nothing()
        {
            var send = new InputSendBuffer<TestInput>(redundancy: 4);
            var packets = new List<byte[]>();
            for (uint seq = 1; seq <= 12; seq++) packets.Add(PacketAfterPush(send, seq));

            // A sequenced lane drops packets overtaken by newer ones; simulate packet 9 overtaking 6, 7, 8.
            var receive = new InputReceiveBuffer<TestInput>(maxBufferedTicks: 1000);
            int[] arrival = { 0, 1, 2, 3, 4, 8, 9, 10, 11 }; // packets 6-8 (indices 5-7) dropped as stale
            foreach (int i in arrival) receive.ReadPacket(new NetReader(packets[i]), Codec);
            for (uint seq = 1; seq <= 12; seq++)
            {
                Assert.Equal(InputSource.Received, receive.ConsumeNext().Source);
            }
        }

        [Fact]
        public void Starvation_Repeats_The_Last_Input_Then_Goes_Neutral_Without_Losing_Late_Inputs()
        {
            var neutral = new TestInput(0, 0, false);
            var receive = new InputReceiveBuffer<TestInput>(neutral, maxRepeatTicks: 2);
            Assert.Equal(InputSource.None, receive.ConsumeNext().Source);

            receive.Accept(1, new TestInput(1, 1, true));
            Assert.Equal(InputSource.Received, receive.ConsumeNext().Source);

            ConsumedInput<TestInput> r1 = receive.ConsumeNext();
            ConsumedInput<TestInput> r2 = receive.ConsumeNext();
            ConsumedInput<TestInput> n1 = receive.ConsumeNext();
            Assert.Equal(InputSource.Repeated, r1.Source);
            Assert.Equal(1, r1.Input.MoveX);
            Assert.Equal(0u, r1.Sequence);
            Assert.Equal(InputSource.Repeated, r2.Source);
            Assert.Equal(InputSource.Neutral, n1.Source);
            Assert.Equal(0, n1.Input.MoveX);
            Assert.Equal(1u, receive.LastProcessedSequence);

            // The late input is still used: the stream gained buffering, not a gap.
            receive.Accept(2, new TestInput(2, 2, false));
            ConsumedInput<TestInput> late = receive.ConsumeNext();
            Assert.Equal(InputSource.Received, late.Source);
            Assert.Equal(2u, late.Sequence);
            Assert.Equal(2, receive.RepeatedTicks);
            Assert.Equal(1, receive.NeutralTicks);
        }

        [Fact]
        public void Duplicates_And_Consumed_Sequences_Are_Rejected()
        {
            var receive = new InputReceiveBuffer<TestInput>();
            Assert.True(receive.Accept(1, InputFor(1)));
            Assert.False(receive.Accept(1, InputFor(1)));
            Assert.False(receive.Accept(0, InputFor(0)));
            receive.ConsumeNext();
            Assert.False(receive.Accept(1, InputFor(1)));
            Assert.Equal(1, receive.Duplicates);
            Assert.Equal(1, receive.Late);
        }

        [Fact]
        public void A_Client_Running_Ahead_Cannot_Bank_More_Than_The_Buffer_Bound()
        {
            var receive = new InputReceiveBuffer<TestInput>(maxBufferedTicks: 4);
            for (uint seq = 1; seq <= 10; seq++) receive.Accept(seq, InputFor(seq));
            ConsumedInput<TestInput> first = receive.ConsumeNext();
            Assert.Equal(7u, first.Sequence); // 7..10 is the newest four
            Assert.Equal(6, receive.Skipped);
            Assert.Equal(3, receive.BufferedCount);
        }

        [Fact]
        public void A_Forged_Far_Future_Sequence_Costs_Nothing_And_Stays_Bounded()
        {
            // Skipping must not walk every sequence between the cursor and a forged newest one (a hang otherwise).
            var receive = new InputReceiveBuffer<TestInput>();
            for (uint seq = 1; seq <= 5; seq++) receive.Accept(seq, InputFor(seq));
            Assert.True(receive.Accept(4_000_000_000u, InputFor(9)));
            Assert.Equal(1, receive.BufferedCount);
            Assert.Equal(4_000_000_000L - NetRules.InputMaxBufferedTicks, receive.Skipped);
            Assert.False(receive.Accept(6, InputFor(6))); // now far behind the cursor
            Assert.True(receive.BufferedCount <= NetRules.InputMaxBufferedTicks);

            // The stream continues at the newest window: lost sequences stand in with the last known input.
            ConsumedInput<TestInput> next = receive.ConsumeNext();
            Assert.Equal(InputSource.Lost, next.Source);
            Assert.Equal(4_000_000_000u - (uint)NetRules.InputMaxBufferedTicks + 1, next.Sequence);
            Assert.Equal(InputFor(5).MoveX, next.Input.MoveX);
        }

        [Fact]
        public void Malformed_Input_Packets_Are_Rejected()
        {
            var writer = new NetWriter();
            writer.WriteVarUInt(5);
            writer.WriteVarUInt(NetRules.MaxInputsPerPacket + 1);
            Assert.Throws<NetReadException>(() => InputPacket.Read(new NetReader(writer.ToArray()), Codec, new List<InputCommand<TestInput>>()));

            writer.Reset();
            writer.WriteVarUInt(2); // newest 2 cannot carry 3 commands
            writer.WriteVarUInt(3);
            writer.WriteBytes(new byte[9]);
            Assert.Throws<NetReadException>(() => InputPacket.Read(new NetReader(writer.ToArray()), Codec, new List<InputCommand<TestInput>>()));

            writer.Reset();
            writer.WriteVarUInt(1);
            writer.WriteVarUInt(0);
            Assert.Throws<NetReadException>(() => InputPacket.Read(new NetReader(writer.ToArray()), Codec, new List<InputCommand<TestInput>>()));
        }

        [Fact]
        public void Reconciliation_Replays_Only_Unacknowledged_Inputs()
        {
            var send = new InputSendBuffer<TestInput>();
            for (uint seq = 1; seq <= 5; seq++) send.Push(new TestInput(1, 0, false));

            // Authoritative X after the server processed inputs 1-3 is 30 (it moved 10 per input); replay 4 and 5.
            int predicted = Reconciliation.Replay(30, 3, send, (x, input) => x + 10 * input.MoveX);
            Assert.Equal(50, predicted);
            Assert.Equal(2, send.UnacknowledgedCount);
            Assert.Equal(3u, send.AckedSequence);
        }
    }
}
