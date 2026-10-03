using System;
using System.Linq;
using Confluence.Core.Random;
using Confluence.Core.Rules;
using Confluence.Match;
using Confluence.Net.Game.Headless;
using Confluence.Net.Game.Server;
using Confluence.Net.Game.Wire;
using Confluence.Net.Tests.TestSupport;
using Xunit.Abstractions;

namespace Confluence.Net.Tests.Game
{
    public sealed class EndToEndTests
    {
        private readonly ITestOutputHelper _output;

        public EndToEndTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public static ulong SeedWithFirstDiameter(MatchMode mode, int diameter, ulong start)
        {
            int capacity = mode == MatchMode.Conclave ? MatchRules.ConclaveScheduledRoundCapacity : MatchRules.DuelScheduledRoundCapacity;
            for (ulong seed = start; seed < start + 1000; seed++)
            {
                int[] sequence = Schedules.ArenaDiameterSequence(RngStreams.For(seed, RngStream.Arena, 0), capacity);
                if (sequence[0] == diameter) return seed;
            }

            throw new InvalidOperationException("No seed found.");
        }

        [Fact]
        public void Two_Headless_Clients_Play_A_Laboratory_Duel_Over_Udp()
        {
            var settings = new GameServerSettings
            {
                Mode = MatchMode.Duel,
                PlayerCount = 2,
                Seed = SeedWithFirstDiameter(MatchMode.Duel, 24, 300),
                Laboratory = true,
                ArenaPrefetchRounds = 2,
            };
            using (var rig = new GameRig(settings))
            {
                HeadlessPlayer a = rig.AddPlayer("Ash", "WIZ-002", seed: 1);
                HeadlessPlayer b = rig.AddPlayer("Brook", "WIZ-010", seed: 2);
                bool done = rig.RunUntil(() => a.MatchOver && b.MatchOver, 60 * 60 * 30);
                foreach (string line in rig.Log.Take(200)) _output.WriteLine(line);
                _output.WriteLine(a.Summary());
                _output.WriteLine(b.Summary());
                foreach (DuelResultMessage r in a.Client.View.Results)
                    _output.WriteLine("duel " + r.DuelId + " round " + r.Round + " winner " + r.WinnerSeat + " " + r.Reason + " " + r.Health0 + "/" + r.Health1);
                foreach (PhaseMessage p in a.Client.View.PhaseHistory)
                    _output.WriteLine("phase " + p.Phase + " r" + p.Round + " a" + p.Attempt + " " + p.StartTick + "-" + p.EndTick);
                _output.WriteLine("sight " + a.Pilot.TicksWithSight + " / " + b.Pilot.TicksWithSight + " guard " + a.Pilot.GuardPresses);
                Assert.True(done, "match did not finish");
            }
        }
    }
}
