using System;
using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Reactions;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Casting;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Effects;
using Confluence.Sim.Hashing;
using Confluence.Sim.Movement;
using Confluence.Sim.Players;
using Confluence.Sim.Status;
using Confluence.Sim.World.Materials;
using SurfaceKind = Confluence.Sim.World.SurfaceKind;

namespace Confluence.Sim.Content.Augments.AugmentsA
{
    // Shared augments of the "Movement arts" family (spec L80-L82 movement, L124 movement caps, L175 surfaces).

    /// <summary>
    /// AUG-097 Economical Sprint (Silver, always). Condition "Use ordinary sprint." Rank 1 "Drain decreases 1 stamina/s.",
    /// rank 2 "Drain decreases 2 stamina/s." Drawback "Dodge costs 1 extra stamina." (spec L80: 12 stamina/s; L82: 25.)
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class EconomicalSprint : IAugmentBehavior
    {
        public const string Id = "AUG-097";
        public const float Rank1Drain = 1f;  // "Drain decreases 1 stamina/s."
        public const float Rank2Drain = 2f;  // "Drain decreases 2 stamina/s."
        public const float DodgePenalty = 1f; // "Dodge costs 1 extra stamina."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.AddModifier(new StatModifier(StatKind.SprintStaminaPerSecond, -AugmentsAKit.ByRank(rank, Rank1Drain, Rank2Drain), Id));
            host.AddModifier(new StatModifier(StatKind.DodgeStaminaCost, DodgePenalty, Id));
        }
    }

    /// <summary>
    /// AUG-098 Deliberate Dodge (Silver, always). Condition "Use ordinary dodge." Rank 1 "Cost decreases 2 stamina.", rank 2
    /// "Cost decreases 4 stamina." Drawback "Dodge recovery increases 0.05 s." The base dodge (spec L82) has no recovery,
    /// so the drawback adds one: for 0.05 s after each dodge's travel ends the dodger cannot move, sprint, jump, or dodge
    /// again — the counterplay's "recovery at destination".
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class DeliberateDodge : IAugmentBehavior
    {
        public const string Id = "AUG-098";
        public const float Rank1Cost = 2f;          // "Cost decreases 2 stamina."
        public const float Rank2Cost = 4f;          // "Cost decreases 4 stamina."
        public const float RecoverySec = 0.05f;     // "Dodge recovery increases 0.05 s."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.AddModifier(new StatModifier(StatKind.DodgeStaminaCost, -AugmentsAKit.ByRank(rank, Rank1Cost, Rank2Cost), Id));
            host.Attach(new Hook());
        }

        private sealed class Hook : EffectHook
        {
            private int _previousDodgeTicks;
            private int _recoveryUntil = -1;

            public Hook() : base(Id)
            {
            }

            public override void OnTick()
            {
                int dodge = Self.Motor.DodgeTicks;
                if (_previousDodgeTicks > 0 && dodge == 0)
                {
                    _recoveryUntil = Now + AugmentsAKit.Ticks(RecoverySec); // the travel ended this tick
                }

                _previousDodgeTicks = dodge;
            }

            public override void AdjustMotor(ref MotorCommand command)
            {
                if (_recoveryUntil >= 0 && Now <= _recoveryUntil)
                {
                    command.Locks |= MotorLocks.Move | MotorLocks.Sprint | MotorLocks.Jump | MotorLocks.Dodge;
                }
            }

            public override void Hash(ref StateHasher h)
            {
                h.Add(_previousDodgeTicks);
                h.Add(_recoveryUntil);
            }
        }
    }

    /// <summary>
    /// AUG-099 Light Landing (Silver, always). Condition "Use ordinary jump." Rank 1 "Cost decreases 1 stamina.", rank 2
    /// "Cost decreases 2 stamina." Drawback "Airborne incoming damage increases 2%." (spec L80: a jump costs 10). The
    /// drawback is a 2% passive vulnerability while airborne, inside the pipeline's passive step and its 45% cap (L112),
    /// re-evaluated at the dispatch point (one tick of latency, D-158).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class LightLanding : IAugmentBehavior
    {
        public const string Id = "AUG-099";
        public const float Rank1Cost = 1f;          // "Cost decreases 1 stamina."
        public const float Rank2Cost = 2f;          // "Cost decreases 2 stamina."
        public const float AirborneVulnerability = 0.02f; // "Airborne incoming damage increases 2%."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.AddModifier(new StatModifier(StatKind.JumpStaminaCost, -AugmentsAKit.ByRank(rank, Rank1Cost, Rank2Cost), Id));
            host.Attach(new ConditionalModifier(Conditions.Airborne, new StatModifier(StatKind.PassiveReduction, -AirborneVulnerability, Id)));
        }
    }

    /// <summary>
    /// AUG-100 Walking Scholar (Silver, always). Condition "Walk without sprinting for 1 s, then cast." Rank 1 "Cast costs
    /// 2 less mana.", rank 2 "Cast costs 3 less mana." Drawback "Sprint resets preparation." After 1 s of continuous
    /// walking (grounded, moving, not sprinting; see <see cref="AugmentsAKit.WalkingMinSpeed"/>) the next spell committed
    /// while still walking pays 2 (3) less inside the 70% floor (spec L124). A cast uses the walk; sprinting, stopping,
    /// or leaving the ground restarts it.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class WalkingScholar : IAugmentBehavior
    {
        public const string Id = "AUG-100";
        public const float Rank1Mana = 2f;   // "Cast costs 2 less mana."
        public const float Rank2Mana = 3f;   // "Cast costs 3 less mana."
        public const float WalkSec = 1f;     // "for 1 s"

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Mana, Rank2Mana)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _mana;
            private int _walked;
            private bool _paid;

            public Hook(float mana) : base(Id)
            {
                _mana = mana;
            }

            public int WalkedTicks => _walked;

            public override void OnEvent(DuelEvent e)
            {
                if (e is CastDuelEvent c && c.Player == Player && c.Kind == CastEventKind.Committed && c.Cast.Slot >= 0 && _walked >= AugmentsAKit.Ticks(WalkSec))
                {
                    _paid = true;
                }
            }

            public override void OnTick()
            {
                if (_paid)
                {
                    _paid = false;
                    _walked = 0; // the cast used this walk
                    TryActivate();
                    return;
                }

                _walked = InCombat && AugmentsAKit.IsWalking(Self) ? _walked + 1 : 0; // sprinting resets the preparation
            }

            public override void AdjustCastPrice(in CastQuote quote, ref float reduction, ref float increase)
            {
                if (_walked >= AugmentsAKit.Ticks(WalkSec) && AugmentsAKit.IsWalking(Self) && AugmentsAKit.CurrentCast(Self).IsValid)
                {
                    reduction += _mana;
                }
            }

            public override void Hash(ref StateHasher h)
            {
                h.Add(_walked);
                h.Add(_paid);
            }
        }
    }

    /// <summary>
    /// AUG-101 Short Burst (Silver, always). Condition "Sprint continuously for at most 1 s." Rank 1 "Speed increases 3%.",
    /// rank 2 "Speed increases 5%." Drawback "After 1 s, sprint drains 1 extra stamina/s." The first 1 s of each
    /// continuous sprint runs 3% (5%) faster under the shared +20% sprint cap (spec L124); from the second second of
    /// the same sprint the drain is 13 instead of 12 per second (L80).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class ShortBurst : IAugmentBehavior
    {
        public const string Id = "AUG-101";
        public const float Rank1Speed = 0.03f; // "Speed increases 3%."
        public const float Rank2Speed = 0.05f; // "Speed increases 5%."
        public const float BurstSec = 1f;      // "at most 1 s"
        public const float LateDrain = 1f;     // "After 1 s, sprint drains 1 extra stamina/s."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Speed, Rank2Speed)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _speed;
            private int _sprintRun;

            public Hook(float speed) : base(Id)
            {
                _speed = speed;
            }

            public override void OnTick() => _sprintRun = Self.Motor.Sprinting ? _sprintRun + 1 : 0;

            public override void AdjustMotor(ref MotorCommand command)
            {
                // The ticks this sprint has already run decide this step: a sprint starting now is in its burst.
                if (_sprintRun < AugmentsAKit.Ticks(BurstSec))
                {
                    command.Tuning.SprintMultiplier = AugmentsAKit.SprintMultiplier(Self.Stats, _speed);
                }
                else
                {
                    command.Tuning.SprintStaminaPerSecond += LateDrain;
                }
            }

            public override void Hash(ref StateHasher h) => h.Add(_sprintRun);
        }
    }

    /// <summary>
    /// AUG-102 Grounded Recovery (Silver, always). Condition "Remain grounded after landing for 0.5 s." Rank 1 "Gain 2
    /// stamina, 4 s cooldown.", rank 2 "Gain 4 stamina, 4 s cooldown." Drawback "Moving above walk interrupts recovery."
    /// A landing (spec L175 drop) followed by 0.5 s on the ground grants the stamina (within the maximum, L78); a jump,
    /// sprint, dodge, or dash before then interrupts that landing's recovery.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class GroundedRecovery : IAugmentBehavior
    {
        public const string Id = "AUG-102";
        public const float Rank1Stamina = 2f;  // "Gain 2 stamina, 4 s cooldown."
        public const float Rank2Stamina = 4f;  // "Gain 4 stamina, 4 s cooldown."
        public const float GroundedSec = 0.5f; // "for 0.5 s"
        public const float CooldownSec = 4f;   // "4 s cooldown"

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Stamina, Rank2Stamina)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _stamina;
            private int _landTick = -1;
            private int _landEvent;
            private int _lastActivation = int.MinValue / 2;

            public Hook(float stamina) : base(Id)
            {
                _stamina = stamina;
            }

            public override void OnEvent(DuelEvent e)
            {
                if (e is MovementEvent m && m.Player == Player && m.Change == MovementChange.Landed)
                {
                    _landTick = m.Tick;
                    _landEvent = m.Id;
                }
            }

            public override void OnTick()
            {
                if (_landTick < 0)
                {
                    return;
                }

                DuelPlayer p = Self;
                if (!p.Motor.Grounded || p.Motor.Sprinting || p.Motor.DodgeTicks > 0 || p.Motor.IsDisplaced)
                {
                    _landTick = -1; // moving above walk (or leaving the ground) interrupts this recovery
                    return;
                }

                if (Now - _landTick < AugmentsAKit.Ticks(GroundedSec))
                {
                    return;
                }

                _landTick = -1;
                if (InCombat && Now - _lastActivation >= AugmentsAKit.Ticks(CooldownSec) && TryActivate(_landEvent, -1, _stamina))
                {
                    _lastActivation = Now;
                    Host.System.GainResource(Player, EffectResource.Stamina, _stamina, Id);
                }
            }

            public override void Hash(ref StateHasher h)
            {
                h.Add(_landTick);
                h.Add(_lastActivation);
            }
        }
    }

    /// <summary>
    /// AUG-104 Inward Step (Silver, always). Condition "Dodge toward center while closing boundary is active." Rank 1 "Cost
    /// decreases 2 stamina.", rank 2 "Cost decreases 4 stamina." Drawback "Outward dodges cost 2 extra stamina." A dodge
    /// whose direction points toward the arena center (less than 90 degrees from it) costs 2 (4) less while the boundary
    /// contracts (spec L48, L51); one pointing away from the center costs 2 more at any time (a sideways dodge is
    /// neither). The cost is decided at the motor step that starts the dodge, from the same direction the motor uses.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class InwardStep : IAugmentBehavior
    {
        public const string Id = "AUG-104";
        public const float Rank1Cost = 2f;       // "Cost decreases 2 stamina."
        public const float Rank2Cost = 4f;       // "Cost decreases 4 stamina."
        public const float OutwardPenalty = 2f;  // "Outward dodges cost 2 extra stamina."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Cost, Rank2Cost)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _reduction;

            public Hook(float reduction) : base(Id)
            {
                _reduction = reduction;
            }

            public override void AdjustMotor(ref MotorCommand command)
            {
                if (!command.DodgePressed)
                {
                    return;
                }

                Vec3 toCenter = World.Layout.Center - Self.Position;
                toCenter = new Vec3(toCenter.X, 0f, toCenter.Z);
                if (toCenter.LengthSquared() < 1e-6f)
                {
                    return;
                }

                float dot = Vec3.Dot(DodgeDirection(command), toCenter.Normalized());
                if (dot > 1e-4f && BoundaryClosing())
                {
                    command.Tuning.DodgeStaminaCost = Math.Max(0f, command.Tuning.DodgeStaminaCost - _reduction);
                }
                else if (dot < -1e-4f)
                {
                    command.Tuning.DodgeStaminaCost += OutwardPenalty;
                }
            }

            private bool BoundaryClosing() =>
                World.Phase == DuelPhase.Combat && World.Layout.Boundary != null && World.CombatSeconds >= World.Layout.Boundary.ContractStartSec;

            // The motor's dodge direction (spec L82): the movement input, else straight back.
            private static Vec3 DodgeDirection(in MotorCommand c)
            {
                Vec2 move = c.Move;
                float mag = move.Length();
                if (mag > 1f) move = move / mag;
                Vec3 forward = CMath.ForwardFromYaw(c.YawDeg);
                Vec3 right = CMath.RightFromYaw(c.YawDeg);
                Vec3 wish = right * move.X + forward * move.Y;
                return wish.LengthSquared() > 0f ? wish.Normalized() : -forward;
            }
        }
    }

    /// <summary>
    /// AUG-107 Exit Interview (Gold, always). Condition "Leave steam at walking speed." Rank 1 "Gain 3% walk speed for 1 s,
    /// 5 s cooldown.", rank 2 "Gain 5% walk speed for 1 s, 5 s cooldown." Drawback "Re-entering steam ends buff." Walking
    /// (not sprinting) out of a steam volume (spec L144, L167; tested at the chest) grants the walking bonus under the
    /// +20% cap (L124) for 1 s; stepping back into steam ends it.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class ExitInterview : IAugmentBehavior
    {
        public const string Id = "AUG-107";
        public const float Rank1Speed = 0.03f; // "Gain 3% walk speed for 1 s, 5 s cooldown."
        public const float Rank2Speed = 0.05f; // "Gain 5% walk speed for 1 s, 5 s cooldown."
        public const float DurationSec = 1f;
        public const float CooldownSec = 5f;

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Speed, Rank2Speed)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _speed;
            private readonly List<ObscurationVolume> _scratch = new List<ObscurationVolume>();
            private bool _inSteam;
            private int _buffUntil = -1;
            private int _lastActivation = int.MinValue / 2;

            public Hook(float speed) : base(Id)
            {
                _speed = speed;
            }

            public override void OnTick()
            {
                bool inSteam = InSteam();
                if (_inSteam && !inSteam && InCombat && AugmentsAKit.IsWalking(Self) && Now - _lastActivation >= AugmentsAKit.Ticks(CooldownSec) && TryActivate())
                {
                    _lastActivation = Now;
                    _buffUntil = Now + AugmentsAKit.Ticks(DurationSec);
                }
                else if (inSteam || (_buffUntil >= 0 && Now >= _buffUntil))
                {
                    _buffUntil = -1; // "Re-entering steam ends buff."
                }

                _inSteam = inSteam;
            }

            private bool InSteam()
            {
                if (!(World.Materials is ReactionMaterials materials) || materials.World == null)
                {
                    return false;
                }

                _scratch.Clear();
                DuelPlayer p = Self;
                materials.World.GetVolumesAt(p.Position + new Vec3(0f, p.Motor.Height * 0.5f, 0f), _scratch);
                for (int i = 0; i < _scratch.Count; i++)
                {
                    if (_scratch[i].Kind == VolumeKind.Steam) return true;
                }

                return false;
            }

            public override void AdjustMotor(ref MotorCommand command)
            {
                if (_buffUntil >= 0 && Now < _buffUntil)
                {
                    command.Tuning.WalkMultiplier = AugmentsAKit.WalkMultiplier(Self.Stats, _speed);
                }
            }

            public override void Hash(ref StateHasher h)
            {
                h.Add(_inSteam);
                h.Add(_buffUntil);
                h.Add(_lastActivation);
            }
        }
    }

    /// <summary>
    /// AUG-108 Ice Route (Gold, always). Condition "Sprint on ice." Rank 1 "Sprint cost decreases 1 stamina/s.", rank 2
    /// "Sprint cost decreases 2 stamina/s." Drawback "Normal ice traction still applies." Sprinting on an ice surface
    /// (spec L145, L175) drains 1 (2) less per second; the ice's reduced braking and drift are unchanged.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class IceRoute : IAugmentBehavior
    {
        public const string Id = "AUG-108";
        public const float Rank1Drain = 1f; // "Sprint cost decreases 1 stamina/s."
        public const float Rank2Drain = 2f; // "Sprint cost decreases 2 stamina/s."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new SurfaceSprintHook(Id, SurfaceKind.Ice, -AugmentsAKit.ByRank(rank, Rank1Drain, Rank2Drain)));
        }
    }

    /// <summary>Changes the sprint drain on one surface at the motor step.</summary>
    internal sealed class SurfaceSprintHook : EffectHook
    {
        private readonly SurfaceKind _surface;
        private readonly float _drain;

        public SurfaceSprintHook(string sourceId, SurfaceKind surface, float drain) : base(sourceId)
        {
            _surface = surface;
            _drain = drain;
        }

        public override void AdjustMotor(ref MotorCommand command)
        {
            if (Self.Motor.Grounded && command.Surface.Kind == _surface)
            {
                command.Tuning.SprintStaminaPerSecond = Math.Max(0f, command.Tuning.SprintStaminaPerSecond + _drain);
            }
        }
    }

    /// <summary>
    /// AUG-109 Wet Route (Gold, always). Condition "Walk through shallow water." Rank 1 "Movement penalty reduced by 15% of
    /// normal penalty.", rank 2 "Movement penalty reduced by 25% of normal penalty." Drawback "Wetness persists 0.5 s longer
    /// after exit." Shallow water itself carries no penalty (spec L175), so the penalty is the slow on the walker while
    /// their feet are in liquid water (a water field's slow or any slow applied there, the strongest one, L116): it is
    /// reduced by 15% (25%) of its amount. When the Wet from the water (D-150) ends after the walker left the water, they
    /// stay Wet 0.5 s longer.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class WetRoute : IAugmentBehavior
    {
        public const string Id = "AUG-109";
        public const float Rank1Relief = 0.15f;   // "reduced by 15% of normal penalty"
        public const float Rank2Relief = 0.25f;   // "reduced by 25% of normal penalty"
        public const float LingerSec = 0.5f;      // "Wetness persists 0.5 s longer after exit."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Relief, Rank2Relief)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _relief;
            private bool _wetEnded;

            public Hook(float relief) : base(Id)
            {
                _relief = relief;
            }

            public override void OnEvent(DuelEvent e)
            {
                if (e is StatusEvent s && s.Player == Player && s.Kind == StatusKind.Wet && s.Change == StatusChange.Expired
                    && string.Equals(s.DefinitionId, MaterialRules.WetCellSource, StringComparison.Ordinal))
                {
                    _wetEnded = true;
                }
            }

            public override void OnTick()
            {
                if (!_wetEnded)
                {
                    return;
                }

                _wetEnded = false;
                if (InCombat && !InWater())
                {
                    World.ApplyStatus(Player, StatusApplication.Of(StatusKind.Wet, Id, -1, AugmentsAKit.Ticks(LingerSec)));
                }
            }

            private bool InWater() =>
                Self.Motor.Grounded && World.Materials is ReactionMaterials materials && materials.World != null && materials.StandsInWater(Self.Position);

            public override void AdjustMotor(ref MotorCommand command)
            {
                if (command.Surface.Kind != SurfaceKind.Wet || !Self.Motor.Grounded)
                {
                    return;
                }

                float slow = 1f - command.SpeedMultiplier;
                if (slow > 0f)
                {
                    command.SpeedMultiplier = 1f - slow * (1f - _relief);
                }
            }

            public override void Hash(ref StateHasher h) => h.Add(_wetEnded);
        }
    }

    /// <summary>
    /// AUG-110 Sand Route (Gold, always). Condition "Sprint on sand." Rank 1 "Gain 3% sprint speed.", rank 2 "Gain 5% sprint
    /// speed." Drawback "Sprint on wet ground costs 1 extra stamina/s." Sprinting with the feet on a cell holding sand
    /// (spec L154, damp sand included) is 3% (5%) faster under the +20% sprint cap (L124); sprinting on wet ground
    /// (liquid water, L175) drains 13 instead of 12 per second (L80).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class SandRoute : IAugmentBehavior
    {
        public const string Id = "AUG-110";
        public const float Rank1Speed = 0.03f; // "Gain 3% sprint speed."
        public const float Rank2Speed = 0.05f; // "Gain 5% sprint speed."
        public const float WetDrain = 1f;      // "Sprint on wet ground costs 1 extra stamina/s."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Speed, Rank2Speed)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _speed;

            public Hook(float speed) : base(Id)
            {
                _speed = speed;
            }

            public override void AdjustMotor(ref MotorCommand command)
            {
                if (!Self.Motor.Grounded)
                {
                    return;
                }

                if (OnSand(command))
                {
                    command.Tuning.SprintMultiplier = AugmentsAKit.SprintMultiplier(Self.Stats, _speed);
                }

                if (command.Surface.Kind == SurfaceKind.Wet)
                {
                    command.Tuning.SprintStaminaPerSecond += WetDrain;
                }
            }

            private bool OnSand(in MotorCommand command)
            {
                if (command.Surface.Kind == SurfaceKind.DampSand)
                {
                    return true;
                }

                return World.Materials is ReactionMaterials materials && materials.World != null && materials.World.UnitsAt(Self.Position).Sand > 0;
            }
        }
    }

    /// <summary>
    /// AUG-115 Leapfrog (Prismatic, always). Condition "Jump after a direct weapon hit within 0.4 s." Rank 1 "Jump costs 2
    /// less stamina.", rank 2 "Jump costs 4 less stamina." Drawback "The weapon attack's recovery still completes." The
    /// first jump within 0.4 s of the owner's weapon hit costs 8 (6) instead of 10 (spec L80); jumping never cancels the
    /// strike's recovery (L102, L106).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class Leapfrog : IAugmentBehavior
    {
        public const string Id = "AUG-115";
        public const float Rank1Cost = 2f;    // "Jump costs 2 less stamina."
        public const float Rank2Cost = 4f;    // "Jump costs 4 less stamina."
        public const float WindowSec = 0.4f;  // "within 0.4 s"

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Cost, Rank2Cost)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _reduction;
            private int _hitTick = -1;

            public Hook(float reduction) : base(Id)
            {
                _reduction = reduction;
            }

            public override void OnEvent(DuelEvent e)
            {
                if (e is DamageEvent d && AugmentsAKit.IsWeaponHit(d, Player))
                {
                    _hitTick = d.Tick;
                }
                else if (e is MovementEvent m && m.Player == Player && m.Change == MovementChange.Jumped && _hitTick >= 0 && Now - _hitTick <= AugmentsAKit.Ticks(WindowSec))
                {
                    _hitTick = -1; // one cheaper jump per hit
                }
            }

            public override void AdjustMotor(ref MotorCommand command)
            {
                if (command.JumpPressed && _hitTick >= 0 && Now - _hitTick <= AugmentsAKit.Ticks(WindowSec))
                {
                    command.Tuning.JumpStaminaCost = Math.Max(0f, command.Tuning.JumpStaminaCost - _reduction);
                }
            }

            public override void Hash(ref StateHasher h) => h.Add(_hitTick);
        }
    }
}

namespace Confluence.Sim.Content.Augments.AugmentsA
{
    /// <summary>
    /// AUG-105 Wall Reading (Gold, always). Condition "Jump within 0.5 m of solid wall." Rank 1 "Add 0.4 m lateral motion
    /// away from wall.", rank 2 "Add 0.7 m lateral motion away from wall." Drawback "Costs 2 extra stamina; no wall flight, 5
    /// s cooldown." An ordinary jump (spec L80) taken with a vertical solid face within 0.5 m of the body (probed in 16
    /// horizontal directions at waist height) costs 12 instead of 10 stamina and adds 0.4 m (0.7 m) of horizontal travel
    /// along the face's normal, spread over <see cref="PushSec"/> right after takeoff (a self displacement under the motor's
    /// collision and the legal boundary, L51); the jump's own vertical arc is unchanged ("no wall flight": no extra height,
    /// no jump from a wall, no second use within 5 s). A visible cue marks the exit (counterplay "Cover the visible wall
    /// exit."). Jumps while the cooldown runs cost the normal 10 and get no motion.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class WallReading : IAugmentBehavior
    {
        public const string Id = "AUG-105";
        public const float WallRange = 0.5f;     // "within 0.5 m of solid wall"
        public const float Rank1Lateral = 0.4f;  // "Add 0.4 m lateral motion away from wall."
        public const float Rank2Lateral = 0.7f;  // "Add 0.7 m lateral motion away from wall."
        public const float ExtraStamina = 2f;    // "Costs 2 extra stamina"
        public const float CooldownSec = 5f;     // "5 s cooldown"

        /// <summary>Time over which the lateral motion is added (open number: a short shove that keeps the jump readable).</summary>
        public const float PushSec = 0.1f;

        /// <summary>Probe height above the feet (waist of the 1.8 m standing capsule).</summary>
        public const float ProbeHeight = 0.9f;

        public const string ExitCue = "wall-exit";

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Lateral, Rank2Lateral)));
        }

        /// <summary>The outward normal of the nearest vertical solid face within range of a body standing at the feet, if any.</summary>
        public static bool TryFindWall(Confluence.Sim.World.CollisionWorld collision, Vec3 feet, out Vec3 away)
        {
            away = Vec3.Zero;
            Vec3 origin = feet + new Vec3(0f, ProbeHeight, 0f);
            float reach = CharacterMotor.Radius + WallRange;
            float best = float.PositiveInfinity;
            for (int i = 0; i < 16; i++)
            {
                double a = i * Math.PI / 8.0;
                Vec3 dir = new Vec3((float)Math.Sin(a), 0f, (float)Math.Cos(a));
                if (!collision.SegmentCast(origin, origin + dir * reach, Confluence.Sim.World.SolidMask.Movement, out Confluence.Sim.World.WorldHit hit))
                {
                    continue;
                }

                Vec3 n = new Vec3(hit.Normal.X, 0f, hit.Normal.Z);
                if (Math.Abs(hit.Normal.Y) > 0.5f || n.LengthSquared() < 1e-6f || hit.Distance >= best)
                {
                    continue; // floors, ceilings and slopes are not walls
                }

                best = hit.Distance;
                away = n.Normalized();
            }

            return best < float.PositiveInfinity;
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _lateral;
            private int _lastUse = int.MinValue / 2;
            private Vec3 _preStep = new Vec3(float.NaN, 0f, 0f);
            private int _jumpEvent;
            private Vec3 _away;
            private int _restoreAt = -1;
            private Vec3 _restoreVelocity;

            public Hook(float lateral) : base(Id)
            {
                _lateral = lateral;
            }

            private bool Ready => Now - _lastUse >= AugmentsAKit.Ticks(CooldownSec);

            private Vec3 PreStep => float.IsNaN(_preStep.X) ? Self.Position : _preStep;

            public override void AdjustMotor(ref MotorCommand command)
            {
                if (command.JumpPressed && Self.Motor.Grounded && InCombat && Ready && TryFindWall(World.Collision, Self.Position, out _))
                {
                    command.Tuning.JumpStaminaCost += ExtraStamina;
                }
            }

            public override void OnEvent(DuelEvent e)
            {
                if (e is MovementEvent m && m.Player == Player && m.Change == MovementChange.Jumped && InCombat && Ready
                    && TryFindWall(World.Collision, PreStep, out Vec3 away))
                {
                    _jumpEvent = m.Id;
                    _away = away;
                }
            }

            public override void OnTick()
            {
                if (_restoreAt >= 0 && Now >= _restoreAt && !Self.Motor.IsDisplaced)
                {
                    // The shove only adds its distance: the body keeps the horizontal velocity it jumped with.
                    _restoreAt = -1;
                    Vec3 v = Self.Motor.Velocity;
                    Self.Motor.Velocity = new Vec3(_restoreVelocity.X, v.Y, _restoreVelocity.Z);
                }

                if (_jumpEvent != 0)
                {
                    int origin = _jumpEvent;
                    _jumpEvent = 0;
                    if (!Self.Motor.IsDisplaced && TryActivate(origin, -1, _lateral))
                    {
                        _lastUse = Now;
                        int ticks = Math.Max(1, AugmentsAKit.Ticks(PushSec));
                        Vec3 hv = new Vec3(Self.Motor.Velocity.X, 0f, Self.Motor.Velocity.Z);
                        _restoreVelocity = hv;
                        _restoreAt = Now + ticks;
                        CharacterMotor.BeginDisplacement(ref Self.Motor, DisplacementKind.Dash, hv + _away * (_lateral / (ticks * Confluence.Core.Time.Ticks.Dt)), ticks);
                        EmitCue(ExitCue, _lateral);
                    }
                }

                _preStep = Self.Position;
            }

            public override void Hash(ref StateHasher h)
            {
                h.Add(_lastUse);
                h.Add(_jumpEvent);
                h.Add(_away);
                h.Add(_restoreAt);
                h.Add(_restoreVelocity);
            }
        }
    }
}
