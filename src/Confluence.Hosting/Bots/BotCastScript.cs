using System;
using Confluence.Core.Random;
using Confluence.Sim.Input;

namespace Confluence.Hosting.Bots
{
    /// <summary>
    /// A bot's input script for one cast of its wizard's grammar: the buttons to hold on each tick after the press.
    /// The script only produces inputs; the server's casting runtime judges them exactly as it judges a human's
    /// (spec L425), so a sloppy script simply casts normally or misses its reward. One script instance is one cast.
    /// </summary>
    public abstract class BotCastScript
    {
        /// <summary>Ticks from the press to the release (Primary is held on ticks 0 .. ReleaseTick-1).</summary>
        public int ReleaseTick { get; protected set; }

        /// <summary>The cast's buttons on hold tick <paramref name="k"/>; <see cref="InputButtons.None"/> from the release on.</summary>
        public virtual InputButtons ButtonsAt(int k) => k >= 0 && k < ReleaseTick ? InputButtons.Primary : InputButtons.None;

        public bool IsDone(int k) => k >= ReleaseTick;

        /// <summary>The script for a wizard's grammar (generic hold-and-release for wizards without a dedicated script).</summary>
        public static BotCastScript For(string wizardId, BotProfile profile, Rng rng)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            switch (wizardId)
            {
                case CrestReleaseScript.WizardId:
                    return new CrestReleaseScript(profile, rng);
                case PressureMeterScript.WizardId:
                    return new PressureMeterScript(profile, rng);
                default:
                    return new HoldReleaseScript(profile, rng);
            }
        }

        protected static int Jitter(BotProfile profile, Rng rng) =>
            profile.CastTimingJitterTicks <= 0 ? 0 : rng.NextInt(-profile.CastTimingJitterTicks, profile.CastTimingJitterTicks + 1);
    }

    /// <summary>Hold Primary about 0.3 s and release: the plain normal cast of most grammars.</summary>
    public sealed class HoldReleaseScript : BotCastScript
    {
        public const int NominalReleaseTick = 18; // 0.30 s

        public HoldReleaseScript(BotProfile profile, Rng rng)
        {
            ReleaseTick = Math.Max(1, NominalReleaseTick + Jitter(profile, rng));
        }
    }

    /// <summary>
    /// WIZ-002 Bram Cinder, crest release: hold Primary and let go. A plain cast holds about 0.3 s (a release from
    /// 0.20 s commits normally); a mastery attempt aims at the crest window 0.40-0.50 s (hold ticks 24-30) and lands
    /// in it only as often as the bot's timing jitter allows. Never holds to the 0.60 s auto-vent.
    /// </summary>
    public sealed class CrestReleaseScript : BotCastScript
    {
        public const string WizardId = "WIZ-002";
        public const int NormalReleaseTick = 18;  // ~0.3 s
        public const int CrestReleaseTick = 27;   // middle of the 24-30 crest window
        public const int LatestReleaseTick = 35;  // one tick before the 0.60 s auto-vent

        public CrestReleaseScript(BotProfile profile, Rng rng)
        {
            int target = profile.AttemptsMastery ? CrestReleaseTick : NormalReleaseTick;
            ReleaseTick = Clamp(target + Jitter(profile, rng), 1, LatestReleaseTick);
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }

    /// <summary>
    /// WIZ-010 Halen Tide, pressure metering. Pressure starts at 30 on the press and rises one unit per held tick;
    /// a vent (Modifier A) lowers it 50/60 per tick instead. A plain cast holds 24 ticks (pressure about 54, inside the
    /// safe 20-80 band) and releases. A mastery attempt holds to 50 (tick 20), vents for six ticks (the 0.1 s accepted
    /// interval) down to 45, compresses again and releases at tick 35 just below 55, so the final 15 ticks stay
    /// inside 45-55. Jitter shifts the vent and the release, so imperfect bots leave the band and cast normally.
    /// </summary>
    public sealed class PressureMeterScript : BotCastScript
    {
        public const string WizardId = "WIZ-010";
        public const int NormalReleaseTick = 24;
        public const int VentStartTick = 20;
        public const int VentTicks = 6;
        public const int MasteryReleaseTick = 35;
        public const int EarliestReleaseTick = 18; // 0.30 s window opening
        public const int LatestReleaseTick = 53;   // before the 0.90 s auto-vent

        private readonly int _ventStart;
        private readonly int _ventEnd;

        public PressureMeterScript(BotProfile profile, Rng rng)
        {
            if (profile.AttemptsMastery)
            {
                _ventStart = VentStartTick + Jitter(profile, rng);
                _ventEnd = _ventStart + VentTicks;
                ReleaseTick = Clamp(MasteryReleaseTick + Jitter(profile, rng), _ventEnd + 1, LatestReleaseTick);
            }
            else
            {
                _ventStart = -1;
                _ventEnd = -1;
                ReleaseTick = Clamp(NormalReleaseTick + Jitter(profile, rng), EarliestReleaseTick, LatestReleaseTick);
            }
        }

        public bool Vents => _ventStart >= 0;

        public override InputButtons ButtonsAt(int k)
        {
            InputButtons buttons = base.ButtonsAt(k);
            if (buttons != InputButtons.None && k >= _ventStart && k < _ventEnd) buttons |= InputButtons.ModifierA;
            return buttons;
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
