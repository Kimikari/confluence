using System;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Movement;
using Confluence.Sim.Players;

namespace Confluence.Client.Laboratory
{
    /// <summary>Produces one player's command for the next tick from the world it can see.</summary>
    public interface IDuelPilot
    {
        PlayerInput Next(DuelWorld world, int self);
    }

    /// <summary>What the Laboratory training dummy does (spec L343 practice dummies; L381 bots are Stage 6).</summary>
    public enum DummyMode
    {
        /// <summary>Stands still facing the player.</summary>
        Idle,

        /// <summary>
        /// Faces the player and raises its guard against a melee windup when the guard is ready, holding it through the
        /// strike (spec L102-L104): practise guard angles, and baiting the 3 s guard cooldown before the real strike.
        /// </summary>
        Guard,

        /// <summary>Strafes left and right facing the player (practise tracking).</summary>
        Strafe,

        /// <summary>Closes to melee reach and strikes (practise guard timing and dodges, spec L100-L104).</summary>
        Spar,
    }

    /// <summary>Shared geometry for pilots: aim angles toward a point in Core convention.</summary>
    public static class PilotMath
    {
        /// <summary>Core yaw (0 faces +Z, positive turns right) and pitch (positive up) from an eye to a point.</summary>
        public static void Aim(Vec3 eye, Vec3 target, out float yawDeg, out float pitchDeg)
        {
            Vec3 d = target - eye;
            float horizontal = (float)Math.Sqrt(d.X * d.X + d.Z * d.Z);
            yawDeg = (float)(Math.Atan2(d.X, d.Z) * 180.0 / Math.PI);
            pitchDeg = (float)(Math.Atan2(d.Y, Math.Max(1e-4f, horizontal)) * 180.0 / Math.PI);
        }

        public static float HorizontalDistance(Vec3 a, Vec3 b) => Vec3.DistanceXZ(a, b);
    }

    /// <summary>
    /// The Laboratory training dummy. It acts only through <see cref="PlayerInput"/> like any player, so every rule
    /// (stamina, guard cooldown, melee cycle, alcove ward) applies to it exactly as to the player.
    /// </summary>
    public sealed class TrainingDummyPilot : IDuelPilot
    {
        public static readonly int StrafePeriodTicks = Ticks.FromSeconds(1.5f);

        public TrainingDummyPilot(DummyMode mode)
        {
            Mode = mode;
        }

        public DummyMode Mode { get; set; }

        public PlayerInput Next(DuelWorld world, int self)
        {
            DuelPlayer me = world.Player(self);
            DuelPlayer target = world.Opponent(self);
            PilotMath.Aim(me.Eye, target.Position + new Vec3(0f, 1.2f, 0f), out float yaw, out float pitch);
            var input = PlayerInput.Idle(yaw);
            input.PitchDeg = pitch;
            input.SelectedSlot = me.SelectedSlot;
            if (!me.Alive || world.Phase != DuelPhase.Combat)
            {
                return input;
            }

            int now = world.Tick + 1;
            switch (Mode)
            {
                case DummyMode.Guard:
                    // A fresh press starts the guard; holding keeps it until the attack is over or the 0.8 s hold ends.
                    MeleePhase attack = target.Melee.Phase;
                    bool threatened = attack == MeleePhase.Windup || attack == MeleePhase.Strike;
                    if (threatened && (me.Guard.Active || (me.Guard.CanStart(now) && !me.IsHeld(InputButtons.Guard))))
                    {
                        input.Buttons |= InputButtons.Guard;
                    }

                    break;
                case DummyMode.Strafe:
                    input.MoveX = (now / StrafePeriodTicks) % 2 == 0 ? 1f : -1f;
                    break;
                case DummyMode.Spar:
                    float reach = GlobalRules.Melee.FocusReach;
                    float distance = PilotMath.HorizontalDistance(me.Position, target.Position);
                    if (distance > reach)
                    {
                        input.MoveY = 1f;
                    }
                    else if (!me.Melee.Busy && !me.IsHeld(InputButtons.Melee))
                    {
                        input.Buttons |= InputButtons.Melee;
                    }

                    break;
            }

            return input;
        }
    }

    /// <summary>
    /// Scripted demonstration pilot for smoke screenshots and tests: walks toward the opponent, guards briefly, and
    /// strikes with common melee once in reach. It drives the player through the same input path as the keyboard.
    /// </summary>
    public sealed class DemoPilot : IDuelPilot
    {
        public DemoPilot(int maxStrikes = int.MaxValue)
        {
            MaxStrikes = maxStrikes;
        }

        /// <summary>Strikes the pilot starts before it only keeps facing the opponent (keeps a demonstration duel alive).</summary>
        public int MaxStrikes { get; set; }

        public int Strikes { get; private set; }

        /// <summary>Distance the pilot backs off to once its strikes are used.</summary>
        public float RetreatDistance { get; set; } = 3f;

        /// <summary>Meters short of strike range at which the pilot stops walking in.</summary>
        public const float StoppingMargin = 0.3f;

        public PlayerInput Next(DuelWorld world, int self)
        {
            DuelPlayer me = world.Player(self);
            DuelPlayer target = world.Opponent(self);
            PilotMath.Aim(me.Eye, target.Position + new Vec3(0f, 1.1f, 0f), out float yaw, out float pitch);
            var input = PlayerInput.Idle(yaw);
            input.PitchDeg = pitch;
            if (!me.Alive || world.Phase != DuelPhase.Combat)
            {
                return input;
            }

            // Reach is measured from the attacker's center line to the target capsule (spec L100), so the pilot strikes
            // from just inside reach plus most of the target's radius; after its strikes it backs off to watch.
            float distance = PilotMath.HorizontalDistance(me.Position, target.Position);
            float strikeRange = GlobalRules.Melee.FocusReach + CharacterMotor.Radius * 0.6f;
            if (Strikes >= MaxStrikes)
            {
                input.MoveY = distance < RetreatDistance ? -1f : 0f;
            }
            else if (distance > strikeRange + StoppingMargin)
            {
                input.MoveY = 1f; // stop a little early: walking momentum carries most of the rest
            }
            else if (distance > strikeRange)
            {
                input.MoveY = 0.3f; // creep the last few centimeters
            }
            else
            {
                input.MoveY = distance < strikeRange - 0.6f ? -1f : 0f; // keep spacing
                if (!me.Melee.Busy && !me.IsHeld(InputButtons.Melee) && me.Resources.Stamina >= GlobalRules.Melee.FocusStamina)
                {
                    input.Buttons |= InputButtons.Melee;
                    Strikes++;
                }
            }

            return input;
        }
    }
}
