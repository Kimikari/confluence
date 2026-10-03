using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Sim.Spells;

namespace Confluence.Sim.Content.Wizards.HalenTide
{
    /// <summary>
    /// AUG-447 Spring Fan (Silver, SPL-080, no exclusive group). Rank 1 "Utility becomes a 6 m cone removing one
    /// hot/corrosive unit per cell." Rank 2 "Cone 8 m, one unit per cell." Drawback "Original focused removal 2 becomes 1; mana
    /// +3." A delivery branch: a ground cone from the caster's feet along the aim (no surface impact needed) takes one heat or
    /// venom unit from every covered cell; the record gives no angle, so the framework cone's 90-degree default applies.
    /// Counterplay "Spread hazard or attack during cone windup".
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class SpringFan : ISpellTransform
    {
        public const string Id = "AUG-447";

        public static readonly ProseParameter Reach = ProseParameter.Of("cone_reach", 6, "Utility becomes a 6 m cone");
        public static readonly ProseParameter ReachRank2 = ProseParameter.Of("cone_reach_rank2", 8, "Cone 8 m, one unit per cell");
        public static readonly ProseParameter PerCell = ProseParameter.Of("units_per_cell", 1, "Original focused removal 2 becomes 1");
        public static readonly ProseParameter ManaIncrease = ProseParameter.Of("mana_increase", 3, "mana +3");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Reach, ReachRank2, PerCell, ManaIncrease };

        static SpringFan() => HalenStructure.EnsureRegistered();

        public TransformStage Stage => TransformStage.Delivery;

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Apply(SpellFormBuilder form, int rank)
        {
            form.Delivery = new SpringConeDelivery
            {
                Reach = rank >= 2 ? ReachRank2.Float : Reach.Float,
                PerCell = (int)PerCell.Value,
            };
            form.Mana += ManaIncrease.Float;
        }
    }

    /// <summary>
    /// AUG-448 Spring Steam (Gold, SPL-080, no exclusive group). Rank 1 "Neutralizing Heat leaves 1.5 m steam for 2 s; Water 2
    /// still consumed." Rank 2 "Steam 2 m for 3 s." Drawback "No effect on corrosion unless actual neutralization." Where the
    /// cast neutralizes heat a sight-blocking steam cloud forms (spec L167), owned by the caster (a field, spec L128); no water
    /// survives. The water now goes to the heat reaction: a cast that finds no heat to neutralize leaves corrosive (venom)
    /// cells untouched. Counterplay "Wind-clear or shoot through": wind disperses steam (spec L151) and steam never stops
    /// projectiles (spec L167).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class SpringSteam : ISpellTransform
    {
        public const string Id = "AUG-448";

        public static readonly ProseParameter Radius = ProseParameter.Of("steam_radius", 1.5, "Neutralizing Heat leaves 1.5 m steam for 2 s");
        public static readonly ProseParameter Lifetime = ProseParameter.Of("steam_lifetime", 2, "steam for 2 s");
        public static readonly ProseParameter Water = ProseParameter.Of("water_consumed", 2, "Water 2 still consumed");
        public static readonly ProseParameter RadiusRank2 = ProseParameter.Of("steam_radius_rank2", 2, "Steam 2 m for 3 s");
        public static readonly ProseParameter LifetimeRank2 = ProseParameter.Of("steam_lifetime_rank2", 3, "Steam 2 m for 3 s");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Radius, Lifetime, Water, RadiusRank2, LifetimeRank2 };

        static SpringSteam() => HalenStructure.EnsureRegistered();

        public TransformStage Stage => TransformStage.Payload;

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Apply(SpellFormBuilder form, int rank)
        {
            if (!(form.Delivery is ISpringNeutralizer spring)) throw new InvalidOperationException(Id + " needs SPL-080's neutralizing delivery.");
            spring.SteamRadius = rank >= 2 ? RadiusRank2.Float : Radius.Float;
            spring.SteamLifetimeSec = rank >= 2 ? LifetimeRank2.Float : Lifetime.Float;
            spring.CorrosionNeedsHeat = true;
        }
    }
}
