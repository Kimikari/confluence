using System;
using Confluence.Core.Time;

namespace Confluence.Casting.Grammars
{
    /// <summary>
    /// WIZ-010 Halen Tide, "pressure metering" (catalogue WIZ-010; spec L468): holding primary compresses a virtual
    /// breath gauge, Modifier A vents it, and releasing primary between 0.30 and 0.90 s casts. It is a game meter with
    /// no microphone or breath requirement.
    /// <list type="bullet">
    /// <item>Gauge: starts at 30 when primary is pressed; rises 60 per second while held; falls 50 per second while the
    /// vent is engaged (the vent valve overrides compression). Pure holding enters the 45-55 band at 0.25 s and
    /// leaves it at 0.42 s, so mastery needs at least one deliberate vent.</item>
    /// <item>Vent input obeys the 0.1 s accepted input interval: its effective state changes at most once per
    /// interval, so faster toggling adds nothing (spec L478, <see cref="RateLimitedToggle"/>).</item>
    /// <item>Any release with the gauge in the safe 20-80 band casts normally. A release before 0.30 s queues the
    /// normal cast at 0.30 s (WIZ-001's roster rule for early releases).</item>
    /// <item>Leaving the safe band vents automatically; 0.30 s later the normal cast commits. Mana is spent only on
    /// that cast. Still holding at 0.90 s auto-vents a normal cast at once (Bram's overhold rule).</item>
    /// <item>Mastery: pressure inside 45-55 on every tick of the final 0.25 s before release: 15% less mana.</item>
    /// </list>
    /// Pressure is integer "pressure x 60", so one tick at R per second changes it by exactly R.
    /// </summary>
    public sealed class PressureMeterGrammar : GrammarBase
    {
        public const string Id = "WIZ-010";

        public const double ReleaseOpenSec = 0.30;      // WIZ-010 "Release primary between 0.30 and 0.90 s"
        public const double ReleaseCloseSec = 0.90;
        public const float SafeLow = 20f;               // WIZ-010 "safe 20-80 pressure band"
        public const float SafeHigh = 80f;
        public const float MasteryLow = 45f;            // WIZ-010 mastery "within 45-55"
        public const float MasteryHigh = 55f;
        public const double MasteryHoldSec = 0.25;      // WIZ-010 "for a continuous 0.25 s before release"
        public const double AcceptedIntervalSec = 0.1;  // WIZ-010 "the 0.1 s accepted input interval"
        public const double BandExitVentSec = 0.30;     // WIZ-010 "Leaving the band automatically vents and offers a normal cast after 0.30 s"
        public const float RewardFraction = 0.15f;      // WIZ-010 "pay 15% less mana"

        // Tuning chosen for this grammar (docs/notes/casting.md): demanding but reachable inside the 0.90 s window.
        public const float StartPressure = 30f;
        public const float CompressPerSecond = 60f;
        public const float VentPerSecond = 50f;
        public const float MaxPressure = 100f;

        public static readonly TickWindow ReleaseWindow = TickWindow.Between(ReleaseOpenSec, ReleaseCloseSec); // [18, 54]
        public static readonly int MasteryHoldTicks = Ticks.FromSeconds(MasteryHoldSec);                    // 15
        public static readonly int AcceptedIntervalTicks = Ticks.FromSeconds(AcceptedIntervalSec);          // 6
        public static readonly int BandExitVentTicks = Ticks.FromSeconds(BandExitVentSec);                  // 18

        private const int Scale = Ticks.PerSecond;
        private static readonly int SafeLowUnits = Units(SafeLow);
        private static readonly int SafeHighUnits = Units(SafeHigh);
        private static readonly int MasteryLowUnits = Units(MasteryLow);
        private static readonly int MasteryHighUnits = Units(MasteryHigh);
        private static readonly int CompressPerTick = (int)CompressPerSecond;
        private static readonly int VentPerTick = (int)VentPerSecond;
        private static readonly int MaxUnits = Units(MaxPressure);

        private readonly RateLimitedToggle _vent = new RateLimitedToggle(AcceptedIntervalTicks);

        private int _pressure;
        private int _bandStart;
        private int _lastThrottledAt;
        private int _throttleCount;
        private bool _venting;
        private int _ventEnd;
        private int _ventFrom;
        private bool _queued;
        private MasteryResult _queuedResult;

        public PressureMeterGrammar()
            : base(Id, GrammarFamily.HeldGauge, InputRoute.Standard)
        {
        }

        /// <summary>Current gauge pressure 0-100.</summary>
        public float Pressure => _pressure / (float)Scale;

        public override bool WantsToBegin(in CastInputFrame frame, CastableSpell spell) => frame.WasPressed(InputButtons.Primary);

        public override void Begin(in CastInputFrame frame, in GrammarContext context)
        {
            _pressure = Units(StartPressure);
            _bandStart = -1;
            _lastThrottledAt = int.MinValue / 2;
            _throttleCount = 0;
            _venting = false;
            _queued = false;
            _vent.Reset(false);
            UiState = new GrammarUiState
            {
                Active = true,
                HasSafeBand = true,
                SafeBandLow = SafeLow,
                SafeBandHigh = SafeHigh,
                HasMasteryBand = true,
                MasteryBandLow = MasteryLow,
                MasteryBandHigh = MasteryHigh,
                HasReleaseWindow = true,
                ReleaseWindow = ReleaseWindow,
                RequiredTicks = MasteryHoldTicks,
            };
        }

        public override GrammarStep Advance(in CastInputFrame frame, in GrammarContext context)
        {
            int elapsed = context.Elapsed;
            UiState.ElapsedTicks = elapsed;

            if (_venting)
            {
                // Band exit: the gauge vents for 0.30 s and then the normal cast commits on its own.
                UiState.VentRemainingTicks = Math.Max(0, _ventEnd - elapsed);
                SetGauge(_ventFrom + (Units(StartPressure) - _ventFrom) * (BandExitVentTicks - UiState.VentRemainingTicks) / BandExitVentTicks);
                return elapsed >= _ventEnd ? GrammarStep.AutoCommit(MasteryResult.Normal(MissReason.LeftSafeBand)) : GrammarStep.Continue;
            }

            if (_queued)
            {
                return elapsed >= ReleaseWindow.Open ? GrammarStep.Commit(_queuedResult) : GrammarStep.Continue;
            }

            SampleVent(frame, elapsed);

            if (!frame.IsHeld(InputButtons.Primary))
            {
                MasteryResult result = Evaluate(elapsed);
                if (elapsed < ReleaseWindow.Open)
                {
                    _queued = true;
                    _queuedResult = MasteryResult.Normal(MissReason.EarlyRelease, elapsed - ReleaseWindow.Open);
                    UiState.Queued = true;
                    UiState.QueuedCommitTick = ReleaseWindow.Open;
                    return GrammarStep.Continue;
                }

                return GrammarStep.Commit(result);
            }

            if (elapsed > 0)
            {
                _pressure += _vent.Effective ? -VentPerTick : CompressPerTick;
                if (_pressure < 0) _pressure = 0;
                if (_pressure > MaxUnits) _pressure = MaxUnits;
            }

            if (_pressure < SafeLowUnits || _pressure > SafeHighUnits)
            {
                _venting = true;
                _ventFrom = _pressure;
                _ventEnd = elapsed + BandExitVentTicks;
                UiState.Venting = true;
                UiState.VentRemainingTicks = BandExitVentTicks;
                UiState.ReleaseNow = MissReason.LeftSafeBand;
                SetGauge(_pressure);
                return GrammarStep.Continue;
            }

            bool inBand = _pressure >= MasteryLowUnits && _pressure <= MasteryHighUnits;
            if (!inBand) _bandStart = -1;
            else if (_bandStart < 0) _bandStart = elapsed;

            SetGauge(_pressure);
            UiState.ProgressTicks = _bandStart < 0 ? 0 : elapsed - _bandStart;
            UiState.ModifierEngaged = _vent.Effective;
            UiState.ThrottledInputs = _throttleCount;
            UiState.ReleaseNow = elapsed < ReleaseWindow.Open ? MissReason.EarlyRelease : Evaluate(elapsed).Miss;

            if (elapsed >= ReleaseWindow.Close)
            {
                return GrammarStep.AutoCommit(MasteryResult.Normal(MissReason.HeldPastWindow, elapsed - ReleaseWindow.Close));
            }

            return GrammarStep.Continue;
        }

        /// <summary>
        /// The result of releasing at <paramref name="elapsed"/> given the samples so far: mastered after 15
        /// continuous in-band ticks; otherwise the exact miss, preferring "too fast toggling" when inputs were
        /// throttled inside the qualifying 0.25 s.
        /// </summary>
        private MasteryResult Evaluate(int elapsed)
        {
            int run = _bandStart < 0 ? 0 : elapsed - _bandStart;
            if (_bandStart >= 0 && run >= MasteryHoldTicks) return MasteryResult.Mastered(MasteryReward.ManaEfficiency, RewardFraction);
            if (elapsed - _lastThrottledAt <= MasteryHoldTicks) return MasteryResult.Normal(MissReason.TooFastToggling);
            if (_bandStart < 0) return MasteryResult.Normal(MissReason.OutsideMasteryBand);
            return MasteryResult.Normal(MissReason.BandHeldTooShort, run - MasteryHoldTicks);
        }

        private void SampleVent(in CastInputFrame frame, int elapsed)
        {
            int before = _vent.ThrottledChanges;
            _vent.Sample(frame.Tick, frame.IsHeld(InputButtons.ModifierA), Edges(frame, InputButtons.ModifierA));
            if (_vent.ThrottledChanges > before)
            {
                _throttleCount = _vent.ThrottledChanges;
                _lastThrottledAt = elapsed;
            }
        }

        private void SetGauge(int units)
        {
            UiState.GaugeValue = units / (float)Scale;
            UiState.Gauge = UiState.GaugeValue / MaxPressure;
        }

        private static int Units(float pressure) => (int)Math.Round(pressure * Scale);
    }
}
