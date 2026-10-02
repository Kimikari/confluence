using System;
using Confluence.Core.Random;
using Confluence.Core.Rules;
using UnityEngine;

namespace Confluence.Client.Arena
{
    /// <summary>
    /// TEMPORARY ASSET SOURCE (docs/notes/client.md): procedural, seamlessly tiling pattern textures standing in
    /// for final material art. Patterns are luminance-only (tinted by the material color), so swapping a color
    /// palette keeps every material's pattern (spec L371). Alpha carries translucency for glass, water, and
    /// ground decals. Noise comes from Confluence.Core.Random.Rng with fixed seeds, so textures are identical
    /// on every machine.
    /// </summary>
    public static class PatternTextures
    {
        public const int DefaultSize = 256;
        public const int GlyphSize = 64;

        private delegate void PixelFunction(float u, float v, out float luminance, out float alpha);

        public static Texture2D Surface(SurfaceKind kind, int size = DefaultSize)
        {
            var noise = new PatternNoise(0xC0F1u + (ulong)kind * 7919u, size);
            PixelFunction f;
            switch (kind)
            {
                case SurfaceKind.Stone: f = (float u, float v, out float l, out float a) => StoneBlocks(noise, u, v, out l, out a); break;
                case SurfaceKind.Metal: f = (float u, float v, out float l, out float a) => MetalRails(noise, u, v, out l, out a); break;
                case SurfaceKind.Wood: f = (float u, float v, out float l, out float a) => WoodPlanks(noise, u, v, out l, out a); break;
                case SurfaceKind.Glass: f = GlassFacets; break;
                case SurfaceKind.Growth: f = (float u, float v, out float l, out float a) => GrowthLeaves(noise, u, v, out l, out a); break;
                case SurfaceKind.Water: f = WaterCells; break;
                case SurfaceKind.Sand: f = (float u, float v, out float l, out float a) => SandLayers(noise, u, v, out l, out a); break;
                case SurfaceKind.Ice: f = (float u, float v, out float l, out float a) => IceFractures(noise, u, v, out l, out a); break;
                case SurfaceKind.Porous: f = (float u, float v, out float l, out float a) => PorousHoles(noise, u, v, out l, out a); break;
                case SurfaceKind.Heat: f = HeatChevrons; break;
                case SurfaceKind.Frost: f = (float u, float v, out float l, out float a) => FrostStars(noise, u, v, out l, out a); break;
                case SurfaceKind.Dust: f = (float u, float v, out float l, out float a) => DustSpeckle(noise, u, v, out l, out a); break;
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }

            return Render("Pattern " + kind, size, f, TextureWrapMode.Repeat);
        }

        /// <summary>Spawn ward volume: side 0 vertical bars, side 1 horizontal bars (sides differ by pattern, not only tint).</summary>
        public static Texture2D Ward(int side, int size = 128)
        {
            bool vertical = side == 0;
            return Render("Ward " + side, size, (float u, float v, out float l, out float a) =>
            {
                float t = Frac((vertical ? u : v) * 4f);
                bool bar = t < 0.22f;
                l = bar ? 1f : 0.8f;
                a = bar ? 0.38f : 0.07f; // light enough to see through from inside the alcove
            }, TextureWrapMode.Repeat);
        }

        /// <summary>Boundary wall: diagonal hazard stripes fading toward the top (v = 1).</summary>
        public static Texture2D Boundary(int size = 128)
        {
            return Render("Boundary", size, (float u, float v, out float l, out float a) =>
            {
                bool stripe = Frac(u * 2f + v * 2f) < 0.5f;
                float fade = 1f - 0.75f * v;
                l = stripe ? 1f : 0.55f;
                a = (stripe ? 0.62f : 0.22f) * fade;
            }, TextureWrapMode.Repeat);
        }

        /// <summary>Final floor ring: dashes along u.</summary>
        public static Texture2D FinalRing(int size = 64)
        {
            return Render("Final ring", size, (float u, float v, out float l, out float a) =>
            {
                bool dash = Frac(u * 2f) < 0.6f;
                l = 1f;
                a = dash ? 0.95f : 0.3f;
            }, TextureWrapMode.Repeat);
        }

        /// <summary>Prop interaction icon for the arena preview (spec L329 "prop interaction icons").</summary>
        public static Texture2D Glyph(PropCategory category, int size = GlyphSize)
        {
            return Render("Glyph " + category, size, (float u, float v, out float l, out float a) =>
            {
                float coverage = GlyphCoverage(category, u, v);
                float disc = Mathf.Clamp01((0.49f - Mathf.Sqrt(Sq(u - 0.5f) + Sq(v - 0.5f))) * size);
                l = coverage > 0.5f ? 1f : 0.12f;
                a = Mathf.Max(coverage, disc * 0.65f);
            }, TextureWrapMode.Clamp);
        }

        // ---- Patterns (u, v in [0, 1) over one tile; all functions tile seamlessly) -------------------------------

        private static void StoneBlocks(PatternNoise n, float u, float v, out float l, out float a)
        {
            const int rows = 4;
            const int cols = 2;
            float y = v * rows;
            int row = (int)y;
            float x = u * cols + ((row & 1) != 0 ? 0.5f : 0f);
            int col = ((int)Mathf.Floor(x) % cols + cols) % cols;
            float fx = Frac(x);
            float fy = Frac(y);
            float edge = Mathf.Min(Mathf.Min(fx, 1f - fx) / cols, Mathf.Min(fy, 1f - fy) / rows);
            float shade = n.Cell(row * 31 + col) - 0.5f;
            if (edge < 0.012f)
            {
                l = 0.3f;
            }
            else
            {
                float bevel = Mathf.Clamp01((edge - 0.012f) / 0.02f);
                l = (0.5f + 0.16f * bevel) + 0.16f * shade + 0.07f * (n.Grain(u, v) - 0.5f);
            }

            a = 1f;
        }

        private static void MetalRails(PatternNoise n, float u, float v, out float l, out float a)
        {
            const int plates = 4;
            float y = v * plates;
            int plate = (int)y;
            float fy = Frac(y);
            float groove = Mathf.Min(fy, 1f - fy) / plates;
            if (groove < 0.012f)
            {
                l = 0.22f;
            }
            else
            {
                // Brushed along the rail direction: grain varies across rows only.
                l = 0.62f + 0.12f * (n.Grain(0f, v) - 0.5f) + 0.05f * (n.Cell(plate) - 0.5f);
                for (int i = 0; i < 2; i++)
                {
                    float d = Mathf.Sqrt(Sq(WrapDelta(u - (0.15f + 0.5f * i))) + Sq((fy - 0.5f) / plates));
                    if (d < 0.022f)
                    {
                        l = d < 0.015f ? 0.92f : 0.3f;
                    }
                }
            }

            a = 1f;
        }

        private static void WoodPlanks(PatternNoise n, float u, float v, out float l, out float a)
        {
            const int rows = 8;
            float y = v * rows;
            int row = (int)y;
            float fy = Frac(y);
            float seam = Mathf.Min(fy, 1f - fy) / rows;
            float joint = Mathf.Abs(WrapDelta(u - n.Cell(row + 101)));
            if (seam < 0.006f || joint < 0.004f)
            {
                l = 0.22f;
            }
            else
            {
                float grain = Mathf.Sin((fy * 3f + 0.35f * Mathf.Sin(u * Mathf.PI * 2f * 3f + row)) * Mathf.PI * 2f);
                l = 0.56f + 0.09f * grain + 0.12f * (n.Cell(row + 7) - 0.5f) + 0.04f * (n.Grain(u, v) - 0.5f);
            }

            a = 1f;
        }

        private static void GlassFacets(float u, float v, out float l, out float a)
        {
            float frame = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
            float diag1 = Mathf.Abs(WrapDelta(u - v)) * 0.7071f;
            float diag2 = Mathf.Abs(WrapDelta(u + v)) * 0.7071f;
            if (frame < 0.03f)
            {
                l = 0.95f;
                a = 0.95f;
            }
            else if (diag1 < 0.01f || diag2 < 0.01f)
            {
                l = 1f;
                a = 0.8f;
            }
            else
            {
                l = 0.78f;
                a = 0.32f;
            }
        }

        private static void GrowthLeaves(PatternNoise n, float u, float v, out float l, out float a)
        {
            n.Voronoi(6, u, v, out float f1, out float f2, out int cell);
            const float leafRadius = 0.11f;
            if (f2 - f1 < 0.008f)
            {
                l = 0.16f;
            }
            else if (f1 < leafRadius)
            {
                l = 0.5f + 0.38f * (1f - f1 / leafRadius) * (0.6f + 0.4f * n.Cell(cell));
            }
            else
            {
                l = 0.3f + 0.08f * n.Grain(u, v);
            }

            a = 1f;
        }

        private static void WaterCells(float u, float v, out float l, out float a)
        {
            // One tile is 1 m: grid lines every 0.5 m mark the interaction cells (spec L136, L682).
            float cu = Frac(u * 2f);
            float cv = Frac(v * 2f);
            float grid = Mathf.Min(Mathf.Min(cu, 1f - cu), Mathf.Min(cv, 1f - cv)) * 0.5f;
            if (grid < 0.008f)
            {
                l = 0.95f;
                a = 0.9f;
                return;
            }

            float ripple = Mathf.Sin((v * 3f + 0.25f * Mathf.Sin(u * Mathf.PI * 4f)) * Mathf.PI * 2f);
            l = 0.62f + 0.2f * ripple;
            a = 0.55f + 0.12f * ripple;
        }

        private static void SandLayers(PatternNoise n, float u, float v, out float l, out float a)
        {
            const int bands = 5;
            float wave = 0.04f * Mathf.Sin(u * Mathf.PI * 2f * 2f);
            int band = ((int)Mathf.Floor((v + wave) * bands) % bands + bands) % bands;
            float g = n.Grain(u, v);
            l = 0.62f + 0.14f * (n.Cell(band + 13) - 0.5f) + 0.16f * (g - 0.5f);
            if (g > 0.94f)
            {
                l = 0.32f;
            }

            a = 1f;
        }

        private static void IceFractures(PatternNoise n, float u, float v, out float l, out float a)
        {
            n.Voronoi(5, u, v, out float f1, out float f2, out int cell);
            float crack = f2 - f1;
            if (crack < 0.012f)
            {
                l = 1f;
                a = 1f;
            }
            else
            {
                l = 0.7f + 0.12f * n.Cell(cell) + 0.1f * Mathf.Clamp01(1f - crack / 0.08f);
                a = 0.78f;
            }
        }

        private static void PorousHoles(PatternNoise n, float u, float v, out float l, out float a)
        {
            n.Voronoi(7, u, v, out float f1, out float f2, out int cell);
            float r = 0.03f + 0.02f * n.Cell(cell);
            if (f1 < r)
            {
                l = 0.18f;
            }
            else if (f1 < r + 0.01f)
            {
                l = 0.38f;
            }
            else
            {
                l = 0.58f + 0.12f * (n.Grain(u, v) - 0.5f);
            }

            a = 1f;
        }

        private static void HeatChevrons(float u, float v, out float l, out float a)
        {
            float t = Frac(v * 4f + Mathf.Abs(Frac(u * 2f) - 0.5f));
            bool band = t < 0.3f;
            l = band ? 1f : 0.55f;
            a = band ? 0.85f : 0.3f;
        }

        private static void FrostStars(PatternNoise n, float u, float v, out float l, out float a)
        {
            const int grid = 4;
            const float arm = 0.09f;
            float best = float.MaxValue;
            n.ForEachFeature(grid, u, v, (float px, float py) =>
            {
                float dx = u - px;
                float dy = v - py;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                if (r > arm)
                {
                    return;
                }

                for (int k = 0; k < 3; k++)
                {
                    float angle = k * Mathf.PI / 3f;
                    float d = Mathf.Abs(-Mathf.Sin(angle) * dx + Mathf.Cos(angle) * dy);
                    best = Mathf.Min(best, d);
                }
            });

            bool line = best < 0.007f;
            l = line ? 1f : 0.72f;
            a = line ? 0.95f : 0.28f;
        }

        private static void DustSpeckle(PatternNoise n, float u, float v, out float l, out float a)
        {
            float g = n.Grain(u, v);
            if (g > 0.86f)
            {
                l = 0.35f;
                a = 0.85f;
            }
            else
            {
                l = 0.62f + 0.2f * (g - 0.5f);
                a = 0.4f;
            }
        }

        // ---- Glyphs ------------------------------------------------------------------------------------------------

        private static float GlyphCoverage(PropCategory category, float u, float v)
        {
            float d;
            switch (category)
            {
                case PropCategory.Conductive: // lightning bolt
                    d = Mathf.Min(Mathf.Min(Seg(u, v, 0.62f, 0.86f, 0.38f, 0.52f), Seg(u, v, 0.38f, 0.52f, 0.62f, 0.48f)), Seg(u, v, 0.62f, 0.48f, 0.38f, 0.14f));
                    return d < 0.05f ? 1f : 0f;
                case PropCategory.Reservoir: // three water waves
                    float best = float.MaxValue;
                    for (int i = 0; i < 3; i++)
                    {
                        float y = 0.32f + 0.18f * i + 0.05f * Mathf.Sin((u - 0.2f) * Mathf.PI * 4f);
                        if (u > 0.2f && u < 0.8f)
                        {
                            best = Mathf.Min(best, Mathf.Abs(v - y));
                        }
                    }

                    return best < 0.035f ? 1f : 0f;
                case PropCategory.Fuel: // flame: disc with a tip pointing up
                    bool inDisc = Sq(u - 0.5f) + Sq(v - 0.38f) < Sq(0.2f);
                    bool inTip = v >= 0.38f && v <= 0.86f && Mathf.Abs(u - 0.5f) < 0.2f * (0.86f - v) / 0.48f;
                    return inDisc || inTip ? 1f : 0f;
                case PropCategory.Optical: // diamond outline with a center dot
                    float l1 = Mathf.Abs(u - 0.5f) + Mathf.Abs(v - 0.5f);
                    return Mathf.Abs(l1 - 0.32f) < 0.045f || l1 < 0.07f ? 1f : 0f;
                case PropCategory.Kinetic: // arrow
                    d = Mathf.Min(Mathf.Min(Seg(u, v, 0.18f, 0.5f, 0.78f, 0.5f), Seg(u, v, 0.78f, 0.5f, 0.56f, 0.28f)), Seg(u, v, 0.78f, 0.5f, 0.56f, 0.72f));
                    return d < 0.05f ? 1f : 0f;
                case PropCategory.Acoustic: // concentric arcs
                    if (u < 0.28f)
                    {
                        return Sq(u - 0.28f) + Sq(v - 0.5f) < Sq(0.06f) ? 1f : 0f;
                    }

                    float r = Mathf.Sqrt(Sq(u - 0.28f) + Sq(v - 0.5f));
                    return Mathf.Abs(r - 0.18f) < 0.03f || Mathf.Abs(r - 0.32f) < 0.03f || r < 0.06f ? 1f : 0f;
                case PropCategory.Reactive: // burst
                    float burst = float.MaxValue;
                    for (int k = 0; k < 4; k++)
                    {
                        float angle = k * Mathf.PI / 4f;
                        float cx = 0.36f * Mathf.Cos(angle);
                        float cy = 0.36f * Mathf.Sin(angle);
                        burst = Mathf.Min(burst, Seg(u, v, 0.5f - cx, 0.5f - cy, 0.5f + cx, 0.5f + cy));
                    }

                    return burst < 0.04f ? 1f : 0f;
                default: // Utility: plus
                    d = Mathf.Min(Seg(u, v, 0.5f, 0.18f, 0.5f, 0.82f), Seg(u, v, 0.18f, 0.5f, 0.82f, 0.5f));
                    return d < 0.07f ? 1f : 0f;
            }
        }

        // ---- Helpers ---------------------------------------------------------------------------------------------

        private static Texture2D Render(string name, int size, PixelFunction f, TextureWrapMode wrap)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, false)
            {
                name = name,
                wrapMode = wrap,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8,
            };

            var pixels = new Color32[size * size];
            float inv = 1f / size;
            for (int y = 0; y < size; y++)
            {
                float v = (y + 0.5f) * inv;
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) * inv;
                    f(u, v, out float l, out float a);
                    byte lb = ToByte(l);
                    pixels[y * size + x] = new Color32(lb, lb, lb, ToByte(a));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(true, false);
            return texture;
        }

        private static byte ToByte(float value) => (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);

        private static float Frac(float x) => x - Mathf.Floor(x);

        private static float Sq(float x) => x * x;

        /// <summary>Signed shortest offset on the unit circle (-0.5, 0.5].</summary>
        private static float WrapDelta(float d) => d - Mathf.Round(d);

        private static float Seg(float px, float py, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax;
            float dy = by - ay;
            float t = Mathf.Clamp01(((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy));
            return Mathf.Sqrt(Sq(px - (ax + t * dx)) + Sq(py - (ay + t * dy)));
        }

        /// <summary>Seeded noise tables: per-pixel grain, per-cell values, and wrapped Voronoi feature points.</summary>
        private sealed class PatternNoise
        {
            private const int CellValues = 256;
            private const int MaxGrid = 8;

            private readonly int _size;
            private readonly float[] _grain;
            private readonly float[] _cells = new float[CellValues];
            private readonly float[] _featureX = new float[MaxGrid * MaxGrid];
            private readonly float[] _featureY = new float[MaxGrid * MaxGrid];

            public PatternNoise(ulong seed, int size)
            {
                var rng = new Rng(seed);
                _size = size;
                _grain = new float[size * size];
                for (int i = 0; i < _grain.Length; i++)
                {
                    _grain[i] = rng.NextFloat();
                }

                for (int i = 0; i < CellValues; i++)
                {
                    _cells[i] = rng.NextFloat();
                }

                for (int i = 0; i < _featureX.Length; i++)
                {
                    _featureX[i] = 0.15f + 0.7f * rng.NextFloat();
                    _featureY[i] = 0.15f + 0.7f * rng.NextFloat();
                }
            }

            /// <summary>Per-pixel noise in [0, 1).</summary>
            public float Grain(float u, float v)
            {
                int x = Mathf.Clamp((int)(Frac(u) * _size), 0, _size - 1);
                int y = Mathf.Clamp((int)(Frac(v) * _size), 0, _size - 1);
                return _grain[y * _size + x];
            }

            /// <summary>Stable value in [0, 1) for an integer cell key.</summary>
            public float Cell(int key) => _cells[((key % CellValues) + CellValues) % CellValues];

            /// <summary>Distances to the nearest and second-nearest feature point on a wrapped grid x grid lattice.</summary>
            public void Voronoi(int grid, float u, float v, out float f1, out float f2, out int nearestCell)
            {
                float best = float.MaxValue;
                float second = float.MaxValue;
                int bestCell = 0;
                int cx = (int)Mathf.Floor(u * grid);
                int cy = (int)Mathf.Floor(v * grid);
                for (int oy = -1; oy <= 1; oy++)
                {
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int gx = cx + ox;
                        int gy = cy + oy;
                        int index = Wrap(gy, grid) * MaxGrid + Wrap(gx, grid);
                        float px = (gx + _featureX[index]) / grid;
                        float py = (gy + _featureY[index]) / grid;
                        float d = Mathf.Sqrt(Sq(u - px) + Sq(v - py));
                        if (d < best)
                        {
                            second = best;
                            best = d;
                            bestCell = index;
                        }
                        else if (d < second)
                        {
                            second = d;
                        }
                    }
                }

                f1 = best;
                f2 = second;
                nearestCell = bestCell;
            }

            /// <summary>Calls visit with every feature point in the 3 x 3 neighborhood of (u, v), unwrapped to u, v space.</summary>
            public void ForEachFeature(int grid, float u, float v, Action<float, float> visit)
            {
                int cx = (int)Mathf.Floor(u * grid);
                int cy = (int)Mathf.Floor(v * grid);
                for (int oy = -1; oy <= 1; oy++)
                {
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int gx = cx + ox;
                        int gy = cy + oy;
                        int index = Wrap(gy, grid) * MaxGrid + Wrap(gx, grid);
                        visit((gx + _featureX[index]) / grid, (gy + _featureY[index]) / grid);
                    }
                }
            }

            private static int Wrap(int i, int n) => ((i % n) + n) % n;
        }
    }
}
