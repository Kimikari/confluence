using Confluence.Core.Arena;

namespace Confluence.Reactions.Environment
{
    /// <summary>
    /// BIO-011 Windmill Yard: "Every 12 s an announced 2 s gust moves clouds up to 2 m along the shown compass
    /// direction; force strength 1." The arena generator writes the announced schedule into the layout (spec L664);
    /// this rule lets those strength-1 gusts translate clouds, which the global wind rule reserves for strength above
    /// 1 (L151), bounded by the stated 2 m (DECISIONS D-023, D-038). A gust only moves volumes: "The gust does not move
    /// players or damaging props" (BIO-011 fairness), so it never queues a prop force. The Sim's BIO-011 behavior
    /// installs it in a duel.
    /// </summary>
    public static class WindmillYardGusts
    {
        public const string BiomeId = "BIO-011";

        /// <summary>"moves clouds up to 2 m".</summary>
        public const float GustMaxDistance = 2f;

        /// <summary>Installs the layout's announced gusts with the Windmill Yard cloud rule. Returns the gust count.</summary>
        public static int Install(ReactionWorld world, ArenaLayout layout) =>
            EnvironmentRules.InstallAnnouncedGusts(world, layout, true, GustMaxDistance);

        /// <summary>The layout's announced gusts (the Windmill Yard rule applies only to a BIO-011 layout).</summary>
        public static void InstallGusts(ReactionWorld world, ArenaLayout layout) => EnvironmentRules.InstallGusts(world, layout);
    }
}
