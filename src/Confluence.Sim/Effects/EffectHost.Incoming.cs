using System;
using Confluence.Sim.Combat;
using Confluence.Sim.Status;

namespace Confluence.Sim.Effects
{
    /// <summary>The player's hooks that adjust what the player receives (IncomingHooks.cs), in attach order.</summary>
    public sealed partial class EffectHost
    {
        internal void AdjustIncomingHooks(DamageRequest hit, ref float reduction, ref float increase)
        {
            for (int i = 0; i < _hooks.Count; i++)
            {
                if (_hooks[i] is IIncomingHitHook incoming)
                {
                    float r = 0f;
                    float inc = 0f;
                    incoming.AdjustIncoming(hit, ref r, ref inc);
                    reduction += Math.Max(0f, r);
                    increase += Math.Max(0f, inc);
                }
            }
        }

        internal void AdjustIncomingStatusHooks(ref StatusApplication application)
        {
            for (int i = 0; i < _hooks.Count; i++)
            {
                if (_hooks[i] is IIncomingStatusHook incoming)
                {
                    incoming.AdjustIncomingStatus(ref application);
                }
            }

            if (application.DurationTicks < 0)
            {
                application.DurationTicks = 0;
            }
        }

        internal float AdjustIncomingKnockbackHooks(int sourcePlayer, float distance, string sourceId)
        {
            for (int i = 0; i < _hooks.Count; i++)
            {
                if (_hooks[i] is IIncomingKnockbackHook incoming)
                {
                    distance = Math.Max(0f, incoming.AdjustIncomingKnockback(sourcePlayer, distance, sourceId));
                }
            }

            return distance;
        }

        internal float AdjustCooldownHooks(string spellId, float seconds)
        {
            for (int i = 0; i < _hooks.Count; i++)
            {
                if (_hooks[i] is ICastCooldownHook cooldown)
                {
                    seconds = Math.Max(seconds, cooldown.AdjustCooldownSec(spellId, seconds)); // hooks only lengthen (L124)
                }
            }

            return seconds;
        }
    }
}
