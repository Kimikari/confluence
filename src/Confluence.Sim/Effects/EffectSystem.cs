using System;
using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Combat;
using Confluence.Sim.Content;
using Confluence.Sim.Duel;
using Confluence.Sim.Hashing;
using Confluence.Sim.Players;
using Confluence.Sim.Props;
using Confluence.Sim.World.Materials;
using SurfaceKind = Confluence.Sim.World.SurfaceKind;

namespace Confluence.Sim.Effects
{
    /// <summary>
    /// The effect framework of one duel (spec §14 data model L433, trigger rules L126). It attaches loadout behaviors,
    /// turns duel events and polled state changes into typed triggers, runs matching effects under their filters,
    /// conditions, internal cooldowns, activation limits, and the global proc budget, and owns the cast-price and
    /// refund ledger and the outgoing-hit bonuses.
    /// <para>
    /// Dispatch happens once per tick at the end of tick-order step 7 (after statuses and regeneration), in a fixed
    /// order: duel events of the tick in emission order, spell-runtime raises in call order, then polled changes per
    /// player (sprint, selection, melee miss, surface, volume) and periodic ticks. Only original (generation 0) triggers
    /// fire effects; everything an effect's actions emit is the one permitted generation of secondary effects and is
    /// never translated into further triggers (spec L126). Secondary damage — reactions, props, boundary, recall —
    /// raises no hit triggers at all (L51, L126, L173, L672).
    /// </para>
    /// </summary>
    public sealed class EffectSystem
    {
        private readonly EffectHost[] _hosts;
        private readonly PlayerWatch[] _watch;
        private readonly List<TriggerEvent> _queue = new List<TriggerEvent>();
        private readonly List<TriggerEvent> _external = new List<TriggerEvent>();
        private readonly List<PaymentRecord> _payments = new List<PaymentRecord>();
        private readonly Dictionary<int, DamageEvent> _damageById = new Dictionary<int, DamageEvent>();
        private readonly List<ObscurationVolume> _volumeScratch = new List<ObscurationVolume>();
        private readonly int[] _statVersion;
        private int _cursor;
        private int _cursorTick = -1;
        private int _synthetic;

        public EffectSystem(DuelWorld world)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            _hosts = new EffectHost[DuelWorld.PlayerCount];
            _watch = new PlayerWatch[DuelWorld.PlayerCount];
            _statVersion = new int[DuelWorld.PlayerCount];
            for (int i = 0; i < DuelWorld.PlayerCount; i++)
            {
                _hosts[i] = new EffectHost(this, i);
                _watch[i] = new PlayerWatch();
                _statVersion[i] = -1;
            }
        }

        public DuelWorld World { get; }

        public EffectHost Host(int player) => _hosts[player];

        // ----- setup ------------------------------------------------------------------------------------------

        /// <summary>
        /// Attaches each player's wizard passive, equipment (setup order), and active shared augments (setup order)
        /// through their registered behaviors. Records without a behavior attach nothing (the pools offer only
        /// implemented records, D-010); spell transformations are resolved into spell forms by the spell runtime.
        /// </summary>
        public void AttachLoadouts(ContentRegistry content)
        {
            if (content == null)
            {
                return;
            }

            for (int i = 0; i < DuelWorld.PlayerCount; i++)
            {
                DuelPlayerSetup setup = World.Player(i).Setup;
                if (setup.WizardId != null && content.TryGetKit(setup.WizardId, out IWizardKitBehavior kit))
                {
                    kit.AttachPassive(_hosts[i]); // spec L472: the wizard passive is always active
                }

                if (setup.Equipment != null)
                {
                    for (int e = 0; e < setup.Equipment.Count; e++)
                    {
                        EquippedItem item = setup.Equipment[e];
                        if (item != null && content.TryGetEquipment(item.Id, out IEquipmentBehavior equipment))
                        {
                            equipment.Attach(_hosts[i], item);
                        }
                    }
                }

                if (setup.Augments != null)
                {
                    for (int a = 0; a < setup.Augments.Count; a++)
                    {
                        ActiveAugment augment = setup.Augments[a];
                        if (augment.Id != null && content.TryGetAugment(augment.Id, out IAugmentBehavior behavior))
                        {
                            behavior.Attach(_hosts[i], Math.Max(1, Math.Min(2, augment.Rank)));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Round start (spec L46, L579): players stand in their alcoves with restored pools; round-start effects such as
        /// reserve shields run now, once. Called by the world after alcove placement.
        /// </summary>
        public void BeginRound()
        {
            for (int i = 0; i < DuelWorld.PlayerCount; i++)
            {
                SnapshotWatch(i);
                _statVersion[i] = World.Player(i).Stats.Version;
            }

            for (int i = 0; i < DuelWorld.PlayerCount; i++)
            {
                _hosts[i].Fire(new TriggerEvent { Trigger = EffectTrigger.RoundStart, Player = i, Other = -1, Tick = World.Tick });
            }

            _cursorTick = World.Tick;
            _cursor = World.TickEvents.Count; // the round-start outputs are secondary
            SyncResourceMaxima();
        }

        // ----- dispatch ---------------------------------------------------------------------------------------

        /// <summary>The once-per-tick dispatch point (end of tick-order step 7).</summary>
        public void Dispatch()
        {
            int now = World.Tick;
            if (_cursorTick != now)
            {
                _cursorTick = now;
                _cursor = 0;
            }

            for (int i = 0; i < DuelWorld.PlayerCount; i++)
            {
                _hosts[i].ExpireTimed(now);
                _hosts[i].RefreshConditional();
            }

            TranslateNewEvents(); // after any carried-over triggers of the previous tick
            _queue.AddRange(_external);
            _external.Clear();
            bool combat = World.Phase == DuelPhase.Combat;
            for (int p = 0; p < DuelWorld.PlayerCount; p++)
            {
                Poll(p, combat);
            }

            if (combat)
            {
                for (int p = 0; p < DuelWorld.PlayerCount; p++)
                {
                    _queue.Add(new TriggerEvent { Trigger = EffectTrigger.Periodic, Player = p, Other = -1, Tick = now });
                }

                for (int i = 0; i < _queue.Count; i++)
                {
                    TriggerEvent t = _queue[i];
                    if (t.Player >= 0 && t.Player < DuelWorld.PlayerCount && World.Player(t.Player).Alive)
                    {
                        _hosts[t.Player].Fire(t);
                    }
                }
            }

            _queue.Clear();
            _cursor = World.TickEvents.Count; // outputs of the actions above are the secondary generation
            SyncResourceMaxima();
        }

        /// <summary>
        /// Called by the world before it clears the tick's events: events emitted after the last dispatch (between
        /// steps, or after step 7) are translated now and fire at the next dispatch point.
        /// </summary>
        public void CarryOver()
        {
            if (_cursorTick == World.Tick)
            {
                TranslateNewEvents();
            }
        }

        private void TranslateNewEvents()
        {
            IReadOnlyList<DuelEvent> events = World.TickEvents;
            _damageById.Clear();
            for (int i = _cursor; i < events.Count; i++)
            {
                Translate(events[i]);
            }

            _cursor = events.Count;
        }

        /// <summary>
        /// The spell runtime reports a release (spec L90). The trigger fires at the next dispatch point, in call order
        /// after the tick's duel events.
        /// </summary>
        public void RaiseCastReleased(int player, string spellId, int castEventId)
        {
            _external.Add(new TriggerEvent
            {
                Trigger = EffectTrigger.CastReleased,
                Player = player,
                Other = -1,
                Tick = World.Tick,
                EventId = castEventId,
                OriginEventId = castEventId,
                SourceId = spellId,
            });
        }

        /// <summary>Keeps the resource pools' maxima equal to the stat block (static capacity, spec L78, L114, L579).</summary>
        public void SyncResourceMaxima()
        {
            for (int i = 0; i < DuelWorld.PlayerCount; i++)
            {
                DuelPlayer p = World.Player(i);
                if (p.Stats.Version == _statVersion[i])
                {
                    continue;
                }

                _statVersion[i] = p.Stats.Version;
                p.Resources.SetMaxHealth(p.Stats.MaxHealth);
                p.Resources.SetMaxMana(p.Stats.MaxMana);
                p.Resources.SetMaxStamina(p.Stats.MaxStamina);
            }
        }

        // ----- cast pricing and refunds (spec L90, L124) -----------------------------------------------------

        /// <summary>The price of a cast now: stat cost reduction and pending bonuses, at least 70% paid, plus drawbacks.</summary>
        public CastPrice PriceCast(int player, in CastQuote quote)
        {
            DuelPlayer p = World.Player(player);
            _hosts[player].SumCastBonuses(quote, World.Tick, out float reduction, out float increase);
            return CastCostRules.Price(quote.BaseCost, p.Stats.CostReduction, reduction, increase);
        }

        /// <summary>
        /// Pays a cast at its price (full cost available, spec L78), consumes the next-cast bonuses that priced it, and
        /// records the payment for refunds. The cast-commit trigger fires at the next dispatch.
        /// </summary>
        public bool TryCommitCast(int player, in CastQuote quote, out CastReceipt receipt)
        {
            CastPrice price = PriceCast(player, quote);
            return TryCommitPricedCast(player, quote, price, out receipt);
        }

        /// <summary>
        /// Pays a cast the cast machine priced (the spell runtime's path, <c>ICastHost.TryPayCommitment</c>): the machine
        /// floored the stat and pending reductions of <see cref="PriceCast"/> together with mastery efficiency at 70% and
        /// added the drawbacks (spec L124, L480), so <paramref name="paid"/> is final. Consumes the next-cast bonuses that
        /// priced it and records the payment for refunds.
        /// </summary>
        public bool TryCommitPricedCast(int player, in CastQuote quote, float paid, out CastReceipt receipt)
        {
            CastPrice price = PriceCast(player, quote);
            price.Paid = Math.Max(0f, paid);
            return TryCommitPricedCast(player, quote, price, out receipt);
        }

        private bool TryCommitPricedCast(int player, in CastQuote quote, in CastPrice price, out CastReceipt receipt)
        {
            receipt = default;
            if (!World.TryCommitMana(player, price.Paid, quote.SpellId))
            {
                return false;
            }

            _hosts[player].ConsumeCastBonuses(quote, World.Tick);
            ResourceEvent payment = LastPayment(player);
            receipt = new CastReceipt { PaymentEventId = payment == null ? 0 : payment.Id, SpellId = quote.SpellId, Paid = price.Paid, Price = price };
            RecordPayment(player, receipt.PaymentEventId, quote.SpellId, price.Paid);
            return true;
        }

        /// <summary>
        /// Returns mana for a paid cast: at most 25% of its payment and only once per cast (spec L90, L124). Returns
        /// the mana restored.
        /// </summary>
        public float RefundCast(int player, int paymentEventId, float requested, string sourceId)
        {
            PaymentRecord record = FindPayment(paymentEventId);
            if (record == null || record.Player != player || record.Refunded)
            {
                return 0f;
            }

            // A spell commitment shares its cast machine's ledger (spec L90, L124): every refund of the cast, interrupt
            // refunds included, draws on one 25% cap, and each refund effect fires at most once per cast.
            if (World.Player(player).Caster != null
                && World.Player(player).Caster.TryRefundPayment(paymentEventId, sourceId, requested, out float granted))
            {
                record.RefundedAmount += granted;
                return granted;
            }

            float amount = World.Player(player).Stats.CapRefund(record.Paid, requested);
            record.Refunded = true;
            record.RefundedAmount = World.RefundMana(player, amount, sourceId);
            return record.RefundedAmount;
        }

        // ----- outgoing hits ----------------------------------------------------------------------------------

        /// <summary>
        /// Applies the attacker's outgoing damage modifiers to an original spell or melee hit before the pipeline: the
        /// spell or weapon damage stat and pending next-hit bonuses (consumed by this hit). Secondary, environment,
        /// prop, and rule damage are never modified.
        /// </summary>
        public void ModifyOutgoing(DamageRequest request)
        {
            if (request == null || request.Generation != 0 || request.Parts == null || request.Parts.Length == 0
                || request.Attacker < 0 || request.Attacker >= DuelWorld.PlayerCount || request.Attacker == request.Target)
            {
                return;
            }

            if (request.Kind != DamageKind.Direct && request.Kind != DamageKind.Splash && request.Kind != DamageKind.Melee)
            {
                return;
            }

            DuelPlayer attacker = World.Player(request.Attacker);
            float multiplier = 1f;
            if (request.IsSpellHit)
            {
                multiplier *= attacker.Stats.SpellDamageMultiplier;
            }
            else if (request.Kind == DamageKind.Melee)
            {
                multiplier *= attacker.Stats.WeaponDamageMultiplier;
            }

            float range = request.Range > 0f ? request.Range : Vec3.Distance(attacker.Position, World.Player(request.Target).Position);
            _hosts[request.Attacker].ConsumeHitBonuses(request, range, World.Tick, out float fraction, out float flat);
            multiplier *= Math.Max(0f, 1f + fraction);
            if (multiplier == 1f && flat == 0f)
            {
                return;
            }

            var parts = (DamagePart[])request.Parts.Clone();
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i].Amount = Math.Max(0f, parts[i].Amount * multiplier + (i == 0 ? flat : 0f));
            }

            request.Parts = parts;
        }

        // ----- action services --------------------------------------------------------------------------------

        internal void GainResource(int player, EffectResource resource, float amount, string sourceId)
        {
            if (resource == EffectResource.Mana)
            {
                World.RefundMana(player, amount, sourceId);
                return;
            }

            float gained = World.Player(player).Resources.GainStamina(amount);
            if (gained > 0f)
            {
                World.RecordEvent(new EffectEvent { Change = EffectChange.StaminaGained, SourceId = sourceId, Amount = gained }, player);
            }
        }

        internal void EmitCue(int player, Effect effect, string cue)
        {
            World.RecordEvent(new EffectEvent { Change = EffectChange.Cue, SourceId = effect.SourceId, EffectKey = effect.Key, Trigger = effect.Trigger, Cue = cue }, player);
        }

        internal void EmitActivation(int player, Effect effect, in TriggerEvent trigger)
        {
            World.RecordEvent(new EffectEvent
            {
                Change = EffectChange.Activated,
                SourceId = effect.SourceId,
                EffectKey = effect.Key,
                Trigger = trigger.Trigger,
                TriggerEventId = trigger.EventId,
                Amount = trigger.Amount,
            }, player);
        }

        /// <summary>A unique origin for triggers that are not hits, so only the 0.5 s interval limits them.</summary>
        internal int NextSyntheticOrigin() => -(++_synthetic);

        public void Hash(ref StateHasher h)
        {
            for (int i = 0; i < _hosts.Length; i++)
            {
                _hosts[i].Hash(ref h);
                _watch[i].Hash(ref h);
            }

            h.Add(_synthetic);
            h.Add(_payments.Count);
            for (int i = 0; i < _payments.Count; i++)
            {
                h.Add(_payments[i].PaymentEventId);
                h.Add(_payments[i].Paid);
                h.Add(_payments[i].Refunded);
            }
        }

        // ----- translation ------------------------------------------------------------------------------------

        private void Translate(DuelEvent e)
        {
            switch (e)
            {
                case PhaseEvent phase when phase.Phase == DuelPhase.Combat:
                    for (int p = 0; p < DuelWorld.PlayerCount; p++)
                    {
                        Add(EffectTrigger.CombatStart, p, -1, e);
                    }

                    break;
                case DamageEvent damage:
                    TranslateDamage(damage);
                    break;
                case GuardEvent guard:
                    TranslateGuard(guard);
                    break;
                case MovementEvent move:
                    if (move.Change == MovementChange.Jumped) Add(EffectTrigger.Jump, move.Player, -1, e);
                    else if (move.Change == MovementChange.Dodged) Add(EffectTrigger.Dodge, move.Player, -1, e);
                    else if (move.Change == MovementChange.Landed) Add(EffectTrigger.Land, move.Player, -1, e, move.Amount);
                    break;
                case MeleeEvent melee when melee.Change == MeleeChange.Hit:
                    Add(EffectTrigger.MeleeHit, melee.Player, melee.TargetPlayer, e, 0f, melee.SourceId);
                    break;
                case StatusEvent status:
                    TranslateStatus(status);
                    break;
                case FieldEvent field when field.Change == FieldChange.Created:
                    Add(EffectTrigger.FieldCreated, field.Player, -1, e, 0f, field.DefinitionId);
                    break;
                case SolidEvent solid when solid.Change == SolidChange.Destroyed && !World.Props.IsPropSolid(solid.SolidId):
                    Add(EffectTrigger.CoverDestroyed, solid.Player, -1, e);
                    break;
                case PropEvent prop when prop.Change == PropChange.Destroyed:
                    Add(EffectTrigger.PropDestroyed, prop.Player, -1, e, 0f, prop.DefinitionId);
                    break;
                case ResourceEvent resource when resource.Change == ResourceChange.ManaSpent:
                    Add(EffectTrigger.CastCommitted, resource.Player, -1, e, resource.Amount, resource.SourceId);
                    if (FindPayment(resource.Id) == null)
                    {
                        RecordPayment(resource.Player, resource.Id, resource.SourceId, resource.Amount);
                    }

                    break;
            }
        }

        // Hit triggers only from original damage that may carry procs (spec L51, L126, L173, L175, L672).
        private void TranslateDamage(DamageEvent d)
        {
            _damageById[d.Id] = d;
            if (d.Generation != 0 || d.Resolution == null || !(d.Dealt > 0f))
            {
                return;
            }

            bool hit = d.Kind == DamageKind.Direct || d.Kind == DamageKind.Splash || d.Kind == DamageKind.Melee;
            bool taken = hit || d.Kind == DamageKind.Status || d.Kind == DamageKind.Fall;
            if (!taken)
            {
                return;
            }

            bool direct = d.Kind == DamageKind.Direct || d.Kind == DamageKind.Melee;
            TriggerEvent t = HitTrigger(d);
            t.Player = d.Target;
            t.Other = d.Attacker;
            t.Trigger = EffectTrigger.DamageTaken;
            _queue.Add(t);
            if (direct)
            {
                t.Trigger = EffectTrigger.DirectHitTaken;
                _queue.Add(t);
            }

            if (d.Resolution.ShieldAbsorbed > 0f)
            {
                t.Trigger = EffectTrigger.ShieldAbsorbed;
                t.Amount = d.Resolution.ShieldAbsorbed;
                _queue.Add(t);
            }

            if (direct && d.Attacker >= 0 && d.Attacker < DuelWorld.PlayerCount && d.Attacker != d.Target)
            {
                TriggerEvent dealt = HitTrigger(d);
                dealt.Trigger = EffectTrigger.DirectHitDealt;
                dealt.Player = d.Attacker;
                dealt.Other = d.Target;
                _queue.Add(dealt);
            }
        }

        private static TriggerEvent HitTrigger(DamageEvent d)
        {
            var t = new TriggerEvent
            {
                Tick = d.Tick,
                EventId = d.Id,
                OriginEventId = d.OriginEventId,
                Amount = d.Dealt,
                Range = d.Range,
                SourceId = d.SourceId,
                DamageKind = d.Kind,
                IsSpellHit = (d.Kind == DamageKind.Direct || d.Kind == DamageKind.Splash) && d.SourceId != null
                    && d.SourceId.StartsWith("SPL-", StringComparison.Ordinal),
            };
            ChannelDamage[] channels = d.Resolution.Channels;
            if (channels != null)
            {
                for (int i = 0; i < channels.Length; i++)
                {
                    if (i == 0)
                    {
                        t.Channel = channels[i].Channel;
                    }

                    t.ChannelMask |= TriggerEvent.ChannelBit(channels[i].Channel);
                }
            }

            return t;
        }

        private void TranslateGuard(GuardEvent g)
        {
            if (g.Change == GuardChange.Started)
            {
                Add(EffectTrigger.GuardStarted, g.Player, -1, g);
                return;
            }

            if (g.Change != GuardChange.Blocked && g.Change != GuardChange.PerfectBlocked)
            {
                return;
            }

            int attacker = _damageById.TryGetValue(g.BlockedEventId, out DamageEvent hit) ? hit.Attacker : -1;
            int origin = hit != null ? hit.OriginEventId : g.BlockedEventId;
            if (g.Change == GuardChange.PerfectBlocked)
            {
                _queue.Add(new TriggerEvent { Trigger = EffectTrigger.PerfectGuard, Player = g.Player, Other = attacker, Tick = g.Tick, EventId = g.Id, OriginEventId = origin });
            }

            _queue.Add(new TriggerEvent { Trigger = EffectTrigger.GuardBlocked, Player = g.Player, Other = attacker, Tick = g.Tick, EventId = g.Id, OriginEventId = origin });
        }

        private void TranslateStatus(StatusEvent s)
        {
            EffectTrigger trigger;
            switch (s.Change)
            {
                case StatusChange.Applied:
                case StatusChange.Refreshed:
                    trigger = EffectTrigger.StatusApplied;
                    break;
                case StatusChange.Expired:
                case StatusChange.Removed:
                case StatusChange.Broken:
                    trigger = EffectTrigger.StatusRemoved;
                    break;
                default:
                    return;
            }

            _queue.Add(new TriggerEvent
            {
                Trigger = trigger,
                Player = s.Player,
                Other = s.SourcePlayer,
                Tick = s.Tick,
                EventId = s.Id,
                SourceId = s.DefinitionId,
                Status = s.Kind,
            });
        }

        private void Add(EffectTrigger trigger, int player, int other, DuelEvent e, float amount = 0f, string sourceId = null)
        {
            if (player < 0 || player >= DuelWorld.PlayerCount)
            {
                return;
            }

            _queue.Add(new TriggerEvent { Trigger = trigger, Player = player, Other = other, Tick = e.Tick, EventId = e.Id, Amount = amount, SourceId = sourceId });
        }

        // ----- polled state changes ---------------------------------------------------------------------------

        private void Poll(int player, bool fire)
        {
            DuelPlayer p = World.Player(player);
            PlayerWatch w = _watch[player];
            int now = World.Tick;

            bool sprinting = p.Motor.Sprinting;
            if (sprinting != w.Sprinting && fire)
            {
                AddPolled(sprinting ? EffectTrigger.SprintStarted : EffectTrigger.SprintStopped, player, now);
            }

            if (p.SelectedSlot != w.SelectedSlot && fire)
            {
                AddPolled(EffectTrigger.SpellSelectionChanged, player, now);
            }

            // A strike that finished its live window without hitting is a miss. A stun cancels only a strike still in
            // its windup, which leaves the attack idle before its cycle ends: no strike, no miss.
            MeleeState m = p.Melee;
            if (m.AttackSerial != w.MeleeSerialJudged && m.AttackSerial > 0 && now >= m.StrikeEndTick)
            {
                bool struck = m.Phase == MeleePhase.Strike || m.Phase == MeleePhase.Recovery || now >= m.EndTick;
                if (struck && !m.HitOpponent && fire)
                {
                    AddPolled(EffectTrigger.MeleeMiss, player, now, m.SourceId);
                }

                w.MeleeSerialJudged = m.AttackSerial;
            }

            SurfaceKind surface = World.SurfaceUnder(p).Kind;
            if (surface != w.Surface)
            {
                if (fire)
                {
                    TriggerEvent left = Polled(EffectTrigger.LeftSurface, player, now);
                    left.Surface = w.Surface;
                    _queue.Add(left);
                    TriggerEvent entered = Polled(EffectTrigger.EnteredSurface, player, now);
                    entered.Surface = surface;
                    _queue.Add(entered);
                }

                w.Surface = surface;
            }

            PollVolumes(player, p, w, now, fire);
            w.Sprinting = sprinting;
            w.SelectedSlot = p.SelectedSlot;
        }

        private void PollVolumes(int player, DuelPlayer p, PlayerWatch w, int now, bool fire)
        {
            _volumeScratch.Clear();
            if (World.Materials is ReactionMaterials materials && materials.World != null)
            {
                Vec3 chest = p.Position + new Vec3(0f, p.Motor.Height * 0.5f, 0f);
                materials.World.GetVolumesAt(chest, _volumeScratch);
            }

            List<int> current = w.Scratch;
            current.Clear();
            for (int i = 0; i < _volumeScratch.Count; i++)
            {
                current.Add(_volumeScratch[i].Id);
            }

            current.Sort();
            if (fire)
            {
                for (int i = 0; i < w.Volumes.Count; i++)
                {
                    if (current.BinarySearch(w.Volumes[i]) < 0)
                    {
                        TriggerEvent left = Polled(EffectTrigger.LeftVolume, player, now);
                        left.VolumeId = w.Volumes[i];
                        left.Volume = w.VolumeKinds[i];
                        _queue.Add(left);
                    }
                }

                for (int i = 0; i < current.Count; i++)
                {
                    if (w.Volumes.BinarySearch(current[i]) < 0)
                    {
                        TriggerEvent entered = Polled(EffectTrigger.EnteredVolume, player, now);
                        entered.VolumeId = current[i];
                        entered.Volume = KindOf(current[i]);
                        _queue.Add(entered);
                    }
                }
            }

            w.Volumes.Clear();
            w.VolumeKinds.Clear();
            for (int i = 0; i < current.Count; i++)
            {
                w.Volumes.Add(current[i]);
                w.VolumeKinds.Add(KindOf(current[i]));
            }
        }

        private VolumeKind KindOf(int volumeId)
        {
            for (int i = 0; i < _volumeScratch.Count; i++)
            {
                if (_volumeScratch[i].Id == volumeId)
                {
                    return _volumeScratch[i].Kind;
                }
            }

            return VolumeKind.Steam;
        }

        private void SnapshotWatch(int player)
        {
            DuelPlayer p = World.Player(player);
            PlayerWatch w = _watch[player];
            w.Sprinting = p.Motor.Sprinting;
            w.SelectedSlot = p.SelectedSlot;
            w.Surface = World.SurfaceUnder(p).Kind;
            w.MeleeSerialJudged = p.Melee.AttackSerial;
        }

        private void AddPolled(EffectTrigger trigger, int player, int now, string sourceId = null)
        {
            TriggerEvent t = Polled(trigger, player, now);
            t.SourceId = sourceId;
            _queue.Add(t);
        }

        private static TriggerEvent Polled(EffectTrigger trigger, int player, int now) =>
            new TriggerEvent { Trigger = trigger, Player = player, Other = -1, Tick = now };

        // ----- payments ---------------------------------------------------------------------------------------

        private ResourceEvent LastPayment(int player)
        {
            IReadOnlyList<DuelEvent> events = World.TickEvents;
            for (int i = events.Count - 1; i >= 0; i--)
            {
                if (events[i] is ResourceEvent r && r.Change == ResourceChange.ManaSpent && r.Player == player)
                {
                    return r;
                }
            }

            return null;
        }

        private void RecordPayment(int player, int paymentEventId, string spellId, float paid)
        {
            if (paymentEventId == 0 || FindPayment(paymentEventId) != null)
            {
                return;
            }

            _payments.Add(new PaymentRecord { PaymentEventId = paymentEventId, Player = player, SpellId = spellId, Paid = paid });
        }

        private PaymentRecord FindPayment(int paymentEventId)
        {
            for (int i = _payments.Count - 1; i >= 0; i--)
            {
                if (_payments[i].PaymentEventId == paymentEventId)
                {
                    return _payments[i];
                }
            }

            return null;
        }

        private sealed class PlayerWatch
        {
            public bool Sprinting;
            public int SelectedSlot;
            public SurfaceKind Surface;
            public int MeleeSerialJudged;
            public readonly List<int> Volumes = new List<int>();
            public readonly List<VolumeKind> VolumeKinds = new List<VolumeKind>();
            public readonly List<int> Scratch = new List<int>();

            public void Hash(ref StateHasher h)
            {
                h.Add(Sprinting);
                h.Add(SelectedSlot);
                h.Add((int)Surface);
                h.Add(MeleeSerialJudged);
                h.Add(Volumes.Count);
                for (int i = 0; i < Volumes.Count; i++)
                {
                    h.Add(Volumes[i]);
                }
            }
        }
    }
}
