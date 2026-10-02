using System;
using Confluence.Core.Geometry;
using Confluence.Core.Time;
using Confluence.Reactions.Grid;
using Confluence.Sim.Duel;
using Confluence.Sim.Hashing;
using Confluence.Sim.World;

namespace Confluence.Sim.Spells
{
    /// <summary>
    /// A ground wave or wind front (SPL-118 "A 2 m-wide wave moves 18 m/s along ground and pushes 2 m once"; SPL-192
    /// "solid obstacles stop segments"). The front is split into segments across its width; each segment advances
    /// along the ground and stops separately at an obstacle. Every target touched by a live segment takes the payload
    /// once per cast, so its push applies once; targets whose feet are higher than the wave's reach avoid it
    /// (SPL-118 "airborne targets above 1 m avoid it"). Waves are not lag-compensated (spec L423).
    /// </summary>
    public sealed class WaveDelivery : DeliverySpec
    {
        private float _speed;
        private float _width;
        private float _length;
        private float _maxTargetHeight = SpellRules.WaveDefaultMaxTargetHeight;
        private bool _groundOnly = true;

        public override DeliveryKind Kind => DeliveryKind.Wave;

        public float Speed { get => _speed; set => _speed = Set(value); }

        /// <summary>Front width in meters.</summary>
        public float Width { get => _width; set => _width = Set(value); }

        /// <summary>Travel distance in meters; 0 = the form's range.</summary>
        public float Length { get => _length; set => _length = Set(value); }

        /// <summary>Highest feet above the wave's ground that it still touches.</summary>
        public float MaxTargetHeight { get => _maxTargetHeight; set => _maxTargetHeight = Set(value); }

        /// <summary>Segments test obstacles near the ground (a wind front through the air sets false).</summary>
        public bool GroundOnly { get => _groundOnly; set => _groundOnly = Set(value); }

        internal override void ApplyGlobalCaps()
        {
            if (!(_speed > 0f) || !(_width > 0f)) throw new InvalidOperationException("A wave needs a speed and a width.");
        }

        public override void Release(SpellContext ctx, CastRecord cast)
        {
            Vec3 aim = AimDirection(ctx, cast);
            Vec3 dir = new Vec3(aim.X, 0f, aim.Z).NormalizedOr(CMath.ForwardFromYaw(ctx.Player(cast.Caster).Motor.YawDeg));
            Vec3 origin = ctx.Player(cast.Caster).Position;
            float length = Length > 0f ? Length : cast.Form.Range;
            WaveEntity wave = ctx.Spawn(new WaveEntity(cast, this, origin, dir, length));
            ctx.Emit(SpellEventKind.WaveLaunched, cast, origin, dir, wave.Id, length);
        }
    }

    /// <summary>A wave in motion (see <see cref="WaveDelivery"/>).</summary>
    public sealed class WaveEntity : SpellEntity
    {
        private readonly WaveDelivery _spec;
        private readonly Vec3 _origin;
        private readonly Vec3 _dir;
        private readonly Vec3 _right;
        private readonly float _length;
        private readonly float[] _offsets;
        private readonly float[] _reach;
        private readonly bool[] _blocked;
        private readonly bool[] _touched = new bool[DuelWorld.PlayerCount];
        private float _front;

        internal WaveEntity(CastRecord cast, WaveDelivery spec, Vec3 origin, Vec3 dir, float length)
            : base(cast)
        {
            _spec = spec;
            _origin = origin;
            _dir = dir;
            _right = CMath.RightFromYaw(CMath.YawOf(dir));
            _length = length;
            int segments = Math.Max(1, (int)Math.Ceiling(spec.Width / SpellRules.WaveSegmentWidth));
            _offsets = new float[segments];
            _reach = new float[segments];
            _blocked = new bool[segments];
            float segmentWidth = spec.Width / segments;
            for (int i = 0; i < segments; i++) _offsets[i] = -spec.Width * 0.5f + segmentWidth * (i + 0.5f);
        }

        public float Front => _front;
        public bool SegmentBlocked(int segment) => _blocked[segment];
        public int Segments => _offsets.Length;

        protected internal override void Step(SpellContext ctx)
        {
            float previous = _front;
            _front = Math.Min(_length, _front + _spec.Speed * Ticks.Dt);
            float segmentHalf = _spec.Width / _offsets.Length * 0.5f;
            bool anyLive = false;
            for (int s = 0; s < _offsets.Length; s++)
            {
                if (_blocked[s]) continue;
                Vec3 lane = _origin + _right * _offsets[s];
                float probe = _spec.GroundOnly ? SpellRules.WaveObstacleProbeHeight : 1f;
                Vec3 a = lane + _dir * previous + new Vec3(0f, probe, 0f);
                Vec3 b = lane + _dir * _front + new Vec3(0f, probe, 0f);
                float to = _front;
                if (ctx.Collision.SegmentCast(a, b, SolidMask.Movement, out WorldHit wall))
                {
                    to = previous + wall.Distance;
                    _blocked[s] = true;
                    ctx.Emit(SpellEventKind.WaveSegmentBlocked, Cast, lane + _dir * to, _dir, Id, solidId: wall.SolidId);
                }
                else
                {
                    anyLive = true;
                }

                _reach[s] = to;
                Touch(ctx, lane, previous, to, segmentHalf);
            }

            if (!anyLive || _front >= _length)
            {
                ctx.Emit(SpellEventKind.WaveEnded, Cast, _origin + _dir * _front, _dir, Id, _front);
                Vec2 from = _origin.XZ();
                Vec2 end = (_origin + _dir * _front).XZ();
                ctx.EmitDeposits(Cast, Cast.Form.Payload, DepositPlacement.Footprint, _origin, _dir, Footprint.Lane(from, end, _spec.Width));
                End(ctx);
            }
        }

        // Targets whose feet lie in the slab the segment swept this tick.
        private void Touch(SpellContext ctx, Vec3 lane, float from, float to, float halfWidth)
        {
            for (int i = 0; i < DuelWorld.PlayerCount; i++)
            {
                if (i == Owner || _touched[i] || !ctx.IsTargetable(i)) continue;
                Vec3 feet = ctx.Player(i).Position;
                Vec3 rel = feet - lane;
                float along = Vec3.Dot(new Vec3(rel.X, 0f, rel.Z), _dir);
                float across = Vec3.Dot(new Vec3(rel.X, 0f, rel.Z), _right);
                float radius = ctx.Player(i).HitVolume.Radius;
                if (along < from - radius || along > to + radius || Math.Abs(across) > halfWidth + radius) continue;
                if (feet.Y - _origin.Y > _spec.MaxTargetHeight) continue;
                _touched[i] = true;
                ctx.ApplyToPlayer(Cast, Cast.Form.Payload, i, PayloadHit.Direct(lane + _dir * from, _dir));
            }
        }

        protected override void HashState(ref StateHasher h)
        {
            h.Add(_front);
            for (int i = 0; i < _blocked.Length; i++)
            {
                h.Add(_blocked[i]);
                h.Add(_reach[i]);
            }

            for (int i = 0; i < _touched.Length; i++) h.Add(_touched[i]);
        }
    }
}
