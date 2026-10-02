using System;
using System.Globalization;

namespace Confluence.Casting
{
    /// <summary>The single stated benefit a mastered cast may earn (spec L124, L478).</summary>
    public enum MasteryReward : byte
    {
        None = 0,

        /// <summary>Pay less of the listed mana (efficiency category, cap 20%).</summary>
        ManaEfficiency,

        /// <summary>Pay less of the spell's stamina cost (efficiency category, cap 20%; WIZ-016 Motion).</summary>
        StaminaEfficiency,

        /// <summary>More direct damage on the primary payload only (attribute category, cap 15%; spec L480).</summary>
        DirectDamage,

        /// <summary>Faster projectile travel (attribute category, cap 15%).</summary>
        ProjectileSpeed,

        /// <summary>Shorter intrinsic windup (attribute category, cap 15%), never removing it entirely (spec L86).</summary>
        WindupReduction,
    }

    /// <summary>
    /// A cast's one grammar result (spec L478-L480, L508): either one reward or the exact reason it was missed.
    /// Recasts, splits, reflections, and delayed portions reuse the source cast's result rather than earning another.
    /// Magnitudes are clamped to the shared category caps on construction.
    /// </summary>
    public readonly struct MasteryResult : IEquatable<MasteryResult>
    {
        private MasteryResult(MasteryReward reward, float magnitude, int windupCapTicks, MissReason miss, int timingErrorTicks)
        {
            Reward = reward;
            Magnitude = MasteryClamp.ClampReward(reward, magnitude);
            WindupCapTicks = windupCapTicks;
            Miss = reward == MasteryReward.None ? miss : MissReason.None;
            TimingErrorTicks = timingErrorTicks;
        }

        public MasteryReward Reward { get; }

        /// <summary>Reward fraction (0.12 = 12%), already within its category cap.</summary>
        public float Magnitude { get; }

        /// <summary>Absolute cap on a windup reduction in ticks (WIZ-005 "capped at a 0.06 s reduction"); 0 = none.</summary>
        public int WindupCapTicks { get; }

        /// <summary>Why mastery was missed; <see cref="MissReason.None"/> when mastered.</summary>
        public MissReason Miss { get; }

        /// <summary>Signed ticks outside the qualifying window for timing misses (negative early, positive late).</summary>
        public int TimingErrorTicks { get; }

        public bool IsMastered => Reward != MasteryReward.None;

        public static MasteryResult Mastered(MasteryReward reward, float magnitude, int windupCapTicks = 0)
        {
            if (reward == MasteryReward.None) throw new ArgumentException("A mastered result needs a reward.", nameof(reward));
            if (!(magnitude > 0f)) throw new ArgumentOutOfRangeException(nameof(magnitude));
            return new MasteryResult(reward, magnitude, windupCapTicks, MissReason.None, 0);
        }

        public static MasteryResult Normal(MissReason miss, int timingErrorTicks = 0)
        {
            if (miss == MissReason.None) throw new ArgumentException("A normal result states why mastery was missed.", nameof(miss));
            return new MasteryResult(MasteryReward.None, 0f, 0, miss, timingErrorTicks);
        }

        /// <summary>Guided normal preparation: reliable, never rewarded (spec L470).</summary>
        public static MasteryResult Guided => Normal(MissReason.Guided);

        public string Describe()
        {
            if (IsMastered)
            {
                return Reward + " +" + (Magnitude * 100f).ToString("0.#", CultureInfo.InvariantCulture) + "%";
            }

            string text = MissReasons.Describe(Miss);
            if (TimingErrorTicks != 0)
            {
                double sec = TimingErrorTicks / (double)Confluence.Core.Time.Ticks.PerSecond;
                text += " (" + (sec > 0 ? "+" : "") + sec.ToString("0.000", CultureInfo.InvariantCulture) + " s)";
            }

            return text;
        }

        public bool Equals(MasteryResult other) =>
            Reward == other.Reward && Magnitude.Equals(other.Magnitude) && WindupCapTicks == other.WindupCapTicks && Miss == other.Miss && TimingErrorTicks == other.TimingErrorTicks;

        public override bool Equals(object obj) => obj is MasteryResult other && Equals(other);

        public override int GetHashCode() => ((int)Reward * 31 + (int)Miss) * 31 + Magnitude.GetHashCode();

        public override string ToString() => Describe();
    }
}
