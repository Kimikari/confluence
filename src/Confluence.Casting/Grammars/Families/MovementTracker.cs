using System;
using System.Collections.Generic;
using Confluence.Core.Geometry;

namespace Confluence.Casting.Grammars
{
    /// <summary>Movement direction relative to the caster's aim yaw.</summary>
    public enum RelativeDirection : byte
    {
        Forward = 0,
        Right = 1,
        Back = 2,
        Left = 3,
    }

    /// <summary>
    /// Real-movement measurement for the Movement family (WIZ-013, -014, -027, -029, -030). Everything comes from
    /// authoritative positions and velocities, never from key state, so "shaking keys against a wall" produces no
    /// travel (spec L482). Ticks with a forced displacement or teleport event add no travel and mark the sample
    /// displaced (WIZ-030 "Teleporting or being knocked across marks does not qualify"); terrain collisions are
    /// recorded separately (WIZ-014 "invalidate only the reward").
    /// </summary>
    public sealed class MovementTracker
    {
        private readonly List<RelativeDirection> _legs = new List<RelativeDirection>();
        private Vec3 _origin;
        private Vec3 _last;
        private bool _started;
        private float _minLegMeters;
        private bool _pendingValid;
        private RelativeDirection _pendingDirection;
        private float _pendingMeters;

        private bool _declared;
        private float _declaredYaw;
        private float _minSpeed;
        private float _toleranceDeg;

        public MovementTracker(float minLegMeters = 0.5f)
        {
            if (!(minLegMeters > 0f)) throw new ArgumentOutOfRangeException(nameof(minLegMeters));
            _minLegMeters = minLegMeters;
        }

        /// <summary>Horizontal path length travelled since <see cref="Reset"/>, in meters.</summary>
        public float TravelMeters { get; private set; }

        public bool Displaced { get; private set; }
        public bool Collided { get; private set; }

        /// <summary>Consecutive ticks, up to now, moving at the declared speed within the declared angle.</summary>
        public int AlignedTicks { get; private set; }

        /// <summary>Direction legs of at least the minimum length, in order; repeats of the same direction merge.</summary>
        public IReadOnlyList<RelativeDirection> Legs => _legs;

        public float DisplacementMeters => _started ? Vec3.DistanceXZ(_origin, _last) : 0f;

        public void Reset(in CastInputFrame frame)
        {
            _origin = frame.Position;
            _last = frame.Position;
            _started = true;
            TravelMeters = 0f;
            Displaced = false;
            Collided = false;
            AlignedTicks = 0;
            _legs.Clear();
            _pendingValid = false;
            _pendingMeters = 0f;
        }

        /// <summary>Declares a horizontal velocity requirement (WIZ-029: at least 4 m/s within 20 degrees).</summary>
        public void Declare(float yawDeg, float minSpeed, float toleranceDeg)
        {
            _declared = true;
            _declaredYaw = yawDeg;
            _minSpeed = minSpeed;
            _toleranceDeg = toleranceDeg;
            AlignedTicks = 0;
        }

        public void Sample(in CastInputFrame frame)
        {
            if (!_started)
            {
                Reset(frame);
                return;
            }

            bool forced = frame.HasCombatEvent(CombatEventKind.ForcedDisplacement) || frame.HasCombatEvent(CombatEventKind.Teleported);
            if (frame.HasCombatEvent(CombatEventKind.TerrainCollision)) Collided = true;
            Vec3 delta = frame.Position - _last;
            _last = frame.Position;
            if (forced)
            {
                Displaced = true;
                _pendingValid = false;
                _pendingMeters = 0f;
            }
            else
            {
                float step = delta.LengthXZ();
                TravelMeters += step;
                if (step > 1e-4f) TrackLeg(Relative(delta.XZ(), frame.AimYawDeg), step);
            }

            if (_declared)
            {
                Vec2 v = frame.Velocity.XZ();
                bool aligned = !forced && v.Length() >= _minSpeed && Math.Abs(CMath.DeltaAngle(CMath.YawOf(v), _declaredYaw)) <= _toleranceDeg;
                AlignedTicks = aligned ? AlignedTicks + 1 : 0;
            }
        }

        private void TrackLeg(RelativeDirection direction, float meters)
        {
            if (!_pendingValid || direction != _pendingDirection)
            {
                _pendingValid = true;
                _pendingDirection = direction;
                _pendingMeters = 0f;
            }

            _pendingMeters += meters;
            bool repeat = _legs.Count > 0 && _legs[_legs.Count - 1] == direction;
            if (_pendingMeters >= _minLegMeters && !repeat) _legs.Add(direction);
        }

        /// <summary>Quantizes a horizontal step to forward/right/back/left of the aim yaw.</summary>
        public static RelativeDirection Relative(Vec2 stepXZ, float aimYawDeg)
        {
            float delta = CMath.DeltaAngle(aimYawDeg, CMath.YawOf(stepXZ));
            if (delta >= -45f && delta < 45f) return RelativeDirection.Forward;
            if (delta >= 45f && delta < 135f) return RelativeDirection.Right;
            if (delta >= -135f && delta < -45f) return RelativeDirection.Left;
            return RelativeDirection.Back;
        }
    }
}
