using System;
using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Reactions.Electricity
{
    /// <summary>
    /// A visible conductive object registered by Sim or the arena: a metal prop, rail section, statue, or plate
    /// (spec L149-L150). Its shape is either up to three branch segments or a box; electricity travels along this
    /// one object only, so insulated gaps between rail sections stop it (BIO-006, PROP-003, PROP-006).
    /// </summary>
    public sealed class ConductorDef
    {
        public int Id { get; set; }

        /// <summary>Branch segments of the object. Only the first <see cref="GlobalRules.World.MetalMaxBranches"/> conduct.</summary>
        public List<Segment> Branches { get; set; } = new List<Segment>();

        /// <summary>Box shape, used when no branches are given (plates, crates, statues).</summary>
        public YawBox Box { get; set; }

        /// <summary>Grounded (anchored) conductor: absorbs branches up to its capacity, then overloads (L150).</summary>
        public bool Grounded { get; set; }

        /// <summary>Branches absorbed before overload (L150 "after its stated capacity").</summary>
        public int GroundCapacity { get; set; } = ReactionRules.DefaultGroundCapacity;

        /// <summary>Time ungrounded after an overload (PROP-002, BIO-012: 4 s).</summary>
        public float OverloadSec { get; set; } = ReactionRules.GroundOverloadSec;

        /// <summary>Radius of the conductive contact area around the energized object (L149).</summary>
        public float ContactRadius { get; set; } = ReactionRules.ConductorContactRadius;

        public bool HasBranches => Branches != null && Branches.Count > 0;

        public static ConductorDef Rail(int id, Vec3 a, Vec3 b, bool grounded = false)
        {
            var def = new ConductorDef { Id = id, Grounded = grounded };
            def.Branches.Add(new Segment(a, b));
            return def;
        }

        public static ConductorDef ForBox(int id, YawBox box, bool grounded = false) =>
            new ConductorDef { Id = id, Box = box, Grounded = grounded };

        public ConductorDef Clone()
        {
            var copy = (ConductorDef)MemberwiseClone();
            copy.Branches = Branches == null ? new List<Segment>() : new List<Segment>(Branches);
            return copy;
        }
    }

    /// <summary>Runtime state of a registered conductor.</summary>
    internal sealed class Conductor
    {
        public Conductor(ConductorDef def)
        {
            Def = def;
            RemainingCapacity = Math.Max(0, def.GroundCapacity);
        }

        public ConductorDef Def { get; }
        public int Id => Def.Id;

        public int RemainingCapacity;

        /// <summary>Tick until which the conductor is overloaded and ungrounded (0 when not overloaded).</summary>
        public int OverloadedUntil;

        /// <summary>Sim override of the grounding (a destroyed ground, a statue knocked loose).</summary>
        public bool GroundingEnabled = true;

        public bool CanAbsorb => Def.Grounded && GroundingEnabled && OverloadedUntil == 0 && RemainingCapacity > 0;

        public int BranchCount => Def.HasBranches ? Math.Min(Def.Branches.Count, GlobalRules.World.MetalMaxBranches) : 1;

        /// <summary>Closest point of the object to a world point.</summary>
        public Vec3 ClosestPoint(Vec3 point)
        {
            if (!Def.HasBranches)
            {
                return Def.Box.ClosestPoint(point);
            }

            Vec3 best = Def.Branches[0].ClosestPoint(point);
            float bestSq = Vec3.DistanceSquared(best, point);
            for (int i = 1; i < BranchCount; i++)
            {
                Vec3 c = Def.Branches[i].ClosestPoint(point);
                float d = Vec3.DistanceSquared(c, point);
                if (d < bestSq)
                {
                    bestSq = d;
                    best = c;
                }
            }

            return best;
        }

        public float DistanceTo(Vec3 point) => Vec3.Distance(point, ClosestPoint(point));

        /// <summary>Ground-plane distance from a point to the object's footprint.</summary>
        public float DistanceXZ(Vec2 point)
        {
            if (!Def.HasBranches)
            {
                return Vec2.Distance(point, Def.Box.ClosestPointXZ(point));
            }

            float best = float.PositiveInfinity;
            for (int i = 0; i < BranchCount; i++)
            {
                Segment s = Def.Branches[i];
                Vec2 c = Intersections.ClosestPointOnSegment(point, s.A.XZ(), s.B.XZ());
                best = MathF.Min(best, Vec2.Distance(point, c));
            }

            return best;
        }

        /// <summary>
        /// True when a body is inside the conductive contact area of the part of the object energized from the
        /// contact point: within the contact radius of a branch point at most 6 m from the contact (L149).
        /// </summary>
        public bool TouchesEnergizedPart(Vec3 contact, Vec3 feet, float radius, float height)
        {
            float reach = GlobalRules.World.MetalMaxBranchLength;
            Vec3 top = feet + new Vec3(0f, height, 0f);
            if (!Def.HasBranches)
            {
                Vec3 axis = new Vec3(feet.X, CMath.Clamp(Def.Box.Center.Y, feet.Y, top.Y), feet.Z);
                Vec3 onBox = Def.Box.ClosestPoint(axis);
                return Vec3.Distance(onBox, axis) <= Def.ContactRadius + radius && Vec3.Distance(onBox, contact) <= reach;
            }

            for (int i = 0; i < BranchCount; i++)
            {
                Segment s = Def.Branches[i];
                Intersections.SegmentSegmentClosest(s.A, s.B, feet, top, out _, out _, out Vec3 onBranch, out Vec3 onBody);
                if (Vec3.Distance(onBranch, onBody) <= Def.ContactRadius + radius && Vec3.Distance(onBranch, contact) <= reach)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
