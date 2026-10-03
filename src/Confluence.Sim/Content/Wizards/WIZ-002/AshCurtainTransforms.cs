using System;
using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Sim.Spells;

namespace Confluence.Sim.Content.Wizards.Wiz002
{
    /// <summary>
    /// AUG-311 Ash Corridor (Silver, SPL-012, no exclusive group). Rank 1: "Replace cloud with 6 m-long, 2 m-wide opaque
    /// curtain lasting 5 s." Rank 2: "Curtain 8 m long, still 5 s." Drawback: "Cast range 16 becomes 12 m; no damage."
    /// Counterplay: "Move around narrow ends or wind-clear."
    /// <list type="bullet">
    /// <item>The ash takes a 6 m by 2 m rectangle (rank 2: 8 m) centered on the placement point with its long side across
    /// the aim, so a sight line crossing it passes two meters of ash and is blocked (spec L167) while its narrow ends can
    /// be walked around. Every other ash rule is unchanged (wind moves it, water collapses it, no damage).</item>
    /// <item>Placement range 12 m; the curtain lasts 5 s.</item>
    /// </list>
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class AshCorridor : ISpellTransform
    {
        public const string Id = "AUG-311";

        public static readonly ProseParameter Rank1Length = ProseParameter.Of("rank1_curtain_length", 6, "6 m-long");
        public static readonly ProseParameter Width = ProseParameter.Of("curtain_width", 2, "2 m-wide opaque curtain");
        public static readonly ProseParameter Lifetime = ProseParameter.Of("curtain_lifetime", 5, "curtain lasting 5 s");
        public static readonly ProseParameter Rank2Length = ProseParameter.Of("rank2_curtain_length", 8, "Curtain 8 m long");
        public static readonly ProseParameter Range = ProseParameter.Of("range", 12, "Cast range 16 becomes 12 m");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Rank1Length, Width, Lifetime, Rank2Length, Range };

        public TransformStage Stage => TransformStage.Delivery;

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Apply(SpellFormBuilder form, int rank)
        {
            if (!(form.Delivery is AshCloudDelivery ash)) throw new InvalidOperationException(Id + " transforms Ash Curtain (SPL-012).");
            ash.CurtainLength = rank >= 2 ? Rank2Length.Float : Rank1Length.Float;
            ash.CurtainWidth = Width.Float;
            ash.LifetimeSec = Lifetime.Float;
            form.Range = Range.Float;
        }
    }

    /// <summary>
    /// AUG-312 Ash Bellows (Gold, SPL-012, no exclusive group). Rank 1: "Recast pushes existing ash cloud 2 m in manually
    /// chosen direction." Rank 2: "Push 3 m; lifetime never extends." Drawback: "Recast costs 6 mana; cloud duration 6
    /// becomes 5 s." Counterplay: "Use cloud's original location or disperse it."
    /// <list type="bullet">
    /// <item>One recast while the cloud lives (spec L94) pays 6 mana (the record's stated further payment) and moves the
    /// whole cloud 2 m (rank 2: 3 m) along the caster's aim at the wind rule's 3 m/s (spec L151, a targeted local push
    /// that never disperses it). The cloud's expiry does not change.</item>
    /// <item>The cloud lasts 5 s.</item>
    /// </list>
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class AshBellows : ISpellTransform
    {
        public const string Id = "AUG-312";

        public static readonly ProseParameter Rank1Push = ProseParameter.Of("rank1_push", 2, "pushes existing ash cloud 2 m");
        public static readonly ProseParameter Rank2Push = ProseParameter.Of("rank2_push", 3, "Push 3 m");
        public static readonly ProseParameter RecastMana = ProseParameter.Of("recast_mana", 6, "Recast costs 6 mana");
        public static readonly ProseParameter Lifetime = ProseParameter.Of("cloud_lifetime", 5, "cloud duration 6 becomes 5 s");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Rank1Push, Rank2Push, RecastMana, Lifetime };

        public TransformStage Stage => TransformStage.Payload;

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Apply(SpellFormBuilder form, int rank)
        {
            if (!(form.Delivery is AshCloudDelivery ash)) throw new InvalidOperationException(Id + " transforms Ash Curtain (SPL-012).");
            ash.PushDistance = rank >= 2 ? Rank2Push.Float : Rank1Push.Float;
            ash.LifetimeSec = Math.Min(ash.LifetimeSec, Lifetime.Float);
            form.Recast = new RecastRule(extraMana: RecastMana.Float);
        }
    }
}
