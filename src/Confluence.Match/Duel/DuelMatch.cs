using System;
using System.Collections.Generic;
using Confluence.Core.Random;
using Confluence.Core.Rules;

namespace Confluence.Match
{
    /// <summary>Options of a Duel match (spec L65, L68).</summary>
    public sealed class DuelMatchOptions
    {
        private const string FingerprintPrefix = "confluence.duel.rules.v1";

        public DuelMatchOptions(bool ranked = false, bool reuseWizardChoices = false, ulong rematchOfSeed = 0UL)
        {
            Ranked = ranked;
            ReuseWizardChoices = reuseWizardChoices;
            RematchOfSeed = rematchOfSeed;
        }

        public static DuelMatchOptions Standard { get; } = new DuelMatchOptions();

        /// <summary>Ranked Duel: a separate 1v1 rating with the Duel rules (L68).</summary>
        public bool Ranked { get; }

        /// <summary>
        /// Private rematch: keep both players' wizard choices instead of returning to selection (L65). A flag for the lobby;
        /// wizard selection itself is outside the match flow.
        /// </summary>
        public bool ReuseWizardChoices { get; }

        /// <summary>Seed of the match this one is a rematch of, or 0.</summary>
        public ulong RematchOfSeed { get; }

        public bool IsRematch => RematchOfSeed != 0UL;

        /// <summary>Duel rules are fixed; the fingerprint only distinguishes ranked from unranked play.</summary>
        public string Fingerprint => SeedHash.Of(FingerprintPrefix + ";ranked=" + (Ranked ? "1" : "0")).ToString("X16");
    }

    /// <summary>
    /// Duel (spec L65): two players, best of seven with the Conclave round economy and the Duel tier and draft schedules
    /// (tiers in rounds 1, 2, 4, 6; Silver drafts in 1-2, Gold 3-4, Prismatic 5-6). A tied round replays with the same
    /// loadouts and no extra income; after two replays it scores half a win each. At 3.5-3.5 one deciding round is
    /// played, and two drawn deciders record a match draw.
    /// </summary>
    public sealed class DuelMatch : MatchFlow
    {
        /// <summary>Half wins needed to take the match: four wins (L65).</summary>
        public const int HalfWinsToClinch = (int)(GlobalRules.Match.DuelWinsToClinch * 2);

        private int _lastPlannedRound;
        private int _drawsThisRound;
        private bool _matchOver;

        public DuelMatch(ulong matchSeed, IDuelRunner runner, IMatchEconomy economy, IArenaProvider arenas, DuelMatchOptions options = null)
            : base(MatchMode.Duel, matchSeed, 2, MatchTimings.Standard, GlobalRules.Economy.FirstShopIncome,
                MatchRules.DuelScheduledRoundCapacity, runner, economy, arenas)
        {
            Options = options ?? DuelMatchOptions.Standard;
        }

        public DuelMatchOptions Options { get; }

        /// <summary>Score of a player in wins (half wins appear as .5).</summary>
        public float Score(int player) => Player(player).DuelHalfWins / 2f;

        /// <summary>True while the deciding round after a 3.5-3.5 finish is being played.</summary>
        public bool IsDecider => Round > GlobalRules.Match.DuelBestOf;

        /// <summary>True when the match ended as a recorded draw (two drawn deciders).</summary>
        public bool IsRecordedDraw { get; private set; }

        /// <summary>Drawn attempts of the current round so far.</summary>
        public int DrawsThisRound => _drawsThisRound;

        /// <summary>Seed for a private rematch of a finished match (L65); deterministic so the rematch is reproducible.</summary>
        public static ulong RematchSeed(ulong previousSeed) => SeedHash.Combine(previousSeed, SeedHash.Of("confluence.duel.rematch"));

        /// <summary>Options for a private rematch that reuses or resets wizard choices (L65).</summary>
        public DuelMatchOptions RematchOptions(bool reuseWizardChoices) => new DuelMatchOptions(Options.Ranked, reuseWizardChoices, MatchSeed);

        private protected override string SettingsFingerprint => Options.Fingerprint;

        private protected override RoundPlan PlanNextRound()
        {
            if (_matchOver) return null;
            int round = _lastPlannedRound + 1;
            _lastPlannedRound = round;
            _drawsThisRound = 0;
            var seat = Seat(new PlayerPair(0, 1), PairingRng(round, PairingStreams.Sides(0)));
            return new RoundPlan(round, 0, RoundSeedFor(round), new List<SeatedPair> { seat }, new List<int>(), new List<int> { 0, 1 });
        }

        private protected override void ApplyPass(RoundPlan plan, FightPass pass, IDictionary<int, RoundResultEntry> entries, List<PlayerPair> replays)
        {
            var fight = pass.Fights[0];
            var outcome = fight.Outcome;
            if (!outcome.IsDraw)
            {
                P(outcome.Winner).DuelHalfWins += 2;
                EmitScore(plan.Round, pass.Attempt);
                return;
            }

            // A void rerun stands in for its voided attempt, so its draw counts exactly as that attempt's would have.
            _drawsThisRound++;
            var pair = fight.Seats.Pair;
            if (plan.Round > GlobalRules.Match.DuelBestOf)
            {
                if (_drawsThisRound < GlobalRules.Match.DuelDeciderAttempts) replays.Add(pair);
                else
                {
                    _matchOver = true; // L65: two drawn deciders produce a recorded match draw
                    IsRecordedDraw = true;
                }

                return;
            }

            if (_drawsThisRound <= GlobalRules.Match.DuelReplaysBeforeHalfWin)
            {
                replays.Add(pair);
                return;
            }

            // L65: after two replays the round scores one half win each and the round number advances.
            P(0).DuelHalfWins += 1;
            P(1).DuelHalfWins += 1;
            entries[0].Outcome = RoundOutcome.HalfWin;
            entries[1].Outcome = RoundOutcome.HalfWin;
            EmitScore(plan.Round, pass.Attempt);
        }

        private protected override void CompleteRound(RoundPlan plan, IDictionary<int, RoundResultEntry> entries)
        {
            if (_matchOver) return;
            int a = P(0).DuelHalfWins, b = P(1).DuelHalfWins;
            if (a >= HalfWinsToClinch || b >= HalfWinsToClinch) _matchOver = true;
            // After round 7 the total is seven wins, so a score below four each is exactly 3.5-3.5: round 8 decides.
        }

        private protected override MatchResult BuildResult()
        {
            int a = P(0).DuelHalfWins, b = P(1).DuelHalfWins;
            if (IsRecordedDraw || a == b)
            {
                PlaceTogether(new[] { 0, 1 }, Round, 0, false, null);
            }
            else
            {
                int winner = a > b ? 0 : 1;
                PlaceTogether(new[] { 1 - winner }, Round, 0, false, null);
                PlaceTogether(new[] { winner }, Round, 0, false, null);
            }

            return new MatchResult(MatchMode.Duel, new[] { P(0).Placement, P(1).Placement }, Options.Fingerprint, Options.Ranked, a, b);
        }

        private void EmitScore(int round, int attempt) =>
            Emit(new DuelScoreChangedEvent(CurrentTick, round, attempt, P(0).DuelHalfWins, P(1).DuelHalfWins));
    }
}
