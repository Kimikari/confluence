using System;
using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Core.Time;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
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
    public sealed class PlayerCaster : ICastHost
    {
        private readonly DuelWorld _world;
        private readonly DuelPlayer _player;
        private readonly SortedDictionary<string, int> _readyTick = new SortedDictionary<string, int>(StringComparer.Ordinal);
        private CastPermissions _permissions;

        // Previous-tick facts for server-generated combat events.
        private int _previousDodgeTicks;
        private bool _previousGrounded = true;
        private MeleePhase _previousMeleePhase;

        internal PlayerCaster(DuelWorld world, DuelPlayer player, SpellLoadout loadout)
        {
            _world = world;
            _player = player;
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
            _readyTick[spell.Id] = _world.Tick + Ticks.FromSeconds(seconds);
        }

        /// <summary>Ordinary cost reduction from the stat system, already capped at 30% (spec L124).</summary>
        public CastModifiers ModifiersFor(CastableSpell spell) => new CastModifiers { CostReduction = _player.Stats.CostReduction };

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
        }
    }
}
