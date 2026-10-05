using System;
using System.Collections.Generic;
using System.Globalization;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Duel;
using Confluence.Sim.Hashing;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.World;

namespace Confluence.Sim.Content.Wizards.HalenTide
{
    /// <summary>
    /// SPL-078 Riptide Lane (WIZ-010, movement terrain): "Create a 10 m-long, 2 m-wide shallow stream for 6 s; everyone moving
    /// with its arrow gets +15% speed." World interaction "Water 2 along lane; crosswise travel has no speed bonus; remains
    /// connected for electricity." Mana 22, cooldown 11 s, 0.5 s windup and the 20 m placement range from the record.
    /// <list type="bullet">
    /// <item>One gameplay field (spec L128): a lane from the aimed ground point along the aim, Water 2 in every covered cell
    /// for its lifetime; its cells form one connected strip, so a charge travels along it within the L148 bounds.</item>
    /// <item>Everyone (both players, spec L529 neutral terrain) standing in its liquid water and travelling within 45
    /// degrees of the arrow gets +15% movement; crosswise or against the arrow, airborne, or on a frozen or boiled-off
    /// part of the stream gets nothing. The bonus is a stat modifier, so the global +20% movement cap holds (spec L124).</item>
    /// <item>Counterplay "Use the lane yourself or freeze/disrupt its route": the bonus follows the water, so frost (ice) or
    /// heat (steam) removes it where they act.</item>
    /// </list>
    /// </summary>
    [ContentBehavior(Id)]
    public sealed class RiptideLane : ISpellBehavior
    {
        public const string Id = "SPL-078";

        public static readonly ProseParameter Length = ProseParameter.Of("length", 10, "10 m-long");
        public static readonly ProseParameter Width = ProseParameter.Of("width", 2, "2 m-wide shallow stream");
        public static readonly ProseParameter Lifetime = ProseParameter.Of("lifetime", 6, "stream for 6 s");
        public static readonly ProseParameter Bonus = ProseParameter.Percent("speed_bonus", 15, "+15% speed");
        public static readonly ProseParameter Water = ProseParameter.Of("water", 2, "Water 2 along lane");

        private static readonly IReadOnlyList<ProseParameter> All = new[] { Length, Width, Lifetime, Bonus, Water };

        public IReadOnlyList<ProseParameter> Parameters => All;

        public void Build(SpellFormBuilder form)
        {
            form.Delivery = new StreamDelivery
            {
                Length = Length.Float,
                Width = Width.Float,
                LifetimeSec = Lifetime.Float,
                SpeedBonus = Bonus.Float,
            };

            form.Payload.AddDeposit(DepositKind.Water, (int)Water.Value, DepositPlacement.Footprint);
        }
    }

    /// <summary>
    /// A shallow stream: one lane field (or, with AUG-443, two forked lanes from one junction, each its own field) and a
    /// current that gives bodies travelling with its arrow a movement bonus. AUG-444 lets the recast reverse (or at rank 2
    /// turn by 90 degrees) the arrow once without extending the lifetime.
    /// </summary>
    public sealed class StreamDelivery : DeliverySpec, IFieldDelivery
    {
        private float _length;
        private float _width;
        private float _lifetimeSec;
        private float _bonus;
        private float _forkHalfAngleDeg;
        private bool _valve;
        private bool _valveMayTurn;
        private FieldDelivery _lane;

        public override DeliveryKind Kind => DeliveryKind.Field;

        public float Length { get => _length; set => _length = Set(value); }
        public float Width { get => _width; set => _width = Set(value); }
        public float LifetimeSec { get => _lifetimeSec; set => _lifetimeSec = Set(value); }

        /// <summary>Movement bonus fraction for bodies moving with the arrow.</summary>
        public float SpeedBonus { get => _bonus; set => _bonus = Set(value); }

        /// <summary>AUG-443: two lanes this many degrees either side of the aim from one junction; 0 = one lane.</summary>
        public float ForkHalfAngleDeg { get => _forkHalfAngleDeg; set => _forkHalfAngleDeg = Set(value); }

        /// <summary>AUG-444: the recast reverses the arrow once.</summary>
        public bool Valve { get => _valve; set => _valve = Set(value); }

        /// <summary>AUG-444 rank 2: the recast may turn the arrow 90 degrees instead.</summary>
        public bool ValveMayTurn { get => _valveMayTurn; set => _valveMayTurn = Set(value); }

        public FieldDelivery Lane => _lane;

        public int Branches => _forkHalfAngleDeg > 0f ? 2 : 1;

        // The shared field shape (Spells/Deliveries/FieldShapes.cs): one non-damaging lane from the aimed ground point
        // (with AUG-443 the first branch), so the Fieldcraft records see the stream like a framework lane field.
        FieldShape IFieldDelivery.Shape => FieldShape.Lane;

        float IFieldDelivery.Radius { get => Width * 0.5f; set => Width = value * 2f; }

        PlacementMode IFieldDelivery.Placement => PlacementMode.AtAimPoint;

        bool IFieldDelivery.IsDamaging => false;

        PayloadSpec IFieldDelivery.EnterPayload => null;

        void IFieldDelivery.FootprintAt(Vec3 ground, Vec3 aim, out Confluence.Reactions.Grid.Footprint footprint, out Vec3 center) =>
            FieldFootprints.Of(FieldShape.Lane, 0f, Width, Length, ground, Arrows(aim)[0].ToXZ(), out footprint, out center);

        internal override void ApplyGlobalCaps()
        {
            _bonus = CMath.Clamp(_bonus, 0f, GlobalRules.Modifiers.MovementBonusCap); // the stat block caps the sum too (spec L124)
            _lane = new FieldDelivery
            {
                Shape = FieldShape.Lane,
                Length = _length,
                Width = _width,
                Placement = PlacementMode.AtAimPoint,
                LifetimeSec = _lifetimeSec,
                AffectsOwner = true,
            };
            _lane.ApplyGlobalCaps();
        }

        protected override void OnFreeze() => _lane?.Freeze();

        public override Vec3 PredictTarget(SpellContext ctx, SpellForm form, int caster) => ctx.GroundPoint(caster, form.Range);

        /// <summary>The arrow (ground-plane unit direction) of each lane for an aim direction.</summary>
        public Vec2[] Arrows(Vec3 aim)
        {
            Vec2 flat = new Vec2(aim.X, aim.Z);
            Vec2 forward = flat.LengthSquared() > 1e-8f ? flat.Normalized() : new Vec2(0f, 1f);
            if (Branches == 1) return new[] { forward };
            return new[] { forward.RotatedByYaw(-ForkHalfAngleDeg).Normalized(), forward.RotatedByYaw(ForkHalfAngleDeg).Normalized() };
        }

        public override void Release(SpellContext ctx, CastRecord cast)
        {
            Vec3 start = PredictTarget(ctx, cast.Form, cast.Caster);
            Vec3 aim = AimDirection(ctx, cast);
            ctx.World.Effects.BindFieldPlacement(cast, ref start, ref aim); // a placement priced at commitment (Effects/EffectHooks.cs)
            Vec2[] arrows = Arrows(aim);
            var lanes = new List<FieldEntity>();
            var kept = new List<Vec2>();
            for (int i = 0; i < arrows.Length; i++)
            {
                FieldEntity lane = _lane.Spawn(ctx, cast, start, arrows[i].ToXZ());
                if (!lane.Alive) continue; // no field could be created
                lanes.Add(lane);
                kept.Add(arrows[i]);
            }

            if (lanes.Count > 0)
            {
                FieldEntity[] spawned = lanes.ToArray();
                ctx.Spawn(new StreamCurrentEntity(cast, this, spawned, kept.ToArray(), LaneWaterLayer.Plan(ctx, cast, spawned)));
            }
        }
    }

    /// <summary>
    /// The water each lane of a stream actually adds, per cell (AUG-444 rank 2 needs it to lift only the stream's own layer
    /// when a lane turns). Reaction cells pool units of a material (one slot per cell), so the layer is computed at the
    /// release, before the queued footprint deposit applies, with the reaction world's own L163 cap rule: a cell gains
    /// min(strength, 4 - water already there). Lanes are planned in order, so a shared junction cell is never counted twice.
    /// </summary>
    internal static class LaneWaterLayer
    {
        public static Dictionary<int, int>[] Plan(SpellContext ctx, CastRecord cast, FieldEntity[] lanes)
        {
            var layers = new Dictionary<int, int>[lanes.Length];
            Confluence.Reactions.ReactionWorld world = HalenRules.Reactions(ctx);
            int strength = StreamCurrentEntity.LaneWater(cast.Form.Payload);
            var running = new Dictionary<int, int>();
            for (int i = 0; i < lanes.Length; i++)
            {
                layers[i] = new Dictionary<int, int>();
                if (world == null || strength <= 0) continue;
                List<int> cells = HalenRules.Cells(world, lanes[i].Footprint);
                for (int c = 0; c < cells.Count; c++)
                {
                    int cell = cells[c];
                    if (layers[i].ContainsKey(cell)) continue;
                    if (!running.TryGetValue(cell, out int water)) water = world.UnitsAtCell(cell).Water;
                    int add = Math.Max(0, Math.Min(strength, GlobalRules.World.MaxUnitsPerCell - water));
                    running[cell] = water + add;
                    if (add > 0) layers[i][cell] = add;
                }
            }

            return layers;
        }
    }

    /// <summary>
    /// The current of a live stream (tick-order step 4): every tick it gives each body standing in the stream's liquid water
    /// and travelling with an arrow the stream's movement bonus (once per body, whichever lane it is in), and removes it
    /// otherwise; it ends with the stream's lanes. With AUG-444 it accepts the stream's recast.
    /// </summary>
    public sealed class StreamCurrentEntity : SpellEntity
    {
        private readonly StreamDelivery _spec;
        private readonly FieldEntity[] _lanes;
        private readonly Vec2[] _arrows;
        private readonly bool[] _boosted = new bool[DuelWorld.PlayerCount];
        private readonly string[] _keys = new string[DuelWorld.PlayerCount];
        private readonly Dictionary<int, int>[] _layers;
        private bool _valveUsed;

        internal StreamCurrentEntity(CastRecord cast, StreamDelivery spec, FieldEntity[] lanes, Vec2[] arrows, Dictionary<int, int>[] layers)
            : base(cast)
        {
            _spec = spec;
            _lanes = lanes;
            _arrows = arrows;
            _layers = layers;
            for (int i = 0; i < _keys.Length; i++)
            {
                _keys[i] = cast.SpellId + "#" + cast.CastId.Sequence.ToString(CultureInfo.InvariantCulture) + "#current" + i.ToString(CultureInfo.InvariantCulture);
            }
        }

        public IReadOnlyList<FieldEntity> Lanes => _lanes;

        /// <summary>The current arrow of a lane.</summary>
        public Vec2 Arrow(int lane) => _arrows[lane];

        public bool IsBoosted(int player) => _boosted[player];

        public bool ValveUsed => _valveUsed;

        public override bool AcceptsRecast => _spec.Valve && !_valveUsed;

        protected internal override void Step(SpellContext ctx)
        {
            bool anyLane = false;
            for (int i = 0; i < _lanes.Length; i++) anyLane |= _lanes[i].Alive;
            if (!anyLane)
            {
                End(ctx);
                return;
            }

            for (int p = 0; p < DuelWorld.PlayerCount; p++)
            {
                bool with = MovesWithArrow(ctx, p);
                if (with == _boosted[p]) continue;
                _boosted[p] = with;
                StatSheet stats = ctx.Player(p).Stats;
                if (with) stats.Add(StatKind.MovementBonus, _spec.SpeedBonus, _keys[p]);
                else stats.RemoveSource(_keys[p]);
                HalenRules.Emit(ctx, TideEventKind.StreamBonus, Cast, ctx.Player(p).Position, with ? 1f : 0f, p);
            }
        }

        /// <summary>
        /// Whether a body gets the bonus now: alive, grounded in a live lane's liquid water, and travelling within 45 degrees
        /// of that lane's arrow at walking pace or faster (SPL-078 "moving with its arrow"; "crosswise travel has no speed
        /// bonus").
        /// </summary>
        public bool MovesWithArrow(SpellContext ctx, int player)
        {
            DuelPlayer body = ctx.Player(player);
            if (!body.Alive || !body.Motor.Grounded) return false;
            Vec3 feet = body.Position;
            Vec2 travel = new Vec2(body.Motor.Velocity.X, body.Motor.Velocity.Z);
            if (travel.Length() < HalenRules.MovingMinSpeed) return false;
            if (!HalenRules.StandsInWater(ctx, feet)) return false;
            for (int i = 0; i < _lanes.Length; i++)
            {
                if (!_lanes[i].Alive || !_lanes[i].Contains(feet)) continue;
                if (CMath.AngleBetweenDeg(travel, _arrows[i]) <= HalenRules.WithArrowToleranceDeg) return true;
            }

            return false;
        }

        /// <summary>
        /// AUG-444 "Recast reverses lane direction once without extending lifetime"; rank 2 "May rotate direction 90 degrees
        /// once instead": the new arrow is whichever allowed turn points closest to the caster's aim at the recast.
        /// </summary>
        protected internal override void OnRecast(SpellContext ctx)
        {
            if (!_spec.Valve || _valveUsed) return;
            _valveUsed = true;
            Vec3 aim = ctx.AimDirection(Owner);
            Vec2 wanted = new Vec2(aim.X, aim.Z);
            float turned = 180f;
            for (int i = 0; i < _arrows.Length; i++)
            {
                Vec2 reverse = new Vec2(-_arrows[i].X, -_arrows[i].Y);
                Vec2 best = reverse;
                float angle = 180f;
                if (_spec.ValveMayTurn && wanted.LengthSquared() > 1e-8f)
                {
                    float bestScore = CMath.AngleBetweenDeg(wanted, reverse);
                    Vec2 left = _arrows[i].RotatedByYaw(-90f).Normalized();
                    Vec2 right = _arrows[i].RotatedByYaw(90f).Normalized();
                    if (CMath.AngleBetweenDeg(wanted, left) < bestScore - 1e-3f)
                    {
                        best = left;
                        bestScore = CMath.AngleBetweenDeg(wanted, left);
                        angle = 90f;
                    }

                    if (CMath.AngleBetweenDeg(wanted, right) < bestScore - 1e-3f)
                    {
                        best = right;
                        angle = 90f;
                    }
                }

                _arrows[i] = best;
                turned = angle;
                if (angle < 179f) RotateLane(ctx, i, best);
            }

            HalenRules.Emit(ctx, TideEventKind.StreamTurned, Cast, _lanes[0].Center, turned);
        }

        /// <summary>
        /// AUG-444 rank 2 "May rotate direction 90 degrees once instead" (decision): the lane itself turns with its arrow
        /// about its center, so the bonus runs along the new 8 m length (a turned arrow over the unturned 2 m width would
        /// be crosswise travel, which the record gives no bonus). The lane keeps its allocation slot, its water strength
        /// and its remaining lifetime ("without extending lifetime"): the old orientation's field and water are removed
        /// and the turned lane forms in the same step with the remaining ticks.
        /// </summary>
        private void RotateLane(SpellContext ctx, int lane, Vec2 arrow)
        {
            FieldEntity old = _lanes[lane];
            if (!old.Alive || old.Owned == null || !old.Owned.Active) return;
            int remaining = old.Owned.ExpireTick - ctx.Tick;
            if (remaining <= 0) return;

            // Free the old orientation's slot first, so the turned lane never retires another field (spec L128).
            ctx.World.RemoveOwnedEntity(old.Owned.Id, FieldChange.Retired, -1);
            RemoveOwnLayer(ctx, lane);

            FieldDelivery spec = _spec.Lane;
            var turned = new FieldDelivery
            {
                Shape = FieldShape.Lane,
                Length = spec.Length,
                Width = spec.Width,
                Placement = PlacementMode.AtAimPoint,
                LifetimeSec = remaining / (float)Ticks.PerSecond,
                AffectsOwner = spec.AffectsOwner,
            };
            turned.ApplyGlobalCaps();
            Vec3 center = old.Center;
            Vec3 start = new Vec3(center.X - arrow.X * spec.Length * 0.5f, center.Y, center.Z - arrow.Y * spec.Length * 0.5f);
            _lanes[lane] = turned.Spawn(ctx, Cast, start, arrow.ToXZ());
        }

        /// <summary>
        /// Lifts the old orientation's own water: in each cell, the units this lane added at its release that still remain
        /// (never more than the cell holds now). Water from other sources under the lane — an opponent's pool, or the
        /// cell's share the four-unit cap kept the lane from adding — stays (decision; the record removes nothing else).
        /// </summary>
        private void RemoveOwnLayer(SpellContext ctx, int lane)
        {
            Confluence.Reactions.ReactionWorld world = HalenRules.Reactions(ctx);
            Dictionary<int, int> layer = _layers[lane];
            if (world == null || layer.Count == 0) return;
            var cells = new List<int>(layer.Keys);
            cells.Sort();
            for (int i = 0; i < cells.Count; i++)
            {
                int take = Math.Min(layer[cells[i]], world.UnitsAtCell(cells[i]).Water);
                if (take > 0) HalenRules.RemoveWater(ctx, Cast, world.Grid.CellCenter(cells[i]).ToXZ(0f), take);
            }

            layer.Clear();
        }

        internal static int LaneWater(PayloadSpec payload)
        {
            for (int i = 0; i < payload.Deposits.Count; i++)
            {
                DepositSpec d = payload.Deposits[i];
                if (d.Kind == DepositKind.Water && d.Placement == DepositPlacement.Footprint) return d.Strength;
            }

            return 0;
        }

        protected override void OnEnded(SpellContext ctx)
        {
            for (int p = 0; p < DuelWorld.PlayerCount; p++)
            {
                if (!_boosted[p]) continue;
                _boosted[p] = false;
                ctx.Player(p).Stats.RemoveSource(_keys[p]);
            }
        }

        protected override void HashState(ref StateHasher h)
        {
            h.Add(_valveUsed);
            for (int i = 0; i < _arrows.Length; i++) h.Add(_arrows[i]);
            for (int i = 0; i < _boosted.Length; i++) h.Add(_boosted[i]);
            for (int i = 0; i < _layers.Length; i++)
            {
                var cells = new List<int>(_layers[i].Keys);
                cells.Sort();
                h.Add(cells.Count);
                for (int c = 0; c < cells.Count; c++)
                {
                    h.Add(cells[c]);
                    h.Add(_layers[i][cells[c]]);
                }
            }
        }
    }
}
