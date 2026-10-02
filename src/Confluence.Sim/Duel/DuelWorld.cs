using System;
using System.Collections.Generic;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Combat;
using Confluence.Sim.Fields;
using Confluence.Sim.Hashing;
using Confluence.Sim.Input;
using Confluence.Sim.Movement;
using Confluence.Sim.Players;
using Confluence.Sim.Stats;
using Confluence.Sim.Status;
using Confluence.Sim.World;

namespace Confluence.Sim.Duel
{
    /// <summary>Construction options of a duel world.</summary>
    public sealed class DuelWorldOptions
    {
        /// <summary>Spell stage seam (tick-order steps 3-4). Defaults to no spells.</summary>
        public ISpellRuntime SpellRuntime { get; set; }

        /// <summary>Reactions stage seam (tick-order step 5). Defaults to no materials.</summary>
        public IWorldMaterials Materials { get; set; }

        /// <summary>Protected alcove phase length (spec L46: 5 s).</summary>
        public int AlcoveTicks { get; set; } = Ticks.FromSeconds(GlobalRules.Round.AlcoveSec);

        /// <summary>Maximum combat length (spec L47: 90 s).</summary>
        public int CombatTicks { get; set; } = Ticks.FromSeconds(GlobalRules.Round.CombatMaxSec);

        /// <summary>Keep every event for replays and the decisive sequence (spec L359, L419).</summary>
        public bool KeepEventLog { get; set; } = true;
    }

    /// <summary>
    /// The authoritative simulation of one duel: two players in one arena copy (spec L34). <see cref="Step"/>
    /// advances exactly one 60 Hz tick in the fixed order of docs/ARCHITECTURE.md:
    /// 1 inputs; 2 movement and displacement; 3 guard, swap, melee, casting; 4 deliveries; 5 reactions;
    /// 6 fields, summons, temporary solids; 7 statuses, regeneration, shield expiry; 8 boundary; 9 resolution.
    /// Every rule effect from later stages enters through the public APIs here so caps, budgets, and events stay
    /// in one place.
    /// </summary>
    public sealed class DuelWorld
    {
        public const int PlayerCount = 2;

        private readonly DuelPlayer[] _players = new DuelPlayer[PlayerCount];
        private readonly YawBox[] _alcoves = new YawBox[PlayerCount];
        private readonly List<DuelEvent> _tickEvents = new List<DuelEvent>();
        private readonly List<DuelEvent> _log = new List<DuelEvent>();
        private readonly bool _keepLog;
        private readonly BoundaryConstraint _boundary;
        private readonly MaterialsContext _materialsContext;
        private readonly List<ContactDamageRequest> _contactRequests = new List<ContactDamageRequest>();
        private readonly List<StatusInstance> _statusScratch = new List<StatusInstance>();
        private readonly List<DotPulse> _pulseScratch = new List<DotPulse>();
        private readonly List<OwnedEntity> _ownedScratch = new List<OwnedEntity>();
        private readonly List<int> _solidScratch = new List<int>();
        private readonly Capsule[] _volumeScratch = new Capsule[PlayerCount];
        private int _nextEventId = 1;

        public DuelWorld(ArenaLayout layout, DuelPlayerSetup player0, DuelPlayerSetup player1, DuelWorldOptions options = null)
        {
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            options = options ?? new DuelWorldOptions();
            AlcoveTicks = Math.Max(0, options.AlcoveTicks);
            CombatTicks = Math.Max(1, options.CombatTicks);
            _keepLog = options.KeepEventLog;
            Collision = CollisionWorld.FromLayout(layout);
            Materials = options.Materials ?? new NullWorldMaterials();
            Spells = options.SpellRuntime ?? new NullSpellRuntime();
            Owned = new OwnedEntityRegistry();
            Procs = new ProcBudget();
            History = new HitVolumeHistory(PlayerCount, SimRules.LagCompensation.MaxRewindTicks);
            _boundary = new BoundaryConstraint(this);
            _materialsContext = new MaterialsContext(this);

            _players[0] = new DuelPlayer(0, player0 ?? throw new ArgumentNullException(nameof(player0)));
            _players[1] = new DuelPlayer(1, player1 ?? throw new ArgumentNullException(nameof(player1)));
            for (int i = 0; i < PlayerCount; i++)
            {
                PlaceInAlcove(_players[i]);
            }

            Phase = AlcoveTicks > 0 ? DuelPhase.Alcove : DuelPhase.Combat;
            Spells.Attach(this);
        }

        // ----- state ----------------------------------------------------------------------------------------

        public ArenaLayout Layout { get; }
        public CollisionWorld Collision { get; }
        public IWorldMaterials Materials { get; }
        public ISpellRuntime Spells { get; }
        public OwnedEntityRegistry Owned { get; }
        public ProcBudget Procs { get; }
        public HitVolumeHistory History { get; }

        public int AlcoveTicks { get; }
        public int CombatTicks { get; }

        /// <summary>Index of the last simulated tick (0 before the first step).</summary>
        public int Tick { get; private set; }

        public DuelPhase Phase { get; private set; }

        /// <summary>Combat ticks elapsed (0 during the alcove).</summary>
        public int CombatTick => Math.Max(0, Tick - AlcoveTicks);

        /// <summary>Seconds since combat started; the boundary contour uses this clock (spec L48).</summary>
        public float CombatSeconds => CombatTick / (float)Ticks.PerSecond;

        public DuelResult Result { get; private set; }

        public bool IsOver => Phase == DuelPhase.Ended;

        public IReadOnlyList<DuelPlayer> Players => _players;

        public DuelPlayer Player(int index) => _players[index];

        public DuelPlayer Opponent(int index) => _players[1 - index];

        /// <summary>Events produced by the last step, in simulation order.</summary>
        public IReadOnlyList<DuelEvent> TickEvents => _tickEvents;

        /// <summary>Every event since construction (when the log is kept).</summary>
        public IReadOnlyList<DuelEvent> EventLog => _log;

        /// <summary>The alcove volume of a player (spec L46).</summary>
        public YawBox AlcoveOf(int player) => _alcoves[player];

        /// <summary>The legal boundary at the current combat time as a position constraint.</summary>
        public IPositionConstraint LegalBoundary => _boundary;

        // ----- the tick -------------------------------------------------------------------------------------

        public void Step(PlayerInput[] inputs)
        {
            if (inputs == null || inputs.Length != PlayerCount)
            {
                throw new ArgumentException("Exactly two inputs are required.", nameof(inputs));
            }

            Step(inputs[0], inputs[1]);
        }

        /// <summary>Advances exactly one tick. A finished duel does not advance.</summary>
        public void Step(in PlayerInput input0, in PlayerInput input1)
        {
            if (Phase == DuelPhase.Ended)
            {
                _tickEvents.Clear();
                return;
            }

            Tick++;
            _tickEvents.Clear();
            if (Phase == DuelPhase.Alcove && Tick > AlcoveTicks)
            {
                Phase = DuelPhase.Combat;
                Emit(new PhaseEvent { Phase = DuelPhase.Combat }, -1);
            }

            ApplyInput(_players[0], input0);                 // 1
            ApplyInput(_players[1], input1);
            for (int i = 0; i < PlayerCount; i++)            // 2
            {
                StepMovement(_players[i]);
            }

            RecordHitVolumes();
            for (int i = 0; i < PlayerCount; i++)            // 3
            {
                StepGuard(_players[i]);
            }

            for (int i = 0; i < PlayerCount; i++)
            {
                StepSwap(_players[i]);
            }

            for (int i = 0; i < PlayerCount; i++)
            {
                StepMelee(_players[i]);
            }

            for (int i = 0; i < PlayerCount; i++)
            {
                StepCasting(_players[i]);
            }

            Spells.AdvanceDeliveries();                      // 4
            StepMaterials();                                 // 5
            StepOwnedEntities();                             // 6
            for (int i = 0; i < PlayerCount; i++)            // 7
            {
                StepStatusesAndResources(_players[i]);
            }

            if (Phase == DuelPhase.Combat)                   // 8
            {
                for (int i = 0; i < PlayerCount; i++)
                {
                    StepBoundary(_players[i]);
                }
            }

            Resolve();                                       // 9
        }

        /// <summary>Deterministic hash of every authoritative value (spec L427).</summary>
        public ulong StateHash()
        {
            StateHasher h = StateHasher.Create(Layout.Seed);
            h.Add(Tick);
            h.Add((int)Phase);
            h.Add(_nextEventId);
            for (int i = 0; i < PlayerCount; i++)
            {
                _players[i].Hash(ref h);
            }

            Owned.Hash(ref h);
            Collision.Hash(ref h);
            Procs.Hash(ref h);
            Spells.Hash(ref h);
            Materials.Hash(ref h);
            h.Add(Result == null ? -2 : Result.Winner);
            return h.Value;
        }

        // ----- step 1: inputs -------------------------------------------------------------------------------

        private void ApplyInput(DuelPlayer p, in PlayerInput raw)
        {
            PlayerInput input = raw;
            input.MoveX = CMath.Clamp(Finite(input.MoveX), -1f, 1f);
            input.MoveY = CMath.Clamp(Finite(input.MoveY), -1f, 1f);
            input.YawDeg = CMath.WrapAngle(Finite(input.YawDeg));
            input.PitchDeg = CMath.Clamp(Finite(input.PitchDeg), -89f, 89f);
            if (input.SelectedSlot < 0 || input.SelectedSlot >= PlayerInput.SlotCount)
            {
                input.SelectedSlot = p.SelectedSlot;
            }

            p.Pressed = input.Buttons & ~p.PreviousButtons;
            p.PreviousButtons = input.Buttons;
            p.Input = input;
            p.SelectedSlot = input.SelectedSlot; // spec L106: no swap windup for spell selection
        }

        // ----- step 2: movement -----------------------------------------------------------------------------

        private void StepMovement(DuelPlayer p)
        {
            if (p.IsRecalling)
            {
                if (Tick < p.RecallEndTick)
                {
                    return; // the 0.5 s visible recall holds the player in place (spec L175)
                }

                p.RecallEndTick = -1;
                Emit(new MovementEvent { Change = MovementChange.RecallEnded, Position = p.Position }, p.Index);
            }

            MotorCommand cmd = BuildMotorCommand(p);
            CharacterMotor.Step(ref p.Motor, cmd, Collision, Ticks.Dt, out MotorReport report);
            if (report.StaminaSpent > 0f)
            {
                p.Resources.TrySpendStamina(Math.Min(report.StaminaSpent, p.Resources.Stamina), Tick);
            }

            if (report.Jumped)
            {
                Emit(new MovementEvent { Change = MovementChange.Jumped, Position = p.Position }, p.Index);
            }

            if (report.DodgeStarted)
            {
                p.Feats.OnDodge(Tick);
                Emit(new MovementEvent { Change = MovementChange.Dodged, Position = p.Position }, p.Index);
            }

            if (report.Landed && report.LandingDrop > 0f)
            {
                Emit(new MovementEvent { Change = MovementChange.Landed, Position = p.Position, Amount = report.LandingDrop }, p.Index);
                float fall = GlobalRules.Terrain.FallDamage(report.LandingDrop); // spec L175: 10 per meter beyond 3 m, cap 50
                if (fall > 0f)
                {
                    ApplyDamage(DamageRequest.Single(-1, p.Index, DamageKind.Fall, DamageSources.Fall, DamageChannel.Physical, fall));
                }
            }

            if (report.StoppedAtEdge)
            {
                Emit(new MovementEvent { Change = MovementChange.EdgeImpact, Position = p.Position, Amount = GlobalRules.Boundary.EdgeImpactDamage }, p.Index);
                ApplyDamage(DamageRequest.Single(-1, p.Index, DamageKind.Boundary, DamageSources.BoundaryEdge, DamageChannel.None,
                    GlobalRules.Boundary.EdgeImpactDamage));
            }

            if (report.DisplacementEnded && report.EndedDisplacement == DisplacementKind.Knockback)
            {
                RemoveStatusKind(p, StatusKind.Displaced, StatusChange.Expired);
            }

            if (Phase == DuelPhase.Combat)
            {
                p.Feats.OnMoved(report.SelfMovedDistance); // FEAT-005: valid combat movement
            }

            if (report.FellOutOfMap)
            {
                BeginRecall(p);
            }
        }

        private MotorCommand BuildMotorCommand(DuelPlayer p)
        {
            PlayerInput input = p.Input;
            CastingStatus casting = Spells.GetStatus(p.Index);
            var cmd = new MotorCommand
            {
                Move = new Vec2(input.MoveX, input.MoveY),
                YawDeg = input.YawDeg,
                PitchDeg = input.PitchDeg,
                JumpPressed = p.WasPressed(InputButtons.Jump),
                SprintHeld = p.IsHeld(InputButtons.Sprint),
                CrouchHeld = p.IsHeld(InputButtons.Crouch),
                DodgePressed = p.WasPressed(InputButtons.Dodge),
                Stamina = p.Resources.Stamina,
                CastSpeedFactor = casting.MovementFactor,
                Traction = p.Stats.Traction,
                Tuning = p.Stats.Motor, // gait bonuses (+20% cap) and stamina costs from the stat block (spec L80-L82, L124)
            };

            SurfaceInfo surface = SurfaceUnder(p);
            cmd.Surface = surface;
            cmd.SpeedMultiplier = 1f - StatBlock.EffectiveSlow(p.Statuses.StrongestSlow, surface.SlowFraction); // spec L116, L175

            MotorLocks locks = MotorLocks.None;
            if (!p.Alive || Phase == DuelPhase.Ended)
            {
                locks |= MotorLocks.All;
            }

            if (p.Statuses.IsStunned || p.Statuses.IsRooted)
            {
                locks |= MotorLocks.Move | MotorLocks.Jump | MotorLocks.Dodge | MotorLocks.Sprint; // spec L116
            }

            if (p.Melee.Busy || casting.Channeling)
            {
                locks |= MotorLocks.Sprint; // spec L80: sprint disallows channels and normal melee strikes
            }

            if (p.Guard.InReleaseWindow(Tick))
            {
                locks |= MotorLocks.Dodge; // spec L104: the release window stays punishable
            }

            cmd.Locks = locks;
            if (Phase == DuelPhase.Alcove)
            {
                cmd.Confined = true; // spec L46: movement within the alcove
                cmd.Confinement = _alcoves[p.Index];
            }
            else
            {
                cmd.DisplacementBounds = _boundary; // spec L51: forced displacement stops at the edge
            }

            return cmd;
        }

        /// <summary>Ground surface under a player: ice solids are slippery; otherwise the materials hook decides (spec L175).</summary>
        public SurfaceInfo SurfaceUnder(DuelPlayer p)
        {
            if (p.Motor.Grounded && p.Motor.GroundSolidId >= 0)
            {
                CollisionSolid ground = Collision.Find(p.Motor.GroundSolidId);
                if (ground != null && ground.Slippery)
                {
                    return new SurfaceInfo { Kind = SurfaceKind.Ice };
                }
            }

            return p.Motor.Grounded ? Materials.QuerySurface(p.Position) : SurfaceInfo.Normal;
        }

        private void RecordHitVolumes()
        {
            for (int i = 0; i < PlayerCount; i++)
            {
                _volumeScratch[i] = _players[i].HitVolume;
            }

            History.Record(Tick, _volumeScratch);
        }

        // Outside-map fall (spec L175): 40 health penalty, nearest valid platform, 0.5 s visible recall, no procs.
        private void BeginRecall(DuelPlayer p)
        {
            Emit(new MovementEvent { Change = MovementChange.FellOutOfMap, Position = p.Position }, p.Index);
            Vec3 near = new Vec3(p.Position.X, p.Motor.LastGroundedPosition.Y, p.Position.Z);
            Vec3 target;
            IPositionConstraint legal = Phase == DuelPhase.Combat ? _boundary : null;
            if (!Collision.TryFindNearestStandablePoint(near, CharacterMotor.Radius, GlobalRules.Movement.StandingHeight, legal, out target))
            {
                target = SpawnOf(p).Position;
            }

            float yaw = p.Motor.YawDeg;
            int dodgeCooldown = p.Motor.DodgeCooldownTicks; // a fall never refreshes the dodge
            p.Motor = MotorState.At(target, yaw);
            p.Motor.DodgeCooldownTicks = dodgeCooldown;
            CharacterMotor.SnapToGround(ref p.Motor, Collision);
            RemoveStatusKind(p, StatusKind.Displaced, StatusChange.Removed);
            p.RecallEndTick = Tick + Ticks.FromSeconds(GlobalRules.Terrain.OutOfMapRecallSec);
            Emit(new MovementEvent { Change = MovementChange.RecallStarted, Position = target, Amount = GlobalRules.Terrain.OutOfMapRecallPenalty }, p.Index);
            ApplyDamage(DamageRequest.Single(-1, p.Index, DamageKind.Recall, DamageSources.Recall, DamageChannel.None,
                GlobalRules.Terrain.OutOfMapRecallPenalty));
        }

        // ----- step 3: guard, swap, melee, casting ----------------------------------------------------------

        private void StepGuard(DuelPlayer p)
        {
            GuardState g = p.Guard;
            if (g.Active)
            {
                if (!p.IsHeld(InputButtons.Guard) || g.HoldExpired(Tick, p.Stats.GuardMaxHoldTicks) || p.Statuses.IsStunned || !p.Alive || p.IsRecalling)
                {
                    g.Release(Tick, p.Stats.GuardCooldownTicks);
                    Emit(new GuardEvent { Change = GuardChange.Released }, p.Index);
                }

                return;
            }

            if (!p.WasPressed(InputButtons.Guard) || !g.CanStart(Tick) || !CanAct(p) || p.Melee.Busy || p.IsSwapping)
            {
                return;
            }

            if (p.Resources.TrySpendStamina(p.Stats.GuardStaminaCost, Tick)) // spec L104: 25 stamina upfront (after modifiers)
            {
                g.Start(Tick);
                Emit(new GuardEvent { Change = GuardChange.Started }, p.Index);
            }
        }

        private void StepSwap(DuelPlayer p)
        {
            if (p.IsSwapping)
            {
                if (Tick >= p.SwapEndTick)
                {
                    p.ActiveWeapon = p.ActiveWeapon == EquipmentSlot.MainFocus ? EquipmentSlot.Sidearm : EquipmentSlot.MainFocus;
                    p.SwapEndTick = -1;
                    Emit(new MovementEvent { Change = MovementChange.SwapCompleted, Position = p.Position, SourceId = p.CurrentMeleeSource }, p.Index);
                }

                return;
            }

            if (!p.WasPressed(InputButtons.SwapFocus) || !CanAct(p))
            {
                return;
            }

            // spec L106: equip and swap take 0.35 s and cannot cancel a recovery.
            CastingStatus casting = Spells.GetStatus(p.Index);
            if (p.Melee.Busy || casting.Busy || casting.InRecovery || p.Guard.Active)
            {
                return;
            }

            p.SwapEndTick = Tick + Ticks.FromSeconds(GlobalRules.Melee.EquipSwapSec);
            Emit(new MovementEvent { Change = MovementChange.SwapStarted, Position = p.Position }, p.Index);
        }

        private void StepMelee(DuelPlayer p)
        {
            MeleeState m = p.Melee;
            if (m.Busy)
            {
                if (p.Statuses.IsStunned && m.Phase == MeleePhase.Windup)
                {
                    m.Cancel(); // a stun interrupts a strike still winding up
                    return;
                }

                if (m.Advance(Tick, out int strikeIndex))
                {
                    ResolveStrike(p, strikeIndex);
                }

                return;
            }

            if (!p.WasPressed(InputButtons.Melee) || !CanAct(p))
            {
                return;
            }

            // spec L80: sprint disallows normal melee strikes; guard, swaps, and casts are separate actions.
            if (p.Motor.Sprinting || p.Guard.Active || p.Guard.InReleaseWindow(Tick) || p.IsSwapping || Spells.GetStatus(p.Index).Busy)
            {
                return;
            }

            MeleeProfile profile = p.Stats.EffectiveMelee(p.CurrentMeleeProfile); // spec L100-L102 after modifiers
            if (!p.Resources.TrySpendStamina(profile.Stamina, Tick))
            {
                return;
            }

            var started = new MeleeEvent { Change = MeleeChange.Started, ProfileName = profile.Name, SourceId = p.CurrentMeleeSource };
            Emit(started, p.Index);
            m.Begin(profile, p.CurrentMeleeSource, Tick, p.Motor.YawDeg, p.Motor.PitchDeg, started.Id);
            started.AttackSerial = m.AttackSerial;
            BreakInvisibility(p); // spec L120: invisibility ends on attack
        }

        // The swept strike volume follows the body but keeps the committed aim (spec L102: no snap or tracking).
        private void ResolveStrike(DuelPlayer attacker, int strikeTickIndex)
        {
            MeleeState m = attacker.Melee;
            DuelPlayer target = Opponent(attacker.Index);
            MeleeProfile profile = m.Profile;
            float arc = MeleeProfiles.ArcHalfDeg(profile);
            int samples = SimRules.Melee.SamplesPerTick;
            int totalSamples = SimRules.Melee.StrikeActiveTicks * samples;
            Vec3 origin = attacker.Eye - new Vec3(0f, SimRules.Melee.StrikeDropBelowEye, 0f);
            Capsule targetVolume = TargetVolumeFor(attacker, target.Index);

            for (int j = 0; j < samples; j++)
            {
                float u = totalSamples > 1 ? (strikeTickIndex * samples + j) / (float)(totalSamples - 1) : 0.5f;
                float yaw = m.CommittedYawDeg - arc + 2f * arc * u;
                Vec3 dir = CMath.ForwardFromYawPitch(yaw, m.CommittedPitchDeg);
                float reach = profile.Reach;
                if (Collision.SegmentCast(origin, origin + dir * reach, SolidMask.Movement, out WorldHit wall))
                {
                    reach = wall.Distance;
                    if (m.TryContactSolid(wall.SolidId))
                    {
                        Emit(new MeleeEvent { Change = MeleeChange.ContactedSolid, ProfileName = profile.Name, SourceId = m.SourceId, AttackSerial = m.AttackSerial, SolidId = wall.SolidId }, attacker.Index);
                        DamageSolid(wall.SolidId, profile.Damage, attacker.Index);
                    }
                }

                if (m.HitOpponent || !target.Alive)
                {
                    continue;
                }

                if (SegmentHitsCapsule(origin, dir, reach, targetVolume))
                {
                    m.MarkOpponentHit(); // spec L102: one attack damages its opponent once
                    DamageRequest hit = DamageRequest.Single(attacker.Index, target.Index, DamageKind.Melee, m.SourceId, DamageChannel.Physical, profile.Damage)
                        .From(attacker.Position);
                    hit.Range = Vec3.Distance(attacker.Position, target.Position);
                    Emit(new MeleeEvent { Change = MeleeChange.Hit, ProfileName = profile.Name, SourceId = m.SourceId, AttackSerial = m.AttackSerial, TargetPlayer = target.Index }, attacker.Index);
                    ApplyDamage(hit);
                }
            }
        }

        private void StepCasting(DuelPlayer p)
        {
            CastPermissions permissions = CastPermissionsFor(p);
            Spells.UpdateCasting(p.Index, p.Input, permissions);
            if (Spells.GetStatus(p.Index).Channeling)
            {
                p.Resources.MarkChanneling(Tick); // spec L78: channels continuously count as spending
            }
        }

        /// <summary>What casting may do this tick (alcove ward, death, recall, stun, silence, guard release, melee, swap).</summary>
        public CastPermissions CastPermissionsFor(DuelPlayer p)
        {
            CastBlockReason reason = CastBlockReason.None;
            if (Phase == DuelPhase.Ended)
            {
                reason = CastBlockReason.DuelEnded;
            }
            else if (Phase == DuelPhase.Alcove)
            {
                reason = CastBlockReason.AlcoveWard;
            }
            else if (!p.Alive)
            {
                reason = CastBlockReason.Dead;
            }
            else if (p.IsRecalling)
            {
                reason = CastBlockReason.Recalling;
            }
            else if (p.Statuses.IsStunned)
            {
                reason = CastBlockReason.Stunned;
            }
            else if (p.Statuses.IsSilenced)
            {
                reason = CastBlockReason.Silenced;
            }
            else if (p.Guard.InReleaseWindow(Tick))
            {
                reason = CastBlockReason.GuardRelease;
            }
            else if (p.Melee.Busy)
            {
                reason = CastBlockReason.Melee;
            }
            else if (p.IsSwapping)
            {
                reason = CastBlockReason.Swapping;
            }

            bool blocked = reason != CastBlockReason.None;
            bool itemsBlocked = blocked && reason != CastBlockReason.Silenced;
            return new CastPermissions
            {
                CanCast = !blocked,
                CanUseSignature = !blocked,
                CanUseItems = !itemsBlocked,
                CanChannel = !blocked && !p.Motor.Sprinting, // spec L80: sprint disallows channels
                Reason = reason,
            };
        }

        // Common actions need combat, life, freedom from stun and recall (the alcove ward forbids combat actions, spec L46).
        private bool CanAct(DuelPlayer p)
        {
            return Phase == DuelPhase.Combat && p.Alive && !p.Statuses.IsStunned && !p.IsRecalling;
        }

        // ----- step 5: materials ----------------------------------------------------------------------------

        private void StepMaterials()
        {
            _contactRequests.Clear();
            Materials.Step(_materialsContext);
            for (int i = 0; i < _contactRequests.Count; i++)
            {
                ContactDamageRequest c = _contactRequests[i];
                if (c.Target < 0 || c.Target >= PlayerCount)
                {
                    continue;
                }

                DamageRequest req = DamageRequest.Single(c.SourcePlayer, c.Target, DamageKind.Environment, c.SourceId, c.Channel, c.Amount);
                req.OriginEventId = c.OriginEventId;
                req.Reaction = c.Reaction;
                ApplyDamage(req);
            }

            _contactRequests.Clear();
        }

        // ----- step 6: fields, summons, temporary solids ----------------------------------------------------

        private void StepOwnedEntities()
        {
            _ownedScratch.Clear();
            _ownedScratch.AddRange(Owned.Entities);
            for (int i = 0; i < _ownedScratch.Count; i++)
            {
                OwnedEntity e = _ownedScratch[i];
                if (e.Active && e.Behavior != null)
                {
                    e.Behavior.OnTick(this, e);
                }
            }

            _ownedScratch.Clear();
            Owned.Expire(Tick, _ownedScratch);
            for (int i = 0; i < _ownedScratch.Count; i++)
            {
                EmitField(_ownedScratch[i], FieldChange.Expired, -1);
            }

            _solidScratch.Clear();
            Collision.ExpireTemporarySolids(Tick, _solidScratch);
            for (int i = 0; i < _solidScratch.Count; i++)
            {
                Emit(new SolidEvent { Change = SolidChange.Expired, SolidId = _solidScratch[i] }, -1);
            }

            // spec L678: at 85 s map-owned soft cover in the final center retires.
            if (Phase == DuelPhase.Combat && CombatTick == Ticks.FromSeconds(GlobalRules.Boundary.FinalCoverRetireSec))
            {
                IReadOnlyList<CollisionSolid> solids = Collision.Solids;
                for (int i = 0; i < solids.Count; i++)
                {
                    if (solids[i].RetiresAtFinal && Collision.Remove(solids[i].Id))
                    {
                        Emit(new SolidEvent { Change = SolidChange.Retired, SolidId = solids[i].Id }, -1);
                    }
                }
            }
        }

        // ----- step 7: statuses, regeneration, shields ------------------------------------------------------

        private void StepStatusesAndResources(DuelPlayer p)
        {
            _pulseScratch.Clear();
            _statusScratch.Clear();
            p.Statuses.Tick(Tick, _pulseScratch, _statusScratch);
            for (int i = 0; i < _pulseScratch.Count; i++)
            {
                StatusInstance s = _pulseScratch[i].Status;
                DamageChannel channel = s.Channel != DamageChannel.None ? s.Channel : DefaultDotChannel(s.Kind);
                DamageRequest dot = DamageRequest.Single(s.SourcePlayer, p.Index, DamageKind.Status, s.DefinitionId, channel, _pulseScratch[i].Amount);
                dot.OriginEventId = s.OriginEventId;
                ApplyDamage(dot);
            }

            for (int i = 0; i < _statusScratch.Count; i++)
            {
                EmitStatus(p, _statusScratch[i], StatusChange.Expired);
            }

            p.Resources.Regenerate(Tick, p.Stats.ManaRegenPerSecond, p.Stats.StaminaRegenPerSecond, p.Stats.ManaRegenDelayTicks, p.Stats.StaminaRegenDelayTicks);
            float lost = p.Shields.Expire(Tick);
            if (lost > 0f)
            {
                Emit(new ShieldEvent { Change = ShieldChange.Expired, Amount = lost, TotalAfter = p.Shields.Total }, p.Index);
            }

            p.Healing.Advance(Tick);
        }

        private static DamageChannel DefaultDotChannel(StatusKind kind)
        {
            switch (kind)
            {
                case StatusKind.Burn: return DamageChannel.Thermal;
                case StatusKind.Venom: return DamageChannel.Corrosive;
                default: return DamageChannel.Physical;
            }
        }

        // ----- step 8: boundary -----------------------------------------------------------------------------

        private void StepBoundary(DuelPlayer p)
        {
            if (!p.Alive)
            {
                return;
            }

            bool inside = Layout.Boundary == null || Layout.Boundary.Contains(p.Position, CombatSeconds);
            float damage = p.Boundary.Step(inside, out bool exited, out bool returned, out bool reset, out bool resetAfterDamage);
            if (exited)
            {
                Emit(new BoundaryEvent { Change = BoundaryChange.Exited }, p.Index);
            }

            if (returned)
            {
                Emit(new BoundaryEvent { Change = BoundaryChange.Returned }, p.Index);
            }

            if (reset)
            {
                Emit(new BoundaryEvent { Change = BoundaryChange.EscalationReset }, p.Index);
                if (resetAfterDamage && CombatSeconds >= Layout.Boundary.ContractStartSec)
                {
                    p.Feats.OnBoundaryReturn(); // FEAT-024: returned inside the closing boundary after its damage
                }
            }

            if (damage > 0f)
            {
                ApplyDamage(DamageRequest.Single(-1, p.Index, DamageKind.Boundary, DamageSources.Boundary, DamageChannel.None, damage));
            }
        }

        // ----- step 9: resolution ---------------------------------------------------------------------------

        private void Resolve()
        {
            bool dead0 = !_players[0].Alive;
            bool dead1 = !_players[1].Alive;
            if (dead0 && dead1)
            {
                End(DuelOutcome.Draw, -1, DuelEndReason.SimultaneousDeath); // spec L53: same-step lethal outcomes draw
                return;
            }

            if (dead0 || dead1)
            {
                End(DuelOutcome.Win, dead0 ? 1 : 0, DuelEndReason.Death);
                return;
            }

            bool s0 = _players[0].Surrendered;
            bool s1 = _players[1].Surrendered;
            if (s0 || s1)
            {
                // spec L53: a surrender is a normal loss.
                if (s0 && s1)
                {
                    End(DuelOutcome.Draw, -1, DuelEndReason.Surrender);
                }
                else
                {
                    End(DuelOutcome.Win, s0 ? 1 : 0, DuelEndReason.Surrender);
                }

                return;
            }

            if (Phase == DuelPhase.Combat && CombatTick >= CombatTicks)
            {
                int winner = TimerJudgement(out _);
                End(winner < 0 ? DuelOutcome.Draw : DuelOutcome.Win, winner, DuelEndReason.Timer);
            }
        }

        /// <summary>
        /// The 90 s judgement (spec L53): health over maximum health, excluding shields and borrowed health. A difference
        /// below 0.5 percentage points is a draw (-1); otherwise the greater fraction wins. Also drives the final 15 s
        /// equal/ahead/behind indicator.
        /// </summary>
        public int TimerJudgement(out float difference)
        {
            float f0 = Math.Max(0f, _players[0].Resources.HealthFraction);
            float f1 = Math.Max(0f, _players[1].Resources.HealthFraction);
            difference = f0 - f1;
            if (Math.Abs(difference) < GlobalRules.Round.DrawThresholdFraction)
            {
                return -1;
            }

            return difference > 0f ? 0 : 1;
        }

        /// <summary>The equal/ahead/behind indicator is shown throughout the final 15 s of combat (spec L53).</summary>
        public bool JudgementIndicatorVisible => Phase == DuelPhase.Combat && CombatSeconds >= GlobalRules.Round.JudgementIndicatorStartSec;

        /// <summary>The timer judgement from one player's view: 1 ahead, 0 equal (a draw), -1 behind (spec L53).</summary>
        public int JudgementIndicator(int player)
        {
            int leader = TimerJudgement(out _);
            return leader < 0 ? 0 : leader == player ? 1 : -1;
        }

        private void End(DuelOutcome outcome, int winner, DuelEndReason reason)
        {
            Phase = DuelPhase.Ended;
            for (int i = 0; i < PlayerCount; i++)
            {
                Spells.Interrupt(i, InterruptCause.DuelEnded);
            }

            var end = new DuelEndEvent
            {
                Outcome = outcome,
                Winner = winner,
                Reason = reason,
                HealthFraction0 = _players[0].Resources.HealthFraction,
                HealthFraction1 = _players[1].Resources.HealthFraction,
            };
            Emit(end, winner);
            Result = DuelResult.Build(this, end);
        }

        /// <summary>Surrender: a normal loss resolved at the end of the next step (spec L53).</summary>
        public void Surrender(int player)
        {
            if (Phase != DuelPhase.Ended)
            {
                _players[player].Surrendered = true;
            }
        }

        // ----- damage, healing, shields ---------------------------------------------------------------------

        /// <summary>
        /// Resolves a hit through the pipeline (spec L112) and applies its consequences: guard events and the perfect
        /// guard mana, feats, lifesteal within the healing budget, invisibility breaking, and guard-reduced knockback.
        /// Returns null when nothing can be damaged (alcove ward, ended duel, refused secondary generation).
        /// </summary>
        public DamageEvent ApplyDamage(DamageRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (Phase != DuelPhase.Combat || request.Target < 0 || request.Target >= PlayerCount)
            {
                return null; // spec L46: the alcove ward prevents damage
            }

            if (request.Generation > GlobalRules.Triggers.SecondaryGenerations || request.TotalAmount <= 0f)
            {
                return null; // spec L126: one generation of secondary effects
            }

            DuelPlayer target = _players[request.Target];
            DuelPlayer attacker = request.Attacker >= 0 && request.Attacker < PlayerCount ? _players[request.Attacker] : null;
            DamageResolution resolution = DamagePipeline.Resolve(request, target, Tick);
            var ev = new DamageEvent
            {
                Attacker = request.Attacker,
                Target = request.Target,
                Kind = request.Kind,
                SourceId = request.SourceId,
                Generation = request.Generation,
                Reaction = request.Reaction,
                Resolution = resolution,
                TargetPosition = target.Position,
                Range = request.Range > 0f ? request.Range : attacker != null ? Vec3.Distance(attacker.Position, target.Position) : 0f,
            };
            Emit(ev, request.Target);
            ev.OriginEventId = request.OriginEventId != 0 ? request.OriginEventId : ev.Id;

            if (resolution.Guard != GuardOutcome.None)
            {
                OnGuardBlock(target, ev, resolution.Guard == GuardOutcome.PerfectGuard);
            }

            if (resolution.ShieldAbsorbed > 0f)
            {
                target.Feats.OnShieldAbsorbed(resolution.ShieldAbsorbed); // FEAT-017
            }

            bool againstOpponent = attacker != null && attacker != target;
            if (againstOpponent)
            {
                CreditAttacker(attacker, request, ev);
            }

            if (resolution.Dealt > 0f)
            {
                BreakInvisibility(target); // spec L120: invisibility ends on damage
            }

            if (resolution.Lethal)
            {
                Spells.Interrupt(request.Target, InterruptCause.Death);
            }

            if (resolution.Knockback > 0f && !request.BypassesMitigation)
            {
                Vec3 dir = request.KnockbackDirection.LengthXZ() > 0f
                    ? request.KnockbackDirection
                    : request.HasSourcePosition ? target.Position - request.SourcePosition : Vec3.Zero;
                ApplyKnockback(request.Target, request.Attacker, dir, resolution.Knockback, request.SourceId);
            }

            return ev;
        }

        private void OnGuardBlock(DuelPlayer defender, DamageEvent hit, bool perfect)
        {
            float mana = 0f;
            if (perfect && defender.Guard.TryClaimPerfectReward())
            {
                mana = defender.Resources.GainMana(GlobalRules.Guard.PerfectManaGain); // spec L104: 5 mana once per guard
                defender.Feats.OnPerfectGuard(); // FEAT-003
            }

            Emit(new GuardEvent { Change = perfect ? GuardChange.PerfectBlocked : GuardChange.Blocked, BlockedEventId = hit.Id, ManaGained = mana }, defender.Index);
        }

        private void CreditAttacker(DuelPlayer attacker, DamageRequest request, DamageEvent ev)
        {
            DamageResolution r = ev.Resolution;
            FeatTracker feats = attacker.Feats;
            feats.OnDamageDealt(r.Dealt, DamageChannel.None, 0f);
            if (r.Channels != null)
            {
                for (int i = 0; i < r.Channels.Length; i++)
                {
                    feats.OnDamageDealt(0f, r.Channels[i].Channel, r.Channels[i].Mitigated);
                }
            }

            if (request.CanTriggerEffects)
            {
                feats.OnOriginalHit(Tick, request.IsSpellHit, request.Kind == DamageKind.Melee,
                    request.IsSpellHit && request.Kind == DamageKind.Direct, request.SourceId, ev.Range);
            }

            if (request.Reaction == ReactionTag.Conductivity)
            {
                feats.OnConductivityDamage(r.Dealt); // FEAT-020
            }

            if (request.Kind == DamageKind.Prop)
            {
                feats.OnPropHit(); // FEAT-021
            }

            // spec L114: lifesteal from direct damage only, at most 10%, 20 per second, within the 120 budget.
            float lifesteal = attacker.Stats.Lifesteal;
            if (request.IsDirectHit && lifesteal > 0f && r.Dealt > 0f)
            {
                Heal(attacker.Index, r.Dealt * lifesteal, "RULE:LIFESTEAL", true);
            }
        }

        /// <summary>
        /// Direct healing within the shared budget (spec L114): at most 35 per second and 120 per duel per player;
        /// lifesteal also at most 20 per second. Returns the health restored.
        /// </summary>
        public float Heal(int player, float amount, string sourceId, bool lifesteal = false)
        {
            DuelPlayer p = _players[player];
            if (Phase == DuelPhase.Ended || !p.Alive || !(amount > 0f))
            {
                return 0f;
            }

            float missing = Math.Max(0f, p.Resources.MaxHealth - p.Resources.Health);
            float granted = p.Healing.Grant(Math.Min(amount, missing), lifesteal, Tick);
            float restored = p.Resources.RestoreHealth(granted);
            if (restored > 0f)
            {
                p.Feats.OnHealthRestored(restored); // FEAT-018
            }

            Emit(new HealEvent { SourceId = sourceId, Lifesteal = lifesteal, Requested = amount, Restored = restored, BudgetRemaining = p.Healing.RemainingDuel }, player);
            return restored;
        }

        /// <summary>
        /// Grants shield capacity (spec L114, L579): timed shields last at most 8 s; reserve shields persist until
        /// broken; all share the 100 cap. Returns the capacity added.
        /// </summary>
        public float GrantShield(int player, string sourceId, float amount, int durationTicks, bool reserve = false)
        {
            DuelPlayer p = _players[player];
            if (Phase == DuelPhase.Ended)
            {
                return 0f;
            }

            float added = p.Shields.Add(sourceId, amount, durationTicks, reserve, Tick);
            if (added > 0f)
            {
                Emit(new ShieldEvent { Change = ShieldChange.Gained, SourceId = sourceId, Amount = added, Reserve = reserve, TotalAfter = p.Shields.Total }, player);
            }

            return added;
        }

        // ----- statuses -------------------------------------------------------------------------------------

        /// <summary>
        /// Applies a status under the global rules (spec L116-L120). Hard control during immunity applies nothing and
        /// is not queued. A stun or silence interrupts the target's cast.
        /// </summary>
        public StatusApplyResult ApplyStatus(int target, in StatusApplication application)
        {
            if (Phase != DuelPhase.Combat)
            {
                return new StatusApplyResult { Outcome = StatusOutcome.Rejected };
            }

            DuelPlayer p = _players[target];
            StatusApplyResult result = p.Statuses.Apply(application, Tick);
            switch (result.Outcome)
            {
                case StatusOutcome.Applied:
                    EmitStatus(p, result.Instance, StatusChange.Applied);
                    break;
                case StatusOutcome.Refreshed:
                    EmitStatus(p, result.Instance, StatusChange.Refreshed);
                    break;
                case StatusOutcome.BlockedByImmunity:
                case StatusOutcome.BlockedByControlWindow:
                    Emit(new StatusEvent
                    {
                        Change = result.Outcome == StatusOutcome.BlockedByImmunity ? StatusChange.BlockedByImmunity : StatusChange.BlockedByControlWindow,
                        Kind = application.Kind,
                        DefinitionId = application.DefinitionId,
                        SourcePlayer = application.SourcePlayer,
                    }, target);
                    break;
            }

            if (result.Succeeded && (application.Kind == StatusKind.Stun || application.Kind == StatusKind.Silence))
            {
                InterruptCause cause = application.Kind == StatusKind.Stun ? InterruptCause.Stun : InterruptCause.Silence;
                if (Spells.Interrupt(target, cause) && application.SourcePlayer >= 0 && application.SourcePlayer != target)
                {
                    _players[application.SourcePlayer].Feats.OnInterrupt(); // FEAT-019
                }
            }

            return result;
        }

        /// <summary>
        /// Dispels the target's oldest removable hostile status, or the one selected (spec L118). Never removes control
        /// immunity, boundary rules, or terrain. A player removing hostile statuses from themselves counts for FEAT-023.
        /// </summary>
        public StatusInstance Dispel(int target, int initiator, StatusKind? kind = null, string definitionId = null)
        {
            DuelPlayer p = _players[target];
            StatusInstance removed = p.Statuses.Dispel(Tick, kind, definitionId);
            if (removed == null)
            {
                return null;
            }

            EmitStatus(p, removed, StatusChange.Removed);
            if (initiator == target)
            {
                p.Feats.OnHostileStatusRemoved();
            }

            return removed;
        }

        /// <summary>Ends a player's invisibility (attack, cast, signature, or damage; spec L120).</summary>
        public void BreakInvisibility(DuelPlayer p) => RemoveStatusKind(p, StatusKind.Invisibility, StatusChange.Broken);

        /// <summary>The spell stage reports a cast, attack, or signature activation (spec L120).</summary>
        public void NotifyOffensiveAction(int player) => BreakInvisibility(_players[player]);

        private void RemoveStatusKind(DuelPlayer p, StatusKind kind, StatusChange change)
        {
            _statusScratch.Clear();
            p.Statuses.RemoveKind(kind, Tick, _statusScratch);
            for (int i = 0; i < _statusScratch.Count; i++)
            {
                EmitStatus(p, _statusScratch[i], change);
            }

            _statusScratch.Clear();
        }

        // ----- displacement, dashes, teleports --------------------------------------------------------------

        /// <summary>
        /// Knockback (spec L173): at most 4 m per effect. Hostile displacement that prevents deliberate movement is hard
        /// control, so control immunity blocks it (damage still applies) and it lasts at most 0.75 s.
        /// </summary>
        public bool ApplyKnockback(int target, int sourcePlayer, Vec3 direction, float distance, string sourceId)
        {
            DuelPlayer p = _players[target];
            distance = Math.Min(distance, GlobalRules.Terrain.PlayerKnockbackCap);
            Vec3 dir = new Vec3(direction.X, 0f, direction.Z).Normalized();
            if (Phase != DuelPhase.Combat || !(distance > 0f) || dir.LengthSquared() == 0f || !p.Alive || p.IsRecalling)
            {
                return false;
            }

            int ticks = Math.Max(1, (int)MathF.Ceiling(distance / SimRules.Displacement.KnockbackSpeed * Ticks.PerSecond));
            ticks = Math.Min(ticks, SimRules.Displacement.MaxHostileTicks);
            if (sourcePlayer != target)
            {
                StatusApplyResult control = ApplyStatus(target, StatusApplication.Of(StatusKind.Displaced, sourceId, sourcePlayer, ticks));
                if (!control.Succeeded)
                {
                    Emit(new MovementEvent { Change = MovementChange.KnockbackBlockedByImmunity, Position = p.Position, Amount = distance, SourceId = sourceId }, target);
                    return false;
                }

                ticks = Math.Max(1, Math.Min(ticks, control.Instance.ExpireTick - Tick));
                if (Spells.Interrupt(target, InterruptCause.Displacement) && sourcePlayer >= 0 && sourcePlayer < PlayerCount)
                {
                    _players[sourcePlayer].Feats.OnInterrupt(); // FEAT-019: hostile displacement interrupted a committed cast
                }
            }

            float speed = distance / (ticks * Ticks.Dt);
            CharacterMotor.BeginDisplacement(ref p.Motor, DisplacementKind.Knockback, dir * speed, ticks);
            Emit(new MovementEvent { Change = MovementChange.KnockbackStarted, Position = p.Position, Amount = distance, SourceId = sourceId }, target);
            return true;
        }

        /// <summary>A self dash of at most 8 m (spec L124), clamped at the legal boundary.</summary>
        public bool TryDash(int player, Vec3 direction, float distance, int ticks, string sourceId)
        {
            DuelPlayer p = _players[player];
            distance = Math.Min(distance, GlobalRules.Modifiers.MaxDashDistance);
            Vec3 dir = new Vec3(direction.X, 0f, direction.Z).Normalized();
            if (Phase != DuelPhase.Combat || !(distance > 0f) || ticks <= 0 || dir.LengthSquared() == 0f || !p.Alive || p.Motor.IsDisplaced)
            {
                return false;
            }

            CharacterMotor.BeginDisplacement(ref p.Motor, DisplacementKind.Dash, dir * (distance / (ticks * Ticks.Dt)), ticks);
            Emit(new MovementEvent { Change = MovementChange.DashStarted, Position = p.Position, Amount = distance, SourceId = sourceId }, player);
            return true;
        }

        /// <summary>
        /// A teleport of at most 10 m (spec L124) that never ends outside the legal boundary (spec L51) or inside
        /// collision. Returns false (no movement) when no legal destination exists.
        /// </summary>
        public bool TryTeleport(int player, Vec3 destination, string sourceId)
        {
            DuelPlayer p = _players[player];
            if (Phase != DuelPhase.Combat || !p.Alive)
            {
                return false;
            }

            Vec3 from = p.Position;
            Vec3 delta = destination - from;
            float max = GlobalRules.Modifiers.MaxTeleportDistance;
            if (delta.Length() > max)
            {
                destination = from + delta.Normalized() * max;
            }

            if (!_boundary.Contains(destination))
            {
                destination = _boundary.ClampInside(destination, CharacterMotor.Radius);
            }

            Capsule body = Capsule.Vertical(destination, p.Motor.Height, CharacterMotor.Radius);
            if (!Collision.IsCapsuleClear(body, 1e-3f))
            {
                return false;
            }

            CharacterMotor.CancelDisplacement(ref p.Motor);
            p.Motor.Position = destination;
            p.Motor.Velocity = Vec3.Zero;
            p.Motor.DodgeTicks = 0;
            p.Motor.Grounded = false;
            p.Motor.AirPeakY = destination.Y;
            Emit(new MovementEvent { Change = MovementChange.Teleported, Position = destination, Amount = Vec3.Distance(from, destination), SourceId = sourceId }, player);
            return true;
        }

        // ----- fields, summons, solids, deposits ------------------------------------------------------------

        /// <summary>
        /// Creates a gameplay field or summon (spec L128): at most three fields and two summons per player, each at most
        /// 8 s; at the limit the oldest eligible one retires with a dissolution event.
        /// </summary>
        public OwnedEntity CreateOwnedEntity(in OwnedEntitySpec spec)
        {
            if (Phase != DuelPhase.Combat)
            {
                return null;
            }

            _ownedScratch.Clear();
            OwnedEntity created = Owned.Create(spec, Tick, _ownedScratch);
            for (int i = 0; i < _ownedScratch.Count; i++)
            {
                EmitField(_ownedScratch[i], FieldChange.Retired, -1);
            }

            _ownedScratch.Clear();
            if (created != null)
            {
                EmitField(created, FieldChange.Created, -1);
            }

            return created;
        }

        /// <summary>Damages a summon's body; destroying a hostile summon counts for FEAT-015.</summary>
        public bool DamageSummon(int summonId, float amount, int attacker)
        {
            OwnedEntity summon = Owned.Find(summonId);
            if (summon == null || !Owned.DamageSummon(summonId, amount))
            {
                return false;
            }

            if (attacker >= 0 && attacker != summon.Owner)
            {
                _players[attacker].Feats.OnSummonDestroyed();
            }

            EmitField(summon, FieldChange.Destroyed, attacker);
            return true;
        }

        /// <summary>Removes a field or summon by dispel (spec L161: one removable layer).</summary>
        public bool DispelOwnedEntity(int entityId, int dispeller)
        {
            OwnedEntity e = Owned.Find(entityId);
            if (e == null || !Owned.Remove(entityId))
            {
                return false;
            }

            EmitField(e, FieldChange.Dispelled, dispeller);
            return true;
        }

        /// <summary>
        /// Adds a temporary solid (wall, ice block) owned by a player or the world. A solid formed under a player raises
        /// them onto its top by at most 0.5 m; one that would trap a player inside collision fails and returns -1 so the
        /// caller refunds the placement (spec L171).
        /// </summary>
        public int AddTemporarySolid(TemporarySolidSpec spec)
        {
            int id = Collision.AddTemporarySolid(spec, Tick);
            CollisionSolid solid = Collision.Find(id);
            var raised = new float[PlayerCount];
            for (int i = 0; i < PlayerCount; i++)
            {
                raised[i] = float.NaN;
                DuelPlayer p = _players[i];
                if (!solid.CapsulePenetration(p.HitVolume, out Penetration overlap) || overlap.Depth <= 1e-3f)
                {
                    continue;
                }

                float top = solid.SurfaceHeightAt(p.Position.X, p.Position.Z);
                float lift = top - p.Position.Y;
                if (float.IsNaN(top) || lift > GlobalRules.Terrain.WallRaiseMax + 1e-4f
                    || !Collision.IsCapsuleClear(Capsule.Vertical(new Vec3(p.Position.X, top + 0.005f, p.Position.Z), p.Motor.Height, CharacterMotor.Radius), 2e-3f))
                {
                    Collision.Remove(id);
                    return -1;
                }

                raised[i] = top;
            }

            Emit(new SolidEvent { Change = SolidChange.Created, SolidId = id, DurabilityAfter = spec.Durability }, spec.OwnerPlayer);
            for (int i = 0; i < PlayerCount; i++)
            {
                if (!float.IsNaN(raised[i]))
                {
                    DuelPlayer p = _players[i];
                    Vec3 from = p.Position;
                    p.Motor.Position = new Vec3(from.X, raised[i], from.Z);
                    p.Motor.Grounded = true;
                    p.Motor.SupportY = raised[i];
                    p.Motor.GroundSolidId = id;
                    p.Motor.GroundNormal = Vec3.Up;
                    p.Motor.Velocity = new Vec3(p.Motor.Velocity.X, 0f, p.Motor.Velocity.Z);
                    Emit(new MovementEvent { Change = MovementChange.RaisedByTerrain, Position = p.Position, Amount = raised[i] - from.Y, SourceId = spec.SourceId }, i);
                }
            }

            return id;
        }

        /// <summary>
        /// Durability damage to a destructible solid (spec L171). Breaking a destructible cover the attacker did not
        /// create counts for FEAT-007. Returns true when the solid was destroyed.
        /// </summary>
        public bool DamageSolid(int solidId, float amount, int attacker)
        {
            if (Phase != DuelPhase.Combat || !Collision.DamageSolid(solidId, amount, out bool destroyed))
            {
                return false;
            }

            CollisionSolid solid = Collision.Find(solidId);
            Emit(new SolidEvent { Change = destroyed ? SolidChange.Destroyed : SolidChange.Damaged, SolidId = solidId, Amount = amount, DurabilityAfter = solid.Durability }, attacker);
            if (destroyed && attacker >= 0 && attacker < PlayerCount && solid.OwnerPlayer != attacker)
            {
                _players[attacker].Feats.OnCoverBroken();
            }

            return destroyed;
        }

        /// <summary>Queues a world deposit for the reactions step (tick-order step 4 to 5).</summary>
        public void QueueDeposit(in WorldDeposit deposit) => Materials.QueueDeposit(deposit);

        // ----- resources and feat hooks for the casting stage -----------------------------------------------

        /// <summary>Pays a cast commitment only with the full cost available (spec L78, L90); counts for FEAT-013.</summary>
        public bool TryCommitMana(int player, float cost, string sourceId)
        {
            DuelPlayer p = _players[player];
            if (Phase != DuelPhase.Combat || !p.Resources.TrySpendMana(cost, Tick, true))
            {
                return false;
            }

            Emit(new ResourceEvent { Change = ResourceChange.ManaSpent, Amount = cost, SourceId = sourceId }, player);
            return true;
        }

        /// <summary>Returns mana (refunds are capped at 25% of the payment by the caller via StatSheet.CapRefund).</summary>
        public float RefundMana(int player, float amount, string sourceId)
        {
            float gained = _players[player].Resources.GainMana(amount);
            if (gained > 0f)
            {
                Emit(new ResourceEvent { Change = ResourceChange.ManaGained, Amount = gained, SourceId = sourceId }, player);
            }

            return gained;
        }

        public void RecordMasteryCast(int player) => _players[player].Feats.OnMasteryCast();        // FEAT-014

        public void RecordConsumableUse(int player, string itemId)
        {
            _players[player].Feats.OnConsumableUsed(); // FEAT-016
            Emit(new ResourceEvent { Change = ResourceChange.ConsumableUsed, SourceId = itemId }, player);
        }

        // ----- sight and hit queries ------------------------------------------------------------------------

        /// <summary>
        /// Line of sight for target acquisition: no sight-blocking solid and less than 2 m of obscuring volume
        /// (spec L167) between the points.
        /// </summary>
        public bool HasClearSight(Vec3 from, Vec3 to)
        {
            if (!Collision.HasLineOfSight(from, to))
            {
                return false;
            }

            return Materials.ObscuringPathLength(from, to) < GlobalRules.World.SteamSightBlockPathLength;
        }

        /// <summary>
        /// Whether viewer can see target at all: invisibility hides a player beyond its 4 m silhouette radius
        /// (spec L120).
        /// </summary>
        public bool IsVisibleTo(int viewer, int target)
        {
            DuelPlayer t = _players[target];
            if (!t.Statuses.IsInvisible)
            {
                return true;
            }

            return Vec3.Distance(_players[viewer].Position, t.Position) <= GlobalRules.Statuses.InvisibilitySilhouetteRadius;
        }

        /// <summary>Target acquisition: visible and in clear sight from the viewer's eye to the target's chest.</summary>
        public bool CanAcquire(int viewer, int target)
        {
            if (!IsVisibleTo(viewer, target))
            {
                return false;
            }

            DuelPlayer t = _players[target];
            Vec3 chest = t.Position + new Vec3(0f, t.Motor.Height * 0.6f, 0f);
            return HasClearSight(_players[viewer].Eye, chest);
        }

        /// <summary>
        /// Hitscan against the opponent with lag compensation (spec L423): the opponent's hit volume is rewound to the
        /// shooter's viewed tick (at most 150 ms); solids are tested as they are now.
        /// </summary>
        public bool RaycastOpponent(int shooter, Vec3 origin, Vec3 direction, float maxDistance, out float distance)
        {
            distance = 0f;
            int target = 1 - shooter;
            Vec3 dir = direction.Normalized();
            if (dir.LengthSquared() == 0f || !(maxDistance > 0f))
            {
                return false;
            }

            float limit = maxDistance;
            if (Collision.Raycast(origin, dir, maxDistance, SolidMask.Movement, out WorldHit wall))
            {
                limit = wall.Distance;
            }

            Capsule volume = TargetVolumeFor(_players[shooter], target);
            if (!Intersections.RayCapsule(origin, dir, volume.A, volume.B, volume.Radius, limit, out float t))
            {
                return false;
            }

            distance = t;
            return true;
        }

        // The opponent's hit volume as the attacker saw it (lag compensation) or as it is now.
        private Capsule TargetVolumeFor(DuelPlayer attacker, int target)
        {
            int viewTick = attacker.Input.ViewTick;
            if (viewTick > 0 && viewTick < Tick && History.TryGet(target, viewTick, out Capsule rewound, out _))
            {
                return rewound;
            }

            return _players[target].HitVolume;
        }

        private static bool SegmentHitsCapsule(Vec3 origin, Vec3 dir, float length, Capsule capsule)
        {
            float d2 = Intersections.SegmentSegmentClosest(origin, origin + dir * length, capsule.A, capsule.B, out _, out _, out _, out _);
            return d2 <= capsule.Radius * capsule.Radius;
        }

        // ----- setup ----------------------------------------------------------------------------------------

        private SpawnPoint SpawnOf(DuelPlayer p)
        {
            int side = p.Setup.SpawnSide >= 0 ? p.Setup.SpawnSide : p.Index;
            SpawnPoint[] spawns = Layout.Spawns;
            if (spawns == null || side >= spawns.Length || spawns[side] == null)
            {
                return new SpawnPoint { Side = side, Position = Layout.Center };
            }

            return spawns[side];
        }

        // spec L46: protected alcove; health, mana, stamina restored; round-start reserve shields issued once.
        private void PlaceInAlcove(DuelPlayer p)
        {
            SpawnPoint spawn = SpawnOf(p);
            YawBox alcove = spawn.Alcove;
            if (alcove.HalfExtents.X <= 0f || alcove.HalfExtents.Z <= 0f)
            {
                float h = SimRules.Duel.AlcoveFallbackHalfSize;
                alcove = new YawBox(spawn.Position + new Vec3(0f, 1.5f, 0f), new Vec3(h, 1.5f, h), spawn.YawDeg);
            }

            _alcoves[p.Index] = alcove;
            p.Motor = MotorState.At(spawn.Position, spawn.YawDeg);
            CharacterMotor.SnapToGround(ref p.Motor, Collision);
            p.Resources.RestoreForDuel(p.Stats.MaxHealth, p.Stats.MaxMana, p.Stats.MaxStamina);
            if (p.Setup.ReserveShields != null)
            {
                for (int i = 0; i < p.Setup.ReserveShields.Count; i++)
                {
                    ReserveShieldGrant grant = p.Setup.ReserveShields[i];
                    p.Shields.Add(grant.SourceId, grant.Amount, 0, true, 0);
                }
            }
        }

        // ----- events ---------------------------------------------------------------------------------------

        private T Emit<T>(T e, int player) where T : DuelEvent
        {
            e.Id = _nextEventId++;
            e.Tick = Tick;
            e.Player = player;
            _tickEvents.Add(e);
            if (_keepLog)
            {
                _log.Add(e);
            }

            return e;
        }

        private void EmitStatus(DuelPlayer p, StatusInstance s, StatusChange change)
        {
            Emit(new StatusEvent
            {
                Change = change,
                Kind = s.Kind,
                DefinitionId = s.DefinitionId,
                SourcePlayer = s.SourcePlayer,
                StatusId = s.Id,
                ExpireTick = s.ExpireTick,
                Intensity = s.Intensity,
            }, p.Index);
        }

        private void EmitField(OwnedEntity e, FieldChange change, int destroyer)
        {
            Emit(new FieldEvent { Change = change, EntityKind = e.Kind, EntityId = e.Id, DefinitionId = e.DefinitionId, Destroyer = destroyer }, e.Owner);
        }

        private static float Finite(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 0f : v;

        // ----- helpers --------------------------------------------------------------------------------------

        private sealed class BoundaryConstraint : IPositionConstraint
        {
            private readonly DuelWorld _world;

            public BoundaryConstraint(DuelWorld world)
            {
                _world = world;
            }

            public bool Contains(Vec3 point)
            {
                BoundaryContour b = _world.Layout.Boundary;
                return b == null || b.Contains(point, _world.CombatSeconds);
            }

            public Vec3 ClampInside(Vec3 point, float margin)
            {
                BoundaryContour b = _world.Layout.Boundary;
                return b == null ? point : b.ClampInside(point, _world.CombatSeconds, margin);
            }
        }

        private sealed class MaterialsContext : IMaterialsContext
        {
            private readonly DuelWorld _world;

            public MaterialsContext(DuelWorld world)
            {
                _world = world;
            }

            public int Tick => _world.Tick;
            public int PlayerCount => DuelWorld.PlayerCount;
            public CollisionWorld Collision => _world.Collision;

            public Vec3 PlayerFeet(int player) => _world._players[player].Position;

            public bool PlayerGrounded(int player) => _world._players[player].Motor.Grounded;

            public void RequestContactDamage(ContactDamageRequest request) => _world._contactRequests.Add(request);

            public void RecordReaction(int player, string reactionId)
            {
                if (player >= 0 && player < DuelWorld.PlayerCount)
                {
                    _world._players[player].Feats.OnReaction(reactionId); // FEAT-006
                }
            }

            public void RecordCloudCleared(int player)
            {
                if (player >= 0 && player < DuelWorld.PlayerCount)
                {
                    _world._players[player].Feats.OnCloudCleared(); // FEAT-011
                }
            }
        }
    }
}
