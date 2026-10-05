using System.Collections.Generic;
using System.Linq;
using Confluence.Casting;
using Confluence.Casting.Grammars;
using Confluence.Core.Content;
using Confluence.Hosting.Bots;
using Confluence.Hosting.Bots.Profiles;
using Confluence.Match;
using Confluence.Sim.Casting;
using Confluence.Sim.Duel;
using CastEventKind = Confluence.Casting.CastEventKind;

namespace Confluence.Hosting.Tests.Wiz002
{
    /// <summary>
    /// End to end: two WIZ-002 Bram Cinder bots play a whole hosted Duel match (arena, economy, alcoves, Sim with the
    /// spell runtime, reaction world and effects) through real per-tick inputs. Their casts go through the crest-release
    /// grammar, the signature and passive run in the duel, and the kit's spells land on the opponent.
    /// </summary>
    public sealed class Wiz002BotDuelTests
    {
        private static readonly string[] Spells = { "SPL-009", "SPL-010", "SPL-011", "SPL-012", "SPL-013", "SPL-014", "SPL-015", "SPL-016" };

        [Fact]
        [Verifies("WIZ-002")]
        public void Two_Bram_Bots_Duel_Through_The_Crest_Grammar_With_Signature_And_Passive()
        {
            MatchHostSettings settings = HostFixture.DuelSettings(HostFixture.SmallDuelSeed, wizard0: "WIZ-002", wizard1: "WIZ-002");
            MatchHost host = HostFixture.Host(settings);
            BotPlayer a = host.AddBot(0, BotProfile.For(BotCompetence.Expert));
            BotPlayer b = host.AddBot(1, BotProfile.For(BotCompetence.Adept));
            var worlds = new List<DuelWorld>();
            var planners = new List<BramCinderPlanner>();
            host.DuelStarted += d => worlds.Add(d.World);
            host.Duels.Finished += r =>
            {
                planners.Add((BramCinderPlanner)a.Input.Planner);
                planners.Add((BramCinderPlanner)b.Input.Planner);
            };
            Assert.True(host.Start().Started);
            host.RunUntilFinished(60 * 60 * 60);
            Assert.True(host.IsFinished);
            Assert.NotEmpty(worlds);

            // Both seats ran Bram's grammar and his kit.
            Assert.All(worlds, w =>
            {
                for (int p = 0; p < DuelWorld.PlayerCount; p++)
                {
                    Assert.IsType<PressureReleaseGrammar>(w.Player(p).Caster.Loadout.Grammar);
                    Assert.NotNull(w.Player(p).Caster.Loadout.Signature);
                    Assert.All(w.Player(p).Caster.Loadout.Spells, Assert.NotNull);
                }
            });

            List<DuelEvent> log = worlds.SelectMany(w => w.EventLog).ToList();
            List<CastEvent> commits = log.OfType<CastDuelEvent>().Where(c => c.Cast.Kind == CastEventKind.Committed).Select(c => c.Cast).ToList();
            List<string> spellsCast = commits.Select(c => c.SpellId).Where(Spells.Contains).Distinct().ToList();
            Assert.True(spellsCast.Count >= 5, string.Join(",", spellsCast));

            // Normal releases and crest releases (the bots attempt the crest only sometimes).
            Assert.Contains(commits, c => Spells.Contains(c.SpellId) && c.Mastery.IsMastered);
            Assert.Contains(commits, c => Spells.Contains(c.SpellId) && !c.Mastery.IsMastered);

            // Kiln Vent: the signature commits with no grammar reward and lands its 28 thermal.
            Assert.Contains(commits, c => c.SpellId == "WIZ-002" && c.Mastery.Reward == MasteryReward.None);
            Assert.Contains(log.OfType<DamageEvent>(), d => d.SourceId == "WIZ-002" && d.Dealt > 0f && d.Generation == 0);

            // Tempered Hands: thermal hits from the other Bram raised the visible 20-point shield.
            Assert.Contains(log.OfType<ShieldEvent>(), s => s.SourceId == "WIZ-002" && s.Change == ShieldChange.Gained && s.Amount <= 20f + 1e-3f);

            // Several of the kit's spells dealt damage to the opponent, and a settled Boiler Orb was detonated by recast.
            List<string> damaging = log.OfType<DamageEvent>().Where(d => d.Dealt > 0f && Spells.Contains(d.SourceId) && d.Attacker != d.Target)
                .Select(d => d.SourceId).Distinct().ToList();
            Assert.True(damaging.Count >= 4, string.Join(",", damaging));
            Assert.Contains(log.OfType<CastDuelEvent>(), c => c.Cast.Kind == CastEventKind.Recast && c.Cast.SpellId == "SPL-014");

            // The casting profile chose by range: close cones, mid rushes and orbs, long fireballs.
            var planned = new HashSet<string>(planners.Where(p => p != null).SelectMany(p => p.Planned.Keys));
            Assert.Contains("SPL-010", planned);
            Assert.Contains("SPL-009", planned);
            Assert.Contains("SPL-013", planned);
            Assert.Contains("SPL-014", planned);
            Assert.True(planners.Where(p => p != null).Sum(p => p.Vents) > 0);
        }
    }
}
