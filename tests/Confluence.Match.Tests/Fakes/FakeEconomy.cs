using System;
using System.Collections.Generic;

namespace Confluence.Match.Tests.Fakes
{
    /// <summary>
    /// Records every economy call and holds Crown balances. Readiness is scripted: a player becomes ready a given number
    /// of match ticks after their shop or draft opens (null never readies). <see cref="Clock"/> reads the match tick.
    /// </summary>
    internal sealed class FakeEconomy : IMatchEconomy
    {
        private readonly Dictionary<int, int> _openedAt = new Dictionary<int, int>();
        private readonly HashSet<int> _open = new HashSet<int>();

        public List<string> Calls { get; } = new List<string>();
        public List<ShopOpening> Shops { get; } = new List<ShopOpening>();
        public List<DraftOpening> Drafts { get; } = new List<DraftOpening>();
        public List<CompletedDuelRecord> CompletedDuels { get; } = new List<CompletedDuelRecord>();
        public List<(int Player, int Round)> Finalized { get; } = new List<(int, int)>();
        public Dictionary<int, int> Crowns { get; } = new Dictionary<int, int>();

        /// <summary>Per-player ready delay in ticks; players not listed use <see cref="DefaultReadyAfter"/>.</summary>
        public Dictionary<int, int?> ReadyAfterTicks { get; } = new Dictionary<int, int?>();

        public int? DefaultReadyAfter { get; set; }

        public Func<int> Clock { get; set; } = () => 0;

        public void OpenShop(ShopOpening opening)
        {
            Shops.Add(opening);
            Crowns.TryGetValue(opening.Player, out int balance);
            Crowns[opening.Player] = balance + opening.Income;
            Calls.Add("shop+" + opening.Player + "@" + opening.Round);
            Open(opening.Player);
        }

        public void CloseShop(int player)
        {
            Calls.Add("shop-" + player);
            _open.Remove(player);
        }

        public void OpenDraft(DraftOpening opening)
        {
            Drafts.Add(opening);
            Calls.Add("draft+" + opening.Player + "@" + opening.Round + ":" + opening.Rarity);
            Open(opening.Player);
        }

        public void CloseDraft(int player)
        {
            Calls.Add("draft-" + player);
            _open.Remove(player);
        }

        public PublicLoadout FinalizeLoadout(int player, int round)
        {
            Finalized.Add((player, round));
            Calls.Add("final" + player + "@" + round);
            return LoadoutFor(player, round);
        }

        public bool IsReady(int player)
        {
            if (!_open.Contains(player)) return false;
            int? after = ReadyAfterTicks.TryGetValue(player, out var specific) ? specific : DefaultReadyAfter;
            return after.HasValue && Clock() - _openedAt[player] >= after.Value;
        }

        public void RecordCompletedDuel(CompletedDuelRecord record) => CompletedDuels.Add(record);

        /// <summary>The loadout this fake finalizes for a player in a round (one marker item per round).</summary>
        public static PublicLoadout LoadoutFor(int player, int round) => new PublicLoadout(
            "WIZ-" + (player + 1).ToString("000"),
            new[] { "SPL-001" },
            new[] { "EQP-" + round.ToString("000") },
            new string[0],
            new int[0]);

        private void Open(int player)
        {
            _open.Add(player);
            _openedAt[player] = Clock();
        }
    }
}
