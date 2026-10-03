using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Sim.Spells;

namespace Confluence.Sim.Content.Wizards.HalenTide
{
    /// <summary>
    /// AUG-437 Bubble Release (Gold, SPL-075, no exclusive group). Rank 1 "Recast throws remaining shield as 20 m/s
    /// 24-damage bubble, ending guard." Rank 2 "Bubble 30 damage; range 20 m." Drawback "Original shield 45 becomes 35; no
    /// reflection." The spell-recast binding (spec L94, L512: no extra mana, same cast identity and mastery) ends the
    /// bubble shield and throws a physical projectile along the aim: a direct, guardable hit (spec L104) that solids stop
    /// and nothing reflects. The record states the 20 m range in rank 2; rank 1 states no other range, so the thrown bubble
    /// travels 20 m at both ranks. Counterplay "Bait release or dodge bubble": the pending recast is visible (spec L549) and
    /// the bubble is a dodgeable projectile.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class BubbleRelease : ISpellTransform
    {
        public const string Id = "AUG-437";

        public static readonly ProseParameter Speed = ProseParameter.Of("bubble_speed", 20, "20 m/s 24-damage bubble");
        public static readonly ProseParameter Damage = ProseParameter.Of("bubble_damage", 24, "20 m/s 24-damage bubble");
        public static readonly ProseParameter DamageRank2 = ProseParameter.Of("bubble_damage_rank2", 30, "Bubble 30 damage");
        public static readonly ProseParameter Range = ProseParameter.Of("bubble_range", 20, "range 20 m");
        public static readonly ProseParameter Shield = ProseParameter.Of("shield", 35, "Original shield 45 becomes 35");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Speed, Damage, DamageRank2, Range, Shield };

        static BubbleRelease() => HalenStructure.EnsureRegistered();

        public TransformStage Stage => TransformStage.Delivery;

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Apply(SpellFormBuilder form, int rank)
        {
            BubbleShieldDelivery bubble = HalenStructure.Delivery<BubbleShieldDelivery>(form, Id);
            bubble.Capacity = Shield.Float;
            bubble.ReleaseProjectile = new ProjectileDelivery { Speed = Speed.Float, MaxRange = Range.Float };
            bubble.ReleasePayload = new PayloadSpec
            {
                Channel = DamageChannel.Physical,
                Direct = rank >= 2 ? DamageRank2.Float : Damage.Float,
            };
            form.Recast = RecastRule.Default; // once, while the bubble lives, no further payment (spec L94)
        }
    }

    /// <summary>
    /// AUG-438 Bubble Steam (Prismatic, SPL-075, no exclusive group). Rank 1 "Thermal shield break consumes Water 1 and
    /// creates 1.5 m steam puff for 1 s." Rank 2 "Puff 2 m for 1.5 s." Drawback "Shield loses 15 instead of 10 per heat
    /// reaction." A reaction branch: a bubble broken by heat (a thermal hit or its own heat reaction emptying it) leaves no
    /// water; its Water 1 becomes a steam puff that blocks acquisition (spec L167), owned by the caster (a field, spec
    /// L128). Each heat reaction consumes 15 shield. Counterplay "Use sound or attack from side": steam never mutes sound and
    /// the bubble's arc never covers the flanks.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class BubbleSteam : ISpellTransform
    {
        public const string Id = "AUG-438";

        public static readonly ProseParameter Water = ProseParameter.Of("water_consumed", 1, "consumes Water 1");
        public static readonly ProseParameter Radius = ProseParameter.Of("puff_radius", 1.5, "creates 1.5 m steam puff for 1 s");
        public static readonly ProseParameter Lifetime = ProseParameter.Of("puff_lifetime", 1, "steam puff for 1 s");
        public static readonly ProseParameter RadiusRank2 = ProseParameter.Of("puff_radius_rank2", 2, "Puff 2 m for 1.5 s");
        public static readonly ProseParameter LifetimeRank2 = ProseParameter.Of("puff_lifetime_rank2", 1.5, "Puff 2 m for 1.5 s");
        public static readonly ProseParameter HeatLoss = ProseParameter.Of("heat_shield_loss", 15, "Shield loses 15 instead of 10 per heat reaction");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Water, Radius, Lifetime, RadiusRank2, LifetimeRank2, HeatLoss };

        static BubbleSteam() => HalenStructure.EnsureRegistered();

        public TransformStage Stage => TransformStage.Payload;

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Apply(SpellFormBuilder form, int rank)
        {
            BubbleShieldDelivery bubble = HalenStructure.Delivery<BubbleShieldDelivery>(form, Id);
            bubble.ThermalBreakPuffRadius = rank >= 2 ? RadiusRank2.Float : Radius.Float;
            bubble.ThermalBreakPuffLifetimeSec = rank >= 2 ? LifetimeRank2.Float : Lifetime.Float;
            bubble.HeatShieldLoss = HeatLoss.Float;
        }
    }
}
