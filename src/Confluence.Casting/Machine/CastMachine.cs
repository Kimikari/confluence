using System;
using System.Collections.Generic;
using Confluence.Casting.Grammars;
using Confluence.Core.Rules;

namespace Confluence.Casting
{
    /// <summary>
    /// One player's cast phase machine (spec L86-L96, L472): Idle → Preparation (grammar-driven, unpaid) →
    /// commitment (mana paid, cooldown started) → Windup (intrinsic cast_time) → release → optional Channel →
    /// Recovery → Idle. It enforces cancellation, interruption, refund, recast, cue, and movement rules and emits
    /// typed <see cref="CastEvent"/>s. Server and predicting client run it on the same input frames.
    /// <para>
    /// Per-tick order inside <see cref="Step"/>: grammar observation and recast expiry; forced interruption from
    /// host status; spell selection; common-action cancellation; phase timers (release, channel, recovery);
    /// recast input; signature activation; opening a preparation; advancing the preparation.
    /// </para>
    /// </summary>
    public sealed class CastMachine
    {
        private const string InterruptRefundKey = "interrupt";

        private readonly int _player;
        private readonly ICastHost _host;
        private readonly IGrammar _grammar;
        private readonly CastableSpell[] _slots;
        private readonly CastableSpell _signature;
        private readonly PendingRecast[] _recasts;
        private readonly RefundLedger _refunds = new RefundLedger();

        private int _sequence;
        private int _selected;
        private int _lockUntilTick = int.MinValue;

        // The current attempt.
        private CastPhase _phase;
        private CastableSpell _spell;
        private int _spellSlot;
        private bool _usesGrammar;
        private CastEventId _castId;
        private int _preparationStartTick;
        private int _phaseEndTick;
        private int _releaseTick;
        private int _channelPulses;
        private MasteryResult _mastery;
        private float _paid;
        private int _recoveryTicks; // the committed cast's recovery after timing modifiers (CastModifiers)

        // A commit held back so the anticipatory cue lasts its minimum (spec L96).
        private bool _commitHeld;
        private int _heldCommitTick;
        private MasteryResult _heldMastery;
        private bool _heldAuto;

        public CastMachine(int player, ICastHost host, IGrammar grammar, IReadOnlyList<CastableSpell> equipped, CastableSpell signature = null)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (grammar == null) throw new ArgumentNullException(nameof(grammar));
            if (equipped == null) throw new ArgumentNullException(nameof(equipped));
            if (equipped.Count > GlobalRules.Loadout.EquippedSpellSlots)
            {
                throw new ArgumentException("At most " + GlobalRules.Loadout.EquippedSpellSlots + " equipped spells (spec L292).", nameof(equipped));
            }

            if (signature != null && !signature.IsSignature) throw new ArgumentException("Not a signature.", nameof(signature));
            _player = player;
            _host = host;
            _grammar = grammar;
            _slots = new CastableSpell[equipped.Count];
            for (int i = 0; i < equipped.Count; i++) _slots[i] = equipped[i];
            _signature = signature;
            _recasts = new PendingRecast[_slots.Length];
        }

        public int Player => _player;
        public IGrammar Grammar => _grammar;
        public CastPhase Phase => _phase;

        public int SlotCount => _slots.Length;
        public CastableSpell SpellInSlot(int slot) => slot >= 0 && slot < _slots.Length ? _slots[slot] : null;

        public int SelectedSlot => _selected;
        public CastableSpell SelectedSpell => SpellInSlot(_selected);

        /// <summary>The spell of the current attempt (Preparation through Recovery), or null when idle.</summary>
        public CastableSpell CurrentSpell => _phase == CastPhase.Idle ? null : _spell;

        public CastEventId CurrentCastId => _phase == CastPhase.Idle ? CastEventId.None : _castId;

        /// <summary>The committed cast's one mastery result (Windup onward).</summary>
        public MasteryResult CurrentMastery => _mastery;

        /// <summary>Mana paid by the current committed cast.</summary>
        public float CurrentPaidMana => _paid;

        public int PreparationStartTick => _preparationStartTick;
        public int PhaseEndTick => _phaseEndTick;
        public int ReleaseTick => _releaseTick;
        public bool IsChanneling => _phase == CastPhase.Channel;

        /// <summary>Total preparation ticks the player has been visibly exposed this duel, including cancelled ones (spec L90).</summary>
        public int TotalExposedPreparationTicks { get; private set; }

        public bool IsRecastLocked(int tick) => tick < _lockUntilTick;

        /// <summary>Fraction of walk speed allowed this tick: the casting factor from commitment through channel (spec L92).</summary>
        public float MoveSpeedFactor =>
            _phase == CastPhase.Windup || _phase == CastPhase.Channel ? _spell.CastingMoveSpeedFactor : CastingRules.PreparationMoveSpeedFactor;

        /// <summary>
        /// Sprint is disallowed while a non-quick or channeled cast is prepared, winding up, or channeling (spec L80:
        /// sprint "disallows channeled beams" and stays compatible only with quick casts). The simulation ends or
        /// refuses sprint while this is true; the cast itself is never refused for sprinting.
        /// </summary>
        public bool SuppressesSprint =>
            (_phase == CastPhase.Preparation || _phase == CastPhase.Windup || _phase == CastPhase.Channel) && !_spell.IsSprintCompatible;

        /// <summary>HUD state of the open preparation (inactive otherwise).</summary>
        public GrammarUiState GrammarUi
        {
            get
            {
                if (_phase != CastPhase.Preparation || !_usesGrammar) return GrammarUiState.Inactive;
                var ui = _grammar.Ui;
                if (_commitHeld)
                {
                    ui.Queued = true;
                    ui.QueuedCommitTick = _heldCommitTick - _preparationStartTick;
                }

                return ui;
            }
        }

        public PendingRecast PendingRecastFor(int slot) => slot >= 0 && slot < _recasts.Length ? _recasts[slot] : null;

        /// <summary>Advances the machine by one tick using that tick's input frame.</summary>
        public void Step(in CastInputFrame frame, List<CastEvent> events)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            int tick = frame.Tick;
            _grammar.Observe(frame);
            ExpireRecasts(tick, events);

            ApplyForcedInterruption(tick, events);
            ApplySelection(frame, events);
            ApplyCommonActions(frame, events);
            AdvanceTimers(tick, events);

            if (frame.WasPressed(InputButtons.Recast)) TryRecast(tick, events);
            if (frame.WasPressed(InputButtons.Signature) && _signature != null && _phase == CastPhase.Idle) TryBeginSignature(frame, events);
            if (_phase == CastPhase.Idle) TryBeginPreparation(frame, events);
            if (_phase == CastPhase.Preparation) AdvancePreparation(frame, events);
        }

        /// <summary>
        /// Interrupts the current attempt for a simulation-side reason (spell-specific rules, duel end). An open
        /// preparation is cancelled at no cost; a committed windup or channel is interrupted under spec L90.
        /// </summary>
        public bool Interrupt(InterruptCause cause, int tick, List<CastEvent> events)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            switch (_phase)
            {
                case CastPhase.Preparation:
                    CancelPreparation(CancelReasonFor(cause), tick, events);
                    return true;
                case CastPhase.Windup:
                case CastPhase.Channel:
                    InterruptCommitted(cause, tick, events);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Closes a pending recast whose original effect ended (spec L94 "never after that effect has ended").</summary>
        public bool EndRecastWindow(CastEventId castId, int tick, List<CastEvent> events)
        {
            for (int i = 0; i < _recasts.Length; i++)
            {
                if (_recasts[i] != null && _recasts[i].CastId == castId)
                {
                    CloseRecast(i, tick, events);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// A refund effect returning mana for an earlier committed cast. Every refund for one cast shares the 25% cap
        /// and each effect key fires once per cast (spec L90, L124). Returns the amount granted.
        /// </summary>
        public float TryRefund(CastEventId castId, string effectKey, float amount, int tick, List<CastEvent> events)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            float granted = _refunds.Grant(castId, effectKey, amount);
            if (granted > 0f)
            {
                _host.RefundMana(granted);
                events.Add(new CastEvent(CastEventKind.Refunded, tick, castId, null, -1, default, CancelReason.None, InterruptCause.None,
                    ChannelEndReason.Completed, RejectReason.None, granted, 0, 0, false));
            }

            return granted;
        }

        public float RefundRemaining(CastEventId castId) => _refunds.Remaining(castId);

        /// <summary>
        /// Lengthens the recovery of the cast that is recovering now by a record-stated conditional amount (SPL-074 "a
        /// miss leaves 0.3 s recovery"; spec L92: a record may state a longer recovery, never a shorter one). Returns
        /// false (nothing changes) when that cast is not the one in recovery.
        /// </summary>
        public bool ExtendRecovery(CastEventId castId, int addedTicks, int tick)
        {
            if (_phase != CastPhase.Recovery || _castId != castId || addedTicks <= 0) return false;
            _phaseEndTick = Math.Max(_phaseEndTick, tick) + addedTicks;
            return true;
        }

        /// <summary>
        /// Order-dependent digest of the machine's authoritative state (phase, attempt, timers, payment, held commit,
        /// re-cast lock, pending recasts) for the simulation state hash (spec L427). Grammar state is derived from the
        /// same input frames and is not included.
        /// </summary>
        public ulong StateDigest()
        {
            ulong h = 14695981039346656037UL;
            h = Mix(h, (ulong)_phase);
            h = Mix(h, (ulong)_castId.Packed);
            h = Mix(h, (ulong)(uint)_sequence);
            h = Mix(h, (ulong)(uint)_selected);
            h = Mix(h, (ulong)(uint)_spellSlot);
            h = Mix(h, (ulong)(uint)_lockUntilTick);
            h = Mix(h, (ulong)(uint)_preparationStartTick);
            h = Mix(h, (ulong)(uint)_phaseEndTick);
            h = Mix(h, (ulong)(uint)_releaseTick);
            h = Mix(h, (ulong)(uint)_channelPulses);
            h = Mix(h, (ulong)(uint)BitConverter.SingleToInt32Bits(_paid));
            h = Mix(h, (ulong)(uint)_recoveryTicks);
            h = Mix(h, (ulong)_mastery.Reward);
            h = Mix(h, (ulong)(uint)BitConverter.SingleToInt32Bits(_mastery.Magnitude));
            h = Mix(h, _commitHeld ? 1UL : 0UL);
            h = Mix(h, (ulong)(uint)_heldCommitTick);
            h = Mix(h, (ulong)(uint)TotalExposedPreparationTicks);
            for (int i = 0; i < _recasts.Length; i++)
            {
                PendingRecast r = _recasts[i];
                h = Mix(h, r == null ? 0UL : (ulong)r.CastId.Packed);
                h = Mix(h, r == null ? 0UL : (ulong)(uint)r.Used);
            }

            return h;
        }

        private static ulong Mix(ulong h, ulong value)
        {
            unchecked
            {
                h ^= value + 0x9E3779B97F4A7C15UL + (h << 6) + (h >> 2);
                return h * 1099511628211UL;
            }
        }

        // ---- Step stages -------------------------------------------------------------------------------------

        private void ApplyForcedInterruption(int tick, List<CastEvent> events)
        {
            if (_phase == CastPhase.Preparation)
            {
                CancelReason reason = _host.IsWarded ? CancelReason.Warded
                    : _host.IsSilenced ? CancelReason.Silenced
                    : _host.IsHardControlled ? CancelReason.HardControlled
                    : CancelReason.None;
                if (reason != CancelReason.None) CancelPreparation(reason, tick, events);
            }
            else if (_phase == CastPhase.Windup || _phase == CastPhase.Channel)
            {
                if (_host.IsSilenced) InterruptCommitted(InterruptCause.Silence, tick, events);
                else if (_host.IsHardControlled) InterruptCommitted(InterruptCause.HardControl, tick, events);
            }
        }

        private void ApplySelection(in CastInputFrame frame, List<CastEvent> events)
        {
            int slot = frame.SelectSlot;
            if (slot == CastInputFrame.NoSelection || slot == _selected || slot < 0 || slot >= _slots.Length) return;
            _selected = slot;

            // Changing the selected spell has no swap windup (spec L106), but it ends an open preparation of the
            // previous spell; a committed cast continues (the selected spell remains the selected spell, spec L470).
            if (_phase == CastPhase.Preparation && !_spell.IsSignature) CancelPreparation(CancelReason.SelectionChanged, frame.Tick, events);
        }

        private void ApplyCommonActions(in CastInputFrame frame, List<CastEvent> events)
        {
            bool action = frame.WasPressed(CastingRules.CancellingActions);
            switch (_phase)
            {
                case CastPhase.Preparation:
                    if (action) CancelPreparation(CancelReason.CommonAction, frame.Tick, events);
                    break;
                case CastPhase.Windup:
                    if (action) CancelCommitted(frame.Tick, events);
                    break;
                case CastPhase.Channel:
                    // A channel stops on any simulation step the caster chooses (spec L92).
                    if (action || frame.WasPressed(InputButtons.Primary)) EndChannel(ChannelEndReason.Cancelled, frame.Tick, events);
                    break;
            }
        }

        private void AdvanceTimers(int tick, List<CastEvent> events)
        {
            if (_phase == CastPhase.Windup && tick >= _phaseEndTick) Release(tick, events);

            if (_phase == CastPhase.Channel)
            {
                // Pulses fall on the ticks after release; the final pulse lands on the channel's last tick.
                int elapsed = tick - _releaseTick;
                while (_channelPulses < _spell.ChannelMaxTicks / _spell.ChannelPulseTicks && (_channelPulses + 1) * _spell.ChannelPulseTicks <= elapsed)
                {
                    _channelPulses++;
                    events.Add(Event(CastEventKind.ChannelPulse, tick, count: _channelPulses));
                }

                if (tick >= _phaseEndTick) EndChannel(ChannelEndReason.Completed, tick, events);
            }

            if (_phase == CastPhase.Recovery && tick >= _phaseEndTick)
            {
                events.Add(Event(CastEventKind.RecoveryEnded, tick));
                _phase = CastPhase.Idle;
            }
        }

        // ---- Preparation -------------------------------------------------------------------------------------

        private void TryBeginPreparation(in CastInputFrame frame, List<CastEvent> events)
        {
            var spell = SelectedSpell;
            if (spell == null || !_grammar.WantsToBegin(frame, spell)) return;

            // A common action pressed on the same tick wins; guard held blocks new preparations (spec L474, L104).
            if (frame.WasPressed(CastingRules.CancellingActions) || (frame.Held & CastingRules.BlockingHeldActions) != 0) return;

            RejectReason reject = BeginRejection(spell, frame.Tick);
            if (reject != RejectReason.None)
            {
                events.Add(Rejection(frame.Tick, spell, _selected, reject));
                return;
            }

            OpenAttempt(spell, _selected, true, frame.Tick, events);
            _grammar.Begin(frame, Context(frame.Tick));
        }

        private void TryBeginSignature(in CastInputFrame frame, List<CastEvent> events)
        {
            if (frame.WasPressed(CastingRules.CancellingActions) || (frame.Held & CastingRules.BlockingHeldActions) != 0) return;
            RejectReason reject = BeginRejection(_signature, frame.Tick);
            if (reject != RejectReason.None)
            {
                events.Add(Rejection(frame.Tick, _signature, -1, reject));
                return;
            }

            // Signatures use their listed activation and windup with no grammar puzzle (spec L472).
            OpenAttempt(_signature, -1, false, frame.Tick, events);
            RequestCommit(MasteryResult.Normal(MissReason.NotApplicable), false, frame.Tick, events);
        }

        private RejectReason BeginRejection(CastableSpell spell, int tick)
        {
            if (_host.IsWarded) return RejectReason.Warded;
            if (_host.IsSilenced) return RejectReason.Silenced;
            if (_host.IsHardControlled) return RejectReason.HardControlled;
            if (_host.IsActionLocked) return RejectReason.ActionLocked;
            if (IsRecastLocked(tick)) return RejectReason.RecastLocked;
            if (_host.CooldownRemainingTicks(spell.Id) > 0) return RejectReason.CooldownNotReady;
            return RejectReason.None;
        }

        private void OpenAttempt(CastableSpell spell, int slot, bool usesGrammar, int tick, List<CastEvent> events)
        {
            _sequence++;
            _castId = new CastEventId(_player, _sequence);
            _spell = spell;
            _spellSlot = slot;
            _usesGrammar = usesGrammar;
            _preparationStartTick = tick;
            _phase = CastPhase.Preparation;
            _mastery = default;
            _paid = 0f;
            _commitHeld = false;
            _channelPulses = 0;
            events.Add(Event(CastEventKind.PreparationStarted, tick));
            if (spell.RequiresAnticipatoryCue) events.Add(Event(CastEventKind.CueStarted, tick, ticks: spell.MinimumCueTicks));
        }

        private void AdvancePreparation(in CastInputFrame frame, List<CastEvent> events)
        {
            int tick = frame.Tick;
            if (_commitHeld)
            {
                if (tick >= _heldCommitTick) Commit(_heldMastery, _heldAuto, tick, events);
                return;
            }

            GrammarStep step = _grammar.Advance(frame, Context(tick));
            switch (step.Kind)
            {
                case GrammarStepKind.Commit:
                    RequestCommit(step.Mastery, false, tick, events);
                    break;
                case GrammarStepKind.AutoCommit:
                    RequestCommit(step.Mastery, true, tick, events);
                    break;
                case GrammarStepKind.Cancel:
                    CancelPreparation(CancelReason.Grammar, tick, events);
                    break;
            }
        }

        /// <summary>
        /// Commits now, or holds the commit inside preparation until the anticipatory cue has lasted its minimum
        /// before release (spec L96: the cue is part of the listed preparation, never a secret delay after commitment).
        /// </summary>
        private void RequestCommit(MasteryResult mastery, bool auto, int tick, List<CastEvent> events)
        {
            int earliestCommit = _preparationStartTick + _spell.MinimumCueTicks - EffectiveWindup(mastery, _host.ModifiersFor(_spell));
            if (tick >= earliestCommit)
            {
                Commit(mastery, auto, tick, events);
                return;
            }

            _commitHeld = true;
            _heldCommitTick = earliestCommit;
            _heldMastery = mastery;
            _heldAuto = auto;
        }

        private void Commit(MasteryResult mastery, bool auto, int tick, List<CastEvent> events)
        {
            _commitHeld = false;
            if (_host.CooldownRemainingTicks(_spell.Id) > 0)
            {
                CancelPreparation(CancelReason.CooldownNotReady, tick, events);
                return;
            }

            CastModifiers modifiers = _host.ModifiersFor(_spell);
            float reduction = modifiers.CostReduction + (mastery.IsMastered ? 0f : modifiers.NormalCostReduction);
            float cost = MasteryClamp.PaidCost(_spell.Mana, reduction, MasteryClamp.RewardedCastEfficiency(mastery, modifiers))
                + Math.Max(0f, modifiers.CostIncrease);
            if (!_host.TryPayCommitment(_spell, cost))
            {
                CancelPreparation(CancelReason.InsufficientMana, tick, events);
                return;
            }

            _host.StartCooldown(_spell);
            _paid = cost;
            _mastery = mastery;
            _recoveryTicks = MasteryClamp.TimedRecoveryTicks(_spell.RecoveryTicks, modifiers);
            _refunds.Open(_castId, cost);
            TotalExposedPreparationTicks += tick - _preparationStartTick;
            if (_usesGrammar) _grammar.End(true, mastery);

            // A windup reduction never shortens the cue below its minimum before release (spec L96).
            int windup = Math.Max(EffectiveWindup(mastery, modifiers), _preparationStartTick + _spell.MinimumCueTicks - tick);
            _phase = CastPhase.Windup;
            _phaseEndTick = tick + windup;
            events.Add(new CastEvent(CastEventKind.Committed, tick, _castId, _spell.Id, _spellSlot, mastery, CancelReason.None, InterruptCause.None,
                ChannelEndReason.Completed, RejectReason.None, cost, windup, 0, auto));
            if (windup == 0) Release(tick, events); // instant means zero windup, not zero recovery (spec L92)
        }

        /// <summary>Intrinsic windup after a windup-reduction reward and same-category linked bonuses (spec L480).</summary>
        private int EffectiveWindup(MasteryResult mastery, CastModifiers modifiers)
        {
            int windup = mastery.Reward != MasteryReward.WindupReduction
                ? _spell.WindupTicks
                : MasteryClamp.ReducedWindupTicks(_spell.WindupTicks, MasteryClamp.Combined(mastery, modifiers.MasteryLinkedAttribute), mastery.WindupCapTicks);
            return MasteryClamp.TimedWindupTicks(_spell.WindupTicks, windup, mastery, modifiers); // equipment/augment timing
        }

        private void CancelPreparation(CancelReason reason, int tick, List<CastEvent> events)
        {
            int exposed = tick - _preparationStartTick;
            TotalExposedPreparationTicks += exposed;
            _commitHeld = false;
            if (_usesGrammar) _grammar.End(false, default);
            events.Add(new CastEvent(CastEventKind.PreparationCancelled, tick, _castId, _spell.Id, _spellSlot, default, reason, InterruptCause.None,
                ChannelEndReason.Completed, RejectReason.None, 0f, exposed, 0, false));
            _phase = CastPhase.Idle;

            // Before commitment: no mana, the exposure stays spent, and a 0.15 s re-cast lock (spec L90).
            _lockUntilTick = tick + CastingRules.RecastLockTicks;
        }

        // ---- Committed cast ------------------------------------------------------------------------------------

        private void Release(int tick, List<CastEvent> events)
        {
            _releaseTick = tick;
            events.Add(Event(CastEventKind.Released, tick, mastery: _mastery));
            if (_spell.Recast != null && _spellSlot >= 0) OpenRecast(tick, events);

            if (_spell.IsChanneled)
            {
                _phase = CastPhase.Channel;
                _phaseEndTick = tick + _spell.ChannelMaxTicks;
                _channelPulses = 0;
            }
            else
            {
                StartRecovery(tick, _recoveryTicks);
            }
        }

        private void EndChannel(ChannelEndReason reason, int tick, List<CastEvent> events)
        {
            events.Add(new CastEvent(CastEventKind.ChannelEnded, tick, _castId, _spell.Id, _spellSlot, _mastery, CancelReason.None, InterruptCause.None,
                reason, RejectReason.None, 0f, tick - _releaseTick, _channelPulses, false));
            StartRecovery(tick, _recoveryTicks);
        }

        private void StartRecovery(int tick, int ticks)
        {
            _phase = CastPhase.Recovery;
            _phaseEndTick = tick + ticks;
        }

        /// <summary>Voluntary cancellation after commitment: resource spent, cooldown runs, no refund (spec L90).</summary>
        private void CancelCommitted(int tick, List<CastEvent> events)
        {
            events.Add(Event(CastEventKind.CommittedCastCancelled, tick, mastery: _mastery, mana: _paid));
            _phase = CastPhase.Idle;
        }

        /// <summary>
        /// Interruption of a committed cast: the resource stays spent and the cooldown runs; only the universal
        /// interrupt-refund tag returns 25% of the payment, once (spec L90).
        /// </summary>
        private void InterruptCommitted(InterruptCause cause, int tick, List<CastEvent> events)
        {
            float refund = 0f;
            if (_spell.HasInterruptRefund)
            {
                refund = _refunds.Grant(_castId, InterruptRefundKey, _paid * GlobalRules.Casting.InterruptRefundFraction);
                if (refund > 0f) _host.RefundMana(refund);
            }

            events.Add(new CastEvent(CastEventKind.Interrupted, tick, _castId, _spell.Id, _spellSlot, _mastery, CancelReason.None, cause,
                ChannelEndReason.Interrupted, RejectReason.None, refund, 0, 0, false));
            if (_phase == CastPhase.Channel) EndChannel(ChannelEndReason.Interrupted, tick, events);
            else _phase = CastPhase.Idle;
        }

        // ---- Recast --------------------------------------------------------------------------------------------

        private void OpenRecast(int tick, List<CastEvent> events)
        {
            if (_recasts[_spellSlot] != null) CloseRecast(_spellSlot, tick, events);
            var pending = new PendingRecast(_castId, _spell.Id, _mastery, _spell.Recast, tick);
            _recasts[_spellSlot] = pending;
            events.Add(new CastEvent(CastEventKind.RecastWindowOpened, tick, _castId, _spell.Id, _spellSlot, _mastery, CancelReason.None, InterruptCause.None,
                ChannelEndReason.Completed, RejectReason.None, 0f, pending.Rule.WindowTicks, pending.Rule.MaxRecasts, false));
        }

        private void TryRecast(int tick, List<CastEvent> events)
        {
            var spell = SelectedSpell;
            var pending = PendingRecastFor(_selected);

            // Recast acts only on the selected spell's pending state and never starts a new spell (spec L94).
            RejectReason reject = spell == null ? RejectReason.NoSpellSelected
                : pending == null ? RejectReason.NoPendingRecast
                : _host is IRecastGate gate && !gate.HasRecastTarget(pending.CastId) ? RejectReason.NoPendingRecast // nothing to act on yet (L94)
                : _phase == CastPhase.Preparation || _phase == CastPhase.Windup || _phase == CastPhase.Channel ? RejectReason.Busy
                : _host.IsWarded ? RejectReason.Warded
                : _host.IsSilenced ? RejectReason.Silenced
                : _host.IsHardControlled ? RejectReason.HardControlled
                : _host.IsActionLocked ? RejectReason.ActionLocked
                : RejectReason.None;
            if (reject == RejectReason.None && pending.Rule.ExtraMana > 0f && !_host.TryPayMana(pending.Rule.ExtraMana)) reject = RejectReason.InsufficientMana;
            if (reject != RejectReason.None)
            {
                events.Add(Rejection(tick, spell, _selected, reject));
                return;
            }

            // Same event identity and mastery as the original paid cast; no refund, rotation credit, or new trigger.
            pending.Used++;
            events.Add(new CastEvent(CastEventKind.Recast, tick, pending.CastId, pending.SpellId, _selected, pending.Mastery, CancelReason.None, InterruptCause.None,
                ChannelEndReason.Completed, RejectReason.None, pending.Rule.ExtraMana, 0, pending.Used, false));

            int added = pending.Rule.AddedRecoveryTicks;
            if (added > 0)
            {
                if (_phase == CastPhase.Idle)
                {
                    _spell = spell;
                    _spellSlot = _selected;
                    _castId = pending.CastId;
                    StartRecovery(tick, added);
                }
                else if (_phase == CastPhase.Recovery && tick + added > _phaseEndTick)
                {
                    _phaseEndTick = tick + added;
                }
            }

            if (pending.Remaining <= 0) CloseRecast(_selected, tick, events);
        }

        private void ExpireRecasts(int tick, List<CastEvent> events)
        {
            for (int i = 0; i < _recasts.Length; i++)
                if (_recasts[i] != null && tick > _recasts[i].LastTick) CloseRecast(i, tick, events);
        }

        private void CloseRecast(int slot, int tick, List<CastEvent> events)
        {
            var pending = _recasts[slot];
            _recasts[slot] = null;
            events.Add(new CastEvent(CastEventKind.RecastWindowClosed, tick, pending.CastId, pending.SpellId, slot, pending.Mastery, CancelReason.None,
                InterruptCause.None, ChannelEndReason.Completed, RejectReason.None, 0f, 0, pending.Used, false));
        }

        // ---- Helpers -------------------------------------------------------------------------------------------

        private GrammarContext Context(int tick) => new GrammarContext(tick, _preparationStartTick, _spell, _spellSlot);

        private CastEvent Event(CastEventKind kind, int tick, MasteryResult mastery = default, float mana = 0f, int ticks = 0, int count = 0) =>
            new CastEvent(kind, tick, _castId, _spell.Id, _spellSlot, mastery, CancelReason.None, InterruptCause.None, ChannelEndReason.Completed,
                RejectReason.None, mana, ticks, count, false);

        private static CastEvent Rejection(int tick, CastableSpell spell, int slot, RejectReason reason) =>
            new CastEvent(CastEventKind.Rejected, tick, CastEventId.None, spell == null ? null : spell.Id, slot, default, CancelReason.None,
                InterruptCause.None, ChannelEndReason.Completed, reason, 0f, 0, 0, false);

        private static CancelReason CancelReasonFor(InterruptCause cause)
        {
            switch (cause)
            {
                case InterruptCause.Silence: return CancelReason.Silenced;
                case InterruptCause.HardControl: return CancelReason.HardControlled;
                default: return CancelReason.External;
            }
        }
    }
}
