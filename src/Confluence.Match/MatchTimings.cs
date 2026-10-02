using System;
using Confluence.Core.Rules;
using Confluence.Core.Time;

namespace Confluence.Match
{
    /// <summary>Phase lengths of one round in integer ticks (spec L44-L49).</summary>
    public sealed class MatchTimings
    {
        public MatchTimings(int revealTicks, int shopTicks, int draftTicks, int alcoveTicks, int combatMaxTicks, int resultsTicks)
        {
            if (revealTicks < 0) throw new ArgumentOutOfRangeException(nameof(revealTicks));
            if (shopTicks < 1) throw new ArgumentOutOfRangeException(nameof(shopTicks));
            if (draftTicks < 1) throw new ArgumentOutOfRangeException(nameof(draftTicks));
            if (alcoveTicks < 0) throw new ArgumentOutOfRangeException(nameof(alcoveTicks));
            if (combatMaxTicks < 1) throw new ArgumentOutOfRangeException(nameof(combatMaxTicks));
            if (resultsTicks < 0) throw new ArgumentOutOfRangeException(nameof(resultsTicks));
            RevealTicks = revealTicks;
            ShopTicks = shopTicks;
            DraftTicks = draftTicks;
            AlcoveTicks = alcoveTicks;
            CombatMaxTicks = combatMaxTicks;
            ResultsTicks = resultsTicks;
        }

        /// <summary>Standard timings: reveal 0 s, shop 35 s, draft 30 s, alcove 5 s, combat at most 90 s, results 8 s.</summary>
        public static MatchTimings Standard { get; } = WithShopSeconds(GlobalRules.Round.ShopSec);

        /// <summary>Standard timings with a custom shop timer (Custom Conclave, spec L71).</summary>
        public static MatchTimings WithShopSeconds(float shopSec) => new MatchTimings(
            Ticks.FromSeconds(MatchRules.RevealSec),
            Ticks.FromSeconds(shopSec),
            Ticks.FromSeconds(GlobalRules.Round.DraftSec),
            Ticks.FromSeconds(GlobalRules.Round.AlcoveSec),
            Ticks.FromSeconds(GlobalRules.Round.CombatMaxSec),
            Ticks.FromSeconds(GlobalRules.Round.ResultsSec));

        public int RevealTicks { get; }

        /// <summary>Spec L45: 35 s (custom hosts may change it, L71).</summary>
        public int ShopTicks { get; }

        /// <summary>Spec L45, L227: 30 s.</summary>
        public int DraftTicks { get; }

        /// <summary>Spec L46: 5 s.</summary>
        public int AlcoveTicks { get; }

        /// <summary>Spec L47: at most 90 s.</summary>
        public int CombatMaxTicks { get; }

        /// <summary>Spec L49: 8 s.</summary>
        public int ResultsTicks { get; }
    }
}
