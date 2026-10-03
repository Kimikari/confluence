using System;
using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Sim.Spells;

namespace Confluence.Sim.Content.Wizards.Wiz002
{
    /// <summary>
    /// AUG-317 Lash Knot (Gold, SPL-015, no exclusive group). Rank 1: "Hit surface leaves 20-health knot for 3 s; recast
    /// swings lash once from knot along manual arc, 28 damage." Rank 2: "Knot 4 s, swing 32 damage." Drawback: "First
    /// lash 42 becomes 30; Burn removed." Counterplay: "Break knot or avoid its marked arc."
    /// <list type="bullet">
    /// <item>Where the lash meets a surface (the wall that stops it, else the ground under its tip) it leaves a visible
    /// 20-health knot for 3 s (rank 2: 4 s), marked with the arc it would swing.</item>
    /// <item>One spell recast while the knot stands (spec L94) swings the lash from the knot along the caster's current
    /// aim, the same 0.5 m-wide line and reach, for 28 thermal (rank 2: 32), a guardable direct hit. Breaking the knot
    /// first cancels it (spec L512).</item>
    /// <item>The first lash hits for 30 and applies no Burn (structural delta: the burn tag is removed).</item>
    /// </list>
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class LashKnot : ISpellTransform
    {
        public const string Id = "AUG-317";

        public static readonly ProseParameter Health = ProseParameter.Of("knot_health", 20, "leaves 20-health knot");
        public static readonly ProseParameter Rank1Lifetime = ProseParameter.Of("rank1_knot_lifetime", 3, "20-health knot for 3 s");
        public static readonly ProseParameter Rank1Swing = ProseParameter.Of("rank1_swing", 28, "along manual arc, 28 damage");
        public static readonly ProseParameter Rank2Lifetime = ProseParameter.Of("rank2_knot_lifetime", 4, "Knot 4 s");
        public static readonly ProseParameter Rank2Swing = ProseParameter.Of("rank2_swing", 32, "swing 32 damage");
        public static readonly ProseParameter FirstLash = ProseParameter.Of("first_lash", 30, "First lash 42 becomes 30");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Health, Rank1Lifetime, Rank1Swing, Rank2Lifetime, Rank2Swing, FirstLash };

        static LashKnot() => Wiz002StructuralDeltas.EnsureRegistered();

        public LashKnot() => Wiz002StructuralDeltas.EnsureRegistered();

        public TransformStage Stage => TransformStage.Delivery;

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Apply(SpellFormBuilder form, int rank)
        {
            if (!(form.Delivery is LashDelivery lash)) throw new InvalidOperationException(Id + " transforms Flame Lash (SPL-015).");
            lash.KnotLifetimeSec = rank >= 2 ? Rank2Lifetime.Float : Rank1Lifetime.Float;
            lash.KnotHealth = Health.Float;
            lash.KnotSwing = new PayloadSpec { Channel = form.Channel, Direct = rank >= 2 ? Rank2Swing.Float : Rank1Swing.Float };
            form.Payload.Direct = FirstLash.Float;
            form.Payload.ClearStatuses(); // "Burn removed"
            form.Recast = RecastRule.Default;
        }
    }

    /// <summary>
    /// AUG-318 Lash Steam (Prismatic, SPL-015, no exclusive group). Rank 1: "Lash crossing connected Water 1 consumes it
    /// to create 1 m steam strip for 2 s." Rank 2: "Strip width 1.5 m and 3 s." Drawback: "Reach 6 becomes 5 m; no extra
    /// damage or strength." Counterplay: "Move outside strip or wind-clear."
    /// <list type="bullet">
    /// <item>Where the lash's ground track crosses liquid water, it consumes one water unit per crossed cell and raises a
    /// 1 m-wide steam strip along that stretch for 2 s (rank 2: 1.5 m, 3 s) — a steam volume of the caster (spec L167,
    /// L670) that wind can clear. The stretch spends the lash's own Heat 1 (no extra strength); dry cells keep it.</item>
    /// <item>Reach 5 m.</item>
    /// </list>
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class LashSteam : ISpellTransform
    {
        public const string Id = "AUG-318";

        public static readonly ProseParameter Water = ProseParameter.Of("crossed_water", 1, "connected Water 1");
        public static readonly ProseParameter Rank1Width = ProseParameter.Of("rank1_strip_width", 1, "create 1 m steam strip");
        public static readonly ProseParameter Rank1Lifetime = ProseParameter.Of("rank1_strip_lifetime", 2, "steam strip for 2 s");
        public static readonly ProseParameter Rank2Width = ProseParameter.Of("rank2_strip_width", 1.5, "Strip width 1.5 m");
        public static readonly ProseParameter Rank2Lifetime = ProseParameter.Of("rank2_strip_lifetime", 3, "1.5 m and 3 s");
        public static readonly ProseParameter Reach = ProseParameter.Of("reach", 5, "Reach 6 becomes 5 m");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Water, Rank1Width, Rank1Lifetime, Rank2Width, Rank2Lifetime, Reach };

        public TransformStage Stage => TransformStage.Payload;

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Apply(SpellFormBuilder form, int rank)
        {
            if (!(form.Delivery is LashDelivery lash)) throw new InvalidOperationException(Id + " transforms Flame Lash (SPL-015).");
            lash.SteamWidth = rank >= 2 ? Rank2Width.Float : Rank1Width.Float;
            lash.SteamLifetimeSec = rank >= 2 ? Rank2Lifetime.Float : Rank1Lifetime.Float;
            lash.Reach = Reach.Float;
            form.Range = Reach.Float;
        }
    }
}
