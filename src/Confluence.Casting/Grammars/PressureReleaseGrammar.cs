using System;
using Confluence.Core.Time;

namespace Confluence.Casting.Grammars
{
    /// <summary>
    /// WIZ-002 Bram Cinder, "crest release" (catalogue WIZ-002; spec L468): hold primary for 0.20-0.60 s while a
    /// single visible forge gauge rises to its crest at 0.45 s and falls again; release commits.
    /// <list type="bullet">
    /// <item>Any release from 0.20 s casts normally ("Any release after 0.20 s casts normally").</item>
    /// <item>A release before 0.20 s queues a normal cast that commits at 0.20 s, cancellable at no cost while queued
    /// (the roster rule written for WIZ-001: "Releasing earlier queues a normal cast at 0.20 s").</item>
    /// <item>Still holding at 0.60 s auto-vents a normal cast; no misfire or self-damage ("Overholding at 0.60 s auto-vents").</item>
    /// <item>Mastery: release inside the marked 100 ms crest window, the closed interval [0.40 s, 0.50 s] of hold time
    /// (ticks 24-30): +12% direct damage, or 12% less mana on a nondamaging spell. One reward per cast.</item>
    /// </list>
    /// Hold time is measured in ticks from the press tick to the release tick.
    /// </summary>
    public sealed class PressureReleaseGrammar : GrammarBase
    {
        public const string Id = "WIZ-002";

        public const double MinimumHoldSec = 0.20;   // WIZ-002 input sequence "Hold primary for 0.20-0.60 s"
        public const double AutoVentSec = 0.60;      // WIZ-002 normal cast "Overholding at 0.60 s auto-vents"
        public const double CrestSec = 0.45;         // WIZ-002 mastery "crest window at 0.45 s"
        public const double CrestWindowSec = 0.100;  // WIZ-002 mastery "marked 100 ms crest window"
        public const float RewardFraction = 0.12f;   // WIZ-002 mastery "direct damage gains 12% ... pay 12% less mana"

        public static readonly int MinimumHoldTicks = Ticks.FromSeconds(MinimumHoldSec);   // 12
        public static readonly int AutoVentTicks = Ticks.FromSeconds(AutoVentSec);         // 36
        public static readonly int CrestTick = Ticks.FromSeconds(CrestSec);                 // 27
        public static readonly TickWindow CrestWindow = TickWindow.Centered(CrestSec, CrestWindowSec); // [24, 30]

        private bool _queued;
        private int _releasedAt;

        public PressureReleaseGrammar()
            : base(Id, GrammarFamily.ButtonTiming, InputRoute.Standard)
        {
        }

        public override bool WantsToBegin(in CastInputFrame frame, CastableSpell spell) => frame.WasPressed(InputButtons.Primary);

        public override void Begin(in CastInputFrame frame, in GrammarContext context)
        {
            _queued = false;
            _releasedAt = -1;
            UiState = new GrammarUiState
            {
                Active = true,
                HasReleaseWindow = true,
                ReleaseWindow = new TickWindow(MinimumHoldTicks, AutoVentTicks),
                HasMasteryWindow = true,
                MasteryWindow = CrestWindow,
                ReleaseNow = MissReason.EarlyRelease,
            };
        }

        public override GrammarStep Advance(in CastInputFrame frame, in GrammarContext context)
        {
            int held = context.Elapsed;
            UpdateGauge(held);

            if (_queued)
            {
                // The early release already chose a normal cast; it commits at the 0.20 s mark.
                return held >= MinimumHoldTicks ? GrammarStep.Commit(ResultFor(_releasedAt, context.Spell)) : GrammarStep.Continue;
            }

            if (!frame.IsHeld(InputButtons.Primary))
            {
                _releasedAt = held;
                if (held < MinimumHoldTicks)
                {
                    _queued = true;
                    UiState.Queued = true;
                    UiState.QueuedCommitTick = MinimumHoldTicks;
                    return GrammarStep.Continue;
                }

                return GrammarStep.Commit(ResultFor(held, context.Spell));
            }

            if (held >= AutoVentTicks)
            {
                return GrammarStep.AutoCommit(MasteryResult.Normal(MissReason.AutoVented, CrestWindow.Offset(held)));
            }

            UiState.ReleaseNow = ResultFor(held, context.Spell).Miss;
            return GrammarStep.Continue;
        }

        /// <summary>The mastery result for a release after <paramref name="heldTicks"/> of hold.</summary>
        public static MasteryResult ResultFor(int heldTicks, CastableSpell spell)
        {
            if (CrestWindow.Contains(heldTicks))
            {
                return spell.IsDamaging
                    ? MasteryResult.Mastered(MasteryReward.DirectDamage, RewardFraction)
                    : MasteryResult.Mastered(MasteryReward.ManaEfficiency, RewardFraction);
            }

            return MasteryResult.Normal(heldTicks < CrestWindow.Open ? MissReason.EarlyRelease : MissReason.LateRelease, CrestWindow.Offset(heldTicks));
        }

        /// <summary>Gauge height 0-1: rises linearly to the crest at 0.45 s and falls at the same rate (2/3 full at 0.60 s).</summary>
        public static float GaugeAt(int heldTicks) => Math.Max(0f, 1f - Math.Abs(heldTicks - CrestTick) / (float)CrestTick);

        private void UpdateGauge(int held)
        {
            UiState.ElapsedTicks = held;
            UiState.Gauge = GaugeAt(_queued ? _releasedAt : held);
            UiState.GaugeValue = UiState.Gauge * 100f;
        }
    }
}
