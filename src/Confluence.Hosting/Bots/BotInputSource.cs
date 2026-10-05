using System;
using System.Collections.Generic;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Random;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Hosting.Duels;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using InputButtons = Confluence.Casting.InputButtons;
using Confluence.Sim.Players;
using Confluence.Sim.World;

namespace Confluence.Hosting.Bots
{
    /// <summary>Random streams of bots (spec L435: bot RNG is its own stream).</summary>
    public static class BotStreams
    {
        /// <summary>Sub-index offset that keeps economy streams apart from duel streams.</summary>
        public const int EconomySubBase = 1 << 16;

        /// <summary>A bot's stream for one duel: Bot stream, round slot = duel id, sub = player.</summary>
        public static Rng ForDuel(ulong matchSeed, int duelId, int player) => RngStreams.For(matchSeed, RngStream.Bot, duelId, player);

        /// <summary>A bot's stream for one round's shop and draft.</summary>
        public static Rng ForEconomy(ulong matchSeed, int round, int player) => RngStreams.For(matchSeed, RngStream.Bot, round, EconomySubBase + player);
    }

    /// <summary>
    /// A bot that plays a duel through real per-tick inputs, exactly like a client (spec L381). It perceives the
    /// opponent only through the Sim's acquisition query (line of sight, obscuring volumes, invisibility; no
    /// wallhacks) and only after its reaction latency; otherwise it remembers where it last saw them or heads for the
    /// central floor the boundary contracts to. It navigates with the arena graph, keeps its preferred range, strafes,
    /// sprints, hops and dodges within stamina, guards visible incoming strikes after a humanlike reaction (by chance,
    /// never every time), uses common melee in reach, and casts its selected spell with its wizard's input script.
    /// Randomness comes only from the Bot stream.
    /// </summary>
    public sealed class BotInputSource : IInputSource
    {
        private const int HistorySize = 64;                  // > the slowest reaction (0.7 s = 42 ticks)
        private const float NominalProjectileSpeed = 30f;    // m/s, for leading targets (L381 "projectile travel")
        private const float ChestHeightFraction = 0.6f;      // the Sim's acquisition point (DuelWorld.CanAcquire)
        private const int MemoryTicks = 4 * Ticks.PerSecond; // how long a last-seen position stays worth chasing
        private const float BoundaryLookaheadSec = 2f;       // steer in before the contracting boundary arrives
        private const int GuardHoldTicks = 30;               // 0.5 s, under the 0.8 s maximum hold (L104)
        private const int RushTicks = 2 * Ticks.PerSecond;
        private const float SprintMinDistance = 8f;
        private const float SprintStaminaMargin = 15f;
        private const float WalkProbeHeight = 0.8f;          // sphere center above the feet: clears the 0.35 m step
        private const InputButtons Momentary = InputButtons.Melee | InputButtons.Jump | InputButtons.Dodge;

        private readonly ulong _matchSeed;
        private readonly Observation[] _history = new Observation[HistorySize];
        private Rng _rng;
        private BotNavigator _nav;
        private int _latencyTicks;
        private bool _known;
        private Vec3 _lastKnown;
        private Vec3 _lastKnownVelocity;
        private int _lastKnownTick;
        private int _nextThink;
        private float _aimErrorYaw;
        private float _aimErrorPitch;
        private float _yaw;
        private float _pitch;
        private int _strafeSign = 1;
        private bool _hop;
        private int _rushUntil;
        private int _guardFrom = int.MaxValue;
        private int _guardUntil = int.MinValue;
        private int _dodgeAt = int.MaxValue;
        private BotCastScript _cast;
        private int _castStart;
        private int _telegraphUntil = int.MinValue;
        private int _nextCastTick;
        private int _slot;
        private uint _sequence;
        private InputButtons _previous;
        private IBotKitPlanner _planner; // the wizard's kit planner (BotKitPlanner.cs), or null for the slot rotation
        private BotCastPlan _plan;
        private int _recastAt = int.MinValue;

        public BotInputSource(ulong matchSeed, BotProfile profile)
        {
            _matchSeed = matchSeed;
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        }

        public BotProfile Profile { get; }

        /// <summary>Diagnostics of the current or last duel.</summary>
        public BotDuelStats Stats { get; private set; } = new BotDuelStats();

        public void BeginDuel(DuelSeat seat)
        {
            if (seat == null) throw new ArgumentNullException(nameof(seat));
            _rng = BotStreams.ForDuel(_matchSeed, seat.DuelId, seat.Player);
            DuelWorld world = seat.World;
            _nav = new BotNavigator(seat.Arena?.Navigation, (a, b) => ClearWalk(world, a, b));
            _latencyTicks = _rng.NextInt(Profile.ReactionMinTicks, Profile.ReactionMaxTicks + 1);
            Array.Clear(_history, 0, _history.Length);
            _known = false;
            _nextThink = 0;
            _aimErrorYaw = _aimErrorPitch = 0f;
            DuelPlayer me = seat.World.Player(seat.Index);
            _yaw = me.Motor.YawDeg;
            _pitch = 0f;
            _strafeSign = _rng.NextBool() ? 1 : -1;
            _hop = false;
            _rushUntil = int.MinValue;
            _guardFrom = int.MaxValue;
            _guardUntil = int.MinValue;
            _dodgeAt = int.MaxValue;
            _cast = null;
            _telegraphUntil = int.MinValue;
            _nextCastTick = 0;
            _slot = 0;
            _previous = InputButtons.None;
            _planner = BotKitPlanners.For(me.Setup.WizardId);
            _plan = null;
            _recastAt = int.MinValue;
            Stats = new BotDuelStats();
        }

        public void EndDuel(DuelSeat seat, DuelReport report)
        {
            _cast = null;
            _nav = null;
        }

        public PlayerInput NextInput(DuelSeat seat)
        {
            DuelWorld world = seat.World;
            int self = seat.Index;
            int other = 1 - self;
            int tick = world.Tick + 1; // the step this command is for
            DuelPlayer me = world.Player(self);
            if (_rng == null) BeginDuel(seat);

            Observe(world, self, other, tick);
            ReadEvents(world, self, other, tick, me);
            Observation seen = Perceived(tick);
            if (seen.Seen)
            {
                _known = true;
                _lastKnown = seen.Position;
                _lastKnownVelocity = seen.Velocity;
                _lastKnownTick = seen.Tick;
            }

            PlayerInput input;
            switch (world.Phase)
            {
                case DuelPhase.Alcove:
                    input = AlcoveInput(world, me, tick);
                    break;
                case DuelPhase.Combat:
                    input = CombatInput(world, me, tick, seen);
                    break;
                default:
                    input = PlayerInput.Idle(me.Motor.YawDeg);
                    break;
            }

            // Momentary actions are edges in the Sim: release for a tick before pressing again.
            input.Buttons &= ~(_previous & Momentary);
            _previous = input.Buttons;
            input.Tick = tick;
            input.Sequence = ++_sequence;
            input.SelectedSlot = _slot;
            return input;
        }

        // ---- Perception -----------------------------------------------------------------------------------------

        private void Observe(DuelWorld world, int self, int other, int tick)
        {
            // Sight through the Sim's own acquisition query (L167 obscuring volumes, L120 invisibility): no wallhacks.
            bool sees = world.CanAcquire(self, other);
            DuelPlayer them = world.Player(other);
            _history[tick % HistorySize] = new Observation
            {
                Tick = tick,
                Seen = sees,
                Position = sees ? them.Position : default,
                Velocity = sees ? them.Motor.Velocity : default,
                Height = sees ? them.Motor.Height : 0f,
                Casting = sees && them.Caster != null && them.Caster.Machine.Phase != Confluence.Casting.CastPhase.Idle, // a visible windup (spec L96)
            };
            if (sees) Stats.TicksWithSight++;
        }

        /// <summary>What the bot knows now: the observation one reaction latency ago (L381 humanlike floor).</summary>
        private Observation Perceived(int tick)
        {
            int at = tick - _latencyTicks;
            if (at < 0) return default;
            Observation o = _history[at % HistorySize];
            return o.Tick == at ? o : default;
        }

        private void ReadEvents(DuelWorld world, int self, int other, int tick, DuelPlayer me)
        {
            IReadOnlyList<DuelEvent> events = world.TickEvents; // the previous step's events
            for (int i = 0; i < events.Count; i++)
            {
                DuelEvent e = events[i];
                if (e.Player == self)
                {
                    CountOwn(e);
                    continue;
                }

                if (!(e is MeleeEvent melee) || melee.Player != other || melee.Change != MeleeChange.Started) continue;
                if (!_history[tick % HistorySize].Seen) continue; // an unseen windup cannot be reacted to
                int react = tick + _rng.NextInt(Profile.ReactionMinTicks, Profile.ReactionMaxTicks + 1);
                if (_rng.Chance(Profile.GuardChance))
                {
                    _guardFrom = react;
                    _guardUntil = react + GuardHoldTicks;
                    Stats.GuardsPlanned++;
                }
                else if (_rng.Chance(Profile.DodgeChance))
                {
                    _dodgeAt = react;
                    Stats.DodgesPlanned++;
                }
            }
        }

        /// <summary>Counts what the bot's own inputs actually did in the Sim (diagnostics only).</summary>
        private void CountOwn(DuelEvent e)
        {
            switch (e)
            {
                case GuardEvent guard when guard.Change == GuardChange.Started:
                    Stats.Guards++;
                    break;
                case MovementEvent move when move.Change == MovementChange.Dodged:
                    Stats.Dodges++;
                    break;
                case MovementEvent move when move.Change == MovementChange.Jumped:
                    Stats.Jumps++;
                    break;
                case MeleeEvent melee when melee.Change == MeleeChange.Started:
                    Stats.MeleeStrikes++;
                    break;
                case MeleeEvent melee when melee.Change == MeleeChange.Hit:
                    Stats.MeleeHits++;
                    break;
            }
        }

        // ---- Phases ---------------------------------------------------------------------------------------------

        private PlayerInput AlcoveInput(DuelWorld world, DuelPlayer me, int tick)
        {
            // The ward holds both players (L46); face the arena center and wait.
            Vec3 toCenter = world.Layout.Center - me.Position;
            TurnToward(CMath.YawOf(toCenter), 0f);
            return PlayerInput.Idle(_yaw).Facing(_yaw, _pitch);
        }

        private PlayerInput CombatInput(DuelWorld world, DuelPlayer me, int tick, Observation seen)
        {
            if (tick >= _nextThink) Think(world, me, tick, seen);

            bool engaged = _known && tick - _lastKnownTick <= MemoryTicks;
            Vec3 opponent = engaged ? _lastKnown : world.Layout.Center;
            float distance = Vec2.Distance(me.Position.XZ(), opponent.XZ());
            bool rushing = tick < _rushUntil && seen.Seen;
            MeleeProfileView melee = new MeleeProfileView(me);

            // ---- Where to go ----
            bool jump = false;
            Vec2 move;
            if (!world.Layout.Boundary.ContainsSafely(me.Position, world.CombatSeconds + BoundaryLookaheadSec))
            {
                move = _nav.Steer(me.Position, SafePoint(world, me.Position), tick, Profile.ThinkTicks, Sidestep, out jump); // retreat inside (L51)
            }
            else if (seen.Seen)
            {
                float want = rushing ? melee.Reach * 0.7f : Profile.PreferredRange;
                Vec2 toward = (opponent.XZ() - me.Position.XZ()).Normalized();
                if (distance > want + 1.5f) move = _nav.Steer(me.Position, ClampGoal(world, opponent), tick, Profile.ThinkTicks, Sidestep, out jump);
                else if (distance < want - 1.5f && !rushing) move = -toward;
                else if (Profile.StrafesInCombat) move = new Vec2(-toward.Y, toward.X) * _strafeSign;
                else move = Vec2.Zero;
            }
            else
            {
                // Lost sight: go where the opponent was last seen, or to the central floor (L48).
                move = _nav.Steer(me.Position, ClampGoal(world, opponent), tick, Profile.ThinkTicks * 2, Sidestep, out jump);
            }

            // ---- Where to look ----
            if (seen.Seen)
            {
                if (_cast != null && _plan != null && _plan.Aim != BotAimKind.Chest) AimAtPoint(me, _plan.Aim == BotAimKind.Point ? _plan.Point : seen.Position);
                else AimAt(me, seen, distance);
            }
            else if (move.LengthSquared() > 0f)
            {
                TurnToward(CMath.YawOf(move), 0f);
            }

            // ---- Actions ----
            InputButtons buttons = InputButtons.None;
            float stamina = me.Resources.Stamina;
            bool guarding = tick >= _guardFrom && tick < _guardUntil && (me.Guard.Active || (me.Guard.CanStart(tick) && stamina >= GlobalRules.Guard.StaminaCost));
            if (guarding)
            {
                buttons |= InputButtons.Guard;
                if (_cast != null) EndCast(tick); // guarding releases the cast in progress
            }
            else if (tick >= _guardUntil)
            {
                _guardFrom = int.MaxValue;
            }

            if (tick >= _dodgeAt && tick < _dodgeAt + 3 && !guarding && stamina >= GlobalRules.Movement.DodgeStaminaCost && me.Motor.DodgeCooldownTicks == 0)
            {
                buttons |= InputButtons.Dodge;
                Vec2 away = (me.Position.XZ() - opponent.XZ()).Normalized();
                move = new Vec2(-away.Y, away.X) * _strafeSign; // sidestep the strike line
                _dodgeAt = int.MaxValue;
            }

            bool inReach = seen.Seen && distance <= melee.Reach + GlobalRules.Movement.CapsuleRadius - 0.1f;
            if (inReach && !guarding && _cast == null && !me.Melee.Busy && stamina >= melee.Stamina && AimedWithin(me, seen, 25f))
            {
                buttons |= InputButtons.Melee;
                Stats.MeleePresses++;
            }

            if (!guarding && !inReach) buttons |= CastButtons(world, me, tick, seen, distance, rushing, ref move);

            if (jump || (_hop && me.Motor.Grounded))
            {
                if (stamina >= GlobalRules.Movement.JumpStaminaCost + Profile.StaminaReserve) buttons |= InputButtons.Jump;
                _hop = false;
            }

            // Sprint only on long forward runs and never into a melee or a cast (L80 sprint disallows both).
            Vec2 local = ToLocal(move, _yaw);
            bool longRun = distance > SprintMinDistance && _cast == null && !seen.Seen;
            if (longRun && local.Y > 0.7f && stamina > Profile.StaminaReserve + SprintStaminaMargin) buttons |= InputButtons.Sprint;

            var input = new PlayerInput { MoveX = local.X, MoveY = local.Y, Buttons = buttons };
            return input.Facing(_yaw, _pitch);
        }

        private void Think(DuelWorld world, DuelPlayer me, int tick, Observation seen)
        {
            _nextThink = tick + Profile.ThinkTicks;
            float err = Profile.EffectiveAimErrorDeg;
            if (seen.Seen)
            {
                Vec3 lateral = seen.Velocity;
                lateral.Y = 0f;
                err += Profile.AimErrorPerSpeedDeg * lateral.Length(); // moving targets are harder to hit (L381)
            }

            _aimErrorYaw = _rng.Range(-err, err);
            _aimErrorPitch = _rng.Range(-err, err) * 0.5f;
            if (Profile.StrafesInCombat && _rng.Chance(0.35f)) _strafeSign = -_strafeSign;
            _hop = Profile.JumpChance > 0f && _rng.Chance(Profile.JumpChance);

            // Pressing in for melee grows more likely as the duel runs long and the floor shrinks (L48).
            float aggression = Profile.AggressionChance * (1f + world.CombatSeconds / 45f);
            if (seen.Seen && tick >= _rushUntil && _rng.Chance(aggression))
            {
                _rushUntil = tick + RushTicks;
                Stats.Rushes++;
            }
        }

        // ---- Casting --------------------------------------------------------------------------------------------

        private InputButtons CastButtons(DuelWorld world, DuelPlayer me, int tick, Observation seen, float distance, bool rushing, ref Vec2 move)
        {
            if (tick == _recastAt)
            {
                _recastAt = int.MinValue; // a planned recast (one press of the spell-recast binding)
                return InputButtons.Recast;
            }

            if (_cast != null)
            {
                int k = tick - _castStart;
                if (_cast.IsDone(k))
                {
                    EndCast(tick);
                    return InputButtons.None;
                }

                return _cast.ButtonsAt(k);
            }

            if (tick < _telegraphUntil)
            {
                move = Vec2.Zero; // Novice telegraph: stop and face the target before casting (L381)
                return InputButtons.None;
            }

            bool wanted = _planner != null
                ? seen.Seen && tick >= _nextCastTick && !me.Melee.Busy // the kit planner judges range, mana and rushes
                : seen.Seen && !rushing && tick >= _nextCastTick && distance >= 2.5f && distance <= Profile.PreferredRange + 5f &&
                  me.Resources.Mana >= 20f && !me.Melee.Busy;
            if (!wanted) return InputButtons.None;
            if (_telegraphUntil == int.MinValue && Profile.TelegraphTicks > 0)
            {
                _telegraphUntil = tick + Profile.TelegraphTicks;
                move = Vec2.Zero;
                return InputButtons.None;
            }

            _telegraphUntil = int.MinValue;
            if (_planner != null)
            {
                _plan = _planner.Choose(KitView(world, me, tick, seen, distance, rushing), _rng);
                if (_plan == null)
                {
                    _nextCastTick = tick + Profile.ThinkTicks; // nothing worth casting yet
                    return InputButtons.None;
                }

                if (!_plan.Signature) _slot = _plan.Slot;
                _cast = _plan.Signature ? new SignatureScript() : _planner.ScriptFor(_plan, Profile, _rng);
            }
            else
            {
                _slot = me.Setup.EquippedSpellIds.Count > 0 ? (_slot + 1) % Math.Min(PlayerInput.SlotCount, me.Setup.EquippedSpellIds.Count) : 0;
                _cast = BotCastScript.For(me.Setup.WizardId, Profile, _rng);
            }

            _castStart = tick;
            Stats.Casts++;
            return _cast.ButtonsAt(0);
        }

        private void EndCast(int tick)
        {
            if (_plan != null && _plan.RecastAfterTicks >= 0) _recastAt = tick + Math.Max(1, _plan.RecastAfterTicks);
            _plan = null;
            _cast = null;
            _nextCastTick = tick + _rng.NextInt(Ticks.PerSecond, Ticks.FromSeconds(2.5));
        }

        // ---- Aim and movement helpers ---------------------------------------------------------------------------

        private void AimAt(DuelPlayer me, Observation seen, float distance)
        {
            Vec3 target = seen.Position + new Vec3(0f, seen.Height * ChestHeightFraction, 0f);
            if (Profile.LeadsTargets)
            {
                float travel = distance / NominalProjectileSpeed;
                target += new Vec3(seen.Velocity.X, 0f, seen.Velocity.Z) * travel;
            }

            Vec3 aim = target - me.Eye;
            TurnToward(CMath.YawOf(aim) + _aimErrorYaw, CMath.PitchOf(aim) + _aimErrorPitch);
        }

        /// <summary>Looks at a world point (a kit planner's ground or hazard target), with the bot's usual aim error.</summary>
        private void AimAtPoint(DuelPlayer me, Vec3 point)
        {
            Vec3 aim = point - me.Eye;
            TurnToward(CMath.YawOf(aim) + _aimErrorYaw, CMath.PitchOf(aim) + _aimErrorPitch);
        }

        /// <summary>The legal information a kit planner chooses from (see <see cref="BotKitView"/>).</summary>
        private BotKitView KitView(DuelWorld world, DuelPlayer me, int tick, Observation seen, float distance, bool rushing)
        {
            DuelPlayer them = world.Player(1 - me.Index);
            return new BotKitView
            {
                World = world,
                Self = me.Index,
                Me = me,
                Tick = tick,
                Profile = Profile,
                Sees = seen.Seen,
                OpponentPosition = seen.Position,
                OpponentVelocity = seen.Velocity,
                Distance = distance,
                OpponentCasting = seen.Casting, // perceived with the same reaction latency as the position
                OpponentWizardId = them.Setup.WizardId,
                Rushing = rushing,
            };
        }

        private bool AimedWithin(DuelPlayer me, Observation seen, float toleranceDeg)
        {
            float yaw = CMath.YawOf(seen.Position - me.Position);
            return Math.Abs(CMath.WrapAngle(yaw - _yaw)) <= toleranceDeg;
        }

        private void TurnToward(float yawDeg, float pitchDeg)
        {
            float rate = Math.Max(1f, Profile.TurnRateDegPerTick);
            float dy = CMath.WrapAngle(yawDeg - _yaw);
            _yaw = CMath.WrapAngle(_yaw + CMath.Clamp(dy, -rate, rate));
            float dp = pitchDeg - _pitch;
            _pitch = CMath.Clamp(_pitch + CMath.Clamp(dp, -rate, rate), -60f, 60f);
        }

        private Vec2 Sidestep()
        {
            float angle = _rng.Range(0f, 360f);
            return Vec2.FromYaw(angle);
        }

        /// <summary>A world direction expressed in the view's strafe (X) and forward (Y) axes (CharacterMotor).</summary>
        private static Vec2 ToLocal(Vec2 world, float yawDeg)
        {
            if (world.LengthSquared() < 1e-6f) return Vec2.Zero;
            Vec3 f = CMath.ForwardFromYaw(yawDeg);
            Vec3 r = CMath.RightFromYaw(yawDeg);
            var local = new Vec2(world.X * r.X + world.Y * r.Z, world.X * f.X + world.Y * f.Z);
            float length = local.Length();
            return length > 1f ? local / length : local;
        }

        /// <summary>The capsule can walk straight between the points: a body-height sphere sweep meets no movement solid.</summary>
        private static bool ClearWalk(DuelWorld world, Vec3 from, Vec3 to)
        {
            var lift = new Vec3(0f, WalkProbeHeight, 0f);
            return !world.Collision.SphereSweep(from + lift, to + lift, GlobalRules.Movement.CapsuleRadius, SolidMask.Movement, out _);
        }

        /// <summary>A point well inside the boundary as it will be shortly: the bot's retreat target (L51).</summary>
        private static Vec3 SafePoint(DuelWorld world, Vec3 position)
        {
            BoundaryContour b = world.Layout.Boundary;
            return b == null ? world.Layout.Center : RadialInside(b, position, world.CombatSeconds + BoundaryLookaheadSec, 2f);
        }

        /// <summary>The goal itself, or the nearest point toward the center that the boundary will still hold.</summary>
        private static Vec3 ClampGoal(DuelWorld world, Vec3 goal)
        {
            BoundaryContour b = world.Layout.Boundary;
            float t = world.CombatSeconds + BoundaryLookaheadSec;
            if (b == null || b.Contains(goal, t)) return goal;
            return RadialInside(b, goal, t, 1.5f);
        }

        /// <summary>
        /// The point on the ray from the boundary center through <paramref name="p"/> at most <paramref name="margin"/>
        /// inside the radius along that ray. A cheap stand-in for <c>BoundaryContour.ClampInside</c>, which verifies the
        /// exact Euclidean margin and is too slow to call every tick; a bot only needs a target.
        /// </summary>
        private static Vec3 RadialInside(BoundaryContour b, Vec3 p, float timeSec, float margin)
        {
            float dx = p.X - b.Center.X;
            float dz = p.Z - b.Center.Z;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            if (d < 1e-3f) return new Vec3(b.Center.X, p.Y, b.Center.Z);
            float r = Math.Max(0f, Math.Min(d, b.RadiusAt(CMath.YawOf(new Vec3(dx, 0f, dz)), timeSec) - margin));
            return new Vec3(b.Center.X + dx / d * r, p.Y, b.Center.Z + dz / d * r);
        }

        private struct Observation
        {
            public int Tick;
            public bool Seen;
            public Vec3 Position;
            public Vec3 Velocity;
            public float Height;
            public bool Casting;
        }

        private readonly struct MeleeProfileView
        {
            public MeleeProfileView(DuelPlayer p)
            {
                MeleeProfile profile = p.CurrentMeleeProfile;
                Reach = profile.Reach;
                Stamina = profile.Stamina;
            }

            public float Reach { get; }
            public float Stamina { get; }
        }
    }

    /// <summary>Per-duel counters of a bot (tests, tuning).</summary>
    public sealed class BotDuelStats
    {
        public int TicksWithSight;
        public int GuardsPlanned;
        public int Guards;
        public int Jumps;
        public int MeleeStrikes;
        public int MeleeHits;
        public int DodgesPlanned;
        public int Dodges;
        public int MeleePresses;
        public int Casts;
        public int Rushes;
    }

    internal static class BoundaryExtensions
    {
        /// <summary>Inside the boundary as it will be at <paramref name="combatSeconds"/>; a layout without one is always inside.</summary>
        public static bool ContainsSafely(this BoundaryContour boundary, Vec3 point, float combatSeconds) =>
            boundary == null || boundary.Contains(point, combatSeconds);
    }
}
