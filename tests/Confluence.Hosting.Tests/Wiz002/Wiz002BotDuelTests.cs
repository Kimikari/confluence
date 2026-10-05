using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Hosting.Bots;
using Confluence.Hosting.Bots.Profiles;
using Confluence.Match;
using Confluence.Sim.Casting;
using Confluence.Sim.Duel;
using Xunit.Abstractions;
using CastEventKind = Confluence.Casting.CastEventKind;

namespace Confluence.Hosting.Tests.Wiz002
{
    /// <summary>WIZ-002 Bram Cinder bots playing hosted matches through the crest-release grammar.</summary>
    public sealed class Wiz002BotDuelTests
    {
        private readonly ITestOutputHelper _out;

        public Wiz002BotDuelTests(ITestOutputHelper output)
        {
            _out = output;
        }

        [Fact]
        public void Explore()
        {
            MatchHostSettings settings = HostFixture.DuelSettings(HostFixture.SmallDuelSeed, wizard0: "WIZ-002", wizard1: "WIZ-002");
            MatchHost host = HostFixture.Host(settings);
            BotPlayer a = host.AddBot(0, BotProfile.For(BotCompetence.Expert));
            BotPlayer b = host.AddBot(1, BotProfile.For(BotCompetence.Adept));
            var worlds = new List<DuelWorld>();
            host.DuelStarted += d => worlds.Add(d.World);
            Assert.True(host.Start().Started);
            host.RunUntilFinished(60 * 60 * 60);
            List<DuelEvent> log = worlds.SelectMany(w => w.EventLog).ToList();
            foreach (var g in log.OfType<CastDuelEvent>().Where(c => c.Cast.Kind == CastEventKind.Committed).GroupBy(c => c.Cast.SpellId).OrderBy(g => g.Key))
                _out.WriteLine(g.Key + " commits " + g.Count() + " mastered " + g.Count(c => c.Cast.Mastery.IsMastered));
            foreach (var g in log.OfType<DamageEvent>().Where(d => d.Dealt > 0).GroupBy(d => d.SourceId ?? "?").OrderBy(g => g.Key))
                _out.WriteLine(g.Key + " damage " + g.Sum(d => d.Dealt) + " hits " + g.Count());
            _out.WriteLine("shields " + log.OfType<ShieldEvent>().Count(s => s.SourceId == "WIZ-002" && s.Change == ShieldChange.Gained));
            _out.WriteLine("duels " + worlds.Count + " recasts " + log.OfType<CastDuelEvent>().Count(c => c.Cast.Kind == CastEventKind.Recast));
        }
    }
}
