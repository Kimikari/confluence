using System;
using System.Collections.Generic;
using Confluence.Core.Arena;
using Confluence.Core.Rules;
using UnityEngine;

namespace Confluence.Client.Arena
{
    /// <summary>
    /// Presentation class of an arena surface. Each kind has its own pattern so material identity never
    /// depends on color alone (spec L371 "color palettes that preserve material patterns", L401, L682) and a
    /// plain description for the preview legend (spec L329 "conductive rails, shallow water, combustible
    /// hedge, or prism surfaces").
    /// </summary>
    public enum SurfaceKind
    {
        Stone,
        Metal,
        Wood,
        Glass,
        Growth,
        Water,
        Sand,
        Ice,
        Porous,
        Heat,
        Frost,
        Dust,
    }

    /// <summary>How a surface kind looks: pattern, tint, tiling, and whether it is see-through.</summary>
    public readonly struct SurfaceStyle
    {
        public readonly SurfaceKind Kind;
        public readonly string Label;
        public readonly string Description;
        public readonly Color Tint;

        /// <summary>Meters covered by one texture repeat (world-scaled UVs keep patterns the same size everywhere).</summary>
        public readonly float TileMeters;

        public readonly float Metallic;
        public readonly float Smoothness;

        /// <summary>True for surfaces rendered translucent on solids (glass, ice).</summary>
        public readonly bool Translucent;

        public SurfaceStyle(SurfaceKind kind, string label, string description, Color tint, float tileMeters, float metallic, float smoothness, bool translucent)
        {
            Kind = kind;
            Label = label;
            Description = description;
            Tint = tint;
            TileMeters = tileMeters;
            Metallic = metallic;
            Smoothness = smoothness;
            Translucent = translucent;
        }
    }

    /// <summary>Classification of Core arena data into surface kinds, and the default style table.</summary>
    public static class Surfaces
    {
        private static readonly SurfaceStyle[] Styles =
        {
            new SurfaceStyle(SurfaceKind.Stone, "Stone", "Anchored stone: permanent terrain and cover.", new Color(0.66f, 0.65f, 0.62f), 2f, 0f, 0.15f, false),
            new SurfaceStyle(SurfaceKind.Metal, "Metal", "Conductive metal: rails and grilles carry electricity.", new Color(0.56f, 0.64f, 0.74f), 1f, 0.75f, 0.55f, false),
            new SurfaceStyle(SurfaceKind.Wood, "Wood", "Combustible wood: burns and breaks.", new Color(0.62f, 0.43f, 0.25f), 2f, 0f, 0.25f, false),
            new SurfaceStyle(SurfaceKind.Glass, "Prism glass", "Prism surface: reflects light rays from a fixed face.", new Color(0.62f, 0.9f, 1f, 0.5f), 1f, 0.1f, 0.9f, true),
            new SurfaceStyle(SurfaceKind.Growth, "Hedge", "Combustible hedge: growth that ignites with heat.", new Color(0.33f, 0.6f, 0.26f), 1f, 0f, 0.2f, false),
            new SurfaceStyle(SurfaceKind.Water, "Shallow water", "Shallow water: wet cells conduct electricity; grid shows cell connectivity.", new Color(0.2f, 0.5f, 0.9f, 0.7f), 1f, 0f, 0.92f, true),
            new SurfaceStyle(SurfaceKind.Sand, "Sand", "Loose sand: damp when water matches sand units.", new Color(0.88f, 0.77f, 0.52f), 1f, 0f, 0.1f, false),
            new SurfaceStyle(SurfaceKind.Ice, "Ice", "Slippery ice: brittle, shows fracture stages.", new Color(0.8f, 0.94f, 1f, 0.85f), 2f, 0f, 0.95f, true),
            new SurfaceStyle(SurfaceKind.Porous, "Porous stone", "Porous stone: absorbs water.", new Color(0.6f, 0.55f, 0.48f), 1f, 0f, 0.1f, false),
            new SurfaceStyle(SurfaceKind.Heat, "Heated ground", "Heat units on the ground: react with water and ice.", new Color(1f, 0.5f, 0.15f, 0.75f), 1f, 0f, 0.3f, true),
            new SurfaceStyle(SurfaceKind.Frost, "Frost", "Frost units on the ground: freeze water into ice.", new Color(0.75f, 0.9f, 1f, 0.75f), 1f, 0f, 0.7f, true),
            new SurfaceStyle(SurfaceKind.Dust, "Dust bed", "Dust units: can rise into a sight-blocking cloud.", new Color(0.62f, 0.55f, 0.45f, 0.75f), 1f, 0f, 0.05f, true),
        };

        public static int Count => Styles.Length;

        public static SurfaceStyle Style(SurfaceKind kind) => Styles[(int)kind];

        /// <summary>
        /// Every surface kind a layout contains (solids and ground patches), in enum order: the same list the renderer's
        /// legend shows, available before anything is rendered (round reveal, spec L44, L329).
        /// </summary>
        public static List<SurfaceKind> PresentIn(ArenaLayout layout)
        {
            var seen = new bool[Styles.Length];
            if (layout != null)
            {
                if (layout.Solids != null)
                {
                    foreach (Solid solid in layout.Solids)
                    {
                        if (solid != null)
                        {
                            seen[(int)ForSolid(solid)] = true;
                        }
                    }
                }

                if (layout.Materials != null)
                {
                    foreach (MaterialPatch patch in layout.Materials)
                    {
                        if (patch != null)
                        {
                            seen[(int)ForPatch(patch)] = true;
                        }
                    }
                }
            }

            var kinds = new List<SurfaceKind>();
            for (int i = 0; i < seen.Length; i++)
            {
                if (seen[i])
                {
                    kinds.Add((SurfaceKind)i);
                }
            }

            return kinds;
        }

        /// <summary>
        /// Surface kind of a collision solid: the module material name first (stone, slate, basalt, rock, metal,
        /// wood, glass, growth, porous, water, ice, sand appear in the catalogue), then the material flags.
        /// </summary>
        public static SurfaceKind ForSolid(Solid solid)
        {
            if (solid == null)
            {
                throw new ArgumentNullException(nameof(solid));
            }

            return TryParseMaterial(solid.Material, out SurfaceKind kind) ? kind : FromFlags(solid.Flags, SurfaceKind.Stone);
        }

        /// <summary>
        /// Surface kind of a ground material patch: the largest unit count wins (ties in the order water, heat,
        /// frost, sand, growth, dust); a patch without units falls back to its flags (conductor sections, etc.).
        /// </summary>
        public static SurfaceKind ForPatch(MaterialPatch patch)
        {
            if (patch == null)
            {
                throw new ArgumentNullException(nameof(patch));
            }

            SurfaceKind best = SurfaceKind.Stone;
            int bestUnits = 0;
            Consider(patch.Water, SurfaceKind.Water, ref best, ref bestUnits);
            Consider(patch.Heat, SurfaceKind.Heat, ref best, ref bestUnits);
            Consider(patch.Frost, SurfaceKind.Frost, ref best, ref bestUnits);
            Consider(patch.Sand, SurfaceKind.Sand, ref best, ref bestUnits);
            Consider(patch.Growth, SurfaceKind.Growth, ref best, ref bestUnits);
            Consider(patch.Dust, SurfaceKind.Dust, ref best, ref bestUnits);
            return bestUnits > 0 ? best : FromFlags(patch.Flags, SurfaceKind.Stone);
        }

        /// <summary>Largest unit count of a patch (0-4), used to scale its decal opacity.</summary>
        public static int MaxUnits(MaterialPatch patch)
        {
            int max = Math.Max(patch.Water, patch.Heat);
            max = Math.Max(max, patch.Frost);
            max = Math.Max(max, patch.Dust);
            max = Math.Max(max, patch.Sand);
            return Math.Max(max, patch.Growth);
        }

        public static bool TryParseMaterial(string material, out SurfaceKind kind)
        {
            switch ((material ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "stone":
                case "slate":
                case "basalt":
                case "rock":
                case "earth":
                    kind = SurfaceKind.Stone;
                    return true;
                case "metal":
                case "copper":
                case "iron":
                    kind = SurfaceKind.Metal;
                    return true;
                case "wood":
                case "timber":
                    kind = SurfaceKind.Wood;
                    return true;
                case "glass":
                case "prism":
                case "crystal":
                    kind = SurfaceKind.Glass;
                    return true;
                case "growth":
                case "hedge":
                    kind = SurfaceKind.Growth;
                    return true;
                case "water":
                    kind = SurfaceKind.Water;
                    return true;
                case "sand":
                    kind = SurfaceKind.Sand;
                    return true;
                case "ice":
                case "frost":
                    kind = SurfaceKind.Ice;
                    return true;
                case "porous":
                    kind = SurfaceKind.Porous;
                    return true;
                default:
                    kind = SurfaceKind.Stone;
                    return false;
            }
        }

        /// <summary>Surface kind from material flags alone (temporary solids carry flags but no material name).</summary>
        public static SurfaceKind FromFlags(MaterialFlags flags, SurfaceKind fallback)
        {
            if ((flags & MaterialFlags.Water) != 0) return SurfaceKind.Water;
            if ((flags & MaterialFlags.Ice) != 0) return SurfaceKind.Ice;
            if ((flags & (MaterialFlags.Glass | MaterialFlags.Reflective)) != 0) return SurfaceKind.Glass;
            if ((flags & (MaterialFlags.Metal | MaterialFlags.Conductive)) != 0) return SurfaceKind.Metal;
            if ((flags & MaterialFlags.Growth) != 0) return SurfaceKind.Growth;
            if ((flags & MaterialFlags.Sand) != 0) return SurfaceKind.Sand;
            if ((flags & MaterialFlags.Porous) != 0) return SurfaceKind.Porous;
            if ((flags & MaterialFlags.Combustible) != 0) return SurfaceKind.Wood;
            return fallback;
        }

        private static void Consider(int units, SurfaceKind kind, ref SurfaceKind best, ref int bestUnits)
        {
            if (units > bestUnits)
            {
                bestUnits = units;
                best = kind;
            }
        }
    }
}
