using System;
using System.Collections.Generic;
using Confluence.Hosting.Economics;
using Confluence.Match;

namespace Confluence.Hosting.Duels
{
    /// <summary>
    /// The duel runner's view of the economy: each duel fights with the loadout the economy finalized at the alcove
    /// (spec L46), and a completed duel spends the consumable charges the Sim reported (L212). Replays and void reruns
    /// read the same round snapshot, so they start with the round's full loadout (L38).
    /// </summary>
    public sealed class HostedDuelLoadouts : IDuelLoadouts
    {
        private readonly HostedEconomy _economy;
        private readonly IReadOnlyList<string> _playerIds;

        public HostedDuelLoadouts(HostedEconomy economy, IReadOnlyList<string> playerIds)
        {
            _economy = economy ?? throw new ArgumentNullException(nameof(economy));
            _playerIds = playerIds ?? throw new ArgumentNullException(nameof(playerIds));
            if (playerIds.Count != economy.PlayerCount) throw new ArgumentException("One player id per economy seat is required.", nameof(playerIds));
        }

        public MappedLoadout LoadoutFor(int player, DuelSetup setup)
        {
            if (setup == null) throw new ArgumentNullException(nameof(setup));
            return _economy.MapForDuel(player, _playerIds[player], setup.SideOf(player));
        }

        public void RecordConsumablesUsed(int player, int count) => _economy.RecordConsumablesUsed(player, count);
    }
}
