using System;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Sim.Combat;
using Confluence.Sim.Effects;
using Confluence.Sim.Hashing;
using Confluence.Sim.Props;
using Confluence.Sim.Spells;
using Confluence.Sim.World.Materials;

namespace Confluence.Sim.Content.Augments.AugmentsA
{
    // Shared augments of the "World reactions" family (spec L130-L175 world reactions, L171-L173 props).

    /// <summary>
    /// AUG-125 Dry Kiln (Silver, heat spell equipped). Condition "Your Heat effect hits a dry destructible." Rank 1 "Prop
    /// damage increases 10%.", rank 2 "Prop damage increases 16%." Drawback "Direct player damage decreases 2%." When the
    /// direct impact of one of the owner's Heat spells (school Heat, a heat tag, or the thermal channel) damages a
    /// destructible prop (spec L173) whose cell holds no liquid water (L175 "dry"), the durability damage is 10% (16%)
    /// higher. The owner's direct spell hits on players deal 2% less.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class DryKiln : IAugmentBehavior
    {
        public const string Id = "AUG-125";
        public const float Rank1Prop = 0.10f;   // "Prop damage increases 10%."
        public const float Rank2Prop = 0.16f;   // "Prop damage increases 16%."
        public const float DirectCut = 0.02f;   // "Direct player damage decreases 2%."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Prop, Rank2Prop)));
        }

        /// <summary>A Heat form: school Heat (tag school:heat or heat) or thermal damage.</summary>
        public static bool IsHeat(SpellForm form) =>
            form != null && (form.HasTag("school:heat") || form.HasTag("heat") || form.Channel == DamageChannel.Thermal);

        private sealed class Hook : EffectHook
        {
            private readonly float _bonus;

            public Hook(float bonus) : base(Id)
            {
                _bonus = bonus;
            }

            public override float AdjustSolidDamage(int solidId, float amount, string sourceId, CastEventId castId)
            {
                PropBody prop = World.Props.FindBySolid(solidId);
                if (prop == null || prop.Destroyed) return amount;
                CastRecord cast = AugmentsAKit.FindCast(World, castId);
                SpellForm form = cast != null ? cast.Form : AugmentsAKit.FormOf(Self, sourceId);
                if (!IsHeat(form)) return amount;
                bool wet = World.Materials is ReactionMaterials materials && materials.World != null && materials.World.IsWetAt(prop.Position);
                return wet ? amount : amount * (1f + _bonus);
            }

            public override void AdjustOutgoing(DamageRequest hit, float range, ref float fraction, ref float flat)
            {
                if (hit.Kind == DamageKind.Direct && hit.IsSpellHit)
                {
                    fraction -= DirectCut;
                }
            }
        }
    }
}
