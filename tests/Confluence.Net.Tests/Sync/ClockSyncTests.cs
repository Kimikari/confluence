using System;
using Confluence.Core.Random;
using Confluence.Net.Diagnostics;
using Confluence.Net.Sync;
using Confluence.Net.Tests.TestSupport;
using Confluence.Net.Transport;

namespace Confluence.Net.Tests.Sync
{
    public class ClockSyncTests
    {
        /// <summary>Server tick at server-side time t (ms), for a server whose clock started 123456 ms before the client's.</summary>
        private static double ServerTickAt(double clientMs) => (clientMs + 123456.0) * 0.06;

        [Fact]
        public void Converges_Within_A_Tick_Despite_Jitter_And_Asymmetric_Queueing()
        {
            var rng = new Rng(5);
            var sync = new ClockSync();
            long now = 1000;
            for (int i = 0; i < 40; i++)
            {
                // 30 ms base one-way delay each direction plus up to 40 ms of queueing on either leg.
                double up = 30 + rng.NextInt(41);
                double down = 30 + rng.NextInt(41);
                double serverTick = ServerTickAt(now + up);
                long receive = now + (long)(up + down);
                Assert.True(sync.AddSample(now, receive, serverTick));
                now = receive + 100;
            }

            double error = sync.EstimateServerTick(now) - ServerTickAt(now);
            Assert.True(Math.Abs(error) < 1.0, "error " + error + " ticks");
            Assert.InRange(sync.SmoothedRttMs, 60, 140);
            Assert.InRange(sync.MinRttMs, 60, 80);
            Assert.True(sync.RecommendedInputLeadTicks >= 3);
        }

        [Fact]
        public void Large_Errors_Snap_And_Small_Ones_Ease()
        {
            var sync = new ClockSync();
            sync.Seed(1000, 0, 0); // wildly wrong coarse seed
            Assert.True(sync.HasEstimate);
            sync.AddSample(0, 20, ServerTickAt(10));
            Assert.Equal(ServerTickAt(20), sync.EstimateServerTick(20), 6); // first probe replaces the seed

            // A slightly faster probe moves the estimate only part of the way.
            double before = sync.OffsetTicks;
            sync.AddSample(100, 110, ServerTickAt(105) + 0.4);
            double moved = sync.OffsetTicks - before;
            Assert.InRange(moved, 0.05, 0.2); // a quarter of 0.4 ticks

            // A server-side step of five seconds snaps.
            sync.AddSample(200, 205, ServerTickAt(202.5) + 300);
            Assert.Equal(ServerTickAt(205) + 300, sync.EstimateServerTick(205), 3);
        }

        [Fact]
        public void Impossible_Samples_Are_Ignored()
        {
            var sync = new ClockSync();
            Assert.False(sync.AddSample(100, 90, 5));
            Assert.False(sync.AddSample(100, 120, double.NaN));
            Assert.False(sync.HasEstimate);
            Assert.Equal(0, sync.SampleCount);
        }

        [Fact]
        public void Interpolation_Tick_Is_The_Render_Delay_Behind()
        {
            var sync = new ClockSync();
            sync.AddSample(0, 0, 600);
            Assert.Equal(600 - 6, sync.InterpolationTick(0), 6); // 100 ms = 6 ticks
        }

        [Fact]
        public void Client_Estimate_Tracks_The_Server_Tick_Over_A_Delayed_Link()
        {
            NetworkConditions conditions = NetworkConditions.Symmetric(latencyMs: 25, jitterMs: 6, lossChance: 0.05, seed: 21);
            using (var rig = new LoopbackRig(conditions: conditions, configure: s => s.TickClock = new ShiftedTickClock(s.Clock, 5000)))
            {
                ClientTransport client = rig.Connect("Bram");
                Assert.True(rig.PumpUntil(() => client.Clock.SampleCount >= 8, 5000));

                double error = client.Clock.EstimateServerTick(client.NetClock.NowMs) - rig.Server.TickClock.CurrentTick;
                Assert.True(Math.Abs(error) < 2.0, "error " + error + " ticks");
                Assert.InRange(client.Clock.MinRttMs, 45, 120);
            }
        }

        private sealed class ShiftedTickClock : ITickClock
        {
            private readonly INetClock _clock;
            private readonly long _start;
            private readonly double _shift;

            public ShiftedTickClock(INetClock clock, double shiftTicks)
            {
                _clock = clock;
                _start = clock.NowMs;
                _shift = shiftTicks;
            }

            public double CurrentTick => _shift + (_clock.NowMs - _start) * 0.06;
        }
    }
}
