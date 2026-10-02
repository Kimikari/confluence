using System;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Random;
using Confluence.Core.Rules;

namespace Confluence.Arena.Generation
{
    /// <summary>
    /// The irregular closed boundary (spec L306 "an irregular but validated closed contour, not an invisible
    /// rectangular wall"). Radii are a smooth sum of even harmonics, so r(a) = r(a + 180) and the contraction is
    /// identical from both spawns (functional symmetry, L308). Samples vary at most
    /// <see cref="ArenaRules.ContourIrregularity"/> around the tier radius and are rescaled so the enclosed area
    /// equals the tier target exactly (well inside the 10 percent usable-area tolerance, L306).
    /// </summary>
    public static class BoundaryBuilder
    {
        private const int Harmonics = 3;

        public static BoundaryContour Build(Rng rng, SizeProfile profile)
        {
            int n = BoundaryContour.DefaultSampleCount;
            float[] amplitude = new float[Harmonics];
            float[] phase = new float[Harmonics];
            float total = 0f;
            for (int k = 0; k < Harmonics; k++)
            {
                amplitude[k] = rng.Range(0.3f, 1f) / (k + 1);
                phase[k] = rng.Range(0f, CMath.TwoPi);
                total += amplitude[k];
            }

            var contour = new BoundaryContour { Center = Vec3.Zero, InitialRadii = new float[n] };
            float scale = ArenaRules.ContourIrregularity / Math.Max(total, 1e-3f);
            for (int i = 0; i < n; i++)
            {
                float a = i * CMath.TwoPi / n;
                float noise = 0f;
                for (int k = 0; k < Harmonics; k++)
                {
                    noise += amplitude[k] * MathF.Cos(2f * (k + 1) * a + phase[k]); // even harmonics only: point symmetric
                }

                contour.InitialRadii[i] = profile.Radius * (1f + noise * scale);
            }

            // Exact half-turn symmetry despite float noise: copy the first half onto the second.
            for (int i = 0; i < n / 2; i++)
            {
                contour.InitialRadii[i + n / 2] = contour.InitialRadii[i];
            }

            float area = contour.AreaAt(0f);
            float fix = MathF.Sqrt(profile.TargetArea / area);
            for (int i = 0; i < n; i++)
            {
                contour.InitialRadii[i] = Math.Max(GlobalRules.Boundary.FinalFloorRadius, contour.InitialRadii[i] * fix);
            }

            return contour;
        }

        /// <summary>True when the whole rectangle lies inside the initial contour with a margin.</summary>
        public static bool ContainsRect(BoundaryContour contour, Rect2 rect, float margin)
        {
            for (int i = 0; i < 4; i++)
            {
                if (contour.RadialMargin(rect.Corner(i).ToXZ(), 0f) < margin)
                {
                    return false;
                }
            }

            // Edge midpoints guard the shallow concavities between samples.
            Vec2 c = rect.Center;
            return contour.RadialMargin(new Vec3(c.X, 0f, rect.MinZ), 0f) >= margin
                && contour.RadialMargin(new Vec3(c.X, 0f, rect.MaxZ), 0f) >= margin
                && contour.RadialMargin(new Vec3(rect.MinX, 0f, c.Y), 0f) >= margin
                && contour.RadialMargin(new Vec3(rect.MaxX, 0f, c.Y), 0f) >= margin;
        }
    }
}
