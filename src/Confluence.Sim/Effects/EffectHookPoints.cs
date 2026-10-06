using System;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Status;

namespace Confluence.Sim.Effects
{
    // Additional adjustment points for content hooks (equipment stage). An EffectHook opts into a point by implementing its
    // interface; the effect system asks every hook of the affected player at the exact moment the rule computes the value.
    // The same rules as EffectHooks.cs apply: the adjust methods are pure functions of the hook's recorded state and the
    // arguments, and the global caps still apply where the rule computes the value.

    /// <summary>
    /// The player's own side of a hit before the damage pipeline (spec L112): a record that reduces or increases damage the
    /// owner takes from some hits ("burning surface damage to wearer decreases 10%", "Water direct hits deal 5% more damage
    /// to wearer"). Called for every hit that does not bypass mitigation, after the attacker's outgoing modifiers.
    /// </summary>
    public interface IIncomingHitHook
    {
        /// <summary>
        /// Adds the owner's conditional passive reduction (positive, within the 45% total cap) and vulnerability (positive,
        /// "deals 5% more damage") against this hit.
        /// </summary>
        void AdjustIncoming(DamageRequest hit, ref float reduction, ref float vulnerability);
    }

    /// <summary>The distance an enemy's knockback or pull moves the owner (spec L173, before the 4 m cap).</summary>
    public interface IIncomingDisplacementHook
    {
        /// <summary>Fraction of the distance removed (0.15 = "15% less distance").</summary>
        float DisplacementReduction(int sourcePlayer, string sourceId);
    }

    /// <summary>A status about to be applied to the owner (spec L116-L118): its duration may be shortened or extended.</summary>
    public interface IIncomingStatusHook
    {
        /// <summary>Changes the requested duration in ticks; the status rules' maxima still apply afterwards.</summary>
        void AdjustIncomingStatus(in StatusApplication application, ref int durationTicks);
    }

    /// <summary>Cooldown reduction for one of the owner's spells; it joins the 25% permanent reduction cap (spec L124).</summary>
    public interface ISpellCooldownHook
    {
        float CooldownReduction(string spellId);
    }

    /// <summary>
    /// Actions the owner cannot start while an equipment use runs (spec L575 consumable use times: "Cannot cast during the
    /// 0.3 s use", "cannot attack during use").
    /// </summary>
    public interface IActionLockHook
    {
        /// <summary>New casts are refused (<see cref="CastBlockReason.ItemUse"/>).</summary>
        bool LocksCasting { get; }

        /// <summary>New common melee strikes are refused.</summary>
        bool LocksAttacks { get; }
    }

    /// <summary>The call sites of the hook points above (see EffectHookPoints.cs).</summary>
    public sealed partial class EffectSystem
    {
        /// <summary>
        /// The target's incoming adjustments of a hit before the pipeline: reductions join the hit's conditional passive
        /// reduction (45% total cap, spec L112), vulnerabilities make it negative (more damage). Mitigation-bypassing damage
        /// (boundary, recall) is never adjusted (spec L51, L175).
        /// </summary>
        public void AdjustIncoming(DamageRequest request)
        {
            if (request == null || request.BypassesMitigation || request.Target < 0 || request.Target >= DuelWorld.PlayerCount)
            {
                return;
            }

            float reduction = 0f;
            float vulnerability = 0f;
            var hooks = _hosts[request.Target].Hooks;
            for (int i = 0; i < hooks.Count; i++)
            {
                if (hooks[i] is IIncomingHitHook incoming)
                {
                    float r = 0f;
                    float v = 0f;
                    incoming.AdjustIncoming(request, ref r, ref v);
                    reduction += Math.Max(0f, r);
                    vulnerability += Math.Max(0f, v);
                }
            }

            if (reduction == 0f && vulnerability == 0f)
            {
                return;
            }

            float keep = (1f - Math.Min(1f, request.ExtraPassiveReduction)) * (1f - Math.Min(1f, reduction)) * (1f + vulnerability);
            request.ExtraPassiveReduction = 1f - keep;
        }

        /// <summary>A hostile displacement's distance after the target's hooks (spec L173; the 4 m cap applies after).</summary>
        public float AdjustIncomingDisplacement(int target, int sourcePlayer, string sourceId, float distance)
        {
            if (target < 0 || target >= DuelWorld.PlayerCount || sourcePlayer == target)
            {
                return distance;
            }

            float keep = 1f;
            var hooks = _hosts[target].Hooks;
            for (int i = 0; i < hooks.Count; i++)
            {
                if (hooks[i] is IIncomingDisplacementHook d)
                {
                    keep *= 1f - Math.Max(0f, Math.Min(1f, d.DisplacementReduction(sourcePlayer, sourceId)));
                }
            }

            return distance * keep;
        }

        /// <summary>A status application's duration after the target's hooks (the status maxima apply afterwards).</summary>
        public StatusApplication AdjustIncomingStatus(int target, in StatusApplication application)
        {
            if (target < 0 || target >= DuelWorld.PlayerCount)
            {
                return application;
            }

            int duration = application.DurationTicks;
            var hooks = _hosts[target].Hooks;
            for (int i = 0; i < hooks.Count; i++)
            {
                if (hooks[i] is IIncomingStatusHook s)
                {
                    s.AdjustIncomingStatus(application, ref duration);
                }
            }

            if (duration == application.DurationTicks)
            {
                return application;
            }

            StatusApplication adjusted = application;
            adjusted.DurationTicks = Math.Max(1, duration);
            return adjusted;
        }

        /// <summary>The player's spell-specific cooldown reduction from hooks (joins the stat's 25% cap, spec L124).</summary>
        public float SpellCooldownReduction(int player, string spellId)
        {
            if (player < 0 || player >= DuelWorld.PlayerCount)
            {
                return 0f;
            }

            float sum = 0f;
            var hooks = _hosts[player].Hooks;
            for (int i = 0; i < hooks.Count; i++)
            {
                if (hooks[i] is ISpellCooldownHook c)
                {
                    sum += Math.Max(0f, c.CooldownReduction(spellId));
                }
            }

            return sum;
        }

        /// <summary>An equipment use of the player refuses new casts.</summary>
        public bool LocksCasting(int player) => Lock(player, true);

        /// <summary>An equipment use of the player refuses new common melee strikes.</summary>
        public bool LocksAttacks(int player) => Lock(player, false);

        private bool Lock(int player, bool casting)
        {
            if (player < 0 || player >= DuelWorld.PlayerCount)
            {
                return false;
            }

            var hooks = _hosts[player].Hooks;
            for (int i = 0; i < hooks.Count; i++)
            {
                if (hooks[i] is IActionLockHook l && (casting ? l.LocksCasting : l.LocksAttacks))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
