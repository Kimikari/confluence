using System;
using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Casting.Grammars;
using Confluence.Core.Content;
using Confluence.Net.Game.Snapshots;
using Confluence.Net.Game.Wire;
using Confluence.Sim.Content;
using Confluence.Sim.Input;
using Confluence.Sim.Movement;
using Confluence.Sim.Spells;
using Confluence.Sim.Status;

namespace Confluence.Net.Game.Client
{
    /// <summary>
    /// Local prediction of the own preparation for the HUD (spec L419 "clients predict local movement and preparation",
    /// L472 the grammar overlay): the same grammar and cast machine the server runs, fed the same canonical inputs, with a
    /// host that reads mana, cooldowns, and control state from the latest private snapshot. It never decides anything:
    /// commitment, mana, cooldowns, and mastery outcomes come from the server's cast events and snapshots, and when the
    /// authoritative cast phase keeps disagreeing with the prediction the machine is rebuilt (<see cref="Resyncs"/>).
    /// </summary>
    public sealed class GrammarPredictor : ICastHost
    {
        private const int MismatchSnapshots = 3;
        private const int HistorySize = 256;

        private readonly string _wizardId;
        private readonly int _player;
        private readonly CastableSpell[] _castables;
        private readonly CastableSpell _signature;
        private readonly CastPhase[] _phaseAt = new CastPhase[HistorySize];
        private readonly int[] _phaseTick = new int[HistorySize];
        private readonly Dictionary<string, int> _localReady = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<CastEvent> _events = new List<CastEvent>();
        private CastMachine _machine;
        private PrivateEntry _context;
        private int _contextTick;
        private float _mana;
        private int _tick;
        private bool _silenced;
        private bool _hardControlled;
        private bool _warded;
        private InputButtons _previousHeld;
        private int _mismatches;

        public GrammarPredictor(Catalogue catalogue, ContentRegistry registry, DuelSideWire side, int player)
        {
            _wizardId = side?.WizardId;
            _player = player;
            registry = registry ?? ContentRegistry.Default;
            var castables = new List<CastableSpell>();
            if (catalogue != null && side != null)
            {
                foreach (string spellId in side.SpellIds)
                {
                    if (castables.Count >= 6) break;
                    castables.Add(Resolve(catalogue, registry, side, spellId)?.Castable);
                }

                if (_wizardId != null && registry.TryGetKit(_wizardId, out IWizardKitBehavior kit))
                {
                    try
                    {
                        SpellFormBuilder b = SpellFormBuilder.ForSignature(_wizardId);
                        b.Parameters.AddRange(kit.Parameters);
                        kit.BuildSignature(b);
                        _signature = b.Build().Castable;
                    }
                    catch (Exception)
                    {
                        _signature = null; // prediction only: a kit the client cannot rebuild just predicts no signature
                    }
                }
            }

            _castables = castables.ToArray();
            Rebuild();
        }

        /// <summary>A grammar exists for the wizard (otherwise the HUD shows only the server's state).</summary>
        public bool Available => _machine != null;

        public CastPhase Phase => _machine != null ? _machine.Phase : CastPhase.Idle;

        /// <summary>The predicted grammar overlay state.</summary>
        public GrammarUiState Ui => _machine != null ? _machine.GrammarUi : GrammarUiState.Inactive;

        /// <summary>Times the prediction was discarded after disagreeing with the server.</summary>
        public int Resyncs { get; private set; }

        /// <summary>Cast events the prediction produced on the last step (HUD feedback; never sent).</summary>
        public IReadOnlyList<CastEvent> LastEvents => _events;

        /// <summary>Latest authoritative context: resources, cooldowns, controls.</summary>
        public void SetContext(PrivateEntry own, PlayerEntry pub, int snapshotTick, bool warded)
        {
            _context = own;
            _contextTick = snapshotTick;
            if (own != null) _mana = own.Mana;
            _warded = warded;
            _silenced = Has(pub, StatusKind.Silence);
            _hardControlled = Has(pub, StatusKind.Stun) || Has(pub, StatusKind.Displaced);
        }

        /// <summary>Feeds one canonical command (the one sent for <paramref name="tick"/>).</summary>
        public void Step(int tick, in PlayerInput input, MotorState motor)
        {
            if (_machine == null) return;
            _tick = tick;
            InputButtons held = (input.Buttons & ~InputButtons.MovementKeys) | PlayerInput.MovementKeysFromAxes(input.MoveX, input.MoveY);
            held &= InputButtons.All;
            InputButtons tapped = input.Tapped & ~held & InputButtons.All;
            CastInputFrame frame = CastInputFrame.FromHeld(tick, _previousHeld, held);
            frame.Pressed |= tapped & ~_previousHeld;
            frame.Released |= tapped;
            frame.SelectSlot = input.SelectedSlot;
            frame.AimYawDeg = input.YawDeg;
            frame.AimPitchDeg = input.PitchDeg;
            frame.Position = motor.Position;
            frame.Velocity = motor.Velocity;
            frame.Grounded = motor.Grounded;
            frame.InputEvents = input.GrammarEvents;
            _previousHeld = held;
            _events.Clear();
            try
            {
                _machine.Step(frame, _events);
            }
            catch (Exception)
            {
                Rebuild(); // prediction must never break the client loop
            }

            int slot = ((tick % HistorySize) + HistorySize) % HistorySize;
            _phaseAt[slot] = _machine.Phase;
            _phaseTick[slot] = tick;
        }

        /// <summary>
        /// Compares the authoritative cast phase at the server tick that consumed the command of local tick
        /// <paramref name="localTick"/>; repeated disagreement on whether a cast is in progress rebuilds the prediction.
        /// </summary>
        public void Reconcile(int localTick, CastPhase authoritative)
        {
            if (_machine == null || localTick <= 0) return;
            int slot = ((localTick % HistorySize) + HistorySize) % HistorySize;
            if (_phaseTick[slot] != localTick) return;
            bool predictedBusy = _phaseAt[slot] != CastPhase.Idle;
            bool serverBusy = authoritative != CastPhase.Idle;
            if (predictedBusy == serverBusy)
            {
                _mismatches = 0;
                return;
            }

            if (++_mismatches < MismatchSnapshots) return;
            _mismatches = 0;
            Resyncs++;
            Rebuild();
        }

        private void Rebuild()
        {
            IGrammar grammar = _wizardId != null ? CatalogueLoadouts.DefaultGrammar(_wizardId) : null;
            _machine = grammar != null ? new CastMachine(_player, this, grammar, _castables, _signature) : null;
            _localReady.Clear();
            _previousHeld = InputButtons.None;
        }

        private static SpellForm Resolve(Catalogue catalogue, ContentRegistry registry, DuelSideWire side, string spellId)
        {
            if (spellId == null || !registry.TryGetSpell(spellId, out ISpellBehavior behavior)) return null;
            if (!catalogue.TryGetSpell(spellId, out SpellDef def)) return null;
            var transforms = new List<ActiveTransform>();
            for (int i = 0; i < side.AugmentIds.Count; i++)
            {
                string id = side.AugmentIds[i];
                if (!catalogue.AugmentsById.TryGetValue(id ?? string.Empty, out AugmentDef aug) || !aug.IsTransformation) continue;
                if (!string.Equals(aug.TargetSpellId, spellId, StringComparison.Ordinal)) continue;
                if (!registry.TryGetTransform(id, out ISpellTransform transform)) continue;
                transforms.Add(new ActiveTransform(id, Math.Max(1, Math.Min(2, side.AugmentRanks[i])), transform, aug.ExclusiveGroup));
            }

            try
            {
                return SpellFormResolver.Resolve(def, behavior, transforms);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool Has(PlayerEntry p, StatusKind kind)
        {
            if (p == null) return false;
            foreach (StatusWire s in p.Statuses)
                if (s.Kind == (byte)kind) return true;
            return false;
        }

        // ---- ICastHost over the latest authoritative context ------------------------------------------------------

        public float Mana => _mana;

        public bool TryPayMana(float amount)
        {
            if (amount > _mana) return false;
            _mana -= amount;
            return true;
        }

        public bool TryPayCommitment(CastableSpell spell, float amount) => TryPayMana(amount);

        public void RefundMana(float amount) => _mana += Math.Max(0f, amount);

        public int CooldownRemainingTicks(string spellId)
        {
            if (spellId == null) return 0;
            if (_localReady.TryGetValue(spellId, out int ready) && ready > _tick) return ready - _tick;
            if (_context == null) return 0;
            for (int i = 0; i < _castables.Length && i < _context.CooldownTicks.Length; i++)
            {
                if (_castables[i] != null && _castables[i].Id == spellId)
                {
                    return Math.Max(0, _context.CooldownTicks[i] - Math.Max(0, _tick - _contextTick));
                }
            }

            return 0;
        }

        public void StartCooldown(CastableSpell spell)
        {
            if (spell != null) _localReady[spell.Id] = _tick + spell.CooldownTicks;
        }

        public CastModifiers ModifiersFor(CastableSpell spell) => default;

        public bool IsSilenced => _silenced;

        public bool IsHardControlled => _hardControlled;

        public bool IsWarded => _warded;

        public bool IsActionLocked => false;
    }
}
