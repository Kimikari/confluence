using System;
using System.Collections.Generic;

namespace Confluence.Arena.Generation
{
    /// <summary>
    /// The main floor as a set of anchored slabs around the cutouts of sunken modules (MOD-028, MOD-035), which lay
    /// their own lower floors (spec L686 "Height is relative to connected floor"). The square is split along every
    /// cutout edge and the remaining cells are merged along rows, so the pieces are few and deterministic.
    /// </summary>
    public static class FloorSlabs
    {
        public static List<Rect2> Pieces(float half, IReadOnlyList<Rect2> cutouts)
        {
            var xs = new List<float> { -half, half };
            var zs = new List<float> { -half, half };
            foreach (Rect2 c in cutouts)
            {
                AddCut(xs, c.MinX, half);
                AddCut(xs, c.MaxX, half);
                AddCut(zs, c.MinZ, half);
                AddCut(zs, c.MaxZ, half);
            }

            xs.Sort();
            zs.Sort();
            var result = new List<Rect2>();
            for (int j = 0; j + 1 < zs.Count; j++)
            {
                float runStart = float.NaN;
                for (int i = 0; i + 1 < xs.Count; i++)
                {
                    var cell = new Rect2(xs[i], zs[j], xs[i + 1], zs[j + 1]);
                    bool solid = !IsCut(cell, cutouts);
                    if (solid && float.IsNaN(runStart))
                    {
                        runStart = xs[i];
                    }

                    bool last = i + 2 == xs.Count;
                    if ((!solid || last) && !float.IsNaN(runStart))
                    {
                        float runEnd = solid ? xs[i + 1] : xs[i];
                        result.Add(new Rect2(runStart, zs[j], runEnd, zs[j + 1]));
                        runStart = float.NaN;
                    }
                }
            }

            return result;
        }

        private static void AddCut(List<float> cuts, float value, float half)
        {
            if (value <= -half || value >= half)
            {
                return;
            }

            for (int i = 0; i < cuts.Count; i++)
            {
                if (Math.Abs(cuts[i] - value) < 1e-4f)
                {
                    return;
                }
            }

            cuts.Add(value);
        }

        private static bool IsCut(Rect2 cell, IReadOnlyList<Rect2> cutouts)
        {
            var mid = cell.Center;
            foreach (Rect2 c in cutouts)
            {
                if (c.Contains(mid))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
