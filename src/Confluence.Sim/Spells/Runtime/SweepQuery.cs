using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Sim.Duel;
using Confluence.Sim.World;

namespace Confluence.Sim.Spells
{
    /// <summary>The first thing a swept delivery touches.</summary>
    public struct SweepContact
    {
        public bool Found;

        /// <summary>Distance travelled along the sweep to the contact.</summary>
        public float Distance;

        /// <summary>Struck player, or -1.</summary>
        public int Player;

        /// <summary>Struck spell body, or null.</summary>
        public ISpellBody Body;

        /// <summary>Struck solid id, or -1.</summary>
        public int SolidId;

        /// <summary>Center of the swept volume at contact.</summary>
        public Vec3 Point;

        /// <summary>Surface normal at a solid contact (zero for bodies).</summary>
        public Vec3 Normal;

        public bool IsSolid => Found && SolidId >= 0;
        public bool IsPlayer => Found && Player >= 0;
        public bool IsBody => Found && Body != null;
    }

    /// <summary>
    /// Swept tests shared by projectiles, rays, tethers, and dashes: the earliest contact among solids, players, and
    /// hostile spell bodies along a segment. Players are tested with the present hit volume, or rewound for hitscan
    /// (spec L423); solids always as they are now.
    /// </summary>
    public static class SweepQuery
    {
        /// <param name="solidRadius">Radius tested against solids (0 = the center line; rays stop where their line meets a solid).</param>
        /// <param name="bodyRadius">Radius tested against players and bodies (projectile radius or ray half width).</param>
        /// <param name="ignored">Players that cannot be struck (the caster, or targets already hit by a piercing delivery).</param>
        public static SweepContact First(SpellContext ctx, int attacker, Vec3 start, Vec3 unitDir, float length, float solidRadius, float bodyRadius,
            bool lagCompensated, bool hitBodies, ICollection<int> ignored = null)
        {
            var best = new SweepContact { Player = -1, SolidId = -1, Distance = float.PositiveInfinity };
            if (!(length > 0f)) length = 0f;

            Vec3 end = start + unitDir * length;
            bool solid;
            WorldHit wall;
            if (solidRadius > 0f)
            {
                solid = ctx.Collision.SphereSweep(start, end, solidRadius, SolidMask.Movement, out wall);
            }
            else
            {
                solid = ctx.Collision.Raycast(start, unitDir, length, SolidMask.Movement, out wall);
            }

            if (solid)
            {
                best.Found = true;
                best.Distance = wall.Distance;
                best.SolidId = wall.SolidId;
                best.Point = solidRadius > 0f ? wall.Point : start + unitDir * wall.Distance;
                best.Normal = wall.Normal;
            }

            for (int i = 0; i < DuelWorld.PlayerCount; i++)
            {
                if (i == attacker || !ctx.IsTargetable(i) || (ignored != null && ignored.Contains(i))) continue;
                Capsule body = ctx.TargetVolume(attacker, i, lagCompensated);
                if (Intersections.RayCapsule(start, unitDir, body.A, body.B, body.Radius + bodyRadius, length, out float t) && t < best.Distance)
                {
                    best = new SweepContact { Found = true, Distance = t, Player = i, SolidId = -1, Point = start + unitDir * t };
                }
            }

            if (hitBodies)
            {
                IReadOnlyList<ISpellBody> bodies = ctx.HostileBodies(attacker);
                for (int i = 0; i < bodies.Count; i++)
                {
                    ISpellBody b = bodies[i];
                    if (Intersections.RaySphere(start, unitDir, b.BodyCenter, b.BodyRadius + bodyRadius, length, out float t) && t < best.Distance)
                    {
                        best = new SweepContact { Found = true, Distance = t, Player = -1, SolidId = -1, Body = b, Point = start + unitDir * t };
                    }
                }
            }

            if (!best.Found) best.Distance = length;
            return best;
        }
    }
}
