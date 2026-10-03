using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Sim.Spells;

namespace Confluence.Sim.Content.Wizards.HalenTide
{
    /// <summary>
    /// AUG-445 Prison Roll (Gold, SPL-079, no exclusive group). Rank 1 "Bubble rolls along ground at 16 m/s instead of
    /// flying; root 0.6 s unchanged." Rank 2 "Roll speed 20 m/s, root 0.6 s." Drawback "Jump avoids bubble; damage 26 becomes
    /// 22." A trajectory branch: the bubble starts at the caster's feet and rides the ground along the aim (climbing at most
    /// the step height); a jumping body's feet clear its top. The root, its immunity rule and the cue are unchanged.
    /// Counterplay "Jump or block rolling route": a jump or any obstacle in its path stops it reaching the target.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class PrisonRoll : ISpellTransform
    {
        public const string Id = "AUG-445";

        public static readonly ProseParameter Speed = ProseParameter.Of("roll_speed", 16, "rolls along ground at 16 m/s");
        public static readonly ProseParameter Root = ProseParameter.Of("root", 0.6, "root 0.6 s unchanged");
        public static readonly ProseParameter SpeedRank2 = ProseParameter.Of("roll_speed_rank2", 20, "Roll speed 20 m/s");
        public static readonly ProseParameter Direct = ProseParameter.Of("direct", 22, "damage 26 becomes 22");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Speed, Root, SpeedRank2, Direct };

        static PrisonRoll() => HalenStructure.EnsureRegistered();

        public TransformStage Stage => TransformStage.Delivery;

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Apply(SpellFormBuilder form, int rank)
        {
            TideProjectileDelivery bubble = HalenStructure.Delivery<TideProjectileDelivery>(form, Id);
            bubble.Rolling = true;
            bubble.Speed = rank >= 2 ? SpeedRank2.Float : Speed.Float;
            form.Payload.Direct = Direct.Float;
        }
    }

    /// <summary>
    /// AUG-446 Prison Pool (Prismatic, SPL-079, no exclusive group). Rank 1 "After root ends, impact creates 1 m puddle for 2
    /// s, Water 1." Rank 2 "Puddle radius 1.5 m for 3 s." Drawback "Post-root slow removed; no control-chain bypass." When
    /// the root ends (or at once when immunity refused it) a Water 1 puddle forms at the impact point instead of the slow.
    /// The puddle applies no control of its own, so the 2 s hard-control immunity after the root is untouched (spec L116).
    /// Counterplay "Move away after immunity begins".
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class PrisonPool : ISpellTransform
    {
        public const string Id = "AUG-446";

        public static readonly ProseParameter Radius = ProseParameter.Of("puddle_radius", 1, "impact creates 1 m puddle for 2 s");
        public static readonly ProseParameter Lifetime = ProseParameter.Of("puddle_lifetime", 2, "puddle for 2 s, Water 1");
        public static readonly ProseParameter Water = ProseParameter.Of("puddle_water", 1, "puddle for 2 s, Water 1");
        public static readonly ProseParameter RadiusRank2 = ProseParameter.Of("puddle_radius_rank2", 1.5, "Puddle radius 1.5 m for 3 s");
        public static readonly ProseParameter LifetimeRank2 = ProseParameter.Of("puddle_lifetime_rank2", 3, "Puddle radius 1.5 m for 3 s");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Radius, Lifetime, Water, RadiusRank2, LifetimeRank2 };

        static PrisonPool() => HalenStructure.EnsureRegistered();

        public TransformStage Stage => TransformStage.Payload;

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Apply(SpellFormBuilder form, int rank)
        {
            if (!(form.Script is DeepglassPrisonScript script)) throw new InvalidOperationException(Id + " needs SPL-079's script.");
            script.SlowFraction = 0f; // "Post-root slow removed"
            script.PuddleRadius = rank >= 2 ? RadiusRank2.Float : Radius.Float;
            script.PuddleLifetimeSec = rank >= 2 ? LifetimeRank2.Float : Lifetime.Float;
            script.PuddleWater = (int)Water.Value;
        }
    }
}
