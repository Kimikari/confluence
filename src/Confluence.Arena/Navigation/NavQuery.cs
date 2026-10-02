namespace Confluence.Arena.Navigation
{
    /// <summary>
    /// Filter for navigation searches: minimum clearance (capsule or route width), permitted edge kinds, and
    /// optional per-node masks (for example a simulated field footprint or the area inside a contracting boundary).
    /// </summary>
    public sealed class NavQuery
    {
        /// <summary>Any node a player capsule fits through (clearance at least the 0.4 m capsule radius).</summary>
        public static readonly NavQuery Capsule = new NavQuery { MinClearance = ArenaRules.CapsuleClearance };

        /// <summary>
        /// Routes at least 2.5 m wide (spec L312). On the 0.5 m lattice the best node of a corridor can sit up to half a
        /// cell (0.25 m) off its center line, so the threshold is half the route width minus that half cell.
        /// </summary>
        public static readonly NavQuery Route = new NavQuery { MinClearance = ArenaRules.RouteClearance - ArenaRules.NavCellSize * 0.5f };

        public float MinClearance { get; set; } = ArenaRules.CapsuleClearance;
        public bool AllowJumps { get; set; } = true;
        public bool AllowDrops { get; set; } = true;

        /// <summary>Nodes that may not be entered (true = blocked). Length must equal the node count when set.</summary>
        public bool[] Blocked { get; set; }

        /// <summary>Nodes that may be entered (true = allowed). Length must equal the node count when set.</summary>
        public bool[] AllowedMask { get; set; }

        public NavQuery Clone()
        {
            return new NavQuery
            {
                MinClearance = MinClearance,
                AllowJumps = AllowJumps,
                AllowDrops = AllowDrops,
                Blocked = Blocked,
                AllowedMask = AllowedMask,
            };
        }

        public NavQuery WithBlocked(bool[] blocked)
        {
            NavQuery q = Clone();
            q.Blocked = blocked;
            return q;
        }

        public NavQuery WithAllowed(bool[] allowed)
        {
            NavQuery q = Clone();
            q.AllowedMask = allowed;
            return q;
        }

        internal bool Allows(ArenaNav nav, int node)
        {
            // A tiny tolerance absorbs float noise in clearances that sit exactly on a route-width threshold.
            if (nav.Clearance(node) < MinClearance - 1e-3f)
            {
                return false;
            }

            if (Blocked != null && Blocked[node])
            {
                return false;
            }

            return AllowedMask == null || AllowedMask[node];
        }

        internal bool AllowsEdge(NavEdgeKind kind)
        {
            switch (kind)
            {
                case NavEdgeKind.Jump: return AllowJumps;
                case NavEdgeKind.Drop: return AllowDrops;
                default: return true;
            }
        }
    }
}
