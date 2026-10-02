using Confluence.Core.Arena;
using Confluence.Core.Geometry;

namespace Confluence.Client.Arena
{
    /// <summary>Read-only geometric queries over an ArenaLayout used by presentation code (preview walker, markers).</summary>
    public static class ArenaQueries
    {
        /// <summary>
        /// Height of the highest solid surface under (x, z) that is at most maxY, or NaN when nothing is below.
        /// Ramps report their sloped surface (Core RampShape.SurfaceHeightAt); boxes their top.
        /// </summary>
        public static float SurfaceHeightAt(ArenaLayout layout, float x, float z, float maxY)
        {
            float best = float.NaN;
            if (layout?.Solids == null)
            {
                return best;
            }

            var point = new Vec2(x, z);
            for (int i = 0; i < layout.Solids.Count; i++)
            {
                Solid solid = layout.Solids[i];
                if (solid == null || !solid.Box.ContainsXZ(point))
                {
                    continue;
                }

                float top = solid.IsRamp ? solid.Ramp.SurfaceHeightAt(x, z) : solid.Box.Top;
                if (float.IsNaN(top) || top > maxY)
                {
                    continue;
                }

                if (float.IsNaN(best) || top > best)
                {
                    best = top;
                }
            }

            return best;
        }

        /// <summary>Largest initial boundary radius (for framing cameras); falls back to half the diameter.</summary>
        public static float OuterRadius(ArenaLayout layout)
        {
            if (layout?.Boundary != null && layout.Boundary.SampleCount > 0)
            {
                return layout.Boundary.MaxRadiusAt(0f);
            }

            return layout == null ? 16f : layout.Diameter * 0.5f;
        }
    }
}
