using System;
using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions.Events;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Volumes;

namespace Confluence.Reactions
{
    public sealed partial class ReactionWorld
    {
        private int _nextWindOrder = 1;
        private readonly Dictionary<int, Vec2> _windDelta = new Dictionary<int, Vec2>();

        /// <summary>A gust acting over several ticks with its per-volume bookkeeping.</summary>
        private sealed class ActiveWind
        {
            public int Order;
            public WindGust Gust;
            public int RemainingTicks;

            /// <summary>Distance already moved per volume (bounded by the source's stated distance).</summary>
            public readonly Dictionary<int, float> Moved = new Dictionary<int, float>();

            /// <summary>Volumes whose front slice this gust has already dispersed (once per gust and volume).</summary>
            public readonly HashSet<int> Dispersed = new HashSet<int>();
        }

        private void ActivateWind(WindGust gust)
        {
            _winds.Add(new ActiveWind { Order = _nextWindOrder++, Gust = gust, RemainingTicks = Math.Max(1, gust.DurationTicks) });
        }

        private static int CompareWinds(WindGust a, WindGust b)
        {
            int c = CompareSourceOwner(a.SourceEventId, a.Owner, b.SourceEventId, b.Owner, a.Strength.CompareTo(b.Strength));
            if (c != 0) return c;
            c = a.Direction.X.CompareTo(b.Direction.X);
            if (c != 0) return c;
            c = a.Direction.Y.CompareTo(b.Direction.Y);
            if (c != 0) return c;
            c = a.DurationTicks.CompareTo(b.DurationTicks);
            if (c != 0) return c;
            c = a.TargetVolumeId.CompareTo(b.TargetVolumeId);
            if (c != 0) return c;
            c = a.Area.HasValue.CompareTo(b.Area.HasValue);
            if (c != 0) return c;
            return a.Area.HasValue ? CompareFootprints(a.Area.Value, b.Area.Value) : 0;
        }

        /// <summary>
        /// Phase 4 (spec L165 "wind movement"): corridor clears, then every active gust. Above strength 1 a gust
        /// translates touched movable volumes at up to 3 m/s (combined speed of all gusts per volume, L151); strength 2
        /// or more disperses each touched volume's front 2 m slice once (D-023).
        /// </summary>
        private void RunWind()
        {
            _clears.Sort((a, b) => CompareSourceOwner(a.SourceEventId, a.Owner, b.SourceEventId, b.Owner, CompareFootprints(a.Area, b.Area)));
            for (int i = 0; i < _clears.Count; i++)
            {
                ApplyClear(_clears[i]);
            }

            _clears.Clear();

            _newWinds.Sort(CompareWinds);
            for (int i = 0; i < _newWinds.Count; i++)
            {
                ActivateWind(_newWinds[i]);
            }

            _newWinds.Clear();
            if (_winds.Count == 0)
            {
                return;
            }

            float maxStep = GlobalRules.World.WindTranslateMaxSpeed * Ticks.Dt;
            _windDelta.Clear();
            for (int w = 0; w < _winds.Count; w++)
            {
                ActiveWind wind = _winds[w];
                WindGust gust = wind.Gust;
                Vec2 dir = gust.Direction.Normalized();
                if (dir.LengthSquared() < 0.5f)
                {
                    continue;
                }

                float step = CMath.Clamp(gust.Speed, 0f, GlobalRules.World.WindTranslateMaxSpeed) * Ticks.Dt;
                for (int v = 0; v < _volumes.Count; v++)
                {
                    ObscurationVolume volume = _volumes[v];
                    if (!volume.WindMovable || (gust.TargetVolumeId != 0 && gust.TargetVolumeId != volume.Id) ||
                        (gust.Area.HasValue && !VolumeTouches(volume, gust.Area.Value)))
                    {
                        continue;
                    }

                    if (gust.Disperses && wind.Dispersed.Add(volume.Id))
                    {
                        int removed = DisperseFrontSlice(volume, dir);
                        Emit(ReactionKind.WindDisperse, gust.Owner, gust.SourceEventId, Vec3.Zero, -1, removed, volume.Id);
                    }

                    if (!gust.Translates)
                    {
                        continue;
                    }

                    wind.Moved.TryGetValue(volume.Id, out float moved);
                    float move = MathF.Min(step, gust.MaxDistance - moved);
                    if (move <= 0f)
                    {
                        continue;
                    }

                    wind.Moved[volume.Id] = moved + move;
                    _windDelta.TryGetValue(volume.Id, out Vec2 delta);
                    _windDelta[volume.Id] = delta + dir * move;
                }
            }

            for (int v = _volumes.Count - 1; v >= 0; v--)
            {
                ObscurationVolume volume = _volumes[v];
                if (volume.Columns.IsEmpty)
                {
                    Retire(volume, VolumeRetireReason.Dispersed);
                    continue;
                }

                if (_windDelta.TryGetValue(volume.Id, out Vec2 delta))
                {
                    float len = delta.Length();
                    if (len > maxStep)
                    {
                        delta = delta * (maxStep / len); // L151: at most 3 m/s whatever combination of gusts acts
                    }

                    volume.Offset = volume.Offset + delta;
                }
            }

            for (int w = _winds.Count - 1; w >= 0; w--)
            {
                if (--_winds[w].RemainingTicks <= 0)
                {
                    _winds.RemoveAt(w);
                }
            }
        }

        /// <summary>L151: removes the columns within 2 m of the volume's leading face along the wind direction.</summary>
        private int DisperseFrontSlice(ObscurationVolume volume, Vec2 dir)
        {
            _columnXs.Clear();
            _columnZs.Clear();
            volume.Columns.GetColumns(_columnXs, _columnZs);
            float front = float.NegativeInfinity;
            for (int i = 0; i < _columnXs.Count; i++)
            {
                front = MathF.Max(front, Vec2.Dot(volume.ColumnCenter(_columnXs[i], _columnZs[i]), dir));
            }

            float cut = front - GlobalRules.World.WindDisperseSliceDepth + 1e-4f;
            int removed = 0;
            for (int i = 0; i < _columnXs.Count; i++)
            {
                if (Vec2.Dot(volume.ColumnCenter(_columnXs[i], _columnZs[i]), dir) > cut && volume.Columns.Remove(_columnXs[i], _columnZs[i]))
                {
                    removed++;
                }
            }

            return removed;
        }

        /// <summary>SPL-061 style corridor: removes the volume columns whose centers lie in the area.</summary>
        private void ApplyClear(VolumeClear clear)
        {
            for (int v = _volumes.Count - 1; v >= 0; v--)
            {
                ObscurationVolume volume = _volumes[v];
                if (clear.MovableOnly && !volume.WindMovable)
                {
                    continue;
                }

                _columnXs.Clear();
                _columnZs.Clear();
                volume.Columns.GetColumns(_columnXs, _columnZs);
                int removed = 0;
                for (int i = 0; i < _columnXs.Count; i++)
                {
                    if (clear.Area.Contains(volume.ColumnCenter(_columnXs[i], _columnZs[i])) && volume.Columns.Remove(_columnXs[i], _columnZs[i]))
                    {
                        removed++;
                    }
                }

                if (removed == 0)
                {
                    continue;
                }

                Emit(ReactionKind.VolumeCleared, clear.Owner, clear.SourceEventId, clear.Area.Anchor.ToXZ(), -1, removed, volume.Id);
                if (volume.Columns.IsEmpty)
                {
                    Retire(volume, VolumeRetireReason.Cleared);
                }
            }
        }
    }
}
