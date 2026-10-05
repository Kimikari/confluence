using System;
using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Reactions.Grid;
using Confluence.Sim.Casting;
using Confluence.Sim.Duel;
using Confluence.Sim.Effects;
using Confluence.Sim.Fields;
using Confluence.Sim.Hashing;
using Confluence.Sim.Spells;

namespace Confluence.Sim.Content.Augments.AugmentsA
{
    // Shared augments of the "Fieldcraft and terrain" family whose draft requirement is a field spell (spec L128 fields:
    // three per player, 8 s lifetime; L124 cost floor; L92 windup). A "field" here is a field delivery (circle, lane or
    // rectangle); its "radius" is a circle's radius or a lane's or rectangle's half-width, and its "initial direct
    // damage" is the damage of first contact (the enter payload). Placement prices settle against the field actually
    // created (FieldSettlement.cs): drawbacks are charged at commitment from the predicted placement and corrected at
    // creation; decreases are returned once the created field qualifies.

    internal static class FieldStages
    {
        public static FieldDelivery FieldOf(SpellFormBuilder form) => form.Delivery as FieldDelivery;

        public static FieldDelivery FieldOf(SpellForm form) => form?.Delivery as FieldDelivery;

        public static bool IsDamaging(FieldDelivery f) =>
            Deals(f.EnterPayload) || Deals(f.TickPayload) || Deals(f.ExitPayload);

        private static bool Deals(PayloadSpec p) => p != null && (p.Direct > 0f || p.Splash > 0f);

        /// <summary>Scales the radius (circle) or the half-width (lane, rectangle) by adding <paramref name="metres"/>.</summary>
        public static void AddRadius(FieldDelivery f, float metres)
        {
            if (f.Shape == FieldShape.Circle) f.Radius = Math.Max(0.1f, f.Radius + metres);
            else f.Width = Math.Max(0.2f, f.Width + 2f * metres);
        }

        public static void ScaleRadius(FieldDelivery f, float factor)
        {
            if (f.Shape == FieldShape.Circle) f.Radius *= factor;
            else f.Width *= factor;
        }

        /// <summary>Scales the damage of first contact (the enter payload's direct and splash parts).</summary>
        public static void ScaleInitialDamage(FieldDelivery f, float factor)
        {
            if (f.EnterPayload == null) return;
            f.EnterPayload.Direct *= factor;
            f.EnterPayload.Splash *= factor;
        }

        /// <summary>The field form of the cast being priced, or null.</summary>
        public static SpellForm PricedField(EffectHook hook, in CastQuote quote)
        {
            SpellForm form = AugmentsAKit.FormOf(hook.Self, quote.SpellId);
            return FieldOf(form) != null ? form : null;
        }
    }

    /// <summary>
    /// AUG-049 Wide Foundation (Silver, field spell equipped). Condition "Create a non-damaging field." Rank 1 "Radius
    /// increases 0.2 m.", rank 2 "Radius increases 0.35 m." Drawback "Paid mana cost increases 2." The owner's fields that
    /// deal no damage are 0.2 m (0.35 m) wider in radius (half-width for lanes) and cost 2 more mana (after the 70%
    /// floor, spec L124).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class WideFoundation : IAugmentBehavior, IAugmentFormStage
    {
        public const string Id = "AUG-049";
        public const float Rank1Radius = 0.2f;   // "Radius increases 0.2 m."
        public const float Rank2Radius = 0.35f;  // "Radius increases 0.35 m."
        public const float ExtraMana = 2f;       // "Paid mana cost increases 2."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook());
        }

        public void ApplyToForm(SpellFormBuilder form, int rank)
        {
            FieldDelivery f = FieldStages.FieldOf(form);
            if (f == null || FieldStages.IsDamaging(f)) return;
            FieldStages.AddRadius(f, AugmentsAKit.ByRank(rank, Rank1Radius, Rank2Radius));
        }

        private sealed class Hook : EffectHook
        {
            public Hook() : base(Id)
            {
            }

            public override void AdjustCastPrice(in CastQuote quote, ref float reduction, ref float increase)
            {
                SpellForm form = FieldStages.PricedField(this, quote);
                if (form != null && !FieldStages.IsDamaging(FieldStages.FieldOf(form))) increase += ExtraMana;
            }
        }
    }

    /// <summary>
    /// AUG-050 Compact Hazard (Silver, field spell equipped). Condition "Create a damaging field." Rank 1 "Initial direct
    /// damage increases 5%.", rank 2 "Initial direct damage increases 8%." Drawback "Radius decreases 10%." The owner's
    /// damaging fields deal 5% (8%) more on first contact and are 10% smaller in radius (half-width for lanes).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class CompactHazard : IAugmentBehavior, IAugmentFormStage
    {
        public const string Id = "AUG-050";
        public const float Rank1Damage = 0.05f;  // "Initial direct damage increases 5%."
        public const float Rank2Damage = 0.08f;  // "Initial direct damage increases 8%."
        public const float RadiusCut = 0.10f;    // "Radius decreases 10%."

        public void Attach(IEffectHost host, int rank) => ContentArgs.Require(host);

        public void ApplyToForm(SpellFormBuilder form, int rank)
        {
            FieldDelivery f = FieldStages.FieldOf(form);
            if (f == null || !FieldStages.IsDamaging(f)) return;
            FieldStages.ScaleInitialDamage(f, 1f + AugmentsAKit.ByRank(rank, Rank1Damage, Rank2Damage));
            FieldStages.ScaleRadius(f, 1f - RadiusCut);
        }
    }

    /// <summary>
    /// AUG-051 Slow Dissipation (Silver, field spell equipped). Condition "Create a field with normal lifetime at most 6 s."
    /// Rank 1 "Lifetime increases 0.5 s.", rank 2 "Lifetime increases 1 s." Drawback "Initial direct damage decreases 5%; all
    /// lifetimes remain <=8 s." The owner's fields whose lifetime is at most 6 s last 0.5 s (1 s) longer, never past the
    /// 8 s field cap (spec L128, applied when the form is built), and deal 5% less on first contact.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class SlowDissipation : IAugmentBehavior, IAugmentFormStage
    {
        public const string Id = "AUG-051";
        public const float MaxNormalLifetime = 6f; // "normal lifetime at most 6 s"
        public const float Rank1Lifetime = 0.5f;   // "Lifetime increases 0.5 s."
        public const float Rank2Lifetime = 1f;     // "Lifetime increases 1 s."
        public const float DamageCut = 0.05f;      // "Initial direct damage decreases 5%"

        public void Attach(IEffectHost host, int rank) => ContentArgs.Require(host);

        public void ApplyToForm(SpellFormBuilder form, int rank)
        {
            FieldDelivery f = FieldStages.FieldOf(form);
            if (f == null || f.LifetimeSec > MaxNormalLifetime + 1e-4f) return;
            f.LifetimeSec += AugmentsAKit.ByRank(rank, Rank1Lifetime, Rank2Lifetime);
            FieldStages.ScaleInitialDamage(f, 1f - DamageCut);
        }
    }

    /// <summary>
    /// AUG-052 Quick Dissipation (Silver, field spell equipped). Condition "Create a field." Rank 1 "Initial direct damage
    /// increases 6%.", rank 2 "Initial direct damage increases 10%." Drawback "Lifetime decreases 1 s, minimum 1 s." The
    /// owner's fields deal 6% (10%) more on first contact and last 1 s less, never below 1 s (or their own lifetime if
    /// that is already shorter).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class QuickDissipation : IAugmentBehavior, IAugmentFormStage
    {
        public const string Id = "AUG-052";
        public const float Rank1Damage = 0.06f;  // "Initial direct damage increases 6%."
        public const float Rank2Damage = 0.10f;  // "Initial direct damage increases 10%."
        public const float LifetimeCut = 1f;     // "Lifetime decreases 1 s"
        public const float MinLifetime = 1f;     // "minimum 1 s"

        public void Attach(IEffectHost host, int rank) => ContentArgs.Require(host);

        public void ApplyToForm(SpellFormBuilder form, int rank)
        {
            FieldDelivery f = FieldStages.FieldOf(form);
            if (f == null) return;
            FieldStages.ScaleInitialDamage(f, 1f + AugmentsAKit.ByRank(rank, Rank1Damage, Rank2Damage));
            f.LifetimeSec = Math.Max(Math.Min(MinLifetime, f.LifetimeSec), f.LifetimeSec - LifetimeCut);
        }
    }

    /// <summary>
    /// AUG-053 Remote Survey (Silver, field spell equipped). Condition "Place a field beyond 8 m." Rank 1 "Cost decreases 2
    /// mana.", rank 2 "Cost decreases 4 mana." Drawback "Fields within 3 m cost 2 extra mana." A field created more than
    /// 8 m from the caster's feet costs 2 (4) less, returned when it appears (the cast's refund ledger); one created
    /// within 3 m costs 2 more (charged at commitment from the predicted placement and settled at creation).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class RemoteSurvey : IAugmentBehavior
    {
        public const string Id = "AUG-053";
        public const float FarRange = 8f;    // "beyond 8 m"
        public const float NearRange = 3f;   // "Fields within 3 m"
        public const float Rank1Mana = 2f;   // "Cost decreases 2 mana."
        public const float Rank2Mana = 4f;   // "Cost decreases 4 mana."
        public const float NearMana = 2f;    // "cost 2 extra mana"

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new DistancePriceHook(Id, FarRange, float.PositiveInfinity, AugmentsAKit.ByRank(rank, Rank1Mana, Rank2Mana), 0f, NearRange, NearMana));
        }
    }

    /// <summary>
    /// AUG-054 Home Circle (Silver, field spell equipped). Condition "Place a field within 2 m of self." Rank 1 "Cost
    /// decreases 2 mana.", rank 2 "Cost decreases 4 mana." Drawback "Fields beyond 10 m cost 2 extra mana." A field
    /// created within 2 m of the caster's feet (fields centered on the caster included) costs 2 (4) less, returned when it
    /// appears; one created more than 10 m away costs 2 more (settled as for AUG-053).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class HomeCircle : IAugmentBehavior
    {
        public const string Id = "AUG-054";
        public const float NearRange = 2f;   // "within 2 m of self"
        public const float FarRange = 10f;   // "Fields beyond 10 m"
        public const float Rank1Mana = 2f;   // "Cost decreases 2 mana."
        public const float Rank2Mana = 4f;   // "Cost decreases 4 mana."
        public const float FarMana = 2f;     // "cost 2 extra mana"

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new DistancePriceHook(Id, -1f, NearRange, AugmentsAKit.ByRank(rank, Rank1Mana, Rank2Mana), FarRange, float.PositiveInfinity, FarMana));
        }
    }

    /// <summary>A field cast's price by placement distance: a reduction inside one band, an increase inside another.</summary>
    internal sealed class DistancePriceHook : FieldSettlementHook
    {
        private readonly float _bonusMin;
        private readonly float _bonusMax;
        private readonly float _bonus;
        private readonly float _penaltyMin;
        private readonly float _penaltyMax;
        private readonly float _penalty;

        /// <summary>Reduction when bonusMin &lt; d (strict, "beyond") and d &lt;= bonusMax ("within"); same for the penalty.</summary>
        public DistancePriceHook(string id, float bonusMin, float bonusMax, float bonus, float penaltyMin, float penaltyMax, float penalty) : base(id)
        {
            _bonusMin = bonusMin;
            _bonusMax = bonusMax;
            _bonus = bonus;
            _penaltyMin = penaltyMin;
            _penaltyMax = penaltyMax;
            _penalty = penalty;
        }

        protected override void Price(SpellForm form, in FieldPlacement placement, float listedCost, out float decrease, out float increase)
        {
            float d = Vec2.Distance(placement.Feet.XZ(), placement.Anchor);
            decrease = d > _bonusMin && d <= _bonusMax ? _bonus : 0f;
            increase = d > _penaltyMin && d <= _penaltyMax ? _penalty : 0f;
        }
    }

    /// <summary>
    /// AUG-055 Edge Casting (Silver, field spell equipped). Condition "Begin a spell while within 0.5 m of a field edge."
    /// Rank 1 "Preparation decreases 5%.", rank 2 "Preparation decreases 8%." Drawback "Must be grounded; field center grants
    /// no bonus." A spell whose preparation starts while the owner is grounded with its feet within 0.5 m of any live
    /// field's edge (inside or outside, any owner) has its windup — the preparation after commitment, as for AUG-005 —
    /// shortened by 5% (8%) under the windup clamps (L480); deeper inside a field, or airborne, gives nothing.
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class EdgeCasting : IAugmentBehavior
    {
        public const string Id = "AUG-055";
        public const float EdgeBand = 0.5f;      // "within 0.5 m of a field edge"
        public const float Rank1Windup = 0.05f;  // "Preparation decreases 5%."
        public const float Rank2Windup = 0.08f;  // "Preparation decreases 8%."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Windup, Rank2Windup)));
        }

        /// <summary>Whether the feet are within the band of some live field's edge.</summary>
        public static bool AtEdge(DuelWorld world, Vec3 feet)
        {
            List<KeyValuePair<OwnedEntity, Footprint>> fields = FieldGeometry.LiveFields(world);
            for (int i = 0; i < fields.Count; i++)
            {
                if (Math.Abs(FieldGeometry.SignedEdgeDistance(fields[i].Value, feet.XZ())) <= EdgeBand) return true;
            }

            return false;
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _windup;
            private CastEventId _edgeCast = CastEventId.None;

            public Hook(float windup) : base(Id)
            {
                _windup = windup;
            }

            public override void OnEvent(DuelEvent e)
            {
                if (e is CastDuelEvent c && c.Player == Player && c.Kind == CastEventKind.PreparationStarted && c.Cast.Slot >= 0)
                {
                    _edgeCast = Self.Motor.Grounded && AtEdge(World, Self.Position) ? c.CastId : CastEventId.None;
                }
            }

            public override void AdjustCastTiming(in CastQuote quote, ref CastTimingAdjust timing)
            {
                if (_edgeCast.IsValid && AugmentsAKit.CurrentCast(Self) == _edgeCast)
                {
                    timing.WindupReduction += _windup;
                }
            }

            public override void Hash(ref StateHasher h) => h.Add((ulong)_edgeCast.Packed);
        }
    }

    /// <summary>
    /// AUG-060 Safe Corridor (Gold, field spell equipped). Condition "Your field is created without overlapping another
    /// field." Rank 1 "Cost decreases 5% mana.", rank 2 "Cost decreases 8% mana." Drawback "Overlapping fields cost 2
    /// additional mana." A field created sharing no ground with any other live field (either player's, clouds included)
    /// costs 5% (8%) of its listed cost less, returned when it appears; one that overlaps costs 2 more (charged at
    /// commitment from the predicted footprint, settled at creation: a field appearing or dissolving during the windup
    /// counts as of the creation).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class SafeCorridor : IAugmentBehavior
    {
        public const string Id = "AUG-060";
        public const float Rank1Cut = 0.05f;   // "Cost decreases 5% mana."
        public const float Rank2Cut = 0.08f;   // "Cost decreases 8% mana."
        public const float OverlapMana = 2f;   // "Overlapping fields cost 2 additional mana."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Cut, Rank2Cut)));
        }

        public static bool OverlapsAny(DuelWorld world, in Footprint footprint, OwnedEntity except = null)
        {
            List<KeyValuePair<OwnedEntity, Footprint>> fields = FieldGeometry.LiveFields(world);
            for (int i = 0; i < fields.Count; i++)
            {
                if (!ReferenceEquals(fields[i].Key, except) && FieldGeometry.Overlaps(footprint, fields[i].Value)) return true;
            }

            return false;
        }

        private sealed class Hook : FieldSettlementHook
        {
            private readonly float _cut;

            public Hook(float cut) : base(Id)
            {
                _cut = cut;
            }

            protected override void Price(SpellForm form, in FieldPlacement placement, float listedCost, out float decrease, out float increase)
            {
                bool overlaps = OverlapsAny(World, placement.Footprint, placement.Self);
                decrease = overlaps ? 0f : listedCost * _cut;
                increase = overlaps ? OverlapMana : 0f;
            }
        }
    }

    /// <summary>
    /// AUG-065 Open Sky (Prismatic, field spell equipped). Condition "Create a field under no overhead cover." Rank 1
    /// "Preparation decreases 6%.", rank 2 "Preparation decreases 10%." Drawback "Creation under a ceiling costs 2 extra
    /// mana." A field cast whose placement at commitment has no solid above its center (within
    /// <see cref="FieldGeometry.OverheadProbe"/>) has its windup shortened by 6% (10%); one created under a ceiling costs 2
    /// more mana (charged at commitment, settled at creation). The windup is decided at commitment, so an aimed field is
    /// created at the committed placement (re-aiming during the windup cannot keep the shorter windup).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class OpenSky : IAugmentBehavior
    {
        public const string Id = "AUG-065";
        public const float Rank1Windup = 0.06f;  // "Preparation decreases 6%."
        public const float Rank2Windup = 0.10f;  // "Preparation decreases 10%."
        public const float CeilingMana = 2f;     // "Creation under a ceiling costs 2 extra mana."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new CoverHook(Id, AugmentsAKit.ByRank(rank, Rank1Windup, Rank2Windup), 0f, CeilingMana, 0));
        }
    }

    /// <summary>
    /// AUG-066 Sheltered Work (Prismatic, field spell equipped). Condition "Create a field under overhead cover." Rank 1
    /// "Cost decreases 6% mana.", rank 2 "Cost decreases 10% mana." Drawback "Field creation outdoors has 0.05 s longer
    /// preparation." A field created under a solid costs 6% (10%) of its listed cost less, returned when it appears; one
    /// whose placement at commitment is in the open has a windup 0.05 s (3 ticks) longer, and an aimed field is created at
    /// that committed placement (re-aiming under cover during the windup cannot dodge the longer windup).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class ShelteredWork : IAugmentBehavior
    {
        public const string Id = "AUG-066";
        public const float Rank1Cut = 0.06f;     // "Cost decreases 6% mana."
        public const float Rank2Cut = 0.10f;     // "Cost decreases 10% mana."
        public const float OutdoorSec = 0.05f;   // "0.05 s longer preparation"

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new CoverHook(Id, 0f, AugmentsAKit.ByRank(rank, Rank1Cut, Rank2Cut), 0f, AugmentsAKit.Ticks(OutdoorSec)));
        }
    }

    /// <summary>
    /// A field cast's price and windup by overhead cover at its predicted center: in the open a windup reduction
    /// (fraction) and extra windup ticks; under cover a cost reduction (fraction of base) and a flat increase.
    /// </summary>
    internal sealed class CoverHook : FieldSettlementHook
    {
        private readonly float _openWindupCut;
        private readonly float _coveredCostCut;
        private readonly float _coveredMana;
        private readonly int _openWindupTicks;

        public CoverHook(string id, float openWindupCut, float coveredCostCut, float coveredMana, int openWindupTicks) : base(id)
        {
            _openWindupCut = openWindupCut;
            _coveredCostCut = coveredCostCut;
            _coveredMana = coveredMana;
            _openWindupTicks = openWindupTicks;
        }

        protected override void Price(SpellForm form, in FieldPlacement placement, float listedCost, out float decrease, out float increase)
        {
            bool covered = FieldGeometry.UnderCover(World.Collision, placement.Center);
            decrease = covered ? listedCost * _coveredCostCut : 0f;
            increase = covered ? _coveredMana : 0f;
        }

        // The windup cannot be settled after release: it is decided from the placement at commitment, which binds it.
        protected override bool BindsPlacement(SpellForm form) => true;

        public override void AdjustCastTiming(in CastQuote quote, ref CastTimingAdjust timing)
        {
            SpellForm form = FieldStages.PricedField(this, quote);
            if (form == null || !TryPredict(form, out FieldPlacement predicted) || FieldGeometry.UnderCover(World.Collision, predicted.Center)) return;
            timing.WindupReduction += _openWindupCut;
            timing.WindupDeltaTicks += _openWindupTicks;
        }
    }

    /// <summary>
    /// AUG-068 One Landmark (Prismatic, field spell equipped). Condition "You have exactly one living persistent field."
    /// Rank 1 "Spells cast inside it cost 6% less mana.", rank 2 "Spells cast inside it cost 10% less mana." Drawback
    /// "Creating a second field removes benefit; field allegiance remains neutral." While the owner has exactly one live
    /// field (spec L128 field entities: spell fields and player clouds) and stands inside its footprint, every spell it
    /// pays costs 6% (10%) of its base cost less inside the 70% floor; with two or three fields nothing. The field itself
    /// is unchanged (whoever it affects, it still affects).
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class OneLandmark : IAugmentBehavior
    {
        public const string Id = "AUG-068";
        public const float Rank1Cut = 0.06f;   // "Spells cast inside it cost 6% less mana."
        public const float Rank2Cut = 0.10f;   // "Spells cast inside it cost 10% less mana."

        public void Attach(IEffectHost host, int rank)
        {
            ContentArgs.Require(host);
            host.Attach(new Hook(AugmentsAKit.ByRank(rank, Rank1Cut, Rank2Cut)));
        }

        private sealed class Hook : EffectHook
        {
            private readonly float _cut;

            public Hook(float cut) : base(Id)
            {
                _cut = cut;
            }

            public override void AdjustCastPrice(in CastQuote quote, ref float reduction, ref float increase)
            {
                List<KeyValuePair<OwnedEntity, Footprint>> mine = FieldGeometry.LiveFields(World, Player);
                if (mine.Count == 1 && mine[0].Value.Contains(Self.Position.XZ()))
                {
                    reduction += quote.BaseCost * _cut;
                }
            }
        }
    }
}
