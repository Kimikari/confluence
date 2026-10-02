using System;
using System.Collections.Generic;
using Confluence.Core.Geometry;

namespace Confluence.Casting.Grammars
{
    /// <summary>An aim checkpoint as a yaw/pitch offset from the aim recorded at preparation start (degrees).</summary>
    public readonly struct AimOffset
    {
        public AimOffset(float yawDeg, float pitchDeg)
        {
            YawDeg = yawDeg;
            PitchDeg = pitchDeg;
        }

        public float YawDeg { get; }
        public float PitchDeg { get; }
    }

    /// <summary>
    /// Real camera aim measurement for the AimPath family (WIZ-001 held focus, WIZ-025 reticle painting, WIZ-028
    /// prediction alignment). It reads only the caster's own aim from the input frame; guides are fixed offsets
    /// from the caster's recorded aim and never reference an enemy (spec L474 "their guides never aim or detect an
    /// enemy for the player"). Checkpoints must be passed in order (WIZ-025).
    /// </summary>
    public sealed class AimTracker
    {
        private readonly Vec3[] _history;
        private readonly List<AimOffset> _checkpoints = new List<AimOffset>();
        private int _count;
        private int _head;
        private float _checkpointToleranceDeg;

        /// <param name="historyTicks">How many recent aim samples to keep for steadiness checks (WIZ-001: 0.25 s = 15).</param>
        public AimTracker(int historyTicks)
        {
            if (historyTicks < 1) throw new ArgumentOutOfRangeException(nameof(historyTicks));
            _history = new Vec3[historyTicks];
        }

        public float OriginYawDeg { get; private set; }
        public float OriginPitchDeg { get; private set; }
        public Vec3 Origin { get; private set; }
        public Vec3 Current { get; private set; }

        /// <summary>Checkpoints passed in order so far.</summary>
        public int CheckpointsPassed { get; private set; }

        /// <summary>Tick the final checkpoint was passed, or -1.</summary>
        public int CheckpointsCompletedTick { get; private set; }

        /// <summary>A later checkpoint was reached before an earlier one.</summary>
        public bool CheckpointOrderBroken { get; private set; }

        public int CheckpointCount => _checkpoints.Count;

        /// <summary>Records the preparation-start aim and clears history and checkpoints.</summary>
        public void Reset(in CastInputFrame frame)
        {
            OriginYawDeg = frame.AimYawDeg;
            OriginPitchDeg = frame.AimPitchDeg;
            Origin = CMath.ForwardFromYawPitch(frame.AimYawDeg, frame.AimPitchDeg);
            Current = Origin;
            _count = 0;
            _head = 0;
            _checkpoints.Clear();
            CheckpointsPassed = 0;
            CheckpointsCompletedTick = -1;
            CheckpointOrderBroken = false;
            Push(Origin);
        }

        /// <summary>Declares ordered checkpoints relative to the origin aim, each passed within a tolerance.</summary>
        public void SetCheckpoints(IReadOnlyList<AimOffset> checkpoints, float toleranceDeg)
        {
            if (checkpoints == null) throw new ArgumentNullException(nameof(checkpoints));
            if (!(toleranceDeg > 0f)) throw new ArgumentOutOfRangeException(nameof(toleranceDeg));
            _checkpoints.Clear();
            for (int i = 0; i < checkpoints.Count; i++) _checkpoints.Add(checkpoints[i]);
            _checkpointToleranceDeg = toleranceDeg;
            CheckpointsPassed = 0;
            CheckpointsCompletedTick = -1;
            CheckpointOrderBroken = false;
        }

        public void Sample(in CastInputFrame frame)
        {
            Current = CMath.ForwardFromYawPitch(frame.AimYawDeg, frame.AimPitchDeg);
            Push(Current);
            for (int i = CheckpointsPassed; i < _checkpoints.Count; i++)
            {
                if (CMath.AngleBetweenDeg(Current, Direction(_checkpoints[i])) > _checkpointToleranceDeg) continue;
                if (i > CheckpointsPassed) CheckpointOrderBroken = true;
                CheckpointsPassed = i + 1;
                if (CheckpointsPassed == _checkpoints.Count) CheckpointsCompletedTick = frame.Tick;
                break;
            }
        }

        /// <summary>World direction of an offset from the origin aim.</summary>
        public Vec3 Direction(AimOffset offset) => CMath.ForwardFromYawPitch(OriginYawDeg + offset.YawDeg, OriginPitchDeg + offset.PitchDeg);

        /// <summary>Angle between the current aim and the origin aim (WIZ-025 "finish within 1.5 degrees of the initial aim").</summary>
        public float AngleFromOriginDeg => CMath.AngleBetweenDeg(Current, Origin);

        /// <summary>Angle between the current aim and an offset socket (WIZ-028 "within 1.5 degrees of that socket").</summary>
        public float AngleFromDeg(AimOffset offset) => CMath.AngleBetweenDeg(Current, Direction(offset));

        /// <summary>
        /// Largest angle between the current aim and each of the last <paramref name="ticks"/> samples (including the
        /// current one). WIZ-001 "within a 2-degree cone for the final 0.25 s" reads as: every sample of the final
        /// 0.25 s lies within 2 degrees of the release aim. Returns +infinity when fewer samples exist.
        /// </summary>
        public float SteadinessDeg(int ticks)
        {
            if (ticks < 1 || ticks > _history.Length) throw new ArgumentOutOfRangeException(nameof(ticks));
            if (_count < ticks) return float.PositiveInfinity;
            float worst = 0f;
            for (int k = 0; k < ticks; k++)
            {
                int index = (_head - 1 - k + _history.Length) % _history.Length;
                worst = Math.Max(worst, CMath.AngleBetweenDeg(Current, _history[index]));
            }

            return worst;
        }

        private void Push(Vec3 direction)
        {
            _history[_head] = direction;
            _head = (_head + 1) % _history.Length;
            if (_count < _history.Length) _count++;
        }
    }
}
