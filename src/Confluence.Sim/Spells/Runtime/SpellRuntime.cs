using System;
using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Sim.Casting;
using Confluence.Sim.Content;
using Confluence.Sim.Duel;
using Confluence.Sim.Hashing;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using CastInterrupt = Confluence.Casting.InterruptCause;
using WorldInterrupt = Confluence.Sim.Duel.InterruptCause;

namespace Confluence.Sim.Spells
{
    /// <summary>
    /// The spell stage behind <see cref="ISpellRuntime"/>. Each player has a <see cref="PlayerCaster"/> (cast machine
    /// plus host); every tick-order step 3 builds that player's cast frame from the sanitized input and the
    /// server-generated combat events, steps the machine, and turns its cast events into world events and effects:
    /// commitment records the cast (mana, cooldown, mastery), release resolves or spawns the delivery, channel pulses
    /// drive beams and channeled cones, recasts act on the cast's entities. Step 4 advances every live delivery entity
    /// in creation order. A lifetime recast window closes when the cast's last entity ends (spec L94).
    /// </summary>
    public sealed class SpellRuntime : ISpellRuntime
    {
        private readonly ISpellLoadoutSource _loadouts;
        private readonly PlayerCaster[] _casters = new PlayerCaster[DuelWorld.PlayerCount];
        private readonly CombatEventFeed _feed = new CombatEventFeed();
        private readonly List<SpellEntity> _entities = new List<SpellEntity>();
        private readonly List<SpellEntity> _stepScratch = new List<SpellEntity>();
        private readonly List<CastRecord> _casts = new List<CastRecord>();
        private readonly HashSet<CastEventId> _openRecasts = new HashSet<CastEventId>();
        private readonly List<CastEvent> _events = new List<CastEvent>();
        private readonly List<CombatEvent> _combat = new List<CombatEvent>();
        private readonly List<ISpellBody>[] _hostileBodies = { new List<ISpellBody>(), new List<ISpellBody>() };
        private readonly List<IRayReflector> _reflectors = new List<IRayReflector> { ReflectiveSolidReflector.Instance };
        private DuelWorld _world;
        private SpellContext _ctx;
        private int _nextEntityId = 1;

        public SpellRuntime(ISpellLoadoutSource loadouts)
        {
            _loadouts = loadouts ?? throw new ArgumentNullException(nameof(loadouts));
        }

        /// <summary>Loadouts from the catalogue and the registered content behaviors.</summary>
        public static SpellRuntime FromCatalogue(Catalogue catalogue, ContentRegistry registry = null) =>
            new SpellRuntime(new CatalogueLoadouts(catalogue, registry));

        public static SpellRuntime WithLoadouts(SpellLoadout player0, SpellLoadout player1) =>
            new SpellRuntime(new FixedLoadouts(player0, player1));

        public DuelWorld World => _world;

        public SpellContext Context => _ctx;

        public PlayerCaster Caster(int player) => _casters[player];

        /// <summary>Live delivery entities in creation order.</summary>
        public IReadOnlyList<SpellEntity> Entities => _entities;

        /// <summary>Committed casts whose effects or recast windows are still live, in commitment order.</summary>
        public IReadOnlyList<CastRecord> LiveCasts => _casts;

        /// <summary>Reflection hooks for rays, tried in order (reflective solids first; prisms and timed reflections add theirs).</summary>
        public IReadOnlyList<IRayReflector> Reflectors => _reflectors;

        public void AddReflector(IRayReflector reflector) => _reflectors.Add(reflector ?? throw new ArgumentNullException(nameof(reflector)));

        public void Attach(DuelWorld world)
        {
            if (_world != null) throw new InvalidOperationException("A spell runtime serves one duel.");
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _ctx = new SpellContext(world, this);
            for (int i = 0; i < DuelWorld.PlayerCount; i++)
            {
                SpellLoadout loadout = _loadouts.Build(world, i);
                if (loadout == null) continue;
                DuelPlayer p = world.Player(i);
                _casters[i] = new PlayerCaster(world, p, loadout, Process);
                p.Caster = _casters[i];
            }

            if (_casters[0] != null && _casters[1] != null && ReferenceEquals(_casters[0].Loadout.Grammar, _casters[1].Loadout.Grammar))
            {
                throw new InvalidOperationException("Each player needs its own grammar instance (grammars keep per-player state).");
            }

            world.AddEventObserver(_feed);
        }

        // ----- step 3 ---------------------------------------------------------------------------------------

        public void UpdateCasting(int player, in PlayerInput input, in CastPermissions permissions)
        {
            PlayerCaster caster = _casters[player];
            if (caster == null) return;
            DuelPlayer p = _world.Player(player);
            caster.BeginTick(permissions);

            // The visible recall holds the player (spec L175): a committed cast is interrupted, an open one cancelled.
            if (p.IsRecalling && caster.Machine.Phase != CastPhase.Idle && caster.Machine.Phase != CastPhase.Recovery)
            {
                RunInterrupt(caster, CastInterrupt.Recalled);
            }

            _combat.Clear();
            _feed.Drain(player, _combat);
            caster.PollStateEvents(_combat);
            CastInputFrame frame = CastFrameBuilder.Build(_world.Tick, input, p, caster.PreviousHeld, _combat);
            caster.PreviousHeld = frame.Held;

            _events.Clear();
            caster.Machine.Step(frame, _events);
            Process(caster, _events); // re-entrant interrupts (a lethal hit) process their own event lists
            SweepCasts();
        }

        // ----- step 4 ---------------------------------------------------------------------------------------

        public void AdvanceDeliveries()
        {
            if (_world == null) return;
            _stepScratch.Clear();
            _stepScratch.AddRange(_entities);
            for (int i = 0; i < _stepScratch.Count; i++)
            {
                SpellEntity e = _stepScratch[i];
                if (e.Alive) e.Step(_ctx);
            }

            _stepScratch.Clear();
            _entities.RemoveAll(e => !e.Alive);
            SweepCasts();
        }

        public CastingStatus GetStatus(int player)
        {
            PlayerCaster caster = _casters[player];
            if (caster == null) return CastingStatus.Idle;
            CastMachine m = caster.Machine;
            return new CastingStatus
            {
                MovementFactor = m.MoveSpeedFactor,
                Channeling = m.IsChanneling,
                Busy = m.Phase != CastPhase.Idle,
                InRecovery = m.Phase == CastPhase.Recovery,
                SuppressesSprint = m.SuppressesSprint,
            };
        }

        /// <summary>
        /// The world interrupts a player's cast (spec L90, L116): stun and hostile displacement are hard control,
        /// silence silences, death and the duel end stop everything. An open preparation is cancelled at no cost;
        /// a committed windup or channel is interrupted (mana spent, cooldown runs). Returns true only for a committed
        /// cast (FEAT-019).
        /// </summary>
        public bool Interrupt(int player, WorldInterrupt cause)
        {
            PlayerCaster caster = _casters[player];
            if (caster == null) return false;
            CastPhase phase = caster.Machine.Phase;
            bool committed = phase == CastPhase.Windup || phase == CastPhase.Channel;
            if (phase == CastPhase.Idle || phase == CastPhase.Recovery) return false;
            RunInterrupt(caster, Map(cause));
            return committed;
        }

        public void Hash(ref StateHasher h)
        {
            for (int i = 0; i < _casters.Length; i++)
            {
                if (_casters[i] == null)
                {
                    h.Add(-1);
                    continue;
                }

                _casters[i].Hash(ref h);
            }

            _feed.Hash(ref h);
            h.Add(_nextEntityId);
            h.Add(_entities.Count);
            for (int i = 0; i < _entities.Count; i++) _entities[i].Hash(ref h);
            h.Add(_casts.Count);
            for (int i = 0; i < _casts.Count; i++)
            {
                _casts[i].Hash(ref h);
                h.Add(_openRecasts.Contains(_casts[i].CastId));
            }
        }

        // ----- services for deliveries ----------------------------------------------------------------------

        internal T AddEntity<T>(T entity) where T : SpellEntity
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            entity.Id = _nextEntityId++;
            entity.SpawnTick = _world.Tick;
            entity.Cast.LiveEntities++;
            _entities.Add(entity);
            return entity;
        }

        /// <summary>Hostile live spell bodies for an attacker, in creation order.</summary>
        internal IReadOnlyList<ISpellBody> BodiesHostileTo(int attacker)
        {
            List<ISpellBody> list = _hostileBodies[attacker < 0 || attacker >= _hostileBodies.Length ? 0 : attacker];
            list.Clear();
            for (int i = 0; i < _entities.Count; i++)
            {
                if (_entities[i].Alive && _entities[i] is ISpellBody body && body.Owner != attacker && body.IsIntact) list.Add(body);
            }

            return list;
        }

        /// <summary>
        /// The target's conditional reduction against projectile damage from its active buffs (SPL-011-style), combined
        /// multiplicatively; the pipeline keeps all passive reductions inside the 45% cap (spec L112).
        /// </summary>
        internal float ProjectileReductionFor(int target)
        {
            float keep = 1f;
            for (int i = 0; i < _entities.Count; i++)
            {
                if (_entities[i].Alive && _entities[i] is BuffEntity buff && buff.Owner == target) keep *= 1f - buff.Spec.ProjectileDamageReduction;
            }

            return 1f - keep;
        }

        /// <summary>The live record of a cast, or null.</summary>
        public CastRecord FindCast(CastEventId castId)
        {
            for (int i = 0; i < _casts.Count; i++)
                if (_casts[i].CastId == castId) return _casts[i];
            return null;
        }

        // ----- cast events ----------------------------------------------------------------------------------

        private void RunInterrupt(PlayerCaster caster, CastInterrupt cause)
        {
            var events = new List<CastEvent>();
            caster.Machine.Interrupt(cause, _world.Tick, events);
            Process(caster, events);
        }

        private void Process(PlayerCaster caster, List<CastEvent> events)
        {
            for (int i = 0; i < events.Count; i++)
            {
                CastEvent e = events[i];
                CastDuelEvent recorded = Record(caster, e);
                SpellForm form = caster.FormFor(e.SpellId);
                CastRecord cast = e.CastId.IsValid ? FindCast(e.CastId) : null;
                switch (e.Kind)
                {
                    case CastEventKind.CueStarted:
                        if (form != null)
                        {
                            Vec3 target = form.Delivery.PredictTarget(_ctx, form, caster.Player);
                            _ctx.World.RecordEvent(new SpellEvent
                            {
                                Kind = SpellEventKind.Telegraph,
                                SpellId = form.SpellId,
                                CastId = e.CastId,
                                Position = target,
                                Direction = _ctx.AimDirection(caster.Player),
                                Ticks = e.Ticks,
                            }, caster.Player);
                        }

                        break;
                    case CastEventKind.Committed:
                        if (form != null) OnCommitted(caster, form, e, recorded);
                        break;
                    case CastEventKind.Released:
                        if (cast != null)
                        {
                            cast.ReleaseTick = _world.Tick;
                            cast.Form.Delivery.Release(_ctx, cast);
                            cast.Form.Script?.OnReleased(_ctx, cast);
                            _world.Effects.RaiseCastReleased(caster.Player, cast.SpellId, cast.OriginEventId); // spec L90 "on release" triggers
                        }

                        break;
                    case CastEventKind.ChannelPulse:
                        cast?.Form.Delivery.OnChannelPulse(_ctx, cast, e.Count);
                        break;
                    case CastEventKind.ChannelEnded:
                        cast?.Form.Delivery.OnChannelEnded(_ctx, cast);
                        break;
                    case CastEventKind.CommittedCastCancelled:
                    case CastEventKind.Interrupted:
                        // Nothing was released: the cast is spent (spec L90) and leaves no effect behind.
                        if (cast != null && !cast.Released) _casts.Remove(cast);
                        break;
                    case CastEventKind.RecastWindowOpened:
                        _openRecasts.Add(e.CastId);
                        break;
                    case CastEventKind.RecastWindowClosed:
                        _openRecasts.Remove(e.CastId);
                        break;
                    case CastEventKind.Recast:
                        if (cast != null) OnRecast(cast);
                        break;
                }
            }
        }

        private void OnCommitted(PlayerCaster caster, SpellForm form, in CastEvent e, CastDuelEvent recorded)
        {
            int player = caster.Player;
            var cast = new CastRecord(form, player, e.Slot, e.CastId, e.Mastery, caster.ModifiersFor(form.Castable).MasteryLinkedAttribute,
                _world.Tick, recorded.Id, _ctx.AimOrigin(player), _ctx.AimDirection(player));
            _casts.Add(cast);
            _world.NotifyOffensiveAction(player); // spec L120: invisibility ends on a cast or signature activation
            if (e.Mastery.IsMastered) _world.RecordMasteryCast(player);
        }

        private void OnRecast(CastRecord cast)
        {
            // The recast acts on the original cast's live entities, reusing its identity and mastery (spec L94).
            for (int i = 0; i < _entities.Count; i++)
            {
                SpellEntity entity = _entities[i];
                if (entity.Alive && entity.AcceptsRecast && entity.Cast == cast) entity.OnRecast(_ctx);
            }

            cast.Form.Script?.OnRecast(_ctx, cast);
        }

        /// <summary>
        /// Retires finished casts. A recast window without an explicit length lasts the original effect's remaining
        /// lifetime and closes when the effect ends (spec L94 "never after that effect has ended").
        /// </summary>
        private void SweepCasts()
        {
            for (int i = 0; i < _casts.Count;)
            {
                CastRecord cast = _casts[i];
                PlayerCaster caster = _casters[cast.Caster];
                bool channeling = caster.Machine.IsChanneling && caster.Machine.CurrentCastId == cast.CastId;
                if (!cast.EffectEnded || channeling)
                {
                    i++;
                    continue;
                }

                if (_openRecasts.Contains(cast.CastId))
                {
                    RecastRule rule = cast.Form.Castable.Recast;
                    if (rule != null && rule.HasExplicitWindow)
                    {
                        i++; // an explicit window (SPL-008 "recast within 1 s") outlives the effect it can follow up
                        continue;
                    }

                    var events = new List<CastEvent>();
                    caster.Machine.EndRecastWindow(cast.CastId, _world.Tick, events);
                    Process(caster, events);
                }

                _casts.RemoveAt(i);
            }
        }

        private CastDuelEvent Record(PlayerCaster caster, in CastEvent e)
        {
            return _world.RecordEvent(new CastDuelEvent
            {
                Cast = e,
                AimOrigin = _ctx.AimOrigin(caster.Player),
                AimDirection = _ctx.AimDirection(caster.Player),
            }, caster.Player);
        }

        private static CastInterrupt Map(WorldInterrupt cause)
        {
            switch (cause)
            {
                case WorldInterrupt.Silence: return CastInterrupt.Silence;
                case WorldInterrupt.Death: return CastInterrupt.Death;
                case WorldInterrupt.DuelEnded: return CastInterrupt.DuelEnded;
                default: return CastInterrupt.HardControl; // stun and hostile displacement (spec L116)
            }
        }
    }
}
