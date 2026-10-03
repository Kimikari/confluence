using System;
using System.Collections.Generic;
using System.IO;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Reactions.Electricity;
using Confluence.Reactions.Events;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Hazards;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Volumes;

namespace Confluence.Reactions
{
    public sealed partial class ReactionWorld
    {
        // Optional cell field groups in a cell record.
        private const byte GroupIce = 1;
        private const byte GroupBurn = 2;
        private const byte GroupSuppressed = 4;
        private const byte GroupDamp = 8;
        private const byte GroupCharge = 16;
        private const byte GroupVenom = 32;
        private const byte GroupSticky = 64;

        /// <summary>Captures the dynamic state between steps (see <see cref="ReactionSnapshot"/>).</summary>
        public ReactionSnapshot CaptureSnapshot()
        {
            List<int> active = CurrentActive();
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                w.Write(ReactionSnapshot.Magic);
                w.Write(ReactionSnapshot.Version);
                w.Write(_tick);
                w.Write(active.Count);
                w.Write(_volumes.Count);
                w.Write(CombatStartTick);
                w.Write(_grid.Width);
                w.Write(_grid.Depth);
                w.Write(_grid.Origin.X);
                w.Write(_grid.Origin.Y);
                w.Write(_hasStepped);
                w.Write(_nextVolumeId);
                w.Write(_nextHazardId);
                w.Write(_nextWindOrder);
                w.Write(_nextSourceId);

                for (int i = 0; i < active.Count; i++)
                {
                    WriteCell(w, active[i]);
                }

                for (int i = 0; i < _volumes.Count; i++)
                {
                    WriteVolume(w, _volumes[i]);
                }

                w.Write(_hazards.Count);
                for (int i = 0; i < _hazards.Count; i++)
                {
                    WriteHazard(w, _hazards[i]);
                }

                w.Write(_conductors.Count);
                for (int i = 0; i < _conductors.Count; i++)
                {
                    Conductor c = _conductors[i];
                    w.Write(c.Id);
                    w.Write(c.RemainingCapacity);
                    w.Write(c.OverloadedUntil);
                    w.Write(c.GroundingEnabled);
                }

                w.Write(_releases.Count);
                for (int i = 0; i < _releases.Count; i++)
                {
                    PendingRelease r = _releases[i];
                    w.Write(r.ConductorId);
                    WriteVec3(w, r.Contact);
                    w.Write(r.ReleaseTick);
                    w.Write(r.Owner);
                    w.Write(r.Source);
                    w.Write(r.Damage);
                    w.Write(r.HazardId);
                }

                w.Write(_winds.Count);
                for (int i = 0; i < _winds.Count; i++)
                {
                    WriteWind(w, _winds[i]);
                }

                w.Write(_sources.Count);
                for (int i = 0; i < _sources.Count; i++)
                {
                    w.Write(_sources[i].Id);
                    w.Write(_sources[i].NextPulseTick);
                    w.Write(_sources[i].PulsesFired);
                }

                w.Write(_gusts.Count);
                for (int i = 0; i < _gusts.Count; i++)
                {
                    w.Write(_gusts[i].Fired);
                }

                var layerIds = new List<int>(_fieldLayers.Keys);
                layerIds.Sort();
                w.Write(layerIds.Count);
                for (int i = 0; i < layerIds.Count; i++)
                {
                    FieldLayer layer = _fieldLayers[layerIds[i]];
                    w.Write(layer.Id);
                    w.Write(layer.Owner);
                    w.Write(layer.CreatedTick);
                    w.Write(layer.Removable);
                }

                var ignited = new List<int>(_ignitedSolids);
                ignited.Sort();
                w.Write(ignited.Count);
                for (int i = 0; i < ignited.Count; i++)
                {
                    w.Write(ignited[i]);
                }

                GravityFields.WriteHistory(w);
                w.Flush();
                return new ReactionSnapshot(stream.ToArray(), _tick, active.Count, _volumes.Count);
            }
        }

        /// <summary>Deterministic hash of the current dynamic state (desync detection, replay verification).</summary>
        public ulong StateHash() => CaptureSnapshot().Hash;

        /// <summary>
        /// Replaces the dynamic state with a snapshot taken from a world built from the same layout and
        /// registrations. Conductors in the snapshot that are not registered here are skipped.
        /// </summary>
        public void RestoreSnapshot(ReactionSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            using (var r = new BinaryReader(new MemoryStream(snapshot.Data, false)))
            {
                if (r.ReadUInt32() != ReactionSnapshot.Magic || r.ReadUInt16() != ReactionSnapshot.Version)
                {
                    throw new InvalidDataException("Not a reaction snapshot of this version.");
                }

                int tick = r.ReadInt32();
                int cellCount = r.ReadInt32();
                int volumeCount = r.ReadInt32();
                r.ReadInt32(); // combat start tick (fixed by construction)
                int width = r.ReadInt32();
                int depth = r.ReadInt32();
                float ox = r.ReadSingle();
                float oz = r.ReadSingle();
                if (width != _grid.Width || depth != _grid.Depth || ox != _grid.Origin.X || oz != _grid.Origin.Y)
                {
                    throw new InvalidDataException("Snapshot grid does not match this world.");
                }

                _hasStepped = r.ReadBoolean();
                _nextVolumeId = r.ReadInt32();
                _nextHazardId = r.ReadInt32();
                _nextWindOrder = r.ReadInt32();
                _nextSourceId = r.ReadInt32();
                _tick = tick;

                List<int> active = CurrentActive();
                for (int i = 0; i < active.Count; i++)
                {
                    ResetDynamic(ref _grid[active[i]]);
                    _activeMark[active[i]] = false;
                }

                _active.Clear();
                for (int i = 0; i < cellCount; i++)
                {
                    ReadCell(r);
                }

                _volumes.Clear();
                _volumeById.Clear();
                for (int i = 0; i < volumeCount; i++)
                {
                    ObscurationVolume v = ReadVolume(r);
                    _volumes.Add(v);
                    _volumeById[v.Id] = v;
                }

                _hazards.Clear();
                _hazardById.Clear();
                int hazardCount = r.ReadInt32();
                for (int i = 0; i < hazardCount; i++)
                {
                    Hazard h = ReadHazard(r);
                    _hazards.Add(h);
                    _hazardById[h.Id] = h;
                }

                int conductorCount = r.ReadInt32();
                for (int i = 0; i < conductorCount; i++)
                {
                    int id = r.ReadInt32();
                    int remaining = r.ReadInt32();
                    int overloaded = r.ReadInt32();
                    bool grounding = r.ReadBoolean();
                    if (_conductorById.TryGetValue(id, out Conductor c))
                    {
                        c.RemainingCapacity = remaining;
                        c.OverloadedUntil = overloaded;
                        c.GroundingEnabled = grounding;
                    }
                }

                _releases.Clear();
                int releaseCount = r.ReadInt32();
                for (int i = 0; i < releaseCount; i++)
                {
                    _releases.Add(new PendingRelease
                    {
                        ConductorId = r.ReadInt32(),
                        Contact = ReadVec3(r),
                        ReleaseTick = r.ReadInt32(),
                        Owner = r.ReadInt32(),
                        Source = r.ReadInt32(),
                        Damage = r.ReadSingle(),
                        HazardId = r.ReadInt32(),
                    });
                }

                _winds.Clear();
                int windCount = r.ReadInt32();
                for (int i = 0; i < windCount; i++)
                {
                    _winds.Add(ReadWind(r));
                }

                int sourceCount = r.ReadInt32();
                for (int i = 0; i < sourceCount; i++)
                {
                    int id = r.ReadInt32();
                    int next = r.ReadInt32();
                    int fired = r.ReadInt32();
                    for (int k = 0; k < _sources.Count; k++)
                    {
                        if (_sources[k].Id == id)
                        {
                            _sources[k].NextPulseTick = next;
                            _sources[k].PulsesFired = fired;
                        }
                    }
                }

                int gustCount = r.ReadInt32();
                for (int i = 0; i < gustCount; i++)
                {
                    bool fired = r.ReadBoolean();
                    if (i < _gusts.Count)
                    {
                        _gusts[i].Fired = fired;
                    }
                }

                _fieldLayers.Clear();
                int layerCount = r.ReadInt32();
                for (int i = 0; i < layerCount; i++)
                {
                    var layer = new FieldLayer { Id = r.ReadInt32(), Owner = r.ReadInt32(), CreatedTick = r.ReadInt32(), Removable = r.ReadBoolean() };
                    _fieldLayers[layer.Id] = layer;
                }

                _ignitedSolids.Clear();
                int ignitedCount = r.ReadInt32();
                for (int i = 0; i < ignitedCount; i++)
                {
                    _ignitedSolids.Add(r.ReadInt32());
                }

                GravityFields.ReadHistory(r);
            }

            _pendingVolumeEvents.Clear();
            _regions.Rebuild(_grid, _active, _activeMark);
        }

        // ------------------------------------------------------------------ cells

        private static void ResetDynamic(ref Cell c)
        {
            MaterialFlags flags = c.Flags;
            bool staticSurface = (c.State & CellState.StaticSurface) != 0;
            float surface = staticSurface ? c.SurfaceY : 0f;
            CellState keep = c.State & (CellState.StaticSurface | CellState.NaturalSource);
            c = Cell.Empty();
            c.Flags = flags;
            c.SurfaceY = surface;
            c.State = staticSurface ? keep | CellState.HasSurface : keep;
        }

        private void WriteCell(BinaryWriter w, int index)
        {
            ref Cell c = ref _grid[index];
            w.Write(index);
            byte mask = 0;
            for (int m = 0; m < Materials.Count; m++)
            {
                if (Cell.Slot(ref c, (Material)m).Units > 0)
                {
                    mask |= (byte)(1 << m);
                }
            }

            w.Write(mask);
            for (int m = 0; m < Materials.Count; m++)
            {
                if ((mask & (1 << m)) == 0)
                {
                    continue;
                }

                ref MaterialSlot s = ref Cell.Slot(ref c, (Material)m);
                w.Write(s.Units);
                w.Write(s.Natural);
                w.Write(s.Expiry);
                w.Write(s.Owner);
                w.Write(s.Source);
                w.Write(s.Field);
                w.Write(s.Declared);
            }

            w.Write((int)c.Flags);
            w.Write((ushort)c.State);
            w.Write(c.SurfaceY);
            byte groups = 0;
            if (c.HasIce) groups |= GroupIce;
            if (c.IsBurning || c.BurnHazard != 0) groups |= GroupBurn;
            if (c.BurnSuppressedUntil != 0) groups |= GroupSuppressed;
            if (c.DampUnits != 0 || c.DampUntil != 0) groups |= GroupDamp;
            if (c.ChargeHazard != 0) groups |= GroupCharge;
            if (c.VenomHazard != 0) groups |= GroupVenom;
            if (c.StickySlow != 0f) groups |= GroupSticky;
            w.Write(groups);
            if ((groups & GroupIce) != 0)
            {
                w.Write(c.IceDurability);
                w.Write(c.IceExpiry);
                w.Write(c.IceOwner);
                w.Write(c.IceSource);
            }

            if ((groups & GroupBurn) != 0)
            {
                w.Write(c.BurnEnd);
                w.Write(c.BurnHazard);
            }

            if ((groups & GroupSuppressed) != 0) w.Write(c.BurnSuppressedUntil);
            if ((groups & GroupDamp) != 0)
            {
                w.Write(c.DampUnits);
                w.Write(c.DampUntil);
            }

            if ((groups & GroupCharge) != 0) w.Write(c.ChargeHazard);
            if ((groups & GroupVenom) != 0) w.Write(c.VenomHazard);
            if ((groups & GroupSticky) != 0) w.Write(c.StickySlow);
        }

        private void ReadCell(BinaryReader r)
        {
            int index = r.ReadInt32();
            if (index < 0 || index >= _grid.CellCount)
            {
                throw new InvalidDataException("Snapshot cell index out of range.");
            }

            ref Cell c = ref _grid[index];
            c = Cell.Empty();
            byte mask = r.ReadByte();
            for (int m = 0; m < Materials.Count; m++)
            {
                if ((mask & (1 << m)) == 0)
                {
                    continue;
                }

                ref MaterialSlot s = ref Cell.Slot(ref c, (Material)m);
                s.Units = r.ReadByte();
                s.Natural = r.ReadByte();
                s.Expiry = r.ReadInt32();
                s.Owner = r.ReadInt32();
                s.Source = r.ReadInt32();
                s.Field = r.ReadInt32();
                s.Declared = r.ReadSingle();
            }

            c.Flags = (MaterialFlags)r.ReadInt32();
            c.State = (CellState)r.ReadUInt16();
            c.SurfaceY = r.ReadSingle();
            byte groups = r.ReadByte();
            if ((groups & GroupIce) != 0)
            {
                c.IceDurability = r.ReadSingle();
                c.IceExpiry = r.ReadInt32();
                c.IceOwner = r.ReadInt32();
                c.IceSource = r.ReadInt32();
            }

            if ((groups & GroupBurn) != 0)
            {
                c.BurnEnd = r.ReadInt32();
                c.BurnHazard = r.ReadInt32();
            }

            if ((groups & GroupSuppressed) != 0) c.BurnSuppressedUntil = r.ReadInt32();
            if ((groups & GroupDamp) != 0)
            {
                c.DampUnits = r.ReadByte();
                c.DampUntil = r.ReadInt32();
            }

            if ((groups & GroupCharge) != 0) c.ChargeHazard = r.ReadInt32();
            if ((groups & GroupVenom) != 0) c.VenomHazard = r.ReadInt32();
            if ((groups & GroupSticky) != 0) c.StickySlow = r.ReadSingle();
            _activeMark[index] = true;
            _active.Add(index);
        }

        // ------------------------------------------------------------------ volumes and hazards

        private static void WriteVolume(BinaryWriter w, ObscurationVolume v)
        {
            w.Write(v.Id);
            w.Write((byte)v.Kind);
            w.Write(v.Owner);
            w.Write(v.SourceEventId);
            w.Write(v.FieldId);
            w.Write(v.CreatedTick);
            w.Write(v.ExpiresTick);
            w.Write(v.BaseY);
            w.Write(v.Height);
            w.Write(v.Offset.X);
            w.Write(v.Offset.Y);
            int flags = (v.BlocksSight ? 1 : 0) | (v.WindMovable ? 2 : 0) | (v.DrainedByLight ? 4 : 0) | (v.CountsAsField ? 8 : 0) |
                        (v.Removable ? 16 : 0) | (v.IsReactionProduct ? 32 : 0) | (v.Soluble ? 64 : 0) | (v.Diluted ? 128 : 0);
            w.Write((byte)flags);
            w.Write(v.DamagePerSecond);
            w.Write((byte)v.Channel);
            w.Write(v.SourceWater);
            v.Columns.WriteTo(w);
        }

        private ObscurationVolume ReadVolume(BinaryReader r)
        {
            int id = r.ReadInt32();
            var kind = (VolumeKind)r.ReadByte();
            var v = new ObscurationVolume(id, kind, _grid.Origin)
            {
                Owner = r.ReadInt32(),
                SourceEventId = r.ReadInt32(),
                FieldId = r.ReadInt32(),
                CreatedTick = r.ReadInt32(),
                ExpiresTick = r.ReadInt32(),
                BaseY = r.ReadSingle(),
                Height = r.ReadSingle(),
            };
            float ox = r.ReadSingle();
            float oz = r.ReadSingle();
            v.Offset = new Vec2(ox, oz);
            int flags = r.ReadByte();
            v.BlocksSight = (flags & 1) != 0;
            v.WindMovable = (flags & 2) != 0;
            v.DrainedByLight = (flags & 4) != 0;
            v.CountsAsField = (flags & 8) != 0;
            v.Removable = (flags & 16) != 0;
            v.IsReactionProduct = (flags & 32) != 0;
            v.Soluble = (flags & 64) != 0;
            v.Diluted = (flags & 128) != 0;
            v.DamagePerSecond = r.ReadSingle();
            v.Channel = (DamageChannel)r.ReadByte();
            v.SourceWater = r.ReadInt32();
            v.Columns.ReadFrom(r);
            return v;
        }

        private static void WriteHazard(BinaryWriter w, Hazard h)
        {
            w.Write(h.Id);
            w.Write((int)h.Kind);
            w.Write(h.Owner);
            w.Write(h.Source);
            w.Write((byte)h.Channel);
            w.Write(h.Amount);
            w.Write(h.PerSecond);
            w.Write(h.CreatedTick);
            w.Write(h.ExpiresTick);
            w.Write((byte)h.Shape);
            WriteVec3(w, h.Box.Center);
            WriteVec3(w, h.Box.HalfExtents);
            w.Write(h.Box.YawDeg);
            w.Write(h.VolumeId);
            w.Write(h.GroundOnly);
            w.Write(h.Soluble);
            w.Write(h.Diluted);
            w.Write(h.ObjectId);
            w.Write(h.CellCount);
            var bodies = new List<int>(h.NextHit.Keys);
            bodies.Sort();
            w.Write(bodies.Count);
            for (int i = 0; i < bodies.Count; i++)
            {
                w.Write(bodies[i]);
                w.Write(h.NextHit[bodies[i]]);
            }
        }

        private static Hazard ReadHazard(BinaryReader r)
        {
            var h = new Hazard
            {
                Id = r.ReadInt32(),
                Kind = (ReactionKind)r.ReadInt32(),
                Owner = r.ReadInt32(),
                Source = r.ReadInt32(),
                Channel = (DamageChannel)r.ReadByte(),
                Amount = r.ReadSingle(),
                PerSecond = r.ReadBoolean(),
                CreatedTick = r.ReadInt32(),
                ExpiresTick = r.ReadInt32(),
                Shape = (HazardShape)r.ReadByte(),
            };
            Vec3 center = ReadVec3(r);
            Vec3 half = ReadVec3(r);
            float yaw = r.ReadSingle();
            h.Box = new YawBox(center, half, yaw);
            h.VolumeId = r.ReadInt32();
            h.GroundOnly = r.ReadBoolean();
            h.Soluble = r.ReadBoolean();
            h.Diluted = r.ReadBoolean();
            h.ObjectId = r.ReadInt32();
            h.CellCount = r.ReadInt32();
            int hits = r.ReadInt32();
            for (int i = 0; i < hits; i++)
            {
                int body = r.ReadInt32();
                h.NextHit[body] = r.ReadInt32();
            }

            return h;
        }

        // ------------------------------------------------------------------ winds

        private static void WriteWind(BinaryWriter w, ActiveWind wind)
        {
            WindGust g = wind.Gust;
            w.Write(wind.Order);
            w.Write(wind.RemainingTicks);
            w.Write(g.Area.HasValue);
            if (g.Area.HasValue)
            {
                WriteFootprint(w, g.Area.Value);
            }

            w.Write(g.Direction.X);
            w.Write(g.Direction.Y);
            w.Write(g.Strength);
            w.Write(g.DurationTicks);
            w.Write(g.Speed);
            w.Write(g.MaxDistance);
            w.Write(g.LocalCloudOverride);
            w.Write(g.TargetVolumeId);
            w.Write(g.Owner);
            w.Write(g.SourceEventId);

            var moved = new List<int>(wind.Moved.Keys);
            moved.Sort();
            w.Write(moved.Count);
            for (int i = 0; i < moved.Count; i++)
            {
                w.Write(moved[i]);
                w.Write(wind.Moved[moved[i]]);
            }

            var dispersed = new List<int>(wind.Dispersed);
            dispersed.Sort();
            w.Write(dispersed.Count);
            for (int i = 0; i < dispersed.Count; i++)
            {
                w.Write(dispersed[i]);
            }
        }

        private static ActiveWind ReadWind(BinaryReader r)
        {
            var wind = new ActiveWind { Order = r.ReadInt32(), RemainingTicks = r.ReadInt32() };
            var g = new WindGust();
            if (r.ReadBoolean())
            {
                g.Area = ReadFootprint(r);
            }

            float dx = r.ReadSingle();
            float dz = r.ReadSingle();
            g.Direction = new Vec2(dx, dz);
            g.Strength = r.ReadInt32();
            g.DurationTicks = r.ReadInt32();
            g.Speed = r.ReadSingle();
            g.MaxDistance = r.ReadSingle();
            g.LocalCloudOverride = r.ReadBoolean();
            g.TargetVolumeId = r.ReadInt32();
            g.Owner = r.ReadInt32();
            g.SourceEventId = r.ReadInt32();
            wind.Gust = g;
            int moved = r.ReadInt32();
            for (int i = 0; i < moved; i++)
            {
                int id = r.ReadInt32();
                wind.Moved[id] = r.ReadSingle();
            }

            int dispersed = r.ReadInt32();
            for (int i = 0; i < dispersed; i++)
            {
                wind.Dispersed.Add(r.ReadInt32());
            }

            return wind;
        }

        // ------------------------------------------------------------------ primitives

        private static void WriteVec3(BinaryWriter w, Vec3 v)
        {
            w.Write(v.X);
            w.Write(v.Y);
            w.Write(v.Z);
        }

        private static Vec3 ReadVec3(BinaryReader r)
        {
            float x = r.ReadSingle();
            float y = r.ReadSingle();
            float z = r.ReadSingle();
            return new Vec3(x, y, z);
        }

        private static void WriteFootprint(BinaryWriter w, Footprint f)
        {
            w.Write((byte)f.Shape);
            w.Write(f.A.X);
            w.Write(f.A.Y);
            w.Write(f.B.X);
            w.Write(f.B.Y);
            w.Write(f.Radius);
            w.Write(f.HalfSize.X);
            w.Write(f.HalfSize.Y);
            w.Write(f.YawDeg);
            w.Write(f.Direction.X);
            w.Write(f.Direction.Y);
            w.Write(f.HalfAngleDeg);
        }

        private static Footprint ReadFootprint(BinaryReader r)
        {
            var shape = (FootprintShape)r.ReadByte();
            float ax = r.ReadSingle();
            float ay = r.ReadSingle();
            float bx = r.ReadSingle();
            float by = r.ReadSingle();
            float radius = r.ReadSingle();
            float hx = r.ReadSingle();
            float hy = r.ReadSingle();
            float yaw = r.ReadSingle();
            float dx = r.ReadSingle();
            float dy = r.ReadSingle();
            float half = r.ReadSingle();
            return Footprint.FromRaw(shape, new Vec2(ax, ay), new Vec2(bx, by), radius, new Vec2(hx, hy), yaw, new Vec2(dx, dy), half);
        }
    }
}
