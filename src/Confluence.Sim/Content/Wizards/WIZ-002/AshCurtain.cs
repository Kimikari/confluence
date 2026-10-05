using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions;
using Confluence.Reactions.Events;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Hashing;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.World.Materials;

namespace Confluence.Sim.Content.Wizards.Wiz002
{
    /// <summary>
    /// SPL-012 Ash Curtain (WIZ-002, concealment field): "Create a 3 m-radius opaque ash cloud for 6 s; no damage or
    /// reveal inside." World interaction: "Wind displaces it; water collapses it; ash does not absorb projectiles."
    /// Counterplay: "Listen, shoot through, or disperse the cloud." Mana 20, cooldown 11 s, 0.5 s windup, range 16 (the
    /// placement distance along the aim) from the record (spec L525).
    /// <list type="bullet">
    /// <item>The cloud is a server-owned ash volume in the duel's reaction world (spec L134): opaque, so two meters of
    /// path through it block target acquisition both ways (L167); it deals no damage, reveals nothing, never stops a
    /// projectile and never mutes sound (L167). It counts as one of the caster's three fields (spec L128, L670).</item>
    /// <item>Wind moves it under the global wind rule (L151; ash is wind-movable). Liquid water under any part of the
    /// cloud collapses it: the volume is consumed ("water collapses it").</item>
    /// <item>It deals no damage, so a crest release pays 12% less mana.</item>
    /// </list>
    /// </summary>
    [ContentBehavior("SPL-012")]
    public sealed class AshCurtain : ISpellBehavior
    {
        public static readonly ProseParameter Radius = ProseParameter.Of("cloud_radius", 3, "Create a 3 m-radius opaque ash cloud");
        public static readonly ProseParameter Lifetime = ProseParameter.Of("cloud_lifetime", 6, "opaque ash cloud for 6 s");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Radius, Lifetime };

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Build(SpellFormBuilder form)
        {
            form.Delivery = new AshCloudDelivery { Radius = Radius.Float, LifetimeSec = Lifetime.Float };
        }
    }

    /// <summary>The ash cloud placement and its AUG-311 curtain shape and AUG-312 recast push.</summary>
    public sealed class AshCloudDelivery : DeliverySpec
    {
        private float _radius;
        private float _lifetimeSec;
        private float _curtainLength;
        private float _curtainWidth;
        private float _pushDistance;

        public override DeliveryKind Kind => DeliveryKind.Field;

        public float Radius { get => _radius; set => _radius = Set(value); }
        public float LifetimeSec { get => _lifetimeSec; set => _lifetimeSec = Set(value); }

        /// <summary>AUG-311: a curtain of this length across the aim (0 = the 3 m-radius cloud).</summary>
        public float CurtainLength { get => _curtainLength; set => _curtainLength = Set(value); }

        public float CurtainWidth { get => _curtainWidth; set => _curtainWidth = Set(value); }

        /// <summary>AUG-312: the recast pushes the cloud this far in the aimed direction (0 = no recast push).</summary>
        public float PushDistance { get => _pushDistance; set => _pushDistance = Set(value); }

        public bool IsCurtain => CurtainLength > 0f;

        internal override void ApplyGlobalCaps()
        {
            _lifetimeSec = Math.Min(_lifetimeSec, GlobalRules.FieldsAndSummons.MaxLifetimeSec); // spec L128
            if (!(_lifetimeSec > 0f)) throw new InvalidOperationException("Ash Curtain needs a positive lifetime.");
        }

        public override Vec3 PredictTarget(SpellContext ctx, SpellForm form, int caster) => ctx.GroundPoint(caster, form.Range);

        /// <summary>The cloud's ground footprint at a placement point for an aim direction.</summary>
        public Footprint FootprintAt(Vec3 ground, Vec3 aim)
        {
            if (!IsCurtain) return Footprint.Circle(ground.XZ(), Radius);
            float yaw = CMath.YawOf(KitSupport.Flat(aim, Vec3.Forward));
            return Footprint.Rectangle(ground.XZ(), new Vec2(CurtainLength * 0.5f, CurtainWidth * 0.5f), yaw); // long side across the aim
        }

        public override void Release(SpellContext ctx, CastRecord cast)
        {
            Vec3 ground = ctx.GroundPoint(cast.Caster, cast.Form.Range);
            Vec3 aim = AimDirection(ctx, cast);
            ReactionMaterials materials = KitSupport.Materials(ctx);
            if (materials == null)
            {
                ctx.Emit(SpellEventKind.FieldEnded, cast, ground, aim); // no reaction world: no cloud can exist
                return;
            }

            materials.RecordSource(cast.OriginEventId, cast.SpellId);
            materials.CreateVolume(new VolumeSpec
            {
                Kind = VolumeKind.Ash,
                Footprint = FootprintAt(ground, aim),
                BaseY = ground.Y,
                LifetimeTicks = Ticks.FromSeconds(LifetimeSec),
                Owner = cast.Caster,
                SourceEventId = cast.OriginEventId,
            });
            ctx.Spawn(new AshCloudEntity(cast, this, ground));
        }
    }

    /// <summary>
    /// The live ash cloud: follows its reaction-world volume (created at step 5 of the release tick), collapses it when
    /// liquid water lies under any of its columns, and performs the AUG-312 recast push.
    /// </summary>
    public sealed class AshCloudEntity : SpellEntity
    {
        private const int SearchTicks = 2;

        private readonly AshCloudDelivery _spec;
        private readonly List<int> _xs = new List<int>();
        private readonly List<int> _zs = new List<int>();
        private int _volumeId;
        private bool _pushed;
        private bool _collapsed;

        internal AshCloudEntity(CastRecord cast, AshCloudDelivery spec, Vec3 ground)
            : base(cast)
        {
            _spec = spec;
            Ground = ground;
        }

        public Vec3 Ground { get; }

        /// <summary>The cloud's reaction-world volume id (0 before it exists).</summary>
        public int VolumeId => _volumeId;

        public bool Pushed => _pushed;
        public bool Collapsed => _collapsed;

        public override bool AcceptsRecast => _spec.PushDistance > 0f;

        protected internal override void Step(SpellContext ctx)
        {
            ReactionWorld world = KitSupport.Reactions(ctx);
            if (world == null)
            {
                End(ctx);
                return;
            }

            if (_volumeId == 0 && !Bind(ctx, world))
            {
                if (ctx.Tick - SpawnTick > SearchTicks) End(ctx); // refused (no columns) or retired at once
                return;
            }

            ObscurationVolume volume = world.FindVolume(_volumeId);
            if (volume == null)
            {
                ctx.Emit(SpellEventKind.FieldEnded, Cast, Ground, default, Id); // expired, dispersed, dispelled, or retired
                End(ctx);
                return;
            }

            if (UnderWater(world, volume))
            {
                // "water collapses it": the whole cloud is consumed (the retirement reaches the field books next step).
                _collapsed = true;
                world.RetireVolume(volume.Id, VolumeRetireReason.Consumed);
                ctx.Emit(SpellEventKind.FieldEnded, Cast, Ground, default, Id, 1f);
                End(ctx);
            }
        }

        protected internal override void OnRecast(SpellContext ctx)
        {
            // AUG-312 "Recast pushes existing ash cloud 2 m in manually chosen direction": the caster's current aim; the
            // push never changes the cloud's lifetime.
            // The recast is processed (and its 6 mana paid) in step 3, before this entity's own step 4 binds the volume
            // created at step 5 of the release tick: resolve the existing cloud here, so a recast on the first tick after
            // the cloud appeared pushes it.
            ReactionMaterials materials = KitSupport.Materials(ctx);
            if (_pushed || materials == null) return;
            if (_volumeId == 0) Bind(ctx, materials.World);
            if (_volumeId == 0 || materials.World.FindVolume(_volumeId) == null) return;
            _pushed = true;
            DuelPlayer p = ctx.Player(Owner);
            Vec3 dir = KitSupport.Flat(ctx.AimDirection(Owner), CMath.ForwardFromYaw(p.Motor.YawDeg));
            float speed = GlobalRules.World.WindTranslateMaxSpeed;
            materials.Wind(new WindGust
            {
                TargetVolumeId = _volumeId,
                Direction = dir.XZ(),
                Strength = 1,
                LocalCloudOverride = true, // the record's own manual push moves the cloud (D-023 local prose)
                MaxDistance = _spec.PushDistance,
                Speed = speed,
                DurationTicks = (int)Math.Ceiling(_spec.PushDistance / speed * Ticks.PerSecond) + 1,
                Owner = Owner,
                SourceEventId = Cast.OriginEventId,
            });
            ctx.Emit(SpellEventKind.CloudMoved, Cast, Ground, dir, Id, _spec.PushDistance);
        }

        // Binds the cloud's reaction-world volume once it exists and announces the field.
        private bool Bind(SpellContext ctx, ReactionWorld world)
        {
            ObscurationVolume found = Find(world);
            if (found == null) return false;
            _volumeId = found.Id;
            ctx.Emit(SpellEventKind.FieldCreated, Cast, Ground, default, Id, _spec.IsCurtain ? _spec.CurtainLength * 0.5f : _spec.Radius,
                ticks: found.RemainingTicks(world.Tick));
            return true;
        }

        private ObscurationVolume Find(ReactionWorld world)
        {
            IReadOnlyList<ObscurationVolume> volumes = world.Volumes;
            for (int i = 0; i < volumes.Count; i++)
            {
                ObscurationVolume v = volumes[i];
                if (v.Kind == VolumeKind.Ash && v.Owner == Owner && v.SourceEventId == Cast.OriginEventId) return v;
            }

            return null;
        }

        private bool UnderWater(ReactionWorld world, ObscurationVolume volume)
        {
            _xs.Clear();
            _zs.Clear();
            volume.Columns.GetColumns(_xs, _zs);
            for (int i = 0; i < _xs.Count; i++)
            {
                Vec2 c = volume.ColumnCenter(_xs[i], _zs[i]);
                if (world.IsWetAt(new Vec3(c.X, volume.BaseY, c.Y))) return true;
            }

            return false;
        }

        protected override void HashState(ref StateHasher h)
        {
            h.Add(_volumeId);
            h.Add(_pushed);
            h.Add(_collapsed);
        }
    }
}
