using System;
using System.Collections.Generic;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Reactions.Events;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Hazards;
using Confluence.Reactions.Operations;

namespace Confluence.Reactions
{
    /// <summary>
    /// Interactive objects that live in the Sim (props, biome objects) but use the reaction world's standard outcomes
    /// (spec L147 shard cone, L153 burning contact) and static cell flags (L136 "material flags such as conductive,
    /// combustible, porous, or anchored"; PROP-047 insulation). Added for the props-biomes stage; additive to the
    /// queues of <see cref="ReactionWorld"/>.
    /// </summary>
    public sealed partial class ReactionWorld
    {
        private readonly List<ShardBurst> _shardBursts = new List<ShardBurst>();
        private readonly List<BurnZone> _burnZones = new List<BurnZone>();

        /// <summary>Queues the standard shard cone of a breaking brittle object (applied in the direct-impacts phase).</summary>
        public void EmitShards(ShardBurst burst) => _shardBursts.Add(Require(burst));

        /// <summary>Queues a burning object's contact zone (applied in the direct-impacts phase).</summary>
        public void AddBurnZone(BurnZone zone) => _burnZones.Add(Require(zone));

        /// <summary>
        /// Sets or clears static material flags on every cell whose center lies in the area (immediate; for example an
        /// intact insulation tile, PROP-047, or its removal when destroyed). Returns the number of cells changed. Static
        /// flags are arena data: a snapshot restore rebuilds them from the layout, so the caller re-applies its flags.
        /// </summary>
        public int SetCellFlags(Footprint area, MaterialFlags flags, bool enabled)
        {
            _scratchCells2.Clear();
            _grid.Rasterize(area, _scratchCells2);
            int changed = 0;
            for (int i = 0; i < _scratchCells2.Count; i++)
            {
                ref Cell c = ref _grid[_scratchCells2[i]];
                MaterialFlags next = enabled ? c.Flags | flags : c.Flags & ~flags;
                if (next != c.Flags)
                {
                    c.Flags = next;
                    changed++;
                }
            }

            return changed;
        }

        /// <summary>Static material flags of the cell under a point (None outside the grid).</summary>
        public MaterialFlags CellFlagsAt(Vec3 point) => _grid.TryGetIndex(point, out int i) ? _grid[i].Flags : MaterialFlags.None;

        // Called from the direct-impacts phase after the impacts themselves (spec L165 "incoming direct impacts" first).
        private void ApplyObjectRequests()
        {
            _shardBursts.Sort((a, b) => CompareSourceOwner(a.SourceEventId, a.Owner, b.SourceEventId, b.Owner, a.ObjectId.CompareTo(b.ObjectId)));
            for (int i = 0; i < _shardBursts.Count; i++)
            {
                ShardBurst s = _shardBursts[i];
                ShardCone(s.Apex, s.Direction, s.Owner, s.SourceEventId, -1);
            }

            _shardBursts.Clear();

            _burnZones.Sort((a, b) => CompareSourceOwner(a.SourceEventId, a.Owner, b.SourceEventId, b.Owner, a.ObjectId.CompareTo(b.ObjectId)));
            for (int i = 0; i < _burnZones.Count; i++)
            {
                BurnZone z = _burnZones[i];
                int lifetime = PlayerLifetimeCap(z.Owner, z.LifetimeTicks);
                if (lifetime <= 0 || !(z.DamagePerSecond > 0f))
                {
                    continue;
                }

                Hazard burn = NewHazard(ReactionKind.BurnContact, HazardShape.Box, z.Owner, z.SourceEventId,
                    DamageChannel.Thermal, z.DamagePerSecond, true, _tick + lifetime);
                burn.Box = z.Box;
                burn.ObjectId = z.ObjectId;
            }

            _burnZones.Clear();
        }
    }
}
