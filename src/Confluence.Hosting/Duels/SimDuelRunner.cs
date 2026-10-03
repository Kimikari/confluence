using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Confluence.Core.Rules;
using Confluence.Hosting.Arenas;
using Confluence.Hosting.Economics;
using Confluence.Match;
using Confluence.Sim.Duel;
using MatchDuelOutcome = Confluence.Match.DuelOutcome;

namespace Confluence.Hosting.Duels
{
    /// <summary>Where the runner gets each player's finalized round loadout and reports consumable use.</summary>
    public interface IDuelLoadouts
    {
        /// <summary>The player's round loadout mapped for this duel (spawn side from the setup).</summary>
        MappedLoadout LoadoutFor(int player, DuelSetup setup);

        /// <summary>A completed duel committed consumables (L212); never called for voided duels (L427).</summary>
        void RecordConsumablesUsed(int player, int count);
    }

    public sealed class DuelRunnerOptions
    {
        /// <summary>Creates each duel world (and the replay's); the casting and reactions seams plug in here.</summary>
        public ISimWorldFactory WorldFactory { get; set; } = SimWorldFactory.Default;

        /// <summary>Record every command for replays (L419, L427).</summary>
        public bool RecordReplays { get; set; } = true;

        /// <summary>Ruleset fingerprint written into replay manifests (the transport's SHA-256 fingerprint, D-113).</summary>
        public string RulesetFingerprint { get; set; } = string.Empty;
    }

    /// <summary>
    /// Match's <see cref="IDuelRunner"/> over the Sim: one <see cref="DuelWorld"/> per pairing on its own copy of the
    /// pass's arena (L34). The world runs the duel flow itself with Match's phase lengths: the 5 s alcove ward (L46)
    /// and at most 90 s of combat (L47), ending on death, surrender, or the timer judgement (L53). One world step is one
    /// authoritative 60 Hz tick: Match polls each duel once per match tick with the ticks elapsed since the duel
    /// started, and the runner steps the world to exactly that tick (or the host pre-steps every running duel, possibly
    /// in parallel, through <see cref="StepAll"/>). World tick k is therefore Match's duel tick k, and the world's combat
    /// tick is Match's combat elapsed time. A disconnected player's source sends neutral input; nothing pauses (L55).
    /// </summary>
    public sealed class SimDuelRunner : IDuelRunner
    {
        private readonly ArenaProvider _arenas;
        private readonly IDuelLoadouts _loadouts;
        private readonly Func<int, IInputSource> _inputs;
        private readonly Dictionary<int, RunningDuel> _running = new Dictionary<int, RunningDuel>();
        private readonly List<RunningDuel> _order = new List<RunningDuel>();
        private readonly List<DuelReport> _reports = new List<DuelReport>();
        private readonly List<RunningDuel> _scratch = new List<RunningDuel>();

        public SimDuelRunner(ArenaProvider arenas, IDuelLoadouts loadouts, Func<int, IInputSource> inputs, DuelRunnerOptions options = null)
        {
            _arenas = arenas ?? throw new ArgumentNullException(nameof(arenas));
            _loadouts = loadouts ?? throw new ArgumentNullException(nameof(loadouts));
            _inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
            Options = options ?? new DuelRunnerOptions();
        }

        public DuelRunnerOptions Options { get; }

        /// <summary>Running duels in start order.</summary>
        public IReadOnlyList<RunningDuel> Running => _order.AsReadOnly();

        /// <summary>Every completed (non-voided) duel in completion order.</summary>
        public IReadOnlyList<DuelReport> Reports => _reports.AsReadOnly();

        /// <summary>Duels discarded by a void (L427).</summary>
        public int AbortedCount { get; private set; }

        /// <summary>A duel world was created (alcove start).</summary>
        public event Action<RunningDuel> Started;

        /// <summary>A duel advanced one tick: the snapshot hook for the transport (L421).</summary>
        public event Action<RunningDuel> Stepped;

        /// <summary>A duel finished and its outcome went to Match.</summary>
        public event Action<DuelReport> Finished;

        public RunningDuel Find(int duelId) => _running.TryGetValue(duelId, out RunningDuel d) ? d : null;

        /// <summary>The running duel a player fights in, or null.</summary>
        public RunningDuel FindByPlayer(int player)
        {
            foreach (RunningDuel d in _order)
                if (d.Involves(player)) return d;
            return null;
        }

        public void StartDuel(DuelSetup setup)
        {
            if (setup == null) throw new ArgumentNullException(nameof(setup));
            if (_running.ContainsKey(setup.DuelId)) throw new InvalidOperationException("Duel " + setup.DuelId + " is already running.");
            HostedArena arena = _arenas.Get(setup.Arena.ArenaId);
            MappedLoadout side0 = _loadouts.LoadoutFor(setup.Side0Player, setup);
            MappedLoadout side1 = _loadouts.LoadoutFor(setup.Side1Player, setup);
            if (side0.Setup.SpawnSide != 0 || side1.Setup.SpawnSide != 1) throw new InvalidOperationException("Loadouts must carry their duel's spawn sides.");

            DuelWorld world = Options.WorldFactory.Create(arena.CreateCopy(), side0.Setup, side1.Setup, setup.AlcoveTicks, setup.CombatMaxTicks, true);
            ReplayManifest replay = Options.RecordReplays
                ? new ReplayManifest(Options.RulesetFingerprint, setup, arena, side0.Setup, side1.Setup, setup.AlcoveTicks, setup.CombatMaxTicks)
                : null;
            var duel = new RunningDuel(setup, arena, world, side0, side1, Source(setup.Side0Player), Source(setup.Side1Player), replay);
            _running.Add(setup.DuelId, duel);
            _order.Add(duel);
            duel.Begin();
            Started?.Invoke(duel);
        }

        public bool Poll(int duelId, int duelTick, out MatchDuelOutcome outcome)
        {
            outcome = null;
            if (!_running.TryGetValue(duelId, out RunningDuel duel)) throw new InvalidOperationException("Duel " + duelId + " is not running.");
            duel.RequireAligned(duelTick);
            while (!duel.IsOver && duel.World.Tick < duelTick)
            {
                duel.Step();
                Stepped?.Invoke(duel);
            }

            if (!duel.IsOver) return false;
            outcome = Complete(duel);
            return true;
        }

        /// <summary>
        /// Advances every running duel by one tick before Match polls them (the host's alternative to stepping inside
        /// <see cref="Poll"/>). Duels are independent, so a parallel step gives the same result as a sequential one;
        /// snapshot hooks run afterwards in start order.
        /// </summary>
        public void StepAll(bool parallel)
        {
            _scratch.Clear();
            foreach (RunningDuel d in _order)
                if (!d.IsOver) _scratch.Add(d);
            if (_scratch.Count == 0) return;
            if (parallel && _scratch.Count > 1) Parallel.For(0, _scratch.Count, i => _scratch[i].Step());
            else foreach (RunningDuel d in _scratch) d.Step();
            foreach (RunningDuel d in _scratch) Stepped?.Invoke(d);
        }

        public void AbortDuel(int duelId)
        {
            if (!_running.TryGetValue(duelId, out RunningDuel duel)) return;
            Remove(duel);
            AbortedCount++;
            duel.End(null);
        }

        /// <summary>Surrenders the player's running duel (L53). False when the player is not in one or it already ended.</summary>
        public bool Surrender(int player)
        {
            RunningDuel duel = FindByPlayer(player);
            return duel != null && duel.Surrender(duel.SideOf(player));
        }

        private IInputSource Source(int player) => _inputs(player) ?? IdleInputSource.Instance;

        private MatchDuelOutcome Complete(RunningDuel duel)
        {
            DuelWorld world = duel.World;
            DuelResult result = world.Result;
            MatchDuelOutcome outcome = DuelOutcomeMapper.Map(duel.Setup, result, world.CombatTick);
            ulong hash = world.StateHash();
            if (duel.Replay != null)
            {
                duel.Replay.FinalTick = world.Tick;
                duel.Replay.FinalStateHash = hash;
                duel.Replay.IsComplete = true;
            }

            var sides = new DuelSideReport[DuelWorld.PlayerCount];
            for (int i = 0; i < sides.Length; i++)
            {
                int player = i == 0 ? duel.Setup.Side0Player : duel.Setup.Side1Player;
                sides[i] = new DuelSideReport(player, i, world.Player(i).Setup.WizardId, outcome.For(player).HealthFraction,
                    result.CompletedFeats[i], result.DecisiveSequences[i], duel.ConsumablesUsed(i), world.Player(i).Feats.DamageDealt);
                if (duel.ConsumablesUsed(i) > 0) _loadouts.RecordConsumablesUsed(player, duel.ConsumablesUsed(i));
            }

            var report = new DuelReport(duel.Setup, outcome, result, world.Tick, hash, Array.AsReadOnly(sides), duel.Replay);
            Remove(duel);
            _reports.Add(report);
            duel.End(report);
            Finished?.Invoke(report);
            return outcome;
        }

        private void Remove(RunningDuel duel)
        {
            _running.Remove(duel.DuelId);
            _order.Remove(duel);
        }
    }
}
