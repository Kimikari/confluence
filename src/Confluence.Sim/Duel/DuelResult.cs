using System;
using System.Collections.Generic;
using Confluence.Core.Time;

namespace Confluence.Sim.Duel
{
    /// <summary>
    /// Outcome of a finished duel (spec L49-L53, L433 DuelResult): winner or draw, reason, health fractions, feats met
    /// per player, and each player's decisive sequence for the results screen (spec L359).
    /// </summary>
    public sealed class DuelResult
    {
        private DuelResult()
        {
        }

        public DuelOutcome Outcome { get; private set; }

        /// <summary>Winner index, or -1 for a draw.</summary>
        public int Winner { get; private set; }

        public DuelEndReason Reason { get; private set; }
        public int EndTick { get; private set; }
        public float CombatSeconds { get; private set; }
        public bool IsDraw => Outcome == DuelOutcome.Draw;

        /// <summary>Health over maximum health at the end, excluding shields (spec L53).</summary>
        public IReadOnlyList<float> HealthFractions { get; private set; }

        /// <summary>FEAT ids each player met in this duel (spec L251-L280).</summary>
        public IReadOnlyList<IReadOnlyList<string>> CompletedFeats { get; private set; }

        /// <summary>Per player: the events that explain what happened to them in the final seconds (spec L359).</summary>
        public IReadOnlyList<IReadOnlyList<DuelEvent>> DecisiveSequences { get; private set; }

        public bool Won(int player) => Outcome == DuelOutcome.Win && Winner == player;

        internal static DuelResult Build(DuelWorld world, DuelEndEvent end)
        {
            var fractions = new float[DuelWorld.PlayerCount];
            var feats = new IReadOnlyList<string>[DuelWorld.PlayerCount];
            var sequences = new IReadOnlyList<DuelEvent>[DuelWorld.PlayerCount];
            int window = Ticks.FromSeconds(SimRules.Duel.DecisiveSequenceSec);
            for (int i = 0; i < DuelWorld.PlayerCount; i++)
            {
                var p = world.Player(i);
                fractions[i] = p.Resources.HealthFraction;
                bool won = end.Outcome == DuelOutcome.Win && end.Winner == i;
                feats[i] = p.Feats.Completed(won, p.Resources.LowestHealthFraction, p.Resources.ManaCommitted).AsReadOnly();
                sequences[i] = DecisiveSequence.For(world.EventLog, i, world.Tick, window).AsReadOnly();
            }

            return new DuelResult
            {
                Outcome = end.Outcome,
                Winner = end.Winner,
                Reason = end.Reason,
                EndTick = world.Tick,
                CombatSeconds = world.CombatSeconds,
                HealthFractions = Array.AsReadOnly(fractions),
                CompletedFeats = Array.AsReadOnly(feats),
                DecisiveSequences = Array.AsReadOnly(sequences),
            };
        }
    }

    /// <summary>
    /// Builds the results "decisive sequence" (spec L359): incoming attacks with their reaction contribution and
    /// guard/resistance/shield mitigation, status control, displacement, and the final damage, for one player over the
    /// last seconds of the duel, in event order.
    /// </summary>
    public static class DecisiveSequence
    {
        public static List<DuelEvent> For(IReadOnlyList<DuelEvent> log, int player, int endTick, int windowTicks)
        {
            var result = new List<DuelEvent>();
            if (log == null)
            {
                return result;
            }

            int from = endTick - Math.Max(0, windowTicks);
            int start = log.Count;
            while (start > 0 && log[start - 1].Tick >= from)
            {
                start--;
            }

            for (int i = start; i < log.Count; i++)
            {
                if (IsRelevant(log[i], player))
                {
                    result.Add(log[i]);
                }
            }

            return result;
        }

        private static bool IsRelevant(DuelEvent e, int player)
        {
            switch (e)
            {
                case DamageEvent d:
                    return d.Target == player;
                case StatusEvent s:
                    return s.Player == player && s.Change != StatusChange.Expired;
                case GuardEvent g:
                    return g.Player == player && g.Change != GuardChange.Started;
                case MovementEvent m:
                    return m.Player == player && (m.Change == MovementChange.KnockbackStarted || m.Change == MovementChange.EdgeImpact
                        || m.Change == MovementChange.FellOutOfMap || m.Change == MovementChange.KnockbackBlockedByImmunity);
                case Casting.CastDuelEvent c:
                    // The opponent's committed casts and recasts behind incoming hits, and the player's own interrupted casts.
                    return (c.Player != player && (c.Kind == Confluence.Casting.CastEventKind.Committed || c.Kind == Confluence.Casting.CastEventKind.Recast))
                        || (c.Player == player && c.Kind == Confluence.Casting.CastEventKind.Interrupted);
                case DuelEndEvent _:
                    return true;
                default:
                    return false;
            }
        }
    }
}
