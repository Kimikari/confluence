using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Random;
using Confluence.Core.Rules;
using Confluence.Match;
using Confluence.Net.Game.Client;
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
                var opening = new[] { new Dictionary<int, int>(), new Dictionary<int, int>() };
                var closing = new[] { new Dictionary<int, int>(), new Dictionary<int, int>() };
                foreach (HeadlessPlayer p in new[] { a, b })
                {
                    int index = p == a ? 0 : 1;
                    p.Client.ShopChanged += c =>
                    {
                        ShopStateMessage shop = c.View.Shop;
                        if (!opening[index].ContainsKey(shop.Round)) opening[index][shop.Round] = shop.Crowns;
                        closing[index][shop.Round] = shop.Crowns;
                    };
                }

                bool done = rig.RunUntil(() => a.MatchOver && b.MatchOver, 60 * 60 * 30);
                foreach (string line in rig.Log.Take(200)) _output.WriteLine(line);
                _output.WriteLine(a.Summary());
                _output.WriteLine(b.Summary());
                foreach (DuelResultMessage r in a.Client.View.Results)
                    _output.WriteLine("duel " + r.DuelId + " round " + r.Round + " winner " + r.WinnerSeat + " " + r.Reason + " " + r.Health0 + "/" + r.Health1);
                foreach (PhaseMessage p in a.Client.View.PhaseHistory)
                    _output.WriteLine("phase " + p.Phase + " r" + p.Round + " a" + p.Attempt + " " + p.StartTick + "-" + p.EndTick);
                foreach (string m in rig.Server.Metrics) _output.WriteLine(m);
                _output.WriteLine("sight " + a.Pilot.TicksWithSight + " / " + b.Pilot.TicksWithSight + " guard " + a.Pilot.GuardPresses);
                Assert.True(done, "match did not finish");

                // shop -> draft -> duel -> reward -> next round, for both real clients, over UDP.
                int rounds = a.Client.View.Results.Count;
                Assert.True(rounds >= 2, "rounds " + rounds);
                foreach (HeadlessPlayer p in new[] { a, b })
                {
                    int index = p == a ? 0 : 1;
                    ClientMatchView v = p.Client.View;
                    Assert.Equal(rounds, v.Results.Count);
                    for (int r = 1; r <= rounds; r++)
                    {
                        foreach (MatchPhase phase in new[] { MatchPhase.Shop, MatchPhase.Draft, MatchPhase.Alcove, MatchPhase.Combat, MatchPhase.Results })
                            Assert.Contains(v.PhaseHistory, h => h.Round == r && h.Phase == phase);
                    }

                    Assert.True(p.Economy.Purchases >= rounds, p.Options.Name + " purchases " + p.Economy.Purchases);
                    Assert.True(p.Economy.DraftSelections + p.Economy.DraftPasses >= 1);
                    Assert.True(p.Economy.Readies >= rounds);
                    Assert.True(p.Pilot.Casts > 0);
                    Assert.NotNull(v.End);
                    Assert.Contains(v.End.Placements, pl => pl.Seat == v.Seat);
                    Assert.Equal(rounds, v.Reports.Count); // a post-duel report (feats, decisive sequence) every round

                    // Crowns (spec L193): 600 before the first shop, then 500 plus 100 for winning the previous round.
                    Assert.Equal(GlobalRules.Economy.FirstShopIncome, opening[index][1]);
                    for (int r = 2; r <= rounds; r++)
                    {
                        DuelResultMessage previous = v.Results.Find(x => x.Round == r - 1);
                        int bonus = previous != null && previous.WinnerSeat == v.Seat ? GlobalRules.Economy.WinnerBonus : 0;
                        Assert.Equal(closing[index][r - 1] + GlobalRules.Economy.LaterShopIncome + bonus, opening[index][r]);
                    }
                }
            }
        }
    }
}
