using System;
using System.Collections.Generic;
using Confluence.Core.Rules;

namespace Confluence.Economy
{
    /// <summary>Completed-duel feat counts read by feat predicate leaves (spec L633).</summary>
    public interface IFeatCounts
    {
        /// <summary>Number of previous completed duels in this match that met the feat.</summary>
        int CompletedDuelsMeeting(string featId);
    }

    public enum FeatRecordResult
    {
        Recorded,
        /// <summary>Training and practice feats are visible records but never satisfy competitive acquisition (spec L280).</summary>
        IgnoredTraining,
        DuplicateDuel,
        UnknownFeat,
        InvalidDuel,
    }

    /// <summary>
    /// One player's server-verified feats per completed duel in this match (spec L251-L280). Each feat counts once per
    /// duel; a threshold of two needs two separate qualifying duels. Feats grant no Crowns, slots, or choices.
    /// </summary>
    public sealed class FeatLedger : IFeatCounts
    {
        /// <summary>No completed duels (match start, floor checks).</summary>
        public static readonly IFeatCounts None = new NoFeats();

        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> _duels = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _duelOrder = new List<string>();

        /// <summary>Completed competitive duels recorded so far.</summary>
        public int CompletedDuels => _duelOrder.Count;

        /// <summary>Recorded duel keys in report order.</summary>
        public IReadOnlyList<string> DuelKeys => _duelOrder.AsReadOnly();

        public int CompletedDuelsMeeting(string featId)
        {
            return featId != null && _counts.TryGetValue(featId, out int n) ? n : 0;
        }

        /// <summary>
        /// Records the feats the simulation verified for this player in one completed duel. The whole report is rejected
        /// if it names an unknown feat or repeats a duel key; training duels are ignored.
        /// </summary>
        public FeatRecordResult RecordCompletedDuel(string duelKey, bool isTraining, IEnumerable<string> achievedFeatIds)
        {
            if (string.IsNullOrEmpty(duelKey)) return FeatRecordResult.InvalidDuel;
            if (isTraining) return FeatRecordResult.IgnoredTraining;
            if (_duels.Contains(duelKey)) return FeatRecordResult.DuplicateDuel;
            var distinct = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (achievedFeatIds != null)
            {
                foreach (var id in achievedFeatIds)
                {
                    if (!Feats.Exists(id)) return FeatRecordResult.UnknownFeat;
                    if (seen.Add(id)) distinct.Add(id);
                }
            }
            _duels.Add(duelKey);
            _duelOrder.Add(duelKey);
            foreach (var id in distinct)
                _counts[id] = CompletedDuelsMeeting(id) + 1;
            return FeatRecordResult.Recorded;
        }

        private sealed class NoFeats : IFeatCounts
        {
            public int CompletedDuelsMeeting(string featId) => 0;
        }
    }
}
