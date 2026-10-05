using System;
using Confluence.Arena.Navigation;
using Confluence.Casting;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Random;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Hosting.Bots;
using Confluence.Net.Game.Client;
using Confluence.Net.Game.Snapshots;
using Confluence.Net.Game.Wire;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Movement;
using Confluence.Sim.World;

namespace Confluence.Net.Game.Headless
{
    /// <summary>
    /// A bot that plays a networked duel from the client's replicated view only (no server state): its own predicted
    /// position and private resources, the opponent as interpolated about 100 ms behind, the replicated solids and
    /// obscuration volumes for line of sight, and the public duel events. It reuses the Hosting bot pieces that do not
    /// read the authoritative world (<see cref="BotProfile"/> competence and reaction floors (L381), the per-wizard
    /// <see cref="BotCastScript"/>s, <see cref="BotNavigator"/> over the public arena graph, and the Bot RNG stream) and
    /// plays a reduced version of <c>BotInputSource</c>'s combat loop: keep the preferred range, strafe, aim with a
    /// latency-delayed perception, guard seen melee windups by chance, strike in reach, and cast the selected spell.
    /// </summary>
    public sealed class ViewPilot
    {
        private const int HistorySize = 64;
        private const float ChestHeight = 1.0f;
        private const float NominalProjectileSpeed = 30f;
        private const int GuardHoldTicks = 30;
        private const float WalkProbeHeight = 0.8f;
        private const InputButtons Momentary = InputButtons.Melee | InputButtons.Jump | InputButtons.Dodge;

        private readonly ulong _seed;
        private readonly int _seat;
        private readonly Catalogue _catalogue;
        private readonly Seen[] _history = new Seen[HistorySize];
        private Rng _rng;
        private int _duelId = -1;
        private BotNavigator _nav;
        private ArenaLayout _navLayout;
        private int _latency;
        private float _yaw;
        private float _pitch;
        private float _aimErrYaw;
        private float _aimErrPitch;
        private int _nextThink;
        private int _strafe = 1;
        private int _guardFrom = int.MaxValue;
        private int _guardUntil = int.MinValue;
        private int _lastOpponentSerial = -1;
        private BotCastScript _cast;
        private int _castStart;
        private int _nextCast;
        private int _slot;
        private InputButtons _previous;
        private MeleeProfile _melee = MeleeProfile.Focus;

        public ViewPilot(BotProfile profile, ulong seed, int seat, Catalogue catalogue)
        {
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _seed = seed;
            _seat = seat;
            _catalogue = catalogue;
        }

        public BotProfile Profile { get; }

        public int Casts { get; private set; }
        public int MeleePresses { get; private set; }
        public int GuardPresses { get; private set; }
        public int TicksWithSight { get; private set; }

        /// <summary>The input provider for <see cref="GameClient.InputProvider"/>.</summary>
        public PlayerInput NextInput(ClientDuel duel, int tick)
        {
            if (duel.DuelId != _duelId) Begin(duel);
            if (_nav == null && duel.Layout != null && duel.Predictor != null) BuildNavigator(duel);

            Vec3 me = duel.RenderPosition;
            PlayerInput input;
            if (duel.Phase == DuelPhase.Alcove || duel.Latest == null)
            {
                Vec3 center = duel.Layout != null ? duel.Layout.Center : Vec3.Zero;
                Turn(CMath.YawOf(center - me), 0f);
                input = PlayerInput.Idle(_yaw).Facing(_yaw, _pitch);
            }
            else if (duel.Phase == DuelPhase.Combat && !duel.Ended)
            {
                input = Combat(duel, tick, me);
            }
            else
            {
                input = PlayerInput.Idle(_yaw);
            }

            input.Buttons &= ~(_previous & Momentary);
            _previous = input.Buttons;
            input.SelectedSlot = _slot;
            return input;
        }

        private void Begin(ClientDuel duel)
        {
            _duelId = duel.DuelId;
            _rng = BotStreams.ForDuel(_seed, duel.DuelId, _seat);
            _latency = _rng.NextInt(Profile.ReactionMinTicks, Profile.ReactionMaxTicks + 1);
            Array.Clear(_history, 0, _history.Length);
            _nav = null;
            _navLayout = null;
            _yaw = duel.HasPrediction ? duel.Predicted.YawDeg : 0f;
            _pitch = 0f;
            _nextThink = 0;
            _strafe = _rng.NextBool() ? 1 : -1;
            _guardFrom = int.MaxValue;
            _guardUntil = int.MinValue;
            _lastOpponentSerial = -1;
            _cast = null;
            _nextCast = 0;
            _slot = 0;
            _previous = InputButtons.None;
            _melee = MeleeOf(duel);
        }

        private void BuildNavigator(ClientDuel duel)
        {
            if (_navLayout == duel.Layout) return;
            _navLayout = duel.Layout;
            ArenaNav graph;
            try
            {
                graph = ArenaNav.Build(duel.Layout);
            }
            catch (Exception)
            {
                graph = null;
            }

            MotorPredictor predictor = duel.Predictor;
            _nav = new BotNavigator(graph, (a, b) =>
            {
                var lift = new Vec3(0f, WalkProbeHeight, 0f);
                return !predictor.Collision.SphereSweep(a + lift, b + lift, GlobalRules.Movement.CapsuleRadius, SolidMask.Movement, out _);
            });
        }

        private PlayerInput Combat(ClientDuel duel, int tick, Vec3 me)
        {
            Observe(duel, tick, me);
            Seen seen = Perceived(tick);
            if (tick >= _nextThink) Think(tick, seen);
            Vec3 target = seen.Visible ? seen.Position : (duel.Layout != null ? duel.Layout.Center : me);
            float distance = Vec2.Distance(me.XZ(), target.XZ());

            // Movement: keep the preferred range, strafe when there; walk to the center when nothing is seen (L48).
            Vec2 move = Vec2.Zero;
            bool jump = false;
            float combatSeconds = duel.Latest.CombatTick / (float)Ticks.PerSecond;
            BoundaryContour boundary = duel.Layout?.Boundary;
            if (boundary != null && !boundary.Contains(me, combatSeconds + 2f))
            {
                move = Steer(duel, me, duel.Layout.Center, tick, out jump);
            }
            else if (seen.Visible)
            {
                Vec2 toward = (target.XZ() - me.XZ()).Normalized();
                if (distance > Profile.PreferredRange + 1.5f) move = Steer(duel, me, target, tick, out jump);
                else if (distance < Profile.PreferredRange - 1.5f) move = -toward;
                else if (Profile.StrafesInCombat) move = new Vec2(-toward.Y, toward.X) * _strafe;
            }
            else
            {
                move = Steer(duel, me, target, tick, out jump);
            }

            if (seen.Visible) AimAt(duel, me, seen, distance);
            else if (move.LengthSquared() > 0f) Turn(CMath.YawOf(move), 0f);

            InputButtons buttons = InputButtons.None;
            PrivateEntry own = duel.LatestPrivate;
            float stamina = own != null ? own.Stamina : 0f;
            float mana = own != null ? own.Mana : 0f;

            // Guard a seen windup after the reaction delay, by chance (L381: never every time).
            PlayerEntry opponent = duel.InterpolatedOpponent;
            if (opponent != null && seen.Visible && opponent.MeleePhase == (byte)MeleePhase.Windup && opponent.MeleeSerial != _lastOpponentSerial)
            {
                _lastOpponentSerial = opponent.MeleeSerial;
                if (_rng.Chance(Profile.GuardChance))
                {
                    _guardFrom = tick + _rng.NextInt(Profile.ReactionMinTicks, Profile.ReactionMaxTicks + 1);
                    _guardUntil = _guardFrom + GuardHoldTicks;
                }
            }

            bool guarding = tick >= _guardFrom && tick < _guardUntil && stamina >= GlobalRules.Guard.StaminaCost;
            if (guarding)
            {
                buttons |= InputButtons.Guard;
                GuardPresses++;
                _cast = null;
            }

            bool inReach = seen.Visible && distance <= _melee.Reach + GlobalRules.Movement.CapsuleRadius - 0.1f;
            if (inReach && !guarding && _cast == null && stamina >= _melee.Stamina)
            {
                buttons |= InputButtons.Melee;
                MeleePresses++;
            }

            if (!guarding && !inReach) buttons |= CastButtons(duel, tick, seen, distance, mana, ref move);
            if (jump && stamina >= GlobalRules.Movement.JumpStaminaCost + Profile.StaminaReserve) buttons |= InputButtons.Jump;

            Vec2 local = ToLocal(move, _yaw);
            var input = new PlayerInput { MoveX = local.X, MoveY = local.Y, Buttons = buttons };
            return input.Facing(_yaw, _pitch);
        }

        private InputButtons CastButtons(ClientDuel duel, int tick, Seen seen, float distance, float mana, ref Vec2 move)
        {
            if (_cast != null)
            {
                int k = tick - _castStart;
                if (_cast.IsDone(k))
                {
                    _cast = null;
                    _nextCast = tick + _rng.NextInt(Ticks.PerSecond, Ticks.FromSeconds(2.5));
                    return InputButtons.None;
                }

                return _cast.ButtonsAt(k);
            }

            int slots = Math.Min(PlayerInput.SlotCount, duel.Own.SpellIds.Count);
            bool wanted = seen.Visible && tick >= _nextCast && distance >= 2.5f && distance <= Profile.PreferredRange + 5f && mana >= 20f && slots > 0;
            if (!wanted) return InputButtons.None;
            _slot = (_slot + 1) % slots;
            PrivateEntry own = duel.LatestPrivate;
            if (own != null && _slot < own.CooldownTicks.Length && own.CooldownTicks[_slot] > 0) return InputButtons.None;
            _cast = BotCastScript.For(duel.Own.WizardId, Profile, _rng);
            _castStart = tick;
            Casts++;
            return _cast.ButtonsAt(0);
        }

        // ---- Perception over the replicated view (line of sight through replicated solids and volumes) -----------

        private void Observe(ClientDuel duel, int tick, Vec3 me)
        {
            PlayerEntry opponent = duel.InterpolatedOpponent;
            bool visible = false;
            Vec3 position = Vec3.Zero;
            Vec3 velocity = Vec3.Zero;
            if (opponent != null && opponent.Has(PlayerBits.Alive) && duel.OpponentVisible)
            {
                position = duel.OpponentRenderPosition;
                velocity = opponent.VelocityMetersPerSecond;
                Vec3 eye = me + new Vec3(0f, GlobalRules.Movement.StandingEyeHeight, 0f);
                Vec3 chest = position + new Vec3(0f, ChestHeight, 0f);
                visible = (duel.Predictor == null || duel.Predictor.Collision.HasLineOfSight(eye, chest)) && !Obscured(duel, eye, chest);
            }

            if (visible) TicksWithSight++;
            _history[tick % HistorySize] = new Seen { Tick = tick, Visible = visible, Position = position, Velocity = velocity };
        }

        private Seen Perceived(int tick)
        {
            int at = tick - _latency;
            if (at < 0) return default;
            Seen s = _history[at % HistorySize];
            return s.Tick == at ? s : default;
        }

        /// <summary>At least 2 m of a sight-blocking replicated volume along the line blocks acquisition (spec L167).</summary>
        private static bool Obscured(ClientDuel duel, Vec3 from, Vec3 to)
        {
            DuelFrame frame = duel.Latest;
            if (frame == null || frame.Volumes.Count == 0 || duel.Grid == null) return false;
            const float step = 0.25f;
            float length = Vec3.Distance(from, to);
            int samples = Math.Max(1, (int)(length / step));
            float inside = 0f;
            for (int i = 0; i < samples; i++)
            {
                Vec3 p = Vec3.Lerp(from, to, (i + 0.5f) / samples);
                foreach (VolumeEntry v in frame.Volumes)
                {
                    if (!v.BlocksSight) continue;
                    float y = p.Y * 100f;
                    if (y < v.BaseYCm || y > v.BaseYCm + v.HeightCm) continue;
                    var shifted = new Vec2(p.X - v.OffsetXmm / 1000f, p.Z - v.OffsetZmm / 1000f);
                    duel.Grid.CoordOf(shifted, out int x, out int z);
                    if (v.Contains(x, z))
                    {
                        inside += length / samples;
                        break;
                    }
                }
            }

            return inside >= GlobalRules.World.SteamSightBlockPathLength;
        }

        // ---- Helpers ----------------------------------------------------------------------------------------------

        private void Think(int tick, Seen seen)
        {
            _nextThink = tick + Profile.ThinkTicks;
            float err = Profile.EffectiveAimErrorDeg;
            if (seen.Visible)
            {
                Vec3 lateral = seen.Velocity;
                lateral.Y = 0f;
                err += Profile.AimErrorPerSpeedDeg * lateral.Length();
            }

            _aimErrYaw = _rng.Range(-err, err);
            _aimErrPitch = _rng.Range(-err, err) * 0.5f;
            if (Profile.StrafesInCombat && _rng.Chance(0.35f)) _strafe = -_strafe;
        }

        private Vec2 Steer(ClientDuel duel, Vec3 me, Vec3 goal, int tick, out bool jump)
        {
            jump = false;
            if (_nav != null) return _nav.Steer(me, goal, tick, Profile.ThinkTicks, () => Vec2.FromYaw(_rng.Range(0f, 360f)), out jump);
            Vec2 d = goal.XZ() - me.XZ();
            return d.LengthSquared() > 0.25f ? d.Normalized() : Vec2.Zero;
        }

        private void AimAt(ClientDuel duel, Vec3 me, Seen seen, float distance)
        {
            Vec3 target = seen.Position + new Vec3(0f, ChestHeight, 0f);
            if (Profile.LeadsTargets) target += new Vec3(seen.Velocity.X, 0f, seen.Velocity.Z) * (distance / NominalProjectileSpeed);
            Vec3 aim = target - (me + new Vec3(0f, GlobalRules.Movement.StandingEyeHeight, 0f));
            Turn(CMath.YawOf(aim) + _aimErrYaw, CMath.PitchOf(aim) + _aimErrPitch);
        }

        private void Turn(float yawDeg, float pitchDeg)
        {
            float rate = Math.Max(1f, Profile.TurnRateDegPerTick);
            _yaw = CMath.WrapAngle(_yaw + CMath.Clamp(CMath.WrapAngle(yawDeg - _yaw), -rate, rate));
            _pitch = CMath.Clamp(_pitch + CMath.Clamp(pitchDeg - _pitch, -rate, rate), -60f, 60f);
        }

        private static Vec2 ToLocal(Vec2 world, float yawDeg)
        {
            if (world.LengthSquared() < 1e-6f) return Vec2.Zero;
            Vec3 f = CMath.ForwardFromYaw(yawDeg);
            Vec3 r = CMath.RightFromYaw(yawDeg);
            var local = new Vec2(world.X * r.X + world.Y * r.Z, world.X * f.X + world.Y * f.Z);
            float length = local.Length();
            return length > 1f ? local / length : local;
        }

        private MeleeProfile MeleeOf(ClientDuel duel)
        {
            DuelSideWire own = duel.Own;
            for (int i = 0; i < own.EquipmentIds.Count && i < own.EquipmentSlots.Count; i++)
            {
                if (own.EquipmentSlots[i] != EquipmentSlot.MainFocus) continue;
                if (_catalogue != null && _catalogue.TryGetEquipment(own.EquipmentIds[i], out EquipmentDef def))
                {
                    var item = Confluence.Sim.Players.EquippedItem.FromDefinition(def, EquipmentSlot.MainFocus);
                    return item.IsFocusOrWeapon ? item.MeleeProfile : MeleeProfile.Focus;
                }
            }

            return MeleeProfile.Focus;
        }

        private struct Seen
        {
            public int Tick;
            public bool Visible;
            public Vec3 Position;
            public Vec3 Velocity;
        }
    }
}
