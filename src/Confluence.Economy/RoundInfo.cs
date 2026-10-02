using System;
using Confluence.Core.Rules;

namespace Confluence.Economy
{
    /// <summary>
    /// The scheduled round an economy phase belongs to: mode, 1-based round number, and whether it is a bracket
    /// round or tied-round replay (which never adds drafts, spec L225). Immutable.
    /// </summary>
    public sealed class RoundInfo : IEquatable<RoundInfo>
    {
        public RoundInfo(MatchMode mode, int round, bool isBracketOrReplay = false)
        {
            if (round < 1) throw new ArgumentOutOfRangeException(nameof(round), round, "Rounds are 1-based.");
            Mode = mode;
            Round = round;
            IsBracketOrReplay = isBracketOrReplay;
        }

        public MatchMode Mode { get; }
        public int Round { get; }
        public bool IsBracketOrReplay { get; }

        /// <summary>The round number round_at_least leaves compare against (DECISIONS D-029).</summary>
        public int PredicateRound => Schedules.PredicateRound(Mode, Round);

        /// <summary>Draft rarity of this round, or false when the round has no augment draft (spec L216-L225).</summary>
        public bool TryGetDraftRarity(out Rarity rarity) => Schedules.DraftRarity(Mode, Round, IsBracketOrReplay, out rarity);

        /// <summary>Equipment tiers unlock by round for everyone (spec L195-L202).</summary>
        public bool IsTierUnlocked(int tier) => Schedules.IsTierUnlocked(Mode, Round, tier);

        /// <summary>The first scheduled draft round of a rarity in a mode (Conclave 1/5/9, Duel 1/3/5).</summary>
        public static RoundInfo FirstDraftOf(MatchMode mode, Rarity rarity)
        {
            foreach (int round in Schedules.DraftRounds(mode))
            {
                if (Schedules.DraftRarity(mode, round, out var r) && r == rarity) return new RoundInfo(mode, round);
            }
            throw new ArgumentOutOfRangeException(nameof(rarity), rarity, "No draft of this rarity.");
        }

        public bool Equals(RoundInfo other) =>
            other != null && Mode == other.Mode && Round == other.Round && IsBracketOrReplay == other.IsBracketOrReplay;

        public override bool Equals(object obj) => Equals(obj as RoundInfo);

        public override int GetHashCode() => ((int)Mode * 397 ^ Round) * 2 + (IsBracketOrReplay ? 1 : 0);

        public override string ToString() => Mode + " round " + Round + (IsBracketOrReplay ? " (bracket/replay)" : "");
    }
}
