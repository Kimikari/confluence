namespace Confluence.Reactions.Volumes
{
    /// <summary>Server-owned state volumes (spec L134 "steam is a server-owned obscuration volume").</summary>
    public enum VolumeKind
    {
        /// <summary>Heat-water reaction cloud (L144, L167).</summary>
        Steam,

        /// <summary>Opaque ash cloud (SPL-012 Ash Curtain).</summary>
        Ash,

        /// <summary>Mist (SPL-188 Mist Beads; mist breaks detection lines, SPL-103, SPL-174).</summary>
        Mist,

        /// <summary>Raised dust or sand screen (L151, L154).</summary>
        Dust,

        /// <summary>Smoke, such as burned venom residue (SPL-252) or SPL-105's smoke.</summary>
        Smoke,

        /// <summary>Shadow concealment volume (L158).</summary>
        ShadowConcealment,

        /// <summary>Damaging venom cloud (PROP-038, BIO-016).</summary>
        VenomCloud,

        /// <summary>Public silhouette illumination (BIO-017); reveals nothing through solid cover.</summary>
        Illumination,

        /// <summary>Bright light that shortens shadow concealment (L158, PROP-023 Sunstone).</summary>
        BrightLight,
    }

    /// <summary>Default rules per volume kind; a creating spell may override them with its stated text.</summary>
    public static class VolumeKinds
    {
        /// <summary>Whether path length through the volume counts toward the 2 m acquisition block (L167).</summary>
        public static bool BlocksSightByDefault(VolumeKind kind)
        {
            switch (kind)
            {
                case VolumeKind.Steam:
                case VolumeKind.Ash:
                case VolumeKind.Mist:
                case VolumeKind.Dust:
                case VolumeKind.Smoke:
                case VolumeKind.ShadowConcealment:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Whether wind may translate and disperse the volume (L151 "Wind and steam or dust"; SPL-012 "Wind displaces"
        /// ash; SPL-188 "wind clears mist"; SPL-105 smoke). Shadow moves only where its spell says so (SPL-179).
        /// </summary>
        public static bool WindMovableByDefault(VolumeKind kind)
        {
            switch (kind)
            {
                case VolumeKind.Steam:
                case VolumeKind.Ash:
                case VolumeKind.Mist:
                case VolumeKind.Dust:
                case VolumeKind.Smoke:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Bright light drains this kind's remaining lifetime 1 s per second of exposure (L158).</summary>
        public static bool DrainedByBrightLight(VolumeKind kind) => kind == VolumeKind.ShadowConcealment;

        /// <summary>Kinds whose overlap counts as bright-light exposure for shadow volumes (L158).</summary>
        public static bool IsBrightLight(VolumeKind kind) => kind == VolumeKind.BrightLight;
    }
}
