using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Confluence.Core.Rules;

namespace Confluence.Match
{
    /// <summary>How a duel ended (spec L49, L53).</summary>
    public enum DuelEndReason
    {
        /// <summary>One player's health reached zero.</summary>
        Death,

        /// <summary>A surrender is a normal loss and cannot avoid Resolve loss (L53).</summary>
        Surrender,

        /// <summary>The 90 s timer: greater health fraction wins, below 0.5 points apart is a draw (L53).</summary>
        Time,

        /// <summary>Lethal outcomes for both players in the same authoritative step are a draw (L53).</summary>
        SimultaneousLethal,
    }

    /// <summary>Everything the duel runner needs to start one fight (spec L34, L44-L47, L427).</summary>
    public sealed class DuelSetup
    {
        public DuelSetup(int duelId, ulong matchSeed, MatchMode mode, int round, int bracketRound, int attempt, PassKind passKind,
            ArenaPreview arena, int side0Player, int side1Player, int alcoveTicks, int combatMaxTicks)
        {
            if (side0Player == side1Player) throw new ArgumentException("A duel needs two different players.");
            DuelId = duelId;
            MatchSeed = matchSeed;
            Mode = mode;
            Round = round;
            BracketRound = bracketRound;
            Attempt = attempt;
            PassKind = passKind;
            Arena = arena ?? throw new ArgumentNullException(nameof(arena));
            Side0Player = side0Player;
            Side1Player = side1Player;
            AlcoveTicks = alcoveTicks;
            CombatMaxTicks = combatMaxTicks;
        }

        /// <summary>Unique within the match, assigned in a deterministic order.</summary>
        public int DuelId { get; }

        public ulong MatchSeed { get; }
        public MatchMode Mode { get; }
        public int Round { get; }
        public int BracketRound { get; }

        /// <summary>0 for the round's first pass; replays and reruns count up.</summary>
        public int Attempt { get; }

        public PassKind PassKind { get; }

        /// <summary>
        /// True for draw replays and void reruns: the fight reuses the round's frozen loadouts with no shop (L38, L65).
        /// </summary>
        public bool IsReplay => PassKind != PassKind.Original;

        /// <summary>
        /// True for replays and reruns: restore the round's starting loadout in full, including consumables spent in the
        /// drawn or voided attempt ("same loadouts, full resources", L38). Ordinary alcove restoration (L46) applies to
        /// every duel regardless.
        /// </summary>
        public bool FullResourceRestore => IsReplay;

        /// <summary>Shared arena copy for this pass (L34).</summary>
        public ArenaPreview Arena { get; }

        /// <summary>Player spawning at side 0 (<see cref="Confluence.Core.Arena.SpawnPoint.Side"/>).</summary>
        public int Side0Player { get; }

        public int Side1Player { get; }

        /// <summary>Ward duration before the alcoves open (L46).</summary>
        public int AlcoveTicks { get; }

        /// <summary>Combat cap after the alcoves open (L47).</summary>
        public int CombatMaxTicks { get; }

        public bool Involves(int player) => player == Side0Player || player == Side1Player;

        public int OpponentOf(int player)
        {
            if (player == Side0Player) return Side1Player;
            if (player == Side1Player) return Side0Player;
            throw new ArgumentException("Player " + player + " is not in duel " + DuelId + ".", nameof(player));
        }

        public int SideOf(int player)
        {
            if (player == Side0Player) return 0;
            if (player == Side1Player) return 1;
            throw new ArgumentException("Player " + player + " is not in duel " + DuelId + ".", nameof(player));
        }
    }

    /// <summary>One player's side of a resolved duel.</summary>
    public sealed class DuelPlayerResult
    {
        public DuelPlayerResult(int player, float healthFraction, IEnumerable<string> featIds)
        {
            if (!(healthFraction >= 0f && healthFraction <= 1f))
            {
                throw new ArgumentOutOfRangeException(nameof(healthFraction), healthFraction, "Health fractions are 0..1.");
            }

            Player = player;
            HealthFraction = healthFraction;
            FeatIds = new ReadOnlyCollection<string>(featIds == null ? new List<string>() : new List<string>(featIds));
        }

        public int Player { get; }

        /// <summary>Current health over maximum health, excluding shields and temporary borrowed health (L53).</summary>
        public float HealthFraction { get; }

        /// <summary>FEAT-xxx completed in this duel (L251-L280).</summary>
        public IReadOnlyList<string> FeatIds { get; }
    }

    /// <summary>Authoritative result of one duel, reported by the runner.</summary>
    public sealed class DuelOutcome
    {
        public DuelOutcome(int duelId, int winner, DuelEndReason reason, int resolvedCombatTick, DuelPlayerResult first, DuelPlayerResult second)
        {
            if (first == null) throw new ArgumentNullException(nameof(first));
            if (second == null) throw new ArgumentNullException(nameof(second));
            if (first.Player == second.Player) throw new ArgumentException("A duel outcome needs two different players.");
            if (winner != MatchRules.NoPlayer && winner != first.Player && winner != second.Player)
            {
                throw new ArgumentException("The winner must be one of the duelists or NoPlayer for a draw.", nameof(winner));
            }

            if (resolvedCombatTick < 0) throw new ArgumentOutOfRangeException(nameof(resolvedCombatTick));
            DuelId = duelId;
            Winner = winner;
            Reason = reason;
            ResolvedCombatTick = resolvedCombatTick;
            Players = new ReadOnlyCollection<DuelPlayerResult>(new[] { first, second });
        }

        public int DuelId { get; }

        /// <summary>Winning player, or <see cref="MatchRules.NoPlayer"/> for a draw.</summary>
        public int Winner { get; }

        public bool IsDraw => Winner == MatchRules.NoPlayer;

        public DuelEndReason Reason { get; }

        /// <summary>Combat ticks since the alcoves opened when the duel resolved (0 for a surrender in the alcove).</summary>
        public int ResolvedCombatTick { get; }

        public IReadOnlyList<DuelPlayerResult> Players { get; }

        public DuelPlayerResult For(int player)
        {
            if (Players[0].Player == player) return Players[0];
            if (Players[1].Player == player) return Players[1];
            throw new ArgumentException("Player " + player + " is not in duel " + DuelId + ".", nameof(player));
        }

        public int LoserOrNoPlayer => IsDraw ? MatchRules.NoPlayer : (Winner == Players[0].Player ? Players[1].Player : Players[0].Player);

        /// <summary>Checks the outcome against its setup and the end-reason rules (L53). Returns null when consistent.</summary>
        public string Validate(DuelSetup setup, int combatMaxTicks)
        {
            if (setup == null) throw new ArgumentNullException(nameof(setup));
            if (DuelId != setup.DuelId) return "outcome for duel " + DuelId + " reported as duel " + setup.DuelId;
            if (!setup.Involves(Players[0].Player) || !setup.Involves(Players[1].Player)) return "outcome players do not match the duel";
            if (ResolvedCombatTick > combatMaxTicks) return "duel resolved after the combat cap";
            switch (Reason)
            {
                case DuelEndReason.Death:
                case DuelEndReason.Surrender:
                    return IsDraw ? Reason + " needs a winner" : null;
                case DuelEndReason.SimultaneousLethal:
                    return IsDraw ? null : "simultaneous lethal outcomes are draws";
                case DuelEndReason.Time:
                    int judged = DuelJudgement.WinnerAtTime(Players[0], Players[1]);
                    return judged == Winner ? null : "time judgement does not match the health fractions";
                default:
                    return "unknown end reason " + Reason;
            }
        }

        public static DuelOutcome Win(int duelId, int winner, int loser, DuelEndReason reason, int resolvedCombatTick,
            float winnerFraction = 1f, float loserFraction = 0f, IEnumerable<string> winnerFeats = null, IEnumerable<string> loserFeats = null) =>
            new DuelOutcome(duelId, winner, reason, resolvedCombatTick,
                new DuelPlayerResult(winner, winnerFraction, winnerFeats), new DuelPlayerResult(loser, loserFraction, loserFeats));

        public static DuelOutcome Draw(int duelId, int playerA, int playerB, DuelEndReason reason, int resolvedCombatTick,
            float fractionA = 0.5f, float fractionB = 0.5f) =>
            new DuelOutcome(duelId, MatchRules.NoPlayer, reason, resolvedCombatTick,
                new DuelPlayerResult(playerA, fractionA, null), new DuelPlayerResult(playerB, fractionB, null));
    }

    /// <summary>
    /// Runs duels. Implemented by the server's adapter over Confluence.Sim; tests script it. Match starts every duel of
    /// a pass when the alcoves are entered and polls each one every tick until it reports an outcome. A player's
    /// disconnection never pauses a duel (L55): the runner keeps simulating and reports the result normally.
    /// </summary>
    public interface IDuelRunner
    {
        /// <summary>Create the duel world for one pairing; the alcove ward lasts <see cref="DuelSetup.AlcoveTicks"/>.</summary>
        void StartDuel(DuelSetup setup);

        /// <summary>
        /// Called once per match tick from the tick after <see cref="StartDuel"/> until an outcome is returned.
        /// <paramref name="duelTick"/> counts ticks since the duel started (the alcoves open at AlcoveTicks). An adapter may
        /// step its simulation here or report a simulation stepped elsewhere.
        /// </summary>
        bool Poll(int duelId, int duelTick, out DuelOutcome outcome);

        /// <summary>Discard a duel whose round was voided (L427). Its outcome, if any, is never used.</summary>
        void AbortDuel(int duelId);
    }

    /// <summary>Time judgement at the 90 s timer (spec L53), shared by the Sim adapter and outcome validation.</summary>
    public static class DuelJudgement
    {
        /// <summary>
        /// Winner by greater health fraction, or <see cref="MatchRules.NoPlayer"/> when the fractions differ by less than
        /// 0.5 percentage points (GlobalRules.Round.DrawThresholdFraction).
        /// </summary>
        public static int WinnerAtTime(DuelPlayerResult a, DuelPlayerResult b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            int sign = Compare(a.HealthFraction, b.HealthFraction);
            return sign > 0 ? a.Player : sign < 0 ? b.Player : MatchRules.NoPlayer;
        }

        /// <summary>
        /// +1 when <paramref name="mine"/> is ahead, -1 when behind, 0 when equal for judgement purposes: the
        /// equal/ahead/behind indicator shown during the final 15 s (L53).
        /// </summary>
        public static int Compare(float mine, float theirs)
        {
            float difference = mine - theirs;
            if (Math.Abs(difference) < GlobalRules.Round.DrawThresholdFraction) return 0;
            return difference > 0f ? 1 : -1;
        }
    }
}
