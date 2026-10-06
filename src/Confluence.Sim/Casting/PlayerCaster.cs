using System;
using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Core.Time;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Effects;
using Confluence.Sim.Hashing;
using Confluence.Sim.Movement;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Casting
{
    /// <summary>
    /// One player's casting state in the duel: the <see cref="CastMachine"/> and the <see cref="ICastHost"/> it reads.
    /// The host side owns the authoritative numbers: mana is paid all-or-nothing at commitment through the world
    /// (spec L78, L90), the 70% cost floor and cost reduction come from the stat sheet (spec L124), cooldowns start at
    /// commitment with the permanent reduction cap and 0.5 s floor (spec L124), and silence, hard control, the alcove
    /// ward, and action locks come from the player's statuses and the world's cast permissions (spec L46, L104-L116).
    /// </summary>
    public sealed class PlayerCaster : ICastHost, IRecastGate
    {
        private readonly DuelWorld _world;
        private readonly DuelPlayer _player;
        private readonly SortedDictionary<string, int> _readyTick = new SortedDictionary<string, int>(StringComparer.Ordinal);
        private readonly SortedDictionary<int, CastEventId> _castByPayment = new SortedDictionary<int, CastEventId>();
        private readonly List<CastEvent> _refundEvents = new List<CastEvent>();
        private readonly Action<PlayerCaster, List<CastEvent>> _record;
        private CastPermissions _permissions;

        // Previous-tick facts for server-generated combat events.
        private int _previousDodgeTicks;
        private bool _previousGrounded = true;
        private MeleePhase _previousMeleePhase;

        internal PlayerCaster(DuelWorld world, DuelPlayer player, SpellLoadout loadout, Action<PlayerCaster, List<CastEvent>> record = null)
        {
            _world = world;
            _player = player;
            _record = record;
            Loadout = loadout ?? throw new ArgumentNullException(nameof(loadout));
            var castables = new CastableSpell[loadout.Spells.Count];
            for (int i = 0; i < castables.Length; i++)
            {
                castables[i] = loadout.Spells[i]?.Castable;
            }

            Machine = new CastMachine(player.Index, this, loadout.Grammar, castables, loadout.Signature?.Castable);
            _permissions = new CastPermissions { CanCast = true, CanUseSignature = true, CanUseItems = true, CanChannel = true };
        }

        public int Player => _player.Index;
        public CastMachine Machine { get; }
        public SpellLoadout Loadout { get; }

        /// <summary>Buttons held on the previous casting step (edges for the cast frame).</summary>
        public InputButtons PreviousHeld { get; internal set; }

        /// <summary>The form of a spell or signature in this loadout by id, or null.</summary>
        public SpellForm FormFor(string spellId) => Loadout.FormFor(spellId);

        /// <summary>Tick the spell's cooldown is ready again (0 = never started).</summary>
        public int CooldownReadyTick(string spellId) => spellId != null && _readyTick.TryGetValue(spellId, out int t) ? t : 0;

        /// <summary>The world's permissions for this tick (set before the machine steps).</summary>
        internal void BeginTick(in CastPermissions permissions) => _permissions = permissions;

        // ----- ICastHost ------------------------------------------------------------------------------------

        public float Mana => _player.Resources.Mana;

        public bool TryPayMana(float amount) => _world.TryCommitMana(_player.Index, amount, PaymentSource());

        /// <summary>
        /// The commitment payment goes through the effect framework, which consumes the "next cast" bonuses that priced
        /// it and records the payment for refunds (spec L90, L124).
        /// </summary>
        public bool TryPayCommitment(CastableSpell spell, float amount)
        {
            if (spell == null) return TryPayMana(amount);
            if (!_world.Effects.TryCommitPricedCast(_player.Index, QuoteFor(spell), amount, out CastReceipt receipt)) return false;
            if (receipt.PaymentEventId != 0 && Machine.CurrentCastId.IsValid)
            {
                // Effect refunds of this payment share the machine's per-cast refund ledger (see TryRefundPayment).
                _castByPayment[receipt.PaymentEventId] = Machine.CurrentCastId;
                while (_castByPayment.Count > CastingRules.RefundLedgerHistory) _castByPayment.Remove(First(_castByPayment));
            }

            return true;
        }

        /// <summary>
        /// An effect refund for a commitment this caster paid (spec L90, L124): it goes through the cast machine's refund
        /// ledger, so interrupt refunds and every refund effect of one cast share the single 25% cap and each effect key
        /// fires once per cast. Returns false when the payment was not a cast commitment of this caster.
        /// </summary>
        public bool TryRefundPayment(int paymentEventId, string effectKey, float requested, out float granted)
        {
            granted = 0f;
            if (!_castByPayment.TryGetValue(paymentEventId, out CastEventId castId)) return false;
            _refundEvents.Clear();
            granted = Machine.TryRefund(castId, effectKey ?? "effect", requested, _world.Tick, _refundEvents);
            _record?.Invoke(this, _refundEvents); // the Refunded cast event joins the duel's event stream
            return true;
        }

        private static int First(SortedDictionary<int, CastEventId> map)
        {
            foreach (KeyValuePair<int, CastEventId> entry in map) return entry.Key;
            return 0;
        }

        public void RefundMana(float amount) => _world.RefundMana(_player.Index, amount, PaymentSource());

        public int CooldownRemainingTicks(string spellId)
        {
            int ready = CooldownReadyTick(spellId);
            return Math.Max(0, ready - _world.Tick);
        }

        public void StartCooldown(CastableSpell spell)
        {
            if (spell == null || spell.CooldownTicks <= 0) return;

            // Permanent cooldown reduction caps at 25% and no recharge falls below 0.5 s (spec L124).
            float seconds = _player.Stats.EffectiveCooldownSec(Ticks.ToSeconds(spell.CooldownTicks));
            seconds = _world.Effects.AdjustCooldown(_player.Index, spell.Id, seconds); // Effects/IncomingHooks.cs ICastCooldownHook
            _readyTick[spell.Id] = _world.Tick + Ticks.FromSeconds(seconds);
        }

        /// <summary>
        /// Cost modifiers at commitment (spec L124): the stat cost reduction (capped at 30%) plus pending "next cast"
        /// reductions from effects as one fraction of the listed cost, which the machine floors together with mastery
        /// efficiency at 70% of the listed cost; pending drawback increases are added after the floor.
        /// </summary>
        public CastModifiers ModifiersFor(CastableSpell spell)
        {
            if (spell == null) return new CastModifiers { CostReduction = _player.Stats.CostReduction };
            CastQuote quote = QuoteFor(spell);
            CastPrice price = _world.Effects.PriceCast(_player.Index, quote);
            CastTimingAdjust timing = _world.Effects.CastTiming(_player.Index, quote); // content hooks (Effects/EffectHooks.cs)
            _world.Effects.MasteryPrice(_player.Index, quote, out float mastered, out float normal);
            float listed = Math.Max(0f, spell.Mana);
            return new CastModifiers
            {
                CostReduction = listed > 0f ? (price.StatReduction + price.BonusReduction) / listed : _player.Stats.CostReduction,
                CostIncrease = price.Increase,
                MasteredCostReduction = listed > 0f ? mastered / listed : 0f,
                NormalCostReduction = listed > 0f ? normal / listed : 0f,
                WindupReduction = timing.WindupReduction,
                MasteredWindupReduction = timing.MasteredWindupReduction,
                WindupDeltaTicks = timing.WindupDeltaTicks,
                RecoveryDeltaTicks = timing.RecoveryDeltaTicks,
                RecoveryFloorTicks = timing.RecoveryFloorTicks,
            };
        }

        /// <summary>The effect framework's view of a cast (filters read the id, the damaging flag, and the form's tags).</summary>
        private CastQuote QuoteFor(CastableSpell spell)
        {
            SpellForm form = Loadout.FormFor(spell.Id);
            bool damaging = form != null ? form.HasTag("damaging") : true;
            return CastQuote.Of(spell.Id, spell.Mana, damaging, form?.Tags);
        }

        public bool IsSilenced => _player.Statuses.IsSilenced;

        /// <summary>Stun and hostile displacement prevent casting (spec L116); root only stops movement.</summary>
        public bool IsHardControlled => _player.Statuses.IsStunned || _player.Statuses.IsDisplaced;

        /// <summary>The spawn-alcove ward (spec L46).</summary>
        public bool IsWarded => _world.Phase == DuelPhase.Alcove;

        /// <summary>
        /// Rule states that refuse new casts without being control: death, a finished duel, the visible recall (spec
        /// L175), the guard release window (spec L104), a common melee strike, or an equip swap (spec L106).
        /// </summary>
        public bool IsActionLocked
        {
            get
            {
                switch (_permissions.Reason)
                {
                    case CastBlockReason.Dead:
                    case CastBlockReason.DuelEnded:
                    case CastBlockReason.Recalling:
                    case CastBlockReason.GuardRelease:
                    case CastBlockReason.Melee:
                    case CastBlockReason.Swapping:
                        return true;
                    default:
                        return _world.Phase == DuelPhase.Ended || !_player.Alive;
                }
            }
        }

        /// <summary>IRecastGate: the spell runtime knows whether the cast's recast target exists yet (spec L94).</summary>
        public bool HasRecastTarget(CastEventId castId) => !(_world.Spells is SpellRuntime runtime) || runtime.HasRecastTarget(castId);

        // ----- server-generated combat events polled from player state ------------------------------------------

        /// <summary>
        /// Appends this tick's combat events that come from state transitions rather than world events: dodge travel
        /// completed (spec L82), landing, melee windup completed and recovery ended (WIZ-015, WIZ-035), cooldowns ready (WIZ-035).
        /// </summary>
        internal void PollStateEvents(List<CombatEvent> into)
        {
            int tick = _world.Tick;
            int dodge = _player.Motor.DodgeTicks;
            if (_previousDodgeTicks > 0 && dodge == 0) into.Add(new CombatEvent(CombatEventKind.DodgeCompleted, tick));
            _previousDodgeTicks = dodge;

            // Landing on supporting ground after being airborne, whatever the drop (WIZ-035 "landed from jump").
            bool grounded = _player.Motor.Grounded;
            if (!_previousGrounded && grounded) into.Add(new CombatEvent(CombatEventKind.Landed, tick));
            _previousGrounded = grounded;

            MeleePhase melee = _player.Melee.Phase;
            if (_previousMeleePhase == MeleePhase.Windup && (melee == MeleePhase.Strike || melee == MeleePhase.Recovery))
            {
                into.Add(new CombatEvent(CombatEventKind.MeleeWindupCompleted, tick));
            }

            if ((_previousMeleePhase == MeleePhase.Recovery || _previousMeleePhase == MeleePhase.Strike) && melee == MeleePhase.Idle)
            {
                into.Add(new CombatEvent(CombatEventKind.MeleeRecoveryEnded, tick));
            }

            _previousMeleePhase = melee;
            foreach (KeyValuePair<string, int> entry in _readyTick)
            {
                if (entry.Value == tick) into.Add(new CombatEvent(CombatEventKind.CooldownReady, tick, entry.Key));
            }
        }

        private string PaymentSource()
        {
            CastableSpell spell = Machine.CurrentSpell ?? Machine.SelectedSpell;
            return spell != null ? spell.Id : null;
        }

        public void Hash(ref StateHasher h)
        {
            h.Add(Machine.StateDigest());
            h.Add((uint)PreviousHeld);
            h.Add(_previousDodgeTicks);
            h.Add(_previousGrounded);
            h.Add((int)_previousMeleePhase);
            h.Add(_readyTick.Count);
            foreach (KeyValuePair<string, int> entry in _readyTick)
            {
                h.Add(entry.Key);
                h.Add(entry.Value);
            }

            h.Add(_castByPayment.Count);
            foreach (KeyValuePair<int, CastEventId> entry in _castByPayment)
            {
                h.Add(entry.Key);
                h.Add(entry.Value.Player);
                h.Add(entry.Value.Sequence);
            }
        }
    }
}
