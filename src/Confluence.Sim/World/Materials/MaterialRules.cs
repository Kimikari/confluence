using Confluence.Core.Rules;
using Confluence.Reactions.Events;
using Confluence.Sim.Combat;

namespace Confluence.Sim.World.Materials
{
    /// <summary>
    /// Numbers and mappings of the Sim-to-Reactions connection that the spec leaves open (each interprets the cited
    /// line; consolidated from docs/notes/sim-world.md into DECISIONS by the merge).
    /// </summary>
    public static class MaterialRules
    {
        /// <summary>Status definition id of the Wet a player gets from standing in liquid water (spec L118, L175).</summary>
        public const string WetCellSource = "RULE:WET-CELL";

        /// <summary>
        /// Wet lingers this long after the player leaves wet cells (decision: L118 gives Wet no duration of its own;
        /// one second keeps a wet route meaningful for a following electrical cast without making it permanent).
        /// </summary>
        public const float WetLingerSec = 1f;

        /// <summary>
        /// While a player stands in water their Wet is re-applied for linger plus this margin whenever no more than the
        /// linger remains, so it is refreshed four times a second rather than every tick and lasts 1 to 1.25 s after leaving.
        /// </summary>
        public const float WetRefreshMarginSec = 0.25f;

        /// <summary>Source id of reaction damage whose originating spell is unknown.</summary>
        public const string ReactionSourcePrefix = "REACTION:";

        /// <summary>Owned-entity definition id of a reaction cloud counted against its owner's field limit (spec L128, L670).</summary>
        public const string CloudFieldPrefix = "CLOUD:";

        /// <summary>Reaction bodies of summons use ids from here (players are 0 and 1).</summary>
        public const int SummonBodyBase = 1000;

        /// <summary>Summon reaction bodies are standing-player sized unless the summon states a radius (decision).</summary>
        public const float SummonBodyHeight = GlobalRules.Movement.StandingHeight;

        /// <summary>Reaction family of contact damage for feats and attribution (FEAT-020 conductivity).</summary>
        public static ReactionTag TagOf(ReactionKind kind)
        {
            switch (kind)
            {
                case ReactionKind.WetCharge:
                case ReactionKind.ConductorRelease:
                case ReactionKind.GroundRedirect:
                    return ReactionTag.Conductivity;
                case ReactionKind.ShardCone:
                case ReactionKind.IceShattered:
                    return ReactionTag.Shatter;
                case ReactionKind.BurnContact:
                case ReactionKind.Ignition:
                case ReactionKind.SolidIgnited:
                case ReactionKind.HotSurface:
                    return ReactionTag.Burn;
                case ReactionKind.Steam:
                    return ReactionTag.Steam;
                case ReactionKind.IceFormed:
                case ReactionKind.IceMelted:
                    return ReactionTag.Ice;
                case ReactionKind.PropImpulse:
                    return ReactionTag.Prop;
                default:
                    return ReactionTag.Other;
            }
        }

        /// <summary>
        /// The distinct world-reaction family a reaction event counts as for FEAT-006 ("Produce three distinct world
        /// reactions", spec L260), or null for bookkeeping events (decay, refill, glow, overload reset).
        /// </summary>
        public static string FeatReactionOf(ReactionKind kind)
        {
            switch (kind)
            {
                case ReactionKind.Steam: return "steam";
                case ReactionKind.IceFormed: return "freeze";
                case ReactionKind.IceMelted: return "melt";
                case ReactionKind.IceShattered: return "shatter";
                case ReactionKind.WetCharge: return "wet-charge";
                case ReactionKind.ConductorRelease: return "conduct";
                case ReactionKind.GroundRedirect: return "ground";
                case ReactionKind.WindTranslate:
                case ReactionKind.WindDisperse:
                case ReactionKind.VolumeCleared:
                    return "wind";
                case ReactionKind.Extinguish: return "extinguish";
                case ReactionKind.Ignition:
                case ReactionKind.SolidIgnited:
                    return "ignite";
                case ReactionKind.DampFormed: return "damp-sand";
                case ReactionKind.PropImpulse: return "prop-force";
                case ReactionKind.VenomDilution: return "dilute";
                case ReactionKind.Condense: return "condense";
                case ReactionKind.Dispel:
                case ReactionKind.DispelVolume:
                    return "dispel";
                default:
                    return null;
            }
        }

        /// <summary>Events in which a player removes a cloud or part of one (FEAT-011 "Clear two hostile or neutral clouds", L265).</summary>
        public static bool ClearsCloud(ReactionKind kind) =>
            kind == ReactionKind.WindDisperse || kind == ReactionKind.VolumeCleared || kind == ReactionKind.DispelVolume || kind == ReactionKind.Condense;
    }
}
