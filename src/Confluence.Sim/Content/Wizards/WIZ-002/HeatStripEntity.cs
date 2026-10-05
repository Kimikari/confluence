using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Core.Time;
using Confluence.Reactions;
using Confluence.Reactions.Grid;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Fields;
using Confluence.Sim.Hashing;
using Confluence.Sim.Spells;
using Confluence.Sim.World;

namespace Confluence.Sim.Content.Wizards.Wiz002
{
    /// <summary>
    /// A neutral heated ground strip left by a WIZ-002 spell: SPL-013 Phoenix Rush's "1 m-wide Heat 1 trail persists 3 s
    /// and deals 6/s to any entrant", AUG-307 Sweep Wake's "1 m-wide neutral Heat 1 strip ... 4 damage/s". It is one
    /// gameplay field of its caster (spec L128, L510; Sweep Wake "field allocation required"), deposits its heat over its
    /// footprint for its lifetime (the field's own material, spec L161, L163), and damages anyone standing on a still-hot
    /// cell of it once per second — the owner too (spec L496 neutral fields). Water or frost that consumes a cell's heat
    /// cools that part ("Step out or cool strip"); when no covered cell holds heat any more the strip ends early.
    /// Contact is environmental damage attributed to the cast (spec L672): never guarded (spec L104), outside the cast's
    /// mastery reward (spec L480), and it raises no hit, refund or proc triggers.
    /// </summary>
    public sealed class HeatStripEntity : SpellEntity, IOwnedEntityBehavior
    {
        private const float Height = 2.5f;

        private readonly SpellContext _ctx;
        private readonly PayloadSpec _contact;
        private readonly int _heat;
        private readonly int _lifetimeTicks;
        private readonly int _intervalTicks;
        private readonly int[] _nextHit = new int[DuelWorld.PlayerCount];
        private readonly List<int> _cells = new List<int>();
        private OwnedEntity _owned;
        private bool _cooled;

        internal HeatStripEntity(SpellContext ctx, CastRecord cast, Footprint footprint, Vec3 center, float boundRadius, int heat, int lifetimeTicks,
            PayloadSpec contact, float intervalSec = 1f)
            : base(cast)
        {
            _ctx = ctx;
            Footprint = footprint;
            Center = center;
            BoundRadius = boundRadius;
            _heat = heat;
            _lifetimeTicks = lifetimeTicks;
            _contact = contact;
            _intervalTicks = System.Math.Max(1, Ticks.FromSeconds(intervalSec));
        }

        public Footprint Footprint { get; }
        public Vec3 Center { get; }
        public float BoundRadius { get; }

        /// <summary>The ownership-registry field entry (null when it could not be created).</summary>
        public OwnedEntity Owned => _owned;

        /// <summary>Spawns the strip and registers its field; returns null when the duel refuses the field.</summary>
        internal static HeatStripEntity Create(SpellContext ctx, CastRecord cast, Footprint footprint, Vec3 center, float boundRadius, int heat,
            float lifetimeSec, PayloadSpec contact)
        {
            var strip = new HeatStripEntity(ctx, cast, footprint, center, boundRadius, heat, Ticks.FromSeconds(lifetimeSec), contact);
            ctx.Spawn(strip);
            strip.Register();
            return strip.Alive ? strip : null;
        }

        private void Register()
        {
            _owned = _ctx.World.CreateOwnedEntity(new OwnedEntitySpec
            {
                Kind = OwnedEntityKind.Field,
                Owner = Owner,
                DefinitionId = SpellId,
                LifetimeTicks = _lifetimeTicks,
                Position = Center,
                Radius = BoundRadius,
                Neutral = true,
                OriginEventId = Cast.OriginEventId,
                Behavior = this,
            });
            if (_owned == null)
            {
                End(_ctx);
                return;
            }

            for (int i = 0; i < _nextHit.Length; i++) _nextHit[i] = _ctx.Tick;
            int remaining = _owned.ExpireTick - _ctx.Tick;
            _ctx.Emit(SpellEventKind.FieldCreated, Cast, Center, default, Id, BoundRadius, ticks: remaining);
            KitSupport.Deposit(_ctx, Cast, DepositKind.Heat, _heat, Center, Footprint, remaining, _owned.Id);
        }

        /// <summary>Whether a ground point is on the strip.</summary>
        public bool Contains(Vec3 feet) =>
            feet.Y >= Center.Y - 0.5f && feet.Y <= Center.Y + Height && Footprint.Contains(feet.XZ());

        /// <summary>Whether the cell under a point still holds heat (always true in a duel without a reaction world).</summary>
        public bool IsHotAt(Vec3 feet)
        {
            ReactionWorld world = KitSupport.Reactions(_ctx);
            return world == null || world.UnitsAt(feet).Heat > 0;
        }

        protected internal override void Step(SpellContext ctx)
        {
            if (_owned == null || !_owned.Active)
            {
                ctx.Emit(_cooled ? SpellEventKind.Cooled : SpellEventKind.FieldEnded, Cast, Center, default, Id);
                End(ctx);
            }
        }

        public void OnTick(DuelWorld world, OwnedEntity entity)
        {
            if (!Alive || _owned == null) return;
            ReactionWorld reactions = KitSupport.Reactions(_ctx);
            if (reactions != null && world.Tick > _owned.CreatedTick && !KitSupport.AnyHeat(reactions, Footprint, _cells))
            {
                // Every covered cell was cooled (water or frost consumed the strip's heat): the strip ends now.
                _cooled = true;
                world.RemoveOwnedEntity(_owned.Id, FieldChange.Expired, -1);
                return;
            }

            int now = world.Tick;
            for (int i = 0; i < DuelWorld.PlayerCount; i++)
            {
                if (!_ctx.IsTargetable(i)) continue;
                Vec3 feet = world.Player(i).Position;
                if (!Contains(feet) || !IsHotAt(feet) || now < _nextHit[i]) continue;
                _nextHit[i] = now + _intervalTicks;
                // Standing on hot ground is environmental contact (spec L672): attributed to the cast, no hit rewards.
                PayloadHit contact = PayloadHit.Area(Center, feet - Center);
                contact.Environmental = true;
                contact.Reaction = ReactionTag.Burn;
                _ctx.ApplyToPlayer(Cast, _contact, i, contact);
            }
        }

        protected override void HashState(ref StateHasher h)
        {
            h.Add(Center);
            h.Add(_owned == null ? 0 : _owned.Id);
            h.Add(_cooled);
            for (int i = 0; i < _nextHit.Length; i++) h.Add(_nextHit[i]);
        }
    }
}
