using System;
using System.Collections.Generic;
using Confluence.Core.Rules;

namespace Confluence.Match
{
    /// <summary>Authoritative per-player match state. Read-only outside the match flow.</summary>
    public sealed class MatchPlayer
    {
        private readonly List<int> _opponents = new List<int>();
        private readonly List<int> _sides = new List<int>();
        private readonly SortedDictionary<string, int> _featDuels = new SortedDictionary<string, int>(StringComparer.Ordinal);

        internal MatchPlayer(int id, int startingResolve, ulong lobbySeed)
        {
            Id = id;
            Resolve = startingResolve;
            LobbySeed = lobbySeed;
            IsConnected = true;
            DisconnectedAtTick = -1;
        }

        public int Id { get; }

        /// <summary>Conclave match survival (spec L36). Never purchased, healed, or stolen.</summary>
        public int Resolve { get; internal set; }

        /// <summary>
        /// Combat wins (draws, byes, and walkovers are not wins). Pairing (L59) and bracket seeding (L38) read it before any
        /// bracket fight, so only regular wins matter there.
        /// </summary>
        public int CombatWins { get; internal set; }

        public int Losses { get; internal set; }
        public int Draws { get; internal set; }

        /// <summary>Regular-round byes (L61); bracket byes are not counted.</summary>
        public int RegularByes { get; internal set; }

        /// <summary>Round of the last regular bye, 0 if none.</summary>
        public int LastByeRound { get; internal set; }

        public bool IsEliminated => Placement != null;

        /// <summary>Final placement once decided.</summary>
        public MatchPlacement Placement { get; internal set; }

        /// <summary>Recorded lobby seed (bracket tie-break, L38).</summary>
        public ulong LobbySeed { get; }

        /// <summary>Opponents of completed fights in order (draw replays included, voided fights excluded).</summary>
        public IReadOnlyList<int> Opponents => _opponents;

        /// <summary>Spawn sides of completed fights in order (L59).</summary>
        public IReadOnlyList<int> Sides => _sides;

        /// <summary>Completed duels this match (L280); voided duels never count.</summary>
        public int CompletedDuels { get; internal set; }

        /// <summary>FEAT-xxx to the number of completed duels in which the player met it (L280), ordered by id.</summary>
        public IReadOnlyDictionary<string, int> FeatDuels => _featDuels;

        /// <summary>Loadout finalized for the current round (visible to the opponent from the alcove on, L46).</summary>
        public PublicLoadout CurrentLoadout { get; internal set; }

        /// <summary>Completed loadout of the previous round, revealed with the next pairing (L44).</summary>
        public PublicLoadout PreviousRoundLoadout { get; internal set; }

        /// <summary>Won the previous combat round outside a draw replay: earns the 100 Crown bonus (L193).</summary>
        public bool WonPreviousCombatRound { get; internal set; }

        /// <summary>Duel score in half wins (a win is 2, a thrice-drawn round 1 each; L65).</summary>
        public int DuelHalfWins { get; internal set; }

        public bool IsConnected { get; internal set; }

        /// <summary>Tick of the current disconnection, -1 while connected.</summary>
        public int DisconnectedAtTick { get; internal set; }

        /// <summary>Opponents of the most recent fights, most recent first, at most <paramref name="count"/>.</summary>
        public List<int> RecentOpponents(int count)
        {
            var recent = new List<int>(count);
            for (int i = _opponents.Count - 1; i >= 0 && recent.Count < count; i--) recent.Add(_opponents[i]);
            return recent;
        }

        public int FeatDuelCount(string featId) => featId != null && _featDuels.TryGetValue(featId, out int n) ? n : 0;

        internal void RecordFight(int opponent, int side, IReadOnlyList<string> featIds)
        {
            _opponents.Add(opponent);
            _sides.Add(side);
            CompletedDuels++;
            if (featIds == null) return;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in featIds)
            {
                // A feat counts once per completed duel (L280).
                if (string.IsNullOrEmpty(id) || !seen.Add(id)) continue;
                _featDuels[id] = FeatDuelCount(id) + 1;
            }
        }

        internal static int StartingResolve(MatchMode mode) => mode == MatchMode.Conclave ? GlobalRules.Match.StartingResolve : 0;
    }
}
