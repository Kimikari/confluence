using System;
using System.Collections.Generic;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Reactions.Queries
{
    /// <summary>Result of a sound path trace through solid cover (spec L156).</summary>
    public struct SoundOcclusion
    {
        public int SolidsCrossed;
        public int AnchoredCrossed;

        /// <summary>Total path length inside solids.</summary>
        public float Thickness;

        /// <summary>Hearing loudness factor for presentation (1 = unobstructed). Never affects damage.</summary>
        public float HearingTransmission;

        /// <summary>
        /// An explicit sonic attack keeps its damage only along a clear path; it cannot pass through walls for
        /// full damage (L156). Struck fragile cover can be shaken instead (see <see cref="FirstFragileSolidId"/>).
        /// </summary>
        public bool SonicDamagePasses;

        /// <summary>Nearest crossed destructible solid a sonic attack can shake (SPL-025), or -1.</summary>
        public int FirstFragileSolidId;
    }

    /// <summary>Sound attenuation by solid cover. Steam and other volumes never disable audio (L167).</summary>
    public sealed class SoundOcclusionQuery
    {
        private readonly List<Solid> _solids = new List<Solid>();

        public SoundOcclusionQuery(IEnumerable<Solid> solids)
        {
            if (solids != null)
            {
                foreach (Solid s in solids)
                {
                    if (s != null)
                    {
                        _solids.Add(s);
                    }
                }
            }

            _solids.Sort((a, b) => a.Id.CompareTo(b.Id));
        }

        public void RemoveSolid(int id) => _solids.RemoveAll(s => s.Id == id);

        public void AddSolid(Solid solid)
        {
            RemoveSolid(solid.Id);
            _solids.Add(solid);
            _solids.Sort((a, b) => a.Id.CompareTo(b.Id));
        }

        public SoundOcclusion Trace(Vec3 from, Vec3 to)
        {
            var result = new SoundOcclusion { HearingTransmission = 1f, SonicDamagePasses = true, FirstFragileSolidId = -1 };
            Vec3 delta = to - from;
            float length = delta.Length();
            if (length < CMath.Epsilon)
            {
                return result;
            }

            var forward = new Ray(from, delta);
            var backward = new Ray(to, -delta);
            float nearestFragile = float.PositiveInfinity;
            for (int i = 0; i < _solids.Count; i++)
            {
                Solid solid = _solids[i];
                if (!solid.Box.Raycast(forward, length, out RayHit entry))
                {
                    continue;
                }

                float exitFromEnd = solid.Box.Raycast(backward, length, out RayHit exit) ? exit.Distance : 0f;
                float inside = MathF.Max(0f, length - exitFromEnd - entry.Distance);
                result.SolidsCrossed++;
                result.Thickness += inside;
                if (solid.Anchored || !solid.IsDestructible)
                {
                    result.AnchoredCrossed++;
                }
                else if (entry.Distance < nearestFragile)
                {
                    nearestFragile = entry.Distance;
                    result.FirstFragileSolidId = solid.Id;
                }

                bool absorbent = (solid.Flags & MaterialFlags.Absorbent) != 0;
                result.HearingTransmission *= absorbent ? ReactionRules.HearingTransmissionAbsorbent : ReactionRules.HearingTransmissionPerSolid;
            }

            result.SonicDamagePasses = result.SolidsCrossed == 0;
            return result;
        }
    }
}
