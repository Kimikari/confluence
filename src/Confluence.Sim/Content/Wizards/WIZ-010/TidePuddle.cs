using System;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions.Grid;
using Confluence.Sim.Duel;
using Confluence.Sim.Fields;
using Confluence.Sim.Hashing;
using Confluence.Sim.Spells;

namespace Confluence.Sim.Content.Wizards.HalenTide
{
    /// <summary>
    /// A timed puddle authored by a WIZ-010 transformation (AUG-435 Hammer Wake "Hit leaves a 1 m puddle for 3 s", AUG-441
    /// Cascade Landing "Landing creates 1 m puddle for 3 s", AUG-446 Prison Pool "impact creates 1 m puddle for 2 s"). Such a
    /// puddle is a new gameplay field: it consumes the caster's normal allocation (spec L128 "Neutral reaction fields
    /// created by a player count against that player's field limit"; L510 "A new field ... consumes the normal allocation
    /// ... never grants an extra hidden field"), so at three owned fields the oldest retires, exactly like a framework field
    /// (<see cref="FieldEntity"/>). It is neutral, its Water deposit carries the field id (a dispel removes it as one layer,
    /// spec L161), and its lifetime is the owned field's. The water's own effects (wetness, conduction, steam, freezing)
    /// come from the reaction world.
    /// </summary>
    public sealed class TidePuddleEntity : SpellEntity, IOwnedEntityBehavior
    {
        private readonly Vec3 _center;
        private readonly float _radius;

        private TidePuddleEntity(CastRecord cast, Vec3 center, float radius)
            : base(cast)
        {
            _center = center;
            _radius = Math.Max(0f, radius);
        }

        /// <summary>The ground point the puddle is centered on.</summary>
        public Vec3 Center => _center;

        public float Radius => _radius;

        public Footprint Footprint => Footprint.Circle(_center.XZ(), _radius);

        /// <summary>The ownership-registry entry that counts against the three-field limit (spec L128).</summary>
        public OwnedEntity Owned { get; private set; }

        /// <summary>
        /// Creates a puddle of Water <paramref name="strength"/> on the ground below a point for a lifetime (capped at the 8 s
        /// field lifetime, spec L128). Returns null when no field could be created (outside combat, or nothing retirable),
        /// in which case no water is placed either.
        /// </summary>
        public static TidePuddleEntity Create(SpellContext ctx, CastRecord cast, Vec3 point, int strength, float radius, float lifetimeSec)
        {
            if (strength <= 0 || !(radius > 0f)) return null;
            int lifetime = Math.Min(Ticks.FromSeconds(lifetimeSec), Ticks.FromSeconds(GlobalRules.FieldsAndSummons.MaxLifetimeSec));
            if (lifetime <= 0) return null;
            Vec3 ground = HalenRules.Ground(ctx.Collision, point);
            TidePuddleEntity puddle = ctx.Spawn(new TidePuddleEntity(cast, ground, radius));
            puddle.Owned = ctx.World.CreateOwnedEntity(new OwnedEntitySpec
            {
                Kind = OwnedEntityKind.Field,
                Owner = cast.Caster,
                DefinitionId = cast.SpellId,
                LifetimeTicks = lifetime,
                Position = ground,
                Radius = puddle._radius,
                Neutral = true, // water affects everyone (spec L529); ownership grants no immunity (L128)
                OriginEventId = cast.OriginEventId,
                Behavior = puddle,
            });

            if (puddle.Owned == null)
            {
                puddle.End(ctx);
                return null;
            }

            int remaining = puddle.Owned.ExpireTick - ctx.Tick;
            ctx.Emit(SpellEventKind.FieldCreated, cast, ground, default, puddle.Id, puddle._radius, ticks: remaining);
            HalenRules.DepositWaterTicks(ctx, cast, ground, strength, puddle._radius, remaining, puddle.Owned.Id);
            HalenRules.Emit(ctx, TideEventKind.PuddleCreated, cast, ground, puddle._radius);
            return puddle;
        }

        protected internal override void Step(SpellContext ctx)
        {
            if (Owned == null || !Owned.Active)
            {
                ctx.Emit(SpellEventKind.FieldEnded, Cast, _center, default, Id);
                End(ctx);
            }
        }

        /// <summary>No contact rules of its own: the puddle's water acts through the reaction world (spec L118, L136).</summary>
        public void OnTick(DuelWorld world, OwnedEntity entity)
        {
        }

        protected override void HashState(ref StateHasher h)
        {
            h.Add(_center);
            h.Add(_radius);
            h.Add(Owned == null ? 0 : Owned.Id);
        }
    }
}
