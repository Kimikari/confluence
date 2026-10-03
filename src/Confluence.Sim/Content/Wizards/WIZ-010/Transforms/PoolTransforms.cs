using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Sim.Spells;
using Confluence.Sim.World;

namespace Confluence.Sim.Content.Wizards.HalenTide
{
    /// <summary>
    /// AUG-439 Pool Lane (Silver, SPL-076, exclusive group SPL-076-delivery). Rank 1 "Pool becomes a connected 8 m-by-2 m
    /// lane for 6 s, Water 2 total." Rank 2 "Lane 10 m long, still Water 2." Drawback "Less lateral coverage; conductivity
    /// follows exact lane." A delivery branch: one rectangular field centered on the aimed point, its length along the aim,
    /// Water 2 in each covered cell; its connected cells carry a charge only along the lane (spec L148). Counterplay "Leave
    /// lane or consume water".
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class PoolLane : ISpellTransform
    {
        public const string Id = "AUG-439";

        public static readonly ProseParameter Length = ProseParameter.Of("lane_length", 8, "connected 8 m-by-2 m lane for 6 s");
        public static readonly ProseParameter Width = ProseParameter.Of("lane_width", 2, "8 m-by-2 m lane");
        public static readonly ProseParameter Lifetime = ProseParameter.Of("lane_lifetime", 6, "lane for 6 s");
        public static readonly ProseParameter Water = ProseParameter.Of("lane_water", 2, "Water 2 total");
        public static readonly ProseParameter LengthRank2 = ProseParameter.Of("lane_length_rank2", 10, "Lane 10 m long, still Water 2");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Length, Width, Lifetime, Water, LengthRank2 };

        static PoolLane() => HalenStructure.EnsureRegistered();

        public TransformStage Stage => TransformStage.Delivery;

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Apply(SpellFormBuilder form, int rank)
        {
            form.Delivery = new FieldDelivery
            {
                Shape = FieldShape.Rectangle,
                Length = rank >= 2 ? LengthRank2.Float : Length.Float,
                Width = Width.Float,
                Placement = PlacementMode.AtAimPoint,
                LifetimeSec = Lifetime.Float,
                AffectsOwner = true,
            };
            form.Payload.ClearDeposits();
            form.Payload.AddDeposit(DepositKind.Water, (int)Water.Value, DepositPlacement.Footprint);
        }
    }

    /// <summary>
    /// AUG-440 Pool Divide (Gold, SPL-076, exclusive group SPL-076-delivery). Rank 1 "Create two 1.5 m-radius pools
    /// separated 2-4 m, Water 1 each, 6 s." Rank 2 "Pool radius 2 m, Water 1 each." Drawback "Pools are disconnected; cannot
    /// electrify between them." Two separate fields (each one allocation, spec L510) side by side across the aim with a
    /// dry gap (<see cref="HalenRules.PoolDivideGap"/>) between their edges; a charge in one never crosses the dry gap
    /// (spec L148). Counterplay "Use dry gap or react separately".
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class PoolDivide : ISpellTransform
    {
        public const string Id = "AUG-440";

        public static readonly ProseParameter Radius = ProseParameter.Of("pool_radius", 1.5, "two 1.5 m-radius pools");
        public static readonly ProseParameter GapMin = ProseParameter.Of("gap_min", 2, "separated 2–4 m");
        public static readonly ProseParameter GapMax = ProseParameter.Of("gap_max", 4, "separated 2–4 m");
        public static readonly ProseParameter Water = ProseParameter.Of("pool_water", 1, "Water 1 each, 6 s");
        public static readonly ProseParameter Lifetime = ProseParameter.Of("pool_lifetime", 6, "Water 1 each, 6 s");
        public static readonly ProseParameter RadiusRank2 = ProseParameter.Of("pool_radius_rank2", 2, "Pool radius 2 m");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Radius, GapMin, GapMax, Water, Lifetime, RadiusRank2 };

        static PoolDivide() => HalenStructure.EnsureRegistered();

        public TransformStage Stage => TransformStage.Delivery;

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Apply(SpellFormBuilder form, int rank)
        {
            float gap = HalenRules.PoolDivideGap;
            if (gap < GapMin.Float) gap = GapMin.Float;
            if (gap > GapMax.Float) gap = GapMax.Float;
            form.Delivery = new TwinPoolDelivery
            {
                Radius = rank >= 2 ? RadiusRank2.Float : Radius.Float,
                Gap = gap,
                LifetimeSec = Lifetime.Float,
            };
            form.Payload.ClearDeposits();
            form.Payload.AddDeposit(DepositKind.Water, (int)Water.Value, DepositPlacement.Footprint);
        }
    }
}
