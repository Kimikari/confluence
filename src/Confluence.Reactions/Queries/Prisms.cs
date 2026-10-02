using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Reactions.Queries
{
    /// <summary>
    /// An anchored prism with a fixed, previewed face (spec L157, BIO-003, PROP-019 "Reflects one eligible ray along
    /// its fixed visible normal").
    /// </summary>
    public sealed class PrismDef
    {
        public int Id { get; set; }
        public YawBox Box { get; set; }

        /// <summary>Unit outward normal of the reflecting face; the reflected ray leaves along it.</summary>
        public Vec3 FaceNormal { get; set; }
    }

    public enum PrismResult
    {
        /// <summary>No prism along the ray.</summary>
        None,

        /// <summary>The ray struck a prism but cannot reflect (not its face, or an already reflected ray).</summary>
        Blocked,

        /// <summary>The ray reflects once along the prism's fixed normal at 70 percent damage.</summary>
        Reflected,
    }

    public struct PrismHit
    {
        public PrismResult Result;
        public int PrismId;
        public Vec3 Point;
        public float Distance;
        public Vec3 OutDirection;

        /// <summary>Damage retained by the reflected ray (spec L157, L527: 70 percent).</summary>
        public float RetainedFraction;
    }

    /// <summary>Prism reflection query: one reflection, 70 percent retained damage, never a reflected ray (L157, L527).</summary>
    public sealed class PrismSet
    {
        // Minimum alignment between the struck side and the prism face for the hit to count as "on the face".
        private const float FaceAlignment = 0.7f;

        private readonly List<PrismDef> _prisms = new List<PrismDef>();

        public IReadOnlyList<PrismDef> Prisms => _prisms;

        public void Register(PrismDef prism)
        {
            Remove(prism.Id);
            _prisms.Add(prism);
            _prisms.Sort((a, b) => a.Id.CompareTo(b.Id));
        }

        public bool Remove(int id) => _prisms.RemoveAll(p => p.Id == id) > 0;

        /// <summary>
        /// Traces a ray (unit direction) against the prisms. A ray striking a prism's face reflects along its fixed
        /// normal; a reflected ray cannot reflect again (PROP-019 "Cannot reflect a reflected ray").
        /// </summary>
        public PrismHit Trace(Vec3 origin, Vec3 direction, float maxDistance, bool alreadyReflected)
        {
            var result = new PrismHit { Result = PrismResult.None, PrismId = -1 };
            float best = maxDistance;
            PrismDef hitPrism = null;
            RayHit bestHit = default;
            var ray = new Ray(origin, direction);
            for (int i = 0; i < _prisms.Count; i++)
            {
                if (_prisms[i].Box.Raycast(ray, best, out RayHit hit) && hit.Distance <= best)
                {
                    best = hit.Distance;
                    bestHit = hit;
                    hitPrism = _prisms[i];
                }
            }

            if (hitPrism == null)
            {
                return result;
            }

            result.PrismId = hitPrism.Id;
            result.Point = bestHit.Point;
            result.Distance = bestHit.Distance;
            Vec3 face = hitPrism.FaceNormal.NormalizedOr(Vec3.Forward);
            bool onFace = Vec3.Dot(bestHit.Normal, face) >= FaceAlignment && Vec3.Dot(direction, face) < 0f;
            if (alreadyReflected || !onFace)
            {
                result.Result = PrismResult.Blocked;
                return result;
            }

            result.Result = PrismResult.Reflected;
            result.OutDirection = face;
            result.RetainedFraction = GlobalRules.World.PrismRetainedDamageFraction;
            return result;
        }
    }
}
