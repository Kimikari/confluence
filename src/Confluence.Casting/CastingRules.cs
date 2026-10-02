using Confluence.Core.Rules;
using Confluence.Core.Time;

namespace Confluence.Casting
{
    /// <summary>
    /// Casting constants the spec leaves open, chosen once here (global caps stay in
    /// <see cref="GlobalRules.Casting"/> and <see cref="GlobalRules.Modifiers"/>). Each value cites the text it
    /// interprets; see docs/notes/casting.md for the reasoning.
    /// </summary>
    public static class CastingRules
    {
        /// <summary>
        /// Spec L92 names quick versus heavy casts without a threshold. A cast whose intrinsic windup is at most
        /// 0.35 s is quick (0.20 s recovery, 85% move speed); longer windups are heavy (0.35 s, 70%).
        /// </summary>
        public const float QuickCastMaxWindupSec = 0.35f;

        /// <summary>Spec L90 "0.15 second re-cast lock" in ticks.</summary>
        public static readonly int RecastLockTicks = Ticks.FromSeconds(GlobalRules.Casting.PreCommitCancelRecastLockSec);

        /// <summary>Spec L92 "Maximum normal channel duration is 3 seconds" in ticks.</summary>
        public static readonly int MaxChannelTicks = Ticks.FromSeconds(GlobalRules.Casting.MaxChannelDurationSec);

        /// <summary>Spec L96 minimum anticipatory cue in ticks.</summary>
        public static readonly int AnticipatoryCueTicks = Ticks.FromSeconds(GlobalRules.Casting.AnticipatoryCueMinSec);

        /// <summary>
        /// Common actions that end an open preparation (no cost) or voluntarily cancel a committed windup or channel
        /// (no refund, spec L90). Spec L474: no wizard overrides dodge, guard, or common melee, so pressing one always
        /// performs that action and the spell yields.
        /// </summary>
        public const InputButtons CancellingActions = InputButtons.Guard | InputButtons.Dodge | InputButtons.Melee;

        /// <summary>A new preparation cannot open while guard is held (guard has its own punish window, spec L104).</summary>
        public const InputButtons BlockingHeldActions = InputButtons.Guard;

        /// <summary>
        /// Spec L92 applies the casting move-speed factor "while casting". Grammars keep aim and movement live during
        /// preparation (spec L474, WIZ-005/-013/-029), so the factor applies from commitment through windup and
        /// channel; preparation and recovery move at full speed.
        /// </summary>
        public const float PreparationMoveSpeedFactor = 1f;

        /// <summary>Catalogue tag for the universal interrupt-refund rule (spec L90). No base record carries it yet.</summary>
        public const string InterruptRefundTag = "interrupt_refund";

        /// <summary>Spell tags that make a cast hard control for the anticipatory cue (spec L96, L116).</summary>
        public static readonly string[] HardControlTags = { "root", "silence", "stun" };

        /// <summary>Spell tags that make a cast a control spell for recovery (spec L92 "control spells 0.35").</summary>
        public static readonly string[] ControlTags = { "root", "silence", "stun", "slow", "pull" };

        /// <summary>How many recent committed casts keep a refund ledger for late refund effects (spec L90, L124).</summary>
        public const int RefundLedgerHistory = 8;
    }
}
