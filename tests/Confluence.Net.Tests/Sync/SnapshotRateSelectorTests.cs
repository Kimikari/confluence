using System;
using Confluence.Core.Rules;
using Confluence.Net.Sync;
using Confluence.Net.Transport;

namespace Confluence.Net.Tests.Sync
{
    public sealed class SnapshotRateSelectorTests
    {
        private static LinkStats Clear(int rtt = 40) => new LinkStats { RttMs = rtt };

        [Fact]
        public void Starts_At_Thirty_Hertz_Within_The_Spec_Range()
        {
            var selector = new SnapshotRateSelector();
            Assert.Equal(GlobalRules.Network.DefaultSnapshotRate, selector.CurrentRate);
            Assert.Equal(30, selector.HighRate);
            Assert.Equal(20, selector.LowRate);
            Assert.Throws<ArgumentOutOfRangeException>(() => new SnapshotRateSelector(40, 20));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SnapshotRateSelector(30, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SnapshotRateSelector(20, 30));
        }

        [Fact]
        public void Sustained_Loss_Drops_To_Twenty_Hertz_After_The_Downgrade_Delay()
        {
            var selector = new SnapshotRateSelector();
            var lossy = new LinkStats { RttMs = 40, LossFraction = 0.10f };
            for (long t = 0; t < NetRules.CongestionDowngradeMs; t += 50)
            {
                selector.Update(t, lossy);
                Assert.True(selector.IsCongested);
                Assert.Equal(30, selector.CurrentRate);
            }

            selector.Update(NetRules.CongestionDowngradeMs, lossy);
            Assert.Equal(20, selector.CurrentRate);
            Assert.Equal(1, selector.Downgrades);
        }

        [Fact]
        public void Brief_Spikes_Do_Not_Change_The_Rate()
        {
            var selector = new SnapshotRateSelector();
            for (long t = 0; t < 10_000; t += 50)
            {
                bool spike = (t / 50) % 8 == 0; // one bad sample every 400 ms
                selector.Update(t, spike ? new LinkStats { RttMs = 40, LossFraction = 0.5f } : Clear());
            }

            Assert.Equal(30, selector.CurrentRate);
            Assert.Equal(0, selector.Downgrades);
        }

        [Fact]
        public void Recovery_Requires_A_Clear_Link_For_The_Recovery_Delay()
        {
            var selector = new SnapshotRateSelector();
            var backlog = new LinkStats { RttMs = 40, ReliableQueued = NetRules.CongestedReliableQueue };
            selector.Update(0, backlog);
            selector.Update(NetRules.CongestionDowngradeMs, backlog);
            Assert.Equal(20, selector.CurrentRate);

            long clearFrom = NetRules.CongestionDowngradeMs + 50;
            selector.Update(clearFrom, Clear());
            selector.Update(clearFrom + NetRules.CongestionRecoveryMs - 1, Clear());
            Assert.Equal(20, selector.CurrentRate);

            // A relapse restarts the recovery timer.
            selector.Update(clearFrom + NetRules.CongestionRecoveryMs, backlog);
            selector.Update(clearFrom + NetRules.CongestionRecoveryMs + 50, Clear());
            selector.Update(clearFrom + 2 * NetRules.CongestionRecoveryMs, Clear());
            Assert.Equal(20, selector.CurrentRate);
            selector.Update(clearFrom + 2 * NetRules.CongestionRecoveryMs + 50, Clear());
            Assert.Equal(30, selector.CurrentRate);
            Assert.Equal(1, selector.Upgrades);
        }

        [Fact]
        public void Queueing_Delay_Over_The_Rtt_Floor_Counts_As_Congestion()
        {
            var selector = new SnapshotRateSelector();
            selector.Update(0, Clear(30));
            Assert.Equal(30, selector.RttFloorMs);

            // A high but stable ping is not congestion; only inflation over the floor is.
            selector.Update(100, Clear(30 + NetRules.CongestedRttInflationMs - 1));
            Assert.False(selector.IsCongested);
            selector.Update(200, Clear(30 + NetRules.CongestedRttInflationMs));
            Assert.True(selector.IsCongested);

            var highPing = new SnapshotRateSelector();
            for (long t = 0; t < 5000; t += 100)
            {
                highPing.Update(t, Clear(180));
            }

            Assert.False(highPing.IsCongested);
            Assert.Equal(30, highPing.CurrentRate);
        }

        [Fact]
        public void Rtt_Floor_Forgets_Samples_Older_Than_Ten_Seconds()
        {
            var selector = new SnapshotRateSelector();
            selector.Update(0, Clear(20));
            for (long t = 1000; t <= 12_000; t += 500)
            {
                selector.Update(t, Clear(60));
            }

            Assert.Equal(60, selector.RttFloorMs);
            Assert.False(selector.IsCongested);
        }

        [Fact]
        public void Send_Schedule_Follows_The_Current_Rate()
        {
            var selector = new SnapshotRateSelector();
            int sent = 0;
            for (int tick = 1; tick <= 60; tick++)
            {
                if (selector.ShouldSend(tick)) sent++;
            }

            Assert.Equal(30, sent);

            var lossy = new LinkStats { LossFraction = 1f };
            selector.Update(0, lossy);
            selector.Update(NetRules.CongestionDowngradeMs, lossy);
            sent = 0;
            for (int tick = 61; tick <= 120; tick++)
            {
                if (selector.ShouldSend(tick)) sent++;
            }

            Assert.Equal(20, sent);
        }
    }
}
