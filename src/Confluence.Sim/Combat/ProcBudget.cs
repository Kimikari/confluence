using System;
using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Hashing;

namespace Confluence.Sim.Combat
{
    /// <summary>
    /// Trigger budgets (spec L126): an effect may trigger once per 0.5 s and once per original target-hit event,
    /// whichever is less frequent. A beam's ticks share one origin event, so they never cascade independently.
    /// Secondary/proc damage and rule damage cannot trigger anything (one generation of secondary effects).
    /// Keys are (owner, effect id); lookups only, so no outcome depends on dictionary order.
    /// </summary>
    public sealed class ProcBudget
    {
        private readonly Dictionary<string, Budget> _budgets = new Dictionary<string, Budget>(StringComparer.Ordinal);

        public static int IntervalTicks => Ticks.FromSeconds(GlobalRules.Triggers.ProcInternalCooldownSec);

        /// <summary>
        /// Tries to trigger an effect owned by owner for an original hit event against target. Returns true and
        /// records the trigger when both budgets allow it.
        /// </summary>
        public bool TryTrigger(int owner, string effectId, int originEventId, int target, int now)
        {
            if (effectId == null)
            {
                throw new ArgumentNullException(nameof(effectId));
            }

            string key = owner + "|" + effectId;
            if (!_budgets.TryGetValue(key, out Budget b))
            {
                b = new Budget();
                _budgets.Add(key, b);
            }

            long hitKey = ((long)originEventId << 8) | (long)(target & 0xFF);
            if (now - b.LastTick < IntervalTicks || b.UsedHits.Contains(hitKey))
            {
                return false;
            }

            b.LastTick = now;
            b.UsedHits.Add(hitKey);
            b.TriggerCount++;
            return true;
        }

        /// <summary>Gate for a damage event: only original spell, splash, and melee hits may trigger effects (spec L126, L672).</summary>
        public bool TryTrigger(int owner, string effectId, DamageRequest hit, int originEventId, int now)
        {
            if (hit == null || !hit.CanTriggerEffects)
            {
                return false;
            }

            return TryTrigger(owner, effectId, originEventId, hit.Target, now);
        }

        public int TriggerCount(int owner, string effectId)
        {
            return _budgets.TryGetValue(owner + "|" + effectId, out Budget b) ? b.TriggerCount : 0;
        }

        public void Hash(ref StateHasher h)
        {
            // Keys in ordinal order, so dictionary enumeration order never reaches the hash.
            var keys = new List<string>(_budgets.Keys);
            keys.Sort(StringComparer.Ordinal);
            h.Add(keys.Count);
            for (int i = 0; i < keys.Count; i++)
            {
                Budget b = _budgets[keys[i]];
                h.Add(keys[i]);
                h.Add(b.LastTick);
                h.Add(b.TriggerCount);
                h.Add(b.UsedHits.Count);
            }
        }

        private sealed class Budget
        {
            public int LastTick = int.MinValue / 2;
            public int TriggerCount;
            public readonly HashSet<long> UsedHits = new HashSet<long>();
        }
    }

    /// <summary>A candidate for a chain jump (opponent, summon, prop).</summary>
    public struct ChainCandidate
    {
        public int Id;
        public Vec3 Position;

        /// <summary>The opposing player (never hit twice by one cast).</summary>
        public bool IsOpponent;
    }

    /// <summary>
    /// Chain attack target selection (spec L126): at most three jumps, each at most 6 m, never the same opponent
    /// twice from one cast. Nearest unvisited candidate first; ties keep the lower id.
    /// </summary>
    public static class ChainTargeting
    {
        public static List<ChainCandidate> Select(Vec3 start, IReadOnlyList<ChainCandidate> candidates, int maxJumps = GlobalRules.Triggers.ChainMaxJumps,
            float maxJumpDistance = GlobalRules.Triggers.ChainMaxJumpDistance, IReadOnlyCollection<int> alreadyHit = null)
        {
            maxJumps = Math.Min(maxJumps, GlobalRules.Triggers.ChainMaxJumps);
            maxJumpDistance = Math.Min(maxJumpDistance, GlobalRules.Triggers.ChainMaxJumpDistance);
            var path = new List<ChainCandidate>();
            var visited = new HashSet<int>();
            if (alreadyHit != null)
            {
                foreach (int id in alreadyHit)
                {
                    visited.Add(id);
                }
            }

            Vec3 from = start;
            for (int jump = 0; jump < maxJumps; jump++)
            {
                int best = -1;
                float bestD = float.PositiveInfinity;
                for (int i = 0; i < candidates.Count; i++)
                {
                    ChainCandidate c = candidates[i];
                    if (visited.Contains(c.Id))
                    {
                        continue;
                    }

                    float d = Vec3.Distance(from, c.Position);
                    if (d > maxJumpDistance)
                    {
                        continue;
                    }

                    if (d < bestD || (d == bestD && best >= 0 && c.Id < candidates[best].Id))
                    {
                        bestD = d;
                        best = i;
                    }
                }

                if (best < 0)
                {
                    break;
                }

                ChainCandidate chosen = candidates[best];
                path.Add(chosen);
                visited.Add(chosen.Id);
                from = chosen.Position;
            }

            return path;
        }
    }
}
