using System;
using System.Collections.Generic;
using Confluence.Casting;
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
    /// <summary>Where a field cast is placed: its footprint, center, and the caster's feet at that moment.</summary>
    internal struct FieldPlacement
    {
        public Footprint Footprint;
        public Vec3 Center;
        public Vec3 Feet;

        /// <summary>The live field this placement describes (null for a prediction): excluded from overlap tests.</summary>
        public OwnedEntity Self;

        /// <summary>
        /// The placement point: a circle's or rectangle's center, a lane's starting point (the predicted ground point, or
        /// the created field's IFieldBody.PlacementPoint; a lane's footprint center is its midpoint, not where it was placed).
        /// </summary>
        public Vec2 Anchor;
    }

    /// <summary>
    /// A Fieldcraft record whose price depends on where a field cast is created ("Place a field beyond 8 m", "created
    /// without overlapping another field", "Create a field under overhead cover"). The field appears at release, after
    /// the commitment that pays for it (spec L90-L92), so the price settles against the field actually created:
    /// <list type="bullet">
    /// <item>A drawback increase is charged at commitment from the predicted placement (so it is always payable), then
    /// settled at creation: returned if the created field no longer meets it, charged after the fact (at most the mana
    /// the caster has, spec L78) if it newly does.</item>
    /// <item>A cost decrease is granted when the created field qualifies, as a return through the cast's refund ledger
    /// (25% of the payment, once per effect, L90, L124) — the same rule as every condition completed after commitment.</item>
    /// </list>
    /// Re-aiming during the windup or a field appearing or dissolving in the meantime therefore never keeps a decrease
    /// the created field does not earn, or dodges a drawback it incurs. A subclass that also changes the windup from the
    /// placement binds the placement to the committed one (<see cref="BindsPlacement"/>), because the windup cannot be
    /// settled after the fact.
    /// </summary>
    internal abstract class FieldSettlementHook : EffectHook
    {
        /// <summary>A cast whose field has not appeared this long after commitment is no longer tracked.</summary>
        private const int MaxWaitTicks = 300;

        private readonly PaymentLog _payments = new PaymentLog();
        private CastEventId _cast = CastEventId.None;
        private string _spellId;
        private int _commitTick;
        private float _charged;
        private bool _bound;
        private Vec3 _boundGround;
        private Vec3 _boundDirection;

        protected FieldSettlementHook(string id) : base(id)
        {
        }

        /// <summary>The record's decrease and increase (mana) for a field form created at a placement.</summary>
        protected abstract void Price(SpellForm form, in FieldPlacement placement, float listedCost, out float decrease, out float increase);

        /// <summary>Whether this record binds the placement at commitment (it also changes the windup from it).</summary>
        protected virtual bool BindsPlacement(SpellForm form) => false;

        /// <summary>The placement a field cast of this form would get now (along the current aim).</summary>
        protected bool TryPredict(SpellForm form, out FieldPlacement placement)
        {
            placement = default;
            if (!FieldGeometry.TryPredict(World, form, Player, out Footprint footprint, out Vec3 center, out Vec3 ground)) return false;
            placement.Footprint = footprint;
            placement.Center = center;
            placement.Anchor = ground.XZ();
            placement.Feet = Self.Position;
            return true;
        }

        public override void AdjustCastPrice(in CastQuote quote, ref float reduction, ref float increase)
        {
            SpellForm form = FieldStages.PricedField(this, quote);
            if (form == null || !TryPredict(form, out FieldPlacement predicted)) return;
            Price(form, predicted, quote.BaseCost, out _, out float inc);
            increase += Math.Max(0f, inc); // the drawback is payable at commitment; decreases wait for the created field
        }

        public override void OnEvent(DuelEvent e)
        {
            _payments.Observe(e, Player);
            if (CastFacts.IsSpellCommit(e, Player, out CastDuelEvent c))
            {
                SpellForm form = AugmentsAKit.FormOf(Self, c.SpellId);
                _cast = CastEventId.None;
                if (FieldStages.FieldOf(form) == null || !TryPredict(form, out FieldPlacement predicted)) return;
                Price(form, predicted, form.Mana, out _, out float inc);
                _cast = c.CastId;
                _spellId = c.SpellId;
                _commitTick = c.Tick;
                _charged = Math.Max(0f, inc); // what AdjustCastPrice added this tick
                _bound = BindsPlacement(form) && FieldStages.FieldOf(form).Placement == PlacementMode.AtAimPoint;
                if (_bound)
                {
                    SpellRuntime runtime = AugmentsAKit.Runtime(World);
                    _boundGround = form.Delivery.PredictTarget(runtime.Context, form, Player);
                    _boundDirection = runtime.Context.AimDirection(Player);
                }
            }
            else if (e is CastDuelEvent end && end.Player == Player && end.CastId == _cast
                && (end.Kind == CastEventKind.Interrupted || end.Kind == CastEventKind.CommittedCastCancelled))
            {
                _cast = CastEventId.None; // never released: nothing is created
            }
        }

        public override void OnTick()
        {
            if (!_cast.IsValid)
            {
                return;
            }

            IFieldBody field = FindField(_cast);
            if (field == null)
            {
                if (Now - _commitTick > MaxWaitTicks) _cast = CastEventId.None;
                return;
            }

            CastEventId cast = _cast;
            _cast = CastEventId.None;
            Settle(cast, field);
        }

        private void Settle(CastEventId cast, IFieldBody field)
        {
            SpellForm form = AugmentsAKit.FormOf(Self, _spellId);
            if (form == null || !InCombat) return;
            var actual = new FieldPlacement { Footprint = field.Footprint, Center = field.Center, Anchor = field.PlacementPoint, Feet = Self.Position, Self = field.Owned };
            Price(form, actual, form.Mana, out float decrease, out float increase);
            float back = Math.Max(0f, decrease) + Math.Max(0f, _charged - Math.Max(0f, increase));
            float owed = Math.Max(0f, increase) - _charged;
            int payment = _payments.PaymentOf(cast);
            if (back > 0f && payment != 0)
            {
                World.Effects.RefundCast(Player, payment, back, Key); // the cast's 25% refund ledger (L90, L124)
            }

            if (owed > 0f)
            {
                ChargeMana(owed); // a drawback the created field incurs (never below zero mana, L78)
            }
        }

        private IFieldBody FindField(CastEventId cast)
        {
            SpellRuntime runtime = AugmentsAKit.Runtime(World);
            if (runtime == null) return null;
            IReadOnlyList<SpellEntity> entities = runtime.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                if (entities[i] is IFieldBody f && f.Cast.CastId == cast && f.HasAppeared && f.Owned != null) return f; // a framework or kit field
            }

            return null;
        }

        public override bool BindFieldPlacement(CastRecord cast, out Vec3 ground, out Vec3 direction)
        {
            ground = _boundGround;
            direction = _boundDirection;
            return _bound && _cast.IsValid && cast.CastId == _cast;
        }

        public override void Hash(ref StateHasher h)
        {
            h.Add((ulong)_cast.Packed);
            h.Add(_spellId);
            h.Add(_commitTick);
            h.Add(_charged);
            h.Add(_bound);
            h.Add(_boundGround);
            h.Add(_boundDirection);
            _payments.Hash(ref h);
        }
    }
}
