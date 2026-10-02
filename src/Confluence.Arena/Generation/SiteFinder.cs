using System;
using System.Collections.Generic;
using Confluence.Arena.Navigation;
using Confluence.Arena.Validation;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Random;
using Confluence.Core.Rules;

namespace Confluence.Arena.Generation
{
    /// <summary>
    /// Finds open main-floor spots on side 0 for biome features and props. A site needs navigation clearance (the
    /// distance to the nearest wall or edge) of at least the requested amount, so an object of the given radius
    /// leaves a passage around itself. Sites keep off the skeleton routes and away from the spawn alcoves (never the
    /// spawn floor), the final circle (never its support), the contour, existing patches, and earlier sites (spec L662
    /// placement text).
    /// </summary>
    internal sealed class SiteFinder
    {
        private readonly ArenaNav _nav;
        private readonly ArenaLayout _layout;
        private readonly List<Vec3> _sites;
        private readonly List<Rect2> _keepOut;
        private readonly List<Rect2> _protected;

        /// <summary>
        /// keepOut: the skeleton route zones. protectedZones: their cores (bastions, firing lane), where nothing is
        /// placed at all. Inside the other zones a blocking object needs a full route of clearance around it.
        /// </summary>
        public SiteFinder(ArenaNav nav, ArenaLayout layout, List<Vec3> sites, List<Rect2> keepOut, List<Rect2> protectedZones)
        {
            _nav = nav;
            _layout = layout;
            _sites = sites;
            _keepOut = keepOut;
            _protected = protectedZones;
        }

        /// <summary>Clearance a blocking object inside a skeleton zone keeps around itself (a 2.5 m route plus margin).</summary>
        public const float RouteClearance = 2.75f;

        /// <summary>Margin between a site's footprint and the initial contour.</summary>
        public const float SiteContourMargin = 0.4f;

        public bool TryFind(Rng rng, float clearance, float radius, bool blocksMovement, out Vec3 site)
        {
            var candidates = new List<int>();
            for (int node = 0; node < _nav.NodeCount; node++)
            {
                float room = _nav.Clearance(node);
                if (room >= clearance && Accepts(node, radius, blocksMovement, room))
                {
                    candidates.Add(node);
                }
            }

            if (candidates.Count == 0)
            {
                site = default(Vec3);
                return false;
            }

            Vec3 p = _nav.NodePosition(candidates[rng.NextInt(candidates.Count)]);
            site = new Vec3(p.X, 0f, p.Z);
            return true;
        }

        private bool Accepts(int node, float radius, bool blocksMovement, float room)
        {
            Vec3 p = _nav.NodePosition(node);
            Vec2 q = p.XZ();
            if (MathF.Abs(p.Y) > 0.05f || q.Y > -(radius + 1f))
            {
                return false; // main floor, and far enough from the center line that the mirror image never touches
            }

            if (_layout.Boundary.RadialMargin(p, 0f) < radius + SiteContourMargin)
            {
                return false;
            }

            if (q.Length() < _layout.FinalFloorRadius + radius + ArenaRules.PropFinalCircleMargin + 0.5f)
            {
                return false;
            }

            for (int i = 0; i < _protected.Count; i++)
            {
                if (_protected[i].DistanceTo(q) < radius + 0.25f)
                {
                    return false;
                }
            }

            // Skeleton routes keep their width: a blocking object there needs a full route around it, so it is never
            // the only route (spec L662 prop placement text).
            if (blocksMovement && room < radius + RouteClearance)
            {
                for (int i = 0; i < _keepOut.Count; i++)
                {
                    if (_keepOut[i].DistanceTo(q) < radius + 0.25f)
                    {
                        return false;
                    }
                }
            }

            for (int side = 0; side < 2; side++)
            {
                YawBox alcove = _layout.Spawns[side].Alcove;
                if (Vec2.Distance(alcove.ClosestPointXZ(q), q) < radius + 1.5f)
                {
                    return false;
                }
            }

            for (int i = 0; i < _sites.Count; i++)
            {
                Vec3 s = _sites[i];
                float gap = s.Y + radius + 1.5f;
                if (Vec2.DistanceSquared(new Vec2(s.X, s.Z), q) < gap * gap || Vec2.DistanceSquared(new Vec2(-s.X, -s.Z), q) < gap * gap)
                {
                    return false;
                }
            }

            foreach (Solid s in _layout.Solids)
            {
                if (s.Walkable && s.Kind != SolidKind.Floor && ArenaSurvey.FootprintDistance(s.Box, q) < radius + 0.5f)
                {
                    return false; // never on or against a deck, ramp, or bridge
                }
            }

            for (int i = 0; i < _layout.Materials.Count; i++)
            {
                MaterialPatch m = _layout.Materials[i];
                float extent = m.IsCircle ? m.Radius : m.HalfSize.Length();
                if (Vec2.Distance(m.Center, q) < extent + radius + 0.25f)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
