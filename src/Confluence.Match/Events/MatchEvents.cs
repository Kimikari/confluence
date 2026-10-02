using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using Confluence.Core.Json;
using Confluence.Core.Rules;

namespace Confluence.Match
{
    public enum MatchEventKind
    {
        MatchStarted,
        PhaseChanged,
        PairingsAnnounced,
        ArenaRevealed,
        ShopOpened,
        DraftOpened,
        LoadoutFinalized,
        DuelStarted,
        DuelResolved,
        ResolveChanged,
        DuelScoreChanged,
        PlayerEliminated,
        BracketSeeded,
        RoundVoided,
        ConnectionChanged,
        MatchFinished,
    }

    /// <summary>
    /// One authoritative match event. The stream is the replay and client feed for match flow (spec L419, L427): equal
    /// seeds and equal duel outcomes produce identical streams, and <see cref="ToString"/> is a canonical text form.
    /// </summary>
    public abstract class MatchEvent
    {
        protected MatchEvent(int tick, int round, int bracketRound, int attempt)
        {
            Tick = tick;
            Round = round;
            BracketRound = bracketRound;
            Attempt = attempt;
        }

        public abstract MatchEventKind Kind { get; }

        /// <summary>Match tick at which the event happened.</summary>
        public int Tick { get; }

        /// <summary>Scheduled round (0 before the first round).</summary>
        public int Round { get; }

        public int BracketRound { get; }

        /// <summary>Pass within the round: 0 first, then replays and reruns.</summary>
        public int Attempt { get; }

        public sealed override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(Tick).Append(' ').Append(Kind).Append(" r").Append(Round);
            if (BracketRound > 0) sb.Append(" b").Append(BracketRound);
            if (Attempt > 0) sb.Append(" a").Append(Attempt);
            AppendDetails(sb);
            return sb.ToString();
        }

        protected abstract void AppendDetails(StringBuilder sb);

        protected static string Number(float value) => JsonWriter.FormatNumber(value);
    }

    public sealed class MatchStartedEvent : MatchEvent
    {
        public MatchStartedEvent(int tick, MatchMode mode, ulong matchSeed, int playerCount, string settingsFingerprint, IEnumerable<ulong> lobbySeeds)
            : base(tick, 0, 0, 0)
        {
            Mode = mode;
            MatchSeed = matchSeed;
            PlayerCount = playerCount;
            SettingsFingerprint = settingsFingerprint;
            LobbySeeds = new ReadOnlyCollection<ulong>(lobbySeeds == null ? new List<ulong>() : new List<ulong>(lobbySeeds));
        }

        public override MatchEventKind Kind => MatchEventKind.MatchStarted;
        public MatchMode Mode { get; }
        public ulong MatchSeed { get; }
        public int PlayerCount { get; }
        public string SettingsFingerprint { get; }

        /// <summary>Recorded per-player lobby seeds (bracket tie-break, L38), indexed by player id.</summary>
        public IReadOnlyList<ulong> LobbySeeds { get; }

        protected override void AppendDetails(StringBuilder sb)
        {
            sb.Append(' ').Append(Mode).Append(" seed=").Append(MatchSeed.ToString("X16")).Append(" players=").Append(PlayerCount)
                .Append(" settings=").Append(SettingsFingerprint ?? "-");
            if (LobbySeeds.Count > 0)
            {
                sb.Append(" lobby=");
                for (int i = 0; i < LobbySeeds.Count; i++) sb.Append(i == 0 ? "" : ",").Append(LobbySeeds[i].ToString("X16"));
            }
        }
    }

    public sealed class PhaseChangedEvent : MatchEvent
    {
        public PhaseChangedEvent(int tick, int round, int bracketRound, int attempt, MatchPhase phase, int endTick)
            : base(tick, round, bracketRound, attempt)
        {
            Phase = phase;
            EndTick = endTick;
        }

        public override MatchEventKind Kind => MatchEventKind.PhaseChanged;
        public MatchPhase Phase { get; }

        /// <summary>Tick the phase's timer expires, or -1 when it ends on a condition (combat, lobby, finished).</summary>
        public int EndTick { get; }

        protected override void AppendDetails(StringBuilder sb) => sb.Append(' ').Append(Phase).Append(" until=").Append(EndTick);
    }

    public sealed class PairingsAnnouncedEvent : MatchEvent
    {
        public PairingsAnnouncedEvent(int tick, int round, int bracketRound, int attempt, PassKind passKind, ulong roundSeed,
            IEnumerable<SeatedPair> pairs, IEnumerable<int> byes)
            : base(tick, round, bracketRound, attempt)
        {
            PassKind = passKind;
            RoundSeed = roundSeed;
            Pairs = new ReadOnlyCollection<SeatedPair>(new List<SeatedPair>(pairs));
            Byes = new ReadOnlyCollection<int>(byes == null ? new List<int>() : new List<int>(byes));
        }

        public override MatchEventKind Kind => MatchEventKind.PairingsAnnounced;
        public PassKind PassKind { get; }

        /// <summary>Recorded round seed that broke pairing, bye, and side ties (L59, L61).</summary>
        public ulong RoundSeed { get; }

        public IReadOnlyList<SeatedPair> Pairs { get; }

        /// <summary>Regular byes, or bracket byes and walkovers.</summary>
        public IReadOnlyList<int> Byes { get; }

        protected override void AppendDetails(StringBuilder sb)
        {
            sb.Append(' ').Append(PassKind).Append(" seed=").Append(RoundSeed.ToString("X16")).Append(" pairs=");
            for (int i = 0; i < Pairs.Count; i++) sb.Append(i == 0 ? "" : ",").Append(Pairs[i]);
            sb.Append(" byes=").Append(string.Join(",", Byes));
        }
    }

    public sealed class ArenaRevealedEvent : MatchEvent
    {
        public ArenaRevealedEvent(int tick, int round, int bracketRound, int attempt, ArenaPreview arena)
            : base(tick, round, bracketRound, attempt)
        {
            Arena = arena ?? throw new ArgumentNullException(nameof(arena));
        }

        public override MatchEventKind Kind => MatchEventKind.ArenaRevealed;
        public ArenaPreview Arena { get; }

        protected override void AppendDetails(StringBuilder sb) => sb.Append(' ').Append(Arena);
    }

    public sealed class ShopOpenedEvent : MatchEvent
    {
        public ShopOpenedEvent(int tick, int round, int bracketRound, ShopOpening opening)
            : base(tick, round, bracketRound, 0)
        {
            Opening = opening ?? throw new ArgumentNullException(nameof(opening));
        }

        public override MatchEventKind Kind => MatchEventKind.ShopOpened;
        public ShopOpening Opening { get; }
        public int Player => Opening.Player;

        protected override void AppendDetails(StringBuilder sb) =>
            sb.Append(" p").Append(Opening.Player).Append(" baseline=").Append(Opening.BaselineIncome).Append(" bonus=").Append(Opening.WinnerBonus)
                .Append(" tier=").Append(Opening.HighestUnlockedTier);
    }

    public sealed class DraftOpenedEvent : MatchEvent
    {
        public DraftOpenedEvent(int tick, int round, int player, Rarity rarity) : base(tick, round, 0, 0)
        {
            Player = player;
            Rarity = rarity;
        }

        public override MatchEventKind Kind => MatchEventKind.DraftOpened;
        public int Player { get; }
        public Rarity Rarity { get; }

        protected override void AppendDetails(StringBuilder sb) => sb.Append(" p").Append(Player).Append(' ').Append(Rarity);
    }

    public sealed class LoadoutFinalizedEvent : MatchEvent
    {
        public LoadoutFinalizedEvent(int tick, int round, int bracketRound, int player, PublicLoadout loadout)
            : base(tick, round, bracketRound, 0)
        {
            Player = player;
            Loadout = loadout;
        }

        public override MatchEventKind Kind => MatchEventKind.LoadoutFinalized;
        public int Player { get; }

        /// <summary>Public final loadout, visible to the opponent from the alcove on (L46). May be null.</summary>
        public PublicLoadout Loadout { get; }

        protected override void AppendDetails(StringBuilder sb) => sb.Append(" p").Append(Player).Append(' ').Append(Loadout?.ToString() ?? "-");
    }

    public sealed class DuelStartedEvent : MatchEvent
    {
        public DuelStartedEvent(int tick, DuelSetup setup) : base(tick, setup.Round, setup.BracketRound, setup.Attempt)
        {
            Setup = setup;
        }

        public override MatchEventKind Kind => MatchEventKind.DuelStarted;
        public DuelSetup Setup { get; }

        protected override void AppendDetails(StringBuilder sb) =>
            sb.Append(" duel=").Append(Setup.DuelId).Append(' ').Append(Setup.Side0Player).Append('>').Append(Setup.Side1Player)
                .Append(' ').Append(Setup.PassKind).Append(" arena=").Append(Setup.Arena.ArenaId);
    }

    public sealed class DuelResolvedEvent : MatchEvent
    {
        public DuelResolvedEvent(int tick, DuelSetup setup, DuelOutcome outcome) : base(tick, setup.Round, setup.BracketRound, setup.Attempt)
        {
            Setup = setup;
            Outcome = outcome;
        }

        public override MatchEventKind Kind => MatchEventKind.DuelResolved;
        public DuelSetup Setup { get; }
        public DuelOutcome Outcome { get; }

        protected override void AppendDetails(StringBuilder sb)
        {
            sb.Append(" duel=").Append(Outcome.DuelId).Append(" winner=").Append(Outcome.Winner).Append(' ').Append(Outcome.Reason)
                .Append(" at=").Append(Outcome.ResolvedCombatTick);
            foreach (var p in Outcome.Players)
            {
                sb.Append(" p").Append(p.Player).Append('=').Append(Number(p.HealthFraction));
                if (p.FeatIds.Count > 0) sb.Append('[').Append(string.Join(",", p.FeatIds)).Append(']');
            }
        }
    }

    public sealed class ResolveChangedEvent : MatchEvent
    {
        public ResolveChangedEvent(int tick, int round, int player, int before, int after) : base(tick, round, 0, 0)
        {
            Player = player;
            Before = before;
            After = after;
        }

        public override MatchEventKind Kind => MatchEventKind.ResolveChanged;
        public int Player { get; }
        public int Before { get; }
        public int After { get; }
        public int Delta => After - Before;

        protected override void AppendDetails(StringBuilder sb) => sb.Append(" p").Append(Player).Append(' ').Append(Before).Append("->").Append(After);
    }

    public sealed class DuelScoreChangedEvent : MatchEvent
    {
        public DuelScoreChangedEvent(int tick, int round, int attempt, int halfWins0, int halfWins1) : base(tick, round, 0, attempt)
        {
            HalfWins0 = halfWins0;
            HalfWins1 = halfWins1;
        }

        public override MatchEventKind Kind => MatchEventKind.DuelScoreChanged;

        /// <summary>Player 0's score in half wins (a win is 2).</summary>
        public int HalfWins0 { get; }

        public int HalfWins1 { get; }

        protected override void AppendDetails(StringBuilder sb) =>
            sb.Append(' ').Append(Number(HalfWins0 / 2f)).Append('-').Append(Number(HalfWins1 / 2f));
    }

    public sealed class PlayerEliminatedEvent : MatchEvent
    {
        public PlayerEliminatedEvent(int tick, int round, int bracketRound, MatchPlacement placement) : base(tick, round, bracketRound, 0)
        {
            Placement = placement ?? throw new ArgumentNullException(nameof(placement));
        }

        public override MatchEventKind Kind => MatchEventKind.PlayerEliminated;
        public MatchPlacement Placement { get; }
        public int Player => Placement.Player;

        protected override void AppendDetails(StringBuilder sb) => sb.Append(' ').Append(Placement);
    }

    public sealed class BracketSeededEvent : MatchEvent
    {
        public BracketSeededEvent(int tick, int round, IEnumerable<int> playersBySeed, int size) : base(tick, round, 0, 0)
        {
            PlayersBySeed = new ReadOnlyCollection<int>(new List<int>(playersBySeed));
            Size = size;
        }

        public override MatchEventKind Kind => MatchEventKind.BracketSeeded;

        /// <summary>Seed 1 first.</summary>
        public IReadOnlyList<int> PlayersBySeed { get; }

        public int Size { get; }

        protected override void AppendDetails(StringBuilder sb) => sb.Append(" seeds=").Append(string.Join(",", PlayersBySeed)).Append(" size=").Append(Size);
    }

    public sealed class RoundVoidedEvent : MatchEvent
    {
        public RoundVoidedEvent(int tick, int round, int bracketRound, int attempt, string reason) : base(tick, round, bracketRound, attempt)
        {
            Reason = reason ?? "";
        }

        public override MatchEventKind Kind => MatchEventKind.RoundVoided;
        public string Reason { get; }

        protected override void AppendDetails(StringBuilder sb) => sb.Append(" reason=").Append(Reason);
    }

    public sealed class ConnectionChangedEvent : MatchEvent
    {
        public ConnectionChangedEvent(int tick, int round, int player, bool connected) : base(tick, round, 0, 0)
        {
            Player = player;
            Connected = connected;
        }

        public override MatchEventKind Kind => MatchEventKind.ConnectionChanged;
        public int Player { get; }
        public bool Connected { get; }

        protected override void AppendDetails(StringBuilder sb) => sb.Append(" p").Append(Player).Append(Connected ? " connected" : " disconnected");
    }

    public sealed class MatchFinishedEvent : MatchEvent
    {
        public MatchFinishedEvent(int tick, int round, MatchResult result) : base(tick, round, 0, 0)
        {
            Result = result ?? throw new ArgumentNullException(nameof(result));
        }

        public override MatchEventKind Kind => MatchEventKind.MatchFinished;
        public MatchResult Result { get; }

        protected override void AppendDetails(StringBuilder sb) => sb.Append(' ').Append(Result);
    }
}
