using System;
using Confluence.Match;
using MatchDuelEndReason = Confluence.Match.DuelEndReason;
using MatchDuelOutcome = Confluence.Match.DuelOutcome;
using SimDuelEndReason = Confluence.Sim.Duel.DuelEndReason;
using SimDuelOutcome = Confluence.Sim.Duel.DuelOutcome;
using SimDuelResult = Confluence.Sim.Duel.DuelResult;

namespace Confluence.Hosting.Duels
{
    /// <summary>
    /// Translates the Sim's duel result into Match's <see cref="MatchDuelOutcome"/> (the two assemblies both define
    /// DuelOutcome and DuelEndReason). World index i is the player on spawn side i. Reasons (spec L53):
    /// <list type="bullet">
    /// <item>Death -> Death; SimultaneousDeath -> SimultaneousLethal; Timer -> Time.</item>
    /// <item>Surrender with a winner -> Surrender. Both players surrendering in one step is a draw (D-108); Match accepts a
    /// draw only for simultaneous lethal outcomes or time, so it is reported as SimultaneousLethal: the same-step
    /// simultaneity rule of L53 applied to two simultaneous surrenders.</item>
    /// </list>
    /// Health fractions are clamped to 0..1 exactly as the Sim's own timer judgement clamps them, so Match's
    /// re-check of a time outcome (D-097) sees the same numbers.
    /// </summary>
    public static class DuelOutcomeMapper
    {
        public static MatchDuelOutcome Map(DuelSetup setup, SimDuelResult result, int resolvedCombatTick)
        {
            if (setup == null) throw new ArgumentNullException(nameof(setup));
            if (result == null) throw new ArgumentNullException(nameof(result));
            var side0 = new DuelPlayerResult(setup.Side0Player, Clamp01(result.HealthFractions[0]), result.CompletedFeats[0]);
            var side1 = new DuelPlayerResult(setup.Side1Player, Clamp01(result.HealthFractions[1]), result.CompletedFeats[1]);
            bool draw = result.Outcome != SimDuelOutcome.Win;
            int winner = draw ? MatchRules.NoPlayer : result.Winner == 0 ? setup.Side0Player : setup.Side1Player;
            return new MatchDuelOutcome(setup.DuelId, winner, MapReason(result.Reason, draw), Math.Max(0, resolvedCombatTick), side0, side1);
        }

        public static MatchDuelEndReason MapReason(SimDuelEndReason reason, bool draw)
        {
            switch (reason)
            {
                case SimDuelEndReason.Death:
                    return MatchDuelEndReason.Death;
                case SimDuelEndReason.SimultaneousDeath:
                    return MatchDuelEndReason.SimultaneousLethal;
                case SimDuelEndReason.Timer:
                    return MatchDuelEndReason.Time;
                case SimDuelEndReason.Surrender:
                    return draw ? MatchDuelEndReason.SimultaneousLethal : MatchDuelEndReason.Surrender;
                default:
                    throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown Sim end reason.");
            }
        }

        private static float Clamp01(float value)
        {
            if (!(value > 0f)) return 0f; // also maps NaN to 0
            return value > 1f ? 1f : value;
        }
    }
}
