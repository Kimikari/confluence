using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Time;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Hashing;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Status;
using Confluence.Sim.World;
using Confluence.Sim.World.Materials;

namespace Confluence.Sim.Content.Wizards.Wiz002
{
    /// <summary>
    /// SPL-011 Heat Haze (WIZ-002, defensive distortion): "A visible personal shimmer lasts 4 s; reduces incoming
    /// projectile damage 12%; does not alter aim or hitboxes." World interaction: "Heat 1 dries caster wetness once;
    /// shimmer remains visually identifiable." Mana 18, cooldown 12 s, 0.3 s windup, range 0 (self) from the record.
    /// <list type="bullet">
    /// <item>The shimmer is a timed buff (BuffStarted/BuffEnded events) whose 12% reduction applies to spell projectile
    /// damage only and joins the other passive reductions inside the 45% cap (spec L112); melee, cones, splash and field
    /// contact are not reduced ("Use melee ... or overwhelm limited reduction"). Hit volumes and aim are untouched.</item>
    /// <item>Its Heat 1 dries the caster's Wet once, at release (every Wet instance the caster carries then); standing in
    /// water re-wets them by the usual rule (spec L118).</item>
    /// <item>It deals no damage, so a WIZ-002 crest release pays 12% less mana instead (WIZ-002 mastery, D-056).</item>
    /// </list>
    /// </summary>
    [ContentBehavior("SPL-011")]
    public sealed class HeatHaze : ISpellBehavior
    {
        public static readonly ProseParameter Duration = ProseParameter.Of("shimmer_duration", 4, "A visible personal shimmer lasts 4 s");
        public static readonly ProseParameter Reduction = ProseParameter.Percent("projectile_reduction", 12, "reduces incoming projectile damage 12%");
        public static readonly ProseParameter DryHeat = ProseParameter.Of("dry_heat", 1, "Heat 1 dries caster wetness once");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Duration, Reduction, DryHeat };

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Build(SpellFormBuilder form)
        {
            form.Delivery = new BuffDelivery { DurationSec = Duration.Float, ProjectileDamageReduction = Reduction.Float };
            form.Script = HeatHazeScript.Base;
        }
    }

    /// <summary>
    /// Heat Haze's release rules: drying the caster's Wet (base), the AUG-309 steam puff when the caster was Wet, and the
    /// AUG-310 reservoir that readies a recast vent. Immutable configuration shared by every cast of one form.
    /// </summary>
    public sealed class HeatHazeScript : SpellScript
    {
        public static readonly HeatHazeScript Base = new HeatHazeScript(0f, 0f, 0f, 0f, 0f, 0);

        private HeatHazeScript(float mantleRadius, float mantleLifetimeSec, float reservoirThreshold, float ventLength, float ventHalfAngle, int ventHeat)
        {
            MantleRadius = mantleRadius;
            MantleLifetimeSec = mantleLifetimeSec;
            ReservoirThreshold = reservoirThreshold;
            VentLength = ventLength;
            VentHalfAngleDeg = ventHalfAngle;
            VentHeat = ventHeat;
        }

        /// <summary>AUG-309: steam puff radius when cast while Wet (0 = no puff).</summary>
        public float MantleRadius { get; }

        public float MantleLifetimeSec { get; }

        /// <summary>AUG-310: absorbed damage that readies the vent (0 = no reservoir).</summary>
        public float ReservoirThreshold { get; }

        public float VentLength { get; }
        public float VentHalfAngleDeg { get; }
        public int VentHeat { get; }

        public bool HasMantle => MantleRadius > 0f;
        public bool HasReservoir => ReservoirThreshold > 0f;

        public HeatHazeScript WithMantle(float radius, float lifetimeSec) =>
            new HeatHazeScript(radius, lifetimeSec, ReservoirThreshold, VentLength, VentHalfAngleDeg, VentHeat);

        public HeatHazeScript WithReservoir(float threshold, float ventLength, float ventHalfAngleDeg, int ventHeat) =>
            new HeatHazeScript(MantleRadius, MantleLifetimeSec, threshold, ventLength, ventHalfAngleDeg, ventHeat);

        public override void OnReleased(SpellContext ctx, CastRecord cast)
        {
            int caster = cast.Caster;
            DuelPlayer p = ctx.Player(caster);
            bool wasWet = p.Statuses.Has(StatusKind.Wet);

            // "Heat 1 dries caster wetness once": every Wet instance the caster carries at release.
            while (ctx.World.Dispel(caster, caster, StatusKind.Wet) != null)
            {
            }

            if (wasWet && HasMantle)
            {
                // AUG-309 "Casting while Wet consumes Wet and creates 1.5 m steam puff for 1 s": a neutral steam cloud
                // around the caster (it hides the caster's own view as well, spec L167), owned by the caster (L670).
                ReactionMaterials materials = KitSupport.Materials(ctx);
                if (materials != null)
                {
                    materials.RecordSource(cast.OriginEventId, cast.SpellId);
                    materials.CreateVolume(new VolumeSpec
                    {
                        Kind = VolumeKind.Steam,
                        Footprint = Footprint.Circle(p.Position.XZ(), MantleRadius),
                        BaseY = p.Position.Y,
                        LifetimeTicks = Ticks.FromSeconds(MantleLifetimeSec),
                        Owner = caster,
                        SourceEventId = cast.OriginEventId,
                    });
                    ctx.Emit(SpellEventKind.Steamed, cast, p.Position, default, 0, MantleRadius, ticks: Ticks.FromSeconds(MantleLifetimeSec));
                    ctx.Spawn(new MantlePuffEntity(cast, Ticks.FromSeconds(MantleLifetimeSec))); // "steam also hides caster's view"
                }
            }

            if (HasReservoir)
            {
                BuffDelivery buff = cast.Form.Delivery as BuffDelivery;
                int lifetime = Ticks.FromSeconds(buff != null ? buff.DurationSec : 0f);
                ctx.Spawn(new HazeReservoirEntity(cast, this, ctx.Tick + lifetime));
            }
        }
    }

    /// <summary>
    /// AUG-310 Haze Reservoir: "First 20 absorbed damage readies one Heat 1 vent". The damage the shimmer absorbs is the
    /// damage of the hostile hits it soaks: every hit its projectile reduction acts on (a hostile spell projectile while
    /// the shimmer lasts; <see cref="Confluence.Sim.Combat.DamageResolution.ConditionalPrevented"/> above zero) counts
    /// with the damage the caster takes from it (shield and health). Damage the haze does not mitigate (melee, cones,
    /// bursts, terrain, statuses, boundary) and the caster's own damage (spec L114, L651: no self-generated rewards, as
    /// Tempered Hands) never charge it. Counting only the prevented part (12% of a hit) would need about 167 projectile
    /// damage inside the 3 s buff (four Fireballs), which leaves the record inert in a duel. Once 20 is absorbed the
    /// spell recast applies Heat 1 to a forward cone with no damage. Before the vent is ready the cast has no recast target, so a press is refused and
    /// spends nothing (spec L94); the vent is used once.
    /// </summary>
    public sealed class HazeReservoirEntity : SpellEntity, ISpellEventListener, IAwaitsRecastTarget
    {
        private readonly HeatHazeScript _script;
        private readonly int _endTick;
        private float _absorbed;
        private bool _vented;

        internal HazeReservoirEntity(CastRecord cast, HeatHazeScript script, int endTick)
            : base(cast)
        {
            _script = script;
            _endTick = endTick;
        }

        public float Absorbed => _absorbed;
        public bool Ready => !_vented && _absorbed >= _script.ReservoirThreshold;
        public bool Vented => _vented;

        /// <summary>Only the charged vent takes the recast; before it is charged a press does nothing (spec L94).</summary>
        public override bool AcceptsRecast => Ready;

        /// <summary>The vent the recast applies is not readied yet: an early press spends nothing.</summary>
        public bool AwaitingRecastTarget => !_vented && !Ready;

        public void OnWorldEvent(DuelWorld world, DuelEvent e)
        {
            if (_vented || _absorbed >= _script.ReservoirThreshold) return;
            if (!(e is DamageEvent d) || d.Target != Owner || d.Attacker == Owner || d.Resolution == null) return;
            if (d.Resolution.ConditionalPrevented > 0f) _absorbed += d.Resolution.Dealt; // a hit the shimmer soaked
        }

        protected internal override void Step(SpellContext ctx)
        {
            if (ctx.Tick >= _endTick || !ctx.Player(Owner).Alive) End(ctx);
        }

        protected internal override void OnRecast(SpellContext ctx)
        {
            if (!Ready) return;
            _vented = true;
            DuelPlayer p = ctx.Player(Owner);
            Vec3 dir = KitSupport.Flat(ctx.AimDirection(Owner), CMath.ForwardFromYaw(p.Motor.YawDeg));
            ctx.Emit(SpellEventKind.ConeReleased, Cast, p.Position, dir, Id, _script.VentLength);
            Footprint cone = Footprint.Cone(p.Position.XZ(), dir.XZ(), _script.VentLength, _script.VentHalfAngleDeg);
            KitSupport.Deposit(ctx, Cast, DepositKind.Heat, _script.VentHeat, p.Position, cone); // no damage
        }

        protected override void HashState(ref StateHasher h)
        {
            h.Add(_endTick);
            h.Add(_absorbed);
            h.Add(_vented);
        }
    }

    /// <summary>
    /// AUG-309's drawback "steam also hides caster's view": while the caster's eye is inside their own mantle puff, they
    /// cannot acquire a target whose chest is outside it, whatever the steam path length (the 1.5 m rank 1 puff is
    /// shorter than the 2 m L167 threshold from its centre, so the shared rule alone would never hide the caster's view;
    /// the record states it does). A target inside the same puff stays visible ("close range"). Outside viewers keep the
    /// ordinary L167 rule: the puff blocks lines that cross 2 m of it. The entity follows the puff's reaction-world
    /// volume (wind may move it) and ends with it.
    /// </summary>
    public sealed class MantlePuffEntity : SpellEntity, IAcquisitionVeil
    {
        private readonly int _searchTicks;
        private int _volumeId;

        internal MantlePuffEntity(CastRecord cast, int lifetimeTicks)
            : base(cast)
        {
            _searchTicks = Math.Max(2, lifetimeTicks);
        }

        public int VolumeId => _volumeId;

        protected internal override void Step(SpellContext ctx)
        {
            ReactionMaterials materials = KitSupport.Materials(ctx);
            if (materials == null)
            {
                End(ctx);
                return;
            }

            if (_volumeId == 0)
            {
                ObscurationVolume found = Find(materials.World);
                if (found != null) _volumeId = found.Id;
                else if (ctx.Tick - SpawnTick > _searchTicks) End(ctx);
                return;
            }

            if (materials.World.FindVolume(_volumeId) == null) End(ctx); // faded, dispersed or condensed
        }

        public bool HidesView(DuelWorld world, int viewer, int target)
        {
            if (viewer != Owner || viewer == target) return false;
            ReactionMaterials materials = world.Materials as ReactionMaterials;
            ObscurationVolume puff = materials == null ? null : _volumeId != 0 ? materials.World.FindVolume(_volumeId) : Find(materials.World);
            if (puff == null || !puff.ContainsPoint(world.Player(viewer).Eye)) return false;
            DuelPlayer t = world.Player(target);
            Vec3 chest = t.Position + new Vec3(0f, t.Motor.Height * 0.6f, 0f);
            return !puff.ContainsPoint(chest);
        }

        private ObscurationVolume Find(Confluence.Reactions.ReactionWorld world)
        {
            IReadOnlyList<ObscurationVolume> volumes = world.Volumes;
            for (int i = 0; i < volumes.Count; i++)
            {
                ObscurationVolume v = volumes[i];
                if (v.Kind == VolumeKind.Steam && v.Owner == Owner && v.SourceEventId == Cast.OriginEventId) return v;
            }

            return null;
        }

        protected override void HashState(ref StateHasher h)
        {
            h.Add(_volumeId);
        }
    }
}
