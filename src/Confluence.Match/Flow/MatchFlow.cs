using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Confluence.Core.Random;
using Confluence.Core.Rules;

namespace Confluence.Match
{
    /// <summary>
    /// Tick-driven round flow shared by Conclave and Duel (spec L44-L49): Reveal, Shop, Draft on draft rounds, Alcove,
    /// Combat through <see cref="IDuelRunner"/>, Results, then a replay pass, the next round, or the end. Mode rules
    /// (pairing, Resolve, bracket, best of seven) live in the subclasses. One <see cref="Tick"/> is one authoritative step
    /// (60 per second); every timer is an integer tick count, and nothing pauses for a disconnected player (L55).
    /// </summary>
    public abstract class MatchFlow
    {
        private const int MaxTransitionsPerTick = 16;

        private readonly IDuelRunner _runner;
        private readonly IMatchEconomy _economy;
        private readonly IArenaProvider _arenas;
        private readonly MatchPlayer[] _players;
        private readonly int[] _diameters;
        private readonly List<RoundResult> _roundResults = new List<RoundResult>();
        private readonly List<int> _sessionPlayers = new List<int>();
        private RoundPlan _plan;
        private RoundPlan _nextPlan;
        private List<PlayerPair> _pendingReplays = new List<PlayerPair>();
        private int _nextDuelId = 1;
        private bool _anyShopOpened;

        private protected MatchFlow(MatchMode mode, ulong matchSeed, int playerCount, MatchTimings timings, int firstShopIncome,
            int scheduledRoundCapacity, IDuelRunner runner, IMatchEconomy economy, IArenaProvider arenas)
        {
            if (playerCount < 2) throw new ArgumentOutOfRangeException(nameof(playerCount), playerCount, "A match needs at least two players.");
            if (firstShopIncome < 0) throw new ArgumentOutOfRangeException(nameof(firstShopIncome));
            Mode = mode;
            MatchSeed = matchSeed;
            Timings = timings ?? throw new ArgumentNullException(nameof(timings));
            FirstShopIncome = firstShopIncome;
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _economy = economy ?? throw new ArgumentNullException(nameof(economy));
            _arenas = arenas ?? throw new ArgumentNullException(nameof(arenas));
            _players = new MatchPlayer[playerCount];
            for (int i = 0; i < playerCount; i++)
            {
                _players[i] = new MatchPlayer(i, MatchPlayer.StartingResolve(mode), RngStreams.SeedFor(matchSeed, RngStream.Bracket, 0, i));
            }

            // One balanced diameter sequence per match (spec L306, D-028); stream round 0 never collides with arena seeds.
            _diameters = Schedules.ArenaDiameterSequence(RngStreams.For(matchSeed, RngStream.Arena, 0), scheduledRoundCapacity);
            Players = new ReadOnlyCollection<MatchPlayer>(_players);
            Phase = MatchPhase.Lobby;
            PhaseEndTick = -1;
        }

        public MatchMode Mode { get; }

        /// <summary>Recorded match seed; every random choice derives from it through <see cref="RngStreams"/>.</summary>
        public ulong MatchSeed { get; }

        public MatchTimings Timings { get; }

        /// <summary>Income before the first shop: 600, or the custom starting Crowns (L193, L71).</summary>
        public int FirstShopIncome { get; }

        public int CurrentTick { get; private set; }
        public MatchPhase Phase { get; private set; }
        public int PhaseStartTick { get; private set; }

        /// <summary>Tick the current phase's timer expires, or -1 for condition-ended phases.</summary>
        public int PhaseEndTick { get; private set; }

        public int PhaseTicksRemaining => PhaseEndTick < 0 ? -1 : Math.Max(0, PhaseEndTick - CurrentTick);

        public bool IsFinished => Phase == MatchPhase.Finished;

        /// <summary>Current scheduled round (0 before the first).</summary>
        public int Round => _plan?.Round ?? 0;

        public int BracketRound => _plan?.BracketRound ?? 0;

        /// <summary>Current pass within the round: 0 first, then replays and reruns.</summary>
        public int Attempt => _plan?.CurrentPass?.Attempt ?? 0;

        public PassKind CurrentPassKind => _plan?.CurrentPass?.Kind ?? PassKind.Original;

        public IReadOnlyList<MatchPlayer> Players { get; }

        public MatchEventLog Events { get; } = new MatchEventLog();

        public IReadOnlyList<RoundResult> RoundResults => _roundResults;

        public RoundResult LastRoundResult => _roundResults.Count == 0 ? null : _roundResults[_roundResults.Count - 1];

        /// <summary>Final result once <see cref="IsFinished"/>.</summary>
        public MatchResult Result { get; private set; }

        /// <summary>Arena diameter of each scheduled round (index 0 is round 1).</summary>
        public IReadOnlyList<int> ArenaDiameters => Array.AsReadOnly(_diameters);

        public ArenaPreview CurrentArena => _plan?.CurrentPass?.Arena;

        /// <summary>Seated pairs of the current pass.</summary>
        public IReadOnlyList<SeatedPair> CurrentPairs
        {
            get
            {
                var pairs = new List<SeatedPair>();
                var pass = _plan?.CurrentPass;
                if (pass != null)
                    foreach (var f in pass.Fights) pairs.Add(f.Seats);
                return pairs;
            }
        }

        /// <summary>Byes of the current round (regular byes, bracket byes, walkovers).</summary>
        public IReadOnlyList<int> CurrentByes => _plan == null ? (IReadOnlyList<int>)new int[0] : _plan.Byes.AsReadOnly();

        /// <summary>Pairing diagnostics of the current regular round, or null.</summary>
        public PairingSolution CurrentPairing => _plan?.Pairing;

        /// <summary>The draft rarity of the current round, or null when the round has no draft (L216-L225).</summary>
        public Rarity? CurrentDraftRarity => _plan != null && _plan.HasDraft ? _plan.DraftRarity : (Rarity?)null;

        public MatchPlayer Player(int id)
        {
            MatchRules.RequirePlayer(id, _players.Length, nameof(id));
            return _players[id];
        }

        /// <summary>A contender who has not been eliminated.</summary>
        public bool IsActive(int player) => !Player(player).IsEliminated;

        private protected abstract string SettingsFingerprint { get; }

        // ---- Lifecycle -------------------------------------------------------------------------------------------

        /// <summary>Leaves the lobby and reveals round 1.</summary>
        public void Start()
        {
            if (Phase != MatchPhase.Lobby) throw new InvalidOperationException("The match has already started.");
            var lobbySeeds = new List<ulong>(_players.Length);
            foreach (var p in _players) lobbySeeds.Add(p.LobbySeed);
            Emit(new MatchStartedEvent(CurrentTick, Mode, MatchSeed, _players.Length, SettingsFingerprint, lobbySeeds));
            var first = PlanNextRound();
            if (first == null) Finish();
            else BeginRound(first);
        }

        /// <summary>Advances one authoritative tick: polls running duels, then completes any phases whose time or condition is met.</summary>
        public void Tick()
        {
            if (Phase == MatchPhase.Lobby) throw new InvalidOperationException("Start the match before ticking it.");
            if (IsFinished) return;
            CurrentTick++;
            PollDuels();
            for (int i = 0; TryCompletePhase(); i++)
            {
                if (i >= MaxTransitionsPerTick) throw new InvalidOperationException("Phase transitions did not settle within one tick.");
            }
        }

        /// <summary>
        /// Voids the current pass for everyone after a server failure (spec L427): running duels are aborted, their outcomes
        /// are discarded, nobody gains a win, Resolve, or bonus, and the same pairings play again as a rerun with the same
        /// loadouts, a new arena seed, and no shop.
        /// </summary>
        public void VoidCurrentPass(string reason)
        {
            if (Phase != MatchPhase.Alcove && Phase != MatchPhase.Combat)
            {
                throw new InvalidOperationException("Only a pass in the alcove or combat can be voided.");
            }

            var pass = _plan.CurrentPass;
            pass.Voided = true;
            foreach (var f in pass.Fights) _runner.AbortDuel(f.DuelId);
            Emit(new RoundVoidedEvent(CurrentTick, Round, BracketRound, pass.Attempt, reason));
            var seats = new List<SeatedPair>();
            foreach (var f in pass.Fights) seats.Add(f.Seats);
            BeginPass(PassKind.VoidRerun, pass.Origin, seats);
        }

        // ---- Connection (spec L55) ---------------------------------------------------------------------------------

        /// <summary>
        /// Records a disconnection or reconnection. Reconnecting is refused (returns false) once the 90 s window has passed.
        /// Nothing pauses: an absent player's duels continue and are reported by the runner as usual.
        /// </summary>
        public bool SetConnected(int player, bool connected)
        {
            var p = Player(player);
            if (connected == p.IsConnected) return true;
            if (connected)
            {
                if (IsAbandoned(player)) return false;
                p.IsConnected = true;
                p.DisconnectedAtTick = -1;
            }
            else
            {
                p.IsConnected = false;
                p.DisconnectedAtTick = CurrentTick;
            }

            Emit(new ConnectionChangedEvent(CurrentTick, Round, player, connected));
            return true;
        }

        /// <summary>Disconnected for longer than the reconnect window; treated as ready so the lobby is not held up.</summary>
        public bool IsAbandoned(int player)
        {
            var p = Player(player);
            return !p.IsConnected && CurrentTick - p.DisconnectedAtTick > MatchRules.ReconnectWindowTicks;
        }

        // ---- Public information ---------------------------------------------------------------------------------------

        /// <summary>Reveal for a player in the current pass, or null when the player is not in it (spec L44).</summary>
        public RevealInfo GetReveal(int player)
        {
            var me = Player(player);
            var pass = _plan?.CurrentPass;
            if (pass == null || IsFinished) return null;
            var fight = pass.FightOf(player);
            bool bye = fight == null && pass.CarriesRound && _plan.Byes.Contains(player);
            if (fight == null && !bye) return null;
            int opponent = fight == null ? MatchRules.NoPlayer : fight.Seats.Other(player);
            int side = fight == null ? -1 : fight.Seats.SideOf(player);
            PublicLoadout loadout = null;
            int opponentResolve = 0;
            if (opponent != MatchRules.NoPlayer)
            {
                var them = _players[opponent];
                loadout = pass.Kind == PassKind.Original ? them.PreviousRoundLoadout : them.CurrentLoadout;
                opponentResolve = them.Resolve;
            }

            return new RevealInfo(player, Round, BracketRound, pass.Attempt, pass.Kind, opponent, side, pass.Arena, me.Resolve, opponentResolve, loadout);
        }

        /// <summary>
        /// The opponent loadout a player may inspect right now: the completed previous-round loadout until the alcove,
        /// then the opponent's final loadout for this round (spec L44, L46).
        /// </summary>
        public PublicLoadout VisibleOpponentLoadout(int player)
        {
            Player(player);
            var pass = _plan?.CurrentPass;
            var fight = pass?.FightOf(player);
            if (fight == null) return null;
            var them = _players[fight.Seats.Other(player)];
            bool beforeAlcove = pass.Kind == PassKind.Original &&
                (Phase == MatchPhase.Reveal || Phase == MatchPhase.Shop || Phase == MatchPhase.Draft);
            return beforeAlcove ? them.PreviousRoundLoadout : them.CurrentLoadout;
        }

        // ---- Mode hooks -------------------------------------------------------------------------------------------------

        /// <summary>Plans the next round after the previous one completed, or returns null when the match is over.</summary>
        private protected abstract RoundPlan PlanNextRound();

        /// <summary>
        /// Applies the outcomes of a resolved pass to mode state (Resolve, score, bracket) and fills in the entries. Pairs that
        /// drew and must replay go into <paramref name="replays"/>.
        /// </summary>
        private protected abstract void ApplyPass(RoundPlan plan, FightPass pass, IDictionary<int, RoundResultEntry> entries, List<PlayerPair> replays);

        /// <summary>Round finished with no replay pending: eliminations, byes, bracket advancement.</summary>
        private protected abstract void CompleteRound(RoundPlan plan, IDictionary<int, RoundResultEntry> entries);

        /// <summary>Final placements once <see cref="PlanNextRound"/> returned null.</summary>
        private protected abstract MatchResult BuildResult();

        /// <summary>Arena seed of a pass. A new attempt always gets a new seed (L38).</summary>
        private protected virtual ulong ArenaSeedFor(int round, int attempt) => RngStreams.SeedFor(MatchSeed, RngStream.Arena, round, attempt);

        // ---- Helpers for modes ----------------------------------------------------------------------------------------

        private protected void Emit(MatchEvent e) => Events.Append(e);

        private protected MatchPlayer P(int id) => _players[id];

        private protected int PlayerCount => _players.Length;

        /// <summary>Ids of contenders still in the match, ascending.</summary>
        private protected List<int> Contenders()
        {
            var list = new List<int>();
            foreach (var p in _players)
                if (!p.IsEliminated) list.Add(p.Id);
            return list;
        }

        /// <summary>The recorded round seed (L59): Pairing stream, sub-stream 0.</summary>
        private protected ulong RoundSeedFor(int round) => RngStreams.SeedFor(MatchSeed, RngStream.Pairing, round, PairingStreams.PairingTies);

        private protected Rng PairingRng(int round, int sub) => RngStreams.For(MatchSeed, RngStream.Pairing, round, sub);

        /// <summary>Spawn sides for a pair from both players' side histories (L59).</summary>
        private protected SeatedPair Seat(PlayerPair pair, Rng tieBreak) => SideAssigner.Assign(pair, _players[pair.A].Sides, _players[pair.B].Sides, tieBreak);

        /// <summary>
        /// Gives a group leaving the match together the block of positions just below everyone still in it, each at the
        /// block's mean (L40). Eliminations emit an event; the final standing of the last contenders does not.
        /// </summary>
        private protected void PlaceTogether(IReadOnlyList<int> players, int round, int bracketRound, bool eliminated, IDictionary<int, RoundResultEntry> entries)
        {
            if (players.Count == 0) return;
            var group = new List<int>(players);
            group.Sort();
            int staying = 0;
            foreach (var p in _players)
                if (!p.IsEliminated && !group.Contains(p.Id)) staying++;
            int first = staying + 1, last = staying + group.Count;
            foreach (int id in group)
            {
                var placement = new MatchPlacement(id, first, last, round, bracketRound);
                _players[id].Placement = placement;
                if (entries != null && entries.TryGetValue(id, out var entry))
                {
                    entry.Eliminated = eliminated;
                    entry.Placement = placement;
                }

                if (eliminated) Emit(new PlayerEliminatedEvent(CurrentTick, round, bracketRound, placement));
            }
        }

        // ---- Round and pass flow ----------------------------------------------------------------------------------------

        private void BeginRound(RoundPlan plan)
        {
            _plan = plan;
            _nextPlan = null;
            plan.IsFirstShop = !_anyShopOpened;
            plan.HasDraft = Schedules.DraftRarity(Mode, plan.Round, plan.BracketRound > 0, out var rarity);
            plan.DraftRarity = rarity;
            foreach (int p in plan.ActivePlayers) _players[p].CurrentLoadout = null;
            BeginPass(PassKind.Original, PassKind.Original, plan.Pairs);
        }

        private void BeginPass(PassKind kind, PassKind origin, List<SeatedPair> seats)
        {
            int attempt = _plan.Passes.Count;
            var arena = RequestArena(_plan.Round, attempt);
            var fights = new List<Fight>(seats.Count);
            foreach (var seat in seats) fights.Add(new Fight(_nextDuelId++, seat));
            var pass = new FightPass(kind, origin, attempt, arena, fights);
            _plan.Passes.Add(pass);
            Emit(new PairingsAnnouncedEvent(CurrentTick, _plan.Round, _plan.BracketRound, attempt, kind, _plan.RoundSeed, seats,
                pass.CarriesRound ? _plan.Byes : null));
            Emit(new ArenaRevealedEvent(CurrentTick, _plan.Round, _plan.BracketRound, attempt, arena));
            EnterPhase(MatchPhase.Reveal, Timings.RevealTicks);
        }

        private ArenaPreview RequestArena(int round, int attempt)
        {
            if (round > _diameters.Length) throw new InvalidOperationException("Round " + round + " exceeds the scheduled diameter sequence.");
            var request = new ArenaRequest(MatchSeed, round, attempt, _diameters[round - 1], ArenaSeedFor(round, attempt));
            var preview = _arenas.RequestArena(request);
            if (preview == null) throw new InvalidOperationException("The arena provider returned no arena for round " + round + ".");
            if (preview.Diameter != request.Diameter)
            {
                throw new InvalidOperationException("Arena " + preview.ArenaId + " has diameter " + preview.Diameter + "; round " + round + " needs " + request.Diameter + ".");
            }

            return preview;
        }

        private void EnterPhase(MatchPhase phase, int durationTicks)
        {
            Phase = phase;
            PhaseStartTick = CurrentTick;
            PhaseEndTick = durationTicks >= 0 ? CurrentTick + durationTicks : -1;
            Emit(new PhaseChangedEvent(CurrentTick, Round, BracketRound, Attempt, phase, PhaseEndTick));
        }

        private bool TryCompletePhase()
        {
            int elapsed = CurrentTick - PhaseStartTick;
            switch (Phase)
            {
                case MatchPhase.Reveal:
                    if (elapsed < Timings.RevealTicks) return false;
                    if (_plan.CurrentPass.Kind == PassKind.Original) OpenShops();
                    else EnterAlcove(); // replays and reruns keep the frozen loadouts: no shop or draft (L38, L65)
                    return true;

                case MatchPhase.Shop:
                    if (elapsed < Timings.ShopTicks && !EarlyReady(elapsed)) return false;
                    foreach (int p in _sessionPlayers) _economy.CloseShop(p);
                    if (_plan.HasDraft) OpenDrafts();
                    else EnterAlcove();
                    return true;

                case MatchPhase.Draft:
                    if (elapsed < Timings.DraftTicks && !EarlyReady(elapsed)) return false;
                    foreach (int p in _sessionPlayers) _economy.CloseDraft(p);
                    EnterAlcove();
                    return true;

                case MatchPhase.Alcove:
                    if (elapsed < Timings.AlcoveTicks) return false;
                    EnterPhase(MatchPhase.Combat, Timings.CombatMaxTicks);
                    return true;

                case MatchPhase.Combat:
                    if (AllFightsResolved())
                    {
                        ResolvePass();
                        return true;
                    }

                    if (elapsed > Timings.CombatMaxTicks + MatchRules.OutcomeGraceTicks)
                    {
                        VoidCurrentPass("duel outcome missing after the combat cap");
                        return true;
                    }

                    return false;

                case MatchPhase.Results:
                    if (elapsed < Timings.ResultsTicks) return false;
                    AfterResults();
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// The next phase begins early only when every active player is ready (spec L45). Abandoned players cannot act and
        /// count as ready; players inside the reconnect window do not.
        /// </summary>
        private bool EarlyReady(int elapsed)
        {
            if (elapsed < 1) return false;
            foreach (int p in _sessionPlayers)
            {
                if (!IsAbandoned(p) && !_economy.IsReady(p)) return false;
            }

            return true;
        }

        private void OpenShops()
        {
            EnterPhase(MatchPhase.Shop, Timings.ShopTicks);
            _sessionPlayers.Clear();
            foreach (int p in _plan.ActivePlayers)
            {
                // L193: 600 (or custom starting Crowns) first, then 500 per scheduled or bracket shop, +100 for winning the
                // previous combat round; draws, byes, and replay wins earn no bonus (L61).
                int baseline = _plan.IsFirstShop ? FirstShopIncome : GlobalRules.Economy.LaterShopIncome;
                int bonus = _players[p].WonPreviousCombatRound ? GlobalRules.Economy.WinnerBonus : 0;
                var opening = new ShopOpening(p, Mode, _plan.Round, _plan.BracketRound, _plan.IsFirstShop, baseline, bonus);
                _plan.Shops[p] = opening;
                _economy.OpenShop(opening);
                Emit(new ShopOpenedEvent(CurrentTick, _plan.Round, _plan.BracketRound, opening));
                _sessionPlayers.Add(p);
            }

            _anyShopOpened = true;
        }

        private void OpenDrafts()
        {
            EnterPhase(MatchPhase.Draft, Timings.DraftTicks);
            foreach (int p in _sessionPlayers)
            {
                _economy.OpenDraft(new DraftOpening(p, Mode, _plan.Round, _plan.DraftRarity));
                Emit(new DraftOpenedEvent(CurrentTick, _plan.Round, p, _plan.DraftRarity));
            }
        }

        private void EnterAlcove()
        {
            EnterPhase(MatchPhase.Alcove, Timings.AlcoveTicks);
            var pass = _plan.CurrentPass;
            if (pass.Kind == PassKind.Original)
            {
                foreach (int p in _plan.ActivePlayers)
                {
                    var loadout = _economy.FinalizeLoadout(p, _plan.Round);
                    _players[p].CurrentLoadout = loadout;
                    Emit(new LoadoutFinalizedEvent(CurrentTick, _plan.Round, _plan.BracketRound, p, loadout));
                }
            }

            foreach (var fight in pass.Fights)
            {
                var setup = new DuelSetup(fight.DuelId, MatchSeed, Mode, _plan.Round, _plan.BracketRound, pass.Attempt, pass.Kind, pass.Arena,
                    fight.Seats.Side0Player, fight.Seats.Side1Player, Timings.AlcoveTicks, Timings.CombatMaxTicks);
                fight.Setup = setup;
                fight.StartedTick = CurrentTick;
                _runner.StartDuel(setup);
                Emit(new DuelStartedEvent(CurrentTick, setup));
            }
        }

        private void PollDuels()
        {
            if (Phase != MatchPhase.Alcove && Phase != MatchPhase.Combat) return;
            foreach (var fight in _plan.CurrentPass.Fights)
            {
                if (fight.IsResolved) continue;
                if (!_runner.Poll(fight.DuelId, CurrentTick - fight.StartedTick, out var outcome)) continue;
                if (outcome == null) throw new InvalidOperationException("Duel " + fight.DuelId + " reported completion without an outcome.");
                string error = outcome.Validate(fight.Setup, Timings.CombatMaxTicks);
                if (error != null) throw new InvalidOperationException("Duel " + fight.DuelId + " reported an invalid outcome: " + error + ".");
                fight.Outcome = outcome;
                fight.ResolvedTick = CurrentTick;
                Emit(new DuelResolvedEvent(CurrentTick, fight.Setup, outcome));
            }
        }

        private bool AllFightsResolved()
        {
            foreach (var f in _plan.CurrentPass.Fights)
                if (!f.IsResolved) return false;
            return true;
        }

        private void ResolvePass()
        {
            EnterPhase(MatchPhase.Results, Timings.ResultsTicks);
            var pass = _plan.CurrentPass;
            var entries = new Dictionary<int, RoundResultEntry>();
            var order = new List<int>();
            foreach (var fight in pass.Fights)
            {
                var outcome = fight.Outcome;
                foreach (int player in new[] { fight.Seats.Side0Player, fight.Seats.Side1Player })
                {
                    int opponent = fight.Seats.Other(player);
                    var result = outcome.IsDraw ? RoundOutcome.Draw : outcome.Winner == player ? RoundOutcome.Win : RoundOutcome.Loss;
                    var feats = outcome.For(player).FeatIds;
                    _players[player].RecordFight(opponent, fight.Seats.SideOf(player), feats);
                    _economy.RecordCompletedDuel(new CompletedDuelRecord(player, opponent, _plan.Round, _plan.BracketRound, pass.Attempt, result, feats));
                    entries[player] = new RoundResultEntry(player, opponent, result);
                    order.Add(player);
                }

                if (outcome.IsDraw)
                {
                    _players[fight.Seats.Side0Player].Draws++;
                    _players[fight.Seats.Side1Player].Draws++;
                }
                else
                {
                    _players[outcome.Winner].CombatWins++;
                    _players[outcome.LoserOrNoPlayer].Losses++;
                }
            }

            if (pass.CarriesRound)
            {
                foreach (int bye in _plan.Byes)
                {
                    entries[bye] = new RoundResultEntry(bye, MatchRules.NoPlayer, RoundOutcome.Bye);
                    order.Add(bye);
                }
            }

            foreach (int p in order)
            {
                var entry = entries[p];
                entry.ResolveBefore = _players[p].Resolve;
                if (pass.CarriesRound && _plan.Shops.TryGetValue(p, out var shop))
                {
                    entry.BaselineIncome = shop.BaselineIncome;
                    entry.WinnerBonus = shop.WinnerBonus;
                }
            }

            var replays = new List<PlayerPair>();
            ApplyPass(_plan, pass, entries, replays);

            var list = new List<RoundResultEntry>(order.Count);
            foreach (int p in order)
            {
                entries[p].ResolveAfter = _players[p].Resolve;
                entries[p].DuelHalfWins = _players[p].DuelHalfWins;
                list.Add(entries[p]);
            }

            var roundResult = new RoundResult(CurrentTick, _plan.Round, _plan.BracketRound, pass.Attempt, pass.Kind, list);
            if (replays.Count == 0)
            {
                CompleteCurrentRound(entries);
                roundResult.RoundComplete = true;
            }
            else
            {
                foreach (var pair in replays)
                {
                    entries[pair.A].ReplayPending = true;
                    entries[pair.B].ReplayPending = true;
                }
            }

            _pendingReplays = replays;
            _roundResults.Add(roundResult);
        }

        private void CompleteCurrentRound(IDictionary<int, RoundResultEntry> entries)
        {
            // L193: only a combat win outside a draw replay earns the next shop's bonus; byes and draws reset it.
            foreach (var p in _players) p.WonPreviousCombatRound = false;
            foreach (var pass in _plan.Passes)
            {
                if (pass.Voided || pass.IsDrawReplay) continue;
                foreach (var f in pass.Fights)
                    if (f.Outcome != null && !f.Outcome.IsDraw) _players[f.Outcome.Winner].WonPreviousCombatRound = true;
            }

            foreach (int p in _plan.ActivePlayers) _players[p].PreviousRoundLoadout = _players[p].CurrentLoadout;
            CompleteRound(_plan, entries);

            _nextPlan = PlanNextRound();
            foreach (var entry in entries.Values)
            {
                bool shopsNext = _nextPlan != null && _nextPlan.ActivePlayers.Contains(entry.Player);
                entry.NextBaselineIncome = shopsNext ? GlobalRules.Economy.LaterShopIncome : 0;
                entry.NextWinnerBonus = shopsNext && _players[entry.Player].WonPreviousCombatRound ? GlobalRules.Economy.WinnerBonus : 0;
            }
        }

        private void AfterResults()
        {
            if (_pendingReplays.Count > 0)
            {
                int attempt = _plan.Passes.Count;
                var rng = PairingRng(_plan.Round, PairingStreams.Sides(attempt));
                var seats = new List<SeatedPair>(_pendingReplays.Count);
                foreach (var pair in _pendingReplays) seats.Add(Seat(pair, rng));
                _pendingReplays = new List<PlayerPair>();
                BeginPass(PassKind.DrawReplay, PassKind.DrawReplay, seats);
                return;
            }

            if (_nextPlan == null)
            {
                Finish();
                return;
            }

            BeginRound(_nextPlan);
        }

        private void Finish()
        {
            Result = BuildResult();
            EnterPhase(MatchPhase.Finished, -1);
            Emit(new MatchFinishedEvent(CurrentTick, Round, Result));
        }
    }

    /// <summary>Sub-streams of the Pairing stream for one round. Sub 0 is the recorded round seed itself.</summary>
    internal static class PairingStreams
    {
        public const int PairingTies = 0;
        public const int ByeTies = 1;
        private const int SidesBase = 2;

        /// <summary>Side ties of a pass; each attempt draws from its own sub-stream.</summary>
        public static int Sides(int attempt) => SidesBase + attempt;
    }
}
