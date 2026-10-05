using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Rules;
using Confluence.Net.Diagnostics;
using Confluence.Net.Game.Client;
using Confluence.Net.Game.Headless;
using Confluence.Net.Game.Server;
using Confluence.Net.Game.Wire;
using Confluence.Net.Tests.TestSupport;
using Xunit.Abstractions;

namespace Confluence.Net.Tests.Game
{
    /// <summary>
    /// Authoritative duel events (casts, damage confirmations, deaths; spec L419) stay timely under 10% loss: the
    /// reliable Events lane carries no cosmetic traffic, so a lost reaction cue never holds a damage confirmation back
    /// (research §3, snapshots and events).
    /// </summary>
    public sealed class EventLatencyTests
    {
        private readonly ITestOutputHelper _output;

        public EventLatencyTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        [Trait("Category", "Slow")]
        public void Combat_Events_Arrive_Within_A_Bound_Under_Ten_Percent_Loss()
        {
            var settings = new GameServerSettings
            {
                Mode = MatchMode.Duel,
                PlayerCount = 2,
                Seed = EndToEndTests.SeedWithFirstDiameter(MatchMode.Duel, 24, 300),
                Laboratory = true,
                ArenaPrefetchRounds = 1,
            };
            NetworkConditions link = NetworkConditions.Symmetric(40, 5, 0.10, seed: 17);
            using (var rig = new GameRig(settings, link))
            {
                HeadlessPlayer a = rig.AddPlayer("Ash", "WIZ-002", seed: 1);
                HeadlessPlayer b = rig.AddPlayer("Brook", "WIZ-010", seed: 2);
                var lateness = new List<double>();
                var cosmetic = 0;
                foreach (HeadlessPlayer p in new[] { a, b })
                {
                    GameClient client = p.Client;
                    client.DuelStarted += (c, duel) =>
                    {
                        duel.EventReceived += (d, e) =>
                        {
                            if (!rig.PaceRealTime) return;
                            if (e.Type == WireEventType.Reaction) { cosmetic++; return; }
                            double now = c.ServerTick;
                            if (now < 0) return;
                            lateness.Add(now - d.Start.StartMatchTick - e.Tick);
                        };
                    };
                }

                // Accelerated through the lobby, shop, draft and alcove; then 20 s of combat in real time.
                bool inCombat = rig.RunUntil(() => a.Client.Duel != null && b.Client.Duel != null && a.Client.Duel.Phase == Confluence.Sim.Duel.DuelPhase.Combat &&
                                                   b.Client.Duel.Phase == Confluence.Sim.Duel.DuelPhase.Combat, 60 * 60 * 20);
                Assert.True(inCombat, "combat did not start");
                rig.PaceRealTime = true;
                for (int i = 0; i < 20 * 60 && !(a.Client.Duel.Ended || b.Client.Duel.Ended); i++) rig.Step();
                rig.PaceRealTime = false;
                foreach (string m in rig.Server.Metrics) _output.WriteLine(m);
                foreach (HeadlessPlayer p in new[] { a, b }) _output.WriteLine(p.Options.Name + " snapshot lateness p95 " + p.Client.Duel.Interpolation.LatenessP95.ToString("0.0") + "");

                lateness.Sort();
                double p50 = lateness[lateness.Count / 2], p99 = lateness[(int)(lateness.Count * 0.99)], max = lateness[lateness.Count - 1];
                _output.WriteLine("events " + lateness.Count + " (cosmetic " + cosmetic + "), lateness p50 " + p50.ToString("0.0") + " p99 " + p99.ToString("0.0") +
                                  " max " + max.ToString("0.0") + " ticks");
                Assert.True(lateness.Count > 20, "events " + lateness.Count);
                // One-way 40 ms is about 2.4 ticks; a lost packet costs one LiteNetLib resend (about an RTT plus its timer).
                Assert.True(p50 <= 6, "p50 " + p50);
                Assert.True(p99 <= 40, "p99 " + p99);   // 0.67 s: a packet lost twice
                Assert.True(max <= 90, "max " + max);   // 1.5 s: nothing stalls behind a lost packet
            }
        }
    }
}
