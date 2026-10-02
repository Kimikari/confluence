using System;
using System.Collections.Generic;
using Confluence.Core.Rules;
using Confluence.Sim.Hashing;

namespace Confluence.Sim.Combat
{
    /// <summary>Picks the common melee profile of a focus or weapon (spec L100).</summary>
    public static class MeleeProfiles
    {
        /// <summary>
        /// A weapon whose record names a category through its tags (heavy, spear, light_blade) uses that profile;
        /// otherwise weapons use the light-blade profile and wands and staffs the focus profile (spec L100).
        /// </summary>
        public static MeleeProfile For(EquipmentCategory category, IReadOnlyList<string> tags)
        {
            if (category == EquipmentCategory.Weapon && tags != null)
            {
                if (HasTag(tags, "heavy"))
                {
                    return MeleeProfile.Heavy;
                }

                if (HasTag(tags, "spear"))
                {
                    return MeleeProfile.Spear;
                }

                if (HasTag(tags, "light_blade"))
                {
                    return MeleeProfile.LightBlade;
                }
            }

            return MeleeProfile.DefaultFor(category);
        }

        /// <summary>Half angle of the strike sweep: thrusts sweep straight, blades across the committed aim.</summary>
        public static float ArcHalfDeg(MeleeProfile profile)
        {
            if (profile.Equals(MeleeProfile.Heavy))
            {
                return SimRules.Melee.HeavyArcHalfDeg;
            }

            if (profile.Equals(MeleeProfile.LightBlade))
            {
                return SimRules.Melee.LightBladeArcHalfDeg;
            }

            return SimRules.Melee.ThrustArcHalfDeg;
        }

        private static bool HasTag(IReadOnlyList<string> tags, string tag)
        {
            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i], tag, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public enum MeleePhase
    {
        Idle,
        Windup,

        /// <summary>The swept strike volume is live (first ticks after the windup; part of the recovery).</summary>
        Strike,

        Recovery,
    }

    /// <summary>
    /// One player's common melee attack in progress (spec L100-L102): windup, a swept strike committed to the aim
    /// at commitment (no snap or tracking afterward), then recovery for the rest of the cycle. One attack damages
    /// the opponent at most once and may also contact solids.
    /// </summary>
    public sealed class MeleeState
    {
        private readonly List<int> _contactedSolids = new List<int>();

        public MeleePhase Phase { get; private set; }
        public MeleeProfile Profile { get; private set; }
        public string SourceId { get; private set; }
        public int StartTick { get; private set; }
        public int AttackSerial { get; private set; }
        public float CommittedYawDeg { get; private set; }
        public float CommittedPitchDeg { get; private set; }

        /// <summary>The opponent was already damaged by this attack (spec L102: once per attack).</summary>
        public bool HitOpponent { get; private set; }

        /// <summary>Event id of the attack's start, used as the origin of its hit.</summary>
        public int StartEventId { get; private set; }

        public IReadOnlyList<int> ContactedSolids => _contactedSolids;

        public bool Busy => Phase != MeleePhase.Idle;

        public int WindupTicks => Profile.WindupTicks;
        public int CycleTicks => Profile.CycleTicks;
        public int StrikeEndTick => StartTick + WindupTicks + SimRules.Melee.StrikeActiveTicks;
        public int EndTick => StartTick + CycleTicks;

        public bool InRecovery => Phase == MeleePhase.Recovery || Phase == MeleePhase.Strike;

        public void Begin(MeleeProfile profile, string sourceId, int now, float yawDeg, float pitchDeg, int startEventId)
        {
            Profile = profile;
            SourceId = sourceId;
            StartTick = now;
            CommittedYawDeg = yawDeg;
            CommittedPitchDeg = pitchDeg;
            HitOpponent = false;
            StartEventId = startEventId;
            _contactedSolids.Clear();
            AttackSerial++;
            Phase = MeleePhase.Windup;
        }

        /// <summary>Advances the phase for tick now and returns the strike sample index range live this tick (empty when none).</summary>
        public bool Advance(int now, out int strikeTickIndex)
        {
            strikeTickIndex = -1;
            if (Phase == MeleePhase.Idle)
            {
                return false;
            }

            int elapsed = now - StartTick;
            if (elapsed >= CycleTicks)
            {
                Phase = MeleePhase.Idle;
                return false;
            }

            if (elapsed < WindupTicks)
            {
                Phase = MeleePhase.Windup;
                return false;
            }

            int strikeIndex = elapsed - WindupTicks;
            if (strikeIndex < SimRules.Melee.StrikeActiveTicks)
            {
                Phase = MeleePhase.Strike;
                strikeTickIndex = strikeIndex;
                return true;
            }

            Phase = MeleePhase.Recovery;
            return false;
        }

        public void MarkOpponentHit() => HitOpponent = true;

        /// <summary>Records a solid contact; returns false if this attack already contacted it.</summary>
        public bool TryContactSolid(int solidId)
        {
            if (_contactedSolids.Contains(solidId))
            {
                return false;
            }

            _contactedSolids.Add(solidId);
            return true;
        }

        public void Cancel() => Phase = MeleePhase.Idle;

        public void Hash(ref StateHasher h)
        {
            h.Add((int)Phase);
            h.Add(Profile.Name);
            h.Add(SourceId);
            h.Add(StartTick);
            h.Add(AttackSerial);
            h.Add(CommittedYawDeg);
            h.Add(CommittedPitchDeg);
            h.Add(HitOpponent);
            h.Add(_contactedSolids.Count);
        }
    }
}
