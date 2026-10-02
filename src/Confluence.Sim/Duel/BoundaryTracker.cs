using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Hashing;

namespace Confluence.Sim.Duel
{
    /// <summary>
    /// Boundary escalation of one player (spec L51): 15 per second for the first 2 s outside, then 30 per second,
    /// then 60 per second after 4 s. Returning inside resets the escalation after 1 s inside; leaving again sooner
    /// continues it.
    /// </summary>
    public sealed class BoundaryTracker
    {
        public static int ResetTicks => Ticks.FromSeconds(GlobalRules.Boundary.EscalationResetInsideSec);

        /// <summary>Escalation clock: ticks counted outside since the last reset.</summary>
        public int OutsideTicks { get; private set; }

        /// <summary>Consecutive ticks inside since the last exit.</summary>
        public int InsideStreak { get; private set; }

        public bool Outside { get; private set; }

        /// <summary>Boundary damage was taken since the last reset (FEAT-024).</summary>
        public bool DamagedSinceReset { get; private set; }

        public float TotalDamage { get; private set; }

        /// <summary>Seconds of escalation already accrued before the current tick.</summary>
        public float EscalationSeconds => OutsideTicks / (float)Ticks.PerSecond;

        /// <summary>
        /// Advances one tick and returns the boundary damage for it. Sets exited/returned on transitions and reset
        /// when 1 s inside clears the escalation; resetAfterDamage is true when that reset follows boundary damage.
        /// </summary>
        public float Step(bool inside, out bool exited, out bool returned, out bool reset, out bool resetAfterDamage)
        {
            exited = false;
            returned = false;
            reset = false;
            resetAfterDamage = false;
            if (!inside)
            {
                exited = !Outside;
                Outside = true;
                InsideStreak = 0;
                float dps = GlobalRules.Boundary.DamagePerSecond(OutsideTicks / (float)Ticks.PerSecond);
                OutsideTicks++;
                float damage = dps / Ticks.PerSecond;
                TotalDamage += damage;
                DamagedSinceReset = true;
                return damage;
            }

            returned = Outside;
            Outside = false;
            if (OutsideTicks > 0)
            {
                InsideStreak++;
                if (InsideStreak >= ResetTicks)
                {
                    reset = true;
                    resetAfterDamage = DamagedSinceReset;
                    OutsideTicks = 0;
                    DamagedSinceReset = false;
                }
            }

            return 0f;
        }

        public void Hash(ref StateHasher h)
        {
            h.Add(OutsideTicks);
            h.Add(InsideStreak);
            h.Add(Outside);
            h.Add(DamagedSinceReset);
            h.Add(TotalDamage);
        }
    }
}
