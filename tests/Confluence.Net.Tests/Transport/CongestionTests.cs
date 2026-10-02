using System;
using Confluence.Net.Diagnostics;
using Confluence.Net.Sync;
using Confluence.Net.Tests.TestSupport;
using Confluence.Net.Transport;

namespace Confluence.Net.Tests.Transport
{
    /// <summary>
    /// The snapshot rate selector fed with real link statistics (spec L421, DECISIONS D-005: 30 Hz by default,
    /// 20 Hz under congestion).
    /// </summary>
    public class CongestionTests
    {
        [Fact]
        public void Lossy_Link_Drops_The_Snapshot_Rate_To_Twenty_Hertz()
        {
            TransportSettings settings = LoopbackRig.FastSettings();
            settings.DisconnectTimeoutMs = 10000; // keep-alives are lost too
            NetworkConditions conditions = NetworkConditions.Symmetric(latencyMs: 10, jitterMs: 0, lossChance: 0.25, seed: 14);
            using (var rig = new LoopbackRig(settings, conditions))
            {
                var probe = new LinkProbe(rig);
                bool dropped = rig.PumpUntil(probe.Step, 15000);

                Assert.True(dropped, "rate stayed at " + probe.Selector.CurrentRate + "; " + probe);
                Assert.Equal(1, probe.Selector.Downgrades);
                Assert.True(probe.MaxLoss >= NetRules.CongestedLossFraction, "loss should be the signal; " + probe);
            }
        }

        [Fact]
        public void Healthy_Link_Keeps_Thirty_Hertz()
        {
            using (var rig = new LoopbackRig())
            {
                var probe = new LinkProbe(rig);

                // One loss window plus the downgrade delay, with margin.
                int observeMs = 1000 + NetRules.CongestionDowngradeMs + 300;
                Assert.False(rig.PumpUntil(probe.Step, observeMs), "a clean loopback link was judged congested; " + probe);
                Assert.Equal(30, probe.Selector.CurrentRate);
                Assert.Equal(0, probe.Selector.Downgrades);
            }
        }

        /// <summary>
        /// Connects one client, then sends one reliable event per tick (about the rate of combat events in a busy
        /// duel) while feeding the server's link stats for that client into a selector.
        /// </summary>
        private sealed class LinkProbe
        {
            private readonly LoopbackRig _rig;
            private readonly PlayerSession _session;
            private long _nextSendMs;
            private int _sent;

            public LinkProbe(LoopbackRig rig)
            {
                _rig = rig;
                ClientTransport client = rig.NewClient("Bram");
                client.Messages.On(TestMessages.EventCounter, (c, m) => { });
                client.Connect(rig.Endpoint);
                rig.WaitSettled(client, 20000);
                Assert.Equal(ClientState.Connected, client.State);
                Assert.True(rig.Server.Sessions.TryGet(client.PlayerId, out _session));
                Assert.Equal(30, Selector.CurrentRate);
            }

            public SnapshotRateSelector Selector { get; } = new SnapshotRateSelector();

            public float MaxLoss { get; private set; }

            public int MaxQueued { get; private set; }

            /// <summary>One pump step; true once the selector has dropped to the low rate.</summary>
            public bool Step()
            {
                long now = _rig.ServerClock.NowMs;
                if (now >= _nextSendMs)
                {
                    _rig.Server.Send(_session, TestMessages.EventCounter, new CounterMessage { Value = _sent++ });
                    _nextSendMs = now + 1000 / NetRules.TickRate;
                }

                LinkStats stats = _rig.Server.GetLinkStats(_session);
                MaxLoss = Math.Max(MaxLoss, stats.LossFraction);
                MaxQueued = Math.Max(MaxQueued, stats.ReliableQueued);
                Selector.Update(now, stats);
                return Selector.CurrentRate == Selector.LowRate;
            }

            public override string ToString() => "max loss " + MaxLoss + ", max queued " + MaxQueued + ", sent " + _sent;
        }
    }
}
