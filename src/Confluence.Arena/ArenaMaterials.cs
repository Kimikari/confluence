using System;
using Confluence.Core.Rules;

namespace Confluence.Arena
{
    /// <summary>
    /// Material names written into Solid.Material and the reaction flags each one carries. Names come from the
    /// catalogue's module and biome material lists; feature names (prism, grounding-statue, ...) mark solids whose
    /// specific behavior the Sim and Reactions read. Art names never imply collision or damage (spec L656): a
    /// flag is set only where the catalogue states the behavior (for example metal conducts, L149; growth burns, L153).
    /// </summary>
    public static class ArenaMaterials
    {
        public const string Stone = "stone";
        public const string Slate = "slate";
        public const string Basalt = "basalt";
        public const string Rock = "rock";
        public const string Wood = "wood";
        public const string Metal = "metal";
        public const string Glass = "glass";
        public const string Porous = "porous";
        public const string Growth = "growth";
        public const string Hedge = "hedge";
        public const string Ice = "ice";
        public const string Sand = "sand";
        public const string Water = "water";

        // Feature solids with catalogue behavior.
        public const string Prism = "prism";                    // ray reflection (spec L157, PROP-019, MOD-019/042/064)
        public const string GroundingStatue = "grounding-statue"; // absorbs one branch (spec L150, MOD-043, BIO-012)
        public const string Bell = "bell";                      // public ring (MOD-044)
        public const string AcousticPlate = "acoustic-plate";   // public ring (BIO-009, MOD-067)
        public const string HeatSink = "heat-sink";             // finite heat absorption (MOD-045, BIO-020)
        public const string Grille = "metal-grille";            // open upper grille: blocks movement, not sight (MOD-020)
        public const string SlidingShield = "sliding-shield";   // medium-mass cover on a marked track (MOD-023)
        public const string EscortCover = "escort-cover";       // heavy cover on a 0.5 m track (MOD-057)
        public const string Mirror = "mirror";                  // decoy-flash mirror panel (BIO-014)
        public const string Shutter = "shutter";                // timed shutter (BIO-018)
        public const string Lantern = "lantern";                // one-use illumination (BIO-017)
        public const string DustUrn = "dust-urn";               // spills Dust 1 on destruction (BIO-022)
        public const string MagneticCoil = "magnetic-coil";     // projectile deflection pulse (BIO-023)
        public const string Trunk = "trunk";                    // anchored orchard trunk that does not burn (BIO-002)
        public const string Floor = "floor";                    // main floor slab
        public const string Alcove = "alcove";                  // spawn alcove walls

        /// <summary>Reaction flags of a material name (case-insensitive); anchored pieces add Anchored separately.</summary>
        public static MaterialFlags FlagsFor(string material)
        {
            switch ((material ?? string.Empty).ToLowerInvariant())
            {
                case Metal:
                case Grille:
                case SlidingShield:
                case EscortCover:
                case HeatSink:
                case Shutter:
                case MagneticCoil:
                    return MaterialFlags.Metal | MaterialFlags.Conductive;
                case Bell:
                case AcousticPlate:
                    return MaterialFlags.Metal | MaterialFlags.Conductive | MaterialFlags.Acoustic;
                case GroundingStatue:
                    return MaterialFlags.Metal | MaterialFlags.Conductive | MaterialFlags.Grounded;
                case Glass:
                    return MaterialFlags.Glass;
                case Prism:
                    return MaterialFlags.Glass | MaterialFlags.Reflective;
                case Mirror:
                    return MaterialFlags.Reflective;
                case Porous:
                    return MaterialFlags.Porous | MaterialFlags.Absorbent;
                case Growth:
                case Hedge:
                    return MaterialFlags.Growth | MaterialFlags.Combustible;
                case Ice:
                    return MaterialFlags.Ice | MaterialFlags.Brittle;
                case Sand:
                    return MaterialFlags.Sand;
                case Water:
                    return MaterialFlags.Water;
                default:
                    return MaterialFlags.None;
            }
        }

        public static bool IsMaterial(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
