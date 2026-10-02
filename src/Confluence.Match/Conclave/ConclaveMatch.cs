using System;
using System.Collections.Generic;
using Confluence.Core.Random;
using Confluence.Core.Rules;

namespace Confluence.Match
{
    /// <summary>
    /// Conclave (spec L32-L61): 2-8 solo players (8 standard) with 8 Resolve each play twelve regular rounds of simultaneous
    /// 1v1 fights, then a seeded single-elimination bracket of at most three rounds among the survivors. Losing a regular
    /// round removes ResolveLoss(round) Resolve, a draw removes it from both, and players at zero are eliminated together
    /// once the round finishes. The schedule ends early when one contender remains.
    /// </summary>
    public sealed class ConclaveMatch : MatchFlow
    {
        private readonly List<int> _bracketLosers = new List<int>();
        private readonly Dictionary<PlayerPair, int> _bracketMatchOf = new Dictionary<PlayerPair, int>();
        private readonly Dictionary<PlayerPair, int> _bracketDraws = new Dictionary<PlayerPair, int>();
        private int[] _bracketAdvancing;
        private int _lastPlannedRound;

        public ConclaveMatch(ConclaveSettings settings, ulong matchSeed, IDuelRunner runner, IMatchEconomy economy, IArenaProvider arenas)
            : base(MatchMode.Conclave, matchSeed, ValidPlayerCount(settings), settings.Timings, settings.StartingCrowns,
                MatchRules.ConclaveScheduledRoundCapacity, runner, economy, arenas)
        {
            Settings = settings;
        }

        public ConclaveSettings Settings { get; }

        /// <summary>The seeded bracket, created when round 12 completes with two or more contenders (L38).</summary>
        public Bracket Bracket { get; private set; }

        public bool InBracket => BracketRound > 0;

        /// <summary>Contenders still in the match, ascending.</summary>
        public IReadOnlyList<int> RemainingContenders => Contenders();

        private protected override string SettingsFingerprint => Settings.Fingerprint;

        private protected override ulong ArenaSeedFor(int round, int attempt)
        {
            var rotation = Settings.ArenaSeedRotation;
            if (rotation.Count == 0) return base.ArenaSeedFor(round, attempt);
            ulong hostSeed = rotation[(round - 1) % rotation.Count];
            return attempt == 0 ? hostSeed : SeedHash.Combine(hostSeed, (ulong)attempt);
        }

        // ---- Planning ---------------------------------------------------------------------------------------------------

        private protected override RoundPlan PlanNextRound()
        {
            var contenders = Contenders();
            if (contenders.Count <= 1) return null; // L38: the schedule ends early when one contender remains
            int round = _lastPlannedRound + 1;
            RoundPlan plan;
            if (round <= GlobalRules.Match.RegularRounds)
            {
                plan = PlanRegularRound(round, contenders);
            }
            else
            {
                if (Bracket == null) SeedBracket(round, contenders);
                SkipUncontestedBracketRounds();
                if (Bracket.IsComplete) return null;
                plan = PlanBracketRound(round, contenders);
            }

            _lastPlannedRound = round;
            return plan;
        }

        /// <summary>Bye by L61, then the complete minimum-cost pairing (L59), then alternating sides (L59).</summary>
        private RoundPlan PlanRegularRound(int round, List<int> contenders)
        {
            var byes = new List<int>();
            var pool = new List<int>(contenders);
            if (pool.Count % 2 != 0)
            {
                var candidates = new List<ByeCandidate>(pool.Count);
                foreach (int p in pool) candidates.Add(new ByeCandidate(p, P(p).RegularByes, P(p).LastByeRound));
                int bye = ByeSelector.Choose(candidates, PairingRng(round, PairingStreams.ByeTies));
                byes.Add(bye);
                pool.Remove(bye);
            }

            var pairingCandidates = new List<PairingCandidate>(pool.Count);
            foreach (int p in pool)
            {
                var mp = P(p);
                pairingCandidates.Add(new PairingCandidate(p, mp.Resolve, mp.CombatWins, mp.RecentOpponents(GlobalRules.Match.PairingRepeatWindow)));
            }

            ulong roundSeed = RoundSeedFor(round);
            var solution = PairingSolver.Solve(pairingCandidates, new Rng(roundSeed));
            var sideRng = PairingRng(round, PairingStreams.Sides(0));
            var seats = new List<SeatedPair>(solution.Pairs.Count);
            foreach (var pair in solution.Pairs) seats.Add(Seat(pair, sideRng));
            return new RoundPlan(round, 0, roundSeed, seats, byes, contenders) { Pairing = solution };
        }

        private void SeedBracket(int round, List<int> contenders)
        {
            var entries = new List<BracketSeedEntry>(contenders.Count);
            foreach (int p in contenders)
            {
                var mp = P(p);
                entries.Add(new BracketSeedEntry(p, mp.Resolve, mp.CombatWins, mp.RegularByes, mp.LobbySeed));
            }

            Bracket = new Bracket(Bracket.SeedOrder(entries));
            Emit(new BracketSeededEvent(CurrentTick, round, Bracket.Seeds, Bracket.Size));
        }

        /// <summary>
        /// After shared finishes a bracket round can hold only walkovers; it is advanced without being played, so no empty
        /// round with a shop is scheduled.
        /// </summary>
        private void SkipUncontestedBracketRounds()
        {
            while (!Bracket.IsComplete)
            {
                var matches = Bracket.CurrentMatches();
                var advancing = new int[matches.Count];
                foreach (var m in matches)
                {
                    if (m.IsContested) return;
                    advancing[m.Index] = m.UncontestedPlayer;
                }

                Bracket.CompleteRound(advancing);
            }
        }

        private RoundPlan PlanBracketRound(int round, List<int> contenders)
        {
            int bracketRound = Bracket.CurrentRound;
            var matches = Bracket.CurrentMatches();
            var pairs = new List<PlayerPair>();
            var byes = new List<int>();
            _bracketMatchOf.Clear();
            _bracketDraws.Clear();
            _bracketLosers.Clear();
            _bracketAdvancing = new int[matches.Count];
            foreach (var m in matches)
            {
                _bracketAdvancing[m.Index] = MatchRules.NoPlayer;
                if (m.IsContested)
                {
                    var pair = new PlayerPair(m.HigherSeedPlayer, m.LowerSeedPlayer);
                    pairs.Add(pair);
                    _bracketMatchOf[pair] = m.Index;
                }
                else if (m.UncontestedPlayer != MatchRules.NoPlayer)
                {
                    // Seeded bye (or walkover after a shared finish): advances without a combat win or bonus (L38, L61).
                    byes.Add(m.UncontestedPlayer);
                    _bracketAdvancing[m.Index] = m.UncontestedPlayer;
                }
            }

            ulong roundSeed = RoundSeedFor(round);
            var sideRng = PairingRng(round, PairingStreams.Sides(0));
            var seats = new List<SeatedPair>(pairs.Count);
            foreach (var pair in pairs) seats.Add(Seat(pair, sideRng));
            return new RoundPlan(round, bracketRound, roundSeed, seats, byes, contenders);
        }

        // ---- Outcomes ---------------------------------------------------------------------------------------------------

        private protected override void ApplyPass(RoundPlan plan, FightPass pass, IDictionary<int, RoundResultEntry> entries, List<PlayerPair> replays)
        {
            if (plan.BracketRound == 0) ApplyRegularPass(plan, pass);
            else ApplyBracketPass(pass, entries, replays);
        }

        /// <summary>L36: the loser, or both players of a draw, lose the round's scheduled Resolve. Byes keep theirs (L61).</summary>
        private void ApplyRegularPass(RoundPlan plan, FightPass pass)
        {
            int loss = Schedules.ResolveLoss(plan.Round);
            foreach (var fight in pass.Fights)
            {
                var outcome = fight.Outcome;
                if (outcome.IsDraw)
                {
                    LoseResolve(plan.Round, fight.Seats.Side0Player, loss);
                    LoseResolve(plan.Round, fight.Seats.Side1Player, loss);
                }
                else
                {
                    LoseResolve(plan.Round, outcome.LoserOrNoPlayer, loss);
                }
            }
        }

        private void LoseResolve(int round, int player, int amount)
        {
            var p = P(player);
            int before = p.Resolve;
            p.Resolve = Math.Max(0, before - amount);
            Emit(new ResolveChangedEvent(CurrentTick, round, player, before, p.Resolve));
        }

        /// <summary>
        /// L38: every bracket loss eliminates; a draw replays once; a second draw is a shared finish for both players, so
        /// nobody advances from that match.
        /// </summary>
        private void ApplyBracketPass(FightPass pass, IDictionary<int, RoundResultEntry> entries, List<PlayerPair> replays)
        {
            foreach (var fight in pass.Fights)
            {
                var outcome = fight.Outcome;
                var pair = fight.Seats.Pair;
                int matchIndex = _bracketMatchOf[pair];
                if (!outcome.IsDraw)
                {
                    _bracketAdvancing[matchIndex] = outcome.Winner;
                    _bracketLosers.Add(outcome.LoserOrNoPlayer);
                    entries[outcome.LoserOrNoPlayer].Eliminated = true;
                    continue;
                }

                _bracketDraws.TryGetValue(pair, out int draws);
                _bracketDraws[pair] = ++draws;
                if (draws <= GlobalRules.Match.BracketDrawReplays)
                {
                    replays.Add(pair);
                }
                else
                {
                    _bracketLosers.Add(pair.A);
                    _bracketLosers.Add(pair.B);
                    entries[pair.A].Eliminated = true;
                    entries[pair.B].Eliminated = true;
                }
            }
        }

        private protected override void CompleteRound(RoundPlan plan, IDictionary<int, RoundResultEntry> entries)
        {
            if (plan.BracketRound == 0)
            {
                foreach (int bye in plan.Byes)
                {
                    P(bye).RegularByes++;
                    P(bye).LastByeRound = plan.Round;
                }

                // L36, L40: everyone at zero leaves together after the simultaneous round and shares the mean position.
                var eliminated = new List<int>();
                foreach (int p in plan.ActivePlayers)
                    if (P(p).Resolve <= 0) eliminated.Add(p);
                PlaceTogether(eliminated, plan.Round, 0, true, entries);
                return;
            }

            Bracket.CompleteRound(_bracketAdvancing);
            PlaceTogether(_bracketLosers, plan.Round, plan.BracketRound, true, entries);
        }

        private protected override MatchResult BuildResult()
        {
            // The last contender is champion; if the final group left together, they already share first place.
            var remaining = Contenders();
            PlaceTogether(remaining, Round, BracketRound, false, null);
            var placements = new List<MatchPlacement>(PlayerCount);
            foreach (var p in Players) placements.Add(p.Placement);
            return new MatchResult(MatchMode.Conclave, placements, Settings.Fingerprint, Settings.CountsForRating);
        }

        private static int ValidPlayerCount(ConclaveSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            settings.ThrowIfInvalid();
            return settings.PlayerCount;
        }
    }
}
