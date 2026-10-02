using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Reactions.Events;

namespace Confluence.Reactions.Hazards
{
    internal enum HazardShape
    {
        /// <summary>Ground cells that reference the hazard (charged patch, burning growth, venom residue).</summary>
        Cells,

        /// <summary>A surface box (red-hot metal, burning solid).</summary>
        Box,

        /// <summary>An obscuration volume (venom cloud).</summary>
        Volume,
    }

    /// <summary>
    /// A source of reaction contact damage. Damage requests always carry the hazard's owner and source event and are
    /// secondary (spec L126, L672). Once-hazards hit each body at most once (L148 "first contact"); per-second
    /// hazards hit at most once per <see cref="ReactionRules.ContactIntervalSec"/>.
    /// </summary>
    internal sealed class Hazard
    {
        public int Id;
        public ReactionKind Kind;
        public int Owner;
        public int Source;
        public DamageChannel Channel;

        /// <summary>Damage per hit (once-hazards) or per second (per-second hazards).</summary>
        public float Amount;

        public bool PerSecond;
        public int CreatedTick;
        public int ExpiresTick;
        public HazardShape Shape;
        public YawBox Box;
        public int VolumeId;

        /// <summary>Only grounded bodies touch the hazard.</summary>
        public bool GroundOnly;

        public bool Soluble;
        public bool Diluted;

        /// <summary>Linked object (conductor, solid, prop) for presentation, or -1.</summary>
        public int ObjectId = -1;

        /// <summary>Cells currently referencing the hazard.</summary>
        public int CellCount;

        /// <summary>Body id to the first tick it may be hit again (int.MaxValue after a once-hit). Lookup only.</summary>
        public readonly Dictionary<int, int> NextHit = new Dictionary<int, int>();

        public float Factor => Diluted ? GlobalRules.World.VenomDilutionFactor : 1f;

        public bool IsExpired(int tick) => ExpiresTick <= tick;
    }
}
