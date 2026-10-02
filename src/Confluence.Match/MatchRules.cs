using System;
using Confluence.Core.Rules;
using Confluence.Core.Time;

namespace Confluence.Match
{
    /// <summary>
    /// Match-flow numbers the spec leaves open. Global numbers (phase lengths, Resolve, income, bracket and Duel
    /// limits) come from <see cref="GlobalRules"/>; only values with no global home live here.
    /// </summary>
    public static class MatchRules
    {
        /// <summary>Player id meaning "nobody" (no opponent, draw winner, empty bracket slot).</summary>
        public const int NoPlayer = -1;

        /// <summary>
        /// The reveal (spec L44) has no stated duration and its information stays on screen through the shop
        /// (L329 preview "during shopping"), so by default it is an instantaneous phase that opens the shop at once.
        /// </summary>
        public const float RevealSec = 0f;

        /// <summary>
        /// Slack after the 90 s combat cap (L47) before a duel that never reported an outcome is treated as a failed
        /// round and voided for everyone (L427). The simulation itself ends every duel at the cap.
        /// </summary>
        public const float OutcomeGraceSec = 1f;

        /// <summary>Implementation bounds for the custom shop timer (L71 gives no range).</summary>
        public const float CustomShopSecMin = 5f;
        public const float CustomShopSecMax = 300f;

        /// <summary>Implementation bound for custom starting Crowns (L71 gives no range).</summary>
        public const int CustomStartingCrownsMax = 100000;

        /// <summary>Longest spectator delay a custom host may choose (L391 sets only the 15 s minimum).</summary>
        public const float SpectatorDelayMaxSec = 600f;

        /// <summary>Scheduled rounds a Conclave diameter sequence must cover: 12 regular plus up to 3 bracket rounds (L38, D-028).</summary>
        public const int ConclaveScheduledRoundCapacity = GlobalRules.Match.RegularRounds + GlobalRules.Match.MaxBracketRounds;

        /// <summary>Scheduled rounds a Duel diameter sequence must cover: seven rounds plus one decider (L65).</summary>
        public const int DuelScheduledRoundCapacity = GlobalRules.Match.DuelBestOf + 1;

        /// <summary>Ticks of grace after the combat cap; see <see cref="OutcomeGraceSec"/>.</summary>
        public static int OutcomeGraceTicks => Ticks.FromSeconds(OutcomeGraceSec);

        /// <summary>Reconnect window in ticks (L55: 90 seconds).</summary>
        public static int ReconnectWindowTicks => Ticks.FromSeconds(GlobalRules.Match.ReconnectWindowSec);

        internal static void RequirePlayer(int player, int playerCount, string paramName)
        {
            if (player < 0 || player >= playerCount)
            {
                throw new ArgumentOutOfRangeException(paramName, player, "Player ids are 0.." + (playerCount - 1) + ".");
            }
        }
    }
}
