using System;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Status;

namespace Confluence.Sim.Effects
{
    /// <summary>
    /// The call sites of the hooks that adjust what a player receives (IncomingHooks.cs): the world asks here at the exact
    /// point the rule computes the value (damage before the pipeline, a status before it is applied, a knockback before it
    /// displaces, a cooldown when a commitment starts it).
    /// </summary>
    public sealed partial class EffectSystem
    {
        /// <summary>
        /// The target's incoming adjustments of an original spell or melee hit, before the pipeline (after the attacker's
        /// outgoing modifiers): reductions join the hit's conditional passive reduction (45% total cap, spec L112),
        /// increases scale its parts. Secondary, environment, prop and rule damage are never adjusted.
        /// </summary>
        public void ModifyIncoming(DamageRequest request)
        {
            if (request == null || request.Generation != 0 || request.Parts == null || request.Parts.Length == 0
                || request.Target < 0 || request.Target >= DuelWorld.PlayerCount || request.BypassesMitigation)
            {
                return;
            }

            if (request.Kind != DamageKind.Direct && request.Kind != DamageKind.Splash && request.Kind != DamageKind.Melee)
            {
                return;
            }

            float reduction = 0f;
            float increase = 0f;
            _hosts[request.Target].AdjustIncomingHooks(request, ref reduction, ref increase);
            if (reduction > 0f)
            {
                request.ExtraPassiveReduction = Math.Min(1f, Math.Max(0f, request.ExtraPassiveReduction) + reduction);
            }

            if (increase > 0f)
            {
                var parts = (DamagePart[])request.Parts.Clone();
                for (int i = 0; i < parts.Length; i++)
                {
                    parts[i].Amount = Math.Max(0f, parts[i].Amount * (1f + increase));
                }

                request.Parts = parts;
            }
        }

        /// <summary>A status about to be applied to <paramref name="target"/> after the target's hooks (durations only).</summary>
        public StatusApplication AdjustIncomingStatus(int target, in StatusApplication application)
        {
            StatusApplication adjusted = application;
            if (target >= 0 && target < DuelWorld.PlayerCount)
            {
                _hosts[target].AdjustIncomingStatusHooks(ref adjusted);
            }

            return adjusted;
        }

        /// <summary>A hostile knockback distance on <paramref name="target"/> after the target's hooks.</summary>
        public float AdjustIncomingKnockback(int target, int sourcePlayer, float distance, string sourceId)
        {
            if (target < 0 || target >= DuelWorld.PlayerCount || sourcePlayer == target)
            {
                return distance;
            }

            return _hosts[target].AdjustIncomingKnockbackHooks(sourcePlayer, distance, sourceId);
        }

        /// <summary>The recharge (seconds) a commitment of <paramref name="player"/> starts for a spell, after its hooks.</summary>
        public float AdjustCooldown(int player, string spellId, float seconds)
        {
            if (player < 0 || player >= DuelWorld.PlayerCount || !(seconds > 0f))
            {
                return seconds;
            }

            return _hosts[player].AdjustCooldownHooks(spellId, seconds);
        }
    }
}
