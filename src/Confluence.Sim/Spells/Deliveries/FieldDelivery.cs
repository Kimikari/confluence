using System;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions.Grid;
using Confluence.Sim.Duel;
using Confluence.Sim.Fields;
using Confluence.Sim.Hashing;

namespace Confluence.Sim.Spells
{
    public enum FieldShape
    {
        Circle,

        /// <summary>A lane from the placement point along the aim ("A 10 m-by-2 m lane").</summary>
        Lane,

        /// <summary>A rectangle centered on the placement point, its length along the aim ("A 4 m-by-2 m field").</summary>
        Rectangle,
    }

    /// <summary>Where a placed delivery appears.</summary>
    public enum PlacementMode
    {
        /// <summary>Centered on the caster's feet ("A 3 m-radius field lasts 6 s" around the caster).</summary>
        AtCaster,

        /// <summary>On the ground along the aim within the form's range ("Place a ... field").</summary>
        AtAimPoint,
    }

    /// <summary>
    /// A gameplay field (spec L128): a circle (optionally with a safe center), lane, or rectangle footprint on the ground
    /// that lives at most 8 s and applies enter,
    /// per-interval, and exit payloads to every player inside, the owner included unless the record binds it
    /// (spec L128 "ownership ... does not grant immunity"; L529 target filters). It is a player-owned entity, so the
    /// three-field limit retires the owner's oldest field with a dissolution event. Its material deposits cover the
    /// footprint on creation, one unit per covered cell by default (spec L163), and belong to the field for dispels.
    /// Field contact is area damage: never guarded (spec L104 "guard does not stop ground fields").
    /// </summary>
    public sealed class FieldDelivery : DeliverySpec, IFieldDelivery
    {
        private FieldShape _shape;
        private float _radius;
        private float _innerRadius;
        private float _length;
        private float _width;
        private PlacementMode _placement = PlacementMode.AtAimPoint;
        private float _lifetimeSec;
        private float _tickIntervalSec = 0.5f;
        private float _firstTickDelaySec;
        private float _height = 2.5f;
        private bool _affectsOwner = true;
        private PayloadSpec _enter;
        private PayloadSpec _tick;
        private PayloadSpec _exit;

        public override DeliveryKind Kind => DeliveryKind.Field;

        public FieldShape Shape { get => _shape; set => _shape = Set(value); }
        public float Radius { get => _radius; set => _radius = Set(value); }

        /// <summary>A safe center for circles: ring fields ("center remains clear") and storm eyes; 0 = none.</summary>
        public float InnerRadius { get => _innerRadius; set => _innerRadius = Set(value); }

        public float Length { get => _length; set => _length = Set(value); }
        public float Width { get => _width; set => _width = Set(value); }
        public PlacementMode Placement { get => _placement; set => _placement = Set(value); }

        /// <summary>Lifetime in seconds; at most 8 s (spec L128).</summary>
        public float LifetimeSec { get => _lifetimeSec; set => _lifetimeSec = Set(value); }

        /// <summary>Seconds between tick payloads for each player inside ("ticks each 0.5 s").</summary>
        public float TickIntervalSec { get => _tickIntervalSec; set => _tickIntervalSec = Set(value); }

        /// <summary>Delay before an entrant's first tick (SPL-127 "entering requires 0.5 s before first tick").</summary>
        public float FirstTickDelaySec { get => _firstTickDelaySec; set => _firstTickDelaySec = Set(value); }

        /// <summary>Height of the field's volume above its ground, in meters.</summary>
        public float Height { get => _height; set => _height = Set(value); }

        /// <summary>Neutral fields affect both players (default); a bound construct can exclude its owner.</summary>
        public bool AffectsOwner { get => _affectsOwner; set => _affectsOwner = Set(value); }

        public PayloadSpec EnterPayload { get => _enter; set => _enter = Set(value); }
        public PayloadSpec TickPayload { get => _tick; set => _tick = Set(value); }
        public PayloadSpec ExitPayload { get => _exit; set => _exit = Set(value); }

        // The shared field shape (Spells/Deliveries/FieldShapes.cs) for the Fieldcraft records.
        bool IFieldDelivery.IsDamaging => Deals(_enter) || Deals(_tick) || Deals(_exit);

        void IFieldDelivery.FootprintAt(Vec3 ground, Vec3 aim, out Footprint footprint, out Vec3 center) =>
            FieldFootprints.Of(Shape, Radius, Width, Length, ground, aim, out footprint, out center);

        private static bool Deals(PayloadSpec p) => p != null && (p.Direct > 0f || p.Splash > 0f);

        public int LifetimeTicks => Ticks.FromSeconds(LifetimeSec);

        internal override void ApplyGlobalCaps()
        {
            _lifetimeSec = Math.Min(_lifetimeSec, GlobalRules.FieldsAndSummons.MaxLifetimeSec); // spec L128
            if (!(_lifetimeSec > 0f)) throw new InvalidOperationException("A field needs a positive lifetime (spec L128).");
            _tickIntervalSec = Math.Max(Ticks.Dt, _tickIntervalSec);
            CapNested(_enter);
            CapNested(_tick);
            CapNested(_exit);
        }

        protected override void OnFreeze()
        {
            _enter?.Freeze();
            _tick?.Freeze();
            _exit?.Freeze();
        }

        public override Vec3 PredictTarget(SpellContext ctx, SpellForm form, int caster) =>
            Placement == PlacementMode.AtCaster ? ctx.Player(caster).Position : ctx.GroundPoint(caster, form.Range);

        public override void Release(SpellContext ctx, CastRecord cast)
        {
            Vec3 center = PredictTarget(ctx, cast.Form, cast.Caster);
            Vec3 aim = AimDirection(ctx, cast);
            ctx.World.Effects.BindFieldPlacement(cast, ref center, ref aim); // a placement priced at commitment (Effects/EffectHooks.cs)
            Spawn(ctx, cast, center, new Vec3(aim.X, 0f, aim.Z));
        }

        /// <summary>Creates the field at a point (also used by deliveries that leave a field behind).</summary>
        public FieldEntity Spawn(SpellContext ctx, CastRecord cast, Vec3 ground, Vec3 direction)
        {
            Vec2 dir = direction.XZ().LengthSquared() > 1e-8f ? direction.XZ().Normalized() : new Vec2(0f, 1f);
            Footprint footprint;
            Vec3 center;
            float bound;
            if (Shape == FieldShape.Rectangle)
            {
                footprint = Footprint.Rectangle(ground.XZ(), new Vec2(Width * 0.5f, Length * 0.5f), CMath.YawOf(dir));
                center = ground;
                bound = (float)Math.Sqrt(Length * Length * 0.25f + Width * Width * 0.25f);
            }
            else if (Shape == FieldShape.Lane)
            {
                Vec2 from = ground.XZ();
                Vec2 to = from + dir * Length;
                footprint = Footprint.Lane(from, to, Width);
                Vec2 mid = (from + to) * 0.5f;
                center = new Vec3(mid.X, ground.Y, mid.Y);
                bound = (float)Math.Sqrt(Length * Length * 0.25f + Width * Width * 0.25f);
            }
            else
            {
                footprint = Footprint.Circle(ground.XZ(), Radius);
                center = ground;
                bound = Radius;
            }

            var entity = new FieldEntity(cast, this, ctx, footprint, center, bound);
            ctx.Spawn(entity);
            entity.Create();
            return entity;
        }
    }

    /// <summary>A live field (see <see cref="FieldDelivery"/>). Contact rules run in tick-order step 6.</summary>
    public sealed class FieldEntity : SpellEntity, IOwnedEntityBehavior, IFieldBody
    {
        private readonly FieldDelivery _spec;
        private readonly SpellContext _ctx;
        private readonly bool[] _inside = new bool[DuelWorld.PlayerCount];
        private readonly int[] _nextTick = new int[DuelWorld.PlayerCount];

        internal FieldEntity(CastRecord cast, FieldDelivery spec, SpellContext ctx, Footprint footprint, Vec3 center, float boundRadius)
            : base(cast)
        {
            _spec = spec;
            _ctx = ctx;
            Footprint = footprint;
            Center = center;
            BoundRadius = boundRadius;
        }

        public FieldDelivery Spec => _spec;
        public Footprint Footprint { get; }
        public Vec3 Center { get; }
        public float BoundRadius { get; }

        /// <summary>The ownership-registry entry that counts against the three-field limit (spec L128).</summary>
        public OwnedEntity Owned { get; private set; }

        bool IFieldBody.HasAppeared => true; // created with its footprint at release

        // A lane runs from its placement point along the aim (Footprint.Lane: a rectangle whose length lies along its yaw).
        Vec2 IFieldBody.PlacementPoint => _spec.Shape == FieldShape.Lane ? Footprint.A - Vec2.FromYaw(Footprint.YawDeg) * Footprint.HalfSize.Y : Footprint.A;

        public bool IsInside(int player) => _inside[player];

        internal void Create()
        {
            Owned = _ctx.World.CreateOwnedEntity(new OwnedEntitySpec
            {
                Kind = OwnedEntityKind.Field,
                Owner = Owner,
                DefinitionId = SpellId,
                LifetimeTicks = _spec.LifetimeTicks,
                Position = Center,
                Radius = BoundRadius,
                Neutral = _spec.AffectsOwner,
                OriginEventId = Cast.OriginEventId,
                Behavior = this,
            });

            if (Owned == null)
            {
                End(_ctx);
                return;
            }

            _ctx.Emit(SpellEventKind.FieldCreated, Cast, Center, default, Id, BoundRadius, ticks: Owned.ExpireTick - _ctx.Tick);
            _ctx.EmitDeposits(Cast, Cast.Form.Payload, DepositPlacement.Footprint, Center, default, Footprint, Owned.Id, Owned.ExpireTick - _ctx.Tick);
        }

        public bool Contains(Vec3 feet)
        {
            if (feet.Y < Center.Y - 0.5f || feet.Y > Center.Y + _spec.Height || !Footprint.Contains(feet.XZ())) return false;
            return !(_spec.InnerRadius > 0f) || Vec3.DistanceXZ(feet, Center) >= _spec.InnerRadius;
        }

        protected internal override void Step(SpellContext ctx)
        {
            if (Owned == null || !Owned.Active)
            {
                ctx.Emit(SpellEventKind.FieldEnded, Cast, Center, default, Id);
                End(ctx);
            }
        }

        public void OnTick(DuelWorld world, OwnedEntity entity)
        {
            if (!Alive) return;
            int now = world.Tick;
            int interval = Ticks.FromSeconds(_spec.TickIntervalSec);
            for (int i = 0; i < DuelWorld.PlayerCount; i++)
            {
                bool inside = (i != Owner || _spec.AffectsOwner) && _ctx.IsTargetable(i) && Contains(world.Player(i).Position);
                Vec3 feet = world.Player(i).Position;
                if (inside && !_inside[i])
                {
                    _inside[i] = true;
                    _nextTick[i] = now + Ticks.FromSeconds(_spec.FirstTickDelaySec);
                    _ctx.Emit(SpellEventKind.FieldEntered, Cast, feet, default, Id, targetPlayer: i);
                    if (_spec.EnterPayload != null) _ctx.ApplyToPlayer(Cast, _spec.EnterPayload, i, PayloadHit.Area(Center, feet - Center));
                }
                else if (!inside && _inside[i])
                {
                    _inside[i] = false;
                    _ctx.Emit(SpellEventKind.FieldExited, Cast, feet, default, Id, targetPlayer: i);
                    if (_spec.ExitPayload != null) _ctx.ApplyToPlayer(Cast, _spec.ExitPayload, i, PayloadHit.Area(Center, feet - Center));
                }

                if (inside && _spec.TickPayload != null && now >= _nextTick[i])
                {
                    _nextTick[i] = now + interval;
                    _ctx.ApplyToPlayer(Cast, _spec.TickPayload, i, PayloadHit.Area(Center, feet - Center));
                }
            }
        }

        protected override void HashState(ref StateHasher h)
        {
            h.Add(Center);
            h.Add(Owned == null ? 0 : Owned.Id);
            for (int i = 0; i < _inside.Length; i++)
            {
                h.Add(_inside[i]);
                h.Add(_nextTick[i]);
            }
        }
    }
}
