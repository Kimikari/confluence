using System;
using System.Collections.Generic;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Hosting.Arenas
{
    /// <summary>A spawn alcove marker in the preview (L329 "spawn locations").</summary>
    public struct PreviewSpawn
    {
        public int Side;
        public Vec3 Position;
        public float YawDeg;
    }

    /// <summary>A placed catalogue module ("major terrain", L44).</summary>
    public sealed class PreviewModule
    {
        internal PreviewModule(string moduleId, string name, string category, Vec3 position, float yawDeg)
        {
            ModuleId = moduleId;
            Name = name;
            Category = category;
            Position = position;
            YawDeg = yawDeg;
        }

        public string ModuleId { get; }
        public string Name { get; }
        public string Category { get; }
        public Vec3 Position { get; }
        public float YawDeg { get; }
    }

    /// <summary>A placed prop with its interaction icon category (L329 "prop interaction icons").</summary>
    public sealed class PreviewProp
    {
        internal PreviewProp(string propId, string name, string category, string effect, Vec3 position, float yawDeg)
        {
            PropId = propId;
            Name = name;
            Category = category;
            Effect = effect;
            Position = position;
            YawDeg = yawDeg;
        }

        public string PropId { get; }
        public string Name { get; }

        /// <summary>Conductive, Reservoir, Fuel, Optical, Kinetic, Acoustic, Reactive, or Utility: the icon to show.</summary>
        public string Category { get; }

        /// <summary>The catalogue's public effect text.</summary>
        public string Effect { get; }

        public Vec3 Position { get; }
        public float YawDeg { get; }
    }

    /// <summary>A top-down cover or elevation outline (L329 "cover and elevation shapes").</summary>
    public struct PreviewShape
    {
        public SolidKind Kind;
        public Vec2 Center;
        public Vec2 HalfExtents;
        public float YawDeg;

        /// <summary>Height of the top above the arena floor's center.</summary>
        public float TopY;

        public string Material;
        public bool Walkable;
    }

    /// <summary>
    /// Public arena preview shown at reveal and through the shop (spec L44, L329): diameter, spawn locations, biome and
    /// its announced environment rule, major terrain, cover and elevation shapes, prop interaction icons, and plain
    /// material descriptions. Built from the layout's public geometry and the catalogue text only: it carries no
    /// player, build, standing, seed, or validation data, and nothing that differs between the copies of the round.
    /// </summary>
    public sealed class ArenaPreviewInfo
    {
        private ArenaPreviewInfo()
        {
        }

        public string ArenaId { get; private set; }
        public int Round { get; private set; }
        public int Diameter { get; private set; }
        public string BiomeId { get; private set; }
        public string BiomeName { get; private set; }

        /// <summary>The biome's one announced bounded environment rule (L335).</summary>
        public string EnvironmentRule { get; private set; }

        public IReadOnlyList<PreviewSpawn> Spawns { get; private set; }

        /// <summary>Distinct MOD ids in placement order.</summary>
        public IReadOnlyList<string> MajorTerrainIds { get; private set; }

        public IReadOnlyList<PreviewModule> Modules { get; private set; }

        /// <summary>Distinct PROP ids in placement order.</summary>
        public IReadOnlyList<string> PropIds { get; private set; }

        public IReadOnlyList<PreviewProp> Props { get; private set; }
        public IReadOnlyList<PreviewShape> Shapes { get; private set; }
        public IReadOnlyList<MaterialNote> Materials { get; private set; }

        /// <summary>Final central floor radius the boundary contracts to by 85 s (L48).</summary>
        public float FinalFloorRadius { get; private set; }

        public static ArenaPreviewInfo Build(string arenaId, ArenaLayout layout, Catalogue catalogue)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (catalogue == null) throw new ArgumentNullException(nameof(catalogue));
            var info = new ArenaPreviewInfo
            {
                ArenaId = arenaId,
                Round = layout.Round,
                Diameter = layout.Diameter,
                BiomeId = layout.BiomeId,
                BiomeName = layout.BiomeId != null && catalogue.TryGetBiome(layout.BiomeId, out BiomeDef biome) ? biome.Name : null,
                EnvironmentRule = layout.EnvironmentRule,
                FinalFloorRadius = layout.FinalFloorRadius,
            };

            var spawns = new List<PreviewSpawn>();
            if (layout.Spawns != null)
                foreach (SpawnPoint s in layout.Spawns)
                    if (s != null) spawns.Add(new PreviewSpawn { Side = s.Side, Position = s.Position, YawDeg = s.YawDeg });
            info.Spawns = spawns.AsReadOnly();

            var modules = new List<PreviewModule>();
            var moduleIds = new List<string>();
            foreach (ModuleInstance m in layout.Modules)
            {
                ModuleDef def = m.ModuleId != null && catalogue.TryGetModule(m.ModuleId, out ModuleDef d) ? d : null;
                modules.Add(new PreviewModule(m.ModuleId, def?.Name, def?.CategoryText, m.Position, m.YawDeg));
                if (m.ModuleId != null && !moduleIds.Contains(m.ModuleId)) moduleIds.Add(m.ModuleId);
            }

            info.Modules = modules.AsReadOnly();
            info.MajorTerrainIds = moduleIds.AsReadOnly();

            var props = new List<PreviewProp>();
            var propIds = new List<string>();
            foreach (PropPlacement p in layout.Props)
            {
                PropDef def = p.PropId != null && catalogue.TryGetProp(p.PropId, out PropDef d) ? d : null;
                props.Add(new PreviewProp(p.PropId, def?.Name, def?.CategoryText, def?.Effect, p.Position, p.YawDeg));
                if (p.PropId != null && !propIds.Contains(p.PropId)) propIds.Add(p.PropId);
            }

            info.Props = props.AsReadOnly();
            info.PropIds = propIds.AsReadOnly();

            var shapes = new List<PreviewShape>();
            foreach (Solid s in layout.Solids)
            {
                if (s.Kind == SolidKind.Floor) continue;
                Vec3 c = s.Box.Center;
                shapes.Add(new PreviewShape
                {
                    Kind = s.Kind,
                    Center = new Vec2(c.X, c.Z),
                    HalfExtents = new Vec2(s.Box.HalfExtents.X, s.Box.HalfExtents.Z),
                    YawDeg = s.Box.YawDeg,
                    TopY = s.Box.Top - layout.Center.Y, // a ramp's box top is its high end

                    Material = s.Material,
                    Walkable = s.Walkable,
                });
            }

            info.Shapes = shapes.AsReadOnly();
            info.Materials = MaterialDescriptions.Describe(layout).AsReadOnly();
            return info;
        }
    }
}
