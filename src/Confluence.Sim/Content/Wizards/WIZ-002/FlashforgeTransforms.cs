using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Sim.Spells;

namespace Confluence.Sim.Content.Wizards.Wiz002
{
    /// <summary>
    /// AUG-319 Forge Plate (Silver, SPL-016, no exclusive group). Rank 1: "Replace hot-prop selection with one 25-health,
    /// 2 m metal plate lasting 4 s, 6/s contact." Rank 2: "Plate 35 health, 8/s contact, 4 s." Drawback: "Initial 58
    /// becomes 44; Heat 3 shared; plate never blocks routes." Counterplay: "Break/cool plate or leave it."
    /// <list type="bullet">
    /// <item>Instead of heating a prop, the smash forges a 2 m square hot plate on the smashed ground, aligned with the
    /// aim: 25 health (rank 2: 35), 4 s, 6 thermal per second (rank 2: 8) to anyone standing on it, owner included. It
    /// is one of the caster's fields; it has no collision, so it never blocks a route; it is a destructible body (spec
    /// L527) and water or frost under it cools it.</item>
    /// <item>The smash hits for 44. The plate shares the smash's Heat 3 (it adds no heat of its own).</item>
    /// </list>
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class ForgePlate : ISpellTransform
    {
        public const string Id = "AUG-319";

        public static readonly ProseParameter Rank1Health = ProseParameter.Of("rank1_plate_health", 25, "one 25-health");
        public static readonly ProseParameter Size = ProseParameter.Of("plate_size", 2, "2 m metal plate");
        public static readonly ProseParameter Lifetime = ProseParameter.Of("plate_lifetime", 4, "plate lasting 4 s");
        public static readonly ProseParameter Rank1Dps = ProseParameter.Of("rank1_plate_dps", 6, "6/s contact");
        public static readonly ProseParameter Rank2Health = ProseParameter.Of("rank2_plate_health", 35, "Plate 35 health");
        public static readonly ProseParameter Rank2Dps = ProseParameter.Of("rank2_plate_dps", 8, "8/s contact");
        public static readonly ProseParameter Initial = ProseParameter.Of("initial", 44, "Initial 58 becomes 44");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Rank1Health, Size, Lifetime, Rank1Dps, Rank2Health, Rank2Dps, Initial };

        public TransformStage Stage => TransformStage.Delivery;

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Apply(SpellFormBuilder form, int rank)
        {
            if (!(form.Delivery is SmashDelivery smash)) throw new InvalidOperationException(Id + " transforms Flashforge (SPL-016).");
            smash.PlateSize = Size.Float;
            smash.PlateHealth = rank >= 2 ? Rank2Health.Float : Rank1Health.Float;
            smash.HotLifetimeSec = Lifetime.Float;
            smash.HotContact = new PayloadSpec { Channel = form.Channel, Direct = rank >= 2 ? Rank2Dps.Float : Rank1Dps.Float, GuardEligible = false };
            form.Payload.Direct = Math.Min(form.Payload.Direct, Initial.Float); // with AUG-320 the stricter reduction wins (L510)
        }
    }

    /// <summary>
    /// AUG-320 Forge Vacuum (Gold, SPL-016, no exclusive group). Rank 1: "Burst pulls loose steam in ward 2 m to impact
    /// point once, no player pull." Rank 2: "Pull 3 m; steam lifetime unchanged." Drawback: "Cast time+0.2 s; initial 58
    /// becomes 52." Counterplay: "Stay beyond burst and exploit concentrated fog."
    /// <list type="bullet">
    /// <item>The smash pulls every loose (wind-movable) steam cloud touching its area widened by the pull distance toward
    /// the impact point, once, by up to 2 m (rank 2: 3 m) and never past it, at the wind rule's 3 m/s (spec L151). Players
    /// are not pulled and the steam keeps its lifetime.</item>
    /// <item>The windup is 0.85 s and the smash hits for 52.</item>
    /// </list>
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class ForgeVacuum : ISpellTransform
    {
        public const string Id = "AUG-320";

        public static readonly ProseParameter Rank1Pull = ProseParameter.Of("rank1_pull", 2, "in ward 2 m");
        public static readonly ProseParameter Rank2Pull = ProseParameter.Of("rank2_pull", 3, "Pull 3 m");
        public static readonly ProseParameter ExtraCastTime = ProseParameter.Of("extra_cast_time", 0.2, "Cast time+0.2 s");
        public static readonly ProseParameter Initial = ProseParameter.Of("initial", 52, "initial 58 becomes 52");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Rank1Pull, Rank2Pull, ExtraCastTime, Initial };

        public TransformStage Stage => TransformStage.Payload;

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Apply(SpellFormBuilder form, int rank)
        {
            if (!(form.Delivery is SmashDelivery smash)) throw new InvalidOperationException(Id + " transforms Flashforge (SPL-016).");
            smash.PullDistance = rank >= 2 ? Rank2Pull.Float : Rank1Pull.Float;
            form.CastTimeSec += ExtraCastTime.Float;
            form.Payload.Direct = Math.Min(form.Payload.Direct, Initial.Float); // with AUG-319 the stricter reduction wins (L510)
        }
    }
}
