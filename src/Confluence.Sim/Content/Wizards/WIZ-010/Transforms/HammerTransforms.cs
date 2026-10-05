using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Sim.Spells;

namespace Confluence.Sim.Content.Wizards.HalenTide
{
    /// <summary>
    /// AUG-435 Hammer Wake (Silver, SPL-074, exclusive group SPL-074-delivery). Rank 1 "Hit leaves a 1 m puddle for 3 s,
    /// Water 1, no damage." Rank 2 "Puddle 1.5 m for 4 s." Drawback "Damage 48 becomes 40; neutral electricity risk." A
    /// hit leaves a puddle under the struck target instead of the one-cell spray; the puddle deals no damage itself, but it
    /// is neutral water that any electrical source charges for both players (spec L148, L529; counterplay "Avoid or electrify
    /// puddle").
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class HammerWake : ISpellTransform
    {
        public const string Id = "AUG-435";

        public static readonly ProseParameter Radius = ProseParameter.Of("puddle_radius", 1, "Hit leaves a 1 m puddle for 3 s");
        public static readonly ProseParameter Lifetime = ProseParameter.Of("puddle_lifetime", 3, "puddle for 3 s");
        public static readonly ProseParameter Water = ProseParameter.Of("puddle_water", 1, "Water 1, no damage");
        public static readonly ProseParameter RadiusRank2 = ProseParameter.Of("puddle_radius_rank2", 1.5, "Puddle 1.5 m for 4 s");
        public static readonly ProseParameter LifetimeRank2 = ProseParameter.Of("puddle_lifetime_rank2", 4, "Puddle 1.5 m for 4 s");
        public static readonly ProseParameter Direct = ProseParameter.Of("direct", 40, "Damage 48 becomes 40");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Radius, Lifetime, Water, RadiusRank2, LifetimeRank2, Direct };

        static HammerWake() => HalenStructure.EnsureRegistered();

        public TransformStage Stage => TransformStage.Payload;

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Apply(SpellFormBuilder form, int rank)
        {
            if (!(form.Script is WaterHammerScript script))
            {
                throw new System.InvalidOperationException(Id + " needs SPL-074's script.");
            }

            form.Payload.Direct = Direct.Float;
            script.PuddleRadius = rank >= 2 ? RadiusRank2.Float : Radius.Float;
            script.PuddleLifetimeSec = rank >= 2 ? LifetimeRank2.Float : Lifetime.Float;
            script.PuddleStrength = (int)Water.Value;
        }
    }

    /// <summary>
    /// AUG-436 Hammer Fan (Prismatic, SPL-074, exclusive group SPL-074-delivery). Rank 1 "Thrust becomes a 75-degree
    /// 40-damage cone with no push." Rank 2 "Cone 90 degrees and 44 damage." Drawback "Mana +4; windup +0.1 s." A delivery
    /// branch: the framework cone (apex at the feet, per-target cover, D-142) over the 3 m reach, no push; it keeps the
    /// record's miss recovery and impact-spot spray. Counterplay "Flank cone or back out": nothing outside the angle or the
    /// reach is struck. Structural delta: adds cone, removes push (D-012).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class HammerFan : ISpellTransform
    {
        public const string Id = "AUG-436";

        public static readonly ProseParameter Angle = ProseParameter.HalfAngle("cone_half_angle", 75, "a 75-degree 40-damage cone");
        public static readonly ProseParameter Damage = ProseParameter.Of("direct", 40, "40-damage cone with no push");
        public static readonly ProseParameter AngleRank2 = ProseParameter.HalfAngle("cone_half_angle_rank2", 90, "Cone 90 degrees and 44 damage");
        public static readonly ProseParameter DamageRank2 = ProseParameter.Of("direct_rank2", 44, "Cone 90 degrees and 44 damage");
        public static readonly ProseParameter ManaIncrease = ProseParameter.Of("mana_increase", 4, "Mana +4");
        public static readonly ProseParameter WindupIncrease = ProseParameter.Of("windup_increase", 0.1, "windup +0.1 s");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Angle, Damage, AngleRank2, DamageRank2, ManaIncrease, WindupIncrease };

        static HammerFan() => HalenStructure.EnsureRegistered();

        public TransformStage Stage => TransformStage.Delivery;

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Apply(SpellFormBuilder form, int rank)
        {
            form.Delivery = new ConeDelivery
            {
                HalfAngleDeg = rank >= 2 ? AngleRank2.Float : Angle.Float,
                Reach = form.Range,
                CoverPerTarget = true,
            };
            form.Payload.Direct = rank >= 2 ? DamageRank2.Float : Damage.Float;
            form.Payload.Push = 0f; // "with no push"
            form.Mana += ManaIncrease.Float;
            form.CastTimeSec += WindupIncrease.Float;
        }
    }
}
