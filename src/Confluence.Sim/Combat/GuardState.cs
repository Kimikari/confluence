using System;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Hashing;

namespace Confluence.Sim.Combat
{
    /// <summary>
    /// Common guard of one player (spec L104-L106): 25 stamina upfront, held at most 0.8 s, 3 s cooldown from
    /// release, a frontal 120 degree arc reducing weapon and projectile direct damage by 50% (80% during the first
    /// 0.12 s, with 5 mana once per guard on a successful perfect block), and a punishable 0.25 s release window.
    /// </summary>
    public sealed class GuardState
    {
        private const int Never = int.MinValue / 2;

        public static int MaxHoldTicks => Ticks.FromSeconds(GlobalRules.Guard.MaxHoldSec);
        public static int PerfectTicks => Ticks.FromSeconds(GlobalRules.Guard.PerfectWindowSec);
        public static int CooldownTicks => Ticks.FromSeconds(GlobalRules.Guard.CooldownSec);
        public static int PunishTicks => Ticks.FromSeconds(GlobalRules.Guard.ReleasePunishWindowSec);

        /// <summary>Half of the frontal arc: 60 degrees either side of the facing.</summary>
        public static float HalfArcDeg => GlobalRules.Guard.ArcDeg * 0.5f;

        public bool Active { get; private set; }
        public int StartTick { get; private set; } = Never;
        public int ReleaseTick { get; private set; } = Never;
        public int CooldownUntil { get; private set; } = Never;

        /// <summary>The perfect-guard mana reward was already granted for this guard.</summary>
        public bool PerfectRewardGiven { get; private set; }

        public int GuardsStarted { get; private set; }

        public bool OnCooldown(int now) => now < CooldownUntil;

        /// <summary>The clearly punishable 0.25 s after a release (spec L104).</summary>
        public bool InReleaseWindow(int now) => !Active && now >= ReleaseTick && now < ReleaseTick + PunishTicks;

        public bool IsPerfect(int now) => Active && now - StartTick < PerfectTicks;

        public bool CanStart(int now) => !Active && !OnCooldown(now);

        public void Start(int now)
        {
            Active = true;
            StartTick = now;
            PerfectRewardGiven = false;
            GuardsStarted++;
        }

        /// <summary>Releases the guard; the cooldown runs from release (spec L104).</summary>
        public void Release(int now) => Release(now, CooldownTicks);

        /// <summary>Releases the guard with the player's modified cooldown (stat block, spec L104, L124).</summary>
        public void Release(int now, int cooldownTicks)
        {
            if (!Active)
            {
                return;
            }

            Active = false;
            ReleaseTick = now;
            CooldownUntil = now + Math.Max(0, cooldownTicks);
        }

        /// <summary>True when the guard has been held for its maximum duration.</summary>
        public bool HoldExpired(int now) => HoldExpired(now, MaxHoldTicks);

        /// <summary>True when the guard has been held for the player's (at most 0.8 s) hold limit.</summary>
        public bool HoldExpired(int now, int maxHoldTicks) => Active && now - StartTick >= Math.Min(maxHoldTicks, MaxHoldTicks);

        /// <summary>Marks the once-per-guard perfect reward; returns false if it was already granted.</summary>
        public bool TryClaimPerfectReward()
        {
            if (PerfectRewardGiven)
            {
                return false;
            }

            PerfectRewardGiven = true;
            return true;
        }

        /// <summary>True when the source lies inside the visible frontal 120 degree arc (spec L104); rear and flank are not covered.</summary>
        public static bool InArc(Vec3 defenderPosition, float defenderYawDeg, Vec3 sourcePosition)
        {
            Vec3 toSource = sourcePosition - defenderPosition;
            if (toSource.LengthXZ() < CMath.Epsilon)
            {
                return true;
            }

            float yawToSource = CMath.YawOf(toSource);
            return Math.Abs(CMath.DeltaAngle(defenderYawDeg, yawToSource)) <= HalfArcDeg + 1e-3f;
        }

        /// <summary>Damage fraction removed by the guard at now: 0.8 perfect, 0.5 normal (never full immunity, spec L106).</summary>
        public float ReductionAt(int now) => IsPerfect(now) ? GlobalRules.Guard.PerfectDamageReduction : GlobalRules.Guard.DamageReduction;

        public void Reset()
        {
            Active = false;
            StartTick = Never;
            ReleaseTick = Never;
            CooldownUntil = Never;
            PerfectRewardGiven = false;
        }

        public void Hash(ref StateHasher h)
        {
            h.Add(Active);
            h.Add(StartTick);
            h.Add(ReleaseTick);
            h.Add(CooldownUntil);
            h.Add(PerfectRewardGiven);
        }
    }
}
