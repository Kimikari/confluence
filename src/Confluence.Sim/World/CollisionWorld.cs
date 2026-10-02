using System;
using System.Collections.Generic;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Sim.Hashing;

namespace Confluence.Sim.World
{
    /// <summary>
    /// Kinematic collision and sight world of one duel (DECISIONS D-003): the layout's validated boxes and ramps
    /// plus dynamic temporary solids with durability and destruction (spec L134, L171). Queries iterate solids in a
    /// fixed order (layout order, then creation order) and break distance ties toward the earlier solid, so every
    /// result is deterministic. The server and client prediction build identical instances from the same layout.
    /// </summary>
    public sealed class CollisionWorld
    {
        /// <summary>Dynamic solid ids start here so they never collide with layout ids.</summary>
        public const int DynamicIdBase = 1 << 20;

        private readonly List<CollisionSolid> _solids = new List<CollisionSolid>();
        private readonly Dictionary<int, CollisionSolid> _byId = new Dictionary<int, CollisionSolid>();
        private int _nextDynamicId = DynamicIdBase;

        public CollisionWorld(IEnumerable<Solid> layoutSolids)
        {
            if (layoutSolids != null)
            {
                foreach (Solid s in layoutSolids)
                {
                    if (s == null)
                    {
                        continue;
                    }

                    if (_byId.ContainsKey(s.Id))
                    {
                        throw new ArgumentException("Duplicate layout solid id " + s.Id + ".");
                    }

                    bool anchored = s.Anchored || !(s.Durability > 0f);
                    var solid = new CollisionSolid(s.Id, s.Kind, s.Box, s.RampRise, s.Flags, s.BlocksSight,
                        s.Walkable || IsWalkableKind(s.Kind), anchored, anchored ? 0f : s.Durability, false, -1, null, 0,
                        int.MaxValue, s.RetiresAtFinal);
                    Add(solid);
                }
            }

            KillPlaneY = ComputeKillPlane();
        }

        public static CollisionWorld FromLayout(ArenaLayout layout) => new CollisionWorld(layout?.Solids);

        /// <summary>Solids in deterministic query order (inactive ones included).</summary>
        public IReadOnlyList<CollisionSolid> Solids => _solids;

        /// <summary>Below this height a falling player has left the map (spec L175 outside-map falls).</summary>
        public float KillPlaneY { get; }

        /// <summary>Incremented whenever a solid is added, damaged, destroyed, or removed.</summary>
        public int Version { get; private set; }

        public CollisionSolid Find(int id) => _byId.TryGetValue(id, out CollisionSolid s) ? s : null;

        public static bool IsWalkableKind(SolidKind kind)
        {
            return kind == SolidKind.Floor || kind == SolidKind.Platform || kind == SolidKind.Ramp || kind == SolidKind.Stair;
        }

        // ----- dynamic solids --------------------------------------------------------------------------------

        /// <summary>Adds a temporary solid (wall, ice block, growth) and returns its id.</summary>
        public int AddTemporarySolid(TemporarySolidSpec spec, int currentTick)
        {
            int id = _nextDynamicId++;
            int expire = spec.LifetimeTicks > 0 ? currentTick + spec.LifetimeTicks : int.MaxValue;
            bool anchored = !(spec.Durability > 0f);
            var solid = new CollisionSolid(id, spec.Kind, spec.Box, 0f, spec.Flags, spec.BlocksSight, spec.Walkable,
                anchored, spec.Durability, true, spec.OwnerPlayer, spec.SourceId, currentTick, expire, false);
            Add(solid);
            return id;
        }

        /// <summary>
        /// Applies durability damage to a destructible solid. Returns true if any durability was removed;
        /// destroyed is true when this damage broke it (collision changes in the same update, spec L171).
        /// </summary>
        public bool DamageSolid(int id, float amount, out bool destroyed)
        {
            destroyed = false;
            CollisionSolid solid = Find(id);
            if (solid == null || !solid.Active || !solid.Destructible || !(amount > 0f))
            {
                return false;
            }

            solid.Durability = Math.Max(0f, solid.Durability - amount);
            if (solid.Durability <= 0f)
            {
                solid.Active = false;
                destroyed = true;
            }

            Version++;
            return true;
        }

        /// <summary>Deactivates a solid (expiry, dispel, retirement). Anchored layout terrain is never removed.</summary>
        public bool Remove(int id)
        {
            CollisionSolid solid = Find(id);
            if (solid == null || !solid.Active || (!solid.Dynamic && solid.Anchored && !solid.RetiresAtFinal))
            {
                return false;
            }

            solid.Active = false;
            Version++;
            return true;
        }

        /// <summary>
        /// Moves an active dynamic solid to a new box (a pushed prop, spec L155, L173). Layout terrain never moves.
        /// Returns false when the solid is missing, inactive, or static.
        /// </summary>
        public bool MoveSolid(int id, YawBox box)
        {
            CollisionSolid solid = Find(id);
            if (solid == null || !solid.Active || !solid.Dynamic || solid.IsRamp)
            {
                return false;
            }

            solid.MoveTo(box);
            Version++;
            return true;
        }

        /// <summary>Deactivates temporary solids whose lifetime ended at or before tick, appending their ids.</summary>
        public void ExpireTemporarySolids(int tick, List<int> expired)
        {
            for (int i = 0; i < _solids.Count; i++)
            {
                CollisionSolid s = _solids[i];
                if (s.Active && s.Dynamic && s.ExpireTick <= tick)
                {
                    s.Active = false;
                    Version++;
                    expired?.Add(s.Id);
                }
            }
        }

        // ----- queries ---------------------------------------------------------------------------------------

        /// <summary>Nearest solid along a ray within maxDistance.</summary>
        public bool Raycast(Vec3 origin, Vec3 direction, float maxDistance, SolidMask mask, out WorldHit hit)
        {
            hit = default;
            var ray = new Ray(origin, direction);
            if (ray.Direction.LengthSquared() == 0f || !(maxDistance >= 0f))
            {
                return false;
            }

            Aabb sweep = new Aabb(origin, ray.PointAt(maxDistance));
            float best = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < _solids.Count; i++)
            {
                CollisionSolid s = _solids[i];
                if (!s.Matches(mask) || !s.Bounds.Intersects(sweep))
                {
                    continue;
                }

                if (s.Raycast(ray, maxDistance, out RayHit h) && h.Distance < best)
                {
                    best = h.Distance;
                    hit = new WorldHit { Distance = h.Distance, Point = h.Point, Contact = h.Point, Normal = h.Normal, SolidId = s.Id };
                    found = true;
                }
            }

            return found;
        }

        /// <summary>Nearest solid crossing the segment a-b; Distance is meters from a.</summary>
        public bool SegmentCast(Vec3 a, Vec3 b, SolidMask mask, out WorldHit hit)
        {
            Vec3 d = b - a;
            float len = d.Length();
            if (len < CMath.Epsilon)
            {
                hit = default;
                int id = FirstContaining(a, mask);
                if (id < 0)
                {
                    return false;
                }

                hit = new WorldHit { Distance = 0f, Point = a, Contact = a, Normal = Vec3.Up, SolidId = id };
                return true;
            }

            return Raycast(a, d / len, len, mask, out hit);
        }

        /// <summary>
        /// Sphere swept from start to end: the earliest contact. hit.Point is the sphere center at contact and
        /// hit.Distance the travelled distance; an initial touch reports distance 0.
        /// </summary>
        public bool SphereSweep(Vec3 start, Vec3 end, float radius, SolidMask mask, out WorldHit hit)
        {
            hit = default;
            Aabb sweep = new Aabb(start, end).Expanded(radius);
            float bestT = float.PositiveInfinity;
            bool found = false;
            float len = (end - start).Length();
            for (int i = 0; i < _solids.Count; i++)
            {
                CollisionSolid s = _solids[i];
                if (!s.Matches(mask) || !s.Bounds.Intersects(sweep))
                {
                    continue;
                }

                if (s.SphereSweep(start, end, radius, out float t01, out RayHit h) && t01 < bestT)
                {
                    bestT = t01;
                    Vec3 center = Vec3.LerpUnclamped(start, end, t01);
                    hit = new WorldHit { Distance = t01 * len, Point = center, Contact = center - h.Normal * radius, Normal = h.Normal, SolidId = s.Id };
                    found = true;
                }
            }

            return found;
        }

        /// <summary>Appends the ids of solids overlapping the capsule; returns how many were added.</summary>
        public int OverlapCapsule(Capsule capsule, SolidMask mask, List<int> results)
        {
            Aabb bounds = capsule.Bounds();
            int count = 0;
            for (int i = 0; i < _solids.Count; i++)
            {
                CollisionSolid s = _solids[i];
                if (!s.Matches(mask) || !s.Bounds.Intersects(bounds))
                {
                    continue;
                }

                if (s.CapsulePenetration(capsule, out Penetration p) && p.Depth > 0f)
                {
                    results?.Add(s.Id);
                    count++;
                }
            }

            return count;
        }

        /// <summary>Appends the ids of solids overlapping the sphere; returns how many were added.</summary>
        public int OverlapSphere(Vec3 center, float radius, SolidMask mask, List<int> results)
        {
            return OverlapCapsule(new Capsule(center, center, radius), mask, results);
        }

        /// <summary>True if the capsule overlaps no movement-blocking solid by more than tolerance.</summary>
        public bool IsCapsuleClear(Capsule capsule, float tolerance = 1e-4f)
        {
            Aabb bounds = capsule.Bounds();
            for (int i = 0; i < _solids.Count; i++)
            {
                CollisionSolid s = _solids[i];
                if (!s.Matches(SolidMask.Movement) || !s.Bounds.Intersects(bounds))
                {
                    continue;
                }

                if (s.CapsulePenetration(capsule, out Penetration p) && p.Depth > tolerance)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The deepest overlap of a character capsule with any movement-blocking solid.</summary>
        public bool DeepestPenetration(Capsule capsule, out Penetration penetration, out int solidId)
        {
            penetration = default;
            solidId = -1;
            Aabb bounds = capsule.Bounds();
            float best = 0f;
            for (int i = 0; i < _solids.Count; i++)
            {
                CollisionSolid s = _solids[i];
                if (!s.Matches(SolidMask.Movement) || !s.Bounds.Intersects(bounds))
                {
                    continue;
                }

                if (s.CapsulePenetration(capsule, out Penetration p) && p.Depth > best)
                {
                    best = p.Depth;
                    penetration = p;
                    solidId = s.Id;
                }
            }

            return solidId >= 0;
        }

        /// <summary>
        /// Downward sweep of a sphere resting at feet (center feet + radius), from lift above to distance below.
        /// A contact counts as ground when its normal is at most as steep as minNormalY, or when the sphere rests on
        /// the edge of a walkable top surface (a ledge or stair nosing; ledge is then true and the reported normal is
        /// that surface's normal). Side contacts with walls are ignored, so standing beside a wall still finds the
        /// floor. hit.Point is the sphere center at contact.
        /// </summary>
        public bool ProbeGround(Vec3 feet, float radius, float lift, float distance, float minNormalY, out WorldHit hit, out bool ledge)
        {
            hit = default;
            ledge = false;
            Vec3 start = feet + new Vec3(0f, radius + lift, 0f);
            Vec3 end = feet + new Vec3(0f, radius - distance, 0f);
            Aabb sweep = new Aabb(start, end).Expanded(radius);
            float bestT = float.PositiveInfinity;
            float len = lift + distance;
            bool found = false;
            for (int i = 0; i < _solids.Count; i++)
            {
                CollisionSolid s = _solids[i];
                if (!s.Matches(SolidMask.Movement) || !s.Bounds.Intersects(sweep))
                {
                    continue;
                }

                if (!s.SphereSweep(start, end, radius, out float t01, out RayHit h) || !(t01 < bestT))
                {
                    continue;
                }

                Vec3 center = Vec3.LerpUnclamped(start, end, t01);
                Vec3 normal = h.Normal;
                bool isLedge = false;
                if (normal.Y < minNormalY)
                {
                    if (!IsLedgeContact(s, center, normal, radius, minNormalY, out Vec3 surfaceNormal))
                    {
                        continue;
                    }

                    isLedge = true;
                    normal = surfaceNormal;
                }

                bestT = t01;
                hit = new WorldHit { Distance = t01 * len, Point = center, Contact = center - h.Normal * radius, Normal = normal, SolidId = s.Id };
                ledge = isLedge;
                found = true;
            }

            return found;
        }

        // A sphere touching a solid's edge rests on a ledge when the contact lies on the solid's walkable top surface.
        private static bool IsLedgeContact(CollisionSolid s, Vec3 center, Vec3 normal, float radius, float minNormalY, out Vec3 surfaceNormal)
        {
            surfaceNormal = Vec3.Up;
            if (normal.Y <= 0f)
            {
                return false;
            }

            Vec3 contact = center - normal * radius;
            Vec3 inward = new Vec3(-normal.X, 0f, -normal.Z).NormalizedOr(Vec3.Zero) * 0.01f;
            float top = s.SurfaceHeightAt(contact.X + inward.X, contact.Z + inward.Z);
            if (float.IsNaN(top) || Math.Abs(top - contact.Y) > 0.02f)
            {
                return false;
            }

            surfaceNormal = s.IsRamp ? s.Ramp.SurfaceNormal : Vec3.Up;
            return surfaceNormal.Y >= minNormalY;
        }

        /// <summary>True when no sight-blocking solid crosses the segment (solids only; see DuelWorld for steam).</summary>
        public bool HasLineOfSight(Vec3 from, Vec3 to) => !SegmentCast(from, to, SolidMask.Sight, out _);

        private int FirstContaining(Vec3 p, SolidMask mask)
        {
            for (int i = 0; i < _solids.Count; i++)
            {
                CollisionSolid s = _solids[i];
                if (s.Matches(mask) && s.Bounds.Contains(p) && s.ContainsPoint(p))
                {
                    return s.Id;
                }
            }

            return -1;
        }

        // ----- standable points (recall) ---------------------------------------------------------------------

        /// <summary>
        /// Nearest point where a standing capsule fits on top of a static walkable solid (spec L175: an outside-map
        /// fall returns the player to the nearest valid platform). Candidate points are the closest footprint points
        /// (inset by the capsule radius) to <paramref name="near"/>; <paramref name="legal"/> rejects points outside
        /// the legal boundary, optionally after pulling them inward. Ties keep the earlier solid.
        /// </summary>
        public bool TryFindNearestStandablePoint(Vec3 near, float radius, float height, IPositionConstraint legal, out Vec3 feet)
        {
            feet = default;
            float best = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < _solids.Count; i++)
            {
                CollisionSolid s = _solids[i];
                if (!s.Active || s.Dynamic || !s.Walkable)
                {
                    continue;
                }

                if (!TryStandOn(s, near, radius, height, legal, out Vec3 candidate))
                {
                    continue;
                }

                float d = Vec3.DistanceSquared(candidate, near);
                if (d < best)
                {
                    best = d;
                    feet = candidate;
                    found = true;
                }
            }

            return found;
        }

        private bool TryStandOn(CollisionSolid s, Vec3 near, float radius, float height, IPositionConstraint legal, out Vec3 feet)
        {
            feet = default;
            if (!TopPoint(s, near, radius, out feet))
            {
                return false;
            }

            if (legal != null && !legal.Contains(feet))
            {
                Vec3 pulled = legal.ClampInside(feet, radius);
                if (!TopPoint(s, pulled, radius, out feet) || !legal.Contains(feet))
                {
                    return false;
                }
            }

            if (s.IsRamp && s.Ramp.SlopeDeg > SimRules.Motor.MaxWalkableSlopeDeg)
            {
                return false;
            }

            Capsule body = Capsule.Vertical(feet + new Vec3(0f, 0.01f, 0f), height, radius);
            return IsCapsuleClear(body, 1e-3f);
        }

        // Closest point of the solid's top surface footprint to near (inset by radius where the footprint allows).
        private static bool TopPoint(CollisionSolid s, Vec3 near, float radius, out Vec3 feet)
        {
            YawBox box = s.Box;
            Vec3 local = box.ToLocal(near);
            float ix = Math.Max(0f, box.HalfExtents.X - radius);
            float iz = Math.Max(0f, box.HalfExtents.Z - radius);
            local.X = CMath.Clamp(local.X, -ix, ix);
            local.Z = CMath.Clamp(local.Z, -iz, iz);
            local.Y = 0f;
            Vec3 world = box.ToWorld(local);
            float y = s.SurfaceHeightAt(world.X, world.Z);
            if (float.IsNaN(y))
            {
                feet = default;
                return false;
            }

            feet = new Vec3(world.X, y, world.Z);
            return true;
        }

        // ----- hashing ---------------------------------------------------------------------------------------

        public void Hash(ref StateHasher h)
        {
            h.Add(_solids.Count);
            for (int i = 0; i < _solids.Count; i++)
            {
                CollisionSolid s = _solids[i];
                if (!s.Dynamic && !s.Destructible && s.Active)
                {
                    continue; // immutable terrain is covered by the layout seed
                }

                h.Add(s.Id);
                h.Add(s.Active);
                h.Add(s.Durability);
            }
        }

        private void Add(CollisionSolid solid)
        {
            _solids.Add(solid);
            _byId.Add(solid.Id, solid);
            Version++;
        }

        private float ComputeKillPlane()
        {
            float lowestWalkable = float.PositiveInfinity;
            float lowestBottom = float.PositiveInfinity;
            for (int i = 0; i < _solids.Count; i++)
            {
                CollisionSolid s = _solids[i];
                lowestBottom = Math.Min(lowestBottom, s.Box.Bottom);
                if (s.Walkable)
                {
                    float top = s.IsRamp ? s.Ramp.BaseHeight : s.Box.Top;
                    lowestWalkable = Math.Min(lowestWalkable, top);
                }
            }

            float reference = !float.IsPositiveInfinity(lowestWalkable) ? lowestWalkable
                : !float.IsPositiveInfinity(lowestBottom) ? lowestBottom : 0f;
            return reference - SimRules.Motor.KillPlaneDepth;
        }
    }

    /// <summary>A legal-position test with an inward clamp (the duel boundary at the current time).</summary>
    public interface IPositionConstraint
    {
        bool Contains(Vec3 point);

        /// <summary>The closest legal point toward the arena center, at least margin inside.</summary>
        Vec3 ClampInside(Vec3 point, float margin);
    }
}
