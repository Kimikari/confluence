using System;
using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Reactions.Events;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Hazards;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Volumes;

namespace Confluence.Reactions
{
    public sealed partial class ReactionWorld
    {
        private readonly List<float> _intervalStarts = new List<float>();
        private readonly List<float> _intervalEnds = new List<float>();
        private readonly List<int> _order = new List<int>();
        private readonly List<int> _columnXs = new List<int>();
        private readonly List<int> _columnZs = new List<int>();

        // ------------------------------------------------------------------ sight (spec L167)

        /// <summary>
        /// Cumulative path length (meters) of the segment through sight-blocking volumes. Overlapping volumes count
        /// once: the union of the segment's inside intervals is measured (spec L167 "cumulative path length").
        /// </summary>
        public float SightPathLength(Vec3 from, Vec3 to)
        {
            float length = Vec3.Distance(from, to);
            if (length < CMath.Epsilon)
            {
                return 0f;
            }

            _intervalStarts.Clear();
            _intervalEnds.Clear();
            for (int i = 0; i < _volumes.Count; i++)
            {
                if (_volumes[i].BlocksSight)
                {
                    _volumes[i].AppendSegmentIntervals(from, to, _intervalStarts, _intervalEnds);
                }
            }

            return UnionLength() * length;
        }

        /// <summary>Two meters of cumulative path through sight-blocking volumes blocks target acquisition (spec L167).</summary>
        public bool BlocksAcquisition(Vec3 from, Vec3 to) =>
            SightPathLength(from, to) >= GlobalRules.World.SteamSightBlockPathLength - ReactionRules.SightBlockTolerance;

        private float UnionLength()
        {
            int n = _intervalStarts.Count;
            if (n == 0)
            {
                return 0f;
            }

            _order.Clear();
            for (int i = 0; i < n; i++)
            {
                _order.Add(i);
            }

            _order.Sort((a, b) =>
            {
                int c = _intervalStarts[a].CompareTo(_intervalStarts[b]);
                return c != 0 ? c : _intervalEnds[a].CompareTo(_intervalEnds[b]);
            });

            float total = 0f;
            float curStart = _intervalStarts[_order[0]];
            float curEnd = _intervalEnds[_order[0]];
            for (int k = 1; k < n; k++)
            {
                float s = _intervalStarts[_order[k]];
                float e = _intervalEnds[_order[k]];
                if (s <= curEnd)
                {
                    curEnd = MathF.Max(curEnd, e);
                }
                else
                {
                    total += curEnd - curStart;
                    curStart = s;
                    curEnd = e;
                }
            }

            return total + (curEnd - curStart);
        }

        // ------------------------------------------------------------------ lifecycle

        /// <summary>A new, unregistered volume with the kind's default rules.</summary>
        private ObscurationVolume NewVolume(VolumeKind kind, int owner, int source, int fieldId, float baseY, float height, int lifetimeTicks)
        {
            int lifetime = PlayerLifetimeCap(owner, lifetimeTicks);
            var volume = new ObscurationVolume(0 /* assigned on registration */, kind, _grid.Origin);
            volume.Owner = owner;
            volume.SourceEventId = source;
            volume.FieldId = fieldId;
            volume.CreatedTick = _tick;
            volume.ExpiresTick = lifetimeTicks == int.MaxValue && owner == ReactionRules.MapOwner ? int.MaxValue : _tick + lifetime;
            volume.BaseY = baseY;
            volume.Height = MathF.Max(0f, height);
            volume.BlocksSight = VolumeKinds.BlocksSightByDefault(kind);
            volume.WindMovable = VolumeKinds.WindMovableByDefault(kind);
            volume.DrainedByLight = VolumeKinds.DrainedByBrightLight(kind);
            volume.CountsAsField = owner != ReactionRules.MapOwner;
            volume.Removable = true;
            volume.Channel = DamageChannel.None;
            return volume;
        }

        /// <summary>Assigns the next id, registers the volume, and emits its creation event (owner for the field cap).</summary>
        private ObscurationVolume RegisterVolume(ObscurationVolume volume)
        {
            volume.Id = _nextVolumeId++;
            _volumes.Add(volume);
            _volumeById[volume.Id] = volume;
            if (volume.DamagePerSecond > 0f)
            {
                Hazard hazard = NewHazard(ReactionKind.VolumeContact, HazardShape.Volume, volume.Owner, volume.SourceEventId,
                    volume.Channel, volume.DamagePerSecond, true, volume.ExpiresTick);
                hazard.VolumeId = volume.Id;
                hazard.Soluble = volume.Soluble;
            }

            _result.VolumeEventList.Add(MakeVolumeEvent(volume, VolumeEventKind.Created, VolumeRetireReason.None, _tick));
            return volume;
        }

        private static VolumeEvent MakeVolumeEvent(ObscurationVolume v, VolumeEventKind kind, VolumeRetireReason reason, int tick)
        {
            return new VolumeEvent
            {
                Kind = kind,
                VolumeId = v.Id,
                VolumeKind = v.Kind,
                Owner = v.Owner,
                SourceEventId = v.SourceEventId,
                CountsAsField = v.CountsAsField,
                CreatedTick = v.CreatedTick,
                Tick = tick,
                Reason = reason,
            };
        }

        private void RetireVolumeInternal(ObscurationVolume volume, VolumeRetireReason reason, List<VolumeEvent> events, int tick)
        {
            if (!_volumeById.Remove(volume.Id))
            {
                return;
            }

            _volumes.Remove(volume);
            for (int i = _hazards.Count - 1; i >= 0; i--)
            {
                if (_hazards[i].Shape == HazardShape.Volume && _hazards[i].VolumeId == volume.Id)
                {
                    RemoveHazard(_hazards[i]);
                }
            }

            events.Add(MakeVolumeEvent(volume, VolumeEventKind.Retired, reason, tick));
        }

        private void Retire(ObscurationVolume volume, VolumeRetireReason reason) =>
            RetireVolumeInternal(volume, reason, _result.VolumeEventList, _tick);

        /// <summary>Creates a volume from a spec (spells, props, biome rules).</summary>
        private void CreateVolumeNow(VolumeSpec spec)
        {
            if (spec.LifetimeTicks <= 0)
            {
                return;
            }

            ObscurationVolume volume = NewVolume(spec.Kind, spec.Owner, spec.SourceEventId, spec.FieldId, spec.BaseY, spec.Height, spec.LifetimeTicks);
            if (spec.BlocksSight.HasValue) volume.BlocksSight = spec.BlocksSight.Value;
            if (spec.WindMovable.HasValue) volume.WindMovable = spec.WindMovable.Value;
            if (spec.CountsAsField.HasValue) volume.CountsAsField = spec.CountsAsField.Value && spec.Owner != ReactionRules.MapOwner;
            volume.Removable = spec.Removable;
            volume.DamagePerSecond = MathF.Max(0f, spec.DamagePerSecond);
            volume.Channel = spec.Channel;
            volume.Soluble = spec.Soluble;
            volume.SourceWater = Math.Max(0, spec.SourceWater);

            _scratchCells.Clear();
            _grid.Rasterize(spec.Footprint, _scratchCells);
            for (int i = 0; i < _scratchCells.Count; i++)
            {
                volume.Columns.Add(_grid.XOf(_scratchCells[i]), _grid.ZOf(_scratchCells[i]));
            }

            if (!volume.Columns.IsEmpty)
            {
                RegisterVolume(volume);
            }
        }

        /// <summary>True when any column center of the volume lies inside the footprint.</summary>
        private bool VolumeTouches(ObscurationVolume volume, Footprint area)
        {
            _columnXs.Clear();
            _columnZs.Clear();
            volume.Columns.GetColumns(_columnXs, _columnZs);
            for (int i = 0; i < _columnXs.Count; i++)
            {
                if (area.Contains(volume.ColumnCenter(_columnXs[i], _columnZs[i])))
                {
                    return true;
                }
            }

            // A footprint smaller than a column still touches the column under its anchor.
            volume.ColumnOf(area.Anchor, out int ax, out int az);
            return volume.Columns.Contains(ax, az);
        }

        private static int CompareVolumeSpecs(VolumeSpec a, VolumeSpec b)
        {
            int c = a.SourceEventId.CompareTo(b.SourceEventId);
            if (c != 0) return c;
            c = a.Owner.CompareTo(b.Owner);
            if (c != 0) return c;
            c = a.FieldId.CompareTo(b.FieldId);
            if (c != 0) return c;
            c = a.Kind.CompareTo(b.Kind);
            if (c != 0) return c;
            c = CompareFootprints(a.Footprint, b.Footprint);
            if (c != 0) return c;
            c = a.LifetimeTicks.CompareTo(b.LifetimeTicks);
            if (c != 0) return c;
            c = a.BaseY.CompareTo(b.BaseY);
            if (c != 0) return c;
            c = a.Height.CompareTo(b.Height);
            if (c != 0) return c;
            c = a.DamagePerSecond.CompareTo(b.DamagePerSecond);
            return c != 0 ? c : a.SourceWater.CompareTo(b.SourceWater);
        }

        internal static int CompareFootprints(Footprint a, Footprint b)
        {
            int c = a.Shape.CompareTo(b.Shape);
            if (c != 0) return c;
            c = a.A.X.CompareTo(b.A.X);
            if (c != 0) return c;
            c = a.A.Y.CompareTo(b.A.Y);
            if (c != 0) return c;
            c = a.B.X.CompareTo(b.B.X);
            if (c != 0) return c;
            c = a.B.Y.CompareTo(b.B.Y);
            if (c != 0) return c;
            c = a.Radius.CompareTo(b.Radius);
            if (c != 0) return c;
            c = a.HalfSize.X.CompareTo(b.HalfSize.X);
            if (c != 0) return c;
            c = a.HalfSize.Y.CompareTo(b.HalfSize.Y);
            if (c != 0) return c;
            c = a.YawDeg.CompareTo(b.YawDeg);
            if (c != 0) return c;
            c = a.Direction.X.CompareTo(b.Direction.X);
            if (c != 0) return c;
            c = a.Direction.Y.CompareTo(b.Direction.Y);
            return c != 0 ? c : a.HalfAngleDeg.CompareTo(b.HalfAngleDeg);
        }
    }
}
