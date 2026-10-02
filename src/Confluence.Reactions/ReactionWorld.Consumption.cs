using System;
using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions.Events;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Hazards;
using Confluence.Reactions.Volumes;

namespace Confluence.Reactions
{
    public sealed partial class ReactionWorld
    {
        // Steam volumes and burn hazards formed this step, keyed by committing owner and source event.
        private readonly Dictionary<(int, int), ObscurationVolume> _steamThisStep = new Dictionary<(int, int), ObscurationVolume>();
        private readonly Dictionary<(int, int, float), Hazard> _burnThisStep = new Dictionary<(int, int, float), Hazard>();
        private readonly List<(int, int)> _steamOrder = new List<(int, int)>();
        private readonly Dictionary<(int, int), int> _steamUnits = new Dictionary<(int, int), int>();

        /// <summary>
        /// Phase 2 (spec L165 "heat-water/heat-ice/frost-water consumption"). Cells are visited in index order; in each
        /// cell the reactions below repeat until none applies, and every reaction consumes its stated inputs before the
        /// next one reads the cell:
        /// 1. water on burning material: 1 water removes 1 heat and suppresses the burn 2 s (L152);
        /// 2. heat and water: 1 heat + 1 water form steam in the cell (L144);
        /// 3. heat and ice: 2 heat melt the tile into 1 water (L146), which further heat may steam;
        /// 4. frost and water: 2 frost + 1 water form an ice tile, 5 s, 45 durability (L145);
        /// 5. heat and combustible growth: 2 heat ignite it for 2 s (L153);
        /// 6. water and sand or dust: each water unit dampens one unit for 3 s (L154).
        /// Soluble venom touching liquid water dilutes to 50 percent without spreading (L159).
        /// </summary>
        private void RunConsumption()
        {
            _steamThisStep.Clear();
            _burnThisStep.Clear();
            _steamOrder.Clear();
            _steamUnits.Clear();

            List<int> active = CurrentActive();
            for (int i = 0; i < active.Count; i++)
            {
                ReactCell(active[i]);
            }

            for (int i = 0; i < _steamOrder.Count; i++)
            {
                ObscurationVolume steam = _steamThisStep[_steamOrder[i]];
                Emit(ReactionKind.Steam, steam.Owner, steam.SourceEventId, Vec3.Zero, -1, _steamUnits[_steamOrder[i]], steam.Id);
            }

            DiluteVenomClouds();
        }

        private void ReactCell(int index)
        {
            ref Cell c = ref _grid[index];
            for (int iteration = 0; iteration < ReactionRules.ConsumptionIterationLimit; iteration++)
            {
                if (c.IsBurning && c.Water.Units > 0)
                {
                    Extinguish(index, ref c);
                    continue;
                }

                if (c.Heat.Units > 0 && c.IsWet)
                {
                    FormSteam(index, ref c);
                    continue;
                }

                if (c.HasIce && c.Heat.Units >= GlobalRules.World.MeltHeatConsumed)
                {
                    MeltIce(index, ref c);
                    continue;
                }

                if (!c.HasIce && c.Frost.Units >= GlobalRules.World.IceFrostConsumed && c.Water.Units >= GlobalRules.World.IceWaterConsumed)
                {
                    FormIce(index, ref c);
                    continue;
                }

                if (!c.IsBurning && c.Growth.Units > 0 && (c.State & CellState.GrowthCombustible) != 0 &&
                    c.Heat.Units >= GlobalRules.World.GrowthIgniteMinHeat && c.BurnSuppressedUntil <= _tick)
                {
                    Ignite(index, ref c);
                    continue;
                }

                if (c.IsWet && c.Sand.Units + c.Dust.Units > c.DampUnits)
                {
                    Dampen(index, ref c);
                    continue;
                }

                break;
            }

            if (c.VenomHazard != 0 && c.IsWet && _hazardById.TryGetValue(c.VenomHazard, out Hazard venom) && venom.Soluble && !venom.Diluted)
            {
                venom.Diluted = true;
                Emit(ReactionKind.VenomDilution, venom.Owner, venom.Source, CellPoint(index), index, 1, venom.Id);
            }
        }

        private void Consume(int index, Material material, int units)
        {
            Ledger.AddConsumed(material, TakeUnits(index, material, units));
        }

        /// <summary>L152: one water unit removes one heat unit and suppresses that cell's burn for 2 seconds.</summary>
        private void Extinguish(int index, ref Cell c)
        {
            int owner = c.Water.Owner;
            int source = c.Water.Source;
            Consume(index, Material.Water, GlobalRules.World.ExtinguishWaterConsumed);
            Consume(index, Material.Heat, GlobalRules.World.ExtinguishHeatRemoved);
            if (c.BurnHazard != 0)
            {
                ReleaseCellHazard(c.BurnHazard);
                c.BurnHazard = 0;
            }

            c.BurnEnd = 0;
            c.BurnSuppressedUntil = _tick + Ticks.FromSeconds(GlobalRules.World.BurnSuppressionSec);
            Emit(ReactionKind.Extinguish, owner, source, CellPoint(index), index, 1, 0);
        }

        /// <summary>
        /// L144: one heat and one water unit form steam in the cell (3 s base lifetime, 2 m rise, no damage). The last
        /// direct source of heat or water owns the cloud (L670); one cloud per owner and source event.
        /// </summary>
        private void FormSteam(int index, ref Cell c)
        {
            Commit(ref c.Heat, ref c.Water, out int owner, out int source);
            Consume(index, Material.Heat, GlobalRules.World.SteamHeatConsumed);
            Consume(index, Material.Water, GlobalRules.World.SteamWaterConsumed);
            Ledger.SteamWater += GlobalRules.World.SteamWaterConsumed;

            var key = (owner, source);
            if (!_steamThisStep.TryGetValue(key, out ObscurationVolume steam))
            {
                steam = FindExtendableSteam(owner, source);
                if (steam == null)
                {
                    steam = NewVolume(VolumeKind.Steam, owner, source, 0, c.SurfaceY, ReactionRules.SteamHeight,
                        Ticks.FromSeconds(GlobalRules.World.SteamLifetimeSec));
                    steam.IsReactionProduct = true;
                    RegisterVolume(steam);
                }
                else
                {
                    steam.ExpiresTick = Math.Max(steam.ExpiresTick, _tick + Ticks.FromSeconds(GlobalRules.World.SteamLifetimeSec));
                }

                _steamThisStep[key] = steam;
                _steamOrder.Add(key);
                _steamUnits[key] = 0;
            }

            steam.Columns.Add(_grid.XOf(index), _grid.ZOf(index));
            steam.SourceWater += GlobalRules.World.SteamWaterConsumed;
            _steamUnits[key] = _steamUnits[key] + 1;
        }

        /// <summary>An unmoved steam cloud from the same owner and source keeps growing instead of taking a new field slot.</summary>
        private ObscurationVolume FindExtendableSteam(int owner, int source)
        {
            for (int i = 0; i < _volumes.Count; i++)
            {
                ObscurationVolume v = _volumes[i];
                if (v.Kind == VolumeKind.Steam && v.IsReactionProduct && v.Owner == owner && v.SourceEventId == source &&
                    v.Offset == Vec2.Zero && v.ExpiresTick > _tick)
                {
                    return v;
                }
            }

            return null;
        }

        /// <summary>L146: two heat units melt a tile into one water unit, consuming the heat; the water inherits the tile's lifetime.</summary>
        private void MeltIce(int index, ref Cell c)
        {
            int owner = c.Heat.Owner;
            int source = c.Heat.Source;
            Consume(index, Material.Heat, GlobalRules.World.MeltHeatConsumed);
            bool natural = (c.State & CellState.IceNatural) != 0;
            int expiry = c.IceExpiry;
            RemoveIce(ref c);
            int added = natural
                ? AddNatural(index, Material.Water, GlobalRules.World.MeltWaterProduced, Materials.MaxUnits)
                : AddTemporary(index, Material.Water, GlobalRules.World.MeltWaterProduced, expiry, owner, source, 0, 0f);
            Ledger.AddProduced(Material.Water, added);
            Ledger.IceTilesMelted++;
            Emit(ReactionKind.IceMelted, owner, source, CellPoint(index), index, added, 0);
        }

        /// <summary>L145: two frost units consume one water unit and form an ice tile for 5 s with 45 durability.</summary>
        private void FormIce(int index, ref Cell c)
        {
            Commit(ref c.Frost, ref c.Water, out int owner, out int source);
            Consume(index, Material.Frost, GlobalRules.World.IceFrostConsumed);
            Consume(index, Material.Water, GlobalRules.World.IceWaterConsumed);
            c.IceDurability = GlobalRules.World.IceTileDurability;
            c.IceExpiry = _tick + Ticks.FromSeconds(GlobalRules.World.IceLifetimeSec);
            c.IceOwner = owner;
            c.IceSource = source;
            c.State &= ~CellState.IceNatural;
            Ledger.IceTilesFormed++;
            Emit(ReactionKind.IceFormed, owner, source, CellPoint(index), index, 1, 0);
        }

        /// <summary>
        /// L153: at least two heat units ignite combustible growth; the heat is consumed into the fire, which burns 2 s,
        /// deals the growth's declared damage (falling back to the igniting source's), then consumes the growth.
        /// </summary>
        private void Ignite(int index, ref Cell c)
        {
            int owner = c.Heat.Owner;
            int source = c.Heat.Source;
            float dps = c.Growth.Declared > 0f ? c.Growth.Declared : c.Heat.Declared;
            Consume(index, Material.Heat, GlobalRules.World.GrowthIgniteMinHeat);
            int burnTicks = Ticks.FromSeconds(GlobalRules.World.GrowthBurnSec);
            c.BurnEnd = _tick + burnTicks;
            if (dps > 0f)
            {
                var key = (owner, source, dps);
                if (!_burnThisStep.TryGetValue(key, out Hazard burn))
                {
                    burn = NewHazard(ReactionKind.BurnContact, HazardShape.Cells, owner, source, DamageChannel.Thermal, dps, true, c.BurnEnd);
                    burn.GroundOnly = true;
                    _burnThisStep[key] = burn;
                }

                AttachCellHazard(ref c.BurnHazard, burn);
            }

            Emit(ReactionKind.Ignition, owner, source, CellPoint(index), index, c.Growth.Units, 0);
        }

        /// <summary>L154: equal units of water and sand (or dust, BIO-015) make damp sand for 3 s; no immobilization.</summary>
        private void Dampen(int index, ref Cell c)
        {
            int owner = c.Water.Owner;
            int source = c.Water.Source;
            int dampable = c.Sand.Units + c.Dust.Units - c.DampUnits;
            int units = Math.Min(c.Water.Units, dampable);
            Consume(index, Material.Water, units);
            c.DampUnits = (byte)(c.DampUnits + units);
            c.DampUntil = _tick + Ticks.FromSeconds(GlobalRules.World.DampSandSec);
            Emit(ReactionKind.DampFormed, owner, source, CellPoint(index), index, units, 0);
        }

        /// <summary>A soluble venom cloud over liquid water dilutes to 50 percent (L159).</summary>
        private void DiluteVenomClouds()
        {
            for (int i = 0; i < _volumes.Count; i++)
            {
                ObscurationVolume v = _volumes[i];
                if (v.Kind != VolumeKind.VenomCloud || !v.Soluble || v.Diluted)
                {
                    continue;
                }

                _columnXs.Clear();
                _columnZs.Clear();
                v.Columns.GetColumns(_columnXs, _columnZs);
                for (int k = 0; k < _columnXs.Count; k++)
                {
                    if (_grid.TryGetIndex(v.ColumnCenter(_columnXs[k], _columnZs[k]), out int index) && _grid[index].IsWet)
                    {
                        v.Diluted = true;
                        for (int h = 0; h < _hazards.Count; h++)
                        {
                            if (_hazards[h].Shape == HazardShape.Volume && _hazards[h].VolumeId == v.Id)
                            {
                                _hazards[h].Diluted = true;
                            }
                        }

                        Emit(ReactionKind.VenomDilution, v.Owner, v.SourceEventId, CellPoint(index), index, 1, v.Id);
                        break;
                    }
                }
            }
        }
    }
}
