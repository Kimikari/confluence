using System.Linq;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Hosting.Bots;
using Confluence.Hosting.Commands;
using Confluence.Hosting.Duels;
using Confluence.Match;
using Confluence.Sim.Duel;
using MatchDuelEndReason = Confluence.Match.DuelEndReason;
using SimDuelEndReason = Confluence.Sim.Duel.DuelEndReason;

namespace Confluence.Hosting.Tests
{
    /// <summary>Match's duel seam over the Sim: timing, outcomes, idle and absent players (spec L46-L55).</summary>
    public sealed class DuelRunnerTests
    {
        [Fact]
        public void Sim_End_Reasons_Map_To_Match_Reasons()
        {
            Assert.Equal(MatchDuelEndReason.Death, DuelOutcomeMapper.MapReason(SimDuelEndReason.Death, false));
            Assert.Equal(MatchDuelEndReason.SimultaneousLethal, DuelOutcomeMapper.MapReason(SimDuelEndReason.SimultaneousDeath, true));
            Assert.Equal(MatchDuelEndReason.Time, DuelOutcomeMapper.MapReason(SimDuelEndReason.Timer, false));
            Assert.Equal(MatchDuelEndReason.Time, DuelOutcomeMapper.MapReason(SimDuelEndReason.Timer, true));
            Assert.Equal(MatchDuelEndReason.Surrender, DuelOutcomeMapper.MapReason(SimDuelEndReason.Surrender, false));
            // Both surrendering in one step is a draw (D-108), reported as a same-step simultaneous outcome (L53).
            Assert.Equal(MatchDuelEndReason.SimultaneousLethal, DuelOutcomeMapper.MapReason(SimDuelEndReason.Surrender, true));
        }

        [Fact]
        public void Alcove_And_Combat_Clocks_Agree_With_The_Sim_And_Idle_Players_Fall_To_The_Boundary_Together()
        {
            MatchHost host = HostFixture.Host(HostFixture.DuelSettings(HostFixture.SmallDuelSeed));
            Assert.True(host.Start().Started);
            host.Submit(PlayerCommand.Ready(0, 1));
            host.Submit(PlayerCommand.Ready(1, 1));
            host.Tick();
            host.Submit(PlayerCommand.DraftPass(0, 2));
            host.Submit(PlayerCommand.DraftPass(1, 2));
            HostFixture.RunUntilPhase(host, MatchPhase.Alcove);
            RunningDuel duel = host.Duels.Running.Single();
            Assert.Equal(0, duel.World.Tick);
            Assert.Equal(DuelPhase.Alcove, duel.World.Phase);

            // The 5 s ward (L46): Match enters combat once the world has run its 300 alcove steps; the next step is the
            // world's first combat step, so the world's combat tick equals Match's combat elapsed time from then on.
            HostFixture.RunUntilPhase(host, MatchPhase.Combat);
            Assert.Equal(host.Flow.Timings.AlcoveTicks, duel.World.Tick);
            Assert.Equal(0, duel.World.CombatTick);
            int combatStart = host.Flow.PhaseStartTick;
            host.Tick();
            Assert.Equal(DuelPhase.Combat, duel.World.Phase);
            Assert.Equal(host.CurrentTick - combatStart, duel.World.CombatTick);

            // Two idle players stay in their alcoves; the contracting boundary (L48, L51) kills both in the same step: a draw (L53).
            HostFixture.RunUntilPhase(host, MatchPhase.Results, host.Flow.Timings.CombatMaxTicks + 10);
            DuelResolvedEvent resolved = host.Flow.Events.OfType<DuelResolvedEvent>().Single();
            Assert.Equal(MatchDuelEndReason.SimultaneousLethal, resolved.Outcome.Reason);
            Assert.True(resolved.Outcome.IsDraw);
            Assert.True(resolved.Outcome.ResolvedCombatTick > Ticks.FromSeconds(GlobalRules.Boundary.ContractionStartSec) && resolved.Outcome.ResolvedCombatTick < host.Flow.Timings.CombatMaxTicks);
            Assert.Equal(resolved.Tick - combatStart, resolved.Outcome.ResolvedCombatTick);
            DuelReport report = host.Duels.Reports.Single();
            Assert.Equal(report.FinalTick, host.Flow.Timings.AlcoveTicks + resolved.Outcome.ResolvedCombatTick);
            Assert.All(report.Sides, s => Assert.Equal(0f, s.HealthFraction));
            Assert.Equal(PassKind.Original, resolved.Setup.PassKind);
        }

        [Fact]
        public void Disconnected_Players_Keep_Their_Slot_And_Lose_Normally_While_Nothing_Pauses()
        {
            MatchHost host = HostFixture.Host(HostFixture.DuelSettings(HostFixture.SmallDuelSeed));
            host.AddBot(0, BotProfile.For(BotCompetence.Expert));
            var absent = (RemoteInputSource)host.InputSourceOf(1);
            Assert.True(host.Start().Started);
            Assert.True(host.SetConnected(1, false));
            Assert.False(absent.Connected);

            for (int i = 0; i < 600; i++) host.Tick();
            Assert.Equal(600, host.CurrentTick);                    // the match kept running
            Assert.True(host.SetConnected(1, true));                // inside the 90 s window (L55)
            Assert.True(host.SetConnected(1, false));

            // The absent player never readies, so the shop and draft run their full timers; then the duel runs anyway.
            HostFixture.RunUntilPhase(host, MatchPhase.Results, 60 * 60 * 4);
            DuelResolvedEvent resolved = host.Flow.Events.OfType<DuelResolvedEvent>().Single();
            Assert.Equal(0, resolved.Outcome.Winner);              // the absent player loses normally
            Assert.True(absent.Consumed > 0);                       // neutral inputs were simulated every tick
            Assert.False(host.SetConnected(1, true));               // the reconnect window has passed
            Assert.True(host.Flow.IsAbandoned(1));
            Assert.Contains(host.Flow.Events.OfType<ConnectionChangedEvent>(), e => e.Player == 1 && !e.Connected);

            // Abandoned players count as ready (L45): the next shop ends once the bot readies, long before its timer.
            HostFixture.RunUntilPhase(host, MatchPhase.Shop, 60 * 60);
            int shopStart = host.CurrentTick;
            HostFixture.RunUntilPhase(host, MatchPhase.Draft, 60 * 60);
            Assert.True(host.CurrentTick - shopStart < host.Flow.Timings.ShopTicks);
        }
    }
}
