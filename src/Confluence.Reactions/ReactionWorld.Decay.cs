using System;
using System.Collections.Generic;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions.Electricity;
using Confluence.Reactions.Events;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Hazards;
using Confluence.Reactions.Volumes;

namespace Confluence.Reactions
{
    public sealed partial class ReactionWorld
    {
        private readonly List<int> _bodyHazards = new List<int>();
        private readonly List<int> _bodyCells = new List<int>();

        /// <summary>
        /// Phase 5 (spec L165 "decay"): temporary units, ice tiles, burns, burn suppression, damp sand, charges, and
        /// hazards expire; bright light drains shadow concealment (L158); volumes expire; overloaded grounds reset;
        /// then the active cell list and regions are rebuilt.
        /// </summary>
        private void RunDecay()
        {
            List<int> active = CurrentActive();
            for (int i = 0; i < active.Count; i++)
            {
                DecayCell(active[i]);
            }

            for (int i = _hazards.Count - 1; i >= 0; i--)
            {
                Hazard h = _hazards[i];
                bool orphaned = h.Shape == HazardShape.Cells && h.CellCount <= 0;
                bool volumeGone = h.Shape == HazardShape.Volume && !_volumeById.ContainsKey(h.VolumeId);
                if (h.IsExpired(_tick) || orphaned || volumeGone)
                {
                    RemoveHazard(h);
                }
            }

            DecayVolumes();

            for (int i = 0; i < _conductors.Count; i++)
            {
                Conductor c = _conductors[i];
                if (c.OverloadedUntil != 0 && c.OverloadedUntil <= _tick)
                {
                    c.OverloadedUntil = 0;
                    c.RemainingCapacity = Math.Max(0, c.Def.GroundCapacity);
                    Emit(ReactionKind.GroundReset, ReactionRules.MapOwner, ReactionRules.NoSource, c.ClosestPoint(Vec3.Zero), -1, c.RemainingCapacity, c.Id);
                }
            }

            GravityFields.RemoveExpired(_tick);
            RebuildActive();
        }

        private void DecayCell(int index)
        {
            ref Cell c = ref _grid[index];
            for (int m = 0; m < Materials.Count; m++)
            {
                ref MaterialSlot s = ref Cell.Slot(ref c, (Material)m);
                if (s.Temporary > 0 && s.Expiry <= _tick)
                {
                    int expired = TakeUnits(index, (Material)m, s.Temporary);
                    Ledger.AddExpired((Material)m, expired);
                }
            }

            if (c.HasIce && (c.State & CellState.IceNatural) == 0 && c.IceExpiry <= _tick)
            {
                int owner = c.IceOwner;
                int source = c.IceSource;
                RemoveIce(ref c);
                Emit(ReactionKind.IceExpired, owner, source, CellPoint(index), index, 1, 0);
            }

            if (c.IsBurning && c.BurnEnd <= _tick)
            {
                // L153: after its 2 s burn the fire consumes the growth (fuel exhausted, BIO-002).
                int growth = c.Growth.Units;
                Consume(index, Material.Growth, growth);
                if (c.BurnHazard != 0)
                {
                    ReleaseCellHazard(c.BurnHazard);
                    c.BurnHazard = 0;
                }

                c.BurnEnd = 0;
                Emit(ReactionKind.BurnComplete, ReactionRules.MapOwner, ReactionRules.NoSource, CellPoint(index), index, growth, 0);
            }

            if (c.BurnSuppressedUntil != 0 && c.BurnSuppressedUntil <= _tick)
            {
                c.BurnSuppressedUntil = 0;
            }

            if (c.DampUnits > 0 && (c.DampUntil <= _tick || c.Sand.Units + c.Dust.Units == 0))
            {
                c.DampUnits = 0;
                c.DampUntil = 0;
                Emit(ReactionKind.DampExpired, ReactionRules.MapOwner, ReactionRules.NoSource, CellPoint(index), index, 0, 0);
            }

            if (c.ChargeHazard != 0 && (!_hazardById.TryGetValue(c.ChargeHazard, out Hazard charge) || charge.IsExpired(_tick)))
            {
                ReleaseCellHazard(c.ChargeHazard);
                c.ChargeHazard = 0;
            }

            if (c.VenomHazard != 0 && (!_hazardById.TryGetValue(c.VenomHazard, out Hazard venom) || venom.IsExpired(_tick)))
            {
                ReleaseCellHazard(c.VenomHazard);
                c.VenomHazard = 0;
            }
        }

        private void DecayVolumes()
        {
            for (int i = 0; i < _volumes.Count; i++)
            {
                ObscurationVolume v = _volumes[i];
                if (v.DrainedByLight && v.ExpiresTick != int.MaxValue && IsExposedToBrightLight(v))
                {
                    v.ExpiresTick -= 1; // L158: one extra second lost per second of exposure (one tick per tick)
                }
            }

            for (int i = _volumes.Count - 1; i >= 0; i--)
            {
                ObscurationVolume v = _volumes[i];
                if (v.ExpiresTick <= _tick || v.Columns.IsEmpty)
                {
                    Retire(v, VolumeRetireReason.Expired);
                }
            }
        }

        /// <summary>Exposure to a light shaft (L320) or a bright-light volume (PROP-023) over any column (L158).</summary>
        private bool IsExposedToBrightLight(ObscurationVolume volume)
        {
            for (int i = 0; i < _lightShafts.Count; i++)
            {
                LightShaft shaft = _lightShafts[i];
                if (volume.AnyColumnCenterWithin(shaft.Position.XZ(), shaft.Radius))
                {
                    return true;
                }
            }

            for (int i = 0; i < _volumes.Count; i++)
            {
                ObscurationVolume light = _volumes[i];
                if (light == volume || !VolumeKinds.IsBrightLight(light.Kind))
                {
                    continue;
                }

                _columnXs.Clear();
                _columnZs.Clear();
                volume.Columns.GetColumns(_columnXs, _columnZs);
                for (int k = 0; k < _columnXs.Count; k++)
                {
                    Vec2 center = volume.ColumnCenter(_columnXs[k], _columnZs[k]);
                    light.ColumnOf(center, out int lx, out int lz);
                    if (light.Columns.Contains(lx, lz))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // ------------------------------------------------------------------ contact with bodies

        /// <summary>
        /// After the phases, each body (ascending id) touches hazards (ascending id): grounded bodies on charged,
        /// burning, or venom cells; any body against a hot or burning surface box; any body inside a damaging cloud.
        /// Damage requests are secondary and never applied here (spec L126, L672).
        /// </summary>
        private void RunContacts()
        {
            if (_hazards.Count == 0 || _bodies.Count == 0)
            {
                return;
            }

            for (int b = 0; b < _bodies.Count; b++)
            {
                ReactionBody body = _bodies[b];
                _bodyHazards.Clear();
                if (body.IsGrounded)
                {
                    _bodyCells.Clear();
                    _grid.CellsOverlappingDisc(body.Position.XZ(), body.Radius, _bodyCells);
                    for (int i = 0; i < _bodyCells.Count; i++)
                    {
                        ref Cell c = ref _grid[_bodyCells[i]];
                        if (MathF.Abs(body.Position.Y - c.SurfaceY) > ReactionRules.GroundContactTolerance)
                        {
                            continue;
                        }

                        AddUnique(_bodyHazards, c.ChargeHazard);
                        if (c.IsBurning)
                        {
                            AddUnique(_bodyHazards, c.BurnHazard);
                        }

                        AddUnique(_bodyHazards, c.VenomHazard);
                    }
                }

                for (int i = 0; i < _hazards.Count; i++)
                {
                    Hazard h = _hazards[i];
                    if (h.Shape == HazardShape.Box && TouchesBox(body, h.Box))
                    {
                        AddUnique(_bodyHazards, h.Id);
                    }
                    else if (h.Shape == HazardShape.Volume && _volumeById.TryGetValue(h.VolumeId, out ObscurationVolume v) &&
                             v.OverlapsCylinder(body.Position.XZ(), body.Radius, body.Position.Y, body.Top))
                    {
                        AddUnique(_bodyHazards, h.Id);
                    }
                }

                _bodyHazards.Sort();
                for (int i = 0; i < _bodyHazards.Count; i++)
                {
                    if (_hazardById.TryGetValue(_bodyHazards[i], out Hazard hazard) && !hazard.IsExpired(_tick))
                    {
                        Contact(body, hazard);
                    }
                }
            }
        }

        private static void AddUnique(List<int> ids, int id)
        {
            if (id != 0 && !ids.Contains(id))
            {
                ids.Add(id);
            }
        }

        private static bool TouchesBox(ReactionBody body, YawBox box)
        {
            Vec3 axis = new Vec3(body.Position.X, CMath.Clamp(box.Center.Y, body.Position.Y, body.Top), body.Position.Z);
            float reach = body.Radius + ReactionRules.SurfaceContactTolerance;
            return box.DistanceSquared(axis) <= reach * reach;
        }

        private void Contact(ReactionBody body, Hazard hazard)
        {
            float amount;
            if (hazard.PerSecond)
            {
                if (hazard.NextHit.TryGetValue(body.Id, out int next) && next > _tick)
                {
                    return;
                }

                hazard.NextHit[body.Id] = _tick + Ticks.FromSeconds(ReactionRules.ContactIntervalSec);
                amount = hazard.Amount * ReactionRules.ContactIntervalSec * hazard.Factor;
            }
            else
            {
                if (hazard.NextHit.ContainsKey(body.Id))
                {
                    return; // L148: only the first contact deals the declared reaction damage
                }

                hazard.NextHit[body.Id] = int.MaxValue;
                amount = hazard.Amount * hazard.Factor;
            }

            if (!(amount > 0f))
            {
                return;
            }

            _result.DamageList.Add(new ContactDamage
            {
                BodyId = body.Id,
                Amount = amount,
                Channel = hazard.Channel,
                Owner = hazard.Owner,
                SourceEventId = hazard.Source,
                Reaction = hazard.Kind,
                IsSecondary = true,
                HazardId = hazard.Id,
            });
        }
    }
}
