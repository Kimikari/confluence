using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Fields;
using Confluence.Sim.Hashing;
using Confluence.Sim.Players;
using Confluence.Sim.Props;
using Confluence.Sim.Spells;
using Confluence.Sim.World;
using Confluence.Sim.World.Materials;

namespace Confluence.Sim.Content.Wizards.Wiz002
{
    /// <summary>
    /// SPL-016 Flashforge (WIZ-002, close terrain burst): "Smash a 2 m-radius ground area; intact metal prop within it
    /// becomes red-hot for 4 s, dealing 8/s on contact." Damage "58", thermal. World interaction: "Heat 3; metal is still
    /// destructible and walkable rules remain unchanged." Counterplay: "Leave the raised casting marker; cool or avoid
    /// the hot prop." Mana 32, cooldown 16 s, 0.65 s windup, range 5 (the smash point along the aim) from the record.
    /// <list type="bullet">
    /// <item>The smash point is fixed at commitment and raised as a visible marker for the whole windup.</item>
    /// <item>Every opponent within 2 m of it (clear of cover) takes 58 thermal as a direct hit of an offensive melee
    /// spell (tags melee, close; a frontal guard toward the smash reduces it, spec L104, D-135).</item>
    /// <item>"Heat 3": each ground cell of the smashed area receives three heat units as instant residue (spec L163).</item>
    /// <item>The nearest intact prop tagged metal (catalogue tags, never art names, spec L656) whose body lies in the area
    /// turns red-hot for 4 s: one field of the caster (tags field; spec L128), 8 thermal per second to anyone touching
    /// it, owner included (spec L496). The prop keeps its durability and walkable top; destroying it, or water or frost
    /// reaching the cells under it, ends the heat early.</item>
    /// </list>
    /// </summary>
    [ContentBehavior("SPL-016")]
    public sealed class Flashforge : ISpellBehavior
    {
        public static readonly ProseParameter Radius = ProseParameter.Of("smash_radius", 2, "Smash a 2 m-radius ground area");
        public static readonly ProseParameter Damage = ProseParameter.Of("direct", 58, "58");
        public static readonly ProseParameter HotLifetime = ProseParameter.Of("red_hot_lifetime", 4, "becomes red-hot for 4 s");
        public static readonly ProseParameter HotDps = ProseParameter.Of("red_hot_dps", 8, "dealing 8/s on contact");
        public static readonly ProseParameter Heat = ProseParameter.Of("smash_heat", 3, "Heat 3");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Radius, Damage, HotLifetime, HotDps, Heat };

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Build(SpellFormBuilder form)
        {
            form.AimLock = AimLock.AtCommit; // the raised casting marker stays where the cast committed
            form.Payload.Direct = Damage.Float;
            form.Delivery = new SmashDelivery
            {
                Radius = Radius.Float,
                Heat = (int)Heat.Value,
                HotLifetimeSec = HotLifetime.Float,
                HotContact = new PayloadSpec { Channel = form.Channel, Direct = HotDps.Float, GuardEligible = false },
            };
            form.Script = SmashScript.Instance;
        }
    }

    /// <summary>The raised casting marker, emitted at commitment for the whole windup (spec L96, L549).</summary>
    internal sealed class SmashScript : SpellScript
    {
        public static readonly SmashScript Instance = new SmashScript();

        public override void OnCommitted(SpellContext ctx, CastRecord cast)
        {
            if (!(cast.Form.Delivery is SmashDelivery smash)) return;
            // The marker's ground point is stored on the cast: the smash resolves exactly there even when props move or
            // break during the 0.65 s windup ("Leave the raised casting marker"; the cue describes the delivery, L549).
            Vec3 center = SmashDelivery.CommittedPoint(ctx, cast);
            cast.SetChosenPoint(center);
            ctx.Emit(SpellEventKind.Marker, cast, center, KitSupport.Flat(cast.CommitDirection, Vec3.Forward), 0, smash.Radius,
                ticks: cast.Form.Castable.WindupTicks);
        }
    }

    /// <summary>The Flashforge smash and its AUG-319 forge plate and AUG-320 steam pull.</summary>
    public sealed class SmashDelivery : DeliverySpec
    {
        private float _radius;
        private int _heat;
        private float _hotLifetimeSec;
        private PayloadSpec _hotContact;
        private float _plateSize;
        private float _plateHealth;
        private float _pullDistance;

        public override DeliveryKind Kind => DeliveryKind.Field;

        public float Radius { get => _radius; set => _radius = Set(value); }

        /// <summary>Heat units per smashed ground cell (instant residue).</summary>
        public int Heat { get => _heat; set => _heat = Set(value); }

        /// <summary>How long the red-hot prop or forge plate stays hot.</summary>
        public float HotLifetimeSec { get => _hotLifetimeSec; set => _hotLifetimeSec = Set(value); }

        /// <summary>Contact payload of the hot object (its Direct amount once per second).</summary>
        public PayloadSpec HotContact { get => _hotContact; set => _hotContact = Set(value); }

        /// <summary>AUG-319: side of the forge plate that replaces the hot-prop selection (0 = select a prop).</summary>
        public float PlateSize { get => _plateSize; set => _plateSize = Set(value); }

        public float PlateHealth { get => _plateHealth; set => _plateHealth = Set(value); }

        /// <summary>AUG-320: loose steam is pulled this far toward the impact point once (0 = none).</summary>
        public float PullDistance { get => _pullDistance; set => _pullDistance = Set(value); }

        public bool MakesPlate => PlateSize > 0f;

        internal override void ApplyGlobalCaps()
        {
            _hotLifetimeSec = Math.Min(_hotLifetimeSec, GlobalRules.FieldsAndSummons.MaxLifetimeSec); // spec L128
            _heat = Math.Max(0, Math.Min(_heat, GlobalRules.World.MaxUnitsPerCell));
            CapNested(_hotContact);
        }

        protected override void OnFreeze() => _hotContact?.Freeze();

        public override Vec3 PredictTarget(SpellContext ctx, SpellForm form, int caster) => ctx.GroundPoint(caster, form.Range);

        /// <summary>The ground point along the aim fixed at commitment, within the record's range (same rule as placed fields).</summary>
        public static Vec3 CommittedPoint(SpellContext ctx, CastRecord cast)
        {
            Vec3 origin = cast.CommitOrigin;
            Vec3 dir = cast.CommitDirection;
            float range = cast.Form.Range;
            Vec3 aim = ctx.Collision.Raycast(origin, dir, range, SolidMask.Movement, out WorldHit hit) ? hit.Point : origin + dir * range;
            Vec3 probe = aim - dir * 0.05f + new Vec3(0f, 0.05f, 0f);
            if (ctx.Collision.Raycast(probe, Vec3.Down, 30f, SolidMask.Movement, out WorldHit ground)) return ground.Point;
            return new Vec3(aim.X, ctx.Player(cast.Caster).Position.Y, aim.Z);
        }

        public override void Release(SpellContext ctx, CastRecord cast)
        {
            Vec3 center = cast.HasChosenPoint ? cast.ChosenPoint : CommittedPoint(ctx, cast);
            Vec3 aim = KitSupport.Flat(cast.CommitDirection, Vec3.Forward);
            Smash(ctx, cast, center);
            KitSupport.Deposit(ctx, cast, DepositKind.Heat, Heat, center, Footprint.Circle(center.XZ(), Radius)); // "Heat 3"
            if (PullDistance > 0f) PullSteam(ctx, cast, center);
            if (MakesPlate) ForgePlateEntity.Create(ctx, cast, this, center, aim);
            else HeatProp(ctx, cast, center);
        }

        private void Smash(SpellContext ctx, CastRecord cast, Vec3 center)
        {
            ctx.Emit(SpellEventKind.AreaBurst, cast, center, default, 0, Radius);
            Vec3 from = center + new Vec3(0f, 0.3f, 0f);
            for (int i = 0; i < DuelWorld.PlayerCount; i++)
            {
                if (i == cast.Caster || !ctx.IsTargetable(i)) continue;
                Capsule body = ctx.Player(i).HitVolume;
                float bottom = body.A.Y - body.Radius;
                if (bottom > center.Y + 1.5f || body.B.Y + body.Radius < center.Y - 0.5f) continue; // a ground smash
                Vec3 axis = body.ClosestPointOnAxis(from);
                if (Vec3.DistanceXZ(axis, center) - body.Radius > Radius) continue;
                if (!ctx.HasClearPath(from, axis)) continue;
                ctx.ApplyToPlayer(cast, cast.Form.Payload, i, PayloadHit.Direct(center, KitSupport.Flat(body.Center - center, Vec3.Forward)));
            }

            IReadOnlyList<ISpellBody> bodies = ctx.HostileBodies(cast.Caster);
            for (int i = 0; i < bodies.Count; i++)
            {
                ISpellBody b = bodies[i];
                if (b.IsIntact && Vec3.DistanceXZ(b.BodyCenter, center) - b.BodyRadius <= Radius) ctx.ApplyToBody(cast, cast.Form.Payload, b, CastHitKinds.Direct);
            }
        }

        // "intact metal prop within it becomes red-hot": the nearest one (ties: lowest placement id).
        private void HeatProp(SpellContext ctx, CastRecord cast, Vec3 center)
        {
            PropBody best = null;
            float bestDistance = float.PositiveInfinity;
            IReadOnlyList<PropBody> props = ctx.World.Props.Props;
            for (int i = 0; i < props.Count; i++)
            {
                PropBody prop = props[i];
                if (prop.Destroyed || !IsMetal(ctx, prop)) continue;
                Vec2 closest = prop.Box.ClosestPointXZ(center.XZ());
                float d = Vec2.Distance(closest, center.XZ());
                if (d > Radius || d > bestDistance || (d == bestDistance && best != null && prop.Id > best.Id)) continue;
                best = prop;
                bestDistance = d;
            }

            if (best != null) HotPropEntity.Create(ctx, cast, this, best);
        }

        /// <summary>A prop is metal when its catalogue record carries the metal tag.</summary>
        public static bool IsMetal(SpellContext ctx, PropBody prop) =>
            ctx.World.Catalogue != null && ctx.World.Catalogue.TryGetProp(prop.DefinitionId, out PropDef def) && def.HasTag("metal");

        /// <summary>
        /// AUG-320 "Burst pulls loose steam inward 2 m to impact point once, no player pull": every steam cloud touching
        /// the burst area widened by the pull distance moves toward the impact point by at most that distance (never past
        /// it), at the wind rule's 3 m/s (spec L151); its lifetime is unchanged.
        /// </summary>
        private void PullSteam(SpellContext ctx, CastRecord cast, Vec3 center)
        {
            ReactionMaterials materials = KitSupport.Materials(ctx);
            if (materials == null) return;
            float reach = Radius + PullDistance;
            IReadOnlyList<ObscurationVolume> volumes = materials.World.Volumes;
            float speed = GlobalRules.World.WindTranslateMaxSpeed;
            for (int i = 0; i < volumes.Count; i++)
            {
                ObscurationVolume v = volumes[i];
                if (v.Kind != VolumeKind.Steam || !v.WindMovable) continue;
                v.GetBounds(out Vec2 min, out Vec2 max);
                Vec2 c = (min + max) * 0.5f;
                Vec2 nearest = Vec2.Min(Vec2.Max(center.XZ(), min), max);
                if (Vec2.Distance(nearest, center.XZ()) > reach) continue;
                Vec2 toCenter = center.XZ() - c;
                float distance = Math.Min(PullDistance, toCenter.Length());
                if (!(distance > 0.01f)) continue;
                materials.Wind(new WindGust
                {
                    TargetVolumeId = v.Id,
                    Direction = toCenter.Normalized(),
                    Strength = 1,
                    LocalCloudOverride = true, // the record's own pull (D-023 local prose); no dispersal
                    MaxDistance = distance,
                    Speed = speed,
                    DurationTicks = (int)Math.Ceiling(distance / speed * Ticks.PerSecond) + 1,
                    Owner = cast.Caster,
                    SourceEventId = cast.OriginEventId,
                });
                ctx.Emit(SpellEventKind.CloudMoved, cast, new Vec3(c.X, v.BaseY, c.Y), toCenter.Normalized().ToXZ(), v.Id, distance);
            }
        }
    }

    /// <summary>
    /// Shared rules of Flashforge's hot objects: one field of the caster, contact damage once per second to anyone
    /// touching it (owner included), cooled early by water or frost reaching the cells under it.
    /// </summary>
    public abstract class HotObjectEntity : SpellEntity, IOwnedEntityBehavior
    {
        private readonly int[] _nextHit = new int[DuelWorld.PlayerCount];
        private readonly List<int> _cells = new List<int>();
        protected readonly SmashDelivery Spec;
        protected readonly SpellContext Ctx;
        private OwnedEntity _owned;
        private bool _cooled;

        protected HotObjectEntity(SpellContext ctx, CastRecord cast, SmashDelivery spec)
            : base(cast)
        {
            Ctx = ctx;
            Spec = spec;
        }

        public OwnedEntity Owned => _owned;

        /// <summary>The heat ended before its lifetime (cooled, or its anchor broke).</summary>
        public bool Cooled => _cooled;

        /// <summary>The ground footprint under the object (cooling test).</summary>
        protected abstract Footprint GroundFootprint { get; }

        protected abstract Vec3 Center { get; }

        /// <summary>Whether a player touches the hot object now.</summary>
        protected abstract bool Touches(DuelPlayer p);

        /// <summary>The object no longer exists (a destroyed prop or plate).</summary>
        protected abstract bool AnchorLost { get; }

        protected void Register(float radius)
        {
            _owned = Ctx.World.CreateOwnedEntity(new OwnedEntitySpec
            {
                Kind = OwnedEntityKind.Field,
                Owner = Owner,
                DefinitionId = SpellId,
                LifetimeTicks = Ticks.FromSeconds(Spec.HotLifetimeSec),
                Position = Center,
                Radius = radius,
                Neutral = true,
                OriginEventId = Cast.OriginEventId,
                Behavior = this,
            });
            if (_owned == null)
            {
                End(Ctx);
                return;
            }

            for (int i = 0; i < _nextHit.Length; i++) _nextHit[i] = Ctx.Tick;
            Ctx.Emit(SpellEventKind.Heated, Cast, Center, default, Id, Spec.HotContact.Direct, ticks: _owned.ExpireTick - Ctx.Tick);
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
            if (AnchorLost || IsCooled())
            {
                _cooled = true;
                world.RemoveOwnedEntity(_owned.Id, FieldChange.Expired, -1);
                return;
            }

            int now = world.Tick;
            for (int i = 0; i < DuelWorld.PlayerCount; i++)
            {
                if (!Ctx.IsTargetable(i) || now < _nextHit[i] || !Touches(world.Player(i))) continue;
                _nextHit[i] = now + Ticks.PerSecond; // "8/s": once per second of contact (D-045 cadence)
                Vec3 feet = world.Player(i).Position;
                // Touching hot metal is environmental contact (spec L672): attributed to the cast, no hit rewards.
                PayloadHit contact = PayloadHit.Area(Center, feet - Center);
                contact.Environmental = true;
                contact.Reaction = ReactionTag.Burn;
                Ctx.ApplyToPlayer(Cast, Spec.HotContact, i, contact);
            }
        }

        // "cool ... the hot prop": liquid water, frost, or ice in a cell under the object takes its heat.
        private bool IsCooled()
        {
            ReactionWorld world = KitSupport.Reactions(Ctx);
            if (world == null) return false;
            _cells.Clear();
            world.Grid.Rasterize(GroundFootprint, _cells);
            for (int i = 0; i < _cells.Count; i++)
            {
                Confluence.Reactions.CellUnits u = world.UnitsAtCell(_cells[i]);
                if (world.IsWetCell(_cells[i]) || u.Frost > 0) return true;
                Vec2 c = world.Grid.CellCenter(_cells[i]);
                if (world.HasIceAt(new Vec3(c.X, 0f, c.Y))) return true;
            }

            return false;
        }

        protected override void HashState(ref StateHasher h)
        {
            h.Add(_owned == null ? 0 : _owned.Id);
            h.Add(_cooled);
            for (int i = 0; i < _nextHit.Length; i++) h.Add(_nextHit[i]);
            HashObject(ref h);
        }

        protected abstract void HashObject(ref StateHasher h);
    }

    /// <summary>A red-hot metal prop (SPL-016): it follows the prop's body; destroying the prop ends the heat.</summary>
    public sealed class HotPropEntity : HotObjectEntity
    {
        private const float ContactTolerance = 0.1f;

        private HotPropEntity(SpellContext ctx, CastRecord cast, SmashDelivery spec, PropBody prop)
            : base(ctx, cast, spec)
        {
            Prop = prop;
        }

        public PropBody Prop { get; }

        protected override Vec3 Center => Prop.Box.Center;

        protected override Footprint GroundFootprint =>
            Footprint.Rectangle(Prop.Box.Center.XZ(), new Vec2(Prop.Box.HalfExtents.X, Prop.Box.HalfExtents.Z), Prop.Box.YawDeg);

        protected override bool AnchorLost => Prop.Destroyed;

        internal static HotPropEntity Create(SpellContext ctx, CastRecord cast, SmashDelivery spec, PropBody prop)
        {
            var hot = ctx.Spawn(new HotPropEntity(ctx, cast, spec, prop));
            YawBox box = prop.Box;
            hot.Register(Math.Max(box.HalfExtents.X, box.HalfExtents.Z));
            return hot.Alive ? hot : null;
        }

        // Touching: the body's capsule within a few centimeters of the prop box, standing on top included (walkable rules
        // remain unchanged).
        protected override bool Touches(DuelPlayer p)
        {
            Capsule c = p.HitVolume;
            YawBox box = Prop.Box;
            float bottom = c.A.Y - c.Radius;
            float top = c.B.Y + c.Radius;
            var axis = new Vec3(p.Position.X, CMath.Clamp(box.Center.Y, bottom, top), p.Position.Z);
            float reach = c.Radius + ContactTolerance;
            return box.DistanceSquared(axis) <= reach * reach;
        }

        protected override void HashObject(ref StateHasher h) => h.Add(Prop.Id);
    }

    /// <summary>
    /// AUG-319 Forge Plate: a 2 m metal plate on the smashed ground (no collision: it "never blocks routes"), hot for its
    /// lifetime, damaging whoever stands on it; a destructible body (spec L527) that breaks at 0 health or cools in water.
    /// </summary>
    public sealed class ForgePlateEntity : HotObjectEntity, ISpellBody
    {
        private readonly Vec3 _center;
        private readonly float _yaw;
        private float _health;

        private ForgePlateEntity(SpellContext ctx, CastRecord cast, SmashDelivery spec, Vec3 center, float yawDeg)
            : base(ctx, cast, spec)
        {
            _center = center;
            _yaw = yawDeg;
            _health = spec.PlateHealth;
        }

        public float Health => _health;
        public Footprint Plate => Footprint.Rectangle(_center.XZ(), new Vec2(Spec.PlateSize * 0.5f, Spec.PlateSize * 0.5f), _yaw);

        protected override Vec3 Center => _center;
        protected override Footprint GroundFootprint => Plate;
        protected override bool AnchorLost => !(_health > 0f);

        public bool IsIntact => Alive && _health > 0f && Owned != null && Owned.Active;
        public Vec3 BodyCenter => _center + new Vec3(0f, 0.05f, 0f);
        public float BodyRadius => Spec.PlateSize * 0.5f;

        internal static ForgePlateEntity Create(SpellContext ctx, CastRecord cast, SmashDelivery spec, Vec3 center, Vec3 aim)
        {
            var plate = ctx.Spawn(new ForgePlateEntity(ctx, cast, spec, center, CMath.YawOf(aim)));
            plate.Register(spec.PlateSize * 0.71f);
            if (!plate.Alive) return null;
            ctx.Emit(SpellEventKind.DeployablePlaced, cast, center, aim, plate.Id, spec.PlateHealth, ticks: Ticks.FromSeconds(spec.HotLifetimeSec));
            return plate;
        }

        public bool TakeDamage(SpellContext ctx, float amount, int attacker)
        {
            if (!IsIntact || !(amount > 0f)) return false;
            _health -= amount;
            if (_health > 0f) return false;
            ctx.Emit(SpellEventKind.DeployableDestroyed, Cast, _center, default, Id, targetPlayer: attacker);
            return true; // the field ends at the next contact step (its anchor is lost)
        }

        // Standing on the plate: feet inside its footprint, within a step of its surface.
        protected override bool Touches(DuelPlayer p) =>
            p.Motor.Grounded && Math.Abs(p.Position.Y - _center.Y) <= KitSupport.StandHeight && Plate.Contains(p.Position.XZ());

        protected override void HashObject(ref StateHasher h)
        {
            h.Add(_center);
            h.Add(_health);
        }
    }
}
