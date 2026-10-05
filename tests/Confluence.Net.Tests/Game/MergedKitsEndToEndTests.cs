using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Hosting;
using Confluence.Hosting.Content;
using Confluence.Match;
using Confluence.Net.Game.Client;
using Confluence.Net.Game.Headless;
using Confluence.Net.Game.Server;
using Confluence.Net.Game.Wire;
using Confluence.Net.Tests.TestSupport;
using Confluence.Sim.Casting;
using Confluence.Sim.Duel;
using Xunit.Abstractions;
using CastEventKind = Confluence.Casting.CastEventKind;

namespace Confluence.Net.Tests.Game
{
    /// <summary>
    /// Stage 2 + 3 batch 1 merge over the network: the server and two headless clients on UDP with the merged WIZ-002 and
    /// WIZ-010 kits and the augments-a pools. Until the equipment branch lands only three tier-1 items are implemented,
    /// so the standard rules refuse on the equipment floor alone (D-010, D-082); the round loop below therefore runs with
    /// exactly one Laboratory override, <c>bypass-economy-floors</c>, and the standard implemented pools (no
    /// <c>pools-whole-catalogue</c>, no grammar-route wizards).
    /// </summary>
    public sealed class MergedKitsEndToEndTests
    {
        private static readonly string[] Kits = { "WIZ-002", "WIZ-010" };
        private readonly ITestOutputHelper _output;

        public MergedKitsEndToEndTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static GameServerSettings Settings(LaboratoryOverrides overrides) => new GameServerSettings
        {
            Mode = MatchMode.Duel,
            PlayerCount = 2,
            Seed = EndToEndTests.SeedWithFirstDiameter(MatchMode.Duel, 24, 300),
            Overrides = overrides,
            ArenaPrefetchRounds = 2,
        };

        [Fact]
        public void Standard_Lobby_Offers_Only_Implemented_Kits_And_Refuses_On_The_Equipment_Floor_Alone()
        {
            using (var rig = new GameRig(Settings(null)))
            {
                // L71 lobby: only wizards whose kit carries [ContentBehavior] (no Laboratory grammar-route wizards).
                ImplementedContent implemented = ImplementedContent.FromShippedAssemblies();
                Assert.Equal(implemented.IdsOf(ContentType.Wizard), rig.Server.Wizards.Select(w => w.WizardId));
                Assert.Equal(Kits, rig.Server.Wizards.Select(w => w.WizardId));
                Assert.All(rig.Server.Wizards, w => Assert.True(w.KitImplemented));

                HeadlessPlayer stranger = rig.AddPlayer("Cael", "WIZ-001", seed: 3);
                Assert.True(rig.RunUntil(() => stranger.Client.View.LastJoinCode != JoinCode.Accepted || stranger.Client.View.Seat >= 0, 600));
                Assert.Equal(JoinCode.WizardNotAvailable, stranger.Client.View.LastJoinCode);

                HeadlessPlayer a = rig.AddPlayer("Ash", "WIZ-002", seed: 1);
                HeadlessPlayer b = rig.AddPlayer("Brook", "WIZ-010", seed: 2);
                bool refused = rig.RunUntil(() => rig.Server.Status == LobbyStatus.Refused, 60 * 60);
                foreach (string line in rig.Log.Where(l => l.Contains("refused") || l.Contains("floor")).Take(20)) _output.WriteLine(line);
                Assert.True(refused, "the standard match started: " + rig.Server.Status);
                Assert.True(rig.RunUntil(() => a.Client.View.Lobby != null && a.Client.View.Lobby.Status == LobbyStatus.Refused, 600));

                // The draft floors pass with the merged registry; the one gap is the tier-1 equipment floor (3 of 6).
                string notice = a.Client.View.Lobby.Notice;
                _output.WriteLine(notice);
                Assert.Contains("Equipment tier 1 3/6", notice);
                Assert.DoesNotContain("WIZ-", notice);
                Assert.Equal(LobbyStatus.Refused, b.Client.View.Lobby.Status);
            }
        }

        [Fact]
        public void Two_Headless_Clients_Play_The_Merged_Kits_Over_Udp_With_Only_The_Floor_Override()
        {
            var overrides = new LaboratoryOverrides(bypassEconomyFloors: true);
            using (var rig = new GameRig(Settings(overrides)))
            {
                Assert.Equal(Kits, rig.Server.Wizards.Select(w => w.WizardId));
                HeadlessPlayer a = rig.AddPlayer("Ash", "WIZ-002", seed: 1);
                HeadlessPlayer b = rig.AddPlayer("Brook", "WIZ-010", seed: 2);
                var worlds = new List<DuelWorld>();
                var shopCards = new List<string>();
                var draftCards = new List<string>();
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
                        shopCards.AddRange(shop.Cards.Where(x => !string.IsNullOrEmpty(x.DefinitionId)).Select(x => x.DefinitionId));
                    };
                    p.Client.DraftChanged += c => draftCards.AddRange(c.View.Draft.Cards.Where(x => x != null).Select(x => x.AugmentId));
                }

                Assert.True(rig.RunUntil(() => rig.Server.Host != null, 60 * 60), "the match was never created");
                rig.Server.Host.DuelStarted += d => worlds.Add(d.World);
                bool done = rig.RunUntil(() => a.MatchOver && b.MatchOver, 60 * 60 * 30);
                _output.WriteLine(a.Summary());
                _output.WriteLine(b.Summary());
                foreach (DuelResultMessage r in a.Client.View.Results)
                    _output.WriteLine("duel " + r.DuelId + " round " + r.Round + " winner " + r.WinnerSeat + " " + r.Reason + " " + r.Health0 + "/" + r.Health1);
                Assert.True(done, "match did not finish");

                // The label and fingerprint name exactly the one override (L69, L71).
                foreach (HeadlessPlayer p in new[] { a, b })
                {
                    Assert.Equal("laboratory:bypass-economy-floors", p.Client.View.Settings.LaboratoryLabel);
                    Assert.False(p.Client.View.Settings.CountsForRating);
                }

                Assert.Equal(overrides.CanonicalText(), rig.Server.Host.Overrides.CanonicalText());

                // Full round loops (shop -> draft -> alcove -> combat -> results -> next round) for both clients over UDP.
                int rounds = a.Client.View.Results.Select(x => x.Round).Distinct().Count();
                Assert.True(rounds >= 2, "rounds " + rounds);
                foreach (HeadlessPlayer p in new[] { a, b })
                {
                    int index = p == a ? 0 : 1;
                    ClientMatchView v = p.Client.View;
                    for (int r = 1; r <= rounds; r++)
                    {
                        foreach (MatchPhase phase in new[] { MatchPhase.Shop, MatchPhase.Draft, MatchPhase.Alcove, MatchPhase.Combat, MatchPhase.Results })
                        {
                            if (phase == MatchPhase.Draft && r > GlobalRules.Draft.DraftsPerMatch) continue;
                            Assert.True(v.PhaseHistory.Exists(h => h.Round == r && h.Phase == phase), p.Options.Name + " round " + r + " has no " + phase);
                        }
                    }

                    Assert.True(p.Economy.Purchases >= 1, p.Options.Name + " bought nothing");
                    Assert.True(p.Economy.DraftSelections >= 1, p.Options.Name + " drafted nothing");
                    Assert.True(p.Pilot.Casts > 0);
                    Assert.NotNull(v.End);
                    Assert.Equal(v.Results.Count, v.Reports.Count);

                    // The reward: 600 before the first shop, then 500 plus 100 for winning the previous round (L193).
                    Assert.Equal(GlobalRules.Economy.FirstShopIncome, opening[index][1]);
                    for (int r = 2; r <= rounds; r++)
                    {
                        DuelResultMessage previous = v.Results.FindLast(x => x.Round == r - 1);
                        int bonus = previous != null && previous.Attempt == 0 && previous.WinnerSeat == v.Seat ? GlobalRules.Economy.WinnerBonus : 0;
                        Assert.Equal(closing[index][r - 1] + GlobalRules.Economy.LaterShopIncome + bonus, opening[index][r]);
                    }
                }

                // Standard pools: every shop and draft offer was an implemented record, and the drafted augments are owned.
                ImplementedContent implemented = ImplementedContent.FromShippedAssemblies();
                Assert.NotEmpty(shopCards);
                Assert.All(shopCards, id => Assert.True(implemented.IsAvailable(id), id));
                Assert.NotEmpty(draftCards);
                Assert.All(draftCards, id => Assert.True(implemented.IsAvailable(id), id));
                Assert.Contains(worlds, w => w.Player(0).Setup.Augments.Count > 0 || w.Player(1).Setup.Augments.Count > 0);

                // Both kits fought with their own spells through their grammars, driven by the clients' inputs.
                List<DuelEvent> log = worlds.SelectMany(w => w.EventLog).ToList();
                var commits = log.OfType<CastDuelEvent>().Where(c => c.Kind == CastEventKind.Committed).Select(c => c.SpellId).ToList();
                foreach (string kit in Kits)
                {
                    List<string> own = commits.Where(id => id.StartsWith("SPL-", StringComparison.Ordinal) && GameRig.Catalogue.Spell(id).WizardId == kit).Distinct().ToList();
                    _output.WriteLine(kit + " cast " + string.Join(",", own));
                    Assert.True(own.Count >= 2, kit + " cast " + string.Join(",", own));
                }
            }
        }
    }
}
