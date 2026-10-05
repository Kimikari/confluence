using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Reactions;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Spells;
using Confluence.Sim.World;
using Confluence.Sim.World.Materials;

namespace Confluence.Sim.Content.Wizards.HalenTide
{
    /// <summary>
    /// SPL-080 Seal the Spring (WIZ-010, anti-field utility): "A 2 m-radius splash removes up to 2 reaction units from one
    /// visible hot/corrosive ground field; requires a surface impact." World interaction "Water 2 is fully consumed in
    /// neutralization; no surviving pool." Mana 26, cooldown 14 s, 0.6 s windup, 18 m range (spec L525).
    /// <list type="bullet">
    /// <item>The splash lands where the aim first meets a solid surface within 18 m (the ground, a wall, a platform); with
    /// no surface in range there is no impact and the spent cast does nothing ("requires a surface impact").</item>
    /// <item>"one ... ground field": the connected patch of hot (heat) or corrosive (venom) cells nearest the impact inside
    /// the 2 m splash; up to 2 units are removed from its splash cells, nearest first, heat before venom in a cell, through
    /// the reaction world's neutralization (spec L161 neutral removal; L165 direct-impact phase).</item>
    /// <item>"visible": the splash reaches only cells with no solid between the impact and the cell (a wall shields the
    /// far side), and the field must have a cell the caster sees with clear sight (<see cref="SpringSight"/>).</item>
    /// <item>"Water 2 is fully consumed ... no surviving pool": the splash deposits no water at all.</item>
    /// </list>
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class SealTheSpring : ISpellBehavior
    {
        public const string Id = "SPL-080";

        public static readonly ProseParameter Radius = ProseParameter.Of("splash_radius", 2, "A 2 m-radius splash");
        public static readonly ProseParameter Units = ProseParameter.Of("units_removed", 2, "removes up to 2 reaction units");
        public static readonly ProseParameter Water = ProseParameter.Of("water_consumed", 2, "Water 2 is fully consumed in neutralization");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Radius, Units, Water };

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Build(SpellFormBuilder form)
        {
            form.Delivery = new SpringSplashDelivery { Radius = Radius.Float, MaxUnits = (int)Units.Value };
        }
    }

    /// <summary>What AUG-448 Spring Steam adds to either Seal the Spring delivery.</summary>
    public interface ISpringNeutralizer
    {
        /// <summary>AUG-448: steam radius left where heat is neutralized; 0 = none.</summary>
        float SteamRadius { get; set; }

        float SteamLifetimeSec { get; set; }

        /// <summary>AUG-448 drawback "No effect on corrosion unless actual neutralization": venom goes only with heat.</summary>
        bool CorrosionNeedsHeat { get; set; }
    }

    /// <summary>A planned neutralization: cells and the units to take from each (heat first).</summary>
    internal struct SpringRemoval
    {
        public int Cell;
        public int Heat;
        public int Venom;
    }

    /// <summary>Shared neutralization logic of the SPL-080 deliveries.</summary>
    internal static class SpringNeutralization
    {
        public static bool IsHotOrCorrosive(CellUnits u) => u.Heat > 0 || u.Venom > 0;

        /// <summary>Queues the planned removals and the AUG-448 steam; returns the units removed.</summary>
        public static int Apply(SpellContext ctx, CastRecord cast, ISpringNeutralizer spec, List<SpringRemoval> plan, Vec3 steamAt)
        {
            ReactionMaterials materials = HalenRules.Materials(ctx);
            if (materials == null || plan.Count == 0) return 0;
            ReactionWorld world = materials.World;
            int heat = 0;
            for (int i = 0; i < plan.Count; i++) heat += plan[i].Heat;
            if (spec.CorrosionNeedsHeat && heat == 0) return 0; // AUG-448 drawback: corrosion alone is untouched

            int removed = 0;
            materials.RecordSource(cast.OriginEventId, cast.SpellId);
            for (int i = 0; i < plan.Count; i++)
            {
                SpringRemoval r = plan[i];
                int take = r.Heat + r.Venom;
                if (take <= 0) continue;
                materials.RemoveUnits(new UnitRemoval
                {
                    Footprint = Footprint.Point(world.Grid.CellCenter(r.Cell)),
                    Materials = new[] { Material.Heat, Material.Venom },
                    MaxPerCell = take,
                    MaxTotal = take,
                    Owner = cast.Caster,
                    SourceEventId = cast.OriginEventId,
                });
                removed += take;
            }

            // AUG-448 "Neutralizing Heat leaves 1.5 m steam for 2 s; Water 2 still consumed."
            if (heat > 0 && spec.SteamRadius > 0f)
            {
                HalenRules.CreateCloud(ctx, cast, VolumeKind.Steam, steamAt, spec.SteamRadius, spec.SteamLifetimeSec, 0);
            }

            return removed;
        }

        public static float Distance2(ReactionWorld world, int cell, Vec2 point) => Vec2.DistanceSquared(world.Grid.CellCenter(cell), point);
    }

    /// <summary>
    /// SPL-080's splash: the aim's first solid surface within range is the impact; the nearest connected hot/corrosive patch
    /// inside the splash loses up to its unit budget.
    /// </summary>
    public sealed class SpringSplashDelivery : DeliverySpec, ISpringNeutralizer
    {
        private float _radius;
        private int _maxUnits;
        private float _steamRadius;
        private float _steamLifetimeSec;
        private bool _corrosionNeedsHeat;

        public override DeliveryKind Kind => DeliveryKind.Ray;

        public float Radius { get => _radius; set => _radius = Set(value); }

        public int MaxUnits { get => _maxUnits; set => _maxUnits = Set(value); }

        public float SteamRadius { get => _steamRadius; set => _steamRadius = Set(value); }

        public float SteamLifetimeSec { get => _steamLifetimeSec; set => _steamLifetimeSec = Set(value); }

        public bool CorrosionNeedsHeat { get => _corrosionNeedsHeat; set => _corrosionNeedsHeat = Set(value); }

        internal override void ApplyGlobalCaps()
        {
            _maxUnits = Math.Max(0, _maxUnits);
            _steamLifetimeSec = Math.Min(Math.Max(0f, _steamLifetimeSec), Confluence.Core.Rules.GlobalRules.FieldsAndSummons.MaxLifetimeSec);
        }

        public override Vec3 PredictTarget(SpellContext ctx, SpellForm form, int caster) => ctx.AimPoint(caster, form.Range);

        /// <summary>The splash's surface impact for the caster's aim, or false when no surface is within range.</summary>
        public static bool SurfaceImpact(SpellContext ctx, int caster, Vec3 aim, float range, out Vec3 impact) =>
            SurfaceImpact(ctx, caster, aim, range, out impact, out _);

        /// <summary>As <see cref="SurfaceImpact(SpellContext,int,Vec3,float,out Vec3)"/>, with the struck surface's normal.</summary>
        public static bool SurfaceImpact(SpellContext ctx, int caster, Vec3 aim, float range, out Vec3 impact, out Vec3 normal)
        {
            Vec3 origin = ctx.AimOrigin(caster);
            if (ctx.Collision.Raycast(origin, aim, range, SolidMask.Movement, out WorldHit hit))
            {
                impact = hit.Point;
                normal = hit.Normal.LengthSquared() > 1e-6f ? hit.Normal : -aim;
                return true;
            }

            impact = origin + aim * range;
            normal = -aim;
            return false;
        }

        public override void Release(SpellContext ctx, CastRecord cast)
        {
            Vec3 aim = AimDirection(ctx, cast);
            if (!SurfaceImpact(ctx, cast.Caster, aim, cast.Form.Range, out Vec3 impact, out Vec3 normal))
            {
                HalenRules.Emit(ctx, TideEventKind.SpringNoSurface, cast, impact);
                return;
            }

            ReactionWorld world = HalenRules.Reactions(ctx);
            int removed = 0;
            if (world != null)
            {
                var sight = new SpringSight(ctx, world, impact, normal, ctx.AimOrigin(cast.Caster));
                List<SpringRemoval> plan = Plan(world, HalenRules.Ground(ctx.Collision, impact).XZ(), sight);
                removed = SpringNeutralization.Apply(ctx, cast, this, plan, impact);
            }

            HalenRules.Emit(ctx, TideEventKind.SpringNeutralized, cast, impact, removed);
        }

        /// <summary>
        /// The removals for a splash centered on a ground point: the 4-connected hot/corrosive patch of the splash cell
        /// nearest the point, up to <see cref="MaxUnits"/> units from its cells nearest first, heat before venom.
        /// With a <paramref name="sight"/>, only cells the splash reaches from its impact without crossing a solid belong
        /// to the splash, and the field must be visible from the caster's eye ("one visible hot/corrosive ground field").
        /// </summary>
        internal List<SpringRemoval> Plan(ReactionWorld world, Vec2 center, SpringSight sight = null)
        {
            var plan = new List<SpringRemoval>();
            List<int> cells = HalenRules.Cells(world, Footprint.Circle(center, Radius));
            var inSplash = new HashSet<int>();
            var candidates = new List<int>();
            for (int i = 0; i < cells.Count; i++)
            {
                if (sight != null && !sight.Reaches(cells[i])) continue; // a solid between the impact and the cell shields it
                inSplash.Add(cells[i]);
                if (SpringNeutralization.IsHotOrCorrosive(world.UnitsAtCell(cells[i]))) candidates.Add(cells[i]);
            }

            // Nearest hot/corrosive splash cell first (cell index breaks ties), so the nearest visible field is the one.
            candidates.Sort((a, b) =>
            {
                int c = SpringNeutralization.Distance2(world, a, center).CompareTo(SpringNeutralization.Distance2(world, b, center));
                return c != 0 ? c : a.CompareTo(b);
            });

            var seen = new HashSet<int>();
            var neighbors = new List<int>();
            List<int> patch = null;
            for (int s = 0; s < candidates.Count && patch == null; s++)
            {
                int seed = candidates[s];
                if (!seen.Add(seed)) continue;

                // The field: the connected hot/corrosive patch of the seed, limited to the splash.
                var field = new List<int>();
                var queue = new Queue<int>();
                queue.Enqueue(seed);
                bool visible = sight == null;
                while (queue.Count > 0)
                {
                    int c = queue.Dequeue();
                    field.Add(c);
                    if (!visible && sight.SeenByCaster(c)) visible = true;
                    neighbors.Clear();
                    world.Grid.Neighbors4(c, neighbors);
                    for (int n = 0; n < neighbors.Count; n++)
                    {
                        int next = neighbors[n];
                        if (!inSplash.Contains(next) || !SpringNeutralization.IsHotOrCorrosive(world.UnitsAtCell(next)) || !seen.Add(next)) continue;
                        queue.Enqueue(next);
                    }
                }

                if (visible) patch = field;
            }

            if (patch == null) return plan;

            patch.Sort((a, b) =>
            {
                int c = SpringNeutralization.Distance2(world, a, center).CompareTo(SpringNeutralization.Distance2(world, b, center));
                return c != 0 ? c : a.CompareTo(b);
            });

            int budget = MaxUnits;
            for (int i = 0; i < patch.Count && budget > 0; i++)
            {
                CellUnits u = world.UnitsAtCell(patch[i]);
                int heat = Math.Min(u.Heat, budget);
                budget -= heat;
                int venom = Math.Min(u.Venom, budget);
                budget -= venom;
                if (heat + venom > 0) plan.Add(new SpringRemoval { Cell = patch[i], Heat = heat, Venom = venom });
            }

            return plan;
        }
    }

    /// <summary>
    /// SPL-080 "one visible hot/corrosive ground field" (decision): a ground cell belongs to the splash only when the
    /// segment from the impact (lifted 5 cm off the struck surface) to the cell's ground point crosses no solid, so a wall
    /// shields the field behind it; and the chosen field must have at least one cell the caster's eye sees with clear
    /// sight (no sight-blocking solid, less than the obscuring limit of steam/mist, spec L167).
    /// </summary>
    internal sealed class SpringSight
    {
        /// <summary>Cell ground points are tested this far above the ground (decision: a field is seen on its surface).</summary>
        public const float SurfaceLift = 0.1f;

        private readonly SpellContext _ctx;
        private readonly ReactionWorld _world;
        private readonly Vec3 _from;
        private readonly Vec3 _eye;
        private readonly float _probeY;

        public SpringSight(SpellContext ctx, ReactionWorld world, Vec3 impact, Vec3 normal, Vec3 eye)
        {
            _ctx = ctx;
            _world = world;
            _from = impact + normal.NormalizedOr(Vec3.Up) * 0.05f;
            _eye = eye;
            _probeY = HalenRules.GroundY(ctx.Collision, impact) + 1f;
        }

        /// <summary>The surface point of a ground cell (the walkable height under its center).</summary>
        public Vec3 CellPoint(int cell)
        {
            Vec2 c = _world.Grid.CellCenter(cell);
            float y = HalenRules.GroundY(_ctx.Collision, new Vec3(c.X, _probeY, c.Y));
            return new Vec3(c.X, y + SurfaceLift, c.Y);
        }

        /// <summary>Whether the splash reaches the cell from its impact without crossing a solid.</summary>
        public bool Reaches(int cell)
        {
            Vec3 to = CellPoint(cell);
            return !_ctx.Collision.SegmentCast(_from, to, SolidMask.Movement, out WorldHit hit) || hit.Distance >= (to - _from).Length() - 0.02f;
        }

        /// <summary>Whether the caster's eye sees the cell's surface.</summary>
        public bool SeenByCaster(int cell) => _ctx.World.HasClearSight(_eye, CellPoint(cell));
    }

    /// <summary>
    /// AUG-447 Spring Fan: a ground cone from the caster's feet along the aim removing one hot/corrosive unit in every cell
    /// it covers (heat first). The record gives no angle, so the framework cone default (90 degrees, D-145 style) applies.
    /// </summary>
    public sealed class SpringConeDelivery : DeliverySpec, ISpringNeutralizer
    {
        private float _reach;
        private float _halfAngleDeg = 45f;
        private int _perCell = 1;
        private float _steamRadius;
        private float _steamLifetimeSec;
        private bool _corrosionNeedsHeat;

        public override DeliveryKind Kind => DeliveryKind.Cone;

        public float Reach { get => _reach; set => _reach = Set(value); }

        public float HalfAngleDeg { get => _halfAngleDeg; set => _halfAngleDeg = Set(value); }

        public int PerCell { get => _perCell; set => _perCell = Set(value); }

        public float SteamRadius { get => _steamRadius; set => _steamRadius = Set(value); }

        public float SteamLifetimeSec { get => _steamLifetimeSec; set => _steamLifetimeSec = Set(value); }

        public bool CorrosionNeedsHeat { get => _corrosionNeedsHeat; set => _corrosionNeedsHeat = Set(value); }

        internal override void ApplyGlobalCaps()
        {
            _halfAngleDeg = CMath.Clamp(_halfAngleDeg, 0f, 180f);
            _reach = Math.Max(0f, _reach);
            _perCell = Math.Max(0, _perCell);
            _steamLifetimeSec = Math.Min(Math.Max(0f, _steamLifetimeSec), Confluence.Core.Rules.GlobalRules.FieldsAndSummons.MaxLifetimeSec);
        }

        public override Vec3 PredictTarget(SpellContext ctx, SpellForm form, int caster) => ctx.Player(caster).Position;

        /// <summary>The cone's ground footprint for the caster's position and an aim.</summary>
        public Footprint FootprintFor(SpellContext ctx, int caster, Vec3 aim)
        {
            Vec2 flat = new Vec2(aim.X, aim.Z);
            if (flat.LengthSquared() < 1e-8f) flat = CMath.ForwardFromYaw(ctx.Player(caster).Motor.YawDeg).XZ();
            return Footprint.Cone(ctx.Player(caster).Position.XZ(), flat.Normalized(), Reach, HalfAngleDeg);
        }

        public override void Release(SpellContext ctx, CastRecord cast)
        {
            Vec3 aim = AimDirection(ctx, cast);
            ctx.Emit(SpellEventKind.ConeReleased, cast, ctx.Player(cast.Caster).Position, new Vec3(aim.X, 0f, aim.Z), 0, Reach);
            ReactionWorld world = HalenRules.Reactions(ctx);
            if (world == null) return;
            Vec2 apex = ctx.Player(cast.Caster).Position.XZ();
            List<int> cells = HalenRules.Cells(world, FootprintFor(ctx, cast.Caster, aim));
            var plan = new List<SpringRemoval>();
            int nearestHeat = -1;
            float bestHeat = float.MaxValue;
            for (int i = 0; i < cells.Count; i++)
            {
                CellUnits u = world.UnitsAtCell(cells[i]);
                if (!SpringNeutralization.IsHotOrCorrosive(u)) continue;
                int heat = Math.Min(u.Heat, PerCell);
                int venom = Math.Min(u.Venom, PerCell - heat);
                plan.Add(new SpringRemoval { Cell = cells[i], Heat = heat, Venom = venom });
                float d = SpringNeutralization.Distance2(world, cells[i], apex);
                if (heat > 0 && d < bestHeat - 1e-6f)
                {
                    bestHeat = d;
                    nearestHeat = cells[i];
                }
            }

            Vec3 steamAt = nearestHeat >= 0 ? world.Grid.CellCenter(nearestHeat).ToXZ(ctx.Player(cast.Caster).Position.Y) : ctx.Player(cast.Caster).Position;
            int removed = SpringNeutralization.Apply(ctx, cast, this, plan, steamAt);
            HalenRules.Emit(ctx, TideEventKind.SpringNeutralized, cast, steamAt, removed);
        }
    }
}
