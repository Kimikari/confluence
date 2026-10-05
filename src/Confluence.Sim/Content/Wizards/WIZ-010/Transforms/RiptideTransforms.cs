using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Sim.Spells;
using Confluence.Sim.World;

namespace Confluence.Sim.Content.Wizards.HalenTide
{
    /// <summary>
    /// AUG-443 Riptide Fork (Silver, SPL-078, no exclusive group). Rank 1 "Stream splits into two 6 m lanes, each +10% speed
    /// in arrow direction." Rank 2 "Lanes 8 m, +10% unchanged." Drawback "Original +15% bonus removed; Water 2 shared." Two
    /// lanes leave one junction at the aimed point, <see cref="HalenRules.ForkHalfAngleDeg"/> either side of the aim, each
    /// its own field (spec L510: the record defines no shared allocation). The stream's Water 2 is divided between the two
    /// lanes (spec L529: a split divides its original strength), so each lane holds Water 1. A body gets the +10% from at
    /// most one lane at a time. Counterplay "Use either lane or freeze junction".
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class RiptideFork : ISpellTransform
    {
        public const string Id = "AUG-443";

        public static readonly ProseParameter Length = ProseParameter.Of("lane_length", 6, "two 6 m lanes");
        public static readonly ProseParameter Bonus = ProseParameter.Percent("speed_bonus", 10, "each +10% speed in arrow direction");
        public static readonly ProseParameter LengthRank2 = ProseParameter.Of("lane_length_rank2", 8, "Lanes 8 m");
        public static readonly ProseParameter SharedWater = ProseParameter.Of("shared_water", 2, "Water 2 shared");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Length, Bonus, LengthRank2, SharedWater };

        static RiptideFork() => HalenStructure.EnsureRegistered();

        public TransformStage Stage => TransformStage.Delivery;

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Apply(SpellFormBuilder form, int rank)
        {
            StreamDelivery stream = HalenStructure.Delivery<StreamDelivery>(form, Id);
            stream.ForkHalfAngleDeg = HalenRules.ForkHalfAngleDeg;
            stream.Length = rank >= 2 ? LengthRank2.Float : Length.Float;
            stream.SpeedBonus = Bonus.Float; // "Original +15% bonus removed"
            int perLane = System.Math.Max(1, (int)SharedWater.Value / stream.Branches);
            for (int i = 0; i < form.Payload.Deposits.Count; i++)
            {
                DepositSpec d = form.Payload.Deposits[i];
                if (d.Kind == DepositKind.Water && d.Placement == DepositPlacement.Footprint) d.Strength = perLane;
            }
        }
    }

    /// <summary>
    /// AUG-444 Riptide Valve (Prismatic, SPL-078, no exclusive group). Rank 1 "Recast reverses lane direction once without
    /// extending lifetime." Rank 2 "May rotate direction 90 degrees once instead." Drawback "Lane length 10 becomes 8 m;
    /// recast 5 mana." The spell-recast binding (5 mana, spec L94 "unless its record states a further resource payment")
    /// turns the arrow once: reversed, or at rank 2 turned 90 degrees when the caster aims closer to that side. A reversal
    /// keeps the lane's footprint; a 90 degree turn turns the lane itself about its center (so the bonus runs along the new
    /// length, not across the 2 m width). Either way the lane keeps its field slot, Water and remaining lifetime. The length drawback shortens the stream to 80% (10 m to 8 m; with Riptide
    /// Fork its lanes shrink in the same ratio). Counterplay "Read new arrow and claim route".
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class RiptideValve : ISpellTransform
    {
        public const string Id = "AUG-444";

        public static readonly ProseParameter Turn = ProseParameter.Of("turn_rank2", 90, "May rotate direction 90 degrees once instead");
        public static readonly ProseParameter BaseLength = ProseParameter.Of("base_length", 10, "Lane length 10 becomes 8 m");
        public static readonly ProseParameter Length = ProseParameter.Of("lane_length", 8, "Lane length 10 becomes 8 m");
        public static readonly ProseParameter RecastMana = ProseParameter.Of("recast_mana", 5, "recast 5 mana");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Turn, BaseLength, Length, RecastMana };

        static RiptideValve() => HalenStructure.EnsureRegistered();

        public TransformStage Stage => TransformStage.Delivery;

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Apply(SpellFormBuilder form, int rank)
        {
            StreamDelivery stream = HalenStructure.Delivery<StreamDelivery>(form, Id);
            stream.Valve = true;
            stream.ValveMayTurn = rank >= 2;
            stream.Length = stream.Length * (Length.Float / BaseLength.Float);
            form.Recast = new RecastRule(1, 0, RecastMana.Float, 0);
        }
    }
}
