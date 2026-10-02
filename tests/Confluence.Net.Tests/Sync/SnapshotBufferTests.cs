using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Net.Serialization;
using Confluence.Net.Sync;

namespace Confluence.Net.Tests.Sync
{
    public class SnapshotBufferTests
    {
        private static Vec3 At(int tick) => new Vec3(tick, 0f, -2f * tick);

        [Fact]
        public void Samples_Interpolate_Between_Bracketing_Snapshots()
        {
            var buffer = new SnapshotBuffer<Vec3>();
            buffer.Add(10, At(10));
            buffer.Add(12, At(12));
            buffer.Add(15, At(15)); // a 20 Hz gap after a 30 Hz one

            SnapshotSample<Vec3> mid = buffer.Sample(11.0);
            Assert.Equal(SampleKind.Interpolated, mid.Kind);
            Assert.Equal(10, mid.FromTick);
            Assert.Equal(12, mid.ToTick);
            Assert.Equal(0.5f, mid.Alpha, 5);

            Assert.True(buffer.TrySample(14.0, Vec3.Lerp, out Vec3 v));
            Assert.Equal(14f, v.X, 4);
            Assert.Equal(-28f, v.Z, 4);

            Assert.True(buffer.TrySample(12.0, Vec3.Lerp, out Vec3 exact));
            Assert.Equal(12f, exact.X, 5);
        }

        [Fact]
        public void Out_Of_Range_Render_Times_Hold_The_Nearest_Snapshot()
        {
            var buffer = new SnapshotBuffer<Vec3>();
            Assert.Equal(SampleKind.Empty, buffer.Sample(5).Kind);
            Assert.False(buffer.TrySample(5, Vec3.Lerp, out _));

            buffer.Add(10, At(10));
            buffer.Add(12, At(12));
            Assert.Equal(SampleKind.BeforeOldest, buffer.Sample(8).Kind);
            SnapshotSample<Vec3> starved = buffer.Sample(13.5);
            Assert.Equal(SampleKind.Starved, starved.Kind);
            Assert.Equal(12, starved.FromTick);
            Assert.True(buffer.TrySample(13.5, Vec3.Lerp, out Vec3 held));
            Assert.Equal(12f, held.X);
            Assert.Equal(SampleKind.Interpolated, buffer.Sample(12).Kind);
        }

        [Fact]
        public void Out_Of_Order_Duplicate_And_Stale_Snapshots_Are_Handled()
        {
            var buffer = new SnapshotBuffer<Vec3>(capacity: 4);
            Assert.True(buffer.Add(20, At(20)));
            Assert.True(buffer.Add(16, At(16)));
            Assert.True(buffer.Add(18, At(18)));
            Assert.False(buffer.Add(18, At(18)));
            Assert.True(buffer.Add(22, At(22)));
            Assert.Equal(new[] { 16, 22 }, new[] { buffer.OldestTick, buffer.NewestTick });

            Assert.True(buffer.Add(24, At(24))); // evicts 16
            Assert.Equal(18, buffer.OldestTick);
            Assert.False(buffer.Add(14, At(14))); // older than a full buffer
            Assert.Equal(1, buffer.Duplicates);
            Assert.Equal(1, buffer.TooOld);
            Assert.True(buffer.TryGet(20, out Vec3 v20));
            Assert.Equal(20f, v20.X);

            Assert.Equal(19f, buffer.Sample(19).FromTick + buffer.Sample(19).Alpha * 2f, 4);
        }

        [Fact]
        public void Discard_Keeps_The_Span_Start_For_The_Render_Time()
        {
            var buffer = new SnapshotBuffer<Vec3>();
            for (int t = 0; t <= 20; t += 2) buffer.Add(t, At(t));
            buffer.DiscardBefore(9);
            Assert.Equal(8, buffer.OldestTick);
            Assert.Equal(SampleKind.Interpolated, buffer.Sample(9).Kind);
        }

        [Fact]
        public void Header_Round_Trips_Ack_And_Baseline()
        {
            var writer = new NetWriter();
            new SnapshotHeader(1200, 1185, 1196).Write(writer);
            new SnapshotHeader(7, 0).Write(writer);
            var reader = new NetReader(writer.ToArray());
            SnapshotHeader delta = SnapshotHeader.Read(reader);
            SnapshotHeader full = SnapshotHeader.Read(reader);
            Assert.Equal(1200, delta.ServerTick);
            Assert.Equal(1185u, delta.LastProcessedInput);
            Assert.Equal(1196, delta.BaselineTick);
            Assert.False(full.HasBaseline);

            writer.Reset();
            writer.WriteVarUInt(5);
            writer.WriteVarUInt(0);
            writer.WriteVarUInt(9); // baseline 9 ticks before tick 5
            Assert.Throws<NetReadException>(() => SnapshotHeader.Read(new NetReader(writer.ToArray())));
        }

        [Fact]
        public void Rate_Schedule_Sends_Thirty_Or_Twenty_Per_Second()
        {
            int at30 = 0;
            int at20 = 0;
            var gaps20 = new HashSet<int>();
            int last20 = -1;
            for (int tick = 1; tick <= 600; tick++)
            {
                if (SnapshotRateSelector.ShouldSend(tick, 30)) at30++;
                if (SnapshotRateSelector.ShouldSend(tick, 20))
                {
                    at20++;
                    if (last20 >= 0) gaps20.Add(tick - last20);
                    last20 = tick;
                }
            }

            Assert.Equal(300, at30);
            Assert.Equal(200, at20);
            Assert.Equal(new HashSet<int> { 3 }, gaps20);
            Assert.True(SnapshotRateSelector.ShouldSend(0, 30));
        }
    }
}
