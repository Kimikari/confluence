using System;
using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Core.Arena
{
    /// <summary>
    /// Assembled round arena: the contract between the Arena generator and its consumers (Sim, server,
    /// clients, replays). Records everything spec L664 requires: modules and transforms, props, initial material
    /// cells, light shafts, gust schedule, and the contraction contour. Every fighting pair in a round receives a
    /// deep copy (spec L34), so destruction in one copy never leaks into another.
    /// </summary>
    public sealed class ArenaLayout
    {
        /// <summary>Arena-stream seed this layout was generated from (spec L316).</summary>
        public ulong Seed { get; set; }

        public int Round { get; set; }

        /// <summary>Target diameter tier in meters: 24, 32, 40, or 48 (spec L306).</summary>
        public int Diameter { get; set; }

        public string BiomeId { get; set; }

        /// <summary>The biome's announced environment rule (spec L335), shown in preview.</summary>
        public string EnvironmentRule { get; set; }

        /// <summary>Prevalidated fallback layout id, or null when the layout was generated (spec L323).</summary>
        public string FallbackId { get; set; }

        public List<ModuleInstance> Modules { get; set; } = new List<ModuleInstance>();
        public List<Solid> Solids { get; set; } = new List<Solid>();
        public List<MaterialPatch> Materials { get; set; } = new List<MaterialPatch>();
        public List<PropPlacement> Props { get; set; } = new List<PropPlacement>();

        /// <summary>Spawn alcoves indexed by side (length 2).</summary>
        public SpawnPoint[] Spawns { get; set; } = { new SpawnPoint { Side = 0 }, new SpawnPoint { Side = 1 } };

        /// <summary>Center of the central final duel floor and of the boundary contraction.</summary>
        public Vec3 Center { get; set; }

        public float FinalFloorRadius { get; set; } = GlobalRules.Boundary.FinalFloorRadius; // spec L48, L678

        public BoundaryContour Boundary { get; set; } = new BoundaryContour();

        public List<LightShaft> LightShafts { get; set; } = new List<LightShaft>();
        public List<GustEvent> Gusts { get; set; } = new List<GustEvent>();

        public ArenaValidationReport Report { get; set; } = new ArenaValidationReport();

        public bool IsFallback => FallbackId != null;

        /// <summary>Solid by id, or null.</summary>
        public Solid FindSolid(int id)
        {
            for (int i = 0; i < Solids.Count; i++)
            {
                if (Solids[i].Id == id)
                {
                    return Solids[i];
                }
            }

            return null;
        }

        /// <summary>Prop placement by id, or null.</summary>
        public PropPlacement FindProp(int id)
        {
            for (int i = 0; i < Props.Count; i++)
            {
                if (Props[i].Id == id)
                {
                    return Props[i];
                }
            }

            return null;
        }

        /// <summary>Count of natural source patches (at most 8 at combat start, spec L668).</summary>
        public int NaturalSourceCount()
        {
            int count = 0;
            for (int i = 0; i < Materials.Count; i++)
            {
                if (Materials[i].NaturalSource)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Deep copy: no list, element, contour, or report is shared with the original.</summary>
        public ArenaLayout Clone()
        {
            var copy = new ArenaLayout
            {
                Seed = Seed,
                Round = Round,
                Diameter = Diameter,
                BiomeId = BiomeId,
                EnvironmentRule = EnvironmentRule,
                FallbackId = FallbackId,
                Modules = CloneList(Modules, m => m.Clone()),
                Solids = CloneList(Solids, s => s.Clone()),
                Materials = CloneList(Materials, m => m.Clone()),
                Props = CloneList(Props, p => p.Clone()),
                Center = Center,
                FinalFloorRadius = FinalFloorRadius,
                Boundary = Boundary?.Clone(),
                LightShafts = CloneList(LightShafts, l => l.Clone()),
                Gusts = CloneList(Gusts, g => g.Clone()),
                Report = Report?.Clone(),
            };

            if (Spawns == null)
            {
                copy.Spawns = null;
            }
            else
            {
                copy.Spawns = new SpawnPoint[Spawns.Length];
                for (int i = 0; i < Spawns.Length; i++)
                {
                    copy.Spawns[i] = Spawns[i]?.Clone();
                }
            }

            return copy;
        }

        private static List<T> CloneList<T>(List<T> source, Func<T, T> cloneItem) where T : class
        {
            if (source == null)
            {
                return null;
            }

            var result = new List<T>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                T item = source[i];
                result.Add(item == null ? null : cloneItem(item));
            }

            return result;
        }
    }

    /// <summary>Outcome of arena validation (spec L321-L323, L674-L679).</summary>
    public sealed class ArenaValidationReport
    {
        public bool Valid { get; set; }
        public List<string> Failures { get; set; } = new List<string>();

        /// <summary>Measured values such as shortest spawn path, longest firing lane, and cover fraction.</summary>
        public Dictionary<string, float> Metrics { get; set; } = new Dictionary<string, float>(StringComparer.Ordinal);

        /// <summary>Generation attempts consumed (1 + retries, at most 8 seeds before a fallback, spec L323).</summary>
        public int AttemptsUsed { get; set; }

        public void AddFailure(string failure)
        {
            Failures.Add(failure);
            Valid = false;
        }

        public void SetMetric(string name, float value) => Metrics[name] = value;

        public bool TryGetMetric(string name, out float value) => Metrics.TryGetValue(name, out value);

        public ArenaValidationReport Clone()
        {
            return new ArenaValidationReport
            {
                Valid = Valid,
                Failures = Failures == null ? null : new List<string>(Failures),
                Metrics = Metrics == null ? null : new Dictionary<string, float>(Metrics, StringComparer.Ordinal),
                AttemptsUsed = AttemptsUsed,
            };
        }
    }
}
