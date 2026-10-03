using Confluence.Core.Time;
using Confluence.Sim.Duel;
using Confluence.Sim.Status;

namespace Confluence.Client.Laboratory
{
    /// <summary>
    /// Laboratory sandbox control (spec L69: a sandbox with editable rules): puts a set of real Sim effects on a player so
    /// the HUD's status icons and shield can be practised before any spell applies them (spec L347). Everything goes
    /// through the duel's own status and shield rules, so caps, control immunity, and damage over time behave as in a
    /// match; the source is no player (-1), so the burn is rule damage and shows no damage number.
    /// </summary>
    public static class TrainingEffects
    {
        public const string SourceId = "LAB:TRAINING";

        public static readonly int WetTicks = Ticks.FromSeconds(6f);
        public static readonly int BurnTicks = Ticks.FromSeconds(4f);
        public const float BurnPerSecond = 4f;
        public static readonly int SlowTicks = Ticks.FromSeconds(3f);
        public const float SlowFraction = 0.25f;

        /// <summary>Short enough that the control immunity which follows (spec L116) is what the player mostly sees.</summary>
        public static readonly int StunTicks = Ticks.FromSeconds(0.4f);

        public const float Shield = 40f;
        public static readonly int ShieldTicks = Ticks.FromSeconds(6f);

        public const string Description = "wet 6 s, burn 4 s (4/s), slow 3 s (25%), a 0.4 s stun, a 40 shield for 6 s";

        /// <summary>True while the effects can be applied (statuses only apply during combat, spec L46).</summary>
        public static bool Available(DuelWorld world) => world != null && world.Phase == DuelPhase.Combat;

        /// <summary>Applies the effects to one player; returns how many were accepted by the rules.</summary>
        public static int Apply(DuelWorld world, int player)
        {
            if (!Available(world))
            {
                return 0;
            }

            int accepted = 0;
            accepted += Accepted(world.ApplyStatus(player, StatusApplication.Of(StatusKind.Wet, SourceId, -1, WetTicks)));
            accepted += Accepted(world.ApplyStatus(player, StatusApplication.Of(StatusKind.Burn, SourceId, -1, BurnTicks, BurnPerSecond)));
            accepted += Accepted(world.ApplyStatus(player, StatusApplication.Of(StatusKind.Slow, SourceId, -1, SlowTicks, SlowFraction)));
            accepted += Accepted(world.ApplyStatus(player, StatusApplication.Of(StatusKind.Stun, SourceId, -1, StunTicks)));
            accepted += world.GrantShield(player, SourceId, Shield, ShieldTicks) > 0f ? 1 : 0;
            return accepted;
        }

        private static int Accepted(StatusApplyResult result) => result.Succeeded ? 1 : 0;
    }
}
