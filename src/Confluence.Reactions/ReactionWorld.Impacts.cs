using System;
using System.Collections.Generic;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions.Environment;
using Confluence.Reactions.Events;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Hazards;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Volumes;

namespace Confluence.Reactions
{
    public sealed partial class ReactionWorld
    {
        /// <summary>
        /// Phase 1 (spec L165 "incoming direct impacts"): announced refill pulses and gusts, new volumes, deposits,
        /// condensing, neutralization, dust raising, impacts (ice wear and shatter, brittle solids, combustible
        /// solids), prop forces, dispels, and hot surfaces. Each list is applied in canonical order.
        /// </summary>
        private void RunDirectImpacts()
        {
            FireScheduledSources();
            FireScheduledGusts();

            _newVolumes.Sort(CompareVolumeSpecs);
            for (int i = 0; i < _newVolumes.Count; i++)
            {
                CreateVolumeNow(_newVolumes[i]);
            }

            _newVolumes.Clear();

            _deposits.Sort(CompareDeposits);
            for (int i = 0; i < _deposits.Count; i++)
            {
                ApplyDeposit(_deposits[i]);
            }

            _deposits.Clear();

            _condenses.Sort((a, b) => CompareSourceOwner(a.SourceEventId, a.Owner, b.SourceEventId, b.Owner, a.VolumeId.CompareTo(b.VolumeId)));
            for (int i = 0; i < _condenses.Count; i++)
            {
                ApplyCondense(_condenses[i]);
            }

            _condenses.Clear();

            _removals.Sort((a, b) => CompareSourceOwner(a.SourceEventId, a.Owner, b.SourceEventId, b.Owner, CompareFootprints(a.Footprint, b.Footprint)));
            for (int i = 0; i < _removals.Count; i++)
            {
                ApplyRemoval(_removals[i]);
            }

            _removals.Clear();

            _dustRaises.Sort((a, b) => CompareSourceOwner(a.SourceEventId, a.Owner, b.SourceEventId, b.Owner, CompareFootprints(a.Footprint, b.Footprint)));
            for (int i = 0; i < _dustRaises.Count; i++)
            {
                ApplyDustRaise(_dustRaises[i]);
            }

            _dustRaises.Clear();

            _impacts.Sort(CompareImpacts);
            for (int i = 0; i < _impacts.Count; i++)
            {
                ApplyImpact(_impacts[i]);
            }

            _impacts.Clear();

            _forces.Sort((a, b) => CompareSourceOwner(a.SourceEventId, a.Owner, b.SourceEventId, b.Owner, a.PropId.CompareTo(b.PropId)));
            for (int i = 0; i < _forces.Count; i++)
            {
                ApplyPropForce(_forces[i]);
            }

            _forces.Clear();

            _dispels.Sort((a, b) => CompareSourceOwner(a.SourceEventId, a.Owner, b.SourceEventId, b.Owner, CompareFootprints(a.Area, b.Area)));
            for (int i = 0; i < _dispels.Count; i++)
            {
                ApplyDispel(_dispels[i]);
            }

            _dispels.Clear();

            _hotSurfaces.Sort((a, b) => CompareSourceOwner(a.SourceEventId, a.Owner, b.SourceEventId, b.Owner, a.ObjectId.CompareTo(b.ObjectId)));
            for (int i = 0; i < _hotSurfaces.Count; i++)
            {
                ApplyHotSurface(_hotSurfaces[i]);
            }

            _hotSurfaces.Clear();
        }

        private static int CompareSourceOwner(int sourceA, int ownerA, int sourceB, int ownerB, int then)
        {
            int c = sourceA.CompareTo(sourceB);
            if (c != 0) return c;
            c = ownerA.CompareTo(ownerB);
            return c != 0 ? c : then;
        }

        // ------------------------------------------------------------------ environment

        private void FireScheduledSources()
        {
            for (int i = 0; i < _sources.Count; i++)
            {
                ScheduledSource source = _sources[i];
                while (source.NextPulseTick <= _tick)
                {
                    int added = 0;
                    for (int c = 0; c < source.Cells.Count; c++)
                    {
                        added += AddNatural(source.Cells[c], source.Material, source.UnitsPerPulse, source.MaxUnitsPerCell);
                    }

                    Ledger.AddRefilled(source.Material, added);
                    int first = source.Cells.Count > 0 ? source.Cells[0] : -1;
                    Emit(ReactionKind.RefillPulse, ReactionRules.MapOwner, ReactionRules.NoSource,
                        first >= 0 ? CellPoint(first) : Vec3.Zero, first, added, source.Id);
                    source.PulsesFired++;
                    source.NextPulseTick = source.IntervalTicks > 0 ? source.NextPulseTick + source.IntervalTicks : int.MaxValue;
                }
            }
        }

        private void FireScheduledGusts()
        {
            for (int i = 0; i < _gusts.Count; i++)
            {
                ScheduledGust g = _gusts[i];
                if (g.Fired || g.StartTick > _tick)
                {
                    continue;
                }

                g.Fired = true;
                ActivateWind(g.Gust);
                Emit(ReactionKind.GustStarted, g.Gust.Owner, g.Gust.SourceEventId, Vec3.Zero, -1, g.Gust.Strength, i);
            }
        }

        // ------------------------------------------------------------------ deposits (L163)

        private static int CompareDeposits(MaterialDeposit a, MaterialDeposit b)
        {
            int c = a.SourceEventId.CompareTo(b.SourceEventId);
            if (c != 0) return c;
            c = a.Owner.CompareTo(b.Owner);
            if (c != 0) return c;
            c = a.Material.CompareTo(b.Material);
            if (c != 0) return c;
            c = CompareFootprints(a.Footprint, b.Footprint);
            if (c != 0) return c;
            c = a.Strength.CompareTo(b.Strength);
            if (c != 0) return c;
            c = a.Pattern.CompareTo(b.Pattern);
            if (c != 0) return c;
            c = a.AdjacentStrength.CompareTo(b.AdjacentStrength);
            if (c != 0) return c;
            c = a.LifetimeTicks.CompareTo(b.LifetimeTicks);
            if (c != 0) return c;
            c = a.FieldId.CompareTo(b.FieldId);
            if (c != 0) return c;
            c = a.SurfaceY.CompareTo(b.SurfaceY);
            if (c != 0) return c;
            c = a.DeclaredDamagePerSecond.CompareTo(b.DeclaredDamagePerSecond);
            if (c != 0) return c;
            c = a.Combustible.CompareTo(b.Combustible);
            if (c != 0) return c;
            c = a.Sticky.CompareTo(b.Sticky);
            if (c != 0) return c;
            c = a.Soluble.CompareTo(b.Soluble);
            return c != 0 ? c : a.Removable.CompareTo(b.Removable);
        }

        private void ApplyDeposit(MaterialDeposit d)
        {
            if (d.Strength <= 0 && (d.Pattern != DepositPattern.HeavyImpact || d.AdjacentStrength <= 0))
            {
                return;
            }

            bool permanent = d.Owner == ReactionRules.MapOwner && d.LifetimeTicks == int.MaxValue;
            int expiry = permanent ? int.MaxValue : _tick + PlayerLifetimeCap(d.Owner, d.LifetimeTicks);
            NoteFieldLayer(d.FieldId, d.Owner, d.Removable);

            _scratchCells.Clear();
            _grid.Rasterize(d.Footprint, _scratchCells);
            int impactCell = -1;
            if (d.Pattern == DepositPattern.HeavyImpact && !_grid.TryGetIndex(d.Footprint.Anchor, out impactCell))
            {
                return; // the impact itself is outside the arena grid
            }

            Hazard venom = null;
            for (int i = 0; i < _scratchCells.Count; i++)
            {
                int index = _scratchCells[i];
                int strength = d.Pattern == DepositPattern.HeavyImpact && index != impactCell ? d.AdjacentStrength : d.Strength;
                if (strength <= 0)
                {
                    continue;
                }

                int added = permanent
                    ? AddNatural(index, d.Material, strength, Materials.MaxUnits)
                    : AddTemporary(index, d.Material, strength, expiry, d.Owner, d.SourceEventId, d.FieldId, d.DeclaredDamagePerSecond);
                if (permanent)
                {
                    Ledger.AddRefilled(d.Material, added);
                }
                else
                {
                    Ledger.AddDeposited(d.Material, added);
                }

                Ledger.AddClipped(d.Material, strength - added);
                if (added <= 0)
                {
                    continue;
                }

                ref Cell c = ref _grid[index];
                if ((c.State & CellState.HasSurface) == 0)
                {
                    c.SurfaceY = d.SurfaceY;
                    c.State |= CellState.HasSurface;
                }

                if (d.Material == Material.Growth)
                {
                    c.State = d.Combustible ? c.State | CellState.GrowthCombustible : c.State & ~CellState.GrowthCombustible;
                    c.State = d.Sticky ? c.State | CellState.GrowthSticky : c.State & ~CellState.GrowthSticky;
                }
                else if (d.Material == Material.Venom)
                {
                    if (venom == null)
                    {
                        // One venom patch per deposit: the original bounded patch that dilution affects (L159).
                        venom = NewHazard(ReactionKind.VenomContact, HazardShape.Cells, d.Owner, d.SourceEventId,
                            DamageChannel.Corrosive, MathF.Max(0f, d.DeclaredDamagePerSecond), true, expiry);
                        venom.GroundOnly = true;
                        venom.Soluble = d.Soluble;
                    }

                    AttachCellHazard(ref c.VenomHazard, venom);
                }
            }
        }

        // ------------------------------------------------------------------ condense (L167)

        private void ApplyCondense(CondenseRequest request)
        {
            ObscurationVolume volume = FindVolume(request.VolumeId);
            if (volume == null)
            {
                return;
            }

            int budget = Math.Max(0, Math.Min(volume.SourceWater, request.MaxUnits));
            int expiry = _tick + PlayerLifetimeCap(request.Owner, request.LifetimeTicks);
            int total = 0;
            _columnXs.Clear();
            _columnZs.Clear();
            volume.Columns.GetColumns(_columnXs, _columnZs);
            for (int i = 0; i < _columnXs.Count && total < budget; i++)
            {
                if (!_grid.TryGetIndex(volume.ColumnCenter(_columnXs[i], _columnZs[i]), out int index))
                {
                    continue;
                }

                int added = AddTemporary(index, Material.Water, 1, expiry, request.Owner, request.SourceEventId, request.FieldId, 0f);
                total += added;
            }

            Ledger.AddProduced(Material.Water, total);
            Ledger.CondensedWater += total;
            Emit(ReactionKind.Condense, request.Owner, request.SourceEventId, Vec3.Zero, -1, total, volume.Id);
            Retire(volume, VolumeRetireReason.Condensed);
        }

        // ------------------------------------------------------------------ neutralization

        private void ApplyRemoval(UnitRemoval removal)
        {
            if (removal.Materials == null || removal.Materials.Length == 0)
            {
                return;
            }

            _scratchCells.Clear();
            _grid.Rasterize(removal.Footprint, _scratchCells);
            int removed = 0;
            for (int i = 0; i < _scratchCells.Count && removed < removal.MaxTotal; i++)
            {
                int index = _scratchCells[i];
                int inCell = 0;
                for (int m = 0; m < removal.Materials.Length && inCell < removal.MaxPerCell && removed < removal.MaxTotal; m++)
                {
                    Material material = removal.Materials[m];
                    int want = Math.Min(removal.MaxPerCell - inCell, removal.MaxTotal - removed);
                    int took = TakeUnits(index, material, want);
                    Ledger.AddRemoved(material, took);
                    inCell += took;
                    removed += took;
                }
            }

            Emit(ReactionKind.UnitsRemoved, removal.Owner, removal.SourceEventId, removal.Footprint.Anchor.ToXZ(), -1, removed, 0);
        }

        // ------------------------------------------------------------------ dust production (L154)

        private void ApplyDustRaise(DustRaise raise)
        {
            if (raise.LifetimeTicks <= 0)
            {
                return;
            }

            _scratchCells.Clear();
            _grid.Rasterize(raise.Footprint, _scratchCells);
            ObscurationVolume cloud = null;
            for (int i = 0; i < _scratchCells.Count; i++)
            {
                int index = _scratchCells[i];
                ref Cell c = ref _grid[index];
                if (DustProduction(ref c) <= 0)
                {
                    continue;
                }

                Material from = c.Dust.Units > 0 ? Material.Dust : Material.Sand;
                Ledger.AddConsumed(from, TakeUnits(index, from, 1));
                if (cloud == null)
                {
                    cloud = NewVolume(VolumeKind.Dust, raise.Owner, raise.SourceEventId, 0, raise.BaseY, raise.Height, raise.LifetimeTicks);
                }

                cloud.Columns.Add(_grid.XOf(index), _grid.ZOf(index));
            }

            if (cloud != null)
            {
                RegisterVolume(cloud);
                Emit(ReactionKind.DustRaised, raise.Owner, raise.SourceEventId, raise.Footprint.Anchor.ToXZ(raise.BaseY), -1, cloud.ColumnCount, cloud.Id);
            }
        }

        // ------------------------------------------------------------------ impacts (L145-L147, L153)

        private static int CompareImpacts(DirectImpact a, DirectImpact b)
        {
            int c = CompareSourceOwner(a.SourceEventId, a.Owner, b.SourceEventId, b.Owner, a.SolidId.CompareTo(b.SolidId));
            if (c != 0) return c;
            c = a.Point.X.CompareTo(b.Point.X);
            if (c != 0) return c;
            c = a.Point.Z.CompareTo(b.Point.Z);
            if (c != 0) return c;
            c = a.Point.Y.CompareTo(b.Point.Y);
            if (c != 0) return c;
            c = a.DirectDamage.CompareTo(b.DirectDamage);
            if (c != 0) return c;
            c = a.HeatStrength.CompareTo(b.HeatStrength);
            if (c != 0) return c;
            c = a.Direction.X.CompareTo(b.Direction.X);
            if (c != 0) return c;
            c = a.Direction.Z.CompareTo(b.Direction.Z);
            return c != 0 ? c : a.BurnDamagePerSecond.CompareTo(b.BurnDamagePerSecond);
        }

        private void ApplyImpact(DirectImpact impact)
        {
            if (impact.SolidId >= 0)
            {
                ImpactSolid(impact);
                return;
            }

            if (!_grid.TryGetIndex(impact.Point, out int index))
            {
                return;
            }

            ref Cell c = ref _grid[index];
            if (!c.HasIce || !(impact.DirectDamage > 0f))
            {
                return;
            }

            Vec3 tile = CellPoint(index);
            if (impact.DirectDamage >= GlobalRules.World.ShatterMinDirectDamage)
            {
                // L147: breaks one struck tile and emits a 1 m shard cone for 10 physical, once per impact.
                RemoveIce(ref c);
                Emit(ReactionKind.IceShattered, impact.Owner, impact.SourceEventId, tile, index, 1, -1);
                ShardCone(tile, impact.Direction, impact.Owner, impact.SourceEventId, index);
                return;
            }

            // L145: weaker hits wear the tile's 45 durability; a worn-out tile breaks without shards.
            c.IceDurability -= impact.DirectDamage;
            if (c.IceDurability <= 0f)
            {
                RemoveIce(ref c);
                Emit(ReactionKind.IceShattered, impact.Owner, impact.SourceEventId, tile, index, 0, -1);
            }
            else
            {
                Emit(ReactionKind.IceWorn, impact.Owner, impact.SourceEventId, tile, index, (int)MathF.Ceiling(c.IceDurability), -1);
            }
        }

        private static void RemoveIce(ref Cell c)
        {
            c.IceDurability = 0f;
            c.IceExpiry = 0;
            c.IceOwner = ReactionRules.MapOwner;
            c.IceSource = ReactionRules.NoSource;
            c.State &= ~CellState.IceNatural;
        }

        private void ImpactSolid(DirectImpact impact)
        {
            if (!_solids.TryGetValue(impact.SolidId, out Solid solid))
            {
                return;
            }

            if ((solid.Flags & MaterialFlags.Brittle) != 0 && impact.DirectDamage >= GlobalRules.World.ShatterMinDirectDamage)
            {
                // L147 on brittle cover (MOD-047 ice covers, BIO-021 crystal covers): Sim removes the struck subpiece.
                Emit(ReactionKind.IceShattered, impact.Owner, impact.SourceEventId, impact.Point, -1, 1, solid.Id);
                ShardCone(impact.Point, impact.Direction, impact.Owner, impact.SourceEventId, -1);
            }

            // L153, L163: heat ignites only material whose flags permit it; painted stone never becomes combustible.
            bool combustible = (solid.Flags & (MaterialFlags.Combustible | MaterialFlags.Growth)) != 0;
            if (combustible && impact.HeatStrength >= GlobalRules.World.GrowthIgniteMinHeat && _ignitedSolids.Add(solid.Id))
            {
                Emit(ReactionKind.SolidIgnited, impact.Owner, impact.SourceEventId, impact.Point, -1, impact.HeatStrength, solid.Id);
                if (impact.BurnDamagePerSecond > 0f)
                {
                    Hazard burn = NewHazard(ReactionKind.BurnContact, HazardShape.Box, impact.Owner, impact.SourceEventId,
                        DamageChannel.Thermal, impact.BurnDamagePerSecond, true, _tick + Ticks.FromSeconds(GlobalRules.World.GrowthBurnSec));
                    burn.Box = solid.Box;
                    burn.ObjectId = solid.Id;
                }
            }
        }

        /// <summary>The standard shard cone: 1 m, 10 physical, each body at most once per impact (spec L147).</summary>
        private void ShardCone(Vec3 apex, Vec3 direction, int owner, int source, int cellIndex)
        {
            Vec2 dir = direction.XZ();
            float halfAngle = ReactionRules.ShardConeHalfAngleDeg;
            if (dir.LengthSquared() < CMath.Epsilon)
            {
                dir = Vec2.Forward;
                halfAngle = 180f; // a vertical strike sprays shards all around the tile
            }

            dir = dir.Normalized();
            int hazardId = _nextHazardId++;
            int hits = 0;
            float length = GlobalRules.World.ShardConeLength;
            float cosHalf = MathF.Cos(CMath.DegToRad(halfAngle));
            for (int i = 0; i < _bodies.Count; i++)
            {
                ReactionBody body = _bodies[i];
                if (body.Top < apex.Y - length || body.Position.Y > apex.Y + length)
                {
                    continue;
                }

                Vec2 d = body.Position.XZ() - apex.XZ();
                float dist = d.Length();
                bool inside;
                if (dist <= body.Radius)
                {
                    inside = true;
                }
                else if (dist > length + body.Radius)
                {
                    inside = false;
                }
                else
                {
                    float cos = Vec2.Dot(d, dir) / dist;
                    float widen = MathF.Asin(CMath.Clamp(body.Radius / dist, 0f, 1f));
                    inside = halfAngle >= 180f || cos >= MathF.Cos(MathF.Min(CMath.Pi, CMath.DegToRad(halfAngle) + widen)) || cos >= cosHalf;
                }

                if (!inside)
                {
                    continue;
                }

                hits++;
                _result.DamageList.Add(new ContactDamage
                {
                    BodyId = body.Id,
                    Amount = GlobalRules.World.ShardDamage,
                    Channel = DamageChannel.Physical,
                    Owner = owner,
                    SourceEventId = source,
                    Reaction = ReactionKind.ShardCone,
                    IsSecondary = true,
                    HazardId = hazardId,
                });
            }

            Emit(ReactionKind.ShardCone, owner, source, apex, cellIndex, hits, hazardId);
        }

        // ------------------------------------------------------------------ props (L155, L173)

        private void ApplyPropForce(PropForce force)
        {
            bool anchored = force.Mass == PropMass.Anchored;
            Vec2 dir = force.Direction.XZ().Normalized();
            if (dir.LengthSquared() < 0.5f)
            {
                dir = Vec2.Forward;
            }

            float distance = anchored ? 0f : MathF.Max(0f, MathF.Min(force.RequestedDistance, GlobalRules.Terrain.PushDistance(force.Mass)));
            float speed = anchored ? 0f : MathF.Max(0f, MathF.Min(force.RequestedSpeed, GlobalRules.Terrain.DamagingPropVelocityCap));
            var impulse = new PropImpulse
            {
                PropId = force.PropId,
                Mass = force.Mass,
                Direction = dir.ToXZ(),
                Distance = distance,
                MaxSpeed = speed,
                CollisionDamageCap = MathF.Min(GlobalRules.Terrain.CollisionDamage(force.Mass), GlobalRules.World.PropCollisionDamageCap),
                ShakeOnly = anchored,
                Owner = force.Owner,
                SourceEventId = force.SourceEventId,
            };
            _result.ImpulseList.Add(impulse);
            Emit(ReactionKind.PropImpulse, force.Owner, force.SourceEventId, Vec3.Zero, -1, (int)MathF.Round(distance * 100f), force.PropId);
        }

        // ------------------------------------------------------------------ dispel (L161)

        private void ApplyDispel(DispelRequest dispel)
        {
            // Candidates: removable ground field layers with temporary units in the area, and removable volumes
            // touching it. The oldest (creation tick, ground before volume, lowest id) is consumed.
            int bestTick = int.MaxValue;
            int bestKind = int.MaxValue;
            int bestId = int.MaxValue;

            _scratchCells.Clear();
            _grid.Rasterize(dispel.Area, _scratchCells);
            for (int i = 0; i < _scratchCells.Count; i++)
            {
                ref Cell c = ref _grid[_scratchCells[i]];
                for (int m = 0; m < Materials.Count; m++)
                {
                    ref MaterialSlot s = ref Cell.Slot(ref c, (Material)m);
                    if (s.Temporary <= 0 || s.Field == 0 || !_fieldLayers.TryGetValue(s.Field, out FieldLayer layer) || !layer.Removable)
                    {
                        continue;
                    }

                    ConsiderDispelCandidate(layer.CreatedTick, 0, layer.Id, ref bestTick, ref bestKind, ref bestId);
                }
            }

            for (int i = 0; i < _volumes.Count; i++)
            {
                ObscurationVolume v = _volumes[i];
                if (v.Removable && VolumeTouches(v, dispel.Area))
                {
                    ConsiderDispelCandidate(v.CreatedTick, 1, v.Id, ref bestTick, ref bestKind, ref bestId);
                }
            }

            Vec3 at = dispel.Area.Anchor.ToXZ();
            if (bestKind == int.MaxValue)
            {
                Emit(ReactionKind.Dispel, dispel.Owner, dispel.SourceEventId, at, -1, 0, 0);
                return;
            }

            if (bestKind == 1)
            {
                ObscurationVolume v = FindVolume(bestId);
                Emit(ReactionKind.DispelVolume, dispel.Owner, dispel.SourceEventId, at, -1, v.ColumnCount, v.Id);
                Retire(v, VolumeRetireReason.Dispelled);
                return;
            }

            int removed = RemoveFieldLayer(bestId);
            Emit(ReactionKind.Dispel, dispel.Owner, dispel.SourceEventId, at, -1, removed, bestId);
        }

        private static void ConsiderDispelCandidate(int tick, int kind, int id, ref int bestTick, ref int bestKind, ref int bestId)
        {
            if (tick < bestTick || (tick == bestTick && (kind < bestKind || (kind == bestKind && id < bestId))))
            {
                bestTick = tick;
                bestKind = kind;
                bestId = id;
            }
        }

        /// <summary>Removes a field layer's remaining temporary units everywhere; never natural units or solids (L161).</summary>
        private int RemoveFieldLayer(int fieldId)
        {
            int removed = 0;
            List<int> active = CurrentActive();
            for (int i = 0; i < active.Count; i++)
            {
                ref Cell c = ref _grid[active[i]];
                for (int m = 0; m < Materials.Count; m++)
                {
                    ref MaterialSlot s = ref Cell.Slot(ref c, (Material)m);
                    if (s.Field != fieldId || s.Temporary <= 0)
                    {
                        continue;
                    }

                    int took = TakeUnits(active[i], (Material)m, s.Temporary);
                    Ledger.AddRemoved((Material)m, took);
                    removed += took;
                }
            }

            _fieldLayers.Remove(fieldId);
            return removed;
        }

        // ------------------------------------------------------------------ hot surfaces

        private void ApplyHotSurface(HotSurface surface)
        {
            int lifetime = PlayerLifetimeCap(surface.Owner, surface.LifetimeTicks);
            if (lifetime <= 0 || !(surface.DamagePerSecond > 0f))
            {
                return;
            }

            Hazard hot = NewHazard(ReactionKind.HotSurface, HazardShape.Box, surface.Owner, surface.SourceEventId,
                DamageChannel.Thermal, surface.DamagePerSecond, true, _tick + lifetime);
            hot.Box = surface.Box;
            hot.ObjectId = surface.ObjectId;
        }
    }
}
