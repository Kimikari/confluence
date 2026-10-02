using Confluence.Core.Geometry;
using Confluence.Reactions;
using ReactionSurface = Confluence.Reactions.SurfaceInfo;
using ReactionSurfaceKind = Confluence.Reactions.SurfaceKind;

namespace Confluence.Sim.World.Materials
{
    /// <summary>
    /// Maps a reaction cell's surface (Reactions <see cref="ReactionSurface"/>) to what the character motor uses
    /// (<see cref="SurfaceInfo"/>, spec L175): ice reduces braking and adds lateral drift but never steals control;
    /// sticky growth slows within the global 30% (the motor command applies the cap with the strongest-slow rule);
    /// wet stone changes reaction eligibility and footsteps only; damp sand is stable footing. Burning, charged, and
    /// venom cells are contact hazards the reaction world resolves as damage, not movement. Traction comes from the
    /// most restrictive movement condition present, so a charged ice tile is still ice.
    /// </summary>
    public static class SurfaceMapping
    {
        /// <summary>
        /// Feet farther than this above or below the cell's surface do not stand on it (a platform above a wet floor
        /// cell; reaction cells are 2D). Equal to the reaction world's own contact tolerance.
        /// </summary>
        public const float StandingTolerance = ReactionRules.GroundContactTolerance;

        public static SurfaceInfo ToMotor(in ReactionSurface surface, Vec3 feet)
        {
            if (surface.CellIndex < 0 || surface.Flags == SurfaceFlags.None || !Touches(surface, feet))
            {
                return SurfaceInfo.Normal;
            }

            if (surface.Has(SurfaceFlags.Ice))
            {
                return new SurfaceInfo { Kind = SurfaceKind.Ice };
            }

            if (surface.Has(SurfaceFlags.StickyGrowth))
            {
                return new SurfaceInfo { Kind = SurfaceKind.Sticky, SlowFraction = surface.SlowFraction };
            }

            if (surface.Has(SurfaceFlags.DampSand))
            {
                return new SurfaceInfo { Kind = SurfaceKind.DampSand };
            }

            if (surface.Has(SurfaceFlags.Wet))
            {
                return new SurfaceInfo { Kind = SurfaceKind.Wet };
            }

            return SurfaceInfo.Normal;
        }

        /// <summary>The footstep surface for audio and HUD (wet stone, sand, growth...), or Normal when not standing on the cell.</summary>
        public static ReactionSurfaceKind Footstep(in ReactionSurface surface, Vec3 feet)
        {
            return surface.CellIndex >= 0 && Touches(surface, feet) ? surface.Kind : ReactionSurfaceKind.Normal;
        }

        /// <summary>Whether feet stand on the cell's surface height.</summary>
        public static bool Touches(in ReactionSurface surface, Vec3 feet)
        {
            float dy = feet.Y - surface.SurfaceY;
            return dy <= StandingTolerance && dy >= -StandingTolerance;
        }
    }
}
