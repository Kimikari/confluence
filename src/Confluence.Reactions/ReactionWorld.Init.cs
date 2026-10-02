using System;
using System.Collections.Generic;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Reactions.Electricity;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Queries;

namespace Confluence.Reactions
{
    public sealed partial class ReactionWorld
    {
        /// <summary>
        /// Loads the assembled arena's initial material cells, conductive solids and patches, reflective solids, and
        /// light shafts (spec L664). Initial units are map-owned and permanent; natural sources are flagged (L668).
        /// </summary>
        private void InitializeFromLayout(ArenaLayout layout)
        {
            if (layout.Materials != null)
            {
                for (int p = 0; p < layout.Materials.Count; p++)
                {
                    MaterialPatch patch = layout.Materials[p];
                    if (patch != null)
                    {
                        LoadPatch(patch, p);
                    }
                }
            }

            if (layout.Solids != null)
            {
                for (int i = 0; i < layout.Solids.Count; i++)
                {
                    Solid solid = layout.Solids[i];
                    if (solid != null)
                    {
                        LoadSolid(solid);
                    }
                }
            }

            if (layout.LightShafts != null)
            {
                for (int i = 0; i < layout.LightShafts.Count; i++)
                {
                    if (layout.LightShafts[i] != null)
                    {
                        _lightShafts.Add(layout.LightShafts[i].Clone());
                    }
                }
            }
        }

        /// <summary>Cells whose centers lie inside the patch (or the cell under its center for a tiny patch), ascending.</summary>
        internal void RasterizePatch(MaterialPatch patch, List<int> cells)
        {
            float extent = patch.IsCircle ? patch.Radius : MathF.Sqrt(patch.HalfSize.X * patch.HalfSize.X + patch.HalfSize.Y * patch.HalfSize.Y);
            Footprint bounds = Footprint.Circle(patch.Center, extent);
            _scratchCells2.Clear();
            _grid.Rasterize(bounds, _scratchCells2);
            int before = cells.Count;
            for (int i = 0; i < _scratchCells2.Count; i++)
            {
                if (patch.ContainsXZ(_grid.CellCenter(_scratchCells2[i])))
                {
                    cells.Add(_scratchCells2[i]);
                }
            }

            if (cells.Count == before && _grid.TryGetIndex(patch.Center, out int center))
            {
                cells.Add(center);
            }
        }

        private void LoadPatch(MaterialPatch patch, int patchIndex)
        {
            _scratchCells.Clear();
            RasterizePatch(patch, _scratchCells);
            bool ice = (patch.Flags & MaterialFlags.Ice) != 0;
            for (int i = 0; i < _scratchCells.Count; i++)
            {
                int index = _scratchCells[i];
                ref Cell c = ref _grid[index];
                c.Flags |= patch.Flags & ~(MaterialFlags.Ice | MaterialFlags.Water | MaterialFlags.Growth | MaterialFlags.Sand);
                c.SurfaceY = patch.SurfaceY;
                c.State |= CellState.HasSurface;
                if (patch.NaturalSource)
                {
                    c.State |= CellState.NaturalSource;
                }

                int water = patch.Water;
                if (ice)
                {
                    // BIO-004: shallow water tiles begin frozen with 45 durability; one water unit is the tile.
                    c.IceDurability = GlobalRules.World.IceTileDurability;
                    c.IceExpiry = int.MaxValue;
                    c.IceOwner = ReactionRules.MapOwner;
                    c.IceSource = ReactionRules.NoSource;
                    c.State |= CellState.IceNatural;
                    Ledger.IceTilesInitial++;
                    if (water > 0)
                    {
                        water--;
                    }

                    Touch(index);
                }

                LoadNatural(index, Material.Water, water);
                LoadNatural(index, Material.Heat, patch.Heat);
                LoadNatural(index, Material.Frost, patch.Frost);
                LoadNatural(index, Material.Dust, patch.Dust);
                LoadNatural(index, Material.Sand, patch.Sand);
                if (LoadNatural(index, Material.Growth, patch.Growth) > 0)
                {
                    c.State |= CellState.GrowthCombustible; // map growth beds are finite fuel (BIO-002, MOD-040)
                }
            }

            if ((patch.Flags & (MaterialFlags.Metal | MaterialFlags.Conductive)) != 0 && (patch.Flags & MaterialFlags.Insulating) == 0)
            {
                Vec2 half = patch.IsCircle ? new Vec2(patch.Radius, patch.Radius) : patch.HalfSize;
                var box = new YawBox(new Vec3(patch.Center.X, patch.SurfaceY, patch.Center.Y), new Vec3(half.X, 0.05f, half.Y), patch.YawDeg);
                RegisterConductor(ConductorDef.ForBox(PatchConductorIdBase + patchIndex, box, (patch.Flags & MaterialFlags.Grounded) != 0));
            }
        }

        private int LoadNatural(int index, Material material, int units)
        {
            int added = AddNatural(index, material, units, Materials.MaxUnits);
            Ledger.AddInitial(material, added);
            return added;
        }

        private void LoadSolid(Solid solid)
        {
            _solids[solid.Id] = solid;
            MaterialFlags flags = solid.Flags;
            if ((flags & (MaterialFlags.Metal | MaterialFlags.Conductive)) != 0 && (flags & MaterialFlags.Insulating) == 0)
            {
                RegisterConductor(ConductorDef.ForBox(SolidConductorIdBase + solid.Id, solid.Box, (flags & MaterialFlags.Grounded) != 0));
            }

            if ((flags & MaterialFlags.Reflective) != 0)
            {
                // A reflective arena solid reflects along its fixed forward face (spec L157, L682 "Prisms show a fixed face").
                Prisms.Register(new PrismDef { Id = SolidPrismIdBase + solid.Id, Box = solid.Box, FaceNormal = solid.Box.Forward });
            }
        }
    }
}
