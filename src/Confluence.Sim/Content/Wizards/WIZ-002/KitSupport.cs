using System;
using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Reactions;
using Confluence.Reactions.Grid;
using Confluence.Sim.Duel;
using Confluence.Sim.Spells;
using Confluence.Sim.World;
using Confluence.Sim.World.Materials;

namespace Confluence.Sim.Content.Wizards.Wiz002
{
    /// <summary>
    /// Shared helpers of the WIZ-002 Bram Cinder kit: access to the duel's reaction world (spells degrade to their
    /// non-material behavior when a duel runs without one), ground probes, and area bursts.
    /// </summary>
    internal static class KitSupport
    {
        public const string WizardId = "WIZ-002";

        /// <summary>Bodies count as standing on a cell when their feet are within this height of it (spec L175 step height).</summary>
        public const float StandHeight = 0.35f;

        /// <summary>The duel's reaction materials, or null when it runs without the reactions stage.</summary>
        public static ReactionMaterials Materials(SpellContext ctx) => ctx.World.Materials as ReactionMaterials;

        /// <summary>The duel's reaction world, or null.</summary>
        public static ReactionWorld Reactions(SpellContext ctx) => Materials(ctx)?.World;

        /// <summary>The first walkable surface straight below a point (or the point itself when nothing is below).</summary>
        public static Vec3 GroundBelow(SpellContext ctx, Vec3 point)
        {
            Vec3 probe = point + new Vec3(0f, 0.05f, 0f);
            return ctx.Collision.Raycast(probe, Vec3.Down, 30f, SolidMask.Movement, out WorldHit hit) ? hit.Point : point;
        }

        /// <summary>Liquid water (not ice) in the cell under a point (spec L118, L144).</summary>
        public static bool IsWet(SpellContext ctx, Vec3 point)
        {
            ReactionWorld world = Reactions(ctx);
            return world != null && world.IsWetAt(point);
        }

        /// <summary>Material units in the cell under a point (zero without a reaction world).</summary>
        public static CellUnits UnitsAt(SpellContext ctx, Vec3 point)
        {
            ReactionWorld world = Reactions(ctx);
            return world != null ? world.UnitsAt(point) : default;
        }

        /// <summary>Whether any cell of a footprint still holds heat (a heat strip that has not been cooled everywhere).</summary>
        public static bool AnyHeat(ReactionWorld world, Footprint footprint, List<int> scratch)
        {
            scratch.Clear();
            world.Grid.Rasterize(footprint, scratch);
            for (int i = 0; i < scratch.Count; i++)
            {
                if (world.UnitsAtCell(scratch[i]).Heat > 0) return true;
            }

            return false;
        }

        /// <summary>Horizontal unit direction of a vector, or the fallback.</summary>
        public static Vec3 Flat(Vec3 v, Vec3 fallback)
        {
            var flat = new Vec3(v.X, 0f, v.Z);
            return flat.LengthSquared() > 1e-8f ? flat.Normalized() : fallback;
        }

        /// <summary>
        /// An area burst around a point: every targetable player (the caster only when asked) whose hit volume lies within
        /// the radius, and every hostile spell body inside it, takes the payload once. Solid cover between the burst and a
        /// body stops it (spec L120). <paramref name="kind"/> Splash reads the payload's Splash amount, Direct its Direct
        /// amount; <paramref name="scale"/> multiplies it (the cast's one mastery result when the burst is the record's
        /// primary damage, spec L480, L508). A burst is never projectile damage (projectile-only reductions such as
        /// SPL-011's skip it); <paramref name="secondary"/> marks new secondary damage a transformation adds (generation 1,
        /// no hit or proc rewards, spec L126, L508). Returns the players damaged.
        /// </summary>
        public static int Burst(SpellContext ctx, CastRecord cast, PayloadSpec payload, Vec3 center, float radius, CastHitKinds kind, float scale,
            Vec3 hitSource, bool includeCaster = false, bool secondary = false)
        {
            if (payload == null || !(radius > 0f)) return 0;
            ctx.Emit(SpellEventKind.AreaBurst, cast, center, default, 0, radius);
            Vec3 from = center + new Vec3(0f, 0.3f, 0f);
            int hits = 0;
            for (int i = 0; i < DuelWorld.PlayerCount; i++)
            {
                if ((i == cast.Caster && !includeCaster) || !ctx.IsTargetable(i)) continue;
                Capsule body = ctx.Player(i).HitVolume;
                if (SpellContext.DistanceToBody(center, body) > radius) continue;
                if (!ctx.HasClearPath(from, body.ClosestPointOnAxis(from))) continue;
                PayloadHit hit = kind == CastHitKinds.Direct
                    ? PayloadHit.Direct(hitSource, Flat(body.Center - hitSource, Vec3.Forward))
                    : PayloadHit.Splash(center);
                hit.Scale = scale > 0f ? scale : 1f;
                hit.NotProjectile = true;
                hit.Secondary = secondary;
                if (ctx.ApplyToPlayer(cast, payload, i, hit) != null) hits++;
            }

            IReadOnlyList<ISpellBody> bodies = ctx.HostileBodies(cast.Caster);
            for (int i = 0; i < bodies.Count; i++)
            {
                ISpellBody b = bodies[i];
                if (b.IsIntact && Vec3.Distance(center, b.BodyCenter) - b.BodyRadius <= radius && ctx.HasClearPath(from, b.BodyCenter))
                {
                    ctx.ApplyToBody(cast, payload, b, kind, scale);
                }
            }

            return hits;
        }

        /// <summary>A world deposit of one element over a footprint (instant residue unless a lifetime is given).</summary>
        public static void Deposit(SpellContext ctx, CastRecord cast, DepositKind kind, int strength, Vec3 at, Footprint footprint,
            int lifetimeTicks = 0, int fieldId = 0)
        {
            if (strength <= 0) return;
            ctx.World.QueueDeposit(new WorldDeposit
            {
                Kind = kind,
                Strength = Math.Min(strength, Core.Rules.GlobalRules.World.MaxUnitsPerCell),
                Position = at,
                SourcePlayer = cast.Caster,
                SourceId = cast.SpellId,
                OriginEventId = cast.OriginEventId,
                Footprint = footprint,
                HasFootprint = true,
                LifetimeTicks = Math.Max(0, lifetimeTicks),
                FieldId = fieldId,
            });
        }
    }
}
