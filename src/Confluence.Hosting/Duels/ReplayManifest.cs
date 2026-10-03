using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Confluence.Core.Random;
using Confluence.Core.Rules;
using Confluence.Hosting.Arenas;
using Confluence.Match;
using Confluence.Sim.Input;
using Confluence.Sim.Players;

namespace Confluence.Hosting.Duels
{
    /// <summary>
    /// Consecutive commands of one player that differ at most in their stamps, run-length encoded: element k of the run
    /// is <see cref="Input"/> with <c>Tick</c> and <c>Sequence</c> advanced by <c>k * Step</c>. Step 0 is a repeated
    /// command (a network source re-sending the latest input); step 1 is a fresh command every tick with unchanged
    /// controls (a bot standing still, a client holding a button).
    /// </summary>
    public struct InputRun
    {
        public PlayerInput Input;
        public int Count;
        public int Step;

        /// <summary>Element <paramref name="k"/> of the run.</summary>
        public PlayerInput At(int k)
        {
            PlayerInput input = Input;
            input.Tick += k * Step;
            input.Sequence = unchecked(input.Sequence + (uint)(k * Step));
            return input;
        }
    }

    /// <summary>One player's per-tick command stream of a duel (every step, alcove included).</summary>
    public sealed class InputTrack
    {
        private readonly List<InputRun> _runs = new List<InputRun>();

        public IReadOnlyList<InputRun> Runs => _runs.AsReadOnly();

        /// <summary>Recorded steps.</summary>
        public int Length { get; private set; }

        public void Append(in PlayerInput input)
        {
            Length++;
            int last = _runs.Count - 1;
            if (last >= 0 && SameControls(_runs[last].Input, input))
            {
                InputRun run = _runs[last];
                PlayerInput previous = run.At(run.Count - 1);
                int step = input.Tick - previous.Tick;
                bool stampsAdvance = unchecked(input.Sequence - previous.Sequence) == unchecked((uint)step);
                bool extends = stampsAdvance && (run.Count == 1 ? step == 0 || step == 1 : step == run.Step);
                if (extends)
                {
                    run.Step = step;
                    run.Count++;
                    _runs[last] = run;
                    return;
                }
            }

            _runs.Add(new InputRun { Input = input, Count = 1 });
        }

        /// <summary>The commands in step order, exactly as recorded.</summary>
        public IEnumerable<PlayerInput> Expand()
        {
            foreach (InputRun run in _runs)
                for (int k = 0; k < run.Count; k++) yield return run.At(k);
        }

        /// <summary>Equal in everything except the <c>Tick</c> and <c>Sequence</c> stamps.</summary>
        private static bool SameControls(in PlayerInput a, in PlayerInput b) =>
            a.MoveX.Equals(b.MoveX) && a.MoveY.Equals(b.MoveY) && a.YawDeg.Equals(b.YawDeg) && a.PitchDeg.Equals(b.PitchDeg) &&
            a.Buttons == b.Buttons && a.SelectedSlot == b.SelectedSlot && a.ViewTick == b.ViewTick;
    }

    public enum ReplayActionKind
    {
        /// <summary><c>DuelWorld.Surrender</c> for the side, applied before the step (L53).</summary>
        Surrender,
    }

    /// <summary>A non-input authoritative action applied between steps.</summary>
    public struct ReplayAction
    {
        /// <summary>Applied before simulating this tick (world tick + 1 at the time of the action).</summary>
        public int BeforeTick;

        /// <summary>World index (= spawn side) of the acting player.</summary>
        public int Side;

        public ReplayActionKind Kind;
    }

    /// <summary>
    /// What reproduces one duel exactly (spec L419 "a recorded seed plus authoritative event stream"; L427 "ruleset hash,
    /// content definitions, arena seed, spawn assignment, and relevant event history"; L433 ReplayManifest): the ruleset
    /// fingerprint, the arena recipe and layout hash, the spawn assignment, both players' duel setups and the content ids
    /// they reference, the phase lengths, every per-tick command, between-step actions, and the final tick and state hash
    /// to verify against.
    /// </summary>
    public sealed class ReplayManifest
    {
        public const string FormatVersion = "confluence.replay.duel.v1";

        internal ReplayManifest(string rulesetFingerprint, DuelSetup setup, HostedArena arena, DuelPlayerSetup side0, DuelPlayerSetup side1,
            int alcoveTicks, int combatTicks)
        {
            RulesetFingerprint = rulesetFingerprint ?? string.Empty;
            MatchSeed = setup.MatchSeed;
            Mode = setup.Mode;
            DuelId = setup.DuelId;
            Round = setup.Round;
            BracketRound = setup.BracketRound;
            Attempt = setup.Attempt;
            PassKind = setup.PassKind;
            Arena = arena.Recipe;
            ArenaId = arena.ArenaId;
            ArenaLayoutHash = arena.LayoutHash;
            Side0Player = setup.Side0Player;
            Side1Player = setup.Side1Player;
            Setups = Array.AsReadOnly(new[] { CloneSetup(side0), CloneSetup(side1) });
            ContentIds = CollectContentIds(Setups, arena);
            AlcoveTicks = alcoveTicks;
            CombatTicks = combatTicks;
            Inputs = Array.AsReadOnly(new[] { new InputTrack(), new InputTrack() });
        }

        public string Format => FormatVersion;

        /// <summary>Ruleset fingerprint the duel ran under; an incompatible build must refuse the replay (L427).</summary>
        public string RulesetFingerprint { get; }

        public ulong MatchSeed { get; }
        public MatchMode Mode { get; }
        public int DuelId { get; }
        public int Round { get; }
        public int BracketRound { get; }
        public int Attempt { get; }
        public PassKind PassKind { get; }

        public ArenaRecipe Arena { get; }
        public string ArenaId { get; }
        public ulong ArenaLayoutHash { get; }

        /// <summary>Spawn assignment: the match player on side 0 and on side 1 (L59).</summary>
        public int Side0Player { get; }
        public int Side1Player { get; }

        /// <summary>Duel setups by side (copies; wizard, spells, equipment, augments, enabled active).</summary>
        public IReadOnlyList<DuelPlayerSetup> Setups { get; }

        /// <summary>Every catalogue id the duel depends on, ordinal order.</summary>
        public IReadOnlyList<string> ContentIds { get; }

        public int AlcoveTicks { get; }
        public int CombatTicks { get; }

        /// <summary>Per-tick commands by side.</summary>
        public IReadOnlyList<InputTrack> Inputs { get; }

        public IReadOnlyList<ReplayAction> Actions => _actions.AsReadOnly();

        public int FinalTick { get; internal set; }
        public ulong FinalStateHash { get; internal set; }

        public bool IsComplete { get; internal set; }

        private readonly List<ReplayAction> _actions = new List<ReplayAction>();

        internal void RecordStep(in PlayerInput side0, in PlayerInput side1)
        {
            Inputs[0].Append(side0);
            Inputs[1].Append(side1);
        }

        internal void RecordAction(ReplayAction action) => _actions.Add(action);

        /// <summary>Stable text of the header (everything except the input stream) for logs and fingerprints.</summary>
        public string HeaderText()
        {
            var sb = new StringBuilder(FormatVersion);
            sb.Append(";ruleset=").Append(RulesetFingerprint);
            sb.Append(";match=").Append(MatchSeed.ToString("X16", CultureInfo.InvariantCulture));
            sb.Append(";mode=").Append(Mode).Append(";duel=").Append(DuelId).Append(";round=").Append(Round);
            sb.Append(";bracket=").Append(BracketRound).Append(";attempt=").Append(Attempt).Append(";pass=").Append(PassKind);
            sb.Append(";arena=").Append(Arena.CanonicalText()).Append(";layout=").Append(ArenaLayoutHash.ToString("X16", CultureInfo.InvariantCulture));
            sb.Append(";sides=").Append(Side0Player).Append(',').Append(Side1Player);
            sb.Append(";content=").Append(string.Join(",", ContentIds));
            sb.Append(";alcove=").Append(AlcoveTicks).Append(";combat=").Append(CombatTicks);
            sb.Append(";final=").Append(FinalTick).Append(':').Append(FinalStateHash.ToString("X16", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        public ulong HeaderFingerprint() => SeedHash.Of(HeaderText());

        internal static DuelPlayerSetup CloneSetup(DuelPlayerSetup source)
        {
            var copy = new DuelPlayerSetup
            {
                PlayerId = source.PlayerId,
                WizardId = source.WizardId,
                EnabledActiveItemId = source.EnabledActiveItemId,
                SpawnSide = source.SpawnSide,
            };
            copy.EquippedSpellIds.AddRange(source.EquippedSpellIds);
            copy.Equipment.AddRange(source.Equipment);     // EquippedItem is immutable
            copy.Augments.AddRange(source.Augments);
            copy.Modifiers.AddRange(source.Modifiers);
            copy.ReserveShields.AddRange(source.ReserveShields);
            return copy;
        }

        private static IReadOnlyList<string> CollectContentIds(IReadOnlyList<DuelPlayerSetup> setups, HostedArena arena)
        {
            var ids = new SortedSet<string>(StringComparer.Ordinal);
            foreach (DuelPlayerSetup s in setups)
            {
                if (s.WizardId != null) ids.Add(s.WizardId);
                foreach (string spell in s.EquippedSpellIds) ids.Add(spell);
                foreach (EquippedItem item in s.Equipment) ids.Add(item.Id);
                foreach (ActiveAugment augment in s.Augments) ids.Add(augment.Id);
            }

            if (arena.BiomeId != null) ids.Add(arena.BiomeId);
            foreach (string id in arena.PublicPreview.MajorTerrainIds) ids.Add(id);
            foreach (string id in arena.PublicPreview.PropIds) ids.Add(id);
            return new List<string>(ids).AsReadOnly();
        }
    }
}
