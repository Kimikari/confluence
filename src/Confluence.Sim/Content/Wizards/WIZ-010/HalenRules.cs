using System;
using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Duel;
using Confluence.Sim.Spells;
using Confluence.Sim.World;
using Confluence.Sim.World.Materials;

namespace Confluence.Sim.Content.Wizards.HalenTide
{
    /// <summary>
    /// WIZ-010 Halen Tide kit rules the records leave open, and the shared world helpers of the kit (water deposits on the
    /// ground, clouds through the reaction world). Every value cites the text it interprets; docs/notes/kit-wiz-010.md
    /// lists them for consolidation.
    /// </summary>
    public static class HalenRules
    {
        public const string WizardId = "WIZ-010";

        /// <summary>
        /// Halen's impact water whose record states no duration (SPL-073 "Water 1 wets the first impact surface", SPL-074
        /// "Water 1 sprays only the impact spot", SPL-077 "Water 1 at launch and landing", SPL-079 "Water 2 at impact",
        /// SPL-075 "Water 1 when broken") stays for 3 s. Decision: an instant residue (lifetime 0) would erase the "wet"
        /// surface in the same step, so it could never be the "Wet wall" AUG-433 banks from, and SPL-080's explicit "no
        /// surviving pool" implies Halen's water normally survives; 3 s is the kit's own shortest authored puddle
        /// (AUG-435, AUG-441 "for 3 s") and the steam base lifetime of spec L144.
        /// </summary>
        public const float ImpactWaterLifetimeSec = 3f;

        /// <summary>
        /// SPL-078 "everyone moving with its arrow gets +15% speed ... crosswise travel has no speed bonus": a body moves
        /// with the arrow when its horizontal travel is within 45 degrees of the arrow (decision: the half-way angle
        /// between "with" and "crosswise").
        /// </summary>
        public const float WithArrowToleranceDeg = 45f;

        /// <summary>A body below this horizontal speed is standing, not moving with the arrow (decision).</summary>
        public const float MovingMinSpeed = 0.5f;

        /// <summary>
        /// AUG-443 "Stream splits into two 6 m lanes" states no angle: the two lanes leave their shared junction 30
        /// degrees either side of the aim (decision).
        /// </summary>
        public const float ForkHalfAngleDeg = 30f;

        /// <summary>
        /// AUG-440 "two 1.5 m-radius pools separated 2-4 m": the dry gap between the pool edges is the middle of the stated
        /// band (decision), side by side across the aim around the aimed point.
        /// </summary>
        public const float PoolDivideGap = 3f;

        /// <summary>
        /// WIZ-010 signature "Enemies and caster within it are slowed 15%": the eddy re-applies its slow every 0.25 s for
        /// 0.3 s, so the slow lasts while a body is inside and ends within 0.3 s of leaving (decision).
        /// </summary>
        public const float EddySlowIntervalSec = 0.25f;

        public const float EddySlowDurationSec = 0.3f;

        /// <summary>
        /// The bubble of SPL-075 sits in front of its holder: its break water and steam puffs appear this far ahead of the
        /// holder's feet along the facing (decision; the shield protects the forward arc).
        /// </summary>
        public const float BubbleFrontOffset = 0.6f;

        /// <summary>SPL-075's base steam puff states no lifetime: the spec L144 base steam lifetime (3 s).</summary>
        public const float BubbleSteamLifetimeSec = GlobalRules.World.SteamLifetimeSec;

        /// <summary>Clouds rise from their ground like steam (spec L144 "2 meter rise").</summary>
        public const float CloudHeight = GlobalRules.World.SteamRise;

        /// <summary>The kit's reaction world, or null when the duel runs other materials (tests without reactions).</summary>
        public static ReactionMaterials Materials(SpellContext ctx) => ctx.World.Materials as ReactionMaterials;

        public static ReactionWorld Reactions(SpellContext ctx) => Materials(ctx)?.World;

        /// <summary>The walkable surface height below a point (the point's own height when nothing is below).</summary>
        public static float GroundY(CollisionWorld collision, Vec3 point)
        {
            Vec3 probe = point + new Vec3(0f, 0.05f, 0f);
            return collision.Raycast(probe, Vec3.Down, 30f, SolidMask.Movement, out WorldHit hit) ? hit.Point.Y : point.Y;
        }

        /// <summary>The ground point below a point.</summary>
        public static Vec3 Ground(CollisionWorld collision, Vec3 point) => new Vec3(point.X, GroundY(collision, point), point.Z);

        /// <summary>
        /// Queues Halen water on the ground below a point (spec L136, L163): a circle of the radius (0 = the point's cell),
        /// owned by the caster's committed source, lasting the given time. The surface height is the ground's, so a wet
        /// cell stays touchable by a standing body (spec L118, D-150).
        /// </summary>
        public static void DepositWater(SpellContext ctx, CastRecord cast, Vec3 point, int strength, float radius, float lifetimeSec, int fieldId = 0) =>
            DepositWaterTicks(ctx, cast, point, strength, radius, Ticks.FromSeconds(lifetimeSec), fieldId);

        /// <summary>As <see cref="DepositWater"/> with the lifetime in ticks (a field's remaining lifetime).</summary>
        public static void DepositWaterTicks(SpellContext ctx, CastRecord cast, Vec3 point, int strength, float radius, int lifetimeTicks, int fieldId = 0)
        {
            if (strength <= 0) return;
            Vec3 ground = Ground(ctx.Collision, point);
            ctx.World.QueueDeposit(new WorldDeposit
            {
                Kind = DepositKind.Water,
                Strength = Math.Min(strength, GlobalRules.World.MaxUnitsPerCell),
                Position = ground,
                Radius = Math.Max(0f, radius),
                SourcePlayer = cast.Caster,
                SourceId = cast.SpellId,
                OriginEventId = cast.OriginEventId,
                LifetimeTicks = Math.Max(0, lifetimeTicks),
                Footprint = radius > 0f ? Footprint.Circle(ground.XZ(), radius) : Footprint.Point(ground.XZ()),
                HasFootprint = true,
                FieldId = fieldId,
            });
        }

        /// <summary>
        /// Creates a cloud (steam or mist) over the ground below a point through the reaction world (spec L134, L167). The
        /// cloud belongs to the caster's committed source, so it counts against the caster's field limit (spec L128, L670;
        /// D-149). Returns false when the duel has no reaction world.
        /// </summary>
        public static bool CreateCloud(SpellContext ctx, CastRecord cast, VolumeKind kind, Vec3 center, float radius, float lifetimeSec, int sourceWater)
        {
            ReactionMaterials materials = Materials(ctx);
            if (materials == null || !(radius > 0f) || !(lifetimeSec > 0f)) return false;
            Vec3 ground = Ground(ctx.Collision, center);
            materials.RecordSource(cast.OriginEventId, cast.SpellId);
            materials.CreateVolume(new VolumeSpec
            {
                Kind = kind,
                Footprint = Footprint.Circle(ground.XZ(), radius),
                BaseY = ground.Y,
                Height = CloudHeight,
                LifetimeTicks = Ticks.FromSeconds(lifetimeSec),
                Owner = cast.Caster,
                SourceEventId = cast.OriginEventId,
                SourceWater = Math.Max(0, sourceWater),
            });
            return true;
        }

        /// <summary>Removes water units from one cell (AUG-433 "consumes Water 1 on wall").</summary>
        public static bool RemoveWater(SpellContext ctx, CastRecord cast, Vec3 point, int units)
        {
            ReactionMaterials materials = Materials(ctx);
            if (materials == null || units <= 0) return false;
            materials.RemoveUnits(new UnitRemoval
            {
                Footprint = Footprint.Point(point.XZ()),
                Materials = new[] { Material.Water },
                MaxPerCell = units,
                MaxTotal = units,
                Owner = cast.Caster,
                SourceEventId = cast.OriginEventId,
            });
            return true;
        }

        /// <summary>Whether a body's feet stand in liquid water (spec L118; ice is not wet). True without a reaction world.</summary>
        public static bool StandsInWater(SpellContext ctx, Vec3 feet)
        {
            ReactionMaterials materials = Materials(ctx);
            return materials == null || materials.StandsInWater(feet);
        }

        /// <summary>Records a kit event in the duel's ordered stream.</summary>
        public static TideEvent Emit(SpellContext ctx, TideEventKind kind, CastRecord cast, Vec3 position, float amount = 0f, int target = -1)
        {
            return ctx.World.RecordEvent(new TideEvent
            {
                Kind = kind,
                SpellId = cast.SpellId,
                CastId = cast.CastId,
                Position = position,
                Amount = amount,
                TargetPlayer = target,
            }, cast.Caster);
        }

        /// <summary>The cells of a footprint in index order.</summary>
        public static List<int> Cells(ReactionWorld world, Footprint footprint)
        {
            var cells = new List<int>();
            world.Grid.Rasterize(footprint, cells);
            return cells;
        }
    }
}
