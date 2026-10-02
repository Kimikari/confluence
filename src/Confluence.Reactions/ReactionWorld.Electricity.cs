using System;
using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions.Electricity;
using Confluence.Reactions.Events;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Hazards;
using Confluence.Reactions.Operations;

namespace Confluence.Reactions
{
    public sealed partial class ReactionWorld
    {
        private readonly Queue<int> _bfsQueue = new Queue<int>();
        private readonly List<int> _patch = new List<int>();
        private readonly List<int> _neighbors = new List<int>(4);
        private readonly List<int> _seedOrder = new List<int>();
        private readonly List<int> _touched = new List<int>();
        private bool[] _visited = new bool[0];

        /// <summary>A conductor's stored branch, released after the glow (spec L149 "object glows before release").</summary>
        private sealed class PendingRelease
        {
            public int ConductorId;
            public Vec3 Contact;
            public int ReleaseTick;
            public int Owner;
            public int Source;
            public float Damage;
            public int HazardId;
        }

        /// <summary>
        /// Phase 3 (spec L165 "electricity connectivity"): discharges in canonical order, then conductor releases whose
        /// glow has finished.
        /// </summary>
        private void RunElectricity()
        {
            _discharges.Sort(CompareDischarges);
            for (int i = 0; i < _discharges.Count; i++)
            {
                ApplyDischarge(_discharges[i]);
            }

            _discharges.Clear();
            ReleaseConductors();
        }

        private static int CompareDischarges(ElectricDischarge a, ElectricDischarge b)
        {
            int c = CompareSourceOwner(a.SourceEventId, a.Owner, b.SourceEventId, b.Owner, a.ConductorId.CompareTo(b.ConductorId));
            if (c != 0) return c;
            c = a.Point.X.CompareTo(b.Point.X);
            if (c != 0) return c;
            c = a.Point.Z.CompareTo(b.Point.Z);
            if (c != 0) return c;
            c = a.Point.Y.CompareTo(b.Point.Y);
            if (c != 0) return c;
            c = a.ReactionDamage.CompareTo(b.ReactionDamage);
            if (c != 0) return c;
            c = a.ChargeSec.CompareTo(b.ChargeSec);
            if (c != 0) return c;
            c = a.Contact.HasValue.CompareTo(b.Contact.HasValue);
            if (c != 0) return c;
            return a.Contact.HasValue ? CompareFootprints(a.Contact.Value, b.Contact.Value) : 0;
        }

        private void ApplyDischarge(ElectricDischarge discharge)
        {
            Conductor struck = FindStruckConductor(discharge);
            if (struck != null)
            {
                ConductThroughMetal(struck, discharge);
                return;
            }

            ChargeWetPatch(discharge);
        }

        private Conductor FindStruckConductor(ElectricDischarge discharge)
        {
            if (discharge.ConductorId >= 0)
            {
                return _conductorById.TryGetValue(discharge.ConductorId, out Conductor byId) ? byId : null;
            }

            Conductor best = null;
            float bestDistance = ReactionRules.ConductorTouchDistance;
            for (int i = 0; i < _conductors.Count; i++)
            {
                float d = _conductors[i].DistanceTo(discharge.Point);
                if (d <= bestDistance && (best == null || d < bestDistance))
                {
                    best = _conductors[i];
                    bestDistance = d;
                }
            }

            return best;
        }

        /// <summary>
        /// L149: electricity travels along one visible conductive object (at most three branches, 6 m each) and is
        /// released after the object glows. A grounded object instead absorbs the branch (L150).
        /// </summary>
        private void ConductThroughMetal(Conductor conductor, ElectricDischarge discharge)
        {
            Vec3 contact = conductor.ClosestPoint(discharge.Point);
            if (conductor.CanAbsorb)
            {
                AbsorbBranch(conductor, discharge.Owner, discharge.SourceEventId, contact, 1);
                return;
            }

            int glow = Ticks.FromSeconds(ReactionRules.ConductorGlowSec);
            var release = new PendingRelease
            {
                ConductorId = conductor.Id,
                Contact = contact,
                ReleaseTick = _tick + glow,
                Owner = discharge.Owner,
                Source = discharge.SourceEventId,
                Damage = MathF.Max(0f, discharge.ReactionDamage),
                HazardId = _nextHazardId++,
            };
            _releases.Add(release);
            Emit(ReactionKind.ConductorGlow, discharge.Owner, discharge.SourceEventId, contact, -1, release.ReleaseTick, conductor.Id);
        }

        /// <summary>L150: a grounded conductor takes one branch; at its stated capacity it overloads and ungrounds.</summary>
        private void AbsorbBranch(Conductor conductor, int owner, int source, Vec3 at, int amount)
        {
            conductor.RemainingCapacity--;
            Emit(ReactionKind.GroundRedirect, owner, source, at, -1, amount, conductor.Id);
            if (conductor.RemainingCapacity <= 0)
            {
                conductor.RemainingCapacity = 0;
                conductor.OverloadedUntil = _tick + Math.Max(1, Ticks.FromSeconds(conductor.Def.OverloadSec));
                Emit(ReactionKind.GroundOverload, owner, source, at, -1, conductor.OverloadedUntil, conductor.Id);
            }
        }

        private void ReleaseConductors()
        {
            if (_releases.Count == 0)
            {
                return;
            }

            _releases.Sort((a, b) =>
            {
                int c = a.ReleaseTick.CompareTo(b.ReleaseTick);
                if (c != 0) return c;
                c = a.Source.CompareTo(b.Source);
                return c != 0 ? c : a.ConductorId.CompareTo(b.ConductorId);
            });

            int done = 0;
            while (done < _releases.Count && _releases[done].ReleaseTick <= _tick)
            {
                PendingRelease r = _releases[done++];
                if (!_conductorById.TryGetValue(r.ConductorId, out Conductor conductor))
                {
                    continue;
                }

                int hits = 0;
                for (int i = 0; i < _bodies.Count; i++)
                {
                    ReactionBody body = _bodies[i];
                    if (!conductor.TouchesEnergizedPart(r.Contact, body.Position, body.Radius, body.Height))
                    {
                        continue;
                    }

                    hits++;
                    if (r.Damage > 0f)
                    {
                        _result.DamageList.Add(new ContactDamage
                        {
                            BodyId = body.Id,
                            Amount = r.Damage,
                            Channel = DamageChannel.Electrical,
                            Owner = r.Owner,
                            SourceEventId = r.Source,
                            Reaction = ReactionKind.ConductorRelease,
                            IsSecondary = true,
                            HazardId = r.HazardId,
                        });
                    }
                }

                Emit(ReactionKind.ConductorRelease, r.Owner, r.Source, r.Contact, -1, hits, conductor.Id);
            }

            _releases.RemoveRange(0, done);
        }

        /// <summary>
        /// L148: charge the connected wet patch for 0.6 s, at most 6 m from the contact and 12 cells. Cells connect
        /// through shared edges; ice, dry cells, and intact insulation (PROP-047) break connectivity. A grounded
        /// conductor touching the patch redirects the whole branch instead (L150).
        /// </summary>
        private void ChargeWetPatch(ElectricDischarge discharge)
        {
            CollectWetPatch(discharge, _patch);
            if (_patch.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _conductors.Count; i++)
            {
                Conductor conductor = _conductors[i];
                if (!conductor.CanAbsorb)
                {
                    continue;
                }

                for (int k = 0; k < _patch.Count; k++)
                {
                    if (conductor.DistanceXZ(_grid.CellCenter(_patch[k])) <= ReactionRules.GroundTouchDistance)
                    {
                        AbsorbBranch(conductor, discharge.Owner, discharge.SourceEventId, CellPoint(_patch[k]), _patch.Count);
                        return;
                    }
                }
            }

            float chargeSec = CMath.Clamp(discharge.ChargeSec, 0f, GlobalRules.World.WetChargeDurationSec);
            int ticks = Math.Max(1, Ticks.FromSeconds(chargeSec));
            Hazard charge = NewHazard(ReactionKind.WetCharge, HazardShape.Cells, discharge.Owner, discharge.SourceEventId,
                DamageChannel.Electrical, MathF.Max(0f, discharge.ReactionDamage), false, _tick + ticks);
            charge.GroundOnly = true;
            for (int k = 0; k < _patch.Count; k++)
            {
                ref Cell c = ref _grid[_patch[k]];
                AttachCellHazard(ref c.ChargeHazard, charge);
                Touch(_patch[k]);
            }

            Emit(ReactionKind.WetCharge, discharge.Owner, discharge.SourceEventId, discharge.Point, _patch[0], _patch.Count, charge.Id);
        }

        private bool Conducts(int index)
        {
            ref Cell c = ref _grid[index];
            if (!c.IsWet)
            {
                return false;
            }

            return (c.Flags & MaterialFlags.Insulating) == 0 || c.Water.Units >= ReactionRules.InsulationFloodWater;
        }

        /// <summary>Breadth-first wet patch from the contact cells (nearest first), bounded by 12 cells and 6 m.</summary>
        private void CollectWetPatch(ElectricDischarge discharge, List<int> patch)
        {
            patch.Clear();
            if (_visited.Length != _grid.CellCount)
            {
                _visited = new bool[_grid.CellCount];
            }

            Vec2 origin = discharge.Point.XZ();
            float maxDistSq = GlobalRules.World.WetChargeMaxPropagation * GlobalRules.World.WetChargeMaxPropagation;
            _seedOrder.Clear();
            if (discharge.Contact.HasValue)
            {
                _grid.Rasterize(discharge.Contact.Value, _seedOrder);
            }
            else if (_grid.TryGetIndex(origin, out int contactCell))
            {
                _seedOrder.Add(contactCell);
            }

            _seedOrder.RemoveAll(i => !Conducts(i));
            _seedOrder.Sort((a, b) =>
            {
                int c = Vec2.DistanceSquared(_grid.CellCenter(a), origin).CompareTo(Vec2.DistanceSquared(_grid.CellCenter(b), origin));
                return c != 0 ? c : a.CompareTo(b);
            });

            _bfsQueue.Clear();
            _touched.Clear();
            for (int i = 0; i < _seedOrder.Count; i++)
            {
                int seed = _seedOrder[i];
                if (!_visited[seed] && Vec2.DistanceSquared(_grid.CellCenter(seed), origin) <= maxDistSq)
                {
                    _visited[seed] = true;
                    _touched.Add(seed);
                    _bfsQueue.Enqueue(seed);
                }
            }

            while (_bfsQueue.Count > 0 && patch.Count < GlobalRules.World.WetChargeMaxCells)
            {
                int index = _bfsQueue.Dequeue();
                patch.Add(index);
                _neighbors.Clear();
                _grid.Neighbors4(index, _neighbors);
                for (int n = 0; n < _neighbors.Count; n++)
                {
                    int next = _neighbors[n];
                    if (_visited[next] || !Conducts(next) || Vec2.DistanceSquared(_grid.CellCenter(next), origin) > maxDistSq)
                    {
                        continue;
                    }

                    _visited[next] = true;
                    _touched.Add(next);
                    _bfsQueue.Enqueue(next);
                }
            }

            for (int i = 0; i < _touched.Count; i++)
            {
                _visited[_touched[i]] = false;
            }

            patch.Sort();
        }
    }
}
