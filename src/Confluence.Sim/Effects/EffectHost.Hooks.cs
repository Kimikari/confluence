using System;
using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Core.Rules;
using Confluence.Sim.Combat;
using Confluence.Sim.Hashing;
using Confluence.Sim.Movement;

namespace Confluence.Sim.Effects
{
    /// <summary>The player's attached <see cref="EffectHook"/>s (see EffectHooks.cs), in attach order.</summary>
    public sealed partial class EffectHost
    {
        private readonly List<EffectHook> _hooks = new List<EffectHook>();

        public IReadOnlyList<EffectHook> Hooks => _hooks;

        private void AttachHook(EffectHook hook)
        {
            if (hook.Host != null)
            {
                throw new InvalidOperationException(hook + " is already attached.");
            }

            hook.Host = this;
            hook.Key = hook.SourceId + "#" + (++_attachSerial);
            _hooks.Add(hook);
            World.AddEventObserver(hook);
        }

        internal void TickHooks()
        {
            for (int i = 0; i < _hooks.Count; i++)
            {
                _hooks[i].OnTick();
            }
        }

        internal void AdjustMotorHooks(ref MotorCommand command)
        {
            for (int i = 0; i < _hooks.Count; i++)
            {
                _hooks[i].AdjustMotor(ref command);
            }
        }

        internal MeleeProfile AdjustStrikeHooks(MeleeProfile profile)
        {
            for (int i = 0; i < _hooks.Count; i++)
            {
                profile = _hooks[i].AdjustStrike(profile);
            }

            return profile;
        }

        internal void AdjustCastPriceHooks(in CastQuote quote, ref float reduction, ref float increase)
        {
            for (int i = 0; i < _hooks.Count; i++)
            {
                float r = 0f;
                float inc = 0f;
                _hooks[i].AdjustCastPrice(quote, ref r, ref inc);
                reduction += Math.Max(0f, r);
                increase += Math.Max(0f, inc);
            }
        }

        internal void AdjustCastTimingHooks(in CastQuote quote, ref CastTimingAdjust timing)
        {
            for (int i = 0; i < _hooks.Count; i++)
            {
                _hooks[i].AdjustCastTiming(quote, ref timing);
            }
        }

        internal void AdjustOutgoingHooks(DamageRequest hit, float range, ref float fraction, ref float flat)
        {
            for (int i = 0; i < _hooks.Count; i++)
            {
                _hooks[i].AdjustOutgoing(hit, range, ref fraction, ref flat);
            }
        }

        internal float AdjustSolidDamageHooks(int solidId, float amount, string sourceId, CastEventId castId)
        {
            for (int i = 0; i < _hooks.Count; i++)
            {
                amount = Math.Max(0f, _hooks[i].AdjustSolidDamage(solidId, amount, sourceId, castId));
            }

            return amount;
        }

        private void HashHooks(ref StateHasher h)
        {
            h.Add(_hooks.Count);
            for (int i = 0; i < _hooks.Count; i++)
            {
                h.Add(_hooks[i].Key);
                _hooks[i].Hash(ref h);
            }
        }
    }
}
